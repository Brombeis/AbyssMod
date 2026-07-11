using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Project;
using Project.Outgame;
using UnityEngine;

namespace AbyssMod.Patches;

// Intercepts ImageBannerPopup (the tutorial slideshow popup) to:
//   - Save each displayed image as a PNG on first view
//   - Load a translated PNG in its place if one already exists on disk
//
// Images are stored at: <game>/BepInEx/Translation/en/Tutorial/<sprite_name>.png
// Replace any of those files with a translated PNG of the same size to see it in-game.

[HarmonyPatch]
static class TutorialImagePatch
{
    static readonly string TutorialDir;

    // Tracks which popup is currently showing and which page index is active.
    static ImageBannerPopup _activePopup;
    static int _activeIndex = -1;

    // Prevents double-saving the same sprite across popup opens.
    static readonly HashSet<string> _alreadySaved = new();

    static TutorialImagePatch()
    {
        // DLL lives at <game>\BepInEx\plugins\AbyssMod\ — go up three times to reach the game root.
        string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string gameDir = Path.GetFullPath(Path.Combine(dllDir, "..", "..", ".."));
        TutorialDir = Path.Combine(gameDir, "BepInEx", "Translation", "en", "Tutorial");
        Plugin.Log.LogInfo($"[TutorialImage] Tutorial image directory: {TutorialDir}");
    }

    // Hook 1, called every time the shown page changes (including the first show).
    [HarmonyPatch(typeof(ImageBannerPopup), "UpdateView")]
    [HarmonyPostfix]
    static void UpdateView_Postfix(ImageBannerPopup __instance, int index)
    {
        Plugin.Log.LogInfo($"[TutorialImage] UpdateView fired: index={index}, popup={__instance?.GetHashCode()}");

        _activePopup = __instance;
        _activeIndex = index;

        var list = __instance?._thumbnailList;
        if (list == null)
        {
            Plugin.Log.LogWarning("[TutorialImage] UpdateView: _thumbnailList is null");
            return;
        }

        Plugin.Log.LogInfo($"[TutorialImage] UpdateView: thumbnailList.Count={list.Count}");

        if (index < 0 || index >= list.Count)
        {
            Plugin.Log.LogWarning($"[TutorialImage] UpdateView: index {index} out of range [0,{list.Count})");
            return;
        }

        var thumb = list[index];
        if (thumb == null)
        {
            Plugin.Log.LogWarning($"[TutorialImage] UpdateView: thumbnail at index {index} is null");
            return;
        }

        var cached = thumb.Icon;
        if (cached != null)
        {
            Plugin.Log.LogInfo($"[TutorialImage] UpdateView: sprite already cached (name='{cached.name}'), handling immediately");
            HandleSprite(__instance, cached);
        }
        else
        {
            Plugin.Log.LogInfo("[TutorialImage] UpdateView: sprite not cached yet, waiting for set_Icon hook");
        }
    }

    // Hook 2, called by LazyLoadThumbnail internals when the async sprite load finishes.
    [HarmonyPatch(typeof(LazyLoadThumbnail), "set_Icon")]
    [HarmonyPostfix]
    static void SetIcon_Postfix(LazyLoadThumbnail __instance, Sprite value)
    {
        // Check pointer validity before any Il2Cpp property access —
        // the C# reference can be non-null while the native pointer is zero/invalid.
        if (value == null || value.Pointer == IntPtr.Zero) return;
        if (__instance == null || __instance.Pointer == IntPtr.Zero) return;

        // Bail out early (before touching any Il2Cpp props) when no popup is active.
        // set_Icon fires for many unrelated thumbnails across the whole game.
        if (_activePopup == null) return;

        Plugin.Log.LogInfo($"[TutorialImage] set_Icon fired: sprite='{value.name}', ptr={__instance.Pointer}");


        var list = _activePopup._thumbnailList;
        if (list == null)
        {
            Plugin.Log.LogWarning("[TutorialImage] set_Icon: active popup has null thumbnailList");
            return;
        }

        if (_activeIndex < 0 || _activeIndex >= list.Count)
        {
            Plugin.Log.LogInfo($"[TutorialImage] set_Icon: activeIndex={_activeIndex} out of range [0,{list.Count}), skipping");
            return;
        }

        var current = list[_activeIndex];
        if (current == null)
        {
            Plugin.Log.LogWarning($"[TutorialImage] set_Icon: thumbnail at activeIndex {_activeIndex} is null");
            return;
        }

        if (current.Pointer != __instance.Pointer)
        {
            Plugin.Log.LogInfo($"[TutorialImage] set_Icon: ptr mismatch (current={current.Pointer}, this={__instance.Pointer}), not the active slide");
            return;
        }

        Plugin.Log.LogInfo($"[TutorialImage] set_Icon: matched active slide, handling sprite '{value.name}'");
        HandleSprite(_activePopup, value);
    }

    static void HandleSprite(ImageBannerPopup popup, Sprite sprite)
    {
        if (sprite == null)
        {
            Plugin.Log.LogWarning("[TutorialImage] HandleSprite: sprite is null");
            return;
        }
        if (sprite.texture == null)
        {
            Plugin.Log.LogWarning($"[TutorialImage] HandleSprite: sprite '{sprite.name}' has null texture");
            return;
        }

        string name = SanitizeFileName(sprite.name);
        string path = Path.Combine(TutorialDir, name + ".png");

        Plugin.Log.LogInfo($"[TutorialImage] HandleSprite: sprite='{sprite.name}', sanitized='{name}', path='{path}'");

        if (File.Exists(path))
        {
            Plugin.Log.LogInfo("[TutorialImage] HandleSprite: replacement file found, loading");
            LoadAndReplace(popup, path);
        }
        else if (!_alreadySaved.Contains(name))
        {
            Plugin.Log.LogInfo("[TutorialImage] HandleSprite: no replacement, saving original");
            _alreadySaved.Add(name);
            SaveSprite(sprite, path);
        }
        else
        {
            Plugin.Log.LogInfo("[TutorialImage] HandleSprite: already saved this session, skipping");
        }
    }

    static void LoadAndReplace(ImageBannerPopup popup, string path)
    {
        try
        {
            Plugin.Log.LogInfo($"[TutorialImage] LoadAndReplace: reading {path}");
            byte[] bytes = File.ReadAllBytes(path);
            Plugin.Log.LogInfo($"[TutorialImage] LoadAndReplace: read {bytes.Length} bytes");

            var tex = new Texture2D(2, 2);
            var il2CppBytes = new Il2CppStructArray<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
                il2CppBytes[i] = bytes[i];

            bool loaded = ImageConversion.LoadImage(tex, il2CppBytes);
            Plugin.Log.LogInfo($"[TutorialImage] LoadAndReplace: LoadImage returned {loaded}, tex size={tex.width}x{tex.height}");

            var newSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));

            var imgComponent = popup._thumbnailImage;
            if (imgComponent == null)
            {
                Plugin.Log.LogWarning("[TutorialImage] LoadAndReplace: _thumbnailImage is null, cannot replace");
                return;
            }

            imgComponent.sprite = newSprite;
            Plugin.Log.LogInfo($"[TutorialImage] LoadAndReplace: replaced sprite from {Path.GetFileName(path)}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[TutorialImage] LoadAndReplace failed for '{path}': {e}");
        }
    }

    static void SaveSprite(Sprite sprite, string path)
    {
        try
        {
            Directory.CreateDirectory(TutorialDir);
            Plugin.Log.LogInfo($"[TutorialImage] SaveSprite: saving '{sprite.name}' to {path}");

            var src = sprite.texture;
            Plugin.Log.LogInfo($"[TutorialImage] SaveSprite: source texture '{src.name}' {src.width}x{src.height}, isReadable={src.isReadable}");

            // GPU blit to a readable texture (addressable sprites are typically non-readable).
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0);
            Graphics.Blit(src, rt);
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;

            var readable = new Texture2D(src.width, src.height);
            readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readable.Apply();

            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            Plugin.Log.LogInfo("[TutorialImage] SaveSprite: GPU blit done");

            var pngData = ImageConversion.EncodeToPNG(readable);
            Plugin.Log.LogInfo($"[TutorialImage] SaveSprite: encoded {pngData.Length} PNG bytes");

            byte[] managed = new byte[pngData.Length];
            for (int i = 0; i < pngData.Length; i++)
                managed[i] = pngData[i];

            File.WriteAllBytes(path, managed);
            Plugin.Log.LogInfo($"[TutorialImage] SaveSprite: wrote {managed.Length} bytes to {path}");

            UnityEngine.Object.Destroy(readable);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[TutorialImage] SaveSprite failed for '{path}': {e}");
        }
    }

    static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
