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
        public ItemDrop.ItemData utility1 = null;
        public ItemDrop.ItemData utility2 = null;
    }

    public static class HumanoidExtension
    {
        private static readonly ConditionalWeakTable<Humanoid, HumanoidExtraUtilitySlots> data = new ConditionalWeakTable<Humanoid, HumanoidExtraUtilitySlots>();

        public static HumanoidExtraUtilitySlots GetExtraUtilityData(this Humanoid humanoid) => data.GetOrCreateValue(humanoid);

        public static ItemDrop.ItemData GetExtraUtility(this Humanoid humanoid, int index) => index == 0 ? humanoid.GetExtraUtilityData().utility1 : humanoid.GetExtraUtilityData().utility2;

        public static ItemDrop.ItemData SetExtraUtility(this Humanoid humanoid, int index, ItemDrop.ItemData item) => index == 0 ? humanoid.GetExtraUtilityData().utility1 = item : humanoid.GetExtraUtilityData().utility2 = item;
    }

    public static class ExtraUtilitySlots
    {
        private static readonly List<ItemDrop.ItemData> tempItems = new List<ItemDrop.ItemData>();
        private static readonly HashSet<StatusEffect> tempEffects = new HashSet<StatusEffect>();

        public static ItemDrop.ItemData Item1 => Player.m_localPlayer?.GetExtraUtility(0);

        public static ItemDrop.ItemData Item2 => Player.m_localPlayer?.GetExtraUtility(1);

        public static bool IsItemEquipped(ItemDrop.ItemData item) => GetUtilityItemIndex(item) != -1;

        public static bool HaveEmptySlot() => GetEmptySlot() != -1;

        public static int GetEmptySlot() => ExtraSlots.extraUtilitySlotsAmount.Value > 0 && Item1 == null ? 0 : (ExtraSlots.extraUtilitySlotsAmount.Value > 1 && Item2 == null ? 1 : -1);

        public static IEnumerable<ItemDrop.ItemData> GetEquippedItems()
        {
            tempItems.Clear();

            if (Item1 != null)
                tempItems.Add(Item1);

            if (Item2 != null)
                tempItems.Add(Item2);

            return tempItems;
        }

        public static int GetUtilityItemIndex(ItemDrop.ItemData item)
        {
            if (item == null)
                return -1;

            if (ExtraSlots.extraUtilitySlotsAmount.Value > 0 && Item1 == item)
                return 0;

            if (ExtraSlots.extraUtilitySlotsAmount.Value > 1 && Item2 == item)
                return 1;

            return -1;
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

                    GetEquippedItems().DoIf(item => (bool)item.m_shared.m_equipStatusEffect, item => tempEffects.Add(item.m_shared.m_equipStatusEffect));

                    GetEquippedItems().DoIf(item => __instance.HaveSetEffect(item), item => tempEffects.Add(item.m_shared.m_setStatusEffect));
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

                    foreach (ItemDrop.ItemData item in GetEquippedItems())
                        __result += item.m_shared.m_weight;
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

                    if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility && __instance.m_utilityItem != null && !IsItemEquipped(item) && (__state = GetEmptySlot()) != -1)
                    {
                        item.m_shared.m_itemType = tempType;
                        if (__instance.m_visEquipment && __instance.m_visEquipment.m_isPlayer)
                            item.m_shared.m_equipEffect.Create(__instance.transform.position + Vector3.up, __instance.transform.rotation);
                    }
                }

                private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects, int __state, ref bool __result)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    if (item == null || item.m_shared.m_itemType != tempType || __state == -1)
                        return;

                    item.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Utility;

                    if (__instance.GetExtraUtility(__state) != null)
                        __instance.UnequipItem(__instance.GetExtraUtility(__state), triggerEquipEffects);

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
                private static void Postfix(Humanoid __instance, ItemDrop.ItemData item)
                {
                    if (item == null)
                        return;

                    if (__instance.GetExtraUtility(0) == item)
                    {
                        __instance.SetExtraUtility(0, null);
                        __instance.SetupEquipment();
                    }

                    if (__instance.GetExtraUtility(1) == item)
                    {
                        __instance.SetExtraUtility(1, null);
                        __instance.SetupEquipment();
                    }
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

                    GetEquippedItems().Do(item => __instance.UnequipItem(item, triggerEquipEffects: false));
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.GetEquipmentEitrRegenModifier))]
            private static class Player_GetEquipmentEitrRegenModifier_ExtraUtility
            {
                private static void Postfix(Player __instance, ref float __result)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    if (Item1 != null)
                        __result += Item1.m_shared.m_eitrRegenModifier;

                    if (Item2 != null)
                        __result += Item2.m_shared.m_eitrRegenModifier;
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipment))]
            private static class Humanoid_UpdateEquipment_ExtraUtilityDurabilityDrain
            {
                private static void Postfix(Humanoid __instance, float dt)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    DrainDurability(Item1, dt);
                    DrainDurability(Item2, dt);

                    void DrainDurability(ItemDrop.ItemData item, float dt)
                    {
                        if (item != null && item.m_shared.m_useDurability)
                            __instance.DrainEquipedItemDurability(item, dt);
                    }
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.ApplyArmorDamageMods))]
            private static class Player_ApplyArmorDamageMods_ExtraUtility
            {
                private static void Postfix(Player __instance, ref HitData.DamageModifiers mods)
                {
                    if (!IsValidPlayer(__instance))
                        return;

                    if (Item1 != null)
                        mods.Apply(Item1.m_shared.m_damageModifiers);

                    if (Item2 != null)
                        mods.Apply(Item2.m_shared.m_damageModifiers);
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
                    {
                        if (Item1 != null)
                            __instance.m_equipmentModifierValues[i] += (float)Player.s_equipmentModifierSourceFields[i].GetValue(Item1.m_shared);

                        if (Item2 != null)
                            __instance.m_equipmentModifierValues[i] += (float)Player.s_equipmentModifierSourceFields[i].GetValue(Item2.m_shared);
                    }
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
            private static class Player_OnInventoryChanged_ValidateExtraUtilitySlots
            {
                private static void Postfix(Player __instance)
                {
                    if (IsValidPlayer(__instance) && !__instance.m_isLoading)
                    {
                        if (Item1 != null && !PlayerInventory.ContainsItem(Item1))
                        {
                            Player.m_localPlayer.SetExtraUtility(0, null);
                            Player.m_localPlayer.SetupEquipment();
                        }

                        if (Item2 != null && !PlayerInventory.ContainsItem(Item2))
                        {
                            Player.m_localPlayer.SetExtraUtility(1, null);
                            Player.m_localPlayer.SetupEquipment();
                        }
                    }
                }
            }
        }
    }
}
