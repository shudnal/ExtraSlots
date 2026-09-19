using HarmonyLib;
using System;
using System.Collections.Generic;
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

            // Version 0 is the original snapshot-only JSON. Pending inventories are opaque,
            // independently retryable records; unreadable packages are retained whole.
            public int recoveryVersion;
            public string[] pendingInventories;

        }

        public const string customKeyBackupID = "ExtraSlotsInventoryBackup";

        private const int RecoveryVersion = 1;

        // Only an unreadable outer envelope blocks replacement. A missing item is kept in
        // pendingInventories while normal backups continue to cover the current equipment.
        private static Player preserveRawBackupForPlayer;
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
                recoveryVersion = RecoveryVersion
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

            return extraSlotsBackup != null
                && extraSlotsBackup.recoveryVersion >= 0 && extraSlotsBackup.recoveryVersion <= RecoveryVersion
                && (!string.IsNullOrEmpty(extraSlotsBackup.inventoryBase64)
                    || extraSlotsBackup.pendingInventories?.Length > 0);
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

            if (TryGetBackup(player, out extraSlotsBackup))
                return true;

            // Never overwrite a backup payload merely because this version cannot parse it.
            if (!IsCharacterPreview())
                preserveRawBackupForPlayer = player;
            return false;
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
            List<InventorySerialization.RecoveryRecord> records = new List<InventorySerialization.RecoveryRecord>();
            Append(backup.inventoryBase64);
            if (backup.pendingInventories != null)
                foreach (string payload in backup.pendingInventories)
                    Append(payload);
            return records;

            void Append(string payload)
            {
                if (string.IsNullOrEmpty(payload))
                    return;
                try
                {
                    records.AddRange(InventorySerialization.ReadRecoveryRecords(payload));
                }
                catch (Exception ex)
                {
                    // Do not consume a partly parsed, unframed stream. Other independent
                    // packages remain usable, and the original bytes can be retried later.
                    records.Add(new InventorySerialization.RecoveryRecord { Payload = payload });
                    LogWarning($"ExtraSlots backup contains an unreadable inventory package. It is retained without blocking other records: {ex.Message}");
                }
            }
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
                LogWarning($"ExtraSlots backup record {record.Description} could not be materialized with its original stack. Only this record remains pending.");
            }
            catch (Exception ex)
            {
                LogWarning($"ExtraSlots backup record {record.Description} failed to load. Only this record remains pending: {ex}");
            }
            return null;
        }

        private static void TryRestoreBackup(Player player, ExtraSlotsBackup backup)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null || !recoveringPlayers.Add(player))
                return;

            bool normalized = false;
            try
            {
                List<InventorySerialization.RecoveryRecord> records = ReadRecoveryRecords(backup);
                if (IsCharacterPreview())
                {
                    List<ItemDrop.ItemData> items = records.Select(MaterializeBackupRecord).Where(item => item != null).ToList();
                    int projected = ProjectBackupToCharacterPreview(player, inventory, items);
                    LogMessage($"Extra slots backup preview checked. Backup date {backup.date}, world {backup.worldName}, projected {projected}");
                    return;
                }

                // Move the original snapshot into independent pending records before adoption.
                // This transformation preserves every record, including unavailable prefabs.
                // A later save can refresh its normal snapshot without losing these records.
                ZPackage empty = new ZPackage();
                new Inventory(customKeyBackupID, null, 1, 1).Save(empty);
                ZPackage compressed = new ZPackage();
                compressed.WriteCompressed(empty);
                backup.inventoryBase64 = compressed.GetBase64();
                backup.nrOfItems = 0;
                backup.recoveryVersion = RecoveryVersion;
                backup.pendingInventories = records.Select(record => record.Payload).ToArray();
                player.m_customData[customKeyBackupID] = JsonUtility.ToJson(backup);
                normalized = true;
                if (ReferenceEquals(preserveRawBackupForPlayer, player))
                    preserveRawBackupForPlayer = null;

                var matcher = new Compatibility.InventoryMigration.RecoveryMatcher(player);
                int imported = 0;
                int alreadyPresent = 0;
                for (int index = 0; index < records.Count;)
                {
                    ItemDrop.ItemData item = MaterializeBackupRecord(records[index]);
                    if (item == null)
                    {
                        index++;
                        continue;
                    }

                    int representedIndex;
                    try
                    {
                        representedIndex = matcher.Find(item, SlotBackedRepresentationMatchesSource);
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"ExtraSlots backup record {records[index].Description} could not be matched and remains pending: {ex}");
                        index++;
                        continue;
                    }

                    if (!CommitRecoveredRecord(player, backup, index, item, representedIndex >= 0))
                    {
                        index++;
                        continue;
                    }

                    if (representedIndex >= 0)
                    {
                        matcher.Consume(representedIndex);
                        alreadyPresent++;
                    }
                    else
                    {
                        imported++;
                    }
                    records.RemoveAt(index);
                }

                // Never make restored items available before their source records are consumed.
                // Missing prefabs or a failure of one record do not roll back earlier commits.
                if (imported > 0)
                {
                    ItemsSlotsValidation.ValidateItems();
                    ItemsSlotsValidation.ValidateSlots();
                }
                LogMessage($"Extra slots backup recovery committed. Newly deferred {imported}, already present {alreadyPresent}, pending records/packages {records.Count}.");
            }
            catch (Exception ex)
            {
                if (!normalized && !IsCharacterPreview())
                    preserveRawBackupForPlayer = player;
                LogWarning($"ExtraSlots backup recovery was interrupted. Previously committed records remain recovered; remaining source data is retained: {ex}");
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
            string[] originalPending = backup.pendingInventories;
            bool hadDeferred = player.m_customData.TryGetValue(DeferredInventory.CustomDataKey, out string originalDeferred);
            DeferredInventory.DeferredEntry handle = null;
            try
            {
                // Prepare the source update before touching deferred ownership. No gameplay
                // callback is raised between adoption and consumption of this source record.
                backup.pendingInventories = originalPending.Where((_, i) => i != index).ToArray();
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
                backup.pendingInventories = originalPending;
                player.m_customData[customKeyBackupID] = originalBackup;
                if (!alreadyPresent)
                {
                    if (hadDeferred)
                        player.m_customData[DeferredInventory.CustomDataKey] = originalDeferred;
                    else
                        player.m_customData.Remove(DeferredInventory.CustomDataKey);
                    DeferredInventory.EnsureLoaded(player);
                }
                LogWarning($"ExtraSlots backup record {item.m_dropPrefab?.name ?? "<unknown>"} could not be committed and remains pending: {ex}");
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        public static class Player_Save_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.Last)]
            public static void Prefix(Player __instance)
            {
                if (!backupEnabled.Value || __instance != CurrentPlayer || recoveringPlayers.Contains(__instance))
                    return;

                if (ReferenceEquals(preserveRawBackupForPlayer, __instance))
                {
                    LogDebug("Extra slots backup save skipped because the existing outer envelope could not be read safely.");
                    return;
                }

                ExtraSlotsBackup previous = null;
                if (__instance.m_customData.ContainsKey(customKeyBackupID) && !TryGetBackup(__instance, out previous))
                {
                    preserveRawBackupForPlayer = __instance;
                    LogWarning("ExtraSlots cannot read the existing backup envelope; its original value will not be overwritten.");
                    return;
                }

                ExtraSlotsBackup snapshot = GetExtraSlotsBackup(__instance.GetInventory());
                snapshot.pendingInventories = previous?.pendingInventories;
                __instance.m_customData[customKeyBackupID] = JsonUtility.ToJson(snapshot);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        public static class Player_Load_TryLoadBackup
        {
            [HarmonyPriority(Priority.HigherThanNormal)]
            public static void Postfix(Player __instance)
            {
                if (ReferenceEquals(preserveRawBackupForPlayer, __instance))
                    preserveRawBackupForPlayer = null;

                if (!backupEnabled.Value)
                    return;

                if (!PlayerCanRestoreBackup(__instance, out ExtraSlotsBackup extraSlotsBackup))
                    return;

                TryRestoreBackup(__instance, extraSlotsBackup);
            }
        }
    }
}