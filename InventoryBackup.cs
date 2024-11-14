using HarmonyLib;
using System;
using System.Linq;
using UnityEngine;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;
using System.Text;

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
            public string inventoryBase64;

            public string GetTooltip()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Extra Slots backup is available.");
                sb.AppendFormat("Backup made at: {0}\n", date);
                sb.AppendFormat("Made in world: {0}\n", worldName);
                sb.AppendFormat("Number of items: {0}\n\n", nrOfItems);
                sb.AppendFormat("If you do not restore backup now but perform saving of current character this backup will no longer be available.");

                return sb.ToString();
            }
        }

        public const string customKeyBackupID = "ExtraSlotsInventoryBackup";

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

            ExtraSlotsBackup extraSlotsBackup = new ExtraSlotsBackup { date = DateTime.Now.ToString(), worldName = ZNet.instance?.GetWorldName(), nrOfItems = backup.NrOfItems(), width = width, height = height, inventoryBase64 = compressed.GetBase64() };

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

            if (player == null || !player.m_customData.ContainsKey(customKeyBackupID))
                return false;

            if (player.GetInventory().GetAllItems().Any(item => IsItemInSlot(item)))
                return false;

            return TryGetBackup(player, out extraSlotsBackup);
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

                if (backup.NrOfItems() == 0)
                    return;

                if (inventory.m_height < InventoryHeightPlayer + backup.m_height)
                    inventory.m_height = InventoryHeightPlayer + backup.m_height;

                ItemDrop.ItemData item;
                foreach (ItemDrop.ItemData backupItem in backup.GetAllItemsInGridOrder().Reverse<ItemDrop.ItemData>())
                    if (inventory.AddItem(backupItem.Clone(), new Vector2i(backupItem.m_gridPos.x, backupItem.m_gridPos.y + InventoryHeightPlayer)))
                    {
                        item = inventory.GetAllItems().Last();
                        if (item.IsEquipable() && item.m_equipped && !player.EquipItem(item, triggerEquipEffects: false))
                            item.m_equipped = false;
                    }
            }
            catch (Exception ex)
            {
                LogWarning($"Error while loading inventory backup from player:\n{ex}");
                return;
            }

            LogMessage($"Extra slots backup restored. Backup date {extraSlotsBackup.date}, world {extraSlotsBackup.worldName}, items {extraSlotsBackup.nrOfItems}");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        public static class Player_Save_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.Last)]
            public static void Prefix(Player __instance)
            {
                if (backupEnabled.Value && __instance == CurrentPlayer)
                    __instance.m_customData[customKeyBackupID] = JsonUtility.ToJson(GetExtraSlotsBackup(__instance.GetInventory()));
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
