using HarmonyLib;
using BepInEx.Bootstrap;
using System.Reflection;

namespace ExtraSlots.Compatibility;

internal static class ValheimPlusCompat
{
    public const string valheimPlusGuid = "org.bepinex.plugins.valheim_plus";

    internal static void CheckForCompatibility()
    {
        if (Chainloader.PluginInfos.ContainsKey(valheimPlusGuid))
        {
            // Unpatch redundant method that change inventory gui
            MethodInfo method = AccessTools.Method("InventoryGui:Show");
            MethodInfo patch = AccessTools.Method("ValheimPlus.GameClasses.InventoryGui_Show_Patch:Postfix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("ValheimPlus.GameClasses.InventoryGui_Show_Patch:Postfix was unpatched.");
            }

            method = AccessTools.Method("InventoryGrid:UpdateGui");
            patch = AccessTools.Method("ValheimPlus.GameClasses.InventoryGrid_UpdateGui_Patch:Prefix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("ValheimPlus.GameClasses.InventoryGrid_UpdateGui_Patch:Prefix was unpatched.");
            }

            ConstructorInfo ctor = AccessTools.Constructor(typeof(Inventory));
            patch = AccessTools.Method("ValheimPlus.GameClasses.Inventory_Constructor_Patch:Prefix");
            if (ctor != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(ctor, patch);
                ExtraSlots.LogInfo("ValheimPlus.GameClasses.Inventory_Constructor_Patch:Prefix was unpatched.");
            }
        }
    }
}