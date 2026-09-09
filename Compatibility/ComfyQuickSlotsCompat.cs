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
            // Comfy stores a complete vanilla Inventory.Save package. Read the serialized count
            // independently so the source is consumed only after every entry was materialized and
            // represented by either the live inventory or ExtraSlots deferred storage.
            ZPackage header = new ZPackage(data);
            _ = header.ReadInt();
            int expectedItems = header.ReadInt();

            Inventory snapshot = new Inventory(SnapshotKey, null, Width, Height);
            snapshot.Load(new ZPackage(data));
            List<ItemDrop.ItemData> items = snapshot.GetAllItemsInGridOrder().Where(item => item != null).ToList();

            int imported = InventoryMigration.ImportMissingItemsToDeferred(
                player,
                items,
                "ComfyQuickSlots snapshot",
                GetPreferredSlot,
                item => item.m_equipped,
                out bool allRepresented);

            bool allMaterialized = items.Count == expectedItems;
            if (allMaterialized && allRepresented)
            {
                // Migration is intentionally one-way. Remove both possible storage locations so a
                // stale Comfy snapshot cannot resurrect an item after it was later consumed/dropped.
                player.m_knownTexts.Remove(SnapshotKey);
                player.m_customData.Remove(SnapshotKey);
                LogMessage($"ComfyQuickSlots snapshot was fully adopted by ExtraSlots ({items.Count} item(s), {imported} newly deferred) and removed from the character.");
            }
            else if (!allMaterialized)
            {
                LogWarning($"ComfyQuickSlots snapshot materialized {items.Count}/{expectedItems} item(s). Source data will remain intact so unavailable-prefab items can be recovered later.");
            }
            else
            {
                LogWarning("ComfyQuickSlots snapshot could not be fully adopted. Source data will remain intact for a later retry.");
            }

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
