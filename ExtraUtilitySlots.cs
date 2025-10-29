using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    [Serializable]
    public class HumanoidExtraUtilitySlots
    {
        public ItemDrop.ItemData utility1;
        public ItemDrop.ItemData utility2;
        public ItemDrop.ItemData utility3;
        public ItemDrop.ItemData utility4;
    }

    public static class HumanoidExtension
    {
        private static readonly ConditionalWeakTable<Humanoid, HumanoidExtraUtilitySlots> data = new ConditionalWeakTable<Humanoid, HumanoidExtraUtilitySlots>();

        public static HumanoidExtraUtilitySlots GetExtraUtilityData(this Humanoid humanoid) => data.GetOrCreateValue(humanoid);

        public static ItemDrop.ItemData GetExtraUtility(this Humanoid humanoid, int index) 
        {
            return index switch
            {
                0 => humanoid.GetExtraUtilityData().utility1,
                1 => humanoid.GetExtraUtilityData().utility2,
                2 => humanoid.GetExtraUtilityData().utility3,
                3 => humanoid.GetExtraUtilityData().utility4,
                _ => null
            };
        }

        public static ItemDrop.ItemData SetExtraUtility(this Humanoid humanoid, int index, ItemDrop.ItemData item)
        {
            return index switch
            {
                0 => humanoid.GetExtraUtilityData().utility1 = item,
                1 => humanoid.GetExtraUtilityData().utility2 = item,
                2 => humanoid.GetExtraUtilityData().utility3 = item,
                3 => humanoid.GetExtraUtilityData().utility4 = item,
                _ => null
            };
        }
    }

    [Serializable]
    public class VisEquipmentCustomItemState
    {
        public string m_item = "";
        public List<GameObject> m_instances;
        public int m_hash = 0;
    }

    [Serializable]
    public class VisEquipmentCustomItem
    {
        public VisEquipmentCustomItemState customItem1 = new VisEquipmentCustomItemState();
        public VisEquipmentCustomItemState customItem2 = new VisEquipmentCustomItemState();
        public VisEquipmentCustomItemState customItem3 = new VisEquipmentCustomItemState();
        public VisEquipmentCustomItemState customItem4 = new VisEquipmentCustomItemState();
    }

    public static class VisEquipmentExtension
    {
        private static readonly List<int> customItemStateZdoHash = new List<int>()
        {
            "ExtraSlots_ExtraUtility_1".GetStableHashCode(),
            "ExtraSlots_ExtraUtility_2".GetStableHashCode(),
            "ExtraSlots_ExtraUtility_3".GetStableHashCode(),
            "ExtraSlots_ExtraUtility_4".GetStableHashCode(),
        };

        private static readonly ConditionalWeakTable<VisEquipment, VisEquipmentCustomItem> data = new ConditionalWeakTable<VisEquipment, VisEquipmentCustomItem>();

        public static VisEquipmentCustomItem GetCustomItemData(this VisEquipment visEquipment) => data.GetOrCreateValue(visEquipment);

        public static VisEquipmentCustomItemState GetCustomItemState(this VisEquipment humanoid, int index)
        {
            return index switch
            {
                0 => humanoid.GetCustomItemData().customItem1,
                1 => humanoid.GetCustomItemData().customItem2,
                2 => humanoid.GetCustomItemData().customItem3,
                3 => humanoid.GetCustomItemData().customItem4,
                _ => null
            };
        }

        public static void SetCustomItemState(this VisEquipment visEquipment, int index, string name)
        {
            VisEquipmentCustomItemState customItemData = visEquipment.GetCustomItemState(index);

            if (customItemData.m_item != name)
            {
                customItemData.m_item = name;
                if (visEquipment.m_nview.IsValid() && visEquipment.m_nview.IsOwner())
                    visEquipment.m_nview.GetZDO().Set(customItemStateZdoHash[index], (!string.IsNullOrEmpty(name)) ? name.GetStableHashCode() : 0);
            }
        }

        public static bool SetCustomItemEquipped(this VisEquipment visEquipment, int hash, int index)
        {
            VisEquipmentCustomItemState customItemData = visEquipment.GetCustomItemState(index);
            if (customItemData.m_hash == hash)
                return false;

            if (customItemData.m_instances != null)
            {
                foreach (GameObject utilityItemInstance in customItemData.m_instances)
                {
                    if ((bool)visEquipment.m_lodGroup)
                    {
                        Utils.RemoveFromLodgroup(visEquipment.m_lodGroup, utilityItemInstance);
                    }

                    UnityEngine.Object.Destroy(utilityItemInstance);
                }

                customItemData.m_instances = null;
            }

            customItemData.m_hash = hash;
            if (hash != 0)
                customItemData.m_instances = visEquipment.AttachArmor(hash);

            return true;
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.UpdateEquipmentVisuals))]
        public static class VisEquipment_UpdateEquipmentVisuals_CustomItemType
        {
            public static VisEquipment visEq;
            public static bool updateLodGroup;

            private static void Prefix(VisEquipment __instance)
            {
                ZDO zDO = __instance.m_nview?.GetZDO();

                updateLodGroup = false;
                for (int i = 0; i < 4; i++)
                {
                    int itemEquipped = 0;
                    if (zDO != null)
                    {
                        itemEquipped = zDO.GetInt(customItemStateZdoHash[i]);
                    }
                    else
                    {
                        VisEquipmentCustomItemState customItemData = __instance.GetCustomItemState(i);
                        if (!string.IsNullOrEmpty(customItemData.m_item))
                            itemEquipped = customItemData.m_item.GetStableHashCode();
                    }

                    if (__instance.SetCustomItemEquipped(itemEquipped, i))
                        updateLodGroup = true;
                }

                visEq = __instance;
            }

            private static void Postfix(VisEquipment __instance)
            {
                if (updateLodGroup)
                    __instance.UpdateLodgroup();

                visEq = null;
                updateLodGroup = false;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.UpdateLodgroup))]
        public static class VisEquipment_UpdateLodgroup_CustomItemType
        {
            private static void Finalizer(VisEquipment __instance)
            {
                if (__instance == VisEquipment_UpdateEquipmentVisuals_CustomItemType.visEq)
                    VisEquipment_UpdateEquipmentVisuals_CustomItemType.updateLodGroup = false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.SetupVisEquipment))]
        public static class Humanoid_SetupVisEquipment_CustomItemType
        {
            private static void Postfix(Humanoid __instance, VisEquipment visEq)
            {
                for (int i = 0; i < 4; i++)
                {
                    ItemDrop.ItemData itemData = __instance.GetExtraUtility(i);
                    visEq.SetCustomItemState(i, (ExtraSlots.showExtraUtilityItems.Value && itemData != null && itemData.m_dropPrefab != null) ? itemData.m_dropPrefab.name : "");
                }
            }
        }
    }

    public static class ExtraUtilitySlots
    {
        private static readonly List<ItemDrop.ItemData> tempItems = new List<ItemDrop.ItemData>();
        private static readonly HashSet<StatusEffect> tempEffects = new HashSet<StatusEffect>();
        private static readonly List<HashSet<string>> uniqueEquipped = new List<HashSet<string>>();

        public static int ActiveSlots => ExtraSlots.extraUtilitySlotsAmount.Value;

        public static ItemDrop.ItemData GetItem(int index) => Player.m_localPlayer?.GetExtraUtility(index);

        public static bool IsItemEquipped(ItemDrop.ItemData item) => GetUtilityItemIndex(item) != -1;

        public static int GetEmptySlot()
        {
            for (int i = 0; i < ActiveSlots; i++)
                if (IsSlotAvailable(i))
                    return i;

            return -1;
        }

        public static int GetSlotForItem(ItemDrop.ItemData item)
        {
            foreach (HashSet<string> uniqueItems in uniqueEquipped)
            {
                if (uniqueItems.Contains(item.m_shared.m_name))
                {
                    if (Player.m_localPlayer.m_utilityItem != null && uniqueItems.Contains(Player.m_localPlayer.m_utilityItem.m_shared.m_name))
                        return -1;

                    for (int i = 0; i < ActiveSlots; i++)
                        if (GetItem(i) is ItemDrop.ItemData utilityItem && uniqueItems.Contains(utilityItem.m_shared.m_name))
                            return i;
                }
            }

            return GetEmptySlot();
        }
        
        private static bool IsSlotAvailable(int index) => IsExtraUtilitySlotAvailable(index) && GetItem(index) == null;

        public static IEnumerable<ItemDrop.ItemData> GetEquippedItems()
        {
            tempItems.Clear();

            for (int i = 0; i < ActiveSlots; i++)
                if (GetItem(i) is ItemDrop.ItemData utilityItem)
                    tempItems.Add(utilityItem);

            return tempItems;
        }

        public static int GetUtilityItemIndex(ItemDrop.ItemData item)
        {
            if (item == null)
                return -1;

            for (int i = 0; i < ActiveSlots; i++)
                if (GetItem(i) is ItemDrop.ItemData utilityItem && utilityItem == item)
                    return i;

            return -1;
        }

        public static void UpdateUniqueEquipped()
        {
            uniqueEquipped.Clear();

            foreach (string itemsTuple in ExtraSlots.preventUniqueUtilityItemsEquip.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] items = itemsTuple.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (items.Length > 0)
                    uniqueEquipped.Add(new HashSet<string>(items.Select(item => item.GetItemName())));
            };
        }

        public static class ExtraUtilityPatches
        {
            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipmentStatusEffects))]
            private static class Humanoid_UpdateEquipmentStatusEffects_ExtraUtility
            {
                private static void Prefix(Humanoid __instance)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    tempEffects.Clear();

                    foreach(ItemDrop.ItemData item in GetEquippedItems().ToArray())
                    {
                        if ((bool)item.m_shared.m_equipStatusEffect)
                            tempEffects.Add(item.m_shared.m_equipStatusEffect);

                        if (__instance.HaveSetEffect(item))
                            tempEffects.Add(item.m_shared.m_setStatusEffect);
                    }
                }

                private static void Postfix(Humanoid __instance)
                {
                    foreach (StatusEffect item in tempEffects.Where(item => !__instance.m_equipmentStatusEffects.Contains(item)))
                        __instance.m_seman.AddStatusEffect(item);

                    __instance.m_equipmentStatusEffects.UnionWith(tempEffects);

                    tempEffects.Clear();
                }
            }

            [HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), typeof(int), typeof(bool))]
            private static class SEMan_RemoveStatusEffect_ExtraUtilityPreventRemoval
            {
                private static void Prefix(SEMan __instance, ref int nameHash)
                {
                    if (__instance != CurrentPlayer?.GetSEMan() || tempEffects.Count == 0)
                        return;

                    foreach (StatusEffect se in tempEffects)
                        if (se.NameHash() == nameHash)
                            nameHash = 0;
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetEquipmentWeight))]
            private static class Humanoid_GetEquipmentWeight_ExtraUtility
            {
                private static void Postfix(Humanoid __instance, ref float __result)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    __result += GetEquippedItems().Sum(item => item.m_shared.m_weight);
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
            private static class Humanoid_EquipItem_ExtraUtility
            {
                private static readonly ItemDrop.ItemData.ItemType tempType = (ItemDrop.ItemData.ItemType)727;

                private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, ref int __state)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    if (item == null)
                        return;

                    if (IsUtilitySlotItem(item) && !IsCustomSlotItem(item) && __instance.m_utilityItem != null && !IsItemEquipped(item) && (__state = GetSlotForItem(item)) != -1)
                    {
                        item.m_shared.m_itemType = tempType;
                        if (__instance.m_visEquipment && __instance.m_visEquipment.m_isPlayer)
                            item.m_shared.m_equipEffect.Create(__instance.transform.position + Vector3.up, __instance.transform.rotation);
                    }
                }

                [HarmonyPriority(Priority.First)]
                private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects, int __state, ref bool __result)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    if (item == null || item.m_shared.m_itemType != tempType || __state == -1)
                        return;

                    item.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Utility;

                    if (__instance.GetExtraUtility(__state) is ItemDrop.ItemData utilityItem)
                        __instance.UnequipItem(utilityItem, triggerEquipEffects);

                    __instance.SetExtraUtility(__state, item);

                    if (__instance.IsItemEquiped(item))
                    {
                        item.m_equipped = true;
                        __result = true;
                    }

                    __instance.SetupEquipment();
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
            public static class Humanoid_UnequipItem_ExtraUtility
            {
                [HarmonyPriority(Priority.First)]
                private static void Postfix(Humanoid __instance, ItemDrop.ItemData item)
                {
                    if (item == null)
                        return;

                    if (GetUtilityItemIndex(item) is int i && i != -1)
                    {
                        __instance.SetExtraUtility(i, null);
                        __instance.SetupEquipment();
                    }
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipAllItems))]
            public static class Humanoid_UnequipAllItems_ExtraUtility
            {
                [HarmonyPriority(Priority.First)]
                private static void Postfix(Humanoid __instance)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    GetEquippedItems().ToArray().Do(item => __instance.UnequipItem(item, triggerEquipEffects: false));
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsItemEquiped))]
            private static class Humanoid_IsItemEquiped_ExtraUtility
            {
                private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    __result = __result || IsItemEquipped(item);
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.UnequipDeathDropItems))]
            private static class Player_UnequipDeathDropItems_ExtraUtility
            {
                private static void Prefix(Player __instance)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    bool setupVisEq = false;
                    for (int i = 0; i < ActiveSlots; i++)
                        if (GetItem(i) is ItemDrop.ItemData utilityItem)
                        {
                            utilityItem.m_equipped = false;
                            __instance.SetExtraUtility(i, null);
                            setupVisEq = true;
                        }

                    if (setupVisEq)
                        __instance.SetupEquipment();
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.GetEquipmentEitrRegenModifier))]
            private static class Player_GetEquipmentEitrRegenModifier_ExtraUtility
            {
                private static void Postfix(Player __instance, ref float __result)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    __result += GetEquippedItems().Sum(item => item.m_shared.m_eitrRegenModifier);
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipment))]
            private static class Humanoid_UpdateEquipment_ExtraUtilityDurabilityDrain
            {
                private static void Postfix(Humanoid __instance, float dt)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    GetEquippedItems().DoIf(item => item.m_shared.m_useDurability, item => __instance.DrainEquipedItemDurability(item, dt));
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.ApplyArmorDamageMods))]
            private static class Player_ApplyArmorDamageMods_ExtraUtility
            {
                private static void Postfix(Player __instance, ref HitData.DamageModifiers mods)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    GetEquippedItems().Select(item => item.m_shared.m_damageModifiers).Do(mods.Apply);
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.UpdateModifiers))]
            private static class Player_UpdateModifiers_ExtraUtility
            {
                private static void Postfix(Player __instance)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    if (Player.s_equipmentModifierSourceFields == null)
                        return;

                    for (int i = 0; i < __instance.m_equipmentModifierValues.Length; i++)
                        GetEquippedItems().Do(item => __instance.m_equipmentModifierValues[i] += (float)Player.s_equipmentModifierSourceFields[i].GetValue(item.m_shared));
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
            private static class Player_OnInventoryChanged_ValidateExtraUtilitySlots
            {
                private static void Postfix(Player __instance)
                {
                    if (!IsValidPlayer(__instance) || __instance.m_isLoading)
                        return;

                    bool setupVisEq = false;
                    for (int i = 0; i < ActiveSlots; i++)
                        if (GetItem(i) is ItemDrop.ItemData utilityItem && !PlayerInventory.ContainsItem(utilityItem))
                        {
                            __instance.SetExtraUtility(i, null);
                            setupVisEq = true;
                        }

                    if (setupVisEq)
                        __instance.SetupEquipment();
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetSetCount))]
            private static class Humanoid_GetSetCount_ExtraUtility
            {
                private static void Postfix(Humanoid __instance, string setName, ref int __result)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    __result += GetEquippedItems().Count(item => item.m_shared.m_setName == setName);
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
            private static class Player_OnSpawned_UpdateUniqueEquippedItems
            {
                private static void Postfix() => UpdateUniqueEquipped();
            }
        }
    }
}
