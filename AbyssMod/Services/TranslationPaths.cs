using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AbyssMod.Services;

/// <summary>
/// 翻译资源路径构建工具。
/// 负责生成远程 URL 和本地缓存路径。
/// </summary>
public static class TranslationPaths
{
    // ──────────────────────────────────────────────────
    // 类型常量（与作者 CDN 仓库一致）
    // ──────────────────────────────────────────────────
    public const string Manifest            = "manifest";
    public const string Names               = "names";
    public const string Titles              = "titles";
    public const string Descriptions        = "descriptions";
    public const string AnotherName         = "another_name";
    public const string AbilityDescriptions = "ability_descriptions";
    public const string Novels              = "novels";
    public const string UiTexts             = "ui_texts";

    // 本仓库自定义类型（作者 CDN 无此目录，不会被覆盖，靠本地文件兜底）
    public const string Items  = "items";
    public const string Ui     = "ui";
    public const string Other  = "other";

    /// <summary>所有本地自定义类别的统一容器目录（translations/add-on/）。</summary>
    public const string AddOn = "add-on";

    /// <summary>过渡期 legacy 兜底目录（translations/legacy/add-on/）。</summary>
    public const string Legacy = "legacy";

    public const string LegacyUiMisc = "ui_misc";

    /// <summary>由 master_mapping.json dict_types 驱动，启动时由 MasterMapping.Load 填充。</summary>
    public static IReadOnlyList<string> ContentTypes { get; private set; } = [];

    public static void SetContentTypes(List<string> types) => ContentTypes = types;

    /// <summary>
    /// 作者/框架保留的顶层目录名称。
    /// <see cref="TranslationManager"/> 自动扫描时遇到这些目录一律跳过。
    /// add-on 目录本身也列入，防止被误当成类别。
    /// </summary>
    public static readonly HashSet<string> ReservedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        Manifest,
        Names,
        Titles,
        Descriptions,
        AnotherName,
        AbilityDescriptions,
        Novels,
        Other,
        AddOn,
        Legacy,
        UiTexts,
    };

    /// <summary>CDN 扁平字典（m_* / names / ui_texts 等），缓存路径为 {type}/{language}.json。</summary>
    public static bool IsCdnFlatType(string type) =>
        ContentTypes.Contains(type) || ReservedTypes.Contains(type);

    // ──────────────────────────────────────────────────
    // URL / 路径构建
    // ──────────────────────────────────────────────────

    public static string BuildRemoteUrl(string cdn, string type, string language, string id = null)
    {
        return type switch
        {
            Novels when id != null => $"{cdn}/{Novels}/{id}/{language}.json",
            Novels => throw new ArgumentException("Novel ID is required for novels type"),
            _ => $"{cdn}/{type}/{language}.json",
        };
    }

    public static string BuildAddOnRemoteUrl(string cdn, string category, string language) =>
        $"{cdn.TrimEnd('/')}/{AddOn}/{category}/{language}.json";

    public static string BuildAddOnCachePath(string cacheDir, string category, string language) =>
        Path.Combine(cacheDir, AddOn, category, $"{language}.json");

    public static string BuildLegacyAddOnRemoteUrl(string cdn, string category, string language) =>
        $"{cdn.TrimEnd('/')}/{Legacy}/{AddOn}/{category}/{language}.json";

    public static string BuildLegacyAddOnCachePath(
        string cacheDir,
        string category,
        string language
    ) => Path.Combine(cacheDir, Legacy, AddOn, category, $"{language}.json");

    public static string BuildOtherRemoteUrl(string cdn, string category, string language) =>
        $"{cdn.TrimEnd('/')}/{Other}/{category}/{language}.json";

    public static string BuildOtherCachePath(string cacheDir, string category, string language) =>
        Path.Combine(cacheDir, Other, category, $"{language}.json");

    public static string BuildCachePath(string cacheDir, string type, string language, string id = null)
    {
        if (type == Novels && id != null)
            return Path.Combine(cacheDir, Novels, id, $"{language}.json");
        if (type == Novels)
            throw new ArgumentException("Novel ID is required for novels type");

        if (!IsCdnFlatType(type))
            return Path.Combine(cacheDir, AddOn, type, $"{language}.json");

        return Path.Combine(cacheDir, type, $"{language}.json");
    }

    public static IEnumerable<string> EnumerateLocalCategories(string cacheDir, string language)
    {
        var addOnDir = Path.Combine(cacheDir, AddOn);
        if (!Directory.Exists(addOnDir))
            yield break;

        foreach (var dir in Directory.GetDirectories(addOnDir))
        {
            var langFile = Path.Combine(dir, $"{language}.json");
            if (File.Exists(langFile))
                yield return Path.GetFileName(dir);
        }
    }

    public static IEnumerable<string> EnumerateOtherCategories(string cacheDir, string language)
    {
        var otherDir = Path.Combine(cacheDir, Other);
        if (!Directory.Exists(otherDir))
            yield break;

        foreach (var dir in Directory.GetDirectories(otherDir))
        {
            var langFile = Path.Combine(dir, $"{language}.json");
            if (File.Exists(langFile))
                yield return Path.GetFileName(dir);
        }
    }

    /// <summary>master_mapping dict_types 中尚未列入 ReservedTypes 的类型（用于增量加载）。</summary>
    public static IEnumerable<string> EnumerateMasterDictTypes() =>
        ContentTypes.Where(t => !string.Equals(t, Novels, StringComparison.OrdinalIgnoreCase));
}
