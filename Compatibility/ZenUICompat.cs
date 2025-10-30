using BepInEx.Bootstrap;
using BepInEx;
using System.Reflection;

namespace ExtraSlots.Compatibility;

public static class ZenUICompat
{
    public const string GUID = "ZenDragon.ZenUI";
    public static Assembly assembly;
    public static PluginInfo plugin;

    public static void CheckForCompatibility()
    {
        if (Chainloader.PluginInfos.TryGetValue(GUID, out plugin))
        {
            assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());

            // Unpatch redundant method that equip after tombstone interaction
            assembly.RemoveHarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem), "ZenUI.Section.InventoryEquip", "Humanoid_EquipItem", "prevent item from going into assigned slot on equip");
            assembly.RemoveHarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem), "ZenUI.Section.InventoryEquip", "InventoryGrid_DropItem", "disable check if item in assigned slot");
        }
    }
}