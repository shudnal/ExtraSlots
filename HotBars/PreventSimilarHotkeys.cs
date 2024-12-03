using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static ExtraSlots.Slots;
using UnityEngine.InputSystem;

namespace ExtraSlots.HotBars;

public static class PreventSimilarHotkeys
{
    private static readonly Dictionary<string, List<Slot>> similarHotkey = new Dictionary<string, List<Slot>>();

    public static void FillSimilarHotkey() => FillSimilarHotkey(ZInput.instance);

    internal static void FillSimilarHotkey(ZInput __instance)
    {
        similarHotkey.Clear();
        if (__instance == null)
            return;

        if (__instance.m_buttons == null)
            return;

        foreach (Slot slot in slots.Where(slot => slot.IsHotkeySlot))
        {
            if (!ZInput.TryKeyCodeToKey(slot.GetShortcut().MainKey, out Key key))
                continue;

            var button = __instance.m_buttons.FirstOrDefault(kvp => kvp.Value.GetActionPath() == ZInput.KeyToPath(key));
            if (button.Key == null)
                continue;

            if (similarHotkey.TryGetValue(button.Key, out List<Slot> slotsWithHotkey))
                slotsWithHotkey.Add(slot);
            else
                similarHotkey[button.Key] = new List<Slot>() { slot };
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.TryGetButtonState))]
    private static class ZInput_TryGetButtonState_PreventSimilarHotkeys
    {
        private static bool Prefix(string name) => ZInput.IsGamepadActive() || !(similarHotkey.TryGetValue(name, out List<Slot> slotsWithHotkey) && slotsWithHotkey.Any(slot => slot.IsShortcutDown() && slot.IsActive && !slot.IsFree));
    }

    [HarmonyPatch]
    public static class ZInput_SimilarHotkeyOnBind
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetKBMButtons));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetGamepadButtonsGeneric));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetGamepadToClassic));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetGamepadToAlt1));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetGamepadToAlt2));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.OnRebindComplete));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetToDefault));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.Load));
        }

        private static void Postfix(ZInput __instance) => FillSimilarHotkey(__instance);
    }
}
