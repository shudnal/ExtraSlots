using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace ExtraSlots.Compatibility;

internal static class ValheimPlusCompat
{
    public const string GUID = "org.bepinex.plugins.valheim_plus";

    private const string VPlusHarmonyID = "mod.valheim_plus";
    private static readonly System.Version MinimumSupportedVersion = new System.Version(0, 10, 1, 1);

    private static PluginInfo plugin;
    private static Assembly assembly;
    private static ConfigEntry<bool> playerEnabled;
    private static ConfigEntry<float> baseMegingjordBuff;

    internal static bool IsActive => plugin != null && IsSupportedVersion(plugin.Metadata.Version);

    internal static void CheckForCompatibility()
    {
        if (!Chainloader.PluginInfos.TryGetValue(GUID, out plugin))
            return;

        if (!IsSupportedVersion(plugin.Metadata.Version))
        {
            ExtraSlots.LogWarning($"ValheimPlus compatibility requires {MinimumSupportedVersion} or newer. Found {plugin.Metadata.Version}; no compatibility patches were applied.");
            plugin = null;
            return;
        }

        assembly = plugin.Instance.GetType().Assembly;

        plugin.Instance.Config.TryGetEntry("Player", "enabled", out playerEnabled);
        plugin.Instance.Config.TryGetEntry("Player", "baseMegingjordBuff", out baseMegingjordBuff);

        ApplyCompatibilityPatches();
    }


    private static bool IsSupportedVersion(System.Version version) =>
        version != null && version.CompareTo(MinimumSupportedVersion) >= 0;

    private static void ApplyCompatibilityPatches()
    {
        if (!IsActive)
            return;

        // ExtraSlots owns player inventory topology. V+ container sizing and container UI patches
        // remain intact; only V+ patches that resize the player inventory are removed.
        assembly.RemoveHarmonyPatch(
            typeof(Player), nameof(Player.SetInventorySize),
            "ValheimPlus.GameClasses.Player_SetInventorySize_Patch", "Transpiler",
            "let ExtraSlots own player inventory topology");
        assembly.RemoveHarmonyPatch(
            typeof(Player), nameof(Player.Load),
            "ValheimPlus.GameClasses.Player_Load_InventorySize_Patch", "Prefix",
            "let ExtraSlots own player inventory topology");
        assembly.RemoveHarmonyPatch(
            typeof(Player), nameof(Player.OnSpawned),
            "ValheimPlus.GameClasses.Player_OnSpawned_InventorySize_Patch", "Postfix",
            "let ExtraSlots own player inventory topology");

        OrderAutoStackAroundExtraSlots();
    }

    private static void OrderAutoStackAroundExtraSlots()
    {
        MethodBase target = AccessTools.Method(typeof(Inventory), nameof(Inventory.StackAll), new[] { typeof(Inventory), typeof(bool) });
        Type patchType = assembly?.GetType("ValheimPlus.GameClasses.Inventory_StackAll_Patch", throwOnError: false);
        if (target == null || patchType == null)
            return;

        MethodInfo prefix = AccessTools.Method(patchType, "Prefix");
        MethodInfo postfix = AccessTools.Method(patchType, "Postfix");
        if (prefix == null || postfix == null)
            return;

        Patches info = Harmony.GetPatchInfo(target);
        Patch currentPrefix = info?.Prefixes.FirstOrDefault(entry => entry.owner == VPlusHarmonyID && entry.PatchMethod == prefix);
        Patch currentPostfix = info?.Postfixes.FirstOrDefault(entry => entry.owner == VPlusHarmonyID && entry.PatchMethod == postfix);
        if (currentPrefix == null || currentPostfix == null)
            return;

        bool prefixOrdered = currentPrefix.before?.Contains(ExtraSlots.pluginID) == true;
        bool postfixOrdered = currentPostfix.after?.Contains(ExtraSlots.pluginID) == true;
        if (prefixOrdered && postfixOrdered)
            return;

        assembly.RemoveHarmonyPatch(
            target,
            "ValheimPlus.GameClasses.Inventory_StackAll_Patch", "Prefix",
            "reorder Auto Stack before ExtraSlots protected-item handling");
        assembly.RemoveHarmonyPatch(
            target,
            "ValheimPlus.GameClasses.Inventory_StackAll_Patch", "Postfix",
            "reorder Auto Stack after ExtraSlots protected-item handling");

        string[] prefixBefore = (currentPrefix.before ?? Array.Empty<string>())
            .Append(ExtraSlots.pluginID).Distinct().ToArray();
        string[] postfixAfter = (currentPostfix.after ?? Array.Empty<string>())
            .Append(ExtraSlots.pluginID).Distinct().ToArray();

        Harmony vplusHarmony = new Harmony(VPlusHarmonyID);
        vplusHarmony.Patch(
            target,
            prefix: new HarmonyMethod(prefix)
            {
                priority = currentPrefix.priority,
                before = prefixBefore,
                after = currentPrefix.after
            },
            postfix: new HarmonyMethod(postfix)
            {
                priority = currentPostfix.priority,
                before = currentPostfix.before,
                after = postfixAfter
            });

        ExtraSlots.LogDebug("Ordered ValheimPlus Auto Stack outside ExtraSlots protected-item StackAll scope.");
    }

    internal static StatusEffect ProjectCarryWeightEffect(StatusEffect effect)
    {
        if (!IsActive || playerEnabled?.Value != true || baseMegingjordBuff == null || effect is not SE_Stats stats || stats.m_addMaxCarryWeight <= 0f)
            return effect;

        // V+ 0.10.1.1 adjusts positive SE_Stats carry bonuses in SE_Stats.Setup(). EasyFit works
        // with the template before Setup, so project the same value on a disposable clone only.
        SE_Stats projected = (SE_Stats)stats.Clone();
        projected.m_addMaxCarryWeight = projected.m_addMaxCarryWeight - 150f + baseMegingjordBuff.Value;
        return projected;
    }

    [HarmonyPatch]
    private static class ValheimPlus_ValheimPlusPlugin_PatchAll_ReapplyCompatibility
    {
        private static MethodBase target;

        private static bool Prepare()
        {
            if (!IsActive || assembly == null)
                return false;

            Type pluginType = assembly.GetType("ValheimPlus.ValheimPlusPlugin", throwOnError: false);
            target ??= pluginType != null ? AccessTools.Method(pluginType, "PatchAll") : null;
            return target != null;
        }

        private static MethodBase TargetMethod() => target;

        [HarmonyFinalizer]
        private static void Finalizer() => ApplyCompatibilityPatches();
    }
}
