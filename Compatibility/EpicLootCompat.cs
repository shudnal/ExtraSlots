using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots.Compatibility;

public static class EpicLootCompat
{
    public const string GUID = "randyknapp.mods.epicloot";

    private const string ShowEquippedAndHotbarItemsInSacrificeTabKey = "ShowEquippedAndHotbarItemsInSacrificeTab";
    private const int MinimumSupportedApiVersion = 1;

    private enum CompatibilityMode
    {
        None,
        LegacyHarmony,
        ApiV1
    }

    public static PluginInfo epicLootPlugin;
    public static Assembly assembly;

    public static bool isEnabled;

    public static ConfigEntry<bool> ShowEquippedAndHotbarItemsInSacrificeTab;

    private static CompatibilityMode compatibilityMode;

    private static bool equipmentProviderRegistered;
    private static bool sacrificeFilterRegistered;

    private static MethodInfo registerEquipmentProvider;
    private static MethodInfo unregisterEquipmentProvider;
    private static MethodInfo registerSacrificeFilter;
    private static MethodInfo unregisterSacrificeFilter;
    private static MethodInfo invalidatePlayerEffectCache;
    private static MethodInfo getApiVersion;

    public static void CheckForCompatibility()
    {
        compatibilityMode = CompatibilityMode.None;

        if (!(isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out epicLootPlugin)))
            return;

        assembly ??= Assembly.GetAssembly(epicLootPlugin.Instance.GetType());

        FindShowEquippedAndHotbarItemsInSacrificeTabConfig();

        if (TryEnableApiCompatibility())
        {
            compatibilityMode = CompatibilityMode.ApiV1;
            LogInfo("Epic Loot API v1 (0.13+) compatibility enabled");
        }
        else
        {
            compatibilityMode = CompatibilityMode.LegacyHarmony;
            LogInfo("Epic Loot legacy Harmony compatibility enabled");
        }
    }

    public static void UnregisterCompatibility()
    {
        if (compatibilityMode != CompatibilityMode.ApiV1)
            return;

        UnregisterApiProvider(unregisterEquipmentProvider, equipmentProviderRegistered, "equipment provider");
        UnregisterApiProvider(unregisterSacrificeFilter, sacrificeFilterRegistered, "sacrifice filter");

        equipmentProviderRegistered = false;
        sacrificeFilterRegistered = false;
        compatibilityMode = CompatibilityMode.None;
        ClearApiBindings();
    }

    public static void InvalidatePlayerEffectCache(Player player)
    {
        if (compatibilityMode != CompatibilityMode.ApiV1 || player == null || invalidatePlayerEffectCache == null)
            return;

        try
        {
            invalidatePlayerEffectCache.Invoke(null, new object[] { player });
        }
        catch (Exception ex)
        {
            LogWarning($"Epic Loot API failed to invalidate the player effect cache: {ex.GetBaseException().Message}");
        }
    }

    private static void FindShowEquippedAndHotbarItemsInSacrificeTabConfig()
    {
        ShowEquippedAndHotbarItemsInSacrificeTab = epicLootPlugin.Instance.Config
            .Where(entry => entry.Key.Key == ShowEquippedAndHotbarItemsInSacrificeTabKey)
            .Select(entry => entry.Value)
            .OfType<ConfigEntry<bool>>()
            .FirstOrDefault();
    }

    private static bool TryEnableApiCompatibility()
    {
        // Epic Loot 0.13 introduced API version 1. Use this branch only when the complete contract
        // required by Extra Slots is available; otherwise use the legacy Harmony integration.
        if (!TryResolveApiContract() || !HasSupportedApiVersion())
        {
            ClearApiBindings();
            return false;
        }

        equipmentProviderRegistered = RegisterApiProvider(
            registerEquipmentProvider,
            new Func<Player, List<ItemDrop.ItemData>>(GetExtraEquippedItems),
            "equipment provider");

        if (!equipmentProviderRegistered)
        {
            ClearApiBindings();
            return false;
        }

        sacrificeFilterRegistered = RegisterApiProvider(
            registerSacrificeFilter,
            new Func<ItemDrop.ItemData, bool>(CanSacrifice),
            "sacrifice filter");

        if (sacrificeFilterRegistered)
            return true;

        UnregisterApiProvider(unregisterEquipmentProvider, equipmentProviderRegistered, "equipment provider");
        equipmentProviderRegistered = false;
        ClearApiBindings();
        return false;
    }

    private static bool TryResolveApiContract()
    {
        Type apiType = assembly?.GetType("EpicLoot.API");
        if (apiType == null)
            return false;

        Type equipmentProviderType = typeof(Func<Player, List<ItemDrop.ItemData>>);
        Type sacrificeFilterType = typeof(Func<ItemDrop.ItemData, bool>);

        getApiVersion = FindStaticMethod(apiType, "GetApiVersion", Type.EmptyTypes);
        registerEquipmentProvider = FindStaticMethod(apiType, "RegisterEquipmentProvider", new Type[] { typeof(string), equipmentProviderType });
        unregisterEquipmentProvider = FindStaticMethod(apiType, "UnregisterEquipmentProvider", new Type[] { typeof(string) });
        registerSacrificeFilter = FindStaticMethod(apiType, "RegisterSacrificeFilter", new Type[] { typeof(string), sacrificeFilterType });
        unregisterSacrificeFilter = FindStaticMethod(apiType, "UnregisterSacrificeFilter", new Type[] { typeof(string) });
        invalidatePlayerEffectCache = FindStaticMethod(apiType, "InvalidatePlayerEffectCache", new Type[] { typeof(Player) });

        return getApiVersion != null
            && registerEquipmentProvider != null
            && unregisterEquipmentProvider != null
            && registerSacrificeFilter != null
            && unregisterSacrificeFilter != null
            && invalidatePlayerEffectCache != null;
    }

    private static MethodInfo FindStaticMethod(Type type, string methodName, Type[] parameterTypes)
    {
        return type?.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: parameterTypes,
            modifiers: null);
    }

    private static bool HasSupportedApiVersion()
    {
        try
        {
            return getApiVersion?.Invoke(null, Array.Empty<object>()) is int apiVersion
                && apiVersion >= MinimumSupportedApiVersion;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearApiBindings()
    {
        getApiVersion = null;
        registerEquipmentProvider = null;
        unregisterEquipmentProvider = null;
        registerSacrificeFilter = null;
        unregisterSacrificeFilter = null;
        invalidatePlayerEffectCache = null;
    }

    private static bool RegisterApiProvider(MethodInfo method, Delegate provider, string providerName)
    {
        if (method == null)
            return false;

        try
        {
            if (method.Invoke(null, new object[] { pluginID, provider }) is bool registered && registered)
            {
                LogInfo($"Epic Loot API {providerName} registered");
                return true;
            }

            LogWarning($"Epic Loot API rejected the Extra Slots {providerName} registration");
        }
        catch (Exception ex)
        {
            LogWarning($"Epic Loot API failed to register the Extra Slots {providerName}: {ex.GetBaseException().Message}");
        }

        return false;
    }

    private static void UnregisterApiProvider(MethodInfo method, bool registered, string providerName)
    {
        if (!registered || method == null)
            return;

        try
        {
            method.Invoke(null, new object[] { pluginID });
        }
        catch (Exception ex)
        {
            LogWarning($"Epic Loot API failed to unregister the Extra Slots {providerName}: {ex.GetBaseException().Message}");
        }
    }

    private static List<ItemDrop.ItemData> GetExtraEquippedItems(Player player)
    {
        List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
        AppendExtraEquippedItems(player, items);
        return items;
    }

    private static void AppendExtraEquippedItems(Player player, List<ItemDrop.ItemData> items)
    {
        if (!Slots.IsValidPlayer(player) || items == null)
            return;

        foreach (ItemDrop.ItemData item in ExtraUtilitySlots.GetEquippedItems(player))
            if (item != null && !items.Contains(item))
                items.Add(item);

        foreach (Slots.Slot slot in Slots.GetEquipmentSlots())
        {
            if (!slot.IsCustomSlot)
                continue;

            ItemDrop.ItemData item = slot.Item;
            if (item != null && player.IsItemEquiped(item) && !items.Contains(item))
                items.Add(item);
        }
    }

    private static bool CanSacrifice(ItemDrop.ItemData item)
    {
        if (ShowEquippedAndHotbarItemsInSacrificeTab?.Value == true)
            return true;

        if (Slots.GetItemSlot(item) is not Slots.Slot slot)
            return true;

        return !(slot.IsQuickSlot || (slot.IsMiscSlot && epicLootExcludeMiscItemsFromSacrifice.Value));
    }

    [HarmonyPatch]
    public static class EpicLoot_Legacy_Player_GetMagicEquipment_AddItemsFromExtraUtilityAndCustomSlots
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (compatibilityMode != CompatibilityMode.LegacyHarmony)
                return false;

            Type playerExtensions = assembly?.GetType("EpicLoot.PlayerExtensions");
            if (playerExtensions == null)
                return false;

            target ??= FindStaticMethod(playerExtensions, "GetMagicEquipment", new Type[] { typeof(Player) })
                       ?? FindStaticMethod(playerExtensions, "GetEquipment", new Type[] { typeof(Player) });
            if (target == null)
                return false;

            if (original == null)
                LogInfo($"EpicLoot.PlayerExtensions:{target.Name} method is patched to add extra utility and custom slot items");

            return true;
        }

        public static MethodBase TargetMethod() => target;

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Player player, List<ItemDrop.ItemData> __result)
        {
            AppendExtraEquippedItems(player, __result);
        }
    }

    [HarmonyPatch]
    public static class EpicLoot_Legacy_EnchantCostsHelper_GetSacrificeProducts_ExcludeItemsFromSacrifice
    {
        public static MethodBase target;

        public static bool Prepare(MethodBase original)
        {
            if (compatibilityMode != CompatibilityMode.LegacyHarmony)
                return false;

            Type enchantCostsHelper = assembly?.GetType("EpicLoot.Crafting.EnchantCostsHelper");
            if (enchantCostsHelper == null)
                return false;

            target ??= FindStaticMethod(enchantCostsHelper, "GetSacrificeProducts", new Type[] { typeof(ItemDrop.ItemData) });
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
            return CanSacrifice(item);
        }
    }
}
