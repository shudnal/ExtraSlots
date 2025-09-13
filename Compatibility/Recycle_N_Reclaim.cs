using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility
{
    public static class Recycle_N_Reclaim
    {
        public const string GUID = "Azumatt.Recycle_N_Reclaim";

        private static readonly Dictionary<ItemDrop.ItemData, Vector2i> itemsToIgnore = new Dictionary<ItemDrop.ItemData, Vector2i>();

        public static PluginInfo plugin;
        public static Assembly assembly;

        public static bool isEnabled;

        public static void CheckForCompatibility()
        {
            if (isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin))
                assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());
        }

        [HarmonyPatch]
        public static class AdventureBackpacks_PlayerExtensions_CustomSlotItem
        {
            public static List<MethodBase> targets;

            public static List<MethodBase> GetTargets()
            {
                List<MethodBase> list = new List<MethodBase>();

                if (assembly == null)
                    return list;

                assembly.TryAddMethodToPatch(list, "Recycle_N_Reclaim.GamePatches.Recycling.Reclaimer", "RecycleInventoryForAllRecipes", "ignore items in extra slots");
                assembly.TryAddMethodToPatch(list, "Recycle_N_Reclaim.GamePatches.Recycling.Reclaimer", "GetRecyclingAnalysisForInventory", "ignore items in extra slots");
                assembly.TryAddMethodToPatch(list, "Recycle_N_Reclaim.GamePatches.Recycling.Reclaimer", "RecycleOneItemInInventory", "ignore items in extra slots");
                return list;
            }

            public static bool Prepare() => isEnabled && (targets ??= GetTargets()).Count > 0;

            private static IEnumerable<MethodBase> TargetMethods() => targets;

            public static void Prefix(MethodBase __originalMethod, Inventory inventory)
            {
                if (!ExtraSlots.Recycle_N_ReclaimExcludeExtraSlots.Value || inventory != PlayerInventory)
                    return;

                itemsToIgnore.Clear();
                slots.DoIf(slot => !slot.IsFree, KeepItem);

                void KeepItem(Slot slot)
                {
                    ItemDrop.ItemData item = slot.Item;

                    itemsToIgnore.Add(item, item.m_gridPos);
                    item.m_gridPos.y = 0;
                    ExtraSlots.LogDebug($"{__originalMethod.Name}.Prefix: Recycling prevented for {item.m_shared.m_name} from slot {slot}. Item temporary moved into hotbar {item.m_gridPos}.");
                }
            }

            public static void Finalizer(MethodBase __originalMethod, Inventory inventory)
            {
                if (itemsToIgnore.Count == 0)
                    return;

                foreach (var item in itemsToIgnore)
                {
                    item.Key.m_gridPos = item.Value;
                    ExtraSlots.LogDebug($"{__originalMethod.Name}.Finalizer: {item.Key.m_shared.m_name} moved back to {item.Key.m_gridPos} after recycling preventing.");
                }

                itemsToIgnore.Clear();
            }
        }
    }
}
