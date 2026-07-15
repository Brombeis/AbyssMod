using System.Collections.Generic;

namespace AbyssMod.Services;

public static class UiRegexMatcher
{
    private static readonly RegexTemplateIndex Index = new();

    public static void Rebuild(Dictionary<string, string> texts, IReadOnlyDictionary<string, string> keySources = null) => Index.Rebuild(texts, keySources);

    public static bool TryTranslate(string text, out string result) =>
        Index.TryTranslate(text, out result);
}
