using System;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots.Compatibility;

internal static class InventoryMigration
{
    internal static int ImportMissingItemsToDeferred(
        Player player,
        IEnumerable<ItemDrop.ItemData> sourceItems,
        string sourceName,
        Func<ItemDrop.ItemData, string> preferredSlot = null,
        Func<ItemDrop.ItemData, bool> restoreEquipped = null)
    {
        return ImportMissingItemsToDeferred(player, sourceItems, sourceName, preferredSlot, restoreEquipped, out _);
    }

    internal static int ImportMissingItemsToDeferred(
        Player player,
        IEnumerable<ItemDrop.ItemData> sourceItems,
        string sourceName,
        Func<ItemDrop.ItemData, string> preferredSlot,
        Func<ItemDrop.ItemData, bool> restoreEquipped,
        out bool allRepresented)
    {
        allRepresented = true;
        if (player == null || sourceItems == null)
        {
            allRepresented = false;
            return 0;
        }

        Dictionary<string, int> available = new Dictionary<string, int>(StringComparer.Ordinal);
        IEnumerable<ItemDrop.ItemData> playerItems = player.GetInventory()?.m_inventory ?? new List<ItemDrop.ItemData>();
        foreach (ItemDrop.ItemData item in playerItems)
            AddAvailable(DeferredInventory.GetMigrationKey(item));
        foreach (string key in DeferredInventory.GetMaterializedMigrationKeys(player))
            AddAvailable(key);

        int imported = 0;
        foreach (ItemDrop.ItemData item in sourceItems.Where(item => item != null))
        {
            string key = DeferredInventory.GetMigrationKey(item);
            if (!string.IsNullOrEmpty(key) && available.TryGetValue(key, out int count) && count > 0)
            {
                available[key] = count - 1;
                continue;
            }

            string slotId = preferredSlot?.Invoke(item);
            bool shouldRestoreEquipped = restoreEquipped?.Invoke(item) ?? item.m_equipped;
            if (!DeferredInventory.EnqueueDetached(player, item, slotId, shouldRestoreEquipped, $"imported from {sourceName}"))
            {
                allRepresented = false;
                continue;
            }

            imported++;
        }

        if (imported > 0)
            LogMessage($"Imported {imported} missing item(s) from {sourceName} into deferred inventory.");

        return imported;

        void AddAvailable(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            available.TryGetValue(key, out int count);
            available[key] = count + 1;
        }
    }

    internal static bool TryLoadInventoryPackage(string base64, string name, int width, int height, out Inventory inventory)
    {
        inventory = null;
        if (string.IsNullOrWhiteSpace(base64) || width <= 0 || height <= 0)
            return false;

        try
        {
            inventory = new Inventory(name, null, width, height);
            inventory.Load(new ZPackage(base64).ReadCompressedPackage());
            return true;
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to read {name} inventory package:\n{ex}");
            inventory = null;
            return false;
        }
    }
}
