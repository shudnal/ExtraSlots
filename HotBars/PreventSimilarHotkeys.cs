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
    private static readonly HashSet<string> similarName = new HashSet<string>();
    private static readonly HashSet<KeyCode> similarKeyCode = new HashSet<KeyCode>();
    private static bool _anyExtraSlotsHotkeyDown;
    private static bool _anyExtraSlotsHotkeyHeld;

    private static int _cacheUpdatedToken = -1;
    private static int _heldCacheUpdatedToken = -1;

    private static int _skipPreventionDepth;
    private static bool SkipPrevention => _skipPreventionDepth > 0;

    public static bool IsShortcutDown(KeyboardShortcut shortcut) => IsShortcutActive(shortcut, checkForHeld: false);

    public static bool IsShortcutPressed(KeyboardShortcut shortcut) => IsShortcutActive(shortcut, checkForHeld: true);

    private static bool IsShortcutActive(KeyboardShortcut shortcut, bool checkForHeld)
    {
        if (shortcut.MainKey == KeyCode.None)
            return false;

        _skipPreventionDepth++;

        try
        {
            bool mainKeyActive = checkForHeld
                ? ZInput.GetKey(shortcut.MainKey)
                : ZInput.GetKeyDown(shortcut.MainKey);

            if (!mainKeyActive)
                return false;

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!ZInput.GetKey(modifier))
                    return false;
            }

            return true;
        }
        finally
        {
            _skipPreventionDepth--;
        }
    }

    private static int GetCacheToken() => (Time.frameCount << 1) | (Time.inFixedTimeStep ? 1 : 0);

    private static void ResetHotkeyState()
    {
        _cacheUpdatedToken = -1;
        _heldCacheUpdatedToken = -1;

        _anyExtraSlotsHotkeyDown = false;
        _anyExtraSlotsHotkeyHeld = false;

        ZInput_TryGetButtonState_PreventSimilarHotkeys.checkForHeld = false;
        ZInput_TryGetButtonState_PreventSimilarHotkeys.skipCheck = false;

        ZInput_TryGetKeyStateLowLevel_PreventSimilarHotkeys.checkForHeld = false;
        ZInput_TryGetKeyStateLowLevel_PreventSimilarHotkeys.skipCheck = false;
    }

    public static void FillSimilarHotkey() => FillSimilarHotkey(ZInput.instance);

    internal static void FillSimilarHotkey(ZInput __instance)
    {
        if (ExtraSlots.IsDedicated)
            return;

        SanitizeShortcutsKeys();

        similarName.Clear();
        similarKeyCode.Clear();
        ResetHotkeyState();

        if (__instance?.m_buttons == null)
            return;

        Dictionary<string, HashSet<string>> pathToButtonNames = new Dictionary<string, HashSet<string>>();

        foreach (KeyValuePair<string, ZInput.ButtonDef> button in __instance.m_buttons)
        {
            AddButtonPath(button.Value.GetActionPath(effective: true), button.Key);
            AddButtonPath(button.Value.GetActionPath(effective: false), button.Key);
        }

        foreach (Slot slot in slots)
        {
            if (!slot.IsHotkeySlot)
                continue;

            KeyCode mainKey = slot.GetShortcut().MainKey;

            if (mainKey == KeyCode.None)
                continue;

            similarKeyCode.Add(mainKey);

            string keyPath = ZInput.KeyCodeToPath(mainKey);
            if (!pathToButtonNames.TryGetValue(keyPath, out HashSet<string> buttonNames))
                continue;

            similarName.UnionWith(buttonNames);
        }

        void AddButtonPath(string path, string buttonName)
        {
            if (string.IsNullOrEmpty(path))
                return;

            if (!pathToButtonNames.TryGetValue(path, out HashSet<string> buttonNames))
            {
                buttonNames = new HashSet<string>();
                pathToButtonNames[path] = buttonNames;
            }

            buttonNames.Add(buttonName);
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

    internal static bool IsAnyExtraSlotsHotkeyDown(bool checkForHeld = false)
    {
        int token = GetCacheToken();

        if (checkForHeld)
        {
            if (_heldCacheUpdatedToken == token)
                return _anyExtraSlotsHotkeyHeld;

            _heldCacheUpdatedToken = token;
            _anyExtraSlotsHotkeyHeld = false;

            foreach (Slot slot in slots)
            {
                if (!slot.IsHotkeySlot)
                    continue;

                if (!slot.IsShortcutPressedWithItem())
                    continue;

                _anyExtraSlotsHotkeyHeld = true;
                break;
            }

            return _anyExtraSlotsHotkeyHeld;
        }

        if (_cacheUpdatedToken == token)
            return _anyExtraSlotsHotkeyDown;

        _cacheUpdatedToken = token;
        _anyExtraSlotsHotkeyDown = false;

        foreach (Slot slot in slots)
        {
            if (!slot.IsHotkeySlot)
                continue;

            if (!slot.IsShortcutDownWithItem())
                continue;

            _anyExtraSlotsHotkeyDown = true;
            break;
        }

        return _anyExtraSlotsHotkeyDown;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp))]
    private static class ZInput_GetButtonUp_PreventSimilarHotkeys
    {
        private static void Prefix() => ZInput_TryGetButtonState_PreventSimilarHotkeys.skipCheck = true;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))]
    private static class ZInput_GetButton_PreventSimilarHotkeys
    {
        private static void Prefix() => ZInput_TryGetButtonState_PreventSimilarHotkeys.checkForHeld = true;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseButton))]
    private static class ZInput_GetMouseButton_PreventSimilarHotkeys
    {
        private static void Prefix() => ZInput_TryGetButtonState_PreventSimilarHotkeys.checkForHeld = true;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseButtonUp))]
    private static class ZInput_GetMouseButtonUp_PreventSimilarHotkeys
    {
        private static void Prefix() => ZInput_TryGetButtonState_PreventSimilarHotkeys.skipCheck = true;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.TryGetButtonState))]
    private static class ZInput_TryGetButtonState_PreventSimilarHotkeys
    {
        internal static bool checkForHeld = false;
        internal static bool skipCheck = false;

        private static void Postfix(string name, ref bool __result)
        {
            bool held = checkForHeld;
            bool skip = skipCheck;

            checkForHeld = false;
            skipCheck = false;

            if (!skip && __result && similarName.Contains(name))
                __result = !IsAnyExtraSlotsHotkeyDown(held);
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKey))]
    private static class ZInput_GetKey_PreventSimilarHotkeys
    {
        private static void Prefix() => ZInput_TryGetKeyStateLowLevel_PreventSimilarHotkeys.checkForHeld = true;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyUp))]
    private static class ZInput_GetKeyUp_PreventSimilarHotkeys
    {
        private static void Prefix() => ZInput_TryGetKeyStateLowLevel_PreventSimilarHotkeys.skipCheck = true;
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.TryGetKeyStateLowLevel))]
    private static class ZInput_TryGetKeyStateLowLevel_PreventSimilarHotkeys
    {
        internal static bool checkForHeld = false;
        internal static bool skipCheck = false;

        private static void Postfix(KeyCode keyCode, ref bool __result)
        {
            bool held = checkForHeld;
            bool skip = skipCheck;

            checkForHeld = false;
            skipCheck = false;

            if (!SkipPrevention && !skip && __result && similarKeyCode.Contains(keyCode))
                __result = !IsAnyExtraSlotsHotkeyDown(held);
        }
    }

    [HarmonyPatch]
    public static class ZInput_InternalUpdate_ResetSimilarHotkeyState
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.InternalUpdate));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.InternalUpdateFixed));
        }

        private static void Finalizer() => ResetHotkeyState();
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
