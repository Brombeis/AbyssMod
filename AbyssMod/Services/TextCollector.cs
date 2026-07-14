using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BepInEx;

namespace AbyssMod.Services;

/// <summary>
/// 原文收集器：把游戏运行时出现的日文原文按类别写入 dump 目录，
/// 输出格式为 { "日文原文": "" }，方便后续批量翻译填充 value。
/// 每个类别（items / ui / skills ...）各自落盘到 dump/{category}_raw.json。
/// 仅在 Config.CollectText 开启时工作。
/// </summary>
public static class TextCollector
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, HashSet<string>> Collected = new();

    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private static string DumpPath(string category) =>
        Path.Combine(Paths.PluginPath, MyPluginInfo.PLUGIN_GUID, "dump", $"{category}_raw.json");

    /// <summary>
    /// 记录一条原文到指定类别。重复或空字符串会被忽略。发现新条目时立即落盘。
    /// </summary>
    public static void Record(string category, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (Lock)
        {
            if (!Collected.TryGetValue(category, out var set))
            {
                set = [];
                Collected[category] = set;
            }

            if (!set.Add(text))
                return;

            try
            {
                var path = DumpPath(category);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var dict = LoadExisting(path);
                // If the file exists but is unreadable, abort rather than clobber translations.
                if (dict == null)
                    return;

                foreach (var key in set)
                {
                    if (!dict.ContainsKey(key))
                        dict[key] = string.Empty;
                }

                // Write to a temp file first, then replace atomically so a crash
                // mid-write can never corrupt or empty the real dump file.
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(dict, JsonOptions), Utf8);
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                // 收集失败不应影响游戏运行
            }
        }
    }

    // Returns null if the file exists but cannot be read/parsed, to distinguish
    // "file missing" (safe to create) from "file unreadable" (abort write, preserve data).
    private static Dictionary<string, string>? LoadExisting(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(path, Utf8);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return null;
        }
    }
}
