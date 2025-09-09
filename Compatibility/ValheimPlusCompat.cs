using HarmonyLib;
using BepInEx.Bootstrap;
using System.Reflection;
using BepInEx;
using System;

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
            assembly.RemoveHarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show), "ValheimPlus.GameClasses.InventoryGui_Show_Patch", "Postfix", "prevent inventory GUI mess");
            assembly.RemoveHarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui), "ValheimPlus.GameClasses.InventoryGrid_UpdateGui_Patch", "Prefix", "prevent inventory GUI mess");
            assembly.RemoveHarmonyPatch(AccessTools.Constructor(typeof(Inventory)), "ValheimPlus.GameClasses.Inventory_Constructor_Patch", "Prefix", "prevent inventory GUI mess");
        }
    }

    internal static void OverridePlayerInventoryRows(object configuration)
    {
        PropertyInfo inventoryProp = configuration.GetType().GetProperty("Inventory", BindingFlags.Public | BindingFlags.Instance);
        if (inventoryProp == null)
        {
            ExtraSlots.LogInfo("Property Inventory is not found in V+ Configuration");
            return;
        }

        object inventoryObj = inventoryProp.GetValue(configuration);
        if (inventoryObj == null)
        {
            ExtraSlots.LogInfo("Property Inventory is not set in V+ Configuration");
            return;
        }

        PropertyInfo rowsProp = inventoryObj.GetType().GetProperty("playerInventoryRows", BindingFlags.Public | BindingFlags.Instance);
        if (rowsProp == null)
        {
            ExtraSlots.LogInfo("Property Inventory.playerInventoryRows is not found in V+ Configuration");
            return;
        }

        int value = Slots.InventoryHeightFull;
        if (!rowsProp.CanWrite)
        {
            MethodInfo setMethod = rowsProp.GetSetMethod(true);
            if (setMethod == null)
            {
                ExtraSlots.LogInfo("Set method for Inventory.playerInventoryRows is not found in V+ Configuration");
                return;
            }

            setMethod.Invoke(inventoryObj, new object[] { value });
        }
        else
        {
            rowsProp.SetValue(inventoryObj, value);
        }

        ExtraSlots.LogInfo($"ValheimPlus Configuration.Current.Inventory.playerInventoryRows set to {value}");
    }

    internal static void UpdatePlayerInventoryRows()
    {
        if (assembly == null)
            return;

        Type configurationType = assembly.GetType("ValheimPlus.Configurations.Configuration");
        if (configurationType == null)
            return;

        PropertyInfo overrideGamepadInput = AccessTools.Property(configurationType, "Current");
        if (overrideGamepadInput != null)
            OverridePlayerInventoryRows(overrideGamepadInput.GetValue(overrideGamepadInput));
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

        public static void Finalizer() => CheckForCompatibility();
    }

    [HarmonyPatch]
    public static class ValheimPlus_ValheimPlusPlugin_InventoryHeightOverride
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo vplusPlugin))
                return false;

            assembly ??= Assembly.GetAssembly(vplusPlugin.Instance.GetType());

            target ??= AccessTools.PropertyGetter(assembly.GetType("ValheimPlus.Configurations.Sections.InventoryConfiguration"), "playerInventoryRows");
            if (target == null)
                return false;

            if (original == null)
                ExtraSlots.LogInfo("ValheimPlus.Configurations.Sections.InventoryConfiguration:playerInventoryRows property getter is patched to return current rows");

            return true;
        }

        public static MethodBase TargetMethod() => target;

        public static void Finalizer(ref int __result) => __result = Slots.InventoryHeightFull;
    }

    [HarmonyPatch]
    public static class ValheimPlus_Configuration_LoadFromIni_Stream_InventoryHeightOverride
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo vplusPlugin))
                return false;

            target ??= AccessTools.Method(assembly.GetType("ValheimPlus.Configurations.ConfigurationExtra"), "LoadFromIni", new[] { typeof(System.IO.Stream) });
            if (target == null)
                return false;

            if (original == null)
                ExtraSlots.LogInfo("ValheimPlus.Configurations.ConfigurationExtra:LoadFromIni (Stream) method is patched to override current inventory rows");

            return true;
        }

        public static MethodBase TargetMethod() => target;

        public static void Finalizer(object __result) => OverridePlayerInventoryRows(__result);
    }

    [HarmonyPatch]
    public static class ValheimPlus_Configuration_LoadFromIni_File_InventoryHeightOverride
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo vplusPlugin))
                return false;

            target ??= AccessTools.Method(assembly.GetType("ValheimPlus.Configurations.ConfigurationExtra"), "LoadFromIni", new[] { typeof(string), typeof(bool) });
            if (target == null)
                return false;

            if (original == null)
                ExtraSlots.LogInfo("ValheimPlus.Configurations.ConfigurationExtra:LoadFromIni (File) method is patched to override current inventory rows");

            return true;
        }

        public static MethodBase TargetMethod() => target;

        public static void Finalizer(object __result) => OverridePlayerInventoryRows(__result);
    }
}