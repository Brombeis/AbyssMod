using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AbyssMod.Services;

public sealed class RegexTemplateIndex
{
    private sealed class Template
    {
        public Regex Rx;
        public string Prefix;      // literal text before the first slot (cheap pre-filter); empty for raw regex
        public string[] Names;     // slot names
        public bool RawRegex;      // true => Names looked up by group name, not by position
        public string Translation; // EN value, still holding {[name]} slots
        public int Specificity;    // higher = tried first
    }

    private const string RawPrefix = "re:";

    private Dictionary<string, string> _exact = new();
    // norm → (originalKey, translatedValue)
    private Dictionary<string, (string OrigKey, string Value)> _exactNorm = new();
    private List<Template> _templates = new();
    private IReadOnlyDictionary<string, string> _keySources;

    private static readonly Regex Slot = new(@"\{\[([^\]]+)\]\}", RegexOptions.Compiled);
    private static readonly Regex ColorTag = new(@"</?color[^>]*>", RegexOptions.Compiled);

    public void Rebuild(Dictionary<string, string> dict, IReadOnlyDictionary<string, string> keySources = null)
    {
        _exact = dict ?? new Dictionary<string, string>();
        _exactNorm = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        _keySources = keySources;
        var list = new List<Template>();

        foreach (var kv in _exact)
        {
            if (kv.Key.StartsWith(RawPrefix, StringComparison.Ordinal))
            {
                var template = BuildRawRegexTemplate(kv.Key.Substring(RawPrefix.Length), kv.Value);
                if (template != null)
                    list.Add(template);
                continue; // raw-regex entries are patterns, not literal exact-match keys
            }

            string norm = Normalize(kv.Key);
            if (!_exactNorm.TryAdd(norm, (kv.Key, kv.Value)))
            {
                var kept = _exactNorm[norm];
                Logger.Warn(
                    $"RegexTemplateIndex: duplicate normalized key ignored\n"
                    + $"  kept:    {kept.OrigKey} ({SourceOf(kept.OrigKey)})\n"
                    + $"           => {kept.Value}\n"
                    + $"  ignored: {kv.Key} ({SourceOf(kv.Key)})\n"
                    + $"           => {kv.Value}"
                );
            }

            var slots = Slot.Matches(norm);
            if (slots.Count == 0)
                continue; // no variables → exact dictionaries handle it

            var names = new string[slots.Count];
            var sb = new StringBuilder("^");
            int last = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                Match m = slots[i];
                sb.Append(Regex.Escape(norm.Substring(last, m.Index - last)));
                sb.Append("(.+?)");
                names[i] = m.Groups[1].Value;
                last = m.Index + m.Length;
            }
            sb.Append(Regex.Escape(norm.Substring(last)));
            sb.Append('$');

            Regex rx;
            try
            {
                rx = new Regex(sb.ToString(), RegexOptions.Singleline);
            }
            catch (Exception ex)
            {
                Logger.Warn($"RegexTemplateIndex: malformed template skipped: {kv.Key} ({ex.Message})");
                continue; // malformed template → skip rather than crash Rebuild
            }

            list.Add(new Template
            {
                Rx = rx,
                Prefix = norm.Substring(0, slots[0].Index),
                Names = names,
                RawRegex = false,
                Translation = kv.Value,
                Specificity = norm.Length - slots.Count * 4,
            });
        }

        // Most-literal templates first: they match fewer strings, so they win ties safely.
        list.Sort((a, b) => b.Specificity.CompareTo(a.Specificity));
        _templates = list;
    }

    private string SourceOf(string key)
    {
        if (_keySources == null || !_keySources.TryGetValue(key, out var filePath))
            return "unknown";
        int line = FindLineInFile(filePath, key);
        return line >= 0 ? $"{filePath}:{line}" : filePath;
    }

    private static int FindLineInFile(string filePath, string key)
    {
        if (!File.Exists(filePath))
            return -1;
        // Serialize the key so special chars are properly escaped for JSON matching
        string jsonKey = JsonSerializer.Serialize(key);
        int lineNum = 1;
        foreach (var line in File.ReadLines(filePath))
        {
            if (line.Contains(jsonKey, StringComparison.Ordinal))
                return lineNum;
            lineNum++;
        }
        return -1;
    }

    private static Template BuildRawRegexTemplate(string pattern, string translation)
    {
        Regex rx;
        try
        {
            rx = new Regex(pattern, RegexOptions.Singleline);
        }
        catch
        {
            Logger.Warn($"RegexTemplateIndex: malformed re: pattern skipped: {pattern}");
            return null;
        }

        // Named groups only — positional groups have no {[name]} counterpart to fill.
        var names = rx.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToArray();

        return new Template
        {
            Rx = rx,
            Prefix = "",
            Names = names,
            RawRegex = true,
            Translation = translation,
            Specificity = pattern.Length, // rough heuristic; raw regex authors control ordering by pattern specificity themselves
        };
    }

    public bool TryTranslate(string text, out string result)
    {
        result = null;
        if (string.IsNullOrEmpty(text) || (_exact.Count == 0 && _templates.Count == 0))
            return false;

        if (_exact.TryGetValue(text, out result))
            return true;

        string norm = Normalize(text);
        if (_exactNorm.TryGetValue(norm, out var entry))
        {
            result = entry.Value;
            return true;
        }

        foreach (var t in _templates)
        {
            if (t.Prefix.Length > 0 && !norm.StartsWith(t.Prefix, StringComparison.Ordinal))
                continue;

            Match m = t.Rx.Match(norm);
            if (!m.Success)
                continue;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (t.RawRegex)
            {
                foreach (var name in t.Names)
                    values[name] = m.Groups[name].Value;
            }
            else
            {
                for (int i = 0; i < t.Names.Length; i++)
                    values[t.Names[i]] = m.Groups[i + 1].Value;
            }

            result = Slot.Replace(
                t.Translation,
                mm => values.TryGetValue(mm.Groups[1].Value, out var v) ? v : mm.Value
            );
            // A captured value may already carry its '%', while the EN template also
            // has a literal '%' right after the slot → collapse the doubled sign.
            result = result.Replace("%%", "%").Replace("％％", "％");
            return !string.IsNullOrEmpty(result);
        }

        return false;
    }

    private static string Normalize(string s) => StripColor(NormalizeBrackets(s));

    private static string NormalizeBrackets(string s) =>
        s.Replace('「', '【').Replace('」', '】');

    private static string StripColor(string s) => ColorTag.Replace(s, "");
}
