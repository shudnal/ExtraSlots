using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace ExtraSlots.Compatibility
{
    public static class ValheimRadial
    {
        public const string GUID = "PerspectiveBroad4501.ValheimRadial";

        [HarmonyPatch]
        public static class ValheimRadial_InventoryRowGroupConfig_GetConfiguredInventoryItems_ReturnExtraSlotsItems
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo plugin))
                    return false;

                target ??= AccessTools.Method(Assembly.GetAssembly(plugin.Instance.GetType()).GetType("ValheimRadial.InventoryRowGroupConfig"), "GetConfiguredInventoryItems");
                if (target == null)
                    return false;

                if (original == null)
                    ExtraSlots.LogInfo("ValheimRadial.InventoryRowGroupConfig.InventoryRowGroupConfig:GetConfiguredInventoryItems method is patched to return items in extra slots");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Postfix(ref List<ItemDrop.ItemData> __result)
            {
                if (ExtraSlots.valheimRadialAddQuickSlots.Value)
                    __result.AddRange(API.GetQuickSlotsItems());

                if (ExtraSlots.valheimRadialAddAmmoSlots.Value)
                    __result.AddRange(API.GetAmmoSlotsItems());

                if (ExtraSlots.valheimRadialAddFoodSlots.Value)
                    __result.AddRange(API.GetFoodSlotsItems());
            }
        }
    }
}
