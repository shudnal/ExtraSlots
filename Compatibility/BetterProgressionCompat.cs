using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx;
using HarmonyLib;
using System.Reflection;

namespace ExtraSlots.Compatibility;

public static class BetterProgressionCompat
{
    public const string GUID = "Revel.BetterProgression";
    public static PluginInfo betterProgressionPlugin;
    
    public static object toggleOff;
    public static object toggleOn;

    public static Assembly assembly;

    public static ConfigEntryBase toggleInventoryRowFeature;
    public static ConfigDefinition definitionInventoryRowFeature = new ConfigDefinition("1 - General", "Enable Inventory Row Feature");
    public static ConfigEntry<string> addInventoryRowList;

    public static void CheckForCompatibility()
    {
        if (Chainloader.PluginInfos.TryGetValue(GUID, out betterProgressionPlugin))
        {
            assembly ??= Assembly.GetAssembly(betterProgressionPlugin.Instance.GetType());

            System.Type toggle = assembly.GetType("BetterProgression.BetterProgressionPlugin+Toggle");
            
            toggleOff = toggle.GetEnumValues().GetValue(0);
            toggleOn = toggle.GetEnumValues().GetValue(1);

            toggleInventoryRowFeature = betterProgressionPlugin.Instance.Config[definitionInventoryRowFeature];

            betterProgressionPlugin.Instance.Config.SettingChanged += (s, e) => DisableInventoryRows(e.ChangedSetting);

            // Make BetterProgression Inventory Row Feature unenableable since it will mess the inventory grid
            if (betterProgressionPlugin.Instance.Config.TryGetEntry("4 - Inventory", "Add Inventory Row", out addInventoryRowList))
                addInventoryRowList.SettingChanged += (s, e) => ClearInventoryRowList();

            // Make BetterProgression know there is other mod managing inventory
            AccessTools.Field(assembly.GetType("BetterProgression.InventoryUpdate"), "isAzuEPILoaded").SetValue(null, true);

            // Unpatch redundant methods that validate inventory
            MethodInfo method = AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.Update));
            MethodInfo patch = AccessTools.Method(assembly.GetType("BetterProgression.InventoryUpdate+InventoryGui_Update_Patch"), "Postfix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("BetterProgression.InventoryUpdate+InventoryGui_Update_Patch:Postfix was unpatched to prevent inventory mess.");
            }

            method = AccessTools.Method(typeof(Container), nameof(Container.RPC_TakeAllRespons));
            patch = AccessTools.Method(assembly.GetType("BetterProgression.InventoryUpdate+ContainerRPCRequestTakeAllPatch"), "Postfix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("BetterProgression.InventoryUpdate+ContainerRPCRequestTakeAllPatch:Postfix was unpatched to prevent inventory mess.");
            }

            method = AccessTools.Method(typeof(Inventory), nameof(Inventory.MoveAll));
            patch = AccessTools.Method(assembly.GetType("BetterProgression.InventoryUpdate+MoveAllToPatch"), "Postfix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("BetterProgression.InventoryUpdate+ContainerRPCRequestTakeAllPatch:Postfix was unpatched to prevent inventory mess.");
            }
        }
    }

    private static void ClearInventoryRowList()
    {
        if (betterProgressionPlugin != null && addInventoryRowList != null && !addInventoryRowList.Value.IsNullOrWhiteSpace())
        {
            addInventoryRowList.Value = "";
            ExtraSlots.LogWarning("BetterProgression's Add Inventory Row was cleared to prevent issues with inventory grid.");
        }
    }

    private static void DisableInventoryRows(ConfigEntryBase configEntryBase)
    {
        if (configEntryBase == null || configEntryBase != toggleInventoryRowFeature)
            return;

        if (configEntryBase.BoxedValue.Equals(toggleOn))
        {
            configEntryBase.BoxedValue = toggleOff;
            ExtraSlots.LogWarning("BetterProgression's Inventory Row Feature was disabled to prevent issues with inventory grid.");
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
    private static class FejdStartup_Start_DisableBetterProgressionQuiver
    {
        private static void Postfix()
        {
            ClearInventoryRowList();
            DisableInventoryRows(toggleInventoryRowFeature);
        }
    }
}
