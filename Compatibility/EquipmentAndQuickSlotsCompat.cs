using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility;

internal class EquipmentAndQuickSlotsCompat
{
    public const int QuickSlotCount = 3;
    public const int EquipSlotCount = 5;

    public static Inventory QuickSlotInventory = new Inventory(nameof(QuickSlotInventory), null, QuickSlotCount, 1);
    public static Inventory EquipmentSlotInventory = new Inventory(nameof(EquipmentSlotInventory), null, EquipSlotCount, 1);

    internal static Player playerToLoad;

    public const string Sentinel = "<|>";

    private const string EaQSPlayerKey = "eaqs_player";
    private const string EaQSSlotKey = "eaqs_slot";
    private const string EaQSBackupKey = "eaqs_backup";
    private const string EaQSBackupMigrationKey = "ExtraSlotsMigrationEaQSBackup";

    internal static void ApplyCurrentSlotMetadata(Player player, ItemDrop.ItemData item)
    {
        if (player == null || item == null || item.m_customData.ContainsKey(customKeySlotID))
            return;

        if (!item.m_customData.TryGetValue(EaQSSlotKey, out string eaqsSlotId) || string.IsNullOrEmpty(eaqsSlotId))
            return;

        if (item.m_customData.TryGetValue(EaQSPlayerKey, out string eaqsPlayer)
            && !string.IsNullOrEmpty(eaqsPlayer)
            && eaqsPlayer != player.GetPlayerID().ToString())
        {
            return;
        }

        string mappedSlotId = MapCurrentSlotId(eaqsSlotId);
        if (string.IsNullOrEmpty(mappedSlotId))
            return;

        item.m_customData[customKeyPlayerID] = player.GetPlayerID().ToString();
        item.m_customData[customKeySlotID] = mappedSlotId;
    }

    private static string MapCurrentSlotId(string eaqsSlotId)
    {
        if (string.IsNullOrEmpty(eaqsSlotId))
            return null;

        if (eaqsSlotId.Equals("Helmet", StringComparison.OrdinalIgnoreCase)) return helmetSlotID;
        if (eaqsSlotId.Equals("Chest", StringComparison.OrdinalIgnoreCase)) return chestSlotID;
        if (eaqsSlotId.Equals("Legs", StringComparison.OrdinalIgnoreCase)) return legsSlotID;
        if (eaqsSlotId.Equals("Shoulder", StringComparison.OrdinalIgnoreCase)) return shoulderSlotID;
        if (eaqsSlotId.Equals("Utility", StringComparison.OrdinalIgnoreCase)) return utilitySlotID;
        if (eaqsSlotId.Equals("Trinket", StringComparison.OrdinalIgnoreCase)) return trinketSlotID;
        if (eaqsSlotId.Equals("Utility2", StringComparison.OrdinalIgnoreCase)) return $"{extraUtilitySlotID}1";
        if (eaqsSlotId.Equals("Utility3", StringComparison.OrdinalIgnoreCase)) return $"{extraUtilitySlotID}2";

        for (int i = 1; i <= 6; i++)
            if (eaqsSlotId.Equals($"Quick{i}", StringComparison.OrdinalIgnoreCase))
                return $"{quickSlotID}{i}";

        Slot matchingSlot = API.FindSlot(eaqsSlotId);
        return matchingSlot?.ID;
    }

    private static string GetLegacyPreferredSlot(string inventoryName, ItemDrop.ItemData item)
    {
        int index = item?.m_gridPos.x ?? -1;
        if (inventoryName == nameof(QuickSlotInventory) && index >= 0 && index < QuickSlotCount)
            return $"{quickSlotID}{index + 1}";

        if (inventoryName != nameof(EquipmentSlotInventory))
            return null;

        return index switch
        {
            0 => helmetSlotID,
            1 => chestSlotID,
            2 => legsSlotID,
            3 => shoulderSlotID,
            4 => utilitySlotID,
            _ => null
        };
    }

    private static void ApplyPreferredSlotMetadata(Player player, ItemDrop.ItemData item, string slotId)
    {
        if (player == null || item == null || string.IsNullOrEmpty(slotId))
            return;

        item.m_customData[customKeyPlayerID] = player.GetPlayerID().ToString();
        item.m_customData[customKeySlotID] = slotId;
    }

    public static void Load()
    {
        if (playerToLoad == null)
            return;

        MigrateLegacyInventory(nameof(EquipmentSlotInventory), EquipSlotCount, equipItems: true);
        MigrateLegacyInventory(nameof(QuickSlotInventory), QuickSlotCount, equipItems: false);
    }

    private static bool MigrateLegacyInventory(string key, int slotCount, bool equipItems)
    {
        if (!LoadValue(playerToLoad, key, out string data) || string.IsNullOrEmpty(data))
            return true;

        int expectedItems;
        Inventory legacyInventory = new Inventory(key, null, slotCount, 1);
        try
        {
            // Inventory.Save begins with its format version followed by the serialized item count.
            // Read that count independently so an unavailable prefab can never make us delete a
            // legacy payload that still contains an item Inventory.Load could not materialize.
            ZPackage header = new ZPackage(data);
            _ = header.ReadInt();
            expectedItems = header.ReadInt();
            legacyInventory.Load(new ZPackage(data));
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to parse legacy EaQS {key}. Source data will be left untouched:\n{ex}");
            return false;
        }

        List<ItemDrop.ItemData> items = legacyInventory.GetAllItemsInGridOrder().Where(item => item != null).ToList();

        // Character-select preview used the legacy side inventories before there was a local player.
        // Preserve that behavior without consuming or rewriting the source payload: project clones
        // into the preview inventory only, then let the real in-world load perform durable migration.
        if (Player.m_localPlayer == null)
        {
            foreach (ItemDrop.ItemData sourceItem in items)
            {
                ItemDrop.ItemData previewItem = sourceItem.Clone();
                bool restoreEquipped = equipItems || sourceItem.m_equipped;
                previewItem.m_equipped = false;
                string preferredSlotId = GetLegacyPreferredSlot(key, sourceItem);
                ApplyPreferredSlotMetadata(playerToLoad, previewItem, preferredSlotId);

                if (PlayerInventoryOperations.TryInsertDetachedToBestAvailable(
                    previewItem,
                    preferredSlotId,
                    restoreEquipped,
                    out ItemDrop.ItemData placedItem,
                    out bool fullyInserted,
                    out _)
                    && fullyInserted
                    && restoreEquipped
                    && placedItem != null
                    && placedItem.IsEquipable())
                {
                    playerToLoad.EquipItem(placedItem, triggerEquipEffects: false);
                }
            }

            ItemsSlotsValidation.ValidateItems();
            ItemsSlotsValidation.ValidateSlots();
            return false;
        }

        int imported = InventoryMigration.ImportMissingItemsToDeferred(
            playerToLoad,
            items,
            $"legacy EaQS {key}",
            item => GetLegacyPreferredSlot(key, item),
            item => equipItems || item.m_equipped,
            out bool allRepresented);

        bool allMaterialized = items.Count == expectedItems;
        if (allMaterialized && allRepresented)
        {
            RemoveLegacyKey(playerToLoad, key);
            LogMessage($"Legacy EaQS {key} was fully adopted by ExtraSlots ({items.Count} item(s), {imported} newly deferred).");
        }
        else if (!allMaterialized)
        {
            LogWarning($"Legacy EaQS {key} materialized {items.Count}/{expectedItems} item(s). Source data will remain intact so unavailable-prefab items can be recovered later.");
        }
        else
        {
            LogWarning($"Legacy EaQS {key} could not be fully adopted. Source data will remain intact for a later retry.");
        }

        if (imported > 0)
        {
            ItemsSlotsValidation.ValidateItems();
            ItemsSlotsValidation.ValidateSlots();
        }

        return allMaterialized && allRepresented;
    }

    private static bool LoadValue(Player player, string key, out string value)
    {
        if (player.m_customData.TryGetValue(key, out value))
            return true;
        if (player.m_knownTexts.TryGetValue(key, out value))
            return true;
        return player.m_knownTexts.TryGetValue(Sentinel + key, out value);
    }

    private static bool HasLegacyData(Player player, string key) =>
        player.m_customData.ContainsKey(key)
        || player.m_knownTexts.ContainsKey(key)
        || player.m_knownTexts.ContainsKey(Sentinel + key);

    private static void RemoveLegacyKey(Player player, string key)
    {
        player.m_customData.Remove(key);
        player.m_knownTexts.Remove(key);
        player.m_knownTexts.Remove(Sentinel + key);
    }

    private static void ApplyCurrentInventoryMetadata(Player player)
    {
        if (player?.GetInventory()?.m_inventory == null)
            return;

        foreach (ItemDrop.ItemData item in player.GetInventory().m_inventory)
            ApplyCurrentSlotMetadata(player, item);
    }

    private static void ImportCurrentBackup(Player player)
    {
        if (player == null || !player.m_customData.TryGetValue(EaQSBackupKey, out string payload) || string.IsNullOrWhiteSpace(payload))
            return;

        string sourceFingerprint = $"{payload.Length}:{payload.GetStableHashCode()}";
        if (player.m_customData.TryGetValue(EaQSBackupMigrationKey, out string migratedFingerprint)
            && migratedFingerprint == sourceFingerprint)
        {
            return;
        }

        try
        {
            ZPackage envelope = new ZPackage(payload);
            int version = envelope.ReadInt();
            if (version != 1)
            {
                LogWarning($"Unsupported EaQS backup envelope version {version}. Backup will be left untouched.");
                return;
            }

            string date = envelope.ReadString();
            string worldName = envelope.ReadString();
            int expectedItems = envelope.ReadInt();
            int width = envelope.ReadInt();
            int height = envelope.ReadInt();
            _ = envelope.ReadInt(); // visibleRows, only needed by EaQS itself
            ZPackage inventoryPackage = envelope.ReadCompressedPackage();

            Inventory backup = new Inventory(EaQSBackupKey, null, width, height);
            backup.Load(inventoryPackage);
            List<ItemDrop.ItemData> backupItems = backup.GetAllItemsInGridOrder().Where(item => item != null).ToList();
            foreach (ItemDrop.ItemData item in backupItems)
                ApplyCurrentSlotMetadata(player, item);

            int imported = InventoryMigration.ImportMissingItemsToDeferred(
                player,
                backupItems,
                $"EaQS 3.x backup ({date}, {worldName})",
                item => item.m_customData.TryGetValue(customKeySlotID, out string slotId) ? slotId : null,
                item => item.m_equipped,
                out bool allRepresented);

            if (backupItems.Count == expectedItems && allRepresented)
                player.m_customData[EaQSBackupMigrationKey] = sourceFingerprint;
            else if (backupItems.Count != expectedItems)
                LogWarning($"EaQS backup materialized {backupItems.Count}/{expectedItems} item(s). Source will remain eligible for retry so unavailable-prefab items are not forgotten.");
            else
                LogWarning("EaQS backup could not be fully adopted. Source will remain eligible for retry.");

            if (imported > 0)
            {
                ItemsSlotsValidation.ValidateItems();
                ItemsSlotsValidation.ValidateSlots();
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to read EaQS 3.x backup. Source data will be left untouched:\n{ex}");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    public static class Player_Load_MigrateEaQSData
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Player __instance)
        {
            if (__instance == null || (!FejdStartup.instance && !IsValidPlayer(__instance)))
                return;

            playerToLoad = __instance;
            try
            {
                ApplyCurrentInventoryMetadata(__instance);
                PlayerInventoryOperations.ReconcileLoadedTopology();

                if (HasLegacyData(__instance, nameof(EquipmentSlotInventory)) || HasLegacyData(__instance, nameof(QuickSlotInventory)))
                    Load();

                ImportCurrentBackup(__instance);
            }
            finally
            {
                playerToLoad = null;
            }
        }
    }
}
