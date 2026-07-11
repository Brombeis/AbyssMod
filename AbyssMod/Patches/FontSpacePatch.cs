using HarmonyLib;
using Project.Novel;

namespace AbyssMod.Patches;

/// <summary>
/// Latin/proportional layout fix for the main dialogue box (English builds).
/// 
/// Enables proportional text to have proper kerning, plus some dirty hacks to make it look fine.
/// </summary>
[HarmonyPatch]
public static class FontSpacePatch
{
    private static void ApplyProportional(NovelText instance)
    {
        instance.SetFontSpace(2f);         // Increase font space so letters don't overlap
        instance.SetFontSize(30f);
        instance.SetUseProportional(true); // Disable monospace
        instance.SetMaxLength(200);        // Raise the max length since we're hitting it when we duplicate spaces 7x
    }

    // We patch both Initialize and SetParam else the latter being called would overwrite our changes to the former

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelText), nameof(NovelText.Initialize))]
    public static void AfterInitialize(NovelText __instance) => ApplyProportional(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelText), nameof(NovelText.SetParam))]
    public static void AfterSetParam(NovelText __instance) => ApplyProportional(__instance);

    // Proportional mode has a bug where spaces render ~1/7th their normal width.
    // So we duplicate each space 7 times.
    //
    // TranslationPatch.SetText also patches NovelText.Parse to apply the translation,
    // so we have to make sure this runs AFTER it. To that end, we set the priority to Low
    // Might break if TranslationPatch's priority is ever lowered from Normal to Low as well.

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(NovelText), nameof(NovelText.Parse))]
    public static void BeforeParse(ref string message)
    {
        message = message.Replace(" ", "       ");
    }

    // The 7× space expansion creates a new problem: the typing animation pauses
    // on each space, so a single word gap causes 7× the normal delay.
    // This patch skips the text counter past consecutive spaces in one frame.
    //
    // Important: the typing animation lives in NovelViewMessageWindow, NOT in
    // NovelMessageTextComponent (which handles center/UI text only).

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelViewMessageWindow), nameof(NovelViewMessageWindow.OnViewUpdate))]
    public static void AfterOnViewUpdate(NovelViewMessageWindow __instance)
    {
        if (!__instance._isPlay) return;
        var letters = __instance._letters;
        if (letters == null) return;

        // _textCount is a float that the game increments each frame by speed*deltaTime.
        // When it crosses an integer boundary, the next letter is revealed.
        // We jump it forward past any run of spaces so they all appear at once.
        var idx = (int)__instance._textCount;
        var startIdx = idx;
        while (idx < letters.Count && !letters[idx].isNewLineCode && letters[idx].rawText == " ")
        {
            idx++;
        }
        if (idx != startIdx)
        {
            __instance._textCount = idx;
            __instance._prevCount = idx;
        }
    }
}
