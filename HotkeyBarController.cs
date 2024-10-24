using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class HotkeyBarController
    {
        public static List<HotkeyBar> HotkeyBars;
        public static int SelectedHotkeyBarIndex = -1;

        public static void ClearBars()
        {
            HotkeyBars = null;
            SelectedHotkeyBarIndex = -1;
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        public static class Hud_Update_Patch
        {
            public static void Postfix(Hud __instance)
            {
                QuickSlotsHotBar.UpdateBar();
                AmmoSlotsHotBar.UpdateBar();

                HotkeyBars ??= __instance.transform.parent.GetComponentsInChildren<HotkeyBar>().ToList();

                if (Player.m_localPlayer)
                    if (SelectedHotkeyBarIndex >= 0 && SelectedHotkeyBarIndex < HotkeyBars.Count)   
                        UpdateHotkeyBarInput(HotkeyBars[SelectedHotkeyBarIndex]);
                    else
                        UpdateInitialHotkeyBarInput();

                foreach (var hotkeyBar in HotkeyBars)
                {
                    if (hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1)
                        hotkeyBar.m_selected = Mathf.Max(0, hotkeyBar.m_elements.Count - 1);

                    hotkeyBar.UpdateIcons(Player.m_localPlayer);
                }
            }

            private static void UpdateInitialHotkeyBarInput()
            {
                if (ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyDPadRight"))
                    SelectHotkeyBar(0, false);
            }

            public static void UpdateHotkeyBarInput(HotkeyBar hotkeyBar)
            {
                var player = Player.m_localPlayer;
                if (hotkeyBar.m_selected >= 0 && player != null && !InventoryGui.IsVisible() && !Menu.IsVisible() && !GameCamera.InFreeFly())
                {
                    if (ZInput.GetButtonDown("JoyDPadLeft"))
                        if (hotkeyBar.m_selected == 0)
                            GotoHotkeyBar(SelectedHotkeyBarIndex - 1);
                        else
                            hotkeyBar.m_selected = Mathf.Max(0, hotkeyBar.m_selected - 1);
                    else if (ZInput.GetButtonDown("JoyDPadRight"))
                        if (hotkeyBar.m_selected == hotkeyBar.m_elements.Count - 1)
                            GotoHotkeyBar(SelectedHotkeyBarIndex + 1);
                        else
                            hotkeyBar.m_selected = Mathf.Min(hotkeyBar.m_elements.Count - 1, hotkeyBar.m_selected + 1);

                    if (ZInput.GetButtonDown("JoyDPadUp"))
                        if (hotkeyBar.name == QuickSlotsHotBar.barName)
                            player.UseItem(null, QuickSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), false);
                        else if (hotkeyBar.name == AmmoSlotsHotBar.barName)
                            player.UseItem(null, AmmoSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), false);
                        else
                            player.UseHotbarItem(hotkeyBar.m_selected + 1);
                }

                if (hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1)
                    hotkeyBar.m_selected = Mathf.Max(0, hotkeyBar.m_elements.Count - 1);
            }

            public static void GotoHotkeyBar(int newIndex)
            {
                if (newIndex < 0 || newIndex >= HotkeyBars.Count)
                    return;

                var fromRight = newIndex < SelectedHotkeyBarIndex;
                SelectHotkeyBar(newIndex, fromRight);
            }

            public static void SelectHotkeyBar(int index, bool fromRight)
            {
                if (index < 0 || index >= HotkeyBars.Count)
                    return;

                SelectedHotkeyBarIndex = index;
                HotkeyBars.Do(hotkeyBar => hotkeyBar.m_selected = HotkeyBars.IndexOf(hotkeyBar) == index ? (fromRight ? hotkeyBar.m_elements.Count - 1 : 0) : -1);
            }

            public static void DeselectHotkeyBar()
            {
                SelectedHotkeyBarIndex = -1;
                foreach (var hotkeyBar in HotkeyBars)
                    hotkeyBar.m_selected = -1;
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        public static class Hud_OnDestroy_Patch
        {
            public static void Postfix() => ClearBars();
        }
    }

    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.Update))]
    public static class HotkeyBar_Update_Patch
    {
        public static bool Prefix()
        {
            // Logic implemented in HotkeyBarController
            return false;
        }
    }

    [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.UpdateIcons))]
    public static class HotkeyBar_UpdateIcons_QuickBars
    {
        public static bool inCall;
        public static string barName;
        
        public static void Prefix(HotkeyBar __instance)
        {
            if (__instance.name != QuickSlotsHotBar.barName && __instance.name != AmmoSlotsHotBar.barName)
                return;

            barName = __instance.name;

            inCall = true;
        }

        public static void Postfix(HotkeyBar __instance)
        {
            if (!inCall)
                return;

            inCall = false;

            for (int index = 0; index < __instance.m_elements.Count; index++)
                if (__instance.name == QuickSlotsHotBar.barName)
                    EquipmentPanel.SetSlotLabel(__instance.m_elements[index].m_go.transform.Find("binding"), slots[index]);
                else if (__instance.name == AmmoSlotsHotBar.barName)
                    EquipmentPanel.SetSlotLabel(__instance.m_elements[index].m_go.transform.Find("binding"), slots[index + 8]);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetBoundItems))]
    public static class Inventory_GetBoundItems_HotkeyBarQuickSlots
    {
        public static void Postfix(Inventory __instance, List<ItemDrop.ItemData> bound)
        {
            if (__instance == PlayerInventory && HotkeyBar_UpdateIcons_QuickBars.inCall)
            {
                bound.Clear();
                if (HotkeyBar_UpdateIcons_QuickBars.barName == QuickSlotsHotBar.barName)
                    QuickSlotsHotBar.GetItems(bound);
                else if (HotkeyBar_UpdateIcons_QuickBars.barName == AmmoSlotsHotBar.barName)
                    AmmoSlotsHotBar.GetItems(bound);
            }
        }
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
    public static class Hud_Awake_CreateQuickSlotsBar
    {
        public static void Postfix()
        {
            QuickSlotsHotBar.MarkDirty();
            AmmoSlotsHotBar.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
    public static class Hud_OnDestroy_ClearQuickSlotBar
    {
        public static void Postfix()
        {
            QuickSlotsHotBar.ClearBar();
            AmmoSlotsHotBar.ClearBar();
        }
    }
}