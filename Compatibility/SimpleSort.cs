using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility
{
    public static class SimpleSort
    {
        public const string GUID = "aedenthorn.SimpleSort";

        private sealed class SortState : IDisposable
        {
            private readonly Inventory inventory;
            private readonly IDisposable batch;
            private readonly List<ItemDrop.ItemData> itemsToKeep = new List<ItemDrop.ItemData>();
            private bool restored;
            private bool disposed;

            internal SortState(Inventory inventory)
            {
                this.inventory = inventory;
                batch = PlayerInventoryOperations.Batch(inventory);
            }

            internal void KeepSlotItems()
            {
                foreach (Slot slot in slots)
                {
                    ItemDrop.ItemData item = slot.Item;
                    if (item == null || !inventory.ContainsItem(item))
                        continue;

                    itemsToKeep.Add(item);
                    inventory.m_inventory.Remove(item);
                    ExtraSlots.LogDebug($"SimpleSort.SortByType.Prefix: Sorting prevented for item {item.m_shared.m_name} from slot {slot}.");
                }
            }

            internal void Restore()
            {
                if (restored)
                    return;

                foreach (ItemDrop.ItemData item in itemsToKeep)
                    if (!inventory.ContainsItem(item))
                        inventory.m_inventory.Add(item);

                restored = true;
                ClearCachedItems();
                ExtraSlots.LogDebug($"SimpleSort.SortByType: {itemsToKeep.Count} item(s) returned to player inventory after sorting.");
                itemsToKeep.Clear();
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

        [HarmonyPatch]
        public static class SimpleSort_InternalIsEquipOrQuickSlot_IgnoreExtraSlots
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo plugin))
                    return false;

                target ??= AccessTools.Method(Assembly.GetAssembly(plugin.Instance.GetType()).GetType("SimpleSort.BepInExPlugin"), "SortByType");
                if (target == null)
                    return false;

                if (original == null)
                    ExtraSlots.LogInfo("SimpleSort.BepInExPlugin:SortByType method is patched to ignore items in extra slots");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory inventory, out SortState __state)
            {
                __state = null;
                if (inventory == null || inventory != PlayerInventory)
                    return;

                __state = new SortState(inventory);
                __state.KeepSlotItems();
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(SortState __state) => __state?.Restore();

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(SortState __state, Exception __exception)
            {
                __state?.Dispose();
                return __exception;
            }
        }
    }
}
