using System;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    /// <summary>
    /// Identifies utility replacements for the tombstone carry-weight estimate. Conflicting
    /// items must not contribute simultaneous bonuses, but do not by themselves veto recovery.
    /// </summary>
    internal static class TombstoneUtilityConflictGuard
    {
        internal static bool ShareConfiguredUniqueGroup(ItemDrop.ItemData first, ItemDrop.ItemData second)
        {
            if (first?.m_shared == null || second?.m_shared == null)
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
    }
}
