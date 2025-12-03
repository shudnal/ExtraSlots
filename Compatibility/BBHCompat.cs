using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots.Compatibility;

public static class BBHCompat
{
    public const string GUID = "Azumatt.BowsBeforeHoes";
    public static PluginInfo BBHPlugin;
    public static Assembly assembly;

    public static bool isEnabled;

    private static Func<Player, Inventory> _getQuiverBarInventory;

    private static bool quiverInventoryCached;
    private static int quiverInventoryCachedSecond;
    private static Inventory quiverInventoryFromCache;

    public static Inventory GetQuiverBarInventory(Player player)
    {
        if (!isEnabled || player == null || player != Player.m_localPlayer)
            return null;

        if (!ZNet.instance)
            return _getQuiverBarInventory != null ? _getQuiverBarInventory(player) : null;

        if (quiverInventoryCachedSecond != (quiverInventoryCachedSecond = (int)ZNet.instance.GetTimeSeconds()) && quiverInventoryCachedSecond % 5 == 0)
            quiverInventoryCached = false;

        if (!quiverInventoryCached)
        {
            quiverInventoryCached = true;
            quiverInventoryFromCache = _getQuiverBarInventory != null ? _getQuiverBarInventory(player) : null;
        }

        return quiverInventoryFromCache;
    }

    public static void CheckForCompatibility()
    {
        if (isEnabled = (bbhArrowsFindingAndCounting.Value && Chainloader.PluginInfos.TryGetValue(GUID, out BBHPlugin)))
        {
            assembly ??= Assembly.GetAssembly(BBHPlugin.Instance.GetType());

            MethodInfo getQuiver = AccessTools.Method(assembly.GetType("BowsBeforeHoes.Extensions.PlayerExtensions"), "GetQuiverBarInventory", new[] { typeof(Player) });
            if (getQuiver != null)
                _getQuiverBarInventory = (Func<Player, Inventory>)Delegate.CreateDelegate(typeof(Func<Player, Inventory>), getQuiver);
            else
            {
                LogInfo("BowsBeforeHoes mod is loaded but BowsBeforeHoes.Extensions.PlayerExtensions:GetQuiverBarInventory is not found");
                isEnabled = false;
                return;
            }

            // Unpatch redundant methods
            assembly.RemoveHarmonyPatch(typeof(Inventory), nameof(Inventory.GetAmmoItem), "BowsBeforeHoes.QuiverDisplay.InventoryGetAmmoItemPatch", "Postfix", "prevent ammo finding and counting mess");
            assembly.RemoveHarmonyPatch(typeof(Attack), nameof(Attack.HaveAmmo), "BowsBeforeHoes.QuiverFunctionality.AttackHaveAmmoPatch", "Postfix", "prevent ammo finding and counting mess");
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetAmmoItem))]
    internal static class Inventory_GetAmmoItem_BBHFix
    {
        private static void Postfix(Inventory __instance, string ammoName, string matchPrefabName, ref ItemDrop.ItemData __result)
        {
            if (!isEnabled)
                return;

            if (__instance != Player.m_localPlayer?.GetInventory())
                return;

            if (__result == Player.m_localPlayer.GetAmmoItem())
                return;

            Inventory quiverBarInventory = GetQuiverBarInventory(Player.m_localPlayer);
            if (quiverBarInventory == null || quiverBarInventory.m_inventory == null)
                return;

            ItemDrop.ItemData firstMatch = null;
            foreach (ItemDrop.ItemData item in quiverBarInventory.m_inventory.Where(itemData => IsItemFitsFilter(itemData, ammoName, matchPrefabName)))
            {
                firstMatch ??= item;

                if (item.m_equipped)
                {
                    __result = item;
                    return;
                }
            }

            if (firstMatch != null)
                __result = firstMatch;
        }

        private static bool IsItemFitsFilter(ItemDrop.ItemData item, string ammoName, string matchPrefabName = null) => (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable) && item.m_shared.m_ammoType == ammoName && (matchPrefabName == null || item.m_dropPrefab?.name == matchPrefabName);
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
    internal static class Inventory_CountItems_BBHFix
    {
        private static void Postfix(Inventory __instance, string name, ref int __result, int quality = -1, bool matchWorldLevel = true)
        {
            if (!isEnabled)
                return;

            if (__instance != Player.m_localPlayer?.GetInventory())
                return;

            Inventory quiverBarInventory = GetQuiverBarInventory(Player.m_localPlayer);
            if (quiverBarInventory == null || quiverBarInventory.m_inventory == null)
                return;

            __result += quiverBarInventory.m_inventory.Where(itemData => IsItemFitsFilter(itemData, name, quality, matchWorldLevel)).Sum(itemData => itemData.m_stack);
        }

        private static bool IsItemFitsFilter(ItemDrop.ItemData item, string name, int quality = -1, bool matchWorldLevel = true) => (name == null || item.m_shared.m_name == name) && (quality < 0 || quality == item.m_quality) && (!matchWorldLevel || item.m_worldLevel >= Game.m_worldLevel);
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    public static class Humanoid_UnequipItem_QuiverInventoryCache
    {
        private static void Postfix(Humanoid __instance)
        {
            if (isEnabled && __instance == Player.m_localPlayer)
                quiverInventoryCached = false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    public static class Humanoid_EquipItem_QuiverInventoryCache
    {
        private static void Postfix(Humanoid __instance)
        {
            if (isEnabled && __instance == Player.m_localPlayer)
                quiverInventoryCached = false;
        }
    }
}