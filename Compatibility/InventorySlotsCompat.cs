using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility;

internal static class InventorySlotsCompat
{
    private const string SlotIdKey = "InventorySlotsSlotId";
    private const string EquippedByKey = "InventorySlotsEquippedBy";
    private const string BackupKey = "InventorySlotsBackup";
    private const string BackupMigrationKey = "ExtraSlotsMigrationInventorySlotsBackup";

    internal static void ApplyCurrentSlotMetadata(Player player, ItemDrop.ItemData item)
    {
        if (player == null || item == null || item.m_customData.ContainsKey(customKeySlotID))
            return;

        if (!item.m_customData.TryGetValue(SlotIdKey, out string sourceSlotId) || string.IsNullOrWhiteSpace(sourceSlotId))
            return;

        if (item.m_customData.TryGetValue(EquippedByKey, out string equippedBy)
            && !string.IsNullOrEmpty(equippedBy)
            && equippedBy != player.GetPlayerID().ToString())
        {
            return;
        }

        string mappedSlotId = MapSlotId(sourceSlotId);
        if (string.IsNullOrEmpty(mappedSlotId))
            return;

        item.m_customData[customKeyPlayerID] = player.GetPlayerID().ToString();
        item.m_customData[customKeySlotID] = mappedSlotId;
    }

    private static string MapSlotId(string sourceSlotId)
    {
        if (sourceSlotId.Equals("helmet", StringComparison.OrdinalIgnoreCase)) return helmetSlotID;
        if (sourceSlotId.Equals("chest", StringComparison.OrdinalIgnoreCase)) return chestSlotID;
        if (sourceSlotId.Equals("legs", StringComparison.OrdinalIgnoreCase)) return legsSlotID;
        if (sourceSlotId.Equals("cape", StringComparison.OrdinalIgnoreCase)) return shoulderSlotID;
        if (sourceSlotId.Equals("utility", StringComparison.OrdinalIgnoreCase)) return utilitySlotID;
        if (sourceSlotId.Equals("trinket", StringComparison.OrdinalIgnoreCase)) return trinketSlotID;

        Slot matchingSlot = API.FindSlot(sourceSlotId);
        return matchingSlot?.ID;
    }

    private static bool TryGetYamlScalar(string yaml, string key, out string value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(yaml))
            return false;

        string prefix = key + ":";
        foreach (string rawLine in yaml.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            value = line.Substring(prefix.Length).Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\'')))
                value = value.Substring(1, value.Length - 2);
            return true;
        }

        return false;
    }

    private static bool TryGetYamlInt(string yaml, string key, out int value)
    {
        value = 0;
        return TryGetYamlScalar(yaml, key, out string text) && int.TryParse(text, out value);
    }

    private static void ApplyCurrentInventoryMetadata(Player player)
    {
        if (player?.GetInventory()?.m_inventory == null)
            return;

        foreach (ItemDrop.ItemData item in player.GetInventory().m_inventory)
            ApplyCurrentSlotMetadata(player, item);
    }

    private static void ImportBackup(Player player)
    {
        if (player == null || !player.m_customData.TryGetValue(BackupKey, out string yaml) || string.IsNullOrWhiteSpace(yaml))
            return;

        string sourceFingerprint = $"{yaml.Length}:{yaml.GetStableHashCode()}";
        if (player.m_customData.TryGetValue(BackupMigrationKey, out string migratedFingerprint)
            && migratedFingerprint == sourceFingerprint)
        {
            return;
        }

        if (!TryGetYamlInt(yaml, "version", out int version) || version != 1
            || !TryGetYamlInt(yaml, "nrOfItems", out int expectedItems)
            || !TryGetYamlInt(yaml, "width", out int width)
            || !TryGetYamlInt(yaml, "height", out int height)
            || !TryGetYamlScalar(yaml, "inventoryBase64", out string inventoryBase64))
        {
            LogWarning("InventorySlots backup metadata could not be parsed. Source data will be left untouched.");
            return;
        }

        if (!InventoryMigration.TryLoadInventoryPackage(inventoryBase64, BackupKey, width, height, out Inventory backup))
            return;

        List<ItemDrop.ItemData> items = backup.GetAllItemsInGridOrder().Where(item => item != null).ToList();
        foreach (ItemDrop.ItemData item in items)
            ApplyCurrentSlotMetadata(player, item);

        int imported = InventoryMigration.ImportMissingItemsToDeferred(
            player,
            items,
            "InventorySlots backup",
            item => item.m_customData.TryGetValue(customKeySlotID, out string slotId) ? slotId : null,
            item => item.m_equipped,
            out bool allRepresented);

        if (items.Count == expectedItems && allRepresented)
            player.m_customData[BackupMigrationKey] = sourceFingerprint;
        else if (items.Count != expectedItems)
            LogWarning($"InventorySlots backup materialized {items.Count}/{expectedItems} item(s). Source will remain eligible for retry so unavailable-prefab items are not forgotten.");
        else
            LogWarning("InventorySlots backup could not be fully adopted. Source will remain eligible for retry.");

        if (imported > 0)
        {
            ItemsSlotsValidation.ValidateItems();
            ItemsSlotsValidation.ValidateSlots();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    private static class Player_Load_ImportInventorySlotsState
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance)
        {
            if (__instance == null || (!FejdStartup.instance && !IsValidPlayer(__instance)))
                return;

            ApplyCurrentInventoryMetadata(__instance);
            PlayerInventoryOperations.ReconcileLoadedTopology();
            ImportBackup(__instance);
        }
    }
}
