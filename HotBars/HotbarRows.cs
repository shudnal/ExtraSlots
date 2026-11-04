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
        }

        if (!ExtraSlots.vanillaHotbarRowsEnabled.Value)
        {
            currentRow = 0;
            selectedRow = 0;
            lastMode = ExtraSlots.vanillaHotbarRowMode.Value;
            return;
        }

        RowMode mode = ExtraSlots.vanillaHotbarRowMode.Value;
        if (lastMode != mode)
        {
            selectedRow = 0;
            lastMode = mode;
        }

        int height = InventoryHeightPlayer;
        bool allowInput = player.TakeInput() && !GameCamera.InFreeFly();

        if (mode == RowMode.Hold)
        {
            currentRow = allowInput && PreventSimilarHotkeys.IsShortcutPressed(ExtraSlots.vanillaHotbarRowModifier.Value)
                ? ResolveRow(ExtraSlots.vanillaHotbarHoldRow.Value, height)
                : 0;
            return;
        }

        selectedRow = Mathf.Clamp(selectedRow, 0, height - 1);
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


    // The game's Hotbar radial page uses GetHotbar(), which always reads row zero.
    // Keep its eight original positions while taking items from the selected row.
    private static List<ItemDrop.ItemData> GetRadialHotbarItems(Inventory inventory, bool includeEmpty)
    {
        int row = GetRowForInventory(inventory);
        if (row == 0)
            return inventory.GetHotbar(includeEmpty);

        List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(8);
        for (int x = 0; x < 8; x++)
        {
            ItemDrop.ItemData item = inventory.GetItemAt(x, row);
            if (item != null || includeEmpty)
                items.Add(item);
        }
        return items;
    }

    [HarmonyPatch(typeof(HotbarGroupConfig), nameof(HotbarGroupConfig.InitRadialConfig))]
    private static class HotbarGroupConfig_InitRadialConfig_SelectedRow
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
            MethodInfo vanillaGetHotbar = AccessTools.Method(typeof(Inventory), nameof(Inventory.GetHotbar), new[] { typeof(bool) });
            MethodInfo getRadialItems = AccessTools.Method(typeof(HotbarRows), nameof(GetRadialHotbarItems));
            int index = code.FindIndex(instruction => instruction.Calls(vanillaGetHotbar));
            if (index < 0 || code.FindIndex(index + 1, instruction => instruction.Calls(vanillaGetHotbar)) >= 0)
            {
                ExtraSlots.LogWarning("Cannot patch radial hotbar: unexpected inventory lookup.");
                return code;
            }

            code[index].opcode = OpCodes.Call;
            code[index].operand = getRadialItems;
            return code;
        }

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
