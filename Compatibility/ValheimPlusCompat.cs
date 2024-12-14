using HarmonyLib;
using BepInEx.Bootstrap;
using System.Reflection;
using BepInEx;

namespace ExtraSlots.Compatibility;

internal static class ValheimPlusCompat
{
    public const string GUID = "org.bepinex.plugins.valheim_plus";
    public static Assembly assembly;

    internal static void CheckForCompatibility()
    {
        if (Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo vplusPlugin))
        {
            assembly ??= Assembly.GetAssembly(vplusPlugin.Instance.GetType());

            // Unpatch redundant methods that change inventory gui

            MethodInfo method = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.Show));
            MethodInfo patch = AccessTools.Method(assembly.GetType("ValheimPlus.GameClasses.InventoryGui_Show_Patch"), "Postfix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("ValheimPlus.GameClasses.InventoryGui_Show_Patch:Postfix was unpatched to prevent inventory GUI mess.");
            }

            method = AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui));
            patch = AccessTools.Method(assembly.GetType("ValheimPlus.GameClasses.InventoryGrid_UpdateGui_Patch"), "Prefix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("ValheimPlus.GameClasses.InventoryGrid_UpdateGui_Patch:Prefix was unpatched to prevent inventory GUI mess.");
            }

            ConstructorInfo ctor = AccessTools.Constructor(typeof(Inventory));
            patch = AccessTools.Method(assembly.GetType("ValheimPlus.GameClasses.Inventory_Constructor_Patch"), "Prefix");
            if (ctor != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(ctor, patch);
                ExtraSlots.LogInfo("ValheimPlus.GameClasses.Inventory_Constructor_Patch:Prefix was unpatched to prevent inventory GUI mess.");
            }
        }
    }

    [HarmonyPatch]
    public static class ValheimPlus_ValheimPlusPlugin_PatchAll_Unpatch
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo vplusPlugin))
                return false;

            assembly ??= Assembly.GetAssembly(vplusPlugin.Instance.GetType());

            target ??= AccessTools.Method(assembly.GetType("ValheimPlus.ValheimPlusPlugin"), "PatchAll");
            if (target == null)
                return false;

            if (original == null)
                ExtraSlots.LogInfo("ValheimPlus.ValheimPlusPlugin:PatchAll method is patched to unpatch inventory related patches");

            return true;
        }

        public static MethodBase TargetMethod() => target;

        public static void Postfix() => CheckForCompatibility();
    }
}