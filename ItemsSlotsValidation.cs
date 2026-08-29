using HarmonyLib;
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
        public static void ValidateSlots() => SlotsValidation.MarkDirty();
        public static void ValidateItems() => ItemsValidation.MarkDirty();

        public static void Validate()
        {
            if (!Player.m_localPlayer || Player.m_localPlayer.m_isLoading || PlayerInventory == null)
                return;

            if (!ItemsValidation.IsDirty && !SlotsValidation.IsDirty)
                return;

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

                        if (IsPlacementValid(item, out _, out _))
                            continue;

                        LogInfo($"SlotValidation: Item {item.m_shared.m_name} no longer has a valid placement in slot {slot}");

                        if ((item.m_equipped || Player.m_localPlayer.IsItemEquiped(item)) && TryPlaceEquippedItem(item))
                            continue;

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

                            if (!RelocateToBestAvailable(item, deferIfNoSpace: true) || !PlayerInventory.ContainsItem(item))
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
