using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots.Compatibility;

public static class EpicLootCompat
{
    public const string GUID = "randyknapp.mods.epicloot";
    public static PluginInfo epicLootPlugin;
    public static Assembly assembly;

    public static bool isEnabled;

    public static ConfigEntry<bool> ShowEquippedAndHotbarItemsInSacrificeTab;

    public static void CheckForCompatibility()
    {
        if (isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out epicLootPlugin))
        {
            assembly ??= Assembly.GetAssembly(epicLootPlugin.Instance.GetType());
            epicLootPlugin.Instance.Config.TryGetEntry("Crafting UI", "ShowEquippedAndHotbarItemsInSacrificeTab", out ShowEquippedAndHotbarItemsInSacrificeTab);
        }
    }

    [HarmonyPatch]
    public static class EpicLoot_Player_GetEquipment_AddItemsFromExtraUtilityAndCustomSlots
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (!isEnabled)
                return false;

            target ??= AccessTools.Method(assembly.GetType("EpicLoot.PlayerExtensions"), "GetEquipment");
            if (target == null)
                return false;

            if (original == null)
                LogInfo("EpicLoot.PlayerExtensions:GetEquipment method is patched to add extra utility and custom slot items");
            
            return true;
        }

        public static MethodBase TargetMethod() => target;

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Player player, List<ItemDrop.ItemData> __result)
        {
            if (!Slots.IsValidPlayer(player))
                return;

            ExtraUtilitySlots.GetEquippedItems()
                .Union(Slots.GetEquipmentSlots().Where(slot => slot.IsCustomSlot && !slot.IsFree && player.IsItemEquiped(slot.Item)).Select(slot => slot.Item))
                .DoIf(item => !__result.Contains(item), __result.Add);
        }
    }

    [HarmonyPatch]
    public static class EpicLoot_EnchantCostsHelper_GetSacrificeProducts_ExcludeItemsFromSacrifice
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (!isEnabled)
                return false;

            target ??= AccessTools.Method(assembly.GetType("EpicLoot.Crafting.EnchantCostsHelper"), "GetSacrificeProducts", new System.Type[] { typeof(ItemDrop.ItemData) });
            if (target == null)
                return false;

            if (original == null)
                LogInfo("EpicLoot.Crafting.EnchantCostsHelper:GetSacrificeProducts method is patched to optionally exclude quick and misc slots");
            
            return true;
        }

        public static MethodBase TargetMethod() => target;

        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(ItemDrop.ItemData item)
        {
            if (ShowEquippedAndHotbarItemsInSacrificeTab != null && ShowEquippedAndHotbarItemsInSacrificeTab.Value)
                return true;

            if (Slots.GetItemSlot(item) is not Slots.Slot slot)
                return true;

            return !(slot.IsQuickSlot || (slot.IsMiscSlot && epicLootExcludeMiscItemsFromSacrifice.Value));
        }
    }
}