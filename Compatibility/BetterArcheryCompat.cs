using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx;
using HarmonyLib;
using System.Reflection;

namespace ExtraSlots.Compatibility;

public static class BetterArcheryCompat
{
    public const string GUID = "ishid4.mods.betterarchery";
    public static Assembly assembly;
    public static PluginInfo betterArcheryPlugin;

    public static ConfigEntry<bool> baEnableQuiver;

    public static void CheckForCompatibility()
    {
        if (Chainloader.PluginInfos.TryGetValue(GUID, out betterArcheryPlugin))
        {
            // Make BetterArchery quiver unenableable since it will mess the inventory grid
            if (betterArcheryPlugin.Instance.Config.TryGetEntry("Quiver", "Enable Quiver", out baEnableQuiver))
                baEnableQuiver.SettingChanged += (s, e) => DisableQuiver();

            assembly ??= Assembly.GetAssembly(betterArcheryPlugin.Instance.GetType());

            // Unpatch redundant method that validate inventory
            MethodInfo method = AccessTools.Method(typeof(TombStone), nameof(TombStone.OnTakeAllSuccess));
            MethodInfo patch = AccessTools.Method(assembly.GetType("BetterArchery.Tombstone+TombStone_OnTakeAllSuccess_Patch"),"Postfix");
            if (method != null && patch != null)
            {
                ExtraSlots.instance.harmony.Unpatch(method, patch);
                ExtraSlots.LogInfo("BetterArchery.Tombstone+TombStone_OnTakeAllSuccess_Patch:Postfix was unpatched to prevent inventory mess.");
            }
        }
    }

    private static void DisableQuiver()
    {
        if (betterArcheryPlugin!= null && baEnableQuiver != null && baEnableQuiver.Value)
        {
            baEnableQuiver.Value = false;
            ExtraSlots.LogWarning("BetterArchery's Quiver was disabled to prevent issues with inventory grid. You will not lose arrows if you had Quiver enabled in your previous session." +
                $"\nThis logic is designed for BetterArchery version 1.9.8, current version is {betterArcheryPlugin.Metadata.Version}. If quiver implementation was changed pls contact me at any platform.");
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
    private static class FejdStartup_Start_DisableBetterArcheryQuiver
    {
        private static void Postfix() => DisableQuiver();
    }
}
