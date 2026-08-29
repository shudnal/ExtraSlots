using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility;

internal static class ComfyQuickSlotsCompat
{
    private const string SnapshotKey = "ComfyQuickSlotsInventory";
    private const int Width = 8;
    private const int Height = 5;
    private const int SpecialRow = 4;

    private static string GetPreferredSlot(ItemDrop.ItemData item)
    {
        if (item == null || item.m_gridPos.y != SpecialRow)
            return null;

        return item.m_gridPos.x switch
        {
            0 => helmetSlotID,
            1 => chestSlotID,
            2 => legsSlotID,
            3 => shoulderSlotID,
            4 => utilitySlotID,
            5 => $"{quickSlotID}1",
            6 => $"{quickSlotID}2",
            7 => $"{quickSlotID}3",
            _ => null
        };
    }

    private static bool TryGetSnapshot(Player player, out string data)
    {
        data = null;
        if (player == null)
            return false;

        if (player.m_customData.TryGetValue(SnapshotKey, out data) && !string.IsNullOrWhiteSpace(data))
            return true;

        return player.m_knownTexts.TryGetValue(SnapshotKey, out data) && !string.IsNullOrWhiteSpace(data);
    }

    private static void ImportSnapshot(Player player)
    {
        if (!TryGetSnapshot(player, out string data))
            return;

        try
        {
            Inventory snapshot = new Inventory(SnapshotKey, null, Width, Height);
            snapshot.Load(new ZPackage(data));
            List<ItemDrop.ItemData> items = snapshot.GetAllItemsInGridOrder().Where(item => item != null).ToList();
            if (items.Count == 0)
                return;

            int imported = InventoryMigration.ImportMissingItemsToDeferred(
                player,
                items,
                "ComfyQuickSlots snapshot",
                GetPreferredSlot,
                item => item.m_equipped);

            if (imported > 0)
            {
                ItemsSlotsValidation.ValidateItems();
                ItemsSlotsValidation.ValidateSlots();
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to read ComfyQuickSlots snapshot. Source data will be left untouched:\n{ex}");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    private static class Player_Load_ImportComfyQuickSlotsSnapshot
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance)
        {
            if (__instance == null || (!FejdStartup.instance && !IsValidPlayer(__instance)))
                return;

            ImportSnapshot(__instance);
        }
    }
}
