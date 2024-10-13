using HarmonyLib;
using static ExtraSlots.Slots;

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

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.SlotsUsedPercentage))]
        private static class Inventory_SlotsUsedPercentage_ExcludeRedundantSlots
        {
            private static void Postfix(Inventory __instance, ref float __result)
            {
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

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int))]
        private static class Inventory_AddItem_ItemData_amount_x_y_AutoFixInventorySize
        {
            private static void Prefix(Inventory __instance, int y)
            {
                if (__instance != PlayerInventory)
                    return;

                if (y < __instance.m_height)
                    return;

                __instance.m_height = InventoryHeightFull;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        private static class Inventory_AddItem_ItemData_
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

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        public static class InventoryGrid_DropItem_DropPrevention
        {
            public static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos)
            {
                ItemDrop.ItemData itemAt = __instance.m_inventory.GetItemAt(pos.x, pos.y);
                if (itemAt == item)
                    return true;

                if (__instance.m_inventory != PlayerInventory && __instance != InventoryGui.instance.m_playerGrid)
                    return true;

                /*bool targetEquipment = EquipmentSlots.TryGetSlotIndex(pos, out int targetSlot) && __instance.m_inventory == PlayerInventory;

                // If the dropped item is unfit for target slot
                if (item != null && targetEquipment && !EquipmentSlots.IsValidItemForSlot(item, targetSlot))
                {
                    LogInfo($"DropItem Prevented dropping {item.m_shared.m_name} {item.m_gridPos} into unfit slot {slots[targetSlot]}");
                    return false;
                }

                // If dropped item is in slot and interchanged item is unfit for dragged item slot
                if (item != null && itemAt != null && fromInventory == PlayerInventory && EquipmentSlots.TryGetItemSlot(item, out int currentSlot) && !EquipmentSlots.IsValidItemForSlot(itemAt, currentSlot))
                {
                    LogInfo($"DropItem Prevented swapping {item.m_shared.m_name} {slots[currentSlot]} with unfit item {itemAt.m_shared.m_name} {pos}");
                    return false;
                }

                // If item is unequipped and will not be automatically equipped after drop
                if (itemAt == null && item != null && AutoEquip.Value.IsOff() && KeepUnequippedInSlot.Value.IsOff() && targetEquipment)
                {
                    LogInfo($"DropItem Prevented dropping {item.m_shared.m_name} {item.m_gridPos} into slot {slots[targetSlot]} with both autoequip and keep unequipped disabled");
                    return false;
                }*/

                return true;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
        public static class InventoryGui_OnSelectedItem_DragValidation_AutoEquip
        {
            /*public static bool Prefix(InventoryGui __instance, InventoryGrid grid, ref ItemDrop.ItemData item, ref Vector2i pos, ref Vector2i __state)
            {
                __state = new Vector2i(-1, -1);

                Player localPlayer = Player.m_localPlayer;
                if (localPlayer.IsTeleporting())
                    return true;

                if (!__instance.m_dragGo)
                    return true;

                if (grid == __instance.m_playerGrid && EquipmentSlots.TryGetSlotIndex(pos, out int slotIndex))
                {
                    // If the dragged item is unfit for target slot
                    if (__instance.m_dragItem != null && !EquipmentSlots.IsValidItemForSlot(__instance.m_dragItem, slotIndex))
                    {
                        LogInfo($"OnSelectedItem Prevented dragging {__instance.m_dragItem.m_shared.m_name} {__instance.m_dragItem.m_gridPos} into unfit slot {slots[slotIndex]}");
                        return false;
                    }

                    // If item is unequipped and will not be automatically equipped
                    if (__instance.m_dragItem != null && AutoEquip.Value.IsOff() && KeepUnequippedInSlot.Value.IsOff() && !Player.m_localPlayer.IsItemEquiped(__instance.m_dragItem))
                    {
                        LogInfo($"OnSelectedItem Dragging converted into Queued equip action on {__instance.m_dragItem.m_shared.m_name} {__instance.m_dragItem.m_gridPos}");

                        Player.m_localPlayer.QueueEquipAction(__instance.m_dragItem);

                        // Clear item and position to prevent autoequip and unequip
                        item = null!;
                        pos = __state;
                        __instance.SetupDragItem(null, null, 1);
                        return false;
                    }
                }

                // If drag item is in slot and interchanged item is unfit for dragged item slot
                if (__instance.m_dragItem != null && item != null && EquipmentSlots.TryGetItemSlot(__instance.m_dragItem, out int slotIndex1) && !EquipmentSlots.IsValidItemForSlot(item, slotIndex1))
                {
                    LogInfo($"OnSelectedItem Prevented swapping {__instance.m_dragItem.m_shared.m_name} {slots[slotIndex1]} with unfit item {item.m_shared.m_name}");
                    return false;
                }

                // Save position dragged from to check on postfix
                if (__instance.m_dragInventory == PlayerInventory && __instance.m_dragItem != null)
                    __state = __instance.m_dragItem.m_gridPos;

                return true;
            }

            public static void Postfix(InventoryGui __instance, InventoryGrid grid, Vector2i pos, ref Vector2i __state)
            {
                // If dragging is in progress
                if (__instance.m_dragGo)
                    return;

                if (pos == __state)
                    return;

                if (grid == __instance.m_playerGrid)
                    CheckAutoEquip(pos);

                if (__state != new Vector2i(-1, -1))
                    CheckAutoEquip(__state);
            }

            private static void CheckAutoEquip(Vector2i pos)
            {
                ItemDrop.ItemData item = PlayerInventory.GetItemAt(pos.x, pos.y);
                if (EquipmentSlots.IsItemAtSlot(item) && AutoEquip.Value.IsOn())
                    Player.m_localPlayer.EquipItem(item);
                else
                    Player.m_localPlayer.UnequipItem(item);
            }*/
        }
    }
}
