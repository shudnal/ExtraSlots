using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Reflection;

namespace ExtraSlots.Compatibility;

internal static class PlantEasilyCompat
{
    public const string GUID = "advize.PlantEasily";
    public static Assembly assembly;

    public static bool isEnabled;

    public static void CheckForCompatibility()
    {
        isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo plantEasilyPlugin);
        if (!isEnabled)
            return;

        assembly ??= Assembly.GetAssembly(plantEasilyPlugin.Instance.GetType());

        Type pluginPlantEasily = assembly.GetType("Advize_PlantEasily.PlantEasily");
        if (pluginPlantEasily == null)
            return;

        PropertyInfo overrideGamepadInput = AccessTools.Property(pluginPlantEasily, "OverrideGamepadInput");
        if (overrideGamepadInput == null)
            return;

        _overrideGamepadInput = () => (bool)overrideGamepadInput.GetValue(null);
        ExtraSlots.LogInfo("PlantEasily gamepad compatibility enabled");
    }

    private static Func<bool> _overrideGamepadInput;

    public static bool DisableGamepadInput => _overrideGamepadInput != null && _overrideGamepadInput();
}