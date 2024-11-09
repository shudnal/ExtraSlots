using HarmonyLib;
using System;
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
        public static class Hud_Update_BarController
        {
            public static void Postfix(Hud __instance)
            {
                if (!Player.m_localPlayer)
                    return;

                AmmoSlotsHotBar.UpdateBar();
                QuickSlotsHotBar.UpdateBar();

                HotkeyBars ??= __instance.transform.parent.GetComponentsInChildren<HotkeyBar>().ToList();

                if (SelectedHotkeyBarIndex < 0 || SelectedHotkeyBarIndex > HotkeyBars.Count - 1 || !UpdateHotkeyBar(HotkeyBars[SelectedHotkeyBarIndex]))
                    UpdateInitializeHotkeyBar();

                foreach (HotkeyBar bar in HotkeyBars)
                {
                    bar.m_selected = Mathf.Clamp(bar.m_selected, -1, bar.m_elements.Count - 1);
                    bar.UpdateIcons(Player.m_localPlayer);
                }
            }

            private static void UpdateInitializeHotkeyBar()
            {
                if (ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyDPadRight") || ZInput.GetButtonDown("JoyDPadUp"))
                    SelectHotkeyBar();
            }

            public static bool UpdateHotkeyBar(HotkeyBar hotkeyBar)
            {
                bool isOperational = IsHotkeyBarOperational();
                if (hotkeyBar.m_selected >= 0 && isOperational)
                {
                    if (ZInput.GetButtonDown("JoyDPadLeft"))
                        if (hotkeyBar.m_selected == 0)
                            SelectHotkeyBar(right: false);
                        else
                            hotkeyBar.m_selected = Mathf.Max(0, hotkeyBar.m_selected - 1);
                    else if (ZInput.GetButtonDown("JoyDPadRight"))
                        if (hotkeyBar.m_selected == hotkeyBar.m_elements.Count - 1)
                            SelectHotkeyBar(right: true);
                        else
                            hotkeyBar.m_selected = Mathf.Min(hotkeyBar.m_elements.Count - 1, hotkeyBar.m_selected + 1);

                    if (ZInput.GetButtonDown("JoyDPadUp"))
                        if (hotkeyBar.name == QuickSlotsHotBar.barName)
                            Player.m_localPlayer.UseItem(null, QuickSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), false);
                        else if (hotkeyBar.name == AmmoSlotsHotBar.barName)
                            Player.m_localPlayer.UseItem(null, AmmoSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), false);
                        else
                            Player.m_localPlayer.UseHotbarItem(hotkeyBar.m_selected + 1);

                    return true;
                }

                return !isOperational;
            }

            public static void SelectHotkeyBar(bool right = true)
            {
                int[] activeBars = HotkeyBars.Where(bar => bar.m_elements.Count > 0).Select(bar => HotkeyBars.IndexOf(bar)).ToArray();
                if (activeBars.Length == 0)
                {
                    SelectedHotkeyBarIndex = -1;
                    return;
                }

                int index = Array.IndexOf(activeBars, SelectedHotkeyBarIndex);
                if (index == -1)
                    index = 0;
                else if (right)
                    index++;
                else
                    index--;

                SelectedHotkeyBarIndex = activeBars[(index + activeBars.Length) % activeBars.Length];
                HotkeyBars.Do(bar => bar.m_selected = HotkeyBars.IndexOf(bar) == SelectedHotkeyBarIndex ? (right ? 0 : bar.m_elements.Count - 1) : -1);
            }

            public static bool IsHotkeyBarOperational() => !InventoryGui.IsVisible() && !Menu.IsVisible() && !GameCamera.InFreeFly() 
                                                        && !Minimap.IsOpen() && !Hud.IsPieceSelectionVisible() && !StoreGui.IsVisible() 
                                                        && !Console.IsVisible() && !Chat.instance.HasFocus() && !PlayerCustomizaton.IsBarberGuiVisible() 
                                                        && !Hud.InRadial();
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        public static class Hud_OnDestroy_ClearBars
        {
            public static void Postfix() => ClearBars();
        }

        [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.Update))]
        public static class HotkeyBar_Update_PreventCall
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix()
            {
                // Logic implemented on Hud.Update
                return false;
            }
        }

        [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.UpdateIcons))]
        public static class HotkeyBar_UpdateIcons_QuickBarsUpdate
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
                        EquipmentPanel.SetSlotLabel(__instance.m_elements[index].m_go.transform.Find("binding"), slots[index], hotbarElement: true);
                    else if (__instance.name == AmmoSlotsHotBar.barName)
                        EquipmentPanel.SetSlotLabel(__instance.m_elements[index].m_go.transform.Find("binding"), slots[index + 8], hotbarElement: true);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetBoundItems))]
        public static class Inventory_GetBoundItems_QuickBarsItems
        {
            public static void Postfix(Inventory __instance, List<ItemDrop.ItemData> bound)
            {
                if (__instance == PlayerInventory && HotkeyBar_UpdateIcons_QuickBarsUpdate.inCall)
                {
                    bound.Clear();
                    if (HotkeyBar_UpdateIcons_QuickBarsUpdate.barName == QuickSlotsHotBar.barName)
                        QuickSlotsHotBar.GetItems(bound);
                    else if (HotkeyBar_UpdateIcons_QuickBarsUpdate.barName == AmmoSlotsHotBar.barName)
                        AmmoSlotsHotBar.GetItems(bound);
                }
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        public static class Hud_Awake_CreateQuickBars
        {
            public static void Postfix()
            {
                QuickSlotsHotBar.MarkDirty();
                AmmoSlotsHotBar.MarkDirty();
                ClearBars();
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        public static class Hud_OnDestroy_ClearQuickBars
        {
            public static void Postfix()
            {
                QuickSlotsHotBar.ClearBar();
                AmmoSlotsHotBar.ClearBar();
            }
        }
    }
}