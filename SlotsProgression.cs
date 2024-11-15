using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    internal static class SlotsProgression
    {
        private static readonly HashSet<ItemDrop.ItemData.ItemType> itemTypes = new HashSet<ItemDrop.ItemData.ItemType>();
        private static readonly Dictionary<string, IEnumerable<string>> requiredKeysCache = new Dictionary<string, IEnumerable<string>>();

        private static IEnumerable<string> GetRequiredKeys(string configValue)
        {
            if (requiredKeysCache.TryGetValue(configValue, out IEnumerable<string> keys))
                return keys;

            return requiredKeysCache[configValue] = configValue.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace());
        }

        public static bool IsAnyGlobalKeyActive(string requiredKeys)
        {
            if (!slotsProgressionEnabled.Value || string.IsNullOrEmpty(requiredKeys) || !ZoneSystem.instance || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
                return true;

            IEnumerable<string> keys = GetRequiredKeys(requiredKeys);

            return keys.Count() == 0 || keys.Any(s => ZoneSystem.instance.GetGlobalKey(s)) || keys.Any(s => Player.m_localPlayer.HaveUniqueKey(s));
        }

        public static bool IsItemTypeKnown(ItemDrop.ItemData.ItemType itemType)
        {
            if (!slotsProgressionEnabled.Value || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
                return true;

            return itemTypes.Contains(itemType);
        }

        public static bool IsAnyMaterialDiscovered(string itemNames)
        {
            if (!slotsProgressionEnabled.Value || string.IsNullOrEmpty(itemNames) || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
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

        internal static bool IsQuickSlotKnown(int index) => IsAnyGlobalKeyActive(QuickSlotGlobalKey(index)) || IsAnyMaterialDiscovered(QuickSlotItemDiscovered(index));

        private static string QuickSlotGlobalKey(int index)
        {
            return index switch
            {
                0 => quickSlotGlobalKey1.Value,
                1 => quickSlotGlobalKey2.Value,
                2 => quickSlotGlobalKey3.Value,
                3 => quickSlotGlobalKey4.Value,
                4 => quickSlotGlobalKey5.Value,
                5 => quickSlotGlobalKey6.Value,
                _ => ""
            };
        }

        private static string QuickSlotItemDiscovered(int index)
        {
            return index switch
            {
                0 => quickSlotItemDiscovered1.Value,
                1 => quickSlotItemDiscovered2.Value,
                2 => quickSlotItemDiscovered3.Value,
                3 => quickSlotItemDiscovered4.Value,
                4 => quickSlotItemDiscovered5.Value,
                5 => quickSlotItemDiscovered6.Value,
                _ => ""
            };
        }

        internal static bool IsExtraUtilitySlotKnown(int index) => IsAnyGlobalKeyActive(UtilitySlotGlobalKey(index)) || IsAnyMaterialDiscovered(UtilitySlotItemDiscovered(index));

        internal static string UtilitySlotGlobalKey(int index)
        {
            return index switch
            {
                0 => utilitySlotGlobalKey1.Value,
                1 => utilitySlotGlobalKey2.Value,
                2 => utilitySlotGlobalKey3.Value,
                3 => utilitySlotGlobalKey4.Value,
                _ => ""
            };
        }

        internal static string UtilitySlotItemDiscovered(int index)
        {
            return index switch
            {
                0 => utilitySlotItemDiscovered1.Value,
                1 => utilitySlotItemDiscovered2.Value,
                2 => utilitySlotItemDiscovered3.Value,
                3 => utilitySlotItemDiscovered4.Value,
                _ => ""
            };
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

        [HarmonyPatch]
        public static class Player_ClearKnownItemTypes
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Player), nameof(Player.OnDestroy));
                yield return AccessTools.Method(typeof(Player), nameof(Player.ResetCharacter));
                yield return AccessTools.Method(typeof(Player), nameof(Player.ResetCharacterKnownItems));
                yield return AccessTools.Method(typeof(Player), nameof(Player.Load));
            }

            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    itemTypes.Clear();
            }
        }
    }
}