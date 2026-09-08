using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    /// <summary>
    /// Keeps tombstone EasyFit conservative when multiple incoming Utility items are configured as
    /// mutually exclusive. Sequential EquipItem calls can replace an earlier incoming member of the
    /// same group, so their carry-weight effects must never be counted as simultaneous capacity.
    /// </summary>
    internal static class TombstoneUtilityConflictGuard
    {
        private static float GetCarryWeightChange(ItemDrop.ItemData item)
        {
            StatusEffect effect = item?.m_shared?.m_equipStatusEffect;
            if (effect == null)
                return 0f;

            float value = 0f;
            effect.ModifyMaxCarryWeight(0f, ref value);
            return value;
        }

        private static bool IsItemToAutoEquip(ItemDrop.ItemData item)
        {
            if (item == null)
                return false;

            if (slotsTombstoneAutoEquipCarryWeightItemsEnabled.Value && GetCarryWeightChange(item) > 0f)
                return true;

            if (!slotsTombstoneAutoEquipEnabled.Value)
                return false;

            return TombStoneInteraction.ItemFitLists(
                item,
                TombStoneInteraction.autoEquipItemList,
                TombStoneInteraction.autoEquipWhiteList,
                TombStoneInteraction.autoEquipBlackList);
        }

        private static bool ShareConfiguredUniqueGroup(ItemDrop.ItemData first, ItemDrop.ItemData second)
        {
            if (first == null || second == null)
                return false;

            string firstName = first.m_shared.m_name;
            string secondName = second.m_shared.m_name;
            foreach (string tuple in preventUniqueUtilityItemsEquip.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                HashSet<string> group = new HashSet<string>(
                    tuple.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(name => name.GetItemName()));

                if (group.Contains(firstName) && group.Contains(secondName))
                    return true;
            }

            return false;
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory))]
        private static class TombStone_EasyFitInInventory_IncomingUtilityConflictGuard
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(TombStone __instance, ref bool __result)
            {
                if (!__result || __instance?.m_container?.GetInventory() == null)
                    return;

                if (!PlayerInventoryOperations.CanFitItems(
                        __instance.m_container.GetInventory().GetAllItems(),
                        out List<PlayerInventoryOperations.SimulatedEquipmentPlacement> placements))
                {
                    __result = false;
                    return;
                }

                List<ItemDrop.ItemData> utilities = placements
                    .Select(placement => placement.Item)
                    .Where(item => item != null
                        && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility
                        && IsItemToAutoEquip(item))
                    .ToList();

                for (int i = 0; i < utilities.Count; i++)
                {
                    for (int j = i + 1; j < utilities.Count; j++)
                    {
                        if (!ShareConfiguredUniqueGroup(utilities[i], utilities[j]))
                            continue;

                        // Zero-delta effects cannot make the weight fit calculation optimistic.
                        // Any non-zero member is ambiguous because one incoming item replaces another.
                        if (Math.Abs(GetCarryWeightChange(utilities[i])) > 0.0001f
                            || Math.Abs(GetCarryWeightChange(utilities[j])) > 0.0001f)
                        {
                            __result = false;
                            return;
                        }
                    }
                }
            }
        }
    }
}
