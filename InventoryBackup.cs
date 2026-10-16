using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    public static class InventoryBackup
    {
        [Serializable]
        public class ExtraSlotsBackup
        {
            public string date;
            public string worldName;
            public int nrOfItems;
            public int width;
            public int height;
            public int extraRows;
            public string inventoryBase64;

            // Version 2 keeps one immutable snapshot and tracks its consumed record indices.
            // The version 1 pendingInventories field is deliberately not deserialized: it can
            // mix several older snapshots, and cannot identify a single latest generation.
            public int recoveryVersion;
            public int[] consumedRecordIndices;
        }

        public const string customKeyBackupID = "ExtraSlotsInventoryBackup";

        private const int RecoveryVersion = 2;

        // Recovery progress belongs only to the current snapshot. A new save replaces both.
        private static readonly HashSet<Player> recoveringPlayers = new HashSet<Player>();

        internal static bool IsRecovering(Player player) => player != null && recoveringPlayers.Contains(player);

        private static ExtraSlotsBackup GetExtraSlotsBackup(Inventory inventory)
        {
            int width = InventoryWidth;
            int height = InventoryHeightFull - InventoryHeightPlayer;
            Inventory backup = new Inventory(customKeyBackupID, null, width, height);

            foreach (ItemDrop.ItemData item in inventory.GetAllItemsInGridOrder().Where(item => item.m_gridPos.y >= InventoryHeightPlayer))
            {
                ItemDrop.ItemData backupItem = item.Clone();

                // A backup is a snapshot, not an inventory transfer. AddItem would merge separate
                // source stacks and change their identity, causing false "missing item" recovery.
                backupItem.m_gridPos = new Vector2i(backupItem.m_gridPos.x, backupItem.m_gridPos.y - InventoryHeightPlayer);
                backup.m_inventory.Add(backupItem);
            }

            ZPackage pkg = new ZPackage();
            backup.Save(pkg);

            ZPackage compressed = new ZPackage();
            compressed.WriteCompressed(pkg);

            ExtraSlotsBackup extraSlotsBackup = new ExtraSlotsBackup { 
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), 
                worldName = ZNet.instance?.GetWorldName(), 
                nrOfItems = backup.NrOfItems(), 
                width = width, 
                height = height,
                extraRows = ExtraRowsPlayer,
                inventoryBase64 = compressed.GetBase64(),
                recoveryVersion = RecoveryVersion,
                consumedRecordIndices = Array.Empty<int>()
            };

            LogMessage($"Extra slots backup saved {extraSlotsBackup.date}, world {extraSlotsBackup.worldName}, items {extraSlotsBackup.nrOfItems}, size {(float)pkg.Size() / 1000:f1} kb, compressed {(float)compressed.Size() / 1000:f1} kb");

            return extraSlotsBackup;
        }

        private static bool TryGetBackup(Player player, out ExtraSlotsBackup extraSlotsBackup)
        {
            extraSlotsBackup = null;

            if (!player.m_customData.TryGetValue(customKeyBackupID, out string json))
                return false;

            try
            {
                extraSlotsBackup = JsonUtility.FromJson<ExtraSlotsBackup>(json);
            }
            catch (Exception ex)
            {
                LogWarning($"Error while checking inventory backup:\n{ex}");
                return false;
            }

            if (extraSlotsBackup == null || extraSlotsBackup.recoveryVersion < 0
                || extraSlotsBackup.recoveryVersion > RecoveryVersion
                || string.IsNullOrEmpty(extraSlotsBackup.inventoryBase64))
            {
                LogWarning("ExtraSlots cannot read the latest backup snapshot or its recovery metadata. Older backup packages will not be loaded.");
                return false;
            }

            if (extraSlotsBackup.recoveryVersion < RecoveryVersion)
                extraSlotsBackup.consumedRecordIndices = Array.Empty<int>();
            return true;
        }

        private static bool PlayerCanRestoreBackup(Player player, out ExtraSlotsBackup extraSlotsBackup)
        {
            extraSlotsBackup = null;

            // An authoritative manager may have replaced only the inventory since the backup
            // was written. Never resurrect removed items from that older duplicate snapshot.
            // ServerManager's backup-only mode still uses the ordinary local recovery policy.
            if (HasServerCharactersActive || Compatibility.ServerManagerCompat.IsAuthoritativeProfile
                || player == null || !player.m_customData.ContainsKey(customKeyBackupID))
                return false;

            return TryGetBackup(player, out extraSlotsBackup);
        }

        private static bool IsCharacterPreview() => FejdStartup.instance != null && Player.m_localPlayer == null;

        private static string GetSourceSlotId(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null
                && item.m_customData.TryGetValue(customKeySlotID, out string slotId)
                && !string.IsNullOrEmpty(slotId))
            {
                return slotId;
            }

            return null;
        }

        private static string GetPersistedSlotId(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null)
                return null;

            // Physical placement is not ownership provenance: topology reconciliation may move an
            // unrelated regular item into a free ExtraSlots cell. Read the saved return address
            // directly instead of resolving it through today's slot topology, because the original
            // slot may currently be disabled or no longer registered while the provenance is valid.
            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerId)
                && item.m_customData.TryGetValue(customKeySlotID, out string slotId)
                && !string.IsNullOrEmpty(slotId)
                && playerId == CurrentPlayerProfile?.GetPlayerID().ToString())
            {
                return slotId;
            }

            return null;
        }

        private static bool SlotBackedRepresentationMatchesSource(
            ItemDrop.ItemData sourceItem,
            ItemDrop.ItemData playerItem,
            string deferredPreferredSlotId)
        {
            string sourceSlotId = GetSourceSlotId(sourceItem);

            // Old backups may not contain slot metadata. Preserve the conservative fallback only for
            // candidates that themselves have persisted/deferred slot provenance; current physical
            // placement alone is deliberately not enough evidence of ownership.
            if (string.IsNullOrEmpty(sourceSlotId))
                return playerItem != null ? !string.IsNullOrEmpty(GetPersistedSlotId(playerItem)) : !string.IsNullOrEmpty(deferredPreferredSlotId);

            if (playerItem != null)
                return string.Equals(GetPersistedSlotId(playerItem), sourceSlotId, StringComparison.Ordinal);

            return string.Equals(deferredPreferredSlotId, sourceSlotId, StringComparison.Ordinal);
        }

        private static int ProjectBackupToCharacterPreview(Player player, Inventory inventory, IReadOnlyList<ItemDrop.ItemData> backupItems)
        {
            if (player == null || inventory?.m_inventory == null || backupItems == null || !IsCharacterPreview())
                return 0;

            // Character selection uses an ephemeral Player. Show the recovery result immediately for
            // reassurance, but never mutate deferred storage or consume/mark the durable backup here.
            // The real world Player will perform the authoritative deferred adoption after SetLocalPlayer.
            List<ItemDrop.ItemData> represented = inventory.m_inventory
                .Where(existing => !string.IsNullOrEmpty(GetPersistedSlotId(existing)))
                .ToList();

            List<ItemDrop.ItemData> itemsToEquip = new List<ItemDrop.ItemData>();
            int projected = 0;

            foreach (ItemDrop.ItemData backupItem in backupItems)
            {
                if (backupItem == null)
                    continue;

                string key = DeferredInventory.GetMigrationKey(backupItem);
                string storedKey = DeferredInventory.GetMigrationKey(InventorySerialization.GetSavedRepresentation(backupItem));
                int representedIndex = represented.FindIndex(existing =>
                    (string.Equals(DeferredInventory.GetMigrationKey(existing), key, StringComparison.Ordinal)
                        || string.Equals(DeferredInventory.GetMigrationKey(existing), storedKey, StringComparison.Ordinal))
                    && SlotBackedRepresentationMatchesSource(backupItem, existing, null));
                if (representedIndex >= 0)
                {
                    represented.RemoveAt(representedIndex);
                    continue;
                }

                ItemDrop.ItemData previewItem = backupItem.Clone();
                bool shouldEquip = previewItem.m_equipped;
                previewItem.m_equipped = false;

                Vector2i target = GetPreviewTarget(inventory, previewItem, backupItem.m_gridPos);
                previewItem.m_gridPos = target;
                inventory.m_inventory.Add(previewItem);
                projected++;

                if (shouldEquip && previewItem.IsEquipable())
                    itemsToEquip.Add(previewItem);
            }

            if (projected == 0)
                return 0;

            inventory.Changed();
            foreach (ItemDrop.ItemData item in itemsToEquip)
            {
                try
                {
                    if (!player.EquipItem(item, triggerEquipEffects: false))
                        item.m_equipped = false;
                }
                catch (Exception ex)
                {
                    item.m_equipped = false;
                    LogWarning($"Backup preview item {item.m_shared?.m_name ?? item.m_dropPrefab?.name ?? "<unknown>"} could not be equipped:\n{ex}");
                }
            }

            return projected;
        }

        private static Vector2i GetPreviewTarget(Inventory inventory, ItemDrop.ItemData item, Vector2i backupPosition)
        {
            Vector2i target = new Vector2i(backupPosition.x, backupPosition.y + InventoryHeightPlayer);

            if (item.m_customData.TryGetValue(customKeySlotID, out string slotId)
                && API.FindSlot(slotId) is Slot savedSlot)
            {
                target = savedSlot.GridPosition;
            }

            EnsurePreviewCellExists(inventory, target);
            if (inventory.GetItemAt(target.x, target.y) == null)
                return target;

            for (int y = 0; y < inventory.m_height; y++)
                for (int x = 0; x < inventory.m_width; x++)
                    if (inventory.GetItemAt(x, y) == null)
                        return new Vector2i(x, y);

            int appendedRow = inventory.m_height;
            inventory.m_height++;
            return new Vector2i(0, appendedRow);
        }

        private static void EnsurePreviewCellExists(Inventory inventory, Vector2i target)
        {
            if (target.x >= 0 && target.x < inventory.m_width && target.y >= 0)
                inventory.m_height = Math.Max(inventory.m_height, target.y + 1);
        }

        private static List<InventorySerialization.RecoveryRecord> ReadRecoveryRecords(ExtraSlotsBackup backup)
        {
            try
            {
                // Even an empty snapshot is authoritative. Never fall back to legacy pending
                // packages, combine generations, or reinterpret older records as extra items.
                return InventorySerialization.ReadRecoveryRecords(backup.inventoryBase64);
            }
            catch (Exception ex)
            {
                LogWarning($"ExtraSlots could not read the latest backup snapshot. It is retained until the next successful backup save; older snapshots will not be loaded: {ex.Message}");
                return null;
            }
        }

        private static HashSet<int> ReadConsumedRecordIndices(ExtraSlotsBackup backup, int recordCount)
        {
            HashSet<int> consumed = new HashSet<int>();
            foreach (int index in backup.consumedRecordIndices ?? Array.Empty<int>())
                if (index < 0 || index >= recordCount || !consumed.Add(index))
                    throw new InvalidDataException($"Invalid consumed backup record index {index} for {recordCount} records.");
            return consumed;
        }

        private static ItemDrop.ItemData MaterializeBackupRecord(InventorySerialization.RecoveryRecord record)
        {
            if (!record.IsReadable)
                return null;
            try
            {
                ItemDrop.ItemData item = record.Materialize(customKeyBackupID);
                if (item != null)
                    return item;
                LogWarning($"ExtraSlots backup record {record.Description} could not be materialized with its original stack. This record remains in the latest snapshot until the next successful backup save.");
            }
            catch (Exception ex)
            {
                LogWarning($"ExtraSlots backup record {record.Description} failed to load. Other records in this snapshot can still recover: {ex}");
            }
            return null;
        }

        private static void TryRestoreBackup(Player player, ExtraSlotsBackup backup)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null || !recoveringPlayers.Add(player))
                return;

            try
            {
                bool preview = IsCharacterPreview();
                if (!preview)
                {
                    // Rewrite only the envelope, leaving the latest inventory bytes unchanged.
                    // This removes legacy history even when the latest payload cannot be read.
                    backup.recoveryVersion = RecoveryVersion;
                    backup.consumedRecordIndices ??= Array.Empty<int>();
                    player.m_customData[customKeyBackupID] = JsonUtility.ToJson(backup);
                }

                List<InventorySerialization.RecoveryRecord> records = ReadRecoveryRecords(backup);
                if (records == null)
                    return;
                HashSet<int> consumed = ReadConsumedRecordIndices(backup, records.Count);
                if (preview)
                {
                    List<ItemDrop.ItemData> items = records
                        .Where((record, index) => !consumed.Contains(index))
                        .Select(MaterializeBackupRecord).Where(item => item != null).ToList();
                    int projected = ProjectBackupToCharacterPreview(player, inventory, items);
                    LogMessage($"Extra slots backup preview checked. Backup date {backup.date}, world {backup.worldName}, projected {projected}");
                    return;
                }

                var matcher = new Compatibility.InventoryMigration.RecoveryMatcher(player);
                int imported = 0;
                int alreadyPresent = 0;
                for (int index = 0; index < records.Count; index++)
                {
                    if (consumed.Contains(index))
                        continue;

                    ItemDrop.ItemData item = MaterializeBackupRecord(records[index]);
                    if (item == null)
                        continue;

                    int representedIndex;
                    try
                    {
                        representedIndex = matcher.Find(item, SlotBackedRepresentationMatchesSource);
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"ExtraSlots backup record {records[index].Description} could not be matched and remains in the latest snapshot: {ex}");
                        continue;
                    }

                    if (!CommitRecoveredRecord(player, backup, index, item, representedIndex >= 0))
                        continue;

                    consumed.Add(index);
                    if (representedIndex >= 0)
                    {
                        matcher.Consume(representedIndex);
                        alreadyPresent++;
                    }
                    else
                    {
                        imported++;
                    }
                }

                // Imported items remain unavailable until their source indices are consumed.
                // A failed record never rolls back successful neighbors or creates a new snapshot.
                if (imported > 0)
                {
                    ItemsSlotsValidation.ValidateItems();
                    ItemsSlotsValidation.ValidateSlots();
                }
                LogMessage($"Extra slots latest backup recovery committed. Newly deferred {imported}, already present {alreadyPresent}, remaining records {records.Count - consumed.Count}.");
            }
            catch (Exception ex)
            {
                LogWarning($"ExtraSlots latest backup recovery was interrupted. Committed records remain consumed; unresolved data stays in this snapshot until the next successful backup save: {ex}");
            }
            finally
            {
                recoveringPlayers.Remove(player);
            }
        }

        private static bool CommitRecoveredRecord(Player player, ExtraSlotsBackup backup, int index,
            ItemDrop.ItemData item, bool alreadyPresent)
        {
            string originalBackup = player.m_customData[customKeyBackupID];
            int[] originalConsumed = backup.consumedRecordIndices;
            bool hadDeferred = player.m_customData.TryGetValue(DeferredInventory.CustomDataKey, out string originalDeferred);
            DeferredInventory.DeferredEntry handle = null;
            try
            {
                // Keep the original package and its stable record indices. Mark this record
                // before it becomes live, so a retry cannot restore it after it was moved or used.
                int[] updatedConsumed = new int[(originalConsumed?.Length ?? 0) + 1];
                if (originalConsumed != null)
                    Array.Copy(originalConsumed, updatedConsumed, originalConsumed.Length);
                updatedConsumed[updatedConsumed.Length - 1] = index;
                backup.consumedRecordIndices = updatedConsumed;
                string updatedBackup = JsonUtility.ToJson(backup);
                if (!alreadyPresent && !DeferredInventory.EnqueueDetached(player, item, out handle,
                    GetSourceSlotId(item), item.m_equipped, "imported from ExtraSlots backup", pendingFinalization: true))
                {
                    throw new InvalidOperationException("Deferred inventory did not accept the backup record.");
                }

                player.m_customData[customKeyBackupID] = updatedBackup;
                if (handle != null)
                {
                    handle.PendingFinalization = false;
                    DeferredInventory.InvalidateRestorationOpportunity();
                }
                return true;
            }
            catch (Exception ex)
            {
                // Atomicity is per record, not per backup. Do not undo successful neighbors.
                backup.consumedRecordIndices = originalConsumed;
                player.m_customData[customKeyBackupID] = originalBackup;
                if (!alreadyPresent)
                {
                    if (hadDeferred)
                        player.m_customData[DeferredInventory.CustomDataKey] = originalDeferred;
                    else
                        player.m_customData.Remove(DeferredInventory.CustomDataKey);
                    DeferredInventory.EnsureLoaded(player);
                }
                LogWarning($"ExtraSlots backup record {item.m_dropPrefab?.name ?? "<unknown>"} could not be committed and remains in the latest snapshot: {ex}");
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        public static class Player_Save_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.Last)]
            public static void Prefix(Player __instance)
            {
                if (!backupEnabled.Value || __instance != CurrentPlayer || __instance.m_isLoading
                    || recoveringPlayers.Contains(__instance))
                    return;

                try
                {
                    // A successful save replaces the whole previous backup, including unreadable
                    // records and recovery progress. Never inherit data from an older snapshot.
                    ExtraSlotsBackup snapshot = GetExtraSlotsBackup(__instance.GetInventory());
                    string json = JsonUtility.ToJson(snapshot);
                    __instance.m_customData[customKeyBackupID] = json;
                }
                catch (Exception ex)
                {
                    LogWarning($"ExtraSlots could not save a new backup snapshot. The previous snapshot is unchanged: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        public static class Player_Load_TryLoadBackup
        {
            [HarmonyPriority(Priority.HigherThanNormal)]
            public static void Postfix(Player __instance)
            {
                if (!backupEnabled.Value)
                    return;

                if (!PlayerCanRestoreBackup(__instance, out ExtraSlotsBackup extraSlotsBackup))
                    return;

                TryRestoreBackup(__instance, extraSlotsBackup);
            }
        }
    }
}