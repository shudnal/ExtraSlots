using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static ExtraSlots.Slots;

namespace ExtraSlots.HotBars;

public static class QuickBars
{
    public const string vanillaBarName = "HotKeyBar";

    private static List<HotkeyBar> bars;
    private static int _currentBarIndex = -1;

    private static readonly HashSet<string> barNames = new HashSet<string>(){
            vanillaBarName,
            QuickSlotsHotBar.barName,
            AmmoSlotsHotBar.barName,
            FoodSlotsHotBar.barName,
        };
    private static readonly Dictionary<GameObject, Tuple<RectTransform, TMP_Text>> elementsExtraData = new Dictionary<GameObject, Tuple<RectTransform, TMP_Text>>();

    public static RectTransform InstantiateHotKeyBar(string barName)
    {
        RectTransform vanillaBar = Hud.instance.m_rootObject.transform.Find(vanillaBarName).GetComponent<RectTransform>();
        RectTransform result = UnityEngine.Object.Instantiate(vanillaBar, Hud.instance.m_rootObject.transform, true);
        result.name = barName;
        result.localPosition = Vector3.zero;
        result.SetSiblingIndex(vanillaBar.GetSiblingIndex() + 1);

        return result;
    }

    public static void ResetBars()
    {
        elementsExtraData.Clear();
        _currentBarIndex = -1;
        bars = GetHotKeyBarsToControl();
    }

    // Patch this method if you want your bar to be controlled in the same way
    public static bool IsBarToControl(HotkeyBar bar) => bar != null && barNames.Contains(bar.name);

    public static void UseCustomBarItem(HotkeyBar bar)
    {
        // Patch this method to use selected item from your hotbar
    }

    private static Vector3 LeftTopPoint => Hud.instance ? new Vector3(-Hud.instance.m_rootObject.transform.position.x, Hud.instance.m_rootObject.transform.position.y, 0) : new Vector3(-1280, 720, 0);

    private static List<HotkeyBar> GetHotKeyBarsToControl() => Hud.instance ? Hud.instance.m_rootObject.GetComponentsInChildren<HotkeyBar>().Where(IsBarToControl).OrderBy(bar => Vector3.Distance(bar.transform.localPosition, LeftTopPoint)).ToList() : null;

    private static bool UpdateCurrentHotkeyBar()
    {
        if (_currentBarIndex < 0 || _currentBarIndex > bars.Count - 1)
            return false;

        HotkeyBar hotkeyBar = bars[_currentBarIndex];
        if (hotkeyBar.m_selected < 0 || hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1 || !IsHotkeyBarsActive())
            return !IsHotkeyBarsActive();

        if (GetButtonDown("JoyDPadLeft") && --hotkeyBar.m_selected < 0)
            ChangeActiveHotkeyBar(next: false);
        else if (GetButtonDown("JoyDPadRight") && ++hotkeyBar.m_selected > hotkeyBar.m_elements.Count - 1)
            ChangeActiveHotkeyBar(next: true);
        else if (GetButtonDown("JoyDPadUp"))
            if (hotkeyBar.name == QuickSlotsHotBar.barName)
                Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), QuickSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
            else if (hotkeyBar.name == AmmoSlotsHotBar.barName)
                Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), AmmoSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
            else if (hotkeyBar.name == FoodSlotsHotBar.barName)
                Player.m_localPlayer.UseItem(Player.m_localPlayer.GetInventory(), FoodSlotsHotBar.GetItemInSlot(hotkeyBar.m_selected), fromInventoryGui: false);
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

        if (!ExtraSlots.useSingleHotbarItem.Value)
            GetItemsToUse().Do(item => Player.m_localPlayer.UseItem(PlayerInventory, item, fromInventoryGui: false));
        else if (GetItemToUse() is ItemDrop.ItemData item)
            Player.m_localPlayer.UseItem(PlayerInventory, item, fromInventoryGui: false);
    }

    private static ItemDrop.ItemData GetItemToUse()
    {
        Slot quickSlotUsed = QuickSlotsHotBar.GetSlotWithShortcutDown();
        Slot ammoSlotUsed = AmmoSlotsHotBar.GetSlotWithShortcutDown();
        Slot foodSlotUsed = FoodSlotsHotBar.GetSlotWithShortcutDown();

        if (quickSlotUsed != null && ammoSlotUsed != null && foodSlotUsed != null)
        {
            int quickModifiers = quickSlotUsed.GetShortcut().Modifiers.Count();
            int ammoModifiers = ammoSlotUsed.GetShortcut().Modifiers.Count();
            int foodModifiers = foodSlotUsed.GetShortcut().Modifiers.Count();

            if (quickModifiers >= ammoModifiers && quickModifiers >= foodModifiers)
                return quickSlotUsed.Item;
            else if (ammoModifiers >= quickModifiers && ammoModifiers >= foodModifiers)
                return ammoSlotUsed.Item;
            else
                return foodSlotUsed.Item;
        }
        else if (quickSlotUsed != null && ammoSlotUsed != null)
        {
            if (quickSlotUsed.GetShortcut().Modifiers.Count() >= ammoSlotUsed.GetShortcut().Modifiers.Count())
                return quickSlotUsed.Item;
            else
                return ammoSlotUsed.Item;
        }
        else if (quickSlotUsed != null && foodSlotUsed != null)
        {
            if (quickSlotUsed.GetShortcut().Modifiers.Count() >= foodSlotUsed.GetShortcut().Modifiers.Count())
                return quickSlotUsed.Item;
            else
                return foodSlotUsed.Item;
        }
        else if (ammoSlotUsed != null && foodSlotUsed != null)
        {
            if (ammoSlotUsed.GetShortcut().Modifiers.Count() >= foodSlotUsed.GetShortcut().Modifiers.Count())
                return ammoSlotUsed.Item;
            else
                return foodSlotUsed.Item;
        }
        else if (quickSlotUsed != null)
            return quickSlotUsed.Item;
        else if (ammoSlotUsed != null)
            return ammoSlotUsed.Item;
        else if (foodSlotUsed != null)
            return foodSlotUsed.Item;

        return null;
    }

    private static IEnumerable<ItemDrop.ItemData> GetItemsToUse() => QuickSlotsHotBar.GetSlotsWithShortcutDown().Concat(AmmoSlotsHotBar.GetSlotsWithShortcutDown()).Concat(FoodSlotsHotBar.GetSlotsWithShortcutDown()).Select(slot => slot.Item);

    private static bool GetButtonDown(string name) => !Compatibility.PlantEasilyCompat.DisableGamepadInput && ZInput.GetButtonDown(name);

    [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
    public static class Hud_Update_BarController
    {
        public static void Postfix()
        {
            if (!Player.m_localPlayer)
                return;

            if (QuickSlotsHotBar.Refresh() || AmmoSlotsHotBar.Refresh() || FoodSlotsHotBar.Refresh())
                ResetBars();
            else 
                bars ??= GetHotKeyBarsToControl();

            if (bars == null)
                return;

            if (!UpdateCurrentHotkeyBar() && (GetButtonDown("JoyDPadLeft") || GetButtonDown("JoyDPadRight") || GetButtonDown("JoyDPadUp")))
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
                bars.Do(bar => { bar.m_items.Clear(); bar.UpdateIcons(null); });
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
            if (__instance.name != QuickSlotsHotBar.barName && __instance.name != AmmoSlotsHotBar.barName && __instance.name != FoodSlotsHotBar.barName)
                return;

            barName = __instance.name;

            inCall = true;
        }

        public static void Postfix(HotkeyBar __instance)
        {
            if (!inCall)
                return;

            inCall = false;

            if (__instance.name == QuickSlotsHotBar.barName || __instance.name == AmmoSlotsHotBar.barName || __instance.name == FoodSlotsHotBar.barName)
                for (int index = 0; index < __instance.m_elements.Count; index++)
                {
                    HotkeyBar.ElementData elementData = __instance.m_elements[index];
                    if (!elementsExtraData.ContainsKey(elementData.m_go))
                    {
                        Transform binding = elementData.m_go.transform.Find("binding");
                        elementsExtraData[elementData.m_go] = Tuple.Create(binding.GetComponent<RectTransform>(), binding.GetComponent<TMP_Text>());
                    }

                    Tuple<RectTransform, TMP_Text> extraData = elementsExtraData[elementData.m_go];

                    Slot slot = slots[index + (__instance.name == FoodSlotsHotBar.barName ? FoodSlotsHotBar.barSlotIndex : __instance.name == AmmoSlotsHotBar.barName ? AmmoSlotsHotBar.barSlotIndex : QuickSlotsHotBar.barSlotIndex)];
                    EquipmentPanel.SetSlotLabel(extraData.Item1, extraData.Item2, slot, hotbarElement: true);

                    bool hideStackSize = __instance.name == QuickSlotsHotBar.barName ? ExtraSlots.quickSlotsHideStackSize.Value : __instance.name == AmmoSlotsHotBar.barName ? ExtraSlots.ammoSlotsHideStackSize.Value : ExtraSlots.foodSlotsHideStackSize.Value;
                    if (hideStackSize && elementData.m_amount.gameObject.activeInHierarchy && (slot.Item is ItemDrop.ItemData item) && (item.IsEquipable() || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable))
                        elementData.m_amount.SetText(elementData.m_stackText.ToFastString());
                    
                    int widthInElements = __instance.name == QuickSlotsHotBar.barName ? ExtraSlots.quickSlotsWidthInElements.Value : __instance.name == AmmoSlotsHotBar.barName ? ExtraSlots.ammoSlotsWidthInElements.Value : ExtraSlots.foodSlotsWidthInElements.Value;
                    bool fillUp = __instance.name == QuickSlotsHotBar.barName ? ExtraSlots.quickSlotsFillDirectionUp.Value : __instance.name == AmmoSlotsHotBar.barName ? ExtraSlots.ammoSlotsFillDirectionUp.Value : ExtraSlots.foodSlotsFillDirectionUp.Value;
                    float elementSpace = __instance.name == QuickSlotsHotBar.barName ? ExtraSlots.quickSlotsElementSpace.Value : __instance.name == AmmoSlotsHotBar.barName ? ExtraSlots.ammoSlotsElementSpace.Value : ExtraSlots.foodSlotsElementSpace.Value;
                    elementData.m_go.transform.localPosition = new Vector3(index % widthInElements, (fillUp ? 1 : -1) * index / widthInElements, 0f) * elementSpace;
                }

            if (__instance.name == FoodSlotsHotBar.barName)
                FoodSlotsHotBar.RestoreGridPos();
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
                else if (HotkeyBar_UpdateIcons_QuickBarsUpdate.barName == FoodSlotsHotBar.barName)
                    FoodSlotsHotBar.GetItems(bound);
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
            FoodSlotsHotBar.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
    public static class Hud_OnDestroy_ClearQuickBars
    {
        public static void Postfix()
        {
            QuickSlotsHotBar.ClearBar();
            AmmoSlotsHotBar.ClearBar();
            FoodSlotsHotBar.ClearBar();
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