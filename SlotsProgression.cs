using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class SlotsProgression
    {
        private static readonly HashSet<ItemDrop.ItemData.ItemType> itemTypes = new HashSet<ItemDrop.ItemData.ItemType>();
        private static readonly Dictionary<string, IEnumerable<string>> keysCache = new Dictionary<string, IEnumerable<string>>();

        private static IEnumerable<string> GetKeys(string configValue)
        {
            if (keysCache.TryGetValue(configValue, out IEnumerable<string> keys))
                return keys;

            return keysCache[configValue] = configValue.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace());
        }

        public static bool IsAnyGlobalKeyActive(string requiredKeys)
        {
            if (!slotsProgressionEnabled.Value || !CurrentPlayer || CurrentPlayer.m_isLoading || string.IsNullOrEmpty(requiredKeys))
                return true;

            IEnumerable<string> keys = GetKeys(requiredKeys);

            return keys.Count() == 0 || ZoneSystem.instance && keys.Any(s => ZoneSystem.instance.GetGlobalKey(s)) || keys.Any(s => CurrentPlayer.HaveUniqueKey(s));
        }

        public static bool IsItemTypeKnown(ItemDrop.ItemData.ItemType itemType)
        {
            if (!slotsProgressionEnabled.Value || !CurrentPlayer || CurrentPlayer.m_isLoading || itemTypes.Count == 0)
                return true;

            return itemTypes.Contains(itemType);
        }

        public static bool IsAnyMaterialDiscovered(string itemNames)
        {
            if (!slotsProgressionEnabled.Value || !CurrentPlayer || CurrentPlayer.m_isLoading || string.IsNullOrEmpty(itemNames))
                return true;

            IEnumerable<string> keys = GetKeys(itemNames);

            return keys.Count() == 0 || keys.Any(s => CurrentPlayer.IsMaterialKnown(s.GetItemName()));
        }

        public static bool IsAmmoSlotKnown() => !ammoSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Ammo);
        public static bool IsFoodSlotKnown() => !foodSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Consumable);
        public static bool IsUtilitySlotKnown() => !utilitySlotAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Utility);
        public static bool IsHelmetSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Helmet);
        public static bool IsChestSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Chest);
        public static bool IsLegsSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Legs);
        public static bool IsShoulderSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Shoulder);
        public static bool IsTrinketSlotKnown() => !equipmentSlotsAvailableAfterDiscovery.Value || IsItemTypeKnown(ItemDrop.ItemData.ItemType.Trinket);

        private static void UpdateItemTypes()
        {
            itemTypes.Clear();

            if (!ObjectDB.instance || !Player.m_localPlayer)
                return;

            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null || item.GetComponent<ItemDrop>() is not ItemDrop itemDrop)
                    continue;

                if (itemDrop.m_itemData is not ItemDrop.ItemData itemData || itemData.m_shared is not ItemDrop.ItemData.SharedData shared)
                    continue;

                if (Player.m_localPlayer.m_knownMaterial.Contains(shared.m_name))
                    itemTypes.Add(shared.m_itemType);
            }
        }

        internal static bool IsQuickSlotKnown(int index) => IsSlotActive(QuickSlotGlobalKey(index), QuickSlotItemDiscovered(index));

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

        internal static bool IsExtraUtilitySlotKnown(int index) => IsSlotActive(UtilitySlotGlobalKey(index), UtilitySlotItemDiscovered(index));

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

        public static bool IsSlotActive(string globalKey, string itemDiscovered)
        {
            bool globalKeyIsSet = !globalKey.IsNullOrWhiteSpace();
            bool itemIsSet = !itemDiscovered.IsNullOrWhiteSpace();

            // If nothing is set - slot is always active
            if (!globalKeyIsSet && !itemIsSet)
                return true;

            // If both are set - one of both should work
            if (globalKeyIsSet && itemIsSet)
                return IsAnyGlobalKeyActive(globalKey) || IsAnyMaterialDiscovered(itemDiscovered);

            // If global is set, item is not - check only global, otherwise check item
            return globalKeyIsSet ? IsAnyGlobalKeyActive(globalKey) : IsAnyMaterialDiscovered(itemDiscovered);
        }

        public static readonly HashSet<string> m_knownMaterialCache = new HashSet<string>();
        public static readonly HashSet<string> m_uniquesCache = new HashSet<string>();

        public static bool IsAnyPlayerKeyActiveCached(string requiredKeys)
        {
            if (string.IsNullOrEmpty(requiredKeys))
                return true;

            IEnumerable<string> keys = GetKeys(requiredKeys);

            if (keys.Count() == 0)
                return true;
            else if (CurrentPlayer && !CurrentPlayer.m_isLoading)
                return keys.Any(s => CurrentPlayer.HaveUniqueKey(s));
            else
                return keys.Any(s => m_uniquesCache.Contains(s));
        }

        // Check for cache currently loaded mats and keys
        public static bool IsAnyItemDiscoveredCached(string itemNames)
        {
            if (string.IsNullOrEmpty(itemNames))
                return true;

            IEnumerable<string> keys = GetKeys(itemNames);
            if (keys.Count() == 0)
                return true;
            else if (CurrentPlayer && !CurrentPlayer.m_isLoading)
                return keys.Any(s => CurrentPlayer.IsMaterialKnown(s));
            else
                return keys.Any(s => m_knownMaterialCache.Contains(s));
        }

        internal static bool IsRowProgressionActive() => rowsProgressionEnabled.Value && (CurrentPlayer && !CurrentPlayer.m_isLoading || m_knownMaterialCache.Count + m_uniquesCache.Count > 0);

        internal static bool IsExtraRowKnown(int index)
        {
            if (!IsRowProgressionActive())
                return true;

            return IsPlayerKeyItemConditionMet(ExtraRowPlayerKey(index), ExtraRowItemDiscovered(index).GetItemName());
        }

        internal static bool IsPlayerKeyItemConditionMet(string playerKey, string itemDiscovered)
        {
            bool globalKeyIsSet = !playerKey.IsNullOrWhiteSpace();
            bool itemIsSet = !itemDiscovered.IsNullOrWhiteSpace();

            // If nothing is set - slot is always active
            if (!globalKeyIsSet && !itemIsSet)
                return true;

            // If both are set - one of both should work
            if (globalKeyIsSet && itemIsSet)
                return IsAnyPlayerKeyActiveCached(playerKey) || IsAnyItemDiscoveredCached(itemDiscovered);

            // If global is set, item is not - check only global, otherwise check item
            return globalKeyIsSet ? IsAnyPlayerKeyActiveCached(playerKey) : IsAnyItemDiscoveredCached(itemDiscovered);
        }

        private static string ExtraRowPlayerKey(int index)
        {
            return index switch
            {
                -3 => extraRowPlayerKeyMinus2.Value,
                -2 => extraRowPlayerKeyMinus1.Value,
                -1 => extraRowPlayerKeyVanilla.Value,
                0 => extraRowPlayerKey1.Value,
                1 => extraRowPlayerKey2.Value,
                2 => extraRowPlayerKey3.Value,
                3 => extraRowPlayerKey4.Value,
                4 => extraRowPlayerKey5.Value,
                _ => ""
            };
        }

        private static string ExtraRowItemDiscovered(int index)
        {
            return index switch
            {
                -3 => extraRowItemDiscoveredMinus2.Value,
                -2 => extraRowItemDiscoveredMinus1.Value,
                -1 => extraRowItemDiscoveredVanilla.Value,
                0 => extraRowItemDiscovered1.Value,
                1 => extraRowItemDiscovered2.Value,
                2 => extraRowItemDiscovered3.Value,
                3 => extraRowItemDiscovered4.Value,
                4 => extraRowItemDiscovered5.Value,
                _ => ""
            };
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
        private static class Player_AddKnownItem_UpdateKnownItemTypes
        {
            private static void Prefix(Player __instance, ref int __state)
            {
                __state = __instance.m_knownMaterial.Count;
            }

            private static void Postfix(Player __instance, ItemDrop.ItemData item, int __state)
            {
                if (__state != __instance.m_knownMaterial.Count)
                {
                    itemTypes.Add(item.m_shared.m_itemType);
                    if (IsRowProgressionActive() || LightenedSlots.IsEnabled)
                        instance.StartSlotsUpdateNextFrame();
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned_UpdateKnownItemTypes
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

        [HarmonyPatch]
        public static class Patch_UpdateAvailableRows
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Player), nameof(Player.ResetCharacter));
                yield return AccessTools.Method(typeof(Player), nameof(Player.ResetCharacterKnownItems));
                yield return AccessTools.Method(typeof(Player), nameof(Player.AddUniqueKey));
                yield return AccessTools.Method(typeof(Player), nameof(Player.RemoveUniqueKey));
                yield return AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.RPC_GlobalKeys));
            }

            private static void Postfix()
            {
                if (IsRowProgressionActive() || LightenedSlots.IsEnabled)
                    instance.StartSlotsUpdateNextFrame();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load_UpdateSlotsGridPositions
        {
            private static void Postfix(Player __instance)
            {
                loadedPlayer = __instance;
                if (IsRowProgressionActive())
                    UpdateSlotsGridPosition();
                loadedPlayer = null;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
        private static class Player_SetLocalPlayer_ClearPlayerKeyAndKnownMaterialsCache
        {
            private static void Prefix()
            {
                m_knownMaterialCache.Clear();
                m_uniquesCache.Clear();
            }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.SetupCharacterPreview))]
        private static class FejdStartup_SetupCharacterPreview_CacheLastPlayer
        {
            private static void Postfix()
            {
                if (CurrentPlayer != null)
                {
                    CurrentPlayer.m_knownMaterial.Do(mat => m_knownMaterialCache.Add(mat));
                    CurrentPlayer.m_uniques.Do(mat => m_uniquesCache.Add(mat));
                }
            }
        }
    }
}