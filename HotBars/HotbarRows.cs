using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Valheim.UI;
using static ExtraSlots.Slots;

namespace ExtraSlots.HotBars;

public static class HotbarRows
{
    public enum RowMode
    {
        Hold,
        Persistent
    }

    private static Player currentPlayer;
    private static int currentRow;
    private static int selectedRow;
    private static bool holdWasActive;
    private static RowMode lastMode;
    private static bool readingScroll;
    private static int renderedRadialRow = -1;

    internal static int CurrentRow => ExtraSlots.vanillaHotbarRowsEnabled.Value ? currentRow : 0;

    internal static void UpdateInput(Player player)
    {
        if (!ReferenceEquals(currentPlayer, player))
        {
            currentPlayer = player;
            currentRow = 0;
            selectedRow = 0;
            holdWasActive = false;
        }

        if (!ExtraSlots.vanillaHotbarRowsEnabled.Value)
        {
            currentRow = 0;
            selectedRow = 0;
            holdWasActive = false;
            lastMode = ExtraSlots.vanillaHotbarRowMode.Value;
            return;
        }

        RowMode mode = ExtraSlots.vanillaHotbarRowMode.Value;
        if (lastMode != mode)
        {
            if (!ExtraSlots.vanillaHotbarGamepadRowSwitching.Value)
                selectedRow = 0;
            lastMode = mode;
        }

        int height = InventoryHeightPlayer;
        selectedRow = Mathf.Clamp(selectedRow, 0, height - 1);
        int previousSelectedRow = selectedRow;
        bool allowInput = player.TakeInput() && !GameCamera.InFreeFly();

        if (CanSwitchRowsWithGamepad(player))
        {
            bool previous = ZInput.GetButtonDown("JoyHotbarLeft");
            bool next = ZInput.GetButtonDown("JoyHotbarRight");
            if (previous != next)
                selectedRow = (selectedRow + (previous ? height - 1 : 1)) % height;
        }

        if (mode == RowMode.Hold)
        {
            bool holding = allowInput && PreventSimilarHotkeys.IsShortcutPressed(ExtraSlots.vanillaHotbarRowModifier.Value);
            currentRow = holding
                ? ResolveRow(ExtraSlots.vanillaHotbarHoldRow.Value, height)
                : ExtraSlots.vanillaHotbarGamepadRowSwitching.Value ? selectedRow : 0;

            if (holding != holdWasActive)
            {
                if (holding)
                    PlayOpenInventorySound(player);
                else if (allowInput)
                    PlayCloseInventorySound(player);
                holdWasActive = holding;
            }
            else if (!holding && selectedRow != previousSelectedRow)
            {
                PlayOpenInventorySound(player);
            }
            return;
        }

        holdWasActive = false;
        if (allowInput)
        {
            bool previous = PreventSimilarHotkeys.IsShortcutDown(ExtraSlots.vanillaHotbarPreviousRow.Value);
            bool next = PreventSimilarHotkeys.IsShortcutDown(ExtraSlots.vanillaHotbarNextRow.Value);

            if (previous != next)
                selectedRow = (selectedRow + (previous ? height - 1 : 1)) % height;

            if (ExtraSlots.vanillaHotbarScrollToSwitch.Value && !player.InPlaceMode()
                && PreventSimilarHotkeys.IsShortcutPressed(ExtraSlots.vanillaHotbarRowModifier.Value))
            {
                readingScroll = true;
                float scroll;
                try
                {
                    scroll = ZInput.GetMouseScrollWheel();
                }
                finally
                {
                    readingScroll = false;
                }

                if (scroll > 0f)
                    selectedRow = (selectedRow + height - 1) % height;
                else if (scroll < 0f)
                    selectedRow = (selectedRow + 1) % height;
            }
        }

        currentRow = selectedRow;
        if (selectedRow != previousSelectedRow)
            PlayOpenInventorySound(player);
    }

    private static void PlayOpenInventorySound(Player player)
    {
        InventoryGui inventoryGui = InventoryGui.instance;
        if (inventoryGui)
            inventoryGui.m_openInventoryEffects.Create(player.transform.position, Quaternion.identity);
    }

    private static void PlayCloseInventorySound(Player player)
    {
        InventoryGui inventoryGui = InventoryGui.instance;
        if (inventoryGui)
            inventoryGui.m_closeInventoryEffects.Create(player.transform.position, Quaternion.identity);
    }

    // Gamepad row selection is persistent in both keyboard modes.
    // The vanilla D-pad selection already ignores JoyAltKeys, so it stays unchanged.
    private static bool CanSwitchRowsWithGamepad(Player player) =>
        ExtraSlots.vanillaHotbarRowsEnabled.Value
        && ExtraSlots.vanillaHotbarGamepadRowSwitching.Value
        && ZInput.IsGamepadActive()
        && ZInput.GetButton("JoyAltKeys")
        && player && player.TakeInput() && !player.InPlaceMode()
        && !Hud.InRadial()
        && !Compatibility.PlantEasilyCompat.DisableGamepadInput;

    private static bool AllowMinimapZoom() => !CanSwitchRowsWithGamepad(Player.m_localPlayer);

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateMap))]
    private static class Minimap_UpdateMap_GamepadRowSwitching
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> original = instructions.ToList();
            List<CodeInstruction> code = original.Select(instruction => new CodeInstruction(instruction)).ToList();
            MethodInfo getButtonDown = AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonDown));
            MethodInfo getActiveSelf = AccessTools.PropertyGetter(typeof(GameObject), nameof(GameObject.activeSelf));
            MethodInfo allowZoom = AccessTools.Method(typeof(HotbarRows), nameof(AllowMinimapZoom));
            List<(int index, object target)> conditions = new List<(int, object)>(2);

            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldstr || code[i].operand is not string name
                    || (name != "JoyMiniMapZoomIn" && name != "JoyMiniMapZoomOut")
                    || !code[i + 1].Calls(getButtonDown))
                    continue;

                if (code[i + 2].opcode != OpCodes.Brfalse && code[i + 2].opcode != OpCodes.Brfalse_S)
                    return Unsupported(original);

                bool matched = false;
                for (int j = i + 3; j + 1 < code.Count && j < i + 12; j++)
                {
                    if (!code[j].Calls(getActiveSelf)
                        || (code[j + 1].opcode != OpCodes.Brtrue && code[j + 1].opcode != OpCodes.Brtrue_S))
                        continue;

                    // Append "&& AllowMinimapZoom()" after the original activeSelf test.
                    conditions.Add((j + 2, code[j + 1].operand));
                    matched = true;
                    break;
                }

                if (!matched)
                    return Unsupported(original);
            }

            if (conditions.Count != 2 || conditions[0].index == conditions[1].index)
                return Unsupported(original);

            for (int i = conditions.Count - 1; i >= 0; i--)
            {
                (int index, object target) = conditions[i];
                code.Insert(index, new CodeInstruction(OpCodes.Call, allowZoom));
                code.Insert(index + 1, new CodeInstruction(OpCodes.Brfalse, target));
            }

            return code;
        }

        private static IEnumerable<CodeInstruction> Unsupported(List<CodeInstruction> original)
        {
            ExtraSlots.LogWarning("Cannot patch minimap zoom for hotbar row switching.");
            return original;
        }
    }

    // Positive values use player-facing row numbers; negative values count from the end.
    private static int ResolveRow(int configuredRow, int height) =>
        Mathf.Clamp(configuredRow < 0 ? height + configuredRow : Math.Max(0, configuredRow - 1), 0, height - 1);

    private static int GetRowForInventory(Inventory inventory) =>
        ReferenceEquals(inventory, Player.m_localPlayer?.GetInventory()) ? CurrentRow : 0;

    private static int GetRowForPlayer(Player player) =>
        ReferenceEquals(player, Player.m_localPlayer) ? CurrentRow : 0;

    private static bool ConsumingMouseWheel()
    {
        if (readingScroll || !ExtraSlots.vanillaHotbarRowsEnabled.Value
            || ExtraSlots.vanillaHotbarRowMode.Value != RowMode.Persistent
            || !ExtraSlots.vanillaHotbarScrollToSwitch.Value)
            return false;

        Player player = Player.m_localPlayer;
        return player && player.TakeInput() && !player.InPlaceMode() && !GameCamera.InFreeFly()
            && PreventSimilarHotkeys.IsShortcutPressed(ExtraSlots.vanillaHotbarRowModifier.Value);
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    private static class ZInput_GetMouseScrollWheel_SwitchHotbarRow
    {
        private static void Postfix(ref float __result)
        {
            if (__result != 0f && ConsumingMouseWheel())
                __result = 0f;
        }
    }


    // Keep the native LINQ filtering and element layout. Select the player's current
    // hotbar row in both GetHotbar paths (including empty positions).
    private static readonly Func<ItemDrop.ItemData, bool> selectedHotbarRowFilter =
        item => item.m_gridPos.y == CurrentRow;

    private static IEnumerable<ItemDrop.ItemData> FilterHotbarRow(
        IEnumerable<ItemDrop.ItemData> items, Func<ItemDrop.ItemData, bool> originalFilter)
    {
        if (CurrentRow != 0 && ReferenceEquals(items, Player.m_localPlayer?.GetInventory()?.m_inventory))
            return items.Where(selectedHotbarRowFilter);

        return items.Where(originalFilter);
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetHotbar))]
    private static class Inventory_GetHotbar_SelectedRow
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> original = instructions.ToList();
            List<CodeInstruction> code = original.Select(instruction => new CodeInstruction(instruction)).ToList();
            MethodInfo filterItems = AccessTools.Method(typeof(HotbarRows), nameof(FilterHotbarRow));
            int replaced = 0;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].operand is not MethodInfo method
                    || method.DeclaringType != typeof(Enumerable)
                    || method.Name != nameof(Enumerable.Where)
                    || !method.IsGenericMethod || method.GetGenericArguments()[0] != typeof(ItemDrop.ItemData))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2
                    || parameters[1].ParameterType != typeof(Func<ItemDrop.ItemData, bool>))
                    continue;

                code[i].opcode = OpCodes.Call;
                code[i].operand = filterItems;
                replaced++;
            }

            if (replaced == 2)
                return code;

            ExtraSlots.LogWarning($"Cannot patch GetHotbar row filtering: found {replaced} predicates.");
            return original;
        }
    }

    [HarmonyPatch(typeof(HotbarGroupConfig), nameof(HotbarGroupConfig.InitRadialConfig))]
    private static class HotbarGroupConfig_InitRadialConfig_TrackRow
    {
        private static void Postfix() => renderedRadialRow = CurrentRow;
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.Update))]
    private static class RadialBase_Update_SelectedHotbarRow
    {
        private static void Prefix(RadialBase __instance)
        {
            if (__instance.Active && !__instance.m_isClosing && !__instance.m_closeQueued
                && __instance.m_queuedOpenConfigs.openConfig == null
                && __instance.CurrentConfig is HotbarGroupConfig && renderedRadialRow != CurrentRow)
                __instance.Refresh();
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetBoundItems))]
    private static class Inventory_GetBoundItems_SelectedRow
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
            FieldInfo yField = AccessTools.Field(typeof(Vector2i), nameof(Vector2i.y));
            MethodInfo getRow = AccessTools.Method(typeof(HotbarRows), nameof(GetRowForInventory));
            int rowCheck = -1;

            for (int i = 0; i < code.Count - 1; i++)
            {
                if (!code[i].LoadsField(yField))
                    continue;

                if (rowCheck >= 0)
                {
                    ExtraSlots.LogWarning("Cannot patch GetBoundItems: multiple row comparisons.");
                    return code;
                }

                rowCheck = i + 1;
            }

            if (rowCheck < 0)
            {
                ExtraSlots.LogWarning("Cannot patch GetBoundItems: row comparison not found.");
                return code;
            }

            if (code[rowCheck].opcode == OpCodes.Ldc_I4_0)
            {
                code[rowCheck].opcode = OpCodes.Ldarg_0;
                code[rowCheck].operand = null;
                code.Insert(rowCheck + 1, new CodeInstruction(OpCodes.Call, getRow));
                return code;
            }

            OpCode opcode = code[rowCheck].opcode;
            if (opcode != OpCodes.Brtrue && opcode != OpCodes.Brtrue_S
                && opcode != OpCodes.Brfalse && opcode != OpCodes.Brfalse_S)
            {
                ExtraSlots.LogWarning("Cannot patch GetBoundItems: unexpected row comparison.");
                return code;
            }

            // A direct branch on y tests y != 0 (or y == 0).
            // Compare against the selected row without changing the branch target.
            CodeInstruction branch = code[rowCheck];
            branch.opcode = opcode == OpCodes.Brtrue || opcode == OpCodes.Brtrue_S
                ? opcode == OpCodes.Brtrue_S ? OpCodes.Bne_Un_S : OpCodes.Bne_Un
                : opcode == OpCodes.Brfalse_S ? OpCodes.Beq_S : OpCodes.Beq;
            CodeInstruction loadInventory = new CodeInstruction(OpCodes.Ldarg_0);
            loadInventory.labels.AddRange(branch.labels);
            branch.labels.Clear();
            loadInventory.blocks.AddRange(branch.blocks);
            branch.blocks.Clear();
            code.Insert(rowCheck, loadInventory);
            code.Insert(rowCheck + 1, new CodeInstruction(OpCodes.Call, getRow));
            return code;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    private static class Player_UseHotbarItem_SelectedRow
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
            MethodInfo getItem = AccessTools.Method(typeof(Inventory), nameof(Inventory.GetItemAt), new[] { typeof(int), typeof(int) });
            MethodInfo getRow = AccessTools.Method(typeof(HotbarRows), nameof(GetRowForPlayer));
            int rowArgument = -1;

            for (int i = 1; i < code.Count; i++)
            {
                if (!code[i].Calls(getItem))
                    continue;

                if (rowArgument >= 0 || code[i - 1].opcode != OpCodes.Ldc_I4_0)
                {
                    ExtraSlots.LogWarning("Cannot patch UseHotbarItem: unexpected inventory lookup.");
                    return code;
                }

                rowArgument = i - 1;
            }

            if (rowArgument < 0)
            {
                ExtraSlots.LogWarning("Cannot patch UseHotbarItem: inventory lookup not found.");
                return code;
            }

            code[rowArgument].opcode = OpCodes.Ldarg_0;
            code[rowArgument].operand = null;
            code.Insert(rowArgument + 1, new CodeInstruction(OpCodes.Call, getRow));
            return code;
        }
    }
}
