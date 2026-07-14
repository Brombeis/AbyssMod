using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AbyssMod;
using AbyssMod.Patches;
using BepInEx;
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
        // _font is null when FontBundlePath is empty (English fork uses the native
        // font — no Chinese TMP fallback needed).
        if (_font != null)
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
        }
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

    private volatile bool _reloading;

    public async Task ReloadAsync()
    {
        if (_reloading)
        {
            Logger.Warn("Translation reload already in progress, ignoring.");
            return;
        }
        _reloading = true;
        try
        {
            Logger.Info("Reloading translations...");
            await LoadTranslationAsync();
            Toast.Success("AbyssMod", "Translations reloaded");
        }
        catch (Exception e)
        {
            Logger.Warn($"Translation reload failed: {e.Message}");
            Toast.Warn("AbyssMod", $"Reload failed: {e.Message}");
        }
        finally
        {
            _reloading = false;
        }
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
                    Toast.Warn("Load failed", $"Translation load failed: {type}");
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
        UiRegexMatcher.Rebuild(Texts);
        TemplateTextMatcher.Rebuild(Texts, Titles, Descriptions);
        GeneralTextPatch.RefreshAllVisibleText();
        GeneralTextPatch.ReApplyFromRegistry();
    }

    private async Task MergeLocalAddOnFallbackAsync()
    {
        var merged = new Dictionary<string, string>();
        var masterKeys = BuildMasterDataKeySet();

        void MergeTable(string type, ISet<string> skipKeys = null)
        {
            if (_tables.TryGetValue(type, out var table))
                MergeInto(merged, table, type, skipKeys);
        }

        // 1. legacy 底稿（后层 m_* 会覆盖同 key）
        MergeTable(TranslationPaths.Titles);
        MergeTable(TranslationPaths.Descriptions);
        MergeTable(TranslationPaths.AnotherName);
        MergeTable(TranslationPaths.AbilityDescriptions);

        var language = Config.TranslationLanguage.Value;
        var localCategories = TranslationPaths
            .EnumerateLocalCategories(_cache.CacheDir, language)
            .Where(cat => cat != TranslationPaths.Ui)
            .ToList();

        // 2. add-on：不写入已在任一 m_* 表中的 key
        if (localCategories.Count > 0)
        {
            var localTasks = localCategories.Select(cat => _cache.LoadAsync(cat)).ToList();
            await Task.WhenAll(localTasks);
            for (int i = 0; i < localCategories.Count; i++)
                MergeInto(
                    merged,
                    localTasks[i].Result,
                    $"add-on/{localCategories[i]}",
                    masterKeys
                );
        }

        // 3. 全部 m_* 权威层（始终覆盖 legacy / add-on）
        foreach (string type in TranslationPaths.EnumerateMasterDictTypes())
        {
            if (type.StartsWith("m_", StringComparison.Ordinal))
                MergeTable(type);
        }

        // 4. names / ui_texts 仅补 m_* 未覆盖的 key
        MergeTable(TranslationPaths.Names, masterKeys);
        MergeTable(TranslationPaths.UiTexts, masterKeys);

        MergeDumpFallback(merged);

        Texts = merged;
        Logger.Info(
            $"Non-story text fallback merged. Total: {Texts.Count} "
                + $"(add-on: {localCategories.Count}, m_* keys: {masterKeys.Count})"
        );
    }

    private static void MergeDumpFallback(Dictionary<string, string> target)
    {
        var dumpDir = Path.Combine(Paths.PluginPath, MyPluginInfo.PLUGIN_GUID, "dump");
        if (!Directory.Exists(dumpDir))
            return;

        int added = 0;
        foreach (var path in Directory.GetFiles(dumpDir, "*_raw.json"))
        {
            Dictionary<string, string> dump;
            try
            {
                dump = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Logger.Warn($"Dump fallback: unreadable file skipped: {Path.GetFileName(path)} ({e.Message})");
                continue;
            }
            if (dump == null)
                continue;

            foreach (var kv in dump)
            {
                if (string.IsNullOrEmpty(kv.Value))
                    continue; // still untranslated
                if (target.ContainsKey(kv.Key))
                    continue; // official dictionary wins
                target[kv.Key] = kv.Value;
                added++;
            }
        }

        if (added > 0)
            Logger.Info($"Dump fallback merged. Added: {added}");
    }

    private HashSet<string> BuildMasterDataKeySet()
    {
        var keys = new HashSet<string>();
        foreach (string type in TranslationPaths.EnumerateMasterDictTypes())
        {
            if (!type.StartsWith("m_", StringComparison.Ordinal))
                continue;
            if (!_tables.TryGetValue(type, out var table))
                continue;
            foreach (string key in table.Keys)
                keys.Add(key);
        }
        return keys;
    }

    private static void MergeInto(
        Dictionary<string, string> target,
        Dictionary<string, string> source,
        string label,
        ISet<string> skipKeys = null
    )
    {
        if (source == null || source.Count == 0)
            return;

        int merged = 0;
        foreach (var kv in source)
        {
            if (skipKeys != null && skipKeys.Contains(kv.Key))
                continue;
            if (target.TryGetValue(kv.Key, out var existing) && existing != kv.Value)
                Logger.Warn(
                    $"Text fallback '{label}': key already present with a different value, overwriting: {kv.Key}"
                );
            target[kv.Key] = kv.Value;
            merged++;
        }

        Logger.Info($"Text fallback '{label}' merged. Added: {merged} (source: {source.Count})");
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
                Toast.Warn("Load failed", $"Scenario ID: {novelId}");
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
