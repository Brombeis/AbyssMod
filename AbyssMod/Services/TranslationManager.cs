using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbyssMod;
using AbyssMod.Patches;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TMPro;
using Utility.Fonts;
using Utility.Toast;

namespace AbyssMod.Services;

/// <summary>
/// 翻译管理器：协调 masterdata 字典（m_*）、ui_texts 与 legacy 兜底字典的加载与查询。
/// </summary>
public class TranslationManager
{
    private static readonly HashSet<string> CriticalTypes =
    [
        TranslationPaths.Names,
        TranslationPaths.UiTexts,
    ];

    private readonly TranslationCache _cache;
    private readonly FontHelper _font;
    private readonly object _loadLock = new();
    private Task _loadTask;

    private readonly ConcurrentDictionary<string, Task> _loadingNovels = new();
    private readonly Dictionary<string, Dictionary<string, string>> _tables = new();

    public Dictionary<string, string> Names { get; private set; } = [];
    public Dictionary<string, string> Titles { get; private set; } = [];
    public Dictionary<string, string> Descriptions { get; private set; } = [];

    /// <summary>非剧情类文本的合并字典（ui_misc 及所有本地类别兜底）。</summary>
    public Dictionary<string, string> Texts { get; private set; } = [];

    public Dictionary<string, string> AbilityDescriptions { get; private set; } = [];
    public ConcurrentDictionary<string, Dictionary<string, string>> Novels { get; private set; } =
        new();

    public FontHelper Font => _font;

    public TranslationManager(TranslationCache cache, FontHelper font)
    {
        _cache = cache;
        _font = font;
    }

    public void Initialize()
    {
        Plugin.Instance.StartCoroutine(
            _font
                .LoadAsync(() =>
                {
                    Logger.Info($"Font loaded: {_font.Asset.name}");
                    TMP_Settings.fallbackFontAssets.Add(_font.Asset);
                })
                .WrapToIl2Cpp()
        );
        _ = EnsureStaticTranslationsLoadedAsync();
    }

    public Task EnsureStaticTranslationsLoadedAsync()
    {
        lock (_loadLock)
            return _loadTask ??= LoadTranslationAsync();
    }

    public void EnsureStaticTranslationsLoaded()
    {
        EnsureStaticTranslationsLoadedAsync().GetAwaiter().GetResult();
    }

    public Dictionary<string, string> GetTable(string type)
    {
        return _tables.TryGetValue(type, out var table) ? table : null;
    }

    public async Task LoadTranslationAsync()
    {
        if (!Config.Translation.Value)
            return;

        await _cache.FetchManifestAsync();

        var tasks = new Dictionary<string, Task<Dictionary<string, string>>>();
        foreach (string type in TranslationPaths.EnumerateMasterDictTypes())
            tasks[type] = _cache.LoadAsync(type);

        foreach (
            string legacy in new[]
            {
                TranslationPaths.Titles,
                TranslationPaths.Descriptions,
                TranslationPaths.AnotherName,
                TranslationPaths.AbilityDescriptions,
            }
        )
        {
            if (!tasks.ContainsKey(legacy))
                tasks[legacy] = _cache.LoadAsync(legacy);
        }

        await Task.WhenAll(tasks.Values);

        foreach (var (type, task) in tasks)
        {
            if (task.Result != null)
            {
                _tables[type] = task.Result;
                Logger.Info($"Translation loaded [{type}]. Total: {task.Result.Count}");
            }
            else
            {
                Logger.Warn($"Translation load failed [{type}]");
                if (CriticalTypes.Contains(type))
                    Toast.Warn("加载失败", $"翻译加载失败: {type}");
            }
        }

        Names = GetTable(TranslationPaths.Names) ?? [];
        Titles = GetTable(TranslationPaths.Titles) ?? [];
        Descriptions = GetTable(TranslationPaths.Descriptions) ?? [];
        AbilityDescriptions =
            GetTable(TranslationPaths.AbilityDescriptions)
            ?? GetTable("m_ability_details")
            ?? [];

        await _cache.SyncLegacyUiMiscAsync();
        await _cache.SyncAddOnFromCdnAsync();
        await _cache.SyncOtherFromCdnAsync();
        MachineTranslator.ReloadFromDisk();

        await MergeLocalAddOnFallbackAsync();
        AbilityTextMatcher.Rebuild(AbilityDescriptions);
        TemplateTextMatcher.Rebuild(Texts, Titles, Descriptions);
        GeneralTextPatch.RefreshAllVisibleText();
    }

    private async Task MergeLocalAddOnFallbackAsync()
    {
        var merged = new Dictionary<string, string>();

        void MergeTable(string type)
        {
            if (_tables.TryGetValue(type, out var table))
                MergeInto(merged, table, type);
        }

        MergeTable(TranslationPaths.AnotherName);
        MergeTable(TranslationPaths.AbilityDescriptions);
        MergeTable("m_ability_details");
        MergeTable("m_character_action_skills");
        MergeTable("m_character_profiles");
        MergeTable("m_tavern_character_cards");
        MergeTable("m_nether_codes");
        MergeTable("m_missions");

        var localCategories = TranslationPaths
            .EnumerateLocalCategories(_cache.CacheDir, Config.TranslationLanguage.Value)
            .Where(cat => cat != TranslationPaths.Ui)
            .ToList();

        if (localCategories.Count > 0)
        {
            var localTasks = localCategories.Select(cat => _cache.LoadAsync(cat)).ToList();
            await Task.WhenAll(localTasks);
            for (int i = 0; i < localCategories.Count; i++)
                MergeInto(merged, localTasks[i].Result, $"add-on/{localCategories[i]}");
        }

        Texts = merged;
        Logger.Info(
            $"Non-story text fallback merged. Total: {Texts.Count} "
                + $"(local add-on categories: {localCategories.Count})"
        );
    }

    private static void MergeInto(
        Dictionary<string, string> target,
        Dictionary<string, string> source,
        string label
    )
    {
        if (source == null || source.Count == 0)
            return;

        foreach (var kv in source)
            target[kv.Key] = kv.Value;

        Logger.Info($"Text fallback '{label}' merged. Total: {source.Count}");
    }

    public async Task GetNovelTranslationAsync(string novelId)
    {
        if (Novels.ContainsKey(novelId))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existingTask = _loadingNovels.GetOrAdd(novelId, tcs.Task);

        if (existingTask != tcs.Task)
        {
            await existingTask;
            return;
        }

        try
        {
            var translations = await _cache.LoadAsync(TranslationPaths.Novels, novelId);
            if (translations != null)
            {
                Novels[novelId] = translations;
                Logger.Info($"Scenario translation loaded. Total: {translations.Count}");
            }
            else
            {
                Logger.Warn($"Translations loaded failed: {novelId}");
                Toast.Warn("加载失败", $"剧本ID: {novelId}");
            }
            tcs.SetResult();
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            _loadingNovels.TryRemove(novelId, out _);
        }
    }
}
