using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Valheim.UI;
using Object = UnityEngine.Object;

namespace ExtraSlots.Radial;

internal static class NativeRadialMenus
{
    private const string EntryObjectName = "ExtraSlotsQuickBarsRadialEntry";
    private const int HotbarElementsPerLayer = 12;

    private static bool IsExtendedHotbar(RadialBase radial) => radial.CurrentConfig is HotbarGroupConfig
        && radial.m_elements != null && radial.m_elements.Count == HotbarElementsPerLayer
        && radial.m_elements.GetElement(6) is GroupElement group && group.name == EntryObjectName;

    private sealed class ConstructionScope
    {
        internal readonly RadialBase Radial;
        internal readonly ConstructionScope Previous;

        internal ConstructionScope(RadialBase radial, ConstructionScope previous)
        {
            Radial = radial;
            Previous = previous;
        }
    }

    private static readonly Dictionary<RadialBase, ConstructionScope> constructionScopes = new Dictionary<RadialBase, ConstructionScope>();

    // Intercept construction, not the entire HotbarGroupConfig method. The game still
    // creates its eight ItemElement/EmptyElement entries, including other mods' changes.
    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.ConstructRadial))]
    private static class RadialBase_ConstructRadial_QuickBarsEntry
    {
        private static void Prefix(RadialBase __instance, ref List<RadialMenuElement> elements, out ConstructionScope __state)
        {
            __state = null;
            if (__instance.CurrentConfig is not HotbarGroupConfig || !QuickBarsRadialConfig.HasAvailableSlots()
                || elements == null || elements.Count != 8)
                return;
            foreach (RadialMenuElement element in elements)
                if (element is not ItemElement && element is not EmptyElement)
                    return;

            List<RadialMenuElement> owned = new List<RadialMenuElement>(4);
            bool assigned = false;
            try
            {
                List<RadialMenuElement> result = new List<RadialMenuElement>(new RadialMenuElement[HotbarElementsPerLayer]);
                BackElement back = Object.Instantiate(RadialData.SO.BackElement);
                owned.Add(back);
                back.Init(__instance);
                result[0] = back;
                for (int i = 0; i < 8; i++)
                    result[i < 4 ? i + 8 : i - 3] = elements[i];

                GroupElement group = Object.Instantiate(RadialData.SO.GroupElement);
                owned.Add(group);
                group.name = EntryObjectName;
                group.Init(new QuickBarsRadialConfig(), __instance.CurrentConfig, __instance);
                result[6] = group; // 180 degrees: always straight down.
                foreach (int index in new[] { 5, 7 })
                {
                    EmptyElement empty = Object.Instantiate(RadialData.SO.EmptyElement);
                    owned.Add(empty);
                    empty.Init();
                    result[index] = empty;
                }

                constructionScopes.TryGetValue(__instance, out ConstructionScope previous);
                __state = new ConstructionScope(__instance, previous);
                constructionScopes[__instance] = __state;
                __instance.StartOffset = 0;
                if (!__instance.IsRefresh)
                    __instance.StartItemIndex = 0;
                elements = result;
                assigned = true;
            }
            finally
            {
                if (!assigned)
                    foreach (RadialMenuElement element in owned)
                        if (element)
                            Object.Destroy(element.gameObject);
            }
        }

        private static void Finalizer(ConstructionScope __state)
        {
            if (__state == null || !constructionScopes.TryGetValue(__state.Radial, out ConstructionScope current)
                || !ReferenceEquals(current, __state))
                return;
            if (__state.Previous == null)
                constructionScopes.Remove(__state.Radial);
            else
                constructionScopes[__state.Radial] = __state.Previous;
        }
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.SetElementsPerLayer))]
    private static class RadialBase_SetElementsPerLayer_QuickBars
    {
        private static bool Prefix(RadialBase __instance)
        {
            if (__instance.CurrentConfig is not QuickBarsRadialConfig && !constructionScopes.ContainsKey(__instance))
                return true;
            // The parent keeps its twelve-position grid and downward entry. Only
            // the child needs room for Back, six side positions and six quick items.
            int elementsPerLayer = __instance.CurrentConfig is QuickBarsRadialConfig
                ? QuickBarsRadialConfig.ElementsPerLayer : HotbarElementsPerLayer;
            if (__instance.MaxElementsPerLayer != elementsPerLayer || __instance.m_segmentSize == 0f)
            {
                __instance.MaxElementsPerLayer = elementsPerLayer;
                __instance.UpdateSegmentInfo();
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.CenterBackButton))]
    private static class RadialBase_CenterBackButton_QuickBars
    {
        private static bool Prefix(RadialBase __instance) => __instance.CurrentConfig is not QuickBarsRadialConfig
            && !constructionScopes.ContainsKey(__instance);
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.SetRadialLayout))]
    private static class RadialBase_SetRadialLayout_QuickBars
    {
        private static void Postfix(RadialBase __instance)
        {
            if (__instance.CurrentConfig is QuickBarsRadialConfig config)
                config.ApplyLayout(__instance);
            else if (IsExtendedHotbar(__instance))
            {
                foreach (int index in new[] { 5, 7 })
                {
                    RadialMenuElement gap = __instance.m_elements.GetElement(index);
                    if (gap.Background)
                        gap.Background.enabled = false;
                    if (gap.Icon)
                        gap.Icon.gameObject.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.UpdateSelection))]
    private static class RadialBase_UpdateSelection_QuickBarsGaps
    {
        private static void Prefix(RadialBase __instance)
        {
            if ((__instance.m_index == 5 || __instance.m_index == 7) && IsExtendedHotbar(__instance))
                __instance.m_index = -1;
        }
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.SelectStartElement))]
    private static class RadialBase_SelectStartElement_QuickBars
    {
        private static bool Prefix(RadialBase __instance) => __instance.CurrentConfig is not QuickBarsRadialConfig config
            || !config.SelectStart(__instance);
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.OnUpdateCursorInput))]
    private static class RadialBase_OnUpdateCursorInput_QuickBars
    {
        private static bool Prefix(RadialBase __instance, Vector2 newInput) => __instance.CurrentConfig is not QuickBarsRadialConfig config
            || !config.UpdateCursor(__instance, newInput);
    }

    [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.Update))]
    private static class RadialBase_Update_QuickBars
    {
        private static void Prefix(RadialBase __instance)
        {
            if (!__instance.Active || __instance.m_isClosing || __instance.m_closeQueued
                || __instance.CurrentConfig is not QuickBarsRadialConfig config
                || !Player.m_localPlayer || Player.m_localPlayer.IsDead()
                || __instance.m_queuedOpenConfigs.openConfig != null || !config.NeedsRefresh)
                return;
            if (QuickBarsRadialConfig.HasAvailableSlots())
                __instance.Refresh();
            else
                __instance.Back();
        }
    }
}
