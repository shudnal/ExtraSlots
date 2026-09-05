using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.PlayerInventoryOperations;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class ItemsSlotsValidation
    {
        private static int playerLoadDepth;
        private static bool validationInProgress;

        public static void ValidateSlots() => SlotsValidation.MarkDirty();
        public static void ValidateItems() => ItemsValidation.MarkDirty();

        public static void Validate()
        {
            // Player.Load has several independent compatibility/topology postfixes. They are allowed
            // to mark validation dirty, but the actual pass must run only after all of them finish so
            // restore-slot metadata cannot be pruned between reconciliation passes.
            if (validationInProgress || playerLoadDepth > 0 || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading || PlayerInventory == null)
                return;

            if (!ItemsValidation.IsDirty && !SlotsValidation.IsDirty)
                return;

            // Equip and inventory callbacks can call API.UpdateSlots synchronously. Let them mark
            // work dirty, but never restore the same deferred entry recursively before it is consumed.
            validationInProgress = true;
            try
            {
                const int maxPasses = 64;
                for (int pass = 0; pass < maxPasses; pass++)
                {
                    ItemsValidation.Validate();
                    SlotsValidation.Validate();

                    bool placementValid = IsInventoryPlacementValid(out _, out _);
                    bool restoredDeferred = placementValid && DeferredInventory.TryRestoreAvailable();

                    if (!ItemsValidation.IsDirty && !SlotsValidation.IsDirty && !restoredDeferred)
                        return;
                }

                if (ItemsValidation.IsDirty || SlotsValidation.IsDirty)
                    LogWarning("Inventory validation reached its safety pass limit before reaching a stable state.");
            }
            finally
            {
                validationInProgress = false;
            }
        }

        private static bool TryPlaceEquippedItem(ItemDrop.ItemData item)
        {
            if (item == null || PlayerInventory == null || !PlayerInventory.ContainsItem(item))
                return false;

            if (TryFindFreeEquipmentSlotForItem(item, out Slot freeEquipmentSlot))
            {
                Vector2i oldPosition = item.m_gridPos;
                if (MoveToSlot(item, freeEquipmentSlot))
                {
                    LogDebug($"Equipped item {item.m_shared.m_name} was moved from {oldPosition} to equipment slot {freeEquipmentSlot} {freeEquipmentSlot.GridPosition}");
                    return true;
                }
            }

            if (!TryFindFirstUnequippedSlotForItem(item, out Slot slotToSwap))
                return false;

            if (slotToSwap.IsFree)
            {
                Vector2i oldPosition = item.m_gridPos;
                if (MoveToSlot(item, slotToSwap))
                {
                    LogDebug($"Equipped item {item.m_shared.m_name} was moved from {oldPosition} to unequipped slot {slotToSwap} {slotToSwap.GridPosition}");
                    return true;
                }

                return false;
            }

            ItemDrop.ItemData itemToSwap = slotToSwap.Item;
            if (itemToSwap == null)
                return false;

            Vector2i equippedOldPosition = item.m_gridPos;
            Vector2i unequippedOldPosition = itemToSwap.m_gridPos;
            if (!Swap(item, itemToSwap))
                return false;

            LogDebug($"Equipped item {item.m_shared.m_name} {equippedOldPosition} was swapped with unequipped {itemToSwap.m_shared.m_name} {unequippedOldPosition} into slot {slotToSwap}");
            return true;
        }

        private static bool TryRelocateInvalidSlotWithoutDisplacing(ItemDrop.ItemData item)
        {
            Inventory inventory = PlayerInventory;
            if (item == null || inventory == null || !inventory.ContainsItem(item))
                return false;

            ClearCachedItems();

            if (TryGetSavedPlayerSlot(item, out Slot savedSlot)
                && savedSlot.IsActive
                && savedSlot.ItemFits(item)
                && (savedSlot.IsFree || ReferenceEquals(savedSlot.Item, item))
                && MoveToSlot(item, savedSlot))
            {
                return true;
            }

            bool topFirst = inventory.TopFirst(item);
            if (topFirst)
            {
                for (int y = 0; y < InventoryHeightPlayer; y++)
                    for (int x = 0; x < InventoryWidth; x++)
                        if (inventory.GetItemAt(x, y) == null && Move(item, new Vector2i(x, y)))
                            return true;
            }
            else
            {
                for (int y = InventoryHeightPlayer - 1; y >= 0; y--)
                    for (int x = 0; x < InventoryWidth; x++)
                        if (inventory.GetItemAt(x, y) == null && Move(item, new Vector2i(x, y)))
                            return true;
            }

            if (TryFindEmptyQuickSlot(out Slot quickSlot) && quickSlot.ItemFits(item) && MoveToSlot(item, quickSlot))
                return true;

            if (TryFindFreeSlotForItem(item, out Slot freeSlot) && MoveToSlot(item, freeSlot))
                return true;

            // A slot becoming inactive or rejecting its resident is a topology transition, not a
            // reason to reshuffle unrelated regular items merely to manufacture space. If all direct
            // destinations are occupied, preserve the resident in deferred storage and retry when
            // capacity/topology changes.
            return DeferredInventory.DeferExisting(item, "no non-disruptive destination remained for an invalid slot resident");
        }

        private static bool EquippedItemNeedsEquipmentSlot(ItemDrop.ItemData item)
        {
            if (item == null || CurrentPlayer == null || !CurrentPlayer.IsItemEquiped(item) || !IsEquipmentSlotItem(item))
                return false;

            if (GetItemSlot(item) is not Slot currentSlot || !currentSlot.IsEquipmentSlot)
                return true;

            return !customSlotItemsCanUseRegularEquipmentSlots.Value && !currentSlot.IsCustomSlot && IsCustomSlotItem(item);
        }

        internal static class SlotsValidation
        {
            private static bool isDirty;

            internal static bool IsDirty => isDirty;
            internal static void MarkDirty() => isDirty = true;

            internal static void Validate()
            {
                if (!isDirty || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading || PlayerInventory == null)
                    return;

                isDirty = false;

                using (Batch(PlayerInventory))
                {
                    EnsureCurrentGeometry();

                    for (int i = 0; i < slots.Length; i++)
                    {
                        Slot slot = slots[i];
                        ItemDrop.ItemData item = slot.Item;
                        if (item == null)
                            continue;

                        // A cache entry is never allowed to make a slot claim an item that has already moved away.
                        if (item.m_gridPos != slot.GridPosition)
                        {
                            slot.ClearItemCache();
                            continue;
                        }

                        if (IsPlacementValid(item, out PlacementIssue issue, out _))
                            continue;

                        LogInfo($"SlotValidation: Item {item.m_shared.m_name} no longer has a valid placement in slot {slot}");

                        if ((item.m_equipped || Player.m_localPlayer.IsItemEquiped(item)) && TryPlaceEquippedItem(item))
                            continue;

                        if (issue == PlacementIssue.InactiveSlot || issue == PlacementIssue.InvalidForSlot)
                            TryRelocateInvalidSlotWithoutDisplacing(item);
                        else
                            RelocateToBestAvailable(item, deferIfNoSpace: true);
                    }
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.SetupEquipment))]
            private static class Humanoid_SetupEquipment_MarkSlotsDirty
            {
                private static void Postfix(Humanoid __instance)
                {
                    if (__instance is Player player && IsValidPlayer(player) && !player.m_isLoading)
                        MarkDirty();
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
            private static class Player_OnInventoryChanged_ValidateInventory
            {
                private static void Postfix(Player __instance)
                {
                    ClearCachedItems();

                    if (!IsValidPlayer(__instance) || __instance.m_isLoading)
                        return;

                    ItemsValidation.MarkDirty();
                    MarkDirty();
                }
            }
        }

        internal static class ItemsValidation
        {
            private static bool isDirty;

            internal static bool IsDirty => isDirty;
            internal static void MarkDirty() => isDirty = true;

            internal static void Validate()
            {
                if (!isDirty || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading || PlayerInventory?.m_inventory == null)
                    return;

                isDirty = false;

                using (Batch(PlayerInventory))
                {
                    EnsureCurrentGeometry();
                    RepairStructuralIntegrity();

                    List<ItemDrop.ItemData> itemsInGridOrder = PlayerInventory.m_inventory
                        .Select((item, index) => new { Item = item, Index = index })
                        .OrderBy(entry => entry.Item?.m_gridPos.y ?? int.MaxValue)
                        .ThenBy(entry => entry.Item?.m_gridPos.x ?? int.MaxValue)
                        .ThenBy(entry => entry.Index)
                        .Select(entry => entry.Item)
                        .ToList();

                    foreach (ItemDrop.ItemData item in itemsInGridOrder)
                    {
                        if (item == null)
                            continue;

                        if (!IsPlacementValid(item, out PlacementIssue issue, out Slot currentSlot))
                        {
                            LogWarning($"ItemsValidation: Item {item.m_shared.m_name} {item.m_gridPos} has invalid placement: {DescribePlacementIssue(issue)}");

                            if (issue == PlacementIssue.InvalidStack)
                            {
                                Remove(item);
                                continue;
                            }

                            // If an equipped item was invalidated by a topology change, first move it
                            // directly to another equipment slot. The generic relocation path prefers
                            // regular cells and would otherwise move the same item twice in one pass.
                            bool relocated = (item.m_equipped || CurrentPlayer?.IsItemEquiped(item) == true)
                                && TryPlaceEquippedItem(item);

                            if (!relocated)
                            {
                                relocated = issue == PlacementIssue.InactiveSlot || issue == PlacementIssue.InvalidForSlot
                                    ? TryRelocateInvalidSlotWithoutDisplacing(item)
                                    : RelocateToBestAvailable(item, deferIfNoSpace: true);
                            }

                            if (!relocated || !PlayerInventory.ContainsItem(item))
                                continue;

                            if (!IsPlacementValid(item, out issue, out currentSlot))
                            {
                                LogWarning($"ItemsValidation: Item {item.m_shared.m_name} remains invalid after relocation attempt: {DescribePlacementIssue(issue)}");
                                continue;
                            }
                        }

                        if (EquippedItemNeedsEquipmentSlot(item))
                        {
                            LogInfo($"ItemsValidation: Equipped item {item.m_shared.m_name} {item.m_gridPos} is not in its preferred equipment slot");
                            TryPlaceEquippedItem(item);
                        }
                    }

                    if (!IsInventoryPlacementValid(out ItemDrop.ItemData invalidItem, out PlacementIssue invariantIssue))
                    {
                        string itemName = invalidItem?.m_shared?.m_name ?? "<unknown>";
                        LogWarning($"ItemsValidation: Player inventory placement invariant is still broken by {itemName}: {DescribePlacementIssue(invariantIssue)}");
                    }

                    // Slot return-address metadata is only needed while an item is in transit.
                    // Remove it only from items that are currently in a valid active slot.
                    foreach (ItemDrop.ItemData item in PlayerInventory.m_inventory.ToList())
                    {
                        if (item != null
                            && IsPlacementValid(item, out _, out Slot currentSlot)
                            && currentSlot != null)
                        {
                            PruneLastEquippedSlotFromItem(item);
                        }
                    }
                }
            }

            [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
            internal static class Inventory_MoveAll_ValidateItemPositions
            {
                private static void Postfix(Inventory __instance, Inventory fromInventory)
                {
                    if (__instance == PlayerInventory || fromInventory == PlayerInventory)
                    {
                        MarkDirty();
                        SlotsValidation.MarkDirty();
                    }
                }
            }

            [HarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory))]
            internal static class TombStone_EasyFitInInventory_ValidateItemPositions
            {
                private static void Postfix(Player player)
                {
                    if (IsValidPlayer(player))
                    {
                        MarkDirty();
                        SlotsValidation.MarkDirty();
                    }
                }
            }

            [HarmonyPatch]
            public static class Humanoid_OnEquipUnequip
            {
                private static IEnumerable<MethodBase> TargetMethods()
                {
                    yield return AccessTools.Method(typeof(Humanoid), nameof(Humanoid.EquipItem));
                    yield return AccessTools.Method(typeof(Humanoid), nameof(Humanoid.UnequipItem));
                }

                private static void Prefix(Humanoid __instance)
                {
                    if (IsValidPlayer(__instance))
                    {
                        MarkDirty();
                        SlotsValidation.MarkDirty();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load_DeferValidationUntilAllPostfixesFinish
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix() => playerLoadDepth++;

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Exception __exception)
            {
                playerLoadDepth = Math.Max(0, playerLoadDepth - 1);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        public static class InventoryGui_Show_ValidateItems
        {
            private static void Postfix()
            {
                if (Player.m_localPlayer == null)
                    return;

                ValidateSlots();
                ValidateItems();
            }
        }
    }
}
