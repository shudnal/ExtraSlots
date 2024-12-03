using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx;
using HarmonyLib;
using System.Reflection;
using System;

namespace ExtraSlots.Compatibility;

public static class MagicPluginCompat
{
    public const string GUID = "blacks7ar.MagicPlugin";
    public static PluginInfo magicPlugin;

    public const string ESCSGUID = "shudnal.ExtraSlotsCustomSlots";
    public static PluginInfo escsPlugin;
    public static ConfigEntry<bool> escsTomeSlotEnabled;
    public static ConfigEntry<bool> escsEarringSlotEnabled;

    public static object toggleOff;
    public static object toggleOn;

    public static Assembly assembly;

    public static ConfigEntryBase toggleEnableTomeSlot;
    public static ConfigEntryBase toggleEnableEarringSlot;

    public static bool isActive;

    private static Func<ItemDrop.ItemData, bool> _isTome;
    public static bool IsTome(ItemDrop.ItemData item) => _isTome != null && _isTome(item);

    private static Func<ItemDrop.ItemData, bool> _isEarring;
    public static bool IsEarring(ItemDrop.ItemData item) => _isEarring != null && _isEarring(item);
    
    public static bool IsMagicPluginCustomSlotItem(ItemDrop.ItemData item)
    {
        if (!isActive || escsPlugin == null && !Chainloader.PluginInfos.TryGetValue(ESCSGUID, out escsPlugin))
            return false;

        if (escsTomeSlotEnabled == null)
        {
            escsPlugin.Instance.Config.TryGetEntry("Mod - Magic Plugin - Tome", "Enabled", out escsTomeSlotEnabled);
            escsPlugin.Instance.Config.TryGetEntry("Mod - Magic Plugin - Earring", "Enabled", out escsEarringSlotEnabled);
        }

        return escsTomeSlotEnabled != null && escsTomeSlotEnabled.Value && toggleEnableTomeSlot.BoxedValue.Equals(toggleOn) && IsTome(item) 
            || escsEarringSlotEnabled != null && escsEarringSlotEnabled.Value && toggleEnableEarringSlot.BoxedValue.Equals(toggleOn) && IsEarring(item);
    }

    public static void CheckForCompatibility()
    {
        if (!(isActive = Chainloader.PluginInfos.TryGetValue(GUID, out magicPlugin)))
            return;

        assembly ??= Assembly.GetAssembly(magicPlugin.Instance.GetType());

        Type toggle = assembly.GetType("MagicPlugin.Functions.Toggle");

        toggleOff = toggle.GetEnumValues().GetValue(0);
        toggleOn = toggle.GetEnumValues().GetValue(1);

        toggleEnableTomeSlot = magicPlugin.Instance.Config[new ConfigDefinition("2- General", "Enable Tome Slot")];
        toggleEnableEarringSlot = magicPlugin.Instance.Config[new ConfigDefinition("2- General", "Enable Earring Slot")];

        MethodInfo isTome = AccessTools.Method(assembly.GetType("MagicPlugin.Functions.MagicSlot"), "IsTomeItem");
        if (isTome != null)
            _isTome = (ItemDrop.ItemData item) => item != null && (bool)isTome.Invoke(null, new[] { item });
        else
            ExtraSlots.LogWarning("MagicPlugin mod is loaded but MagicPlugin.Functions.MagicSlot:IsTomeItem is not found");
        
        MethodInfo isEarring = AccessTools.Method(assembly.GetType("MagicPlugin.Functions.MagicSlot"), "IsEarringItem");
        if (isEarring!= null)
            _isEarring = (ItemDrop.ItemData item) => item != null && (bool)isEarring.Invoke(null, new[] { item });
        else
            ExtraSlots.LogWarning("MagicPlugin mod is loaded but MagicPlugin.Functions.MagicSlot:IsEarringItem is not found");
    }
}
