using System.Reflection;
using HarmonyLib;
using TMPro;

namespace AbyssMod.Services;

/// <summary>
/// 绕过 TMP_Text.SetText / text 属性，直接写入 m_text 并刷新 mesh，
/// 避免 IL2CPP 下 Postfix 再调 SetText 造成 Stack Overflow。
/// </summary>
public static class TmpTextHelper
{
    private static FieldInfo _mText;
    private static FieldInfo _havePropertiesChanged;
    private static bool _resolved;

    private static void EnsureFields()
    {
        if (_resolved)
            return;
        _resolved = true;
        _mText = AccessTools.Field(typeof(TMP_Text), "m_text");
        _havePropertiesChanged = AccessTools.Field(typeof(TMP_Text), "m_havePropertiesChanged");
    }

    public static bool TrySetTextDirect(TMP_Text tmp, string text)
    {
        if (tmp == null || text == null)
            return false;

        EnsureFields();
        if (_mText == null)
            return false;

        try
        {
            _mText.SetValue(tmp, text);
            _havePropertiesChanged?.SetValue(tmp, true);
            tmp.ForceMeshUpdate(true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
