using AbyssMod.Services;

namespace AbyssMod.Patches;

/// <summary>
/// 通用文本处理：翻译命中则替换，未命中且为日文则按类别收集。
/// 供各补丁共用。
/// </summary>
public static class TextTranslator
{
    /// <summary>
    /// 处理一段文本：
    ///   - 若开启翻译且字典精确命中 → 返回中文译文
    ///   - 否则若开启收集且文本含日文假名 → 记录到 dump
    /// </summary>
    public static string Process(string category, string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var trans = Plugin.Trans;
        var names = trans?.Names;
        var texts = trans?.Texts;

        if (Config.Translation.Value && trans != null)
        {
            // 角色名优先（以 names 为主）：强化、编队等任何显示角色名的界面
            // 都共用 names 翻译，保证全游戏角色名一致。
            if (names != null && names.TryGetValue(text, out string tName))
                return tName;

            var uiTexts = trans.GetTable(TranslationPaths.UiTexts);
            if (uiTexts != null && uiTexts.TryGetValue(text, out string tUi))
                return tUi;

            bool abilityContext = category == TranslationPaths.AbilityDescriptions
                || LooksLikeAbility(text);

            // 技能页优先走 ability 模糊匹配，避免被 equipment_effect 合并字典抢先或漏翻
            if (abilityContext && AbilityTextMatcher.TryTranslate(text, out string abilityFirst))
                return abilityFirst;

            if (texts != null && texts.TryGetValue(text, out string translated))
                return translated;

            var titles = trans.Titles;
            if (titles != null && titles.TryGetValue(text, out string tTitle))
                return tTitle;

            var descriptions = trans.Descriptions;
            if (descriptions != null && descriptions.TryGetValue(text, out string tDesc))
                return tDesc;

            // 任务/系统 UI 常显示具体数字，字典 key 为 {0} 模板
            if (TemplateTextMatcher.TryTranslate(text, out string templated))
                return templated;
        }

        if (Config.CollectText.Value
            && HasKana(text)
            && (texts == null || !texts.ContainsKey(text))
            && (names == null || !names.ContainsKey(text)))
            TextCollector.Record(category, text);

        return text;
    }

    /// <summary>
    /// 是否含日文假名（平假名或片假名）。用于判断是否为待翻译的日文原文，
    /// 避免收集纯数字、英文或已翻译的中文。
    /// </summary>
    private static bool LooksLikeAbility(string text) => AbilityTextMatcher.LooksLikeAbility(text);

    public static bool HasKana(string s)
    {
        foreach (char c in s)
        {
            if ((c >= '\u3040' && c <= '\u309F') || (c >= '\u30A0' && c <= '\u30FF'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// \u542B\u65E5\u6587\uFF08\u5047\u540D\u6216\u6F22\u5B57\uFF09\u3002RefreshVisibleText \u7528\u5B83\u66FF\u4EE3 HasKana\uFF0C\u5426\u5219\u7EAF\u6F22\u5B57 UI
    /// \u6807\u7B7E\uFF08\u65BD\u8A2D / \u4EA4\u6D41 / \u5A3C\u9928 \u7B49\uFF09\u4F1A\u88AB\u5468\u671F\u626B\u63CF\u6F0F\u6389\uFF0C\u5373\u4F7F\u5B57\u5178\u91CC\u5DF2\u6709\u8BD1\u6587\u3002
    /// </summary>
    public static bool HasJapanese(string s)
    {
        foreach (char c in s)
        {
            if ((c >= '\u3040' && c <= '\u30FF')      // hiragana + katakana
                || (c >= '\u4E00' && c <= '\u9FFF')   // CJK unified ideographs (kanji)
                || (c >= '\uFF66' && c <= '\uFF9F'))  // halfwidth katakana
                return true;
        }
        return false;
    }
}
