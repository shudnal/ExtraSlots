using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    /// <summary>
    /// Resolves closed saved-slot permutations after all Player.Load migration/topology postfixes have
    /// run but before deferred validation is allowed to prune transport metadata. A cycle can be
    /// permuted in-place through MoveForTopology and therefore does not require a scratch inventory cell.
    /// </summary>
    internal static class SavedSlotCycleResolver
    {
        private static void Resolve(Inventory inventory)
        {
            if (inventory?.m_inventory == null)
                return;

            List<ItemDrop.ItemData> items = inventory.m_inventory.Where(item => item != null).ToList();
            Dictionary<ItemDrop.ItemData, Slot> assignments = new Dictionary<ItemDrop.ItemData, Slot>();
            foreach (ItemDrop.ItemData item in items)
            {
                if (!TryGetSavedPlayerSlot(item, out Slot savedSlot)
                    || savedSlot == null
                    || !savedSlot.IsActive
                    || !savedSlot.ItemFits(item)
                    || item.m_gridPos == savedSlot.GridPosition)
                {
                    continue;
                }

                assignments[item] = savedSlot;
            }

            if (assignments.Count < 2)
                return;

            HashSet<ItemDrop.ItemData> completed = new HashSet<ItemDrop.ItemData>();
            using (PlayerInventoryOperations.Batch(inventory))
            {
                foreach (ItemDrop.ItemData start in assignments.Keys.ToList())
                {
                    if (completed.Contains(start))
                        continue;

                    List<ItemDrop.ItemData> path = new List<ItemDrop.ItemData>();
                    Dictionary<ItemDrop.ItemData, int> pathIndex = new Dictionary<ItemDrop.ItemData, int>();
                    ItemDrop.ItemData current = start;

                    while (current != null && assignments.TryGetValue(current, out Slot targetSlot))
                    {
                        if (pathIndex.TryGetValue(current, out int cycleStart))
                        {
                            List<ItemDrop.ItemData> cycle = path.Skip(cycleStart).ToList();
                            if (cycle.Count > 1 && IsClosedCycle(inventory, cycle, assignments))
                            {
                                foreach (ItemDrop.ItemData cycleItem in cycle)
                                    PlayerInventoryOperations.MoveForTopology(cycleItem, assignments[cycleItem].GridPosition);

                                ClearCachedItems();
                                ExtraSlots.LogDebug($"Resolved saved-slot provenance cycle with {cycle.Count} resident(s) without scratch inventory space.");
                            }
                            break;
                        }

                        if (completed.Contains(current))
                            break;

                        pathIndex[current] = path.Count;
                        path.Add(current);

                        Vector2i target = targetSlot.GridPosition;
                        ItemDrop.ItemData occupant = inventory.GetItemAt(target.x, target.y);
                        if (occupant == null || ReferenceEquals(occupant, current) || !assignments.ContainsKey(occupant))
                            break;

                        current = occupant;
                    }

                    foreach (ItemDrop.ItemData visited in path)
                        completed.Add(visited);
                }
            }
        }

        private static bool IsClosedCycle(
            Inventory inventory,
            IReadOnlyCollection<ItemDrop.ItemData> cycle,
            IReadOnlyDictionary<ItemDrop.ItemData, Slot> assignments)
        {
            HashSet<ItemDrop.ItemData> cycleItems = new HashSet<ItemDrop.ItemData>(cycle);
            foreach (ItemDrop.ItemData item in cycle)
            {
                Vector2i target = assignments[item].GridPosition;
                ItemDrop.ItemData occupant = inventory.GetItemAt(target.x, target.y);
                if (occupant == null || !cycleItems.Contains(occupant))
                    return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load_ResolveSavedSlotCycles
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance)
            {
                if (__instance == null || __instance != Player.m_localPlayer || __instance.GetInventory() != PlayerInventory)
                    return;

                Resolve(__instance.GetInventory());
            }
        }
    }
}
