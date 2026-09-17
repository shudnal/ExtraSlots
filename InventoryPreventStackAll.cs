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
                Player player = Player.m_localPlayer;
                for (int i = inventory.m_inventory.Count - 1; i >= 0; i--)
                {
                    ItemDrop.ItemData item = inventory.m_inventory[i];
                    if (item == null || AllowStackAll(item))
                        continue;

                    // Vanilla Inventory.StackAll already skips runtime-equipped items through
                    // Player.IsItemEquiped. Keep those items physically present for the whole call:
                    // temporarily removing them can make custom equipment providers discard their
                    // runtime equip state even though ItemData.m_equipped remains true.
                    if (player?.IsItemEquiped(item) == true)
                        continue;

                    removedItems.Add((i, item));
                    inventory.m_inventory.RemoveAt(i);
                }

                LogDebug($"Removed {removedItems.Count} unequipped protected items from player inventory before StackAll");
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
                LogDebug($"Returned {removedItems.Count} unequipped protected items to player inventory after StackAll");
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

    }
}
