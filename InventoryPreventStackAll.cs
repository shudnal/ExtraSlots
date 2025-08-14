using HarmonyLib;
using System.Collections.Generic;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    internal static class InventoryPreventStackAll
    {
        private static readonly List<ItemDrop.ItemData> removedItems = new List<ItemDrop.ItemData>();

        private static void RemoveItemsFromPlayerInventory()
        {
            for (int i = PlayerInventory.m_inventory.Count - 1; i >= 0; i--)
            {
                ItemDrop.ItemData item = PlayerInventory.m_inventory[i];
                if (AllowStackAll(item))
                    continue;

                removedItems.Add(item);
                PlayerInventory.m_inventory.RemoveAt(i);
            }

            LogDebug($"Removed {removedItems.Count} items from player inventory before StackAll");
        }

        private static void BringItemsBack()
        {
            if (removedItems.Count == 0)
                return;

            PlayerInventory.m_inventory.AddRange(removedItems);

            LogDebug($"Returned {removedItems.Count} items to player inventory after StackAll");
            removedItems.Clear();
        }

        private static bool AllowStackAll(ItemDrop.ItemData item)
        {
            if (GetItemSlot(item) is not Slot slot)
                return !hotbarPreventStackAll.Value || !Player.m_localPlayer.GetInventory().GetHotbar().Contains(item);

            if (slot.IsQuickSlot)
                return !quickSlotsPreventStackAll.Value;
            if (slot.IsEquipmentSlot)
                return !equipmentSlotsPreventStackAll.Value;
            if (slot.IsMiscSlot)
                return !miscSlotsPreventStackAll.Value;
            if (slot.IsAmmoSlot)
                return !ammoSlotsPreventStackAll.Value;
            if (slot.IsFoodSlot)
                return !foodSlotsPreventStackAll.Value;

            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
        private static class Inventory_StackAll_PreventStackingItemsFromSlots
        {
            public static bool inCall = false;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory fromInventory)
            {
                if ((inCall = fromInventory == PlayerInventory && !Compatibility.ZenBeehiveCompat.IsHoneyOpen) == false)
                    return;

                RemoveItemsFromPlayerInventory();
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix()
            {
                if (inCall)
                    BringItemsBack();

                inCall = false;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        private static class Inventory_Changed_BringItemsBack
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance == PlayerInventory && Inventory_StackAll_PreventStackingItemsFromSlots.inCall)
                    BringItemsBack();
            }
        }
    }
}