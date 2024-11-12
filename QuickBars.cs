using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class QuickBars
    {
        public static List<HotkeyBar> bars;
        public static int _currentBarIndex = -1;
        public const string vanillaBarName = "HotKeyBar";

        private static readonly HashSet<string> barNames = new HashSet<string>(){
                vanillaBarName,
                QuickSlotsHotBar.barName,
                AmmoSlotsHotBar.barName
            };

        public static void ResetBars()
        {
            _currentBarIndex = -1;
            bars = GetHotKeyBarsToControl();
        }

        public static bool IsBarToControl(HotkeyBar bar) => bar != null && barNames.Contains(bar.name);

        public static List<HotkeyBar> GetHotKeyBarsToControl() => Hud.instance ? Hud.instance.transform.parent.GetComponentsInChildren<HotkeyBar>().Where(IsBarToControl).ToList() : null;

        private static bool IsSelectedIndex() => _currentBarIndex >= 0 && _currentBarIndex < bars.Count;

        private static bool UpdateHotkeyBar(HotkeyBar hotkeyBar)
        {
            if (hotkeyBar.m_selected < 0 || !IsHotkeyBarActive())
                return !IsHotkeyBarActive();

            if (ZInput.GetButtonDown("JoyDPadLeft"))
                hotkeyBar.m_selected = hotkeyBar.m_selected == 0 ? ChangeActiveHotkeyBar(next: false) : Mathf.Max(0, hotkeyBar.m_selected - 1);
            else if (ZInput.GetButtonDown("JoyDPadRight"))
                hotkeyBar.m_selected = hotkeyBar.m_selected == hotkeyBar.m_elements.Count - 1 ? ChangeActiveHotkeyBar(next: true) : Mathf.Min(hotkeyBar.m_elements.Count - 1, hotkeyBar.m_selected + 1);

            if (ZInput.GetButtonDown("JoyDPadUp"))
                if (hotkeyBar.name == QuickSlotsHotBar.barName)
                    Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), QuickSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
                else if (hotkeyBar.name == AmmoSlotsHotBar.barName)
                    Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), AmmoSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
                else
                    Player.m_localPlayer.UseHotbarItem(hotkeyBar.m_selected + 1);

            return true;
        }

        private static int ChangeActiveHotkeyBar(bool next = true)
        {
            int[] activeBars = bars.Where(bar => bar.m_elements.Count > 0).Select(bar => bars.IndexOf(bar)).ToArray();
            if (activeBars.Length == 0)
            {
                _currentBarIndex = -1;
                return _currentBarIndex;
            }

            int index = Array.IndexOf(activeBars, _currentBarIndex);
            index = (index == -1) ? 0 : index + (next ? 1 : -1);

            _currentBarIndex = activeBars[(index + activeBars.Length) % activeBars.Length];
            bars.Do(bar => bar.m_selected = bars.IndexOf(bar) == _currentBarIndex ? (next ? 0 : bar.m_elements.Count - 1) : -1);
            return _currentBarIndex;
        }

        private static bool IsHotkeyBarActive() => !InventoryGui.IsVisible() && !Menu.IsVisible() && !GameCamera.InFreeFly()
                                                    && !Minimap.IsOpen() && !Hud.IsPieceSelectionVisible() && !StoreGui.IsVisible()
                                                    && !Console.IsVisible() && !Chat.instance.HasFocus() && !PlayerCustomizaton.IsBarberGuiVisible()
                                                    && !Hud.InRadial();

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        public static class Hud_Update_BarController
        {
            public static void Postfix()
            {
                if (!Player.m_localPlayer)
                    return;

                if (AmmoSlotsHotBar.Refresh() || QuickSlotsHotBar.Refresh())
                    ResetBars();
                else 
                    bars ??= GetHotKeyBarsToControl();

                if (bars == null)
                    return;

                if (!IsSelectedIndex() || !UpdateHotkeyBar(bars[_currentBarIndex]))
                    if (ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyDPadRight") || ZInput.GetButtonDown("JoyDPadUp"))
                        ChangeActiveHotkeyBar();

                bool clearBars = false;
                for (int i = bars.Count - 1; i >= 0; i--)
                {
                    if (bars[i] == null)
                    {
                        bars.RemoveAt(i);
                        clearBars = true;
                        continue;
                    }

                    HotkeyBar bar = bars[i];
                    bar.m_selected = Mathf.Clamp(bar.m_selected, -1, bar.m_elements.Count - 1);
                    bar.UpdateIcons(Player.m_localPlayer);
                }

                if (clearBars)
                    bars.Do(bar => bar.UpdateIcons(null));
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        public static class Hud_OnDestroy_ResetBars
        {
            public static void Postfix() => ResetBars();
        }

        [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.Update))]
        public static class HotkeyBar_Update_PreventCall
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix() => false;
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