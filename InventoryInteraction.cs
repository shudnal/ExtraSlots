using HarmonyLib;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace ExtraSlots
{
    public static class InventoryInteraction
    {
        public static void UpdatePlayerInventorySize()
        {
            if (CurrentPlayer == null)
                return;

            if (CurrentPlayer.m_inventory.m_height != InventoryHeightFull)
            {
                LogInfo($"Player inventory height changed {CurrentPlayer.m_inventory.m_height} -> {InventoryHeightFull}");
                CurrentPlayer.m_inventory.m_height = InventoryHeightFull;
                CurrentPlayer.m_inventory.Changed();
            }
            
            if (CurrentPlayer.m_tombstone?.GetComponent<Container>() is Container tombstone)
                tombstone.m_height = Mathf.Max(tombstone.m_height, InventoryHeightFull);

            ClearCachedItems();
            ItemsSlotsValidation.ValidateItems();
            ItemsSlotsValidation.ValidateSlots();
        }

        public static float GetItemWeightFactor(Slot slot)
        {
            if (slot.IsEquipmentSlot)
                return itemWeightFactorEquipmentSlots.Value;

            if (slot.IsQuickSlot)
                return itemWeightFactorQuickSlots.Value;

            if (slot.IsAmmoSlot)
                return itemWeightFactorAmmoSlots.Value;

            if (slot.IsFoodSlot)
                return itemWeightFactorFoodSlots.Value;

            if (slot.IsMiscSlot)
                return itemWeightFactorMiscSlots.Value;

            return 1f;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
        private static class Player_Awake_SetInventoryHeight
        {
            private static void Postfix(Player __instance)
            {
                __instance.m_inventory.m_height = InventoryHeightFull;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned_UpdateInventoryOnSpawn
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                if (!IsAwaitingForSlotsUpdate())
                    UpdatePlayerInventorySize();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update_UpdateInventoryHeight
        {
            private static Container tombstoneContainer = null!;

            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                __instance.m_inventory.m_height = InventoryHeightFull;

                tombstoneContainer ??= __instance.m_tombstone.GetComponent<Container>();
                if (tombstoneContainer != null)
                    tombstoneContainer.m_height = Mathf.Max(tombstoneContainer.m_height, __instance.m_inventory.m_height);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        private static class Player_Save_SaveLastEquippedSlots
        {
            private static void Prefix(Player __instance)
            {
                if (__instance.GetInventory() != PlayerInventory)
                    return;

                SaveLastEquippedSlotsToItems();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
        public static class Player_AutoPickup_PreventAutoPickupInExtraSlots
        {
            public static bool preventAddItem = false;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance) => preventAddItem = preventAutoPickup.Value && __instance == CurrentPlayer;

            [HarmonyPriority(Priority.First)]
            private static void Postfix() => preventAddItem = false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.SlotsUsedPercentage))]
        private static class Inventory_SlotsUsedPercentage_ExcludeRedundantSlots
        {
            private static void Postfix(Inventory __instance, ref float __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __result = (float)__instance.m_inventory.Count / InventorySizeActive * 100f;
                LogDebug($"Inventory.SlotsUsedPercentage: {__result}");
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
        private static class Inventory_GetEmptySlots_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref int __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __result = InventoryHeightPlayer * __instance.m_width - __instance.m_inventory.Count(item => !API.IsItemInSlot(item)) + (Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem ? 0 :GetEmptyQuickSlots());
                LogDebug($"Inventory.GetEmptySlots: {__result}, PreventAutoPickupInQuickSlots: {Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem}");
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.FindEmptySlot))]
        private static class Inventory_FindEmptySlot_FindAppropriateSlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightPlayer;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref Vector2i __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightFull;

                if (__result == emptyPosition
                    && InventoryGui_DoCrafting_UpgradeInSlot.UpgradeSourceSlot is Slot sourceSlot
                    && InventoryGui_DoCrafting_UpgradeInSlot.UpgradeSourceItem is ItemDrop.ItemData upgradeItem)
                {
                    sourceSlot.ClearItemCache();
                    if (sourceSlot.IsFree && sourceSlot.ItemFits(upgradeItem))
                    {
                        __result = sourceSlot.GridPosition;
                        LogDebug($"Inventory.FindEmptySlot for upgraded item {upgradeItem.m_shared.m_name} {__result}");
                    }
                }

                if (__result == emptyPosition && TryFindFreeEquipmentSlotForItem(Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot, out Slot slot1))
                {
                    __result = slot1.GridPosition;
                    LogDebug($"Inventory.FindEmptySlot free equipment slot for AddItem_ByName item {Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot.m_shared.m_name} {__result}");
                }

                if (__result == emptyPosition && TryFindFreeSlotForItem(Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot, out Slot slot2))
                {
                    __result = slot2.GridPosition;
                    LogDebug($"Inventory.FindEmptySlot free slot for AddItem_ByName item {Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot.m_shared.m_name} {__result}");
                }

                if (__result == emptyPosition && !Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem)
                {
                    __result = FindEmptyQuickSlot();
                    LogDebug($"Inventory.FindEmptySlot free quick slot {__result}");
                }

                if (__result == emptyPosition && Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot != null && TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                {
                    __result = gridPos;
                    LogDebug($"Inventory.FindEmptySlot made free space for AddItem_ByName item {Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot.m_shared.m_name} {__result}");
                }
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.First)]
            private static void Finalizer(Inventory __instance)
            {
                if (__instance == PlayerInventory)
                    __instance.m_height = InventoryHeightFull;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        internal static class InventoryGui_DoCrafting_UpgradeInSlot
        {
            internal static Slot UpgradeSourceSlot;
            internal static ItemDrop.ItemData UpgradeSourceItem;
            private static bool wasEquipped;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(InventoryGui __instance)
            {
                ResetState();

                if (__instance.m_craftUpgradeItem is not ItemDrop.ItemData item)
                    return;

                UpgradeSourceSlot = GetSlotInGrid(item.m_gridPos);
                UpgradeSourceItem = item;
                wasEquipped = CurrentPlayer != null && CurrentPlayer.IsItemEquiped(item);
            }

            private static void Postfix()
            {
                Slot sourceSlot = UpgradeSourceSlot;
                bool shouldReequip = wasEquipped;
                ResetState();

                if (!shouldReequip || sourceSlot == null || CurrentPlayer == null)
                    return;

                sourceSlot.ClearItemCache();
                ItemDrop.ItemData upgradedItem = sourceSlot.Item;
                if (upgradedItem != null && !CurrentPlayer.IsItemEquiped(upgradedItem))
                    CurrentPlayer.EquipItem(upgradedItem, triggerEquipEffects: false);
            }

            private static void Finalizer() => ResetState();

            private static void ResetState()
            {
                UpgradeSourceSlot = null;
                UpgradeSourceItem = null;
                wasEquipped = false;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
        private static class Inventory_HaveEmptySlot_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref bool __result)
            {
                __result = __instance.GetEmptySlots() > 0;
            }
        }

        private static bool PassDropItem(string source, InventoryGrid grid, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos)
        {
            if (item.m_gridPos == pos)
                return true;

            // If the equipped item from slot is dropped at player inventory
            if (grid.m_inventory == PlayerInventory && GetItemSlot(item) is Slot itemSlot && itemSlot.IsEquipmentSlot && Player.m_localPlayer.IsItemEquiped(item))
            {
                if (GetSlotInGrid(pos) is not Slot posSlot)
                {
                    LogDebug($"{source} Prevented dropping equipped item {item.m_shared.m_name} {item.m_gridPos} into regular inventory {pos}");
                    return false;
                };

                if (!IsSameSlotType(itemSlot, posSlot))
                {
                    LogDebug($"{source} Prevented dropping equipped item {item.m_shared.m_name} {item.m_gridPos} into slot with other type {posSlot}");
                    return false;
                }
            }

            // If target slot is in player inventory and is extra slot
            if (grid.m_inventory == PlayerInventory && GetSlotInGrid(pos) is Slot targetSlot)
            {
                // If the dropped item is unfit for target slot
                if (!targetSlot.ItemFits(item))
                {
                    LogDebug($"{source} Prevented dropping {item.m_shared.m_name} {item.m_gridPos} into unfit slot {targetSlot}");
                    return false;
                }

                // If the dropped item is not from equipment slot and target item is equipped item at equipment slot
                if (targetSlot.IsEquipmentSlot && targetSlot.Item != null && Player.m_localPlayer.IsItemEquiped(targetSlot.Item) && (GetItemSlot(item) is not Slot fromSlot || !fromSlot.IsEquipmentSlot))
                {
                    LogDebug($"{source} Prevented dropping {item.m_shared.m_name} {item.m_gridPos} into occupied equipment slot {targetSlot}");
                    return false;
                }
            }

            ItemDrop.ItemData itemAt = grid.m_inventory.GetItemAt(pos.x, pos.y);

            // If dropped item is in slot and interchanged item is unfit for dragged item slot
            if (itemAt != null && fromInventory == PlayerInventory && GetSlotInGrid(item.m_gridPos) is Slot slot1 && !slot1.ItemFits(itemAt))
            {
                LogDebug($"{source} Prevented swapping {item.m_shared.m_name} {slot1} with unfit item {itemAt.m_shared.m_name} {pos}");
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
        public static class InventoryGui_OnSelectedItem_GetEquippedDragItem
        {
            public static bool Prefix(InventoryGui __instance, InventoryGrid grid, Vector2i pos)
            {
                if (Player.m_localPlayer && !Player.m_localPlayer.IsTeleporting() && __instance.m_dragGo && __instance.m_dragItem != null && __instance.m_dragInventory != null)
                    return PassDropItem("InventoryGui.OnSelectedItem", grid, __instance.m_dragInventory, __instance.m_dragItem, pos);

                return true;
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        public static class InventoryGrid_DropItem_DropPrevention
        {
            public static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos) => PassDropItem("InventoryGrid.DropItem", __instance, fromInventory, item, pos);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int))]
        private static class Inventory_AddItem_ItemData_amount_x_y_TargetPositionRerouting
        {
            [HarmonyPriority(Priority.Last)]
            private static void Prefix(Inventory __instance, ItemDrop.ItemData item, ref int x, ref int y)
            {
                if (__instance != PlayerInventory)
                {
                    // There is some nasty behaviour when a tombstone inventory is created with dimensions smaller than its saved item positions.
                    // Slot metadata identifies items that came from the player slot region, so keep the loading inventory large enough to preserve them.
                    if (Inventory_AddItem_OnLoad_FindAppropriateSlot.inCall
                        && item.m_customData.ContainsKey(customKeySlotID)
                        && item.m_customData.ContainsKey(customKeyPlayerID)
                        && (x >= __instance.m_width || y >= __instance.m_height))
                    {
                        int oldWidth = __instance.m_width;
                        int oldHeight = __instance.m_height;
                        __instance.m_width = Mathf.Max(__instance.m_width, x + 1);
                        __instance.m_height = Mathf.Max(__instance.m_height, y + 1);
                        LogDebug($"Inventory \"{__instance.m_name}\" loading: expanded {oldWidth}x{oldHeight} -> {__instance.m_width}x{__instance.m_height} for {item.m_shared.m_name} at {x},{y}");
                    }
                    return;
                }

                // Known materials and player keys are not yet loaded, custom components are not initialized, skip validation
                if (Inventory_AddItem_OnLoad_FindAppropriateSlot.inCall && CurrentPlayer.m_isLoading)
                    return;

                if (item == null)
                    return;

                // If another item is at grid - let stack logic go
                if (__instance.GetItemAt(x, y) is ItemDrop.ItemData gridTakenItem)
                {
                    LogDebug($"Inventory.AddItem X Y item {item.m_shared.m_name} adding at {x},{y} position is taken {gridTakenItem.m_shared.m_name}");
                    return;
                }

                // If the dropped item fits for target slot
                if (GetSlotInGrid(new Vector2i(x, y)) is not Slot slot || slot.ItemFits(item))
                    return;

                LogDebug($"Inventory.AddItem X Y item {item.m_shared.m_name} adding at {x},{y} unfits slot {slot} {slot.GridPosition}");

                if (TryFindFreeSlotForItem(item, out Slot freeSlot))
                {
                    LogDebug($"Inventory.AddItem X Y Rerouted {item.m_shared.m_name} from {x},{y} to free slot {freeSlot} {freeSlot.GridPosition}");
                    x = freeSlot.GridPosition.x;
                    y = freeSlot.GridPosition.y;
                    return;
                }

                if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                {
                    LogDebug($"Inventory.AddItem X Y Rerouted {item.m_shared.m_name} from {x},{y} to created free space {gridPos}");
                    x = gridPos.x;
                    y = gridPos.y;
                }
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int x, int y, int amount, ref bool __result)
            {
                if (__instance == PlayerInventory && Inventory_AddItem_OnLoad_FindAppropriateSlot.inCall && !__result)
                {
                    amount = Mathf.Min(amount, item.m_stack);

                    // Prevent item disappearing
                    ItemDrop.ItemData itemData = item.Clone();
                    itemData.m_stack = amount;

                    LogMessage($"Item dissappearing prevention at Inventory.AddItem_OnLoad -> Inventory.AddItem_ItemData_amount_x_y: item {item.m_shared.m_name} at {x},{y} amount {amount}");

                    Vector2i target = emptyPosition;
                    if (TryFindFreeEquipmentSlotForItem(itemData, out Slot equipmentSlot))
                    {
                        target = equipmentSlot.GridPosition;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y found free equipment slot for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {target}");
                    }
                    else if (TryFindFreeSlotForItem(itemData, out Slot slot))
                    {
                        target = slot.GridPosition;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y found free slot for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {target}");
                    }
                    else if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                    {
                        target = gridPos;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y made free space for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {target}");
                    }

                    bool inserted = target != emptyPosition && PlayerInventoryOperations.InsertExisting(itemData, target);
                    if (!inserted)
                    {
                        // Loading must never silently lose an item. Keep it represented just outside
                        // the current grid and let the invariant reconciliation settle/drop it once
                        // the player's full progression/config state is available.
                        Vector2i temporary = new Vector2i(0, InventoryHeightFull);
                        inserted = PlayerInventoryOperations.InsertForReconciliation(itemData, temporary);
                        LogWarning($"Inventory.AddItem_ItemData_amount_x_y temporarily staged {itemData.m_shared.m_name} at {temporary} for reconciliation");
                    }

                    if (inserted)
                    {
                        item.m_stack -= amount;
                        __result = true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class Inventory_CanAddItem_ItemData_TryFindAppropriateExtraSlot
        {
            private static readonly List<ItemDrop.ItemData> tempItems = new();

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightPlayer;

                tempItems.Clear();

                for (int i = __instance.m_inventory.Count - 1; i >= 0; i--)
                {
                    ItemDrop.ItemData invItem = __instance.m_inventory[i];
                    if (API.IsItemInSlot(invItem))
                    {
                        tempItems.Add(invItem);
                        __instance.m_inventory.RemoveAt(i);
                    }
                }

                tempItems.Reverse();
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightFull;

                if (tempItems.Count > 0)
                {
                    __instance.m_inventory.AddRange(tempItems);
                    tempItems.Clear();
                }

                if (__result)
                    return;

                int freeStackSpace = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel);
                int freeQuickSlotStackSpace = __instance.GetEmptySlots() * item.m_shared.m_maxStackSize;

                int sizeCombined = freeStackSpace + freeQuickSlotStackSpace;
                if (sizeCombined < 0)
                    sizeCombined = int.MaxValue;

                if (__result = sizeCombined >= stack)
                {
                    LogDebug($"Inventory.CanAddItem_ItemData_int item {item.m_shared.m_name} result {__result}, free stack space: {freeStackSpace}, free quick slot stack space: {freeQuickSlotStackSpace}, have free stack space");
                }
                else if (stack <= item.m_shared.m_maxStackSize && !Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem)
                {
                    if (__result = TryFindFreeSlotForItem(item, out Slot slot))
                        LogDebug($"Inventory.CanAddItem_ItemData_int item {item.m_shared.m_name} result {__result}, free stack space: {freeStackSpace}, free quick slot stack space: {freeQuickSlotStackSpace}, no free stack space, free single slot found {slot} {slot.GridPosition}");
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        private static class Inventory_AddItem_ItemData_TryFindAppropriateExtraSlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, bool __runOriginal, ref bool __result)
            {
                if (__instance != PlayerInventory)
                    return;

                if (__result || !__runOriginal)
                    return;

                if (Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem)
                    return;

                if (!TryFindFreeSlotForItem(item, out Slot slot))
                    return;

                LogDebug($"Inventory.AddItem_Item item {item.m_shared.m_name} found free slot {slot} {slot.GridPosition}");

                if (PlayerInventoryOperations.InsertExisting(item, slot.GridPosition))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
        private static class Inventory_AddItem_ItemData_pos_TargetPositionRerouting
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, ItemDrop.ItemData item, ref Vector2i pos)
            {
                if (__instance != PlayerInventory)
                    return;

                if (item == null)
                    return;

                // If already overlapping or not slot or slot fit - let logic go
                if (__instance.GetItemAt(pos.x, pos.y) != null || GetSlotInGrid(pos) is not Slot slot || slot.ItemFits(item))
                    return;

                // If inventory has available free stack items with the same quality - let stack logic go
                if (item.m_shared.m_maxStackSize > 1)
                {
                    int freeStacks = __instance.GetAllItems()
                        .Where(itemInv => item.m_shared.m_name == itemInv.m_shared.m_name && item.m_quality == itemInv.m_quality && item.m_worldLevel == itemInv.m_worldLevel)
                        .Sum(itemInv => itemInv.m_shared.m_maxStackSize - itemInv.m_stack);

                    if (freeStacks > item.m_stack)
                        return;

                    LogDebug($"Inventory.AddItem_Item_Vector2i item {item.m_shared.m_name}x{item.m_stack} adding at {pos} not enough free stack space {freeStacks}");
                }

                if (TryFindFreeSlotForItem(item, out Slot freeSlot))
                {
                    LogDebug($"Inventory.AddItem_Item_Vector2i Rerouted {item.m_shared.m_name} from {pos} to free slot {freeSlot} {freeSlot.GridPosition}");
                    pos = freeSlot.GridPosition;
                    return;
                }

                if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                {
                    LogDebug($"Inventory.AddItem_Item_Vector2i Rerouted {item.m_shared.m_name} from {pos} to created free space {gridPos}");
                    pos = gridPos;
                    return;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Vector2i), typeof(bool))]
        public static class Inventory_AddItem_ByName_FindAppropriateSlot
        {
            public static ItemDrop.ItemData itemToFindSlot = null;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, string name)
            {
                if (__instance != PlayerInventory)
                    return;

                ItemDrop component = ObjectDB.instance?.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                if (component == null)
                    return;

                if (component.m_itemData.m_shared.m_maxStackSize > 1)
                    return;

                itemToFindSlot = component.m_itemData;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix() => itemToFindSlot = null;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(string), typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Dictionary<string, string>), typeof(int), typeof(bool))]
        public static class Inventory_AddItem_OnLoad_FindAppropriateSlot
        {
            public static bool inCall = false;

            [HarmonyPriority(Priority.First)]
            private static void Prefix() => inCall = true;

            [HarmonyPriority(Priority.First)]
            private static void Postfix() => inCall = false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave))]
        private static class Inventory_MoveInventoryToGrave_UpdateGraveInventory
        {
            private static void Prefix(Inventory original)
            {
                if (original != PlayerInventory)
                    return;

                original.m_height = InventoryHeightFull;
            }
        }

        internal static void UpdateTotalWeight() => PlayerInventory?.UpdateTotalWeight();

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.UpdateTotalWeight))]
        public static class Inventory_UpdateTotalWeight_ApplyWeightFactor
        {
            public static bool inCall = false;

            private static void Prefix(Inventory __instance) => inCall = __instance == PlayerInventory;

            private static void Postfix() => inCall = false;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
        public static class ItemDrop_ItemData_GetWeight_UpdateTotalWeight_ApplyWeightFactor
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                if (!Inventory_UpdateTotalWeight_ApplyWeightFactor.inCall)
                    return;

                if (LightenedSlots.IsRowAffected(__instance.m_gridPos.y))
                    __result *= LightenedSlots.WeightFactor;
                else if (GetItemSlot(__instance) is Slot slot)
                    __result *= GetItemWeightFactor(slot);
            }
        }
    }
}
