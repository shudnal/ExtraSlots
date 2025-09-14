using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility
{
    public static class SimpleSort
    {
        public const string GUID = "aedenthorn.SimpleSort";

        private static readonly List<ItemDrop.ItemData> itemsToKeep = new List<ItemDrop.ItemData>();


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

            public static void Prefix(Inventory inventory)
            {
                if (inventory != PlayerInventory)
                    return;

                itemsToKeep.Clear();
                slots.DoIf(slot => !slot.IsFree, KeepItem);
                
                void KeepItem(Slot slot)
                {
                    ItemDrop.ItemData item = slot.Item;

                    itemsToKeep.Add(item);
                    inventory.m_inventory.Remove(item);
                    ExtraSlots.LogDebug($"SimpleSort.SortByType.Prefix: Sorting prevented for item {item.m_shared.m_name} from slot {slot}. Item temporary removed from player inventory.");
                }
            }

            public static void Postfix(Inventory inventory)
            {
                if (itemsToKeep.Count == 0)
                    return;

                inventory.m_inventory.AddRange(itemsToKeep);
                inventory.Changed();

                ExtraSlots.LogDebug($"SimpleSort.SortByType.Postfix: {itemsToKeep.Count} item(s) returned to player inventory after sorting preventing.");

                itemsToKeep.Clear();
            }
        }
    }
}
