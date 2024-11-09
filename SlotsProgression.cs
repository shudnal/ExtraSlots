using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    internal static class SlotsProgression
    {
        private static readonly HashSet<ItemDrop.ItemData.ItemType> itemTypes = new HashSet<ItemDrop.ItemData.ItemType>();

        public static bool IsAnyGlobalKeyActive(string requiredKeys)
        {
            if (string.IsNullOrEmpty(requiredKeys) || !ZoneSystem.instance || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
                return true;

            IEnumerable<string> keys = requiredKeys.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace());

            return keys.Count() == 0 || keys.Any(s => ZoneSystem.instance.GetGlobalKey(s)) || keys.Any(s => Player.m_localPlayer.HaveUniqueKey(s));
        }

        public static bool IsItemTypeKnown(ItemDrop.ItemData.ItemType itemType)
        {
            if (!Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
                return true;

            return itemTypes.Contains(itemType);
        }

        public static bool IsAnyMaterialDiscovered(string itemNames)
        {
            if (string.IsNullOrEmpty(itemNames) || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
                return true;

            IEnumerable<string> items = itemNames.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace());

            return items.Count() == 0 || items.Any(s => Player.m_localPlayer.IsMaterialKnown(s));
        }

        public static bool IsAmmoSlotKnown() => !ammoSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Ammo);
        public static bool IsFoodSlotKnown() => !foodSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Consumable);
        public static bool IsUtilitySlotKnown() => !utilitySlotAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Utility);
        public static bool IsHelmetSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Helmet);
        public static bool IsChestSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Chest);
        public static bool IsLegsSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Legs);
        public static bool IsShoulderSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Shoulder);

        private static void UpdateItemTypes()
        {
            itemTypes.Clear();

            if (!ObjectDB.instance || !Player.m_localPlayer)
                return;

            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item.GetComponent<ItemDrop>()?.m_itemData is not ItemDrop.ItemData itemData)
                    continue;

                if (Player.m_localPlayer.m_knownMaterial.Contains(itemData.m_shared.m_name))
                    itemTypes.Add(itemData.m_shared.m_itemType);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
        private static class Player_AddKnownItem_UpdateKnownItemTypes
        {
            private static void Prefix(ref int __state)
            {
                __state = Player.m_localPlayer.m_knownMaterial.Count;
            }

            private static void Postfix(ItemDrop.ItemData item, int __state)
            {
                if (__state != Player.m_localPlayer.m_knownMaterial.Count)
                    itemTypes.Add(item.m_shared.m_itemType);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.EquipInventoryItems))]
        private static class Player_EquipInventoryItems_UpdateKnownItemTypes
        {
            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    UpdateItemTypes();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDestroy))]
        private static class Player_OnDestroy_ClearKnownItemTypes
        {
            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    itemTypes.Clear();
            }
        }
    }
}