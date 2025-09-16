using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.InputSystem;
using static ExtraSlots.Slots;

namespace ExtraSlots.HotBars;

public static class PreventSimilarHotkeys
{
    private static readonly Dictionary<string, List<Slot>> similarHotkey = new Dictionary<string, List<Slot>>();

    public static void FillSimilarHotkey() => FillSimilarHotkey(ZInput.instance);

    internal static void FillSimilarHotkey(ZInput __instance)
    {
        ReorderShortcutsKeys();

        similarHotkey.Clear();
        if (__instance == null)
            return;

        if (__instance.m_buttons == null)
            return;

        foreach (Slot slot in slots.Where(slot => slot.IsHotkeySlot))
        {
            if (!ZInput.TryKeyCodeToKey(slot.GetShortcut().MainKey, out Key key))
                continue;

            var button = __instance.m_buttons.FirstOrDefault(kvp => kvp.Value.GetActionPath(effective: true) == ZInput.KeyToPath(key) || kvp.Value.GetActionPath(effective: false) == ZInput.KeyToPath(key));
            if (button.Key == null)
                continue;

            if (similarHotkey.TryGetValue(button.Key, out List<Slot> slotsWithHotkey))
                slotsWithHotkey.Add(slot);
            else
                similarHotkey[button.Key] = new List<Slot>() { slot };
        }
    }

    private static void ReorderShortcutsKeys()
    {
        ReorderKeys(ExtraSlots.foodSlotHotKey1);
        ReorderKeys(ExtraSlots.foodSlotHotKey2);
        ReorderKeys(ExtraSlots.foodSlotHotKey3);
        ReorderKeys(ExtraSlots.ammoSlotHotKey1);
        ReorderKeys(ExtraSlots.ammoSlotHotKey2);
        ReorderKeys(ExtraSlots.ammoSlotHotKey3);
        ReorderKeys(ExtraSlots.quickSlotHotKey1);
        ReorderKeys(ExtraSlots.quickSlotHotKey2);
        ReorderKeys(ExtraSlots.quickSlotHotKey3);
        ReorderKeys(ExtraSlots.quickSlotHotKey4);
        ReorderKeys(ExtraSlots.quickSlotHotKey5);
        ReorderKeys(ExtraSlots.quickSlotHotKey6);
    }

    private static void ReorderKeys(ConfigEntry<KeyboardShortcut> keyboardShortcut)
    {
        if (!IsModifier(keyboardShortcut.Value.MainKey))
            return;

        UnityEngine.KeyCode key = keyboardShortcut.Value.Modifiers.FirstOrDefault(key => !IsModifier(key));
        if (key == UnityEngine.KeyCode.None)
            return;

        keyboardShortcut.Value = new KeyboardShortcut(key, keyboardShortcut.Value.Modifiers.Where(k => k != key).AddItem(keyboardShortcut.Value.MainKey).ToArray());
    }

    private static bool IsModifier(UnityEngine.KeyCode key)
    {
        return key == UnityEngine.KeyCode.AltGr ||
               key == UnityEngine.KeyCode.LeftAlt ||
               key == UnityEngine.KeyCode.RightAlt ||
               key == UnityEngine.KeyCode.LeftShift ||
               key == UnityEngine.KeyCode.RightShift ||
               key == UnityEngine.KeyCode.LeftControl ||
               key == UnityEngine.KeyCode.RightControl ||
               key == UnityEngine.KeyCode.LeftApple ||
               key == UnityEngine.KeyCode.RightApple ||
               key == UnityEngine.KeyCode.LeftCommand ||
               key == UnityEngine.KeyCode.RightCommand ||
               key == UnityEngine.KeyCode.LeftWindows ||
               key == UnityEngine.KeyCode.RightWindows;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.TryGetButtonState))]
    private static class ZInput_TryGetButtonState_PreventSimilarHotkeys
    {
        private static bool Prefix(string name) => ZInput.IsGamepadActive() || !(similarHotkey.TryGetValue(name, out List<Slot> slotsWithHotkey) && slotsWithHotkey.Any(slot => slot.IsShortcutDownWithItem()));
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

        private static void Finalizer(ZInput __instance) => FillSimilarHotkey(__instance);
    }

    [HarmonyPatch(typeof(ConnectPanel), nameof(ConnectPanel.Update))]
    public static class ConnectPanel_Update_RebindF2
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                if (instr.opcode == OpCodes.Ldc_I4 && instr.operand is int value && value == (int)KeyCode.F2)
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConnectPanel_Update_RebindF2), nameof(GetCustomKey)));
                else
                    yield return instr;
            }
        }

        private static KeyCode GetCustomKey() => ExtraSlots.rebindConnectPanel.Value;
    }
}
