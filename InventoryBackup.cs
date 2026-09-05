using HarmonyLib;
using System;
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

                backup.AddItem(backupItem, new Vector2i(backupItem.m_gridPos.x, backupItem.m_gridPos.y - InventoryHeightPlayer));
            }

            ZPackage pkg = new ZPackage();
            backup.Save(pkg);

            ZPackage compressed = new ZPackage();
            compressed.WriteCompressed(pkg);

            ExtraSlotsBackup extraSlotsBackup = new ExtraSlotsBackup { 
                date = DateTime.Now.ToString(), 
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
            preserveRawBackupForPlayer = player;
            return false;
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
                bool allMaterialized = backupItems.Count == extraSlotsBackup.nrOfItems;

                int imported = Compatibility.InventoryMigration.ImportMissingItemsToDeferred(
                    player,
                    backupItems,
                    "ExtraSlots backup",
                    item => item.m_customData.TryGetValue(customKeySlotID, out string slotId) ? slotId : null,
                    item => item.m_equipped,
                    out bool allRepresented);

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
