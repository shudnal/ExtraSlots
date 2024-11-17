using HarmonyLib;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace ExtraSlots
{
    internal class InventoryInteraction
    {
        public static void UpdatePlayerInventorySize()
        {
            if (Player.m_localPlayer == null)
                return;

            if (Player.m_localPlayer.m_inventory.m_height != InventoryHeightFull)
                LogInfo($"Player inventory height changed {Player.m_localPlayer.m_inventory.m_height} -> {InventoryHeightFull}");

            Player.m_localPlayer.m_inventory.m_height = InventoryHeightFull;
            Player.m_localPlayer.m_tombstone.GetComponent<Container>().m_height = InventoryHeightFull;
            Player.m_localPlayer.m_inventory.Changed();

            ItemsSlotsValidation.ValidateItems();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
        private static class Player_Awake_ExcludeRedundantSlots
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

                UpdatePlayerInventorySize();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update_UpdateInventoryHeight
        {
            private static Container tombstoneContainer = null!;

            private static void Postfix(Player __instance)
            {
                __instance.m_inventory.m_height = InventoryHeightFull;

                tombstoneContainer ??= __instance.m_tombstone.GetComponent<Container>();
                if (tombstoneContainer != null)
                    tombstoneContainer.m_height = __instance.m_inventory.m_height;
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

                __result = InventoryHeightPlayer * __instance.m_width - __instance.m_inventory.Count(item => !API.IsItemInSlot(item)) + GetEmptyQuickSlots();
                LogDebug($"Inventory.GetEmptySlots: {__result}");
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

                // TODO Make it more clear upgrading item crafting case
                if (__result == emptyPosition
                    && InventoryGui.instance.m_craftTimer >= InventoryGui.instance.m_craftDuration
                    && InventoryGui.instance.m_craftUpgradeItem is ItemDrop.ItemData item
                    && TryFindFreeSlotForItem(item, out Slot slot))
                {
                    __result = slot.GridPosition;
                    LogDebug($"Inventory.FindEmptySlot for upgraded item {item.m_shared.m_name} {__result}");
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

                if (__result == emptyPosition)
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
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
        private static class Inventory_HaveEmptySlot_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref bool __result, bool __state)
            {
                if (!__state)
                    return;

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

                    if (TryFindFreeEquipmentSlotForItem(itemData, out Slot equipmentSlot))
                    {
                        itemData.m_gridPos = equipmentSlot.GridPosition;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y found free equipment slot for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {itemData.m_gridPos}");
                    }
                    else if (TryFindFreeSlotForItem(itemData, out Slot slot))
                    {
                        itemData.m_gridPos = slot.GridPosition;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y found free slot for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {itemData.m_gridPos}");
                    }
                    else if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                    {
                        itemData.m_gridPos = gridPos;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y made free space for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {itemData.m_gridPos}");
                    }
                    else
                    {
                        itemData.m_gridPos = new Vector2i(InventoryWidth - 1, InventoryHeightFull - 1); // Put in the last slot and item will find its place sooner or later
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y item {itemData.m_shared.m_name} put in the last slot to find place later. Position rerouted {x},{y} -> {itemData.m_gridPos}");
                    }

                    __instance.m_inventory.Add(itemData);
                    item.m_stack -= amount;
                    __result = true;
                    __instance.Changed();
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class Inventory_CanAddItem_ItemData_TryFindAppropriateExtraSlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightPlayer;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightFull;

                if (__result)
                    return;

                int freeStackSpace = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel);
                int freeQuickSlotStackSpace = __instance.GetEmptySlots() * item.m_shared.m_maxStackSize;

                if (__result = freeStackSpace + freeQuickSlotStackSpace >= stack)
                    LogDebug($"Inventory.CanAddItem_ItemData_int item {item.m_shared.m_name} result {__result}, free stack space: {freeStackSpace}, free quick slot stack space: {freeQuickSlotStackSpace}, have free stack space");
                else if (stack <= item.m_shared.m_maxStackSize)
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
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (__instance != PlayerInventory)
                    return;

                if (__result)
                    return;

                if (!TryFindFreeSlotForItem(item, out Slot slot))
                    return;

                LogDebug($"Inventory.AddItem_Item item {item.m_shared.m_name} found free slot {slot} {slot.GridPosition}");

                item.m_gridPos = slot.GridPosition;
                __instance.m_inventory.Add(item);

                __instance.Changed();
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
    }
}
