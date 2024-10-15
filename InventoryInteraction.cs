using HarmonyLib;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;
using System.Linq;
using System.Collections.Generic;

namespace ExtraSlots
{
    internal class InventoryInteraction
    {
        public static void UpdatePlayerInventorySize()
        {
            if (Player.m_localPlayer == null)
                return;

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

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Save))]
        private static class Inventory_Save_SaveLastEquippedSlots
        {
            private static void Prefix(Inventory __instance)
            {
                if (__instance != PlayerInventory)
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
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
        private static class Inventory_GetEmptySlots_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, ref bool __state)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightPlayer;
                __state = true;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref int __result, bool __state)
            {
                if (!__state)
                    return;

                __instance.m_height = InventoryHeightFull;

                __result += GetEmptyQuickSlots();
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
                if (__result == new Vector2i(-1, -1)
                    && InventoryGui.instance.m_craftTimer >= InventoryGui.instance.m_craftDuration
                    && InventoryGui.instance.m_craftUpgradeItem is ItemDrop.ItemData item
                    && TryFindFreeSlotForItem(item, out Slot slot))
                {
                    __result = slot.GridPosition;
                }

                if (__result == new Vector2i(-1, -1) && TryFindFreeEquipmentSlotForItem(Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot, out Slot slot1))
                    __result = slot1.GridPosition;

                if (__result == new Vector2i(-1, -1) && TryFindFreeSlotForItem(Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot, out Slot slot2))
                    __result = slot2.GridPosition;

                if (__result == new Vector2i(-1, -1))
                    __result = FindEmptyQuickSlot();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
        private static class Inventory_HaveEmptySlot_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, ref bool __state)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightPlayer;
                __state = true;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref bool __result, bool __state)
            {
                if (!__state)
                    return;

                __instance.m_height = InventoryHeightFull;
                __result = __result || HaveEmptyQuickSlot();
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        private static class InventoryGrid_DropItem_DropPrevention
        {
            public static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos)
            {
                if (item == null)
                    return true;

                ItemDrop.ItemData itemAt = __instance.m_inventory.GetItemAt(pos.x, pos.y);
                if (itemAt == item)
                    return true;

                // If the dropped item is unfit for target slot
                if (__instance.m_inventory == PlayerInventory && GetSlotInGrid(pos) is Slot slot && !slot.ItemFit(item))
                {
                    LogInfo($"DropItem Prevented dropping {item.m_shared.m_name} {item.m_gridPos} into unfit slot {slot}");
                    return false;
                }

                // If dropped item is in slot and interchanged item is unfit for dragged item slot
                if (itemAt != null && fromInventory == PlayerInventory && GetSlotInGrid(item.m_gridPos) is Slot slot1 && !slot1.ItemFit(itemAt))
                {
                    LogInfo($"DropItem Prevented swapping {item.m_shared.m_name} {slot1} with unfit item {itemAt.m_shared.m_name} {pos}");
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int))]
        private static class Inventory_AddItem_ItemData_amount_x_y_TargetPositionRerouting
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, ItemDrop.ItemData item, ref int x, ref int y)
            {
                if (__instance != PlayerInventory)
                    return;

                if (item == null)
                    return;

                // If another item is at grind - let stack logic go
                if (__instance.GetItemAt(x, y) != null)
                    return;

                // If the dropped item fits for target slot
                if (GetSlotInGrid(new Vector2i(x, y)) is not Slot slot || slot.ItemFit(item))
                    return;

                if (!TryFindFreeSlotForItem(item, out Slot freeSlot))
                    return;

                LogInfo($"AddItem X Y Rerouted {item.m_shared.m_name} from {x},{y} to slot {freeSlot} {freeSlot.GridPosition}");
                x = freeSlot.GridPosition.x;
                y = freeSlot.GridPosition.y;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int amount, ref bool __result)
            {
                if (__instance == PlayerInventory && Inventory_AddItem_OnLoad_FindAppropriateSlot.inCall && !__result)
                {
                    // Prevent item disappearing
                    ItemDrop.ItemData itemData = item.Clone();
                    itemData.m_stack = amount;

                    if (TryFindFreeEquipmentSlotForItem(itemData, out Slot slot))
                        itemData.m_gridPos = slot.GridPosition;
                    else if (TryFindFreeSlotForItem(itemData, out Slot slot1))
                        itemData.m_gridPos = slot1.GridPosition;
                    else
                        itemData.m_gridPos = new Vector2i(0, InventoryHeightFull); // Put out of grid and item will find its place sooner or later

                    __instance.m_inventory.Add(itemData);
                    item.m_stack -= amount;
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class Inventory_CanAddItem_ItemData_TryFindAppropriateExtraSlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (__instance != PlayerInventory)
                    return;

                if (__result)
                    return;

                __result = TryFindFreeSlotForItem(item, out _);
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
                if (__instance.GetItemAt(pos.x, pos.y) != null || GetSlotInGrid(pos) is not Slot slot || slot.ItemFit(item))
                    return;

                // If inventory has available free stack items with the same quality - let stack logic go
                if (item.m_shared.m_maxStackSize > 1)
                {
                    int freeStacks = __instance.GetAllItems()
                        .Where(itemInv => item.m_shared.m_name == itemInv.m_shared.m_name && item.m_quality == itemInv.m_quality && item.m_worldLevel == itemInv.m_worldLevel)
                        .Sum(itemInv => itemInv.m_shared.m_maxStackSize - itemInv.m_stack);

                    if (freeStacks > item.m_stack)
                        return;
                }

                if (!TryFindFreeSlotForItem(item, out Slot freeSlot))
                    return;

                LogInfo($"AddItem Pos Rerouted {item.m_shared.m_name} from {pos} to slot {freeSlot} {freeSlot.GridPosition}");
                pos = freeSlot.GridPosition;
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
            private static void Prefix(Inventory __instance, string name, ref Vector2i pos, bool equipped)
            {
                inCall = true;

                if (__instance != PlayerInventory)
                    return;

                bool extraInventory = pos.y >= InventoryHeightPlayer;
                if (!extraInventory && !equipped)
                    return;

                if (__instance.GetItemAt(pos.x, pos.y) == null)
                    return;

                ItemDrop component = ObjectDB.instance?.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                if (component == null)
                    return;

                ItemDrop.ItemData item = component.m_itemData;

                if (equipped)
                {
                    if (TryFindFreeEquipmentSlotForItem(item, out Slot slot1))
                    {
                        pos = slot1.GridPosition;
                        return;
                    }
                    else if (TryFindFirstUnequippedSlotForItem(item, out Slot slot))
                    {
                        pos = slot.GridPosition;
                        return;
                    }
                }
                
                if (TryFindFreeSlotForItem(item, out Slot slot3))
                    pos = slot3.GridPosition;
                else
                    pos = FindEmptyQuickSlot();
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix() => inCall = false;
        }
    }
}
