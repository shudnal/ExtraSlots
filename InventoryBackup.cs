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
        }

        public const string customKeyBackupID = "ExtraSlotsInventoryBackup";

        // If Inventory.Load cannot materialize every serialized backup entry (typically because an
        // item prefab is temporarily unavailable), keep the original opaque backup payload intact.
        // Deferred storage can adopt every materializable item without making us discard unreadable data.
        private static Player preserveRawBackupForPlayer;

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
                inventoryBase64 = compressed.GetBase64() 
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

            return extraSlotsBackup != null && !string.IsNullOrEmpty(extraSlotsBackup.inventoryBase64);
        }

        private static bool PlayerCanRestoreBackup(Player player, out ExtraSlotsBackup extraSlotsBackup)
        {
            extraSlotsBackup = null;

            if (HasServerCharactersActive || player == null || !player.m_customData.ContainsKey(customKeyBackupID))
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
            if (item == null)
                return null;

            // Physical placement is not ownership provenance: topology reconciliation may move an
            // unrelated regular item into a free ExtraSlots cell. Only the return address written
            // during Player.Save proves that this live item represents a slot-only backup entry.
            if (TryGetSavedPlayerSlot(item, out Slot savedSlot) && savedSlot != null && !savedSlot.IsEmptySlot)
                return savedSlot.ID;

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
                int representedIndex = represented.FindIndex(existing =>
                    string.Equals(DeferredInventory.GetMigrationKey(existing), key, StringComparison.Ordinal)
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

        private static void TryRestoreBackup(Player player, ExtraSlotsBackup extraSlotsBackup)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null)
                return;

            try
            {
                Inventory backup = new Inventory(customKeyBackupID, null, extraSlotsBackup.width, extraSlotsBackup.height);
                backup.Load(new ZPackage(extraSlotsBackup.inventoryBase64).ReadCompressedPackage());

                var backupItems = backup.GetAllItemsInGridOrder().Where(item => item != null).ToList();

                if (IsCharacterPreview())
                {
                    int projected = ProjectBackupToCharacterPreview(player, inventory, backupItems);
                    LogMessage($"Extra slots backup preview checked. Backup date {extraSlotsBackup.date}, world {extraSlotsBackup.worldName}, items {extraSlotsBackup.nrOfItems}, projected {projected}");
                    return;
                }

                bool allMaterialized = backupItems.Count == extraSlotsBackup.nrOfItems;

                int imported = Compatibility.InventoryMigration.ImportMissingItemsToDeferred(
                    player,
                    backupItems,
                    "ExtraSlots backup",
                    item => item.m_customData.TryGetValue(customKeySlotID, out string slotId) ? slotId : null,
                    item => item.m_equipped,
                    out bool allRepresented,
                    SlotBackedRepresentationMatchesSource);

                if (!allMaterialized || !allRepresented)
                {
                    preserveRawBackupForPlayer = player;
                    if (!allMaterialized)
                        LogWarning($"ExtraSlots backup materialized {backupItems.Count}/{extraSlotsBackup.nrOfItems} item(s). The original backup payload will be preserved until every prefab is available.");
                    else
                        LogWarning("ExtraSlots backup could not be fully adopted. The original backup payload will be preserved for a later retry.");
                }
                else if (ReferenceEquals(preserveRawBackupForPlayer, player))
                {
                    preserveRawBackupForPlayer = null;
                }

                if (imported > 0)
                {
                    ItemsSlotsValidation.ValidateItems();
                    ItemsSlotsValidation.ValidateSlots();
                }

                LogMessage($"Extra slots backup checked. Backup date {extraSlotsBackup.date}, world {extraSlotsBackup.worldName}, items {extraSlotsBackup.nrOfItems}, newly deferred {imported}");
            }
            catch (Exception ex)
            {
                if (!IsCharacterPreview())
                    preserveRawBackupForPlayer = player;
                LogWarning($"Error while loading inventory backup from player. The original backup payload will be preserved:\n{ex}");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        public static class Player_Save_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.Last)]
            public static void Prefix(Player __instance)
            {
                if (!backupEnabled.Value || __instance != CurrentPlayer)
                    return;

                if (ReferenceEquals(preserveRawBackupForPlayer, __instance))
                {
                    LogDebug("Extra slots backup save skipped because the existing backup contains data that could not be fully materialized or adopted.");
                    return;
                }

                __instance.m_customData[customKeyBackupID] = JsonUtility.ToJson(GetExtraSlotsBackup(__instance.GetInventory()));
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
