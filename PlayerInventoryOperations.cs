using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    /// <summary>
    /// Centralized player inventory mutations and placement invariants.
    /// Grid moves keep ItemData identity intact, invalidate slot caches immediately,
    /// and aggregate Inventory.Changed() while a mutation batch is active.
    /// </summary>
    internal static class PlayerInventoryOperations
    {
        internal enum PlacementIssue
        {
            None,
            MissingInventory,
            MissingItem,
            InventoryGeometryMismatch,
            InvalidStack,
            DuplicateItemReference,
            OutOfBounds,
            Overlap,
            OrphanedSlotCell,
            InactiveSlot,
            InvalidForSlot
        }

        private sealed class ItemReferenceComparer : IEqualityComparer<ItemDrop.ItemData>
        {
            internal static readonly ItemReferenceComparer Instance = new ItemReferenceComparer();

            public bool Equals(ItemDrop.ItemData x, ItemDrop.ItemData y) => ReferenceEquals(x, y);
            public int GetHashCode(ItemDrop.ItemData obj) => obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
        }

        private sealed class ChangeBatch : IDisposable
        {
            private readonly Inventory inventory;
            private bool disposed;

            internal ChangeBatch(Inventory inventory)
            {
                this.inventory = inventory;
                if (inventory == null)
                    return;

                batchDepth.TryGetValue(inventory, out int depth);
                batchDepth[inventory] = depth + 1;
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                if (inventory == null || !batchDepth.TryGetValue(inventory, out int depth))
                    return;

                if (depth > 1)
                {
                    batchDepth[inventory] = depth - 1;
                    return;
                }

                batchDepth.Remove(inventory);
                if (!pendingChanged.Remove(inventory))
                    return;

                inventory.Changed();
            }
        }

        private static readonly Dictionary<Inventory, int> batchDepth = new Dictionary<Inventory, int>();
        private static readonly HashSet<Inventory> pendingChanged = new HashSet<Inventory>();

        internal static IDisposable Batch(Inventory inventory = null) => new ChangeBatch(inventory ?? PlayerInventory);
        private static IDisposable AutomaticBatch(Inventory inventory) => new ChangeBatch(inventory);

        // Placement invariant: every item belongs to exactly one in-bounds cell. A regular cell
        // must be inside the visible player region; a hidden cell must resolve to one existing,
        // active slot whose current validator accepts the item. No overlaps or orphaned tail cells.
        internal static bool IsPlacementValid(ItemDrop.ItemData item) => IsPlacementValid(item, out _, out _);

        internal static bool IsPlacementValid(ItemDrop.ItemData item, out PlacementIssue issue, out Slot slot)
        {
            issue = PlacementIssue.None;
            slot = null;

            Inventory inventory = PlayerInventory;
            if (inventory == null)
            {
                issue = PlacementIssue.MissingInventory;
                return false;
            }

            if (inventory.m_height != InventoryHeightFull)
            {
                issue = PlacementIssue.InventoryGeometryMismatch;
                return false;
            }

            if (item == null || !inventory.ContainsItem(item))
            {
                issue = PlacementIssue.MissingItem;
                return false;
            }

            int referenceCount = 0;
            for (int i = 0; i < inventory.m_inventory.Count && referenceCount < 2; i++)
                if (ReferenceEquals(inventory.m_inventory[i], item))
                    referenceCount++;

            if (referenceCount != 1)
            {
                issue = PlacementIssue.DuplicateItemReference;
                return false;
            }

            if (item.m_stack < 1)
            {
                issue = PlacementIssue.InvalidStack;
                return false;
            }

            Vector2i pos = item.m_gridPos;
            if (!IsInsideFullInventory(pos))
            {
                issue = PlacementIssue.OutOfBounds;
                return false;
            }

            if (inventory.GetOtherItemAt(pos.x, pos.y, item) != null)
            {
                issue = PlacementIssue.Overlap;
                return false;
            }

            if (pos.y < InventoryHeightPlayer)
                return true;

            slot = GetSlotInGrid(pos);
            if (slot == null || slot.IsEmptySlot)
            {
                issue = PlacementIssue.OrphanedSlotCell;
                return false;
            }

            if (!slot.IsActive)
            {
                issue = PlacementIssue.InactiveSlot;
                return false;
            }

            if (!slot.ItemFits(item))
            {
                issue = PlacementIssue.InvalidForSlot;
                return false;
            }

            return true;
        }

        internal static bool IsInventoryPlacementValid(out ItemDrop.ItemData invalidItem, out PlacementIssue issue)
        {
            invalidItem = null;
            issue = PlacementIssue.None;

            Inventory inventory = PlayerInventory;
            if (inventory == null)
            {
                issue = PlacementIssue.MissingInventory;
                return false;
            }

            if (inventory.m_height != InventoryHeightFull)
            {
                issue = PlacementIssue.InventoryGeometryMismatch;
                return false;
            }

            HashSet<ItemDrop.ItemData> seenItems = new HashSet<ItemDrop.ItemData>(ItemReferenceComparer.Instance);
            HashSet<Vector2i> seenPositions = new HashSet<Vector2i>();

            foreach (ItemDrop.ItemData item in inventory.m_inventory)
            {
                if (item == null)
                {
                    issue = PlacementIssue.MissingItem;
                    return false;
                }

                invalidItem = item;

                if (!seenItems.Add(item))
                {
                    issue = PlacementIssue.DuplicateItemReference;
                    return false;
                }

                if (item.m_stack < 1)
                {
                    issue = PlacementIssue.InvalidStack;
                    return false;
                }

                Vector2i pos = item.m_gridPos;
                if (!IsInsideFullInventory(pos))
                {
                    issue = PlacementIssue.OutOfBounds;
                    return false;
                }

                if (!seenPositions.Add(pos))
                {
                    issue = PlacementIssue.Overlap;
                    return false;
                }

                if (pos.y < InventoryHeightPlayer)
                    continue;

                Slot slot = GetSlotInGrid(pos);
                if (slot == null || slot.IsEmptySlot)
                {
                    issue = PlacementIssue.OrphanedSlotCell;
                    return false;
                }

                if (!slot.IsActive)
                {
                    issue = PlacementIssue.InactiveSlot;
                    return false;
                }

                if (!slot.ItemFits(item))
                {
                    issue = PlacementIssue.InvalidForSlot;
                    return false;
                }
            }

            invalidItem = null;
            issue = PlacementIssue.None;
            return true;
        }

        internal static string DescribePlacementIssue(PlacementIssue issue) => issue switch
        {
            PlacementIssue.MissingInventory => "player inventory is unavailable",
            PlacementIssue.MissingItem => "item is not in player inventory",
            PlacementIssue.InventoryGeometryMismatch => "player inventory height does not match the current ExtraSlots topology",
            PlacementIssue.InvalidStack => "item has an invalid stack size",
            PlacementIssue.DuplicateItemReference => "the same item instance occurs more than once in player inventory",
            PlacementIssue.OutOfBounds => "item is outside the current inventory geometry",
            PlacementIssue.Overlap => "item overlaps another item",
            PlacementIssue.OrphanedSlotCell => "item is in an orphaned or hidden slot cell",
            PlacementIssue.InactiveSlot => "item is in an inactive slot",
            PlacementIssue.InvalidForSlot => "item does not fit its current slot",
            _ => "placement is valid"
        };

        internal static bool RepairStructuralIntegrity()
        {
            Inventory inventory = PlayerInventory;
            if (inventory?.m_inventory == null)
                return false;

            bool changed = false;
            HashSet<ItemDrop.ItemData> seen = new HashSet<ItemDrop.ItemData>(ItemReferenceComparer.Instance);
            for (int i = 0; i < inventory.m_inventory.Count;)
            {
                ItemDrop.ItemData item = inventory.m_inventory[i];
                if (item == null || !seen.Add(item))
                {
                    if (item == null)
                        LogWarning("Player inventory contained a null item entry. Redundant entry was removed.");
                    else
                        LogWarning($"Player inventory contained duplicate reference to {item.m_shared?.m_name ?? "<unknown>"}. Redundant entry was removed without deleting the item.");

                    inventory.m_inventory.RemoveAt(i);
                    changed = true;
                    continue;
                }

                i++;
            }

            if (changed)
            {
                ClearCachedItems();
                MarkChanged(inventory);
            }

            return changed;
        }

        internal static bool EnsureCurrentGeometry()
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null)
                return false;

            if (inventory.m_height == InventoryHeightFull)
                return true;

            LogInfo($"Player inventory height reconciled {inventory.m_height} -> {InventoryHeightFull}");
            inventory.m_height = InventoryHeightFull;
            ClearCachedItems();
            MarkChanged(inventory);
            return true;
        }

        internal static bool InsertExisting(ItemDrop.ItemData item, Vector2i target)
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null || item == null || inventory.ContainsItem(item) || !CanOccupySemantically(item, target)
                || inventory.GetItemAt(target.x, target.y) != null)
                return false;

            item.m_gridPos = target;
            inventory.m_inventory.Add(item);
            ClearCachedItems();
            MarkChanged(inventory);
            return true;
        }

        // Explicit escape hatch for migrations that must keep ItemData represented until the
        // normal reconciliation pass can find a semantic home. Never use for steady-state moves.
        internal static bool InsertForReconciliation(ItemDrop.ItemData item, Vector2i temporaryPosition)
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null || item == null || inventory.ContainsItem(item))
                return false;

            item.m_gridPos = temporaryPosition;
            inventory.m_inventory.Add(item);
            ClearCachedItems();
            MarkChanged(inventory);
            ItemsSlotsValidation.ValidateItems();
            ItemsSlotsValidation.ValidateSlots();
            return true;
        }

        internal static bool Remove(ItemDrop.ItemData item)
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null || item == null || !inventory.m_inventory.Remove(item))
                return false;

            ClearCachedItems();
            MarkChanged(inventory);
            return true;
        }

        internal static bool Move(ItemDrop.ItemData item, Vector2i target)
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null || item == null || !inventory.ContainsItem(item))
                return false;

            if (item.m_gridPos == target)
                return CanOccupy(item, target);

            if (!CanOccupy(item, target))
                return false;

            item.m_gridPos = target;
            ClearCachedItems();
            MarkChanged(inventory);
            return true;
        }

        // Topology rebuilds move several residents simultaneously. Intermediate coordinates may overlap,
        // so this operation intentionally skips semantic validation; callers must wrap the whole rebuild
        // in Batch() and finish by running the placement validators.
        internal static bool MoveForTopology(ItemDrop.ItemData item, Vector2i target)
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null || item == null || !inventory.ContainsItem(item) || item.m_gridPos == target)
                return false;

            item.m_gridPos = target;
            // Keep the pre-rebuild slot cache intact until every resident has moved. This is what
            // makes arbitrary slot permutations safe; the topology owner clears the cache once.
            MarkChanged(inventory);
            return true;
        }

        internal static bool MoveToSlot(ItemDrop.ItemData item, Slot slot) => slot != null && Move(item, slot.GridPosition);

        internal static bool Swap(ItemDrop.ItemData first, ItemDrop.ItemData second)
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null || first == null || second == null || ReferenceEquals(first, second)
                || !inventory.ContainsItem(first) || !inventory.ContainsItem(second))
                return false;

            Vector2i firstPos = first.m_gridPos;
            Vector2i secondPos = second.m_gridPos;
            if (!CanOccupySemantically(first, secondPos) || !CanOccupySemantically(second, firstPos))
                return false;

            first.m_gridPos = secondPos;
            second.m_gridPos = firstPos;
            ClearCachedItems();
            MarkChanged(inventory);
            return true;
        }

        internal static bool RelocateToBestAvailable(ItemDrop.ItemData item, bool dropIfNoSpace = false)
        {
            if (item == null || PlayerInventory == null || !PlayerInventory.ContainsItem(item))
                return false;

            ClearCachedItems();

            if (TryGetSavedPlayerSlot(item, out Slot previousSlot)
                && previousSlot.IsActive
                && previousSlot.ItemFits(item)
                && (previousSlot.IsFree || ReferenceEquals(previousSlot.Item, item))
                && MoveToSlot(item, previousSlot))
            {
                LogDebug($"Item {item.m_shared.m_name} was relocated to previous slot {previousSlot} {previousSlot.GridPosition}");
                return true;
            }

            Vector2i regularPos = FindEmptyRegularPosition(item);
            if (regularPos.x >= 0 && Move(item, regularPos))
            {
                LogDebug($"Item {item.m_shared.m_name} was relocated to regular inventory {regularPos}");
                return true;
            }

            if (TryFindEmptyQuickSlot(out Slot quickSlot) && MoveToSlot(item, quickSlot))
            {
                LogDebug($"Item {item.m_shared.m_name} was relocated to quick slot {quickSlot} {quickSlot.GridPosition}");
                return true;
            }

            if (TryFindFreeSlotForItem(item, out Slot freeSlot) && MoveToSlot(item, freeSlot))
            {
                LogDebug($"Item {item.m_shared.m_name} was relocated to slot {freeSlot} {freeSlot.GridPosition}");
                return true;
            }

            if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: false, out Vector2i freedPosition)
                && Move(item, freedPosition))
            {
                LogDebug($"Item {item.m_shared.m_name} was relocated to freed regular inventory position {freedPosition}");
                return true;
            }

            if (!dropIfNoSpace || CurrentPlayer == null)
                return false;

            LogWarning($"No valid inventory position remained for {item.m_shared.m_name}. Item will be dropped instead of leaving it hidden or invalid.");
            if (!CurrentPlayer.DropItem(PlayerInventory, item, item.m_stack))
            {
                LogWarning($"Failed to drop {item.m_shared.m_name}; its placement remains unresolved.");
                return false;
            }

            ClearCachedItems();
            MarkChanged(PlayerInventory);
            return true;
        }

        internal static bool TryMakeFreeSpaceInPlayerInventory(bool tryFindRegularInventorySlot, out Vector2i gridPos)
        {
            gridPos = emptyPosition;
            Inventory inventory = PlayerInventory;
            if (inventory == null)
                return false;

            if (tryFindRegularInventorySlot)
            {
                // Preserve the historical ExtraSlots choice: when this helper is explicitly asked
                // for regular space, prefer the bottom-right free cell before moving anything.
                for (int y = InventoryHeightPlayer - 1; y >= 0; y--)
                    for (int x = InventoryWidth - 1; x >= 0; x--)
                        if (inventory.GetItemAt(x, y) == null)
                        {
                            gridPos = new Vector2i(x, y);
                            return true;
                        }
            }

            List<ItemDrop.ItemData> candidates = inventory.GetAllItemsInGridOrder()
                .Where(item => item != null && item.m_gridPos.y >= 0 && item.m_gridPos.y < InventoryHeightPlayer)
                .OrderByDescending(item => item.m_gridPos.y)
                .ThenByDescending(item => item.m_gridPos.x)
                .ToList();

            ClearCachedItems();

            foreach (ItemDrop.ItemData candidate in candidates)
            {
                Vector2i oldPosition = candidate.m_gridPos;

                if (TryFindFreeEquipmentSlotForItem(candidate, out Slot equipmentSlot)
                    && MoveToSlot(candidate, equipmentSlot))
                {
                    LogDebug($"To create free space {candidate.m_shared.m_name} was moved from {oldPosition} to equipment slot {equipmentSlot} {equipmentSlot.GridPosition}");
                    gridPos = oldPosition;
                    return true;
                }

                if (TryFindFreeSlotForItem(candidate, out Slot slot)
                    && MoveToSlot(candidate, slot))
                {
                    LogDebug($"To create free space {candidate.m_shared.m_name} was moved from {oldPosition} to slot {slot} {slot.GridPosition}");
                    gridPos = oldPosition;
                    return true;
                }
            }

            return false;
        }

        internal static void ReconcileLoadedTopology()
        {
            Inventory inventory = PlayerInventory;
            if (inventory?.m_inventory == null)
                return;

            using (Batch(inventory))
            {
                EnsureCurrentGeometry();
                RepairStructuralIntegrity();
                ClearCachedItems();

                List<ItemDrop.ItemData> items = inventory.m_inventory.Where(item => item != null).ToList();

                // First honor explicit slot provenance. If an old target cell is currently occupied
                // by an untagged item from a formerly-visible row, move that intruder aside first.
                foreach (ItemDrop.ItemData item in items)
                {
                    if (!TryGetSavedPlayerSlot(item, out Slot savedSlot))
                        continue;

                    if (savedSlot == null || !savedSlot.IsActive || !savedSlot.ItemFits(item))
                    {
                        RelocateToBestAvailable(item, dropIfNoSpace: false);
                        continue;
                    }

                    ItemDrop.ItemData occupant = inventory.GetItemAt(savedSlot.GridPosition.x, savedSlot.GridPosition.y);
                    if (occupant != null && !ReferenceEquals(occupant, item))
                    {
                        bool occupantOwnsTarget = TryGetSavedPlayerSlot(occupant, out Slot occupantSaved)
                            && ReferenceEquals(occupantSaved, savedSlot);

                        if (!occupantOwnsTarget)
                            RelocateToBestAvailable(occupant, dropIfNoSpace: false);
                    }

                    if (!MoveToSlot(item, savedSlot))
                        RelocateToBestAvailable(item, dropIfNoSpace: false);
                }

                ClearCachedItems();

                // A persisted ExtraSlots resident is tagged in Player.Save. Therefore an untagged
                // item that loads into today's hidden region came from a previously-visible row (or
                // otherwise has no right to claim a slot cell), even if the current slot validator
                // would happen to accept it. Move it back through normal placement rules.
                foreach (ItemDrop.ItemData item in items)
                {
                    if (item == null || !inventory.ContainsItem(item) || item.m_gridPos.y < InventoryHeightPlayer)
                        continue;

                    if (TryGetSavedPlayerSlot(item, out _))
                        continue;

                    RelocateToBestAvailable(item, dropIfNoSpace: false);
                }

                ClearCachedItems();
                ItemsSlotsValidation.ValidateItems();
                ItemsSlotsValidation.ValidateSlots();
            }
        }

        internal readonly struct SimulatedEquipmentPlacement
        {
            internal readonly ItemDrop.ItemData Item;
            internal readonly Slot Slot;

            internal SimulatedEquipmentPlacement(ItemDrop.ItemData item, Slot slot)
            {
                Item = item;
                Slot = slot;
            }
        }

        internal static bool CanFitItems(IEnumerable<ItemDrop.ItemData> incomingItems) =>
            CanFitItems(incomingItems, out _);

        internal static bool CanFitItems(IEnumerable<ItemDrop.ItemData> incomingItems, out List<SimulatedEquipmentPlacement> equipmentPlacements)
        {
            equipmentPlacements = new List<SimulatedEquipmentPlacement>();

            Inventory inventory = PlayerInventory;
            if (inventory == null || incomingItems == null)
                return false;

            if (!IsInventoryPlacementValid(out _, out _))
                return false;

            List<ItemDrop.ItemData> incoming = incomingItems.ToList();
            if (incoming.Any(item => item == null || item.m_stack < 1))
                return false;
            if (incoming.Count == 0)
                return true;

            // EasyFit is allowed to auto-loot only when vanilla MoveAll can actually transfer every
            // stack with the current geometry and the exact resulting virtual inventory can then be
            // reconciled into the current ExtraSlots topology without dropping anything.
            VirtualMoveAllState transfer = new VirtualMoveAllState(inventory);
            if (!transfer.IsValid || !transfer.CanTransfer(incoming))
                return false;

            VirtualInventoryState settled = new VirtualInventoryState(transfer.Snapshot());
            if (!settled.IsValid || !settled.CanSettle())
                return false;

            HashSet<ItemDrop.ItemData> incomingSet = new HashSet<ItemDrop.ItemData>(incoming, ItemReferenceComparer.Instance);
            equipmentPlacements = settled.GetEquipmentPlacements(incomingSet);
            return true;
        }

        private sealed class VirtualSnapshotItem
        {
            internal ItemDrop.ItemData Template;
            internal int Stack;
            internal Vector2i Position;
        }

        private sealed class VirtualMoveAllState
        {
            private sealed class VirtualStack
            {
                internal ItemDrop.ItemData Template;
                internal int Stack;
            }

            private readonly Dictionary<Vector2i, VirtualStack> occupied = new Dictionary<Vector2i, VirtualStack>();
            private readonly int width;
            private readonly int height;
            internal bool IsValid { get; }

            internal VirtualMoveAllState(Inventory inventory)
            {
                width = inventory.m_width;
                height = inventory.m_height;

                bool valid = true;
                foreach (ItemDrop.ItemData item in inventory.m_inventory)
                {
                    if (item == null || item.m_stack < 1 || !IsInsidePhysicalInventory(item.m_gridPos) || occupied.ContainsKey(item.m_gridPos))
                    {
                        valid = false;
                        continue;
                    }

                    occupied[item.m_gridPos] = new VirtualStack { Template = item, Stack = item.m_stack };
                }

                IsValid = valid;
            }

            internal bool CanTransfer(IReadOnlyList<ItemDrop.ItemData> incoming)
            {
                List<(ItemDrop.ItemData Item, int Remaining)> secondPass = new List<(ItemDrop.ItemData, int)>();

                // Inventory.MoveAll first attempts to preserve every source grid position.
                foreach (ItemDrop.ItemData item in incoming)
                {
                    if (item == null || item.m_stack < 1)
                        return false;

                    Vector2i target = ResolveFirstPassTarget(item, item.m_gridPos);
                    int remaining = TryAddAtPosition(item, item.m_stack, target);
                    if (remaining > 0)
                        secondPass.Add((item, remaining));
                }

                // Failed or partially merged first-pass items then go through Inventory.AddItem(ItemData):
                // free stacks, a normal cell, a quick cell through ExtraSlots' FindEmptySlot patch,
                // then any valid ExtraSlots cell through its AddItem postfix fallback.
                foreach (var entry in secondPass)
                {
                    ItemDrop.ItemData item = entry.Item;
                    int remaining = ConsumeStackCapacity(item, entry.Remaining);
                    if (remaining <= 0)
                        continue;

                    Vector2i target = FindEmptyRegularPositionVirtual(item);
                    if (target.x < 0)
                        target = FindEmptyQuickPositionVirtual();
                    if (target.x < 0)
                        target = FindEmptyValidSlotPositionVirtual(item);
                    if (target.x < 0)
                        return false;

                    occupied[target] = new VirtualStack { Template = item, Stack = remaining };
                }

                return true;
            }

            internal IEnumerable<VirtualSnapshotItem> Snapshot()
            {
                foreach (KeyValuePair<Vector2i, VirtualStack> entry in occupied)
                {
                    yield return new VirtualSnapshotItem
                    {
                        Template = entry.Value.Template,
                        Stack = entry.Value.Stack,
                        Position = entry.Key
                    };
                }
            }

            private Vector2i ResolveFirstPassTarget(ItemDrop.ItemData item, Vector2i requested)
            {
                if (!IsInsidePhysicalInventory(requested) || requested.y < InventoryHeightPlayer)
                    return requested;

                // ExtraSlots' AddItem(x,y) rerouting intentionally does not run when the requested
                // cell is occupied: vanilla must first get its normal same-cell stack/swap chance.
                // Preserve that ordering in the simulation or EasyFit could claim a tombstone fits
                // by routing an item away from a stack attempt that the real MoveAll performs first.
                if (occupied.ContainsKey(requested))
                    return requested;

                Slot requestedSlot = GetSlotInGrid(requested);
                // The real AddItem(x,y) patch only reroutes a hidden coordinate when it resolves
                // to an actual ExtraSlots slot and that slot rejects the item. Orphaned tail cells
                // are left to vanilla for the first pass and are repaired by the settlement pass.
                if (requestedSlot == null || requestedSlot.ItemFits(item))
                    return requested;

                // Mirrors ExtraSlots' AddItem(item, amount, x, y) prefix: an invalid hidden target
                // is redirected to a free fitting slot first, then to regular space (creating it by
                // moving a regular resident into a fitting slot if necessary). If neither exists,
                // vanilla is allowed to try the original physical cell and final reconciliation
                // decides whether that result is safe enough for automatic grave recovery.
                Vector2i slotTarget = FindEmptyValidSlotPositionVirtual(item);
                if (slotTarget.x >= 0)
                    return slotTarget;

                if (TryMakeFreeRegularPositionVirtual(out Vector2i regularTarget))
                    return regularTarget;

                return requested;
            }

            private bool TryMakeFreeRegularPositionVirtual(out Vector2i freedPosition)
            {
                freedPosition = FindFirstEmptyRegularPositionVirtual();
                if (freedPosition.x >= 0)
                    return true;

                List<KeyValuePair<Vector2i, VirtualStack>> candidates = occupied
                    .Where(entry => entry.Key.y >= 0 && entry.Key.y < InventoryHeightPlayer)
                    .OrderByDescending(entry => entry.Key.y)
                    .ThenByDescending(entry => entry.Key.x)
                    .ToList();

                foreach (KeyValuePair<Vector2i, VirtualStack> candidate in candidates)
                {
                    Slot targetSlot = FindEmptyEquipmentSlotVirtual(candidate.Value.Template)
                        ?? FindEmptyValidSlotVirtual(candidate.Value.Template);
                    if (targetSlot == null)
                        continue;

                    occupied.Remove(candidate.Key);
                    occupied[targetSlot.GridPosition] = candidate.Value;
                    freedPosition = candidate.Key;
                    return true;
                }

                return false;
            }

            private Vector2i FindFirstEmptyRegularPositionVirtual()
            {
                int regularRows = Math.Min(InventoryHeightPlayer, height);
                for (int y = regularRows - 1; y >= 0; y--)
                    for (int x = width - 1; x >= 0; x--)
                    {
                        Vector2i pos = new Vector2i(x, y);
                        if (!occupied.ContainsKey(pos))
                            return pos;
                    }

                return emptyPosition;
            }

            private Slot FindEmptyEquipmentSlotVirtual(ItemDrop.ItemData item)
            {
                if (TryGetSavedPlayerSlot(item, out Slot savedSlot)
                    && savedSlot.IsEquipmentSlot
                    && IsUsableAndFree(savedSlot, item))
                {
                    return savedSlot;
                }

                Slot custom = GetEquipmentSlots().FirstOrDefault(slot => slot.IsCustomSlot && IsUsableAndFree(slot, item));
                if (custom != null)
                    return custom;

                if (IsCustomSlotItem(item) && !customSlotItemsCanUseRegularEquipmentSlots.Value)
                    return null;

                return GetEquipmentSlots().FirstOrDefault(slot => !slot.IsCustomSlot && IsUsableAndFree(slot, item));
            }

            private Slot FindEmptyValidSlotVirtual(ItemDrop.ItemData item)
            {
                if (TryGetSavedPlayerSlot(item, out Slot savedSlot) && IsUsableAndFree(savedSlot, item))
                    return savedSlot;

                Slot custom = slots.FirstOrDefault(slot => slot != null && slot.IsCustomSlot && IsUsableAndFree(slot, item));
                return custom ?? slots.FirstOrDefault(slot => slot != null && IsUsableAndFree(slot, item));
            }

            private int TryAddAtPosition(ItemDrop.ItemData item, int amount, Vector2i position)
            {
                if (!IsInsidePhysicalInventory(position))
                    return amount;

                if (!occupied.TryGetValue(position, out VirtualStack existing))
                {
                    occupied[position] = new VirtualStack { Template = item, Stack = amount };
                    return 0;
                }

                if (!CanStackAtPosition(existing.Template, item))
                    return amount;

                int capacity = Math.Max(0, existing.Template.m_shared.m_maxStackSize - existing.Stack);
                int moved = Math.Min(amount, capacity);
                existing.Stack += moved;
                return amount - moved;
            }

            private int ConsumeStackCapacity(ItemDrop.ItemData item, int amount)
            {
                int remaining = amount;
                foreach (VirtualStack existing in occupied.Values)
                {
                    if (!CanStackGlobally(existing.Template, item))
                        continue;

                    int capacity = Math.Max(0, existing.Template.m_shared.m_maxStackSize - existing.Stack);
                    if (capacity <= 0)
                        continue;

                    int moved = Math.Min(remaining, capacity);
                    existing.Stack += moved;
                    remaining -= moved;
                    if (remaining <= 0)
                        break;
                }

                return remaining;
            }

            private Vector2i FindEmptyRegularPositionVirtual(ItemDrop.ItemData item)
            {
                bool topFirst = PlayerInventory == null || PlayerInventory.TopFirst(item);
                int regularRows = Math.Min(InventoryHeightPlayer, height);
                if (topFirst)
                {
                    for (int y = 0; y < regularRows; y++)
                        for (int x = 0; x < width; x++)
                        {
                            Vector2i pos = new Vector2i(x, y);
                            if (!occupied.ContainsKey(pos))
                                return pos;
                        }
                }
                else
                {
                    for (int y = regularRows - 1; y >= 0; y--)
                        for (int x = 0; x < width; x++)
                        {
                            Vector2i pos = new Vector2i(x, y);
                            if (!occupied.ContainsKey(pos))
                                return pos;
                        }
                }

                return emptyPosition;
            }

            private Vector2i FindEmptyQuickPositionVirtual()
            {
                foreach (Slot slot in GetQuickSlots())
                    if (slot != null && slot.IsActive && IsInsidePhysicalInventory(slot.GridPosition) && !occupied.ContainsKey(slot.GridPosition))
                        return slot.GridPosition;

                return emptyPosition;
            }

            private Vector2i FindEmptyValidSlotPositionVirtual(ItemDrop.ItemData item) =>
                FindEmptyValidSlotVirtual(item)?.GridPosition ?? emptyPosition;

            private bool IsUsableAndFree(Slot slot, ItemDrop.ItemData item) => slot != null
                && !slot.IsEmptySlot
                && slot.IsActive
                && slot.ItemFits(item)
                && IsInsidePhysicalInventory(slot.GridPosition)
                && !occupied.ContainsKey(slot.GridPosition);

            private bool IsInsidePhysicalInventory(Vector2i pos) => pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;

            private static bool CanStackAtPosition(ItemDrop.ItemData target, ItemDrop.ItemData incoming) => target != null
                && incoming != null
                && target.m_shared.m_name == incoming.m_shared.m_name
                && target.m_worldLevel == incoming.m_worldLevel
                && (target.m_shared.m_maxQuality <= 1 || target.m_quality == incoming.m_quality);

            private static bool CanStackGlobally(ItemDrop.ItemData target, ItemDrop.ItemData incoming) => target != null
                && incoming != null
                && target.m_shared.m_name == incoming.m_shared.m_name
                && target.m_quality == incoming.m_quality
                && target.m_worldLevel == incoming.m_worldLevel;
        }

        private sealed class VirtualInventoryState
        {
            private sealed class VirtualItem
            {
                internal ItemDrop.ItemData Template;
                internal int Stack;
                internal Vector2i Position;
            }

            private readonly Dictionary<Vector2i, VirtualItem> occupied = new Dictionary<Vector2i, VirtualItem>();
            private readonly List<VirtualItem> items = new List<VirtualItem>();
            internal bool IsValid { get; }

            internal VirtualInventoryState(IEnumerable<VirtualSnapshotItem> snapshot)
            {
                bool valid = snapshot != null;
                if (snapshot != null)
                {
                    foreach (VirtualSnapshotItem entry in snapshot)
                    {
                        if (entry?.Template == null || entry.Stack < 1 || !IsInsideFullInventory(entry.Position) || occupied.ContainsKey(entry.Position))
                        {
                            valid = false;
                            continue;
                        }

                        VirtualItem item = new VirtualItem
                        {
                            Template = entry.Template,
                            Stack = entry.Stack,
                            Position = entry.Position
                        };
                        occupied[item.Position] = item;
                        items.Add(item);
                    }
                }

                IsValid = valid;
            }

            internal bool CanSettle()
            {
                if (!IsValid)
                    return false;

                foreach (VirtualItem item in items.ToList())
                {
                    if (IsSemanticallyValid(item))
                        continue;

                    if (!TryRelocate(item))
                        return false;
                }

                return items.All(IsSemanticallyValid);
            }

            internal List<SimulatedEquipmentPlacement> GetEquipmentPlacements(HashSet<ItemDrop.ItemData> incoming)
            {
                List<SimulatedEquipmentPlacement> result = new List<SimulatedEquipmentPlacement>();
                if (incoming == null || incoming.Count == 0)
                    return result;

                foreach (VirtualItem item in items)
                {
                    if (item?.Template == null || !incoming.Contains(item.Template))
                        continue;

                    Slot slot = GetSlotInGrid(item.Position);
                    if (slot != null && slot.IsEquipmentSlot && IsUsableSlot(slot, item.Template))
                        result.Add(new SimulatedEquipmentPlacement(item.Template, slot));
                }

                return result;
            }

            private bool TryRelocate(VirtualItem item)
            {
                if (TryGetSavedPlayerSlot(item.Template, out Slot savedSlot)
                    && IsUsableSlot(savedSlot, item.Template)
                    && IsFree(savedSlot.GridPosition))
                {
                    MoveVirtual(item, savedSlot.GridPosition);
                    return true;
                }

                if (TryFindEmptyRegular(item.Template, out Vector2i regular))
                {
                    MoveVirtual(item, regular);
                    return true;
                }

                if (TryFindFreeQuickSlot(out Slot quickSlot))
                {
                    MoveVirtual(item, quickSlot.GridPosition);
                    return true;
                }

                if (TryFindFreeSlot(item.Template, out Slot freeSlot))
                {
                    MoveVirtual(item, freeSlot.GridPosition);
                    return true;
                }

                if (TryMoveRegularOccupantIntoSlot(out Vector2i freedPosition))
                {
                    MoveVirtual(item, freedPosition);
                    return true;
                }

                return false;
            }

            private void MoveVirtual(VirtualItem item, Vector2i target)
            {
                occupied.Remove(item.Position);
                item.Position = target;
                occupied[target] = item;
            }

            private bool IsSemanticallyValid(VirtualItem item)
            {
                if (item == null || item.Template == null || item.Stack < 1 || !IsInsideFullInventory(item.Position))
                    return false;

                if (item.Position.y < InventoryHeightPlayer)
                    return true;

                Slot slot = GetSlotInGrid(item.Position);
                return IsUsableSlot(slot, item.Template);
            }

            private bool TryMoveRegularOccupantIntoSlot(out Vector2i freedPosition)
            {
                freedPosition = emptyPosition;

                List<VirtualItem> regularItems = items
                    .Where(item => item.Position.y >= 0 && item.Position.y < InventoryHeightPlayer)
                    .OrderByDescending(item => item.Position.y)
                    .ThenByDescending(item => item.Position.x)
                    .ToList();

                foreach (VirtualItem regularItem in regularItems)
                {
                    Slot target;
                    if (!TryFindFreeEquipmentSlot(regularItem.Template, out target)
                        && !TryFindFreeSlot(regularItem.Template, out target))
                        continue;

                    Vector2i oldPosition = regularItem.Position;
                    MoveVirtual(regularItem, target.GridPosition);
                    freedPosition = oldPosition;
                    return true;
                }

                return false;
            }

            private bool TryFindEmptyRegular(ItemDrop.ItemData item, out Vector2i destination)
            {
                bool topFirst = PlayerInventory == null || PlayerInventory.TopFirst(item);
                if (topFirst)
                {
                    for (int y = 0; y < InventoryHeightPlayer; y++)
                        for (int x = 0; x < InventoryWidth; x++)
                            if (IsFree(new Vector2i(x, y)))
                            {
                                destination = new Vector2i(x, y);
                                return true;
                            }
                }
                else
                {
                    for (int y = InventoryHeightPlayer - 1; y >= 0; y--)
                        for (int x = 0; x < InventoryWidth; x++)
                            if (IsFree(new Vector2i(x, y)))
                            {
                                destination = new Vector2i(x, y);
                                return true;
                            }
                }

                destination = emptyPosition;
                return false;
            }

            private bool TryFindFreeEquipmentSlot(ItemDrop.ItemData item, out Slot result)
            {
                result = null;

                if (TryGetSavedPlayerSlot(item, out Slot savedSlot)
                    && savedSlot.IsEquipmentSlot
                    && IsUsableSlot(savedSlot, item)
                    && IsFree(savedSlot.GridPosition))
                {
                    result = savedSlot;
                    return true;
                }

                result = GetEquipmentSlots().FirstOrDefault(slot => slot.IsCustomSlot && IsUsableSlot(slot, item) && IsFree(slot.GridPosition));
                if (result != null)
                    return true;

                if (IsCustomSlotItem(item) && !customSlotItemsCanUseRegularEquipmentSlots.Value)
                    return false;

                result = GetEquipmentSlots().FirstOrDefault(slot => !slot.IsCustomSlot && IsUsableSlot(slot, item) && IsFree(slot.GridPosition));
                return result != null;
            }

            private bool TryFindFreeQuickSlot(out Slot result)
            {
                result = GetQuickSlots().FirstOrDefault(slot => slot != null
                    && slot.IsActive
                    && !slot.IsEmptySlot
                    && IsFree(slot.GridPosition));
                return result != null;
            }

            private bool TryFindFreeSlot(ItemDrop.ItemData item, out Slot result)
            {
                result = null;

                if (TryGetSavedPlayerSlot(item, out Slot savedSlot)
                    && IsUsableSlot(savedSlot, item)
                    && IsFree(savedSlot.GridPosition))
                {
                    result = savedSlot;
                    return true;
                }

                result = slots.FirstOrDefault(slot => slot.IsCustomSlot && IsUsableSlot(slot, item) && IsFree(slot.GridPosition));
                if (result != null)
                    return true;

                result = slots.FirstOrDefault(slot => IsUsableSlot(slot, item) && IsFree(slot.GridPosition));
                return result != null;
            }

            private bool IsFree(Vector2i position) => !occupied.ContainsKey(position);

            private static bool IsUsableSlot(Slot slot, ItemDrop.ItemData item) => slot != null
                && !slot.IsEmptySlot
                && slot.IsActive
                && slot.ItemFits(item);
        }

        internal static void MarkChanged(Inventory inventory)
        {
            if (inventory == null)
                return;

            if (batchDepth.ContainsKey(inventory))
            {
                pendingChanged.Add(inventory);
                return;
            }

            inventory.Changed();
        }

        private static Vector2i FindEmptyRegularPosition(ItemDrop.ItemData item)
        {
            Inventory inventory = PlayerInventory;
            if (inventory == null)
                return emptyPosition;

            bool topFirst = item == null || inventory.TopFirst(item);
            if (topFirst)
            {
                for (int y = 0; y < InventoryHeightPlayer; y++)
                    for (int x = 0; x < InventoryWidth; x++)
                        if (inventory.GetItemAt(x, y) == null)
                            return new Vector2i(x, y);
            }
            else
            {
                for (int y = InventoryHeightPlayer - 1; y >= 0; y--)
                    for (int x = 0; x < InventoryWidth; x++)
                        if (inventory.GetItemAt(x, y) == null)
                            return new Vector2i(x, y);
            }

            return emptyPosition;
        }

        private static bool CanOccupy(ItemDrop.ItemData item, Vector2i target)
        {
            Inventory inventory = PlayerInventory;
            return inventory != null
                && CanOccupySemantically(item, target)
                && inventory.GetOtherItemAt(target.x, target.y, item) == null;
        }

        private static bool CanOccupySemantically(ItemDrop.ItemData item, Vector2i target)
        {
            if (item == null || !IsInsideFullInventory(target))
                return false;

            if (target.y < InventoryHeightPlayer)
                return true;

            Slot slot = GetSlotInGrid(target);
            return slot != null && !slot.IsEmptySlot && slot.IsActive && slot.ItemFits(item);
        }

        private static bool IsInsideFullInventory(Vector2i pos) => pos.x >= 0 && pos.x < InventoryWidth && pos.y >= 0 && pos.y < InventoryHeightFull;

        // Treat public Inventory mutations as transactions from the observer point of view.
        // ExtraSlots patches may have to relocate another player item while AddItem/MoveAll/RemoveItem is
        // still executing; keeping one outer batch alive guarantees OnInventoryChanged sees only
        // the settled state, and still fires once if the vanilla operation ultimately fails.
        [HarmonyPatch]
        private static class Inventory_AddItem_BatchPlayerChanges
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                AccessTools.GetDeclaredMethods(typeof(Inventory)).Where(method => method.Name == nameof(Inventory.AddItem));

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, out IDisposable __state)
            {
                __state = __instance == PlayerInventory ? AutomaticBatch(__instance) : null;
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(IDisposable __state, Exception __exception)
            {
                __state?.Dispose();
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
        private static class Inventory_MoveAll_BatchPlayerChanges
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, Inventory fromInventory, out IDisposable __state)
            {
                Inventory playerInventory = PlayerInventory;
                __state = playerInventory != null && (__instance == playerInventory || fromInventory == playerInventory)
                    ? AutomaticBatch(playerInventory)
                    : null;
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(IDisposable __state, Exception __exception)
            {
                __state?.Dispose();
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class Inventory_RemoveItem_BatchPlayerChanges
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                AccessTools.GetDeclaredMethods(typeof(Inventory)).Where(method => method.Name == nameof(Inventory.RemoveItem));

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, out IDisposable __state)
            {
                __state = __instance == PlayerInventory ? AutomaticBatch(__instance) : null;
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(IDisposable __state, Exception __exception)
            {
                __state?.Dispose();
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        private static class Inventory_Changed_DebounceMutationBatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Inventory __instance)
            {
                if (__instance == null || !batchDepth.ContainsKey(__instance))
                    return true;

                // Keep the synchronous state part current while only the observer notification is
                // delayed. Code inside the transaction may legitimately inspect current weight.
                __instance.UpdateTotalWeight();
                pendingChanged.Add(__instance);
                return false;
            }
        }
    }
}
