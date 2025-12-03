using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using static ExtraSlots.Slots;

namespace ExtraSlots.HotBars;

public static class PreventSimilarHotkeys
{
    private static readonly Dictionary<string, List<Slot>> similarHotkey = new Dictionary<string, List<Slot>>();

    public static void FillSimilarHotkey() => FillSimilarHotkey(ZInput.instance);

    internal static void FillSimilarHotkey(ZInput __instance)
    {
        if (ExtraSlots.IsDedicated)
            return;

        SanitizeShortcutsKeys();

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

    private static void SanitizeShortcutsKeys()
    {
        foreach (ConfigEntry<KeyboardShortcut> hotkeyConfig in ExtraSlots.GetHotkeysConfigs())
        {
            KeyCode key = hotkeyConfig.Value.MainKey;
            if (!ZInput.IsKeyCodeValid(key) || !IsCorrectKeyboardBind(key) && !IsCorrectGamepadBind(key) && !IsCorrectMouseBind(key))
            {
                if (!hotkeyConfig.Value.Equals(KeyboardShortcut.Empty))
                    ExtraSlots.LogWarning($"Wrong bind data on {hotkeyConfig.Definition}: {hotkeyConfig.Value}. Hotkey cleared.");

                hotkeyConfig.Value = KeyboardShortcut.Empty;
            }

            ReorderKeys(hotkeyConfig);
        }
    }

    private static bool IsCorrectKeyboardBind(KeyCode keyCode) => ZInput.TryKeyCodeToKey(keyCode, out Key key) && key != Key.None;

    private static bool IsCorrectMouseBind(KeyCode keyCode) => ZInput.TryKeyCodeToMouseButton(keyCode, out MouseButton mouseButton) && mouseButton != MouseButton.Left && mouseButton != MouseButton.Right;

    private static bool IsCorrectGamepadBind(KeyCode keyCode) => ZInput.TryKeyCodeToGamepadButton(keyCode, out _);

    private static void ReorderKeys(ConfigEntry<KeyboardShortcut> keyboardShortcut)
    {
        if (!IsModifier(keyboardShortcut.Value.MainKey))
            return;

        KeyCode key = keyboardShortcut.Value.Modifiers.FirstOrDefault(key => !IsModifier(key));
        if (key == KeyCode.None)
            return;

        keyboardShortcut.Value = new KeyboardShortcut(key, keyboardShortcut.Value.Modifiers.Where(k => k != key).AddItem(keyboardShortcut.Value.MainKey).ToArray());
        ExtraSlots.LogWarning($"Reordered bind data on {keyboardShortcut.Definition}: {keyboardShortcut.Value}.");
    }

    private static bool IsModifier(KeyCode key)
    {
        return key == KeyCode.AltGr ||
               key == KeyCode.LeftAlt ||
               key == KeyCode.RightAlt ||
               key == KeyCode.LeftShift ||
               key == KeyCode.RightShift ||
               key == KeyCode.LeftControl ||
               key == KeyCode.RightControl ||
               key == KeyCode.LeftApple ||
               key == KeyCode.RightApple ||
               key == KeyCode.LeftCommand ||
               key == KeyCode.RightCommand ||
               key == KeyCode.LeftWindows ||
               key == KeyCode.RightWindows;
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
