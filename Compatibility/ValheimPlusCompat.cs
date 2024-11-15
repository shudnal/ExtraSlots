using HarmonyLib;
using BepInEx.Bootstrap;

namespace ExtraSlots.Compatibility;

internal static class ValheimPlusCompat
{
    public const string valheimPlusGuid = "org.bepinex.plugins.valheim_plus";

    public static bool VPlusInstalled = Chainloader.PluginInfos.ContainsKey(valheimPlusGuid);

    internal static bool scrollBarSubstituted = false;

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    public static class InventoryGui_Show_VPLusPrefix
    {
        [HarmonyBefore(valheimPlusGuid)]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(InventoryGui __instance)
        {
            if (!VPlusInstalled)
                return;

            if (__instance.m_playerGrid.m_scrollbar == null)
            {
                __instance.m_playerGrid.m_scrollbar = __instance.m_containerGrid.m_scrollbar;
                scrollBarSubstituted = true;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    public static class InventoryGui_Show_VPLusPostfix
    {
        [HarmonyAfter(valheimPlusGuid)]
        public static void Postfix(InventoryGui __instance)
        {
            if (!VPlusInstalled)
                return;

            if (scrollBarSubstituted)
            {
                __instance.m_playerGrid.m_scrollbar = null;
                scrollBarSubstituted = false;
            }
        }
    }
}
