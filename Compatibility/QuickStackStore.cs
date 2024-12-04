using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Reflection;

namespace ExtraSlots.Compatibility
{
    public static class QuickStackStore
    {
        public const string GUID = "goldenrevolver.quick_stack_store";

        [HarmonyPatch]
        public static class QuickStackStore_InternalIsEquipOrQuickSlot_IgnoreExtraSlots
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo plugin))
                    return false;

                target ??= AccessTools.Method(Assembly.GetAssembly(plugin.Instance.GetType()).GetType("QuickStackStore.CompatibilitySupport"), "InternalIsEquipOrQuickSlot");
                if (target == null)
                    return false;

                if (original == null)
                    ExtraSlots.LogInfo("QuickStackStore.CompatibilitySupport:InternalIsEquipOrQuickSlot method is patched to ignore items in extra slots");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Postfix(Vector2i itemPos, ref bool __result) => __result = __result || Slots.GetSlotInGrid(itemPos) != null;
        }
    }
}
