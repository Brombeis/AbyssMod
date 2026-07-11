using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AbyssMod.Services;

/// <summary>
/// 技能描述匹配：游戏 UI 显示已代入数值的日文，ability_descriptions 字典 key 为含
/// {[name]} 占位符的模板。为每个模板编译一条正则（字面量转义、占位符→捕获组），
/// 用模板本身定位变量，因此能正确处理百分比、秒数、以及模板里的固定数值——
/// 固定值留在字面量里不会被误当成变量。
/// </summary>
public static class AbilityTextMatcher
{
    private sealed class Template
    {
        public Regex Rx;
        public string Prefix;      // literal text before the first slot (cheap pre-filter)
        public string[] Names;     // slot names in the order they appear in the key
        public string Translation; // EN value, still holding {[name]} slots
        public int Specificity;    // more literal text = tried first
    }

    private static Dictionary<string, string> _exact = new();
    private static Dictionary<string, string> _exactNorm = new();
    private static List<Template> _templates = new();

    private static readonly Regex Slot = new(@"\{\[([^\]]+)\]\}", RegexOptions.Compiled);
    private static readonly Regex ColorTag = new(@"</?color[^>]*>", RegexOptions.Compiled);

    public static void Rebuild(Dictionary<string, string> abilityDescriptions)
    {
        _exact = abilityDescriptions ?? new Dictionary<string, string>();
        _exactNorm = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = new List<Template>();

        foreach (var kv in _exact)
        {
            string norm = Normalize(kv.Key);
            _exactNorm.TryAdd(norm, kv.Value);

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
            catch
            {
                continue; // malformed template → skip rather than crash Rebuild
            }

            list.Add(new Template
            {
                Rx = rx,
                Prefix = norm.Substring(0, slots[0].Index),
                Names = names,
                Translation = kv.Value,
                Specificity = norm.Length - slots.Count * 4,
            });
        }

        // Most-literal templates first: they match fewer strings, so they win ties safely.
        list.Sort((a, b) => b.Specificity.CompareTo(a.Specificity));
        _templates = list;
    }

    public static bool LooksLikeAbility(string text)
    {
        if (string.IsNullOrEmpty(text) || !HasKana(text))
            return false;

        return text.Contains("ダメージ", StringComparison.Ordinal)
            || text.Contains("HIT", StringComparison.OrdinalIgnoreCase)
            || text.Contains("紋章", StringComparison.Ordinal)
            || text.Contains("スキル", StringComparison.Ordinal)
            || text.Contains("【効果】", StringComparison.Ordinal)
            || text.Contains("【発動条件】", StringComparison.Ordinal)
            || text.Contains("フォースチェイン", StringComparison.Ordinal);
    }

    public static bool TryTranslate(string text, out string result)
    {
        result = null;
        if (string.IsNullOrEmpty(text) || _exact.Count == 0)
            return false;

        if (_exact.TryGetValue(text, out result))
            return true;

        string norm = Normalize(text);
        if (_exactNorm.TryGetValue(norm, out result))
            return true;

        foreach (var t in _templates)
        {
            if (t.Prefix.Length > 0 && !norm.StartsWith(t.Prefix, StringComparison.Ordinal))
                continue;

            Match m = t.Rx.Match(norm);
            if (!m.Success)
                continue;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < t.Names.Length; i++)
                values[t.Names[i]] = m.Groups[i + 1].Value;

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

    private static bool HasKana(string s)
    {
        foreach (char c in s)
        {
            if ((c >= '぀' && c <= 'ゟ') || (c >= '゠' && c <= 'ヿ'))
                return true;
        }
        return false;
    }
}
