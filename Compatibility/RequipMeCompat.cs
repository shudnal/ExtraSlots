using BepInEx.Bootstrap;
using BepInEx;
using System.Reflection;

namespace ExtraSlots.Compatibility;

public static class RequipMeCompat
{
    public const string GUID = "neobotics.valheim_mod.requipme";
    public static Assembly assembly;
    public static PluginInfo plugin;

    public static void CheckForCompatibility()
    {
        if (Chainloader.PluginInfos.TryGetValue(GUID, out plugin))
        {
            assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());
            
            // Unpatch redundant method that equip after tombstone interaction
            assembly.RemoveHarmonyPatch(typeof(TombStone), nameof(TombStone.OnTakeAllSuccess), "neobotics.ValheimMods.RequipMe+On_Take_All_Success_Patch", "Postfix", "prevent inventory mess on tombstone equip");
            assembly.RemoveHarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory), "neobotics.ValheimMods.RequipMe+EasyFitInInventory_Patch", "Postfix", "prevent inventory mess on tombstone equip");
        }
    }
}