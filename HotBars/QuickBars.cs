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
        public const string vanillaBarName = "HotKeyBar";

        private static List<HotkeyBar> bars;
        private static int _currentBarIndex = -1;

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

        // Patch this method if you want your bar to be controlled in the same way
        public static bool IsBarToControl(HotkeyBar bar) => bar != null && barNames.Contains(bar.name);

        public static void UseCustomBarItem(HotkeyBar bar)
        {
            // Patch this method to use selected item from your hotbar
        }

        private static List<HotkeyBar> GetHotKeyBarsToControl() => Hud.instance ? Hud.instance.transform.parent.GetComponentsInChildren<HotkeyBar>().Where(IsBarToControl).ToList() : null;

        private static bool UpdateCurrentHotkeyBar()
        {
            if (_currentBarIndex < 0 || _currentBarIndex > bars.Count - 1)
                return false;

            HotkeyBar hotkeyBar = bars[_currentBarIndex];
            if (hotkeyBar.m_selected < 0 || hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1 || !IsHotkeyBarsActive())
                return !IsHotkeyBarsActive();

            if (ZInput.GetButtonDown("JoyDPadLeft") && --hotkeyBar.m_selected < 0)
                ChangeActiveHotkeyBar(next: false);
            else if (ZInput.GetButtonDown("JoyDPadRight") && ++hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1)
                ChangeActiveHotkeyBar(next: true);
            else if (ZInput.GetButtonDown("JoyDPadUp"))
                if (hotkeyBar.name == QuickSlotsHotBar.barName)
                    Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), QuickSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
                else if (hotkeyBar.name == AmmoSlotsHotBar.barName)
                    Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), AmmoSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
                else if (hotkeyBar.name == vanillaBarName)
                    Player.m_localPlayer.UseHotbarItem(hotkeyBar.m_selected + 1);
                else
                    UseCustomBarItem(hotkeyBar);

            return true;
        }

        private static void ChangeActiveHotkeyBar(bool next = true)
        {
            int[] activeBars = bars.Where(bar => bar.m_elements.Count > 0).Select(bar => bars.IndexOf(bar)).ToArray();
            if (activeBars.Length == 0)
            {
                _currentBarIndex = -1;
                return;
            }

            int index = Array.IndexOf(activeBars, _currentBarIndex);
            index = (index == -1) ? 0 : index + (next ? 1 : -1);

            _currentBarIndex = activeBars[(index + activeBars.Length) % activeBars.Length];
            bars[_currentBarIndex].m_selected = next ? 0 : bars[_currentBarIndex].m_elements.Count - 1;
        }

        private static bool IsHotkeyBarsActive() => !InventoryGui.IsVisible() && !Menu.IsVisible() && !GameCamera.InFreeFly()
                                                    && !Minimap.IsOpen() && !Hud.IsPieceSelectionVisible() && !StoreGui.IsVisible()
                                                    && !Console.IsVisible() && !Chat.instance.HasFocus() && !PlayerCustomizaton.IsBarberGuiVisible()
                                                    && !Hud.InRadial();

        // Runs every frame Player.Update
        internal static void UpdateItemUse()
        {
            if (!Player.m_localPlayer.TakeInput())
                return;

            if (GetItemToUse() is ItemDrop.ItemData item)
                Player.m_localPlayer.UseItem(PlayerInventory, item, fromInventoryGui: false);
        }

        private static ItemDrop.ItemData GetItemToUse()
        {
            Slot quickSlotUsed = QuickSlotsHotBar.GetSlotWithShortcutDown();
            Slot ammoSlotUsed = AmmoSlotsHotBar.GetSlotWithShortcutDown();

            if (quickSlotUsed != null && ammoSlotUsed != null)
            {
                if (quickSlotUsed.GetShortcut().Modifiers.Count() >= ammoSlotUsed.GetShortcut().Modifiers.Count())
                    return quickSlotUsed.Item;
                else
                    return ammoSlotUsed.Item;
            }
            else if (quickSlotUsed != null)
                return quickSlotUsed.Item;
            else if (ammoSlotUsed != null)
                return ammoSlotUsed.Item;

            return null;
        }

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

                if (!UpdateCurrentHotkeyBar() && (ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyDPadRight") || ZInput.GetButtonDown("JoyDPadUp")))
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
                    bar.m_selected = _currentBarIndex != i ? -1 : Mathf.Clamp(bar.m_selected, -1, bar.m_elements.Count - 1);
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

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update_SlotsUse
        {
            private static void Postfix(Player __instance)
            {
                if (!IsValidPlayer(__instance))
                    return;

                UpdateItemUse();
            }
        }

    }
}