using System.Collections.Generic;

namespace AbyssMod.Services;

public static class AbilityTextMatcher
{
    private static readonly RegexTemplateIndex Index = new();

    public static void Rebuild(Dictionary<string, string> abilityDescriptions) =>
        Index.Rebuild(abilityDescriptions);

    public static bool TryTranslate(string text, out string result) =>
        Index.TryTranslate(text, out result);

    public static bool LooksLikeAbility(string text)
    {
        if (string.IsNullOrEmpty(text) || !HasKana(text))
            return false;

        return text.Contains("ダメージ", System.StringComparison.Ordinal)
            || text.Contains("HIT", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("紋章", System.StringComparison.Ordinal)
            || text.Contains("スキル", System.StringComparison.Ordinal)
            || text.Contains("【効果】", System.StringComparison.Ordinal)
            || text.Contains("【発動条件】", System.StringComparison.Ordinal)
            || text.Contains("フォースチェイン", System.StringComparison.Ordinal);
    }

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
