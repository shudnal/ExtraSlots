using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace ExtraSlots.Compatibility
{
    public static class AzuAutoStore
    {
        public const string GUID = "Azumatt.AzuAutoStore";

        [HarmonyPatch]
        public static class AzuAutoStore_AzuEPI_IsLoaded_Impersonate
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo plugin))
                    return false;

                target ??= AccessTools.Method(Assembly.GetAssembly(plugin.Instance.GetType()).GetType("AzuExtendedPlayerInventory.API"), "IsLoaded");
                if (target == null)
                    return false;

                if (original == null)
                    ExtraSlots.LogInfo("AzuAutoStore.AzuExtendedPlayerInventory.API:IsLoaded method is patched to return true as if AzuEPI is used");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Postfix(ref bool __result) => __result = true;
        }

        [HarmonyPatch]
        public static class AzuAutoStore_AzuEPI_GetQuickSlotsItems_ReturnExtraSlots
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo plugin))
                    return false;

                target ??= AccessTools.Method(Assembly.GetAssembly(plugin.Instance.GetType()).GetType("AzuExtendedPlayerInventory.API"), "GetQuickSlotsItems");
                if (target == null)
                    return false;

                if (original == null)
                    ExtraSlots.LogInfo("AzuAutoStore.AzuExtendedPlayerInventory.API:GetQuickSlotsItems method is patched to return items in extra slots");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Postfix(ref List<ItemDrop.ItemData> __result) => __result.AddRange(API.GetAllExtraSlotsItems());
        }
    }
}
