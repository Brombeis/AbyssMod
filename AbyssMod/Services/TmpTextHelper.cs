using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace AbyssMod.Services;

/// <summary>
/// 更新 TMP 显示文字。优先尝试写入 backing field；IL2CPP/Unity 6000 下
/// 字段不可见时改走 <see cref="TMP_Text.text"/>（调用方须设 GeneralTextPatch 的 _inTranslation）。
/// </summary>
public static class TmpTextHelper
{
    private static FieldInfo _mText;
    private static FieldInfo _havePropertiesChanged;
    private static bool _resolved;
    private static bool _loggedFallback;

    private static void EnsureFields()
    {
        if (_resolved)
            return;
        _resolved = true;

        _mText = AccessTools.Field(typeof(TMP_Text), "m_text")
            ?? AccessTools.Field(typeof(TMP_Text), "m_Text")
            ?? FindStringField("m_text");

        _havePropertiesChanged =
            AccessTools.Field(typeof(TMP_Text), "m_havePropertiesChanged")
            ?? AccessTools.Field(typeof(TMP_Text), "m_HasPropertyChanged");
    }

    private static FieldInfo FindStringField(string preferredName)
    {
        foreach (FieldInfo field in typeof(TMP_Text).GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType != typeof(string))
                continue;
            if (string.Equals(field.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                return field;
        }
        return null;
    }

    public static bool TrySetTextDirect(TMP_Text tmp, string text)
    {
        if (tmp == null || text == null)
            return false;

        EnsureFields();

        try
        {
            if (_mText != null)
            {
                _mText.SetValue(tmp, text);
                _havePropertiesChanged?.SetValue(tmp, true);
                tmp.ForceMeshUpdate(true);
                return true;
            }

            if (!_loggedFallback)
            {
                _loggedFallback = true;
                Logger.Info(
                    "TmpTextHelper: m_text field unavailable, using TMP_Text.text property fallback"
                );
            }

            // 调用方已设 _inTranslation，set_text prefix 不会重复翻译
            tmp.text = text;
            tmp.ForceMeshUpdate(true);
            return true;
        }
        catch (Exception e)
        {
            Logger.Warn($"TrySetTextDirect failed: {e.Message}");
            return false;
        }
    }
}
