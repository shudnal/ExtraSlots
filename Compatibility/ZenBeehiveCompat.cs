using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Reflection;

namespace ExtraSlots.Compatibility;

internal static class ZenBeehiveCompat
{
    public const string GUID = "ZenDragon.ZenBeehive";
    public static Assembly assembly;

    public static bool isEnabled;
    public static ConfigEntry<bool> holdToTakeAll;

    public static void CheckForCompatibility()
    {
        isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo zenBeehivePlugin);
        if (!isEnabled)
            return;

        zenBeehivePlugin.Instance.Config.TryGetEntry("Beehive", "Hold To Take All", out holdToTakeAll);

        assembly ??= Assembly.GetAssembly(zenBeehivePlugin.Instance.GetType());

        Type beehiveRules = assembly.GetType("ZenBeehive.BeehiveRules");
        if (beehiveRules == null)
            return;

        MethodInfo isHoneyOpenMethod = AccessTools.Method(beehiveRules, "IsHoneyOpen");
        if (isHoneyOpenMethod != null)
        {
            isHoneyOpen = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), isHoneyOpenMethod);
            ExtraSlots.LogInfo("ZenBeehive beehive take all compatibility enabled");
        }
    }

    private static Func<bool> isHoneyOpen;

    public static bool IsHoneyOpen => isEnabled && isHoneyOpen != null && (holdToTakeAll == null || holdToTakeAll.Value) && isHoneyOpen();
}