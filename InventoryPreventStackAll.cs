using HarmonyLib;
using System;
using System.Collections.Generic;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    internal static class InventoryPreventStackAll
    {
        private sealed class StackAllState : IDisposable
        {
            private readonly Inventory inventory;
            private readonly IDisposable batch;
            private readonly List<(int Index, ItemDrop.ItemData Item)> removedItems = new List<(int, ItemDrop.ItemData)>();
            private bool restored;
            private bool disposed;

            internal StackAllState(Inventory inventory)
            {
                this.inventory = inventory;
                batch = PlayerInventoryOperations.Batch(inventory);
            }

            internal void RemoveProtectedItems()
            {
                for (int i = inventory.m_inventory.Count - 1; i >= 0; i--)
                {
                    ItemDrop.ItemData item = inventory.m_inventory[i];
                    if (item == null || AllowStackAll(item))
                        continue;

                    removedItems.Add((i, item));
                    inventory.m_inventory.RemoveAt(i);
                }

                LogDebug($"Removed {removedItems.Count} items from player inventory before StackAll");
            }

            internal void Restore()
            {
                if (restored)
                    return;

                for (int i = removedItems.Count - 1; i >= 0; i--)
                {
                    (int index, ItemDrop.ItemData item) = removedItems[i];
                    if (!inventory.m_inventory.Contains(item))
                        inventory.m_inventory.Insert(Math.Min(index, inventory.m_inventory.Count), item);
                }

                restored = true;
                ClearCachedItems();
                LogDebug($"Returned {removedItems.Count} items to player inventory after StackAll");
                removedItems.Clear();
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                try
                {
                    Restore();
                }
                finally
                {
                    disposed = true;
                    batch.Dispose();
                }
            }
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
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory fromInventory, out StackAllState __state)
            {
                __state = null;
                if (fromInventory == null || fromInventory != PlayerInventory || Compatibility.ZenBeehiveCompat.IsHoneyOpen)
                    return;

                // Every nested call owns only its own removed items. Notification batching keeps
                // ordinary observers from seeing the temporarily incomplete player inventory.
                __state = new StackAllState(fromInventory);
                __state.RemoveProtectedItems();
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(StackAllState __state) => __state?.Restore();

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(StackAllState __state, Exception __exception)
            {
                __state?.Dispose();
                return __exception;
            }
        }
    }
}
