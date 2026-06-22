using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AbyssMod.Services;

/// <summary>
/// 将游戏内已代入数值的 UI 文本（如「スタミナを500消費」）匹配到
/// 字典中带 {0} 占位符的模板 key（如「スタミナを{0}消費する」）。
/// 算法与 <see cref="MachineTranslator"/> 的数字模板一致。
/// </summary>
public static class TemplateTextMatcher
{
    private static Dictionary<string, string> _exact = new();

    private static readonly Regex TagOrNumber = new(
        @"<[^>]*>|[0-9]+(?:\.[0-9]+)?",
        RegexOptions.Compiled
    );
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    public static void Rebuild(params Dictionary<string, string>[] sources)
    {
        var merged = new Dictionary<string, string>();
        if (sources != null)
        {
            foreach (var source in sources)
            {
                if (source == null)
                    continue;
                foreach (var kv in source)
                    merged[kv.Key] = kv.Value;
            }
        }
        _exact = merged;
    }

    public static bool TryTranslate(string text, out string result)
    {
        result = null;
        if (string.IsNullOrEmpty(text) || _exact.Count == 0)
            return false;

        if (_exact.TryGetValue(text, out result))
            return true;

        var (template, numbers) = Normalize(text);
        if (template == text || !_exact.TryGetValue(template, out var translated))
            return false;

        result = Fill(translated, numbers);
        return !string.IsNullOrEmpty(result);
    }

    private static (string template, string[] numbers) Normalize(string text)
    {
        var nums = new List<string>();
        int i = 0;
        var template = TagOrNumber.Replace(text, m =>
        {
            if (m.Value.Length > 0 && m.Value[0] == '<')
                return m.Value;
            nums.Add(m.Value);
            return "{" + (i++) + "}";
        });
        return (template, nums.ToArray());
    }

    private static string Fill(string template, string[] numbers)
    {
        if (numbers.Length == 0)
            return template;

        bool ok = true;
        var result = Placeholder.Replace(template, m =>
        {
            int idx = int.Parse(m.Groups[1].Value);
            if (idx < 0 || idx >= numbers.Length)
            {
                ok = false;
                return m.Value;
            }
            return numbers[idx];
        });
        return ok ? result : null;
    }
}
