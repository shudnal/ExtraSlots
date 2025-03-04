using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots.HotBars;

public static class AmmoSlotsHotBar
{
    public const string barName = "ExtraSlotsAmmoHotBar";
    public const int barSlotIndex = 8;
    public static bool isDirty = true;
    private static HotkeyBar hotBar = null;
    private static RectTransform hotBarRect = null;
    private static Slot[] hotBarSlots = Array.Empty<Slot>();

    internal static void UpdateSlots() => hotBarSlots = GetAmmoSlots();

    public static void GetItems(List<ItemDrop.ItemData> bound)
    {
        if (PlayerInventory == null)
            return;

        bound.AddRange(GetItems());
    }

    public static List<ItemDrop.ItemData> GetItems()
    {
        return hotBarSlots.Where(slot => slot.IsActive).Select(slot => slot.Item).Where(item => item != null).ToList();
    }

    public static ItemDrop.ItemData GetItemInSlot(int slotIndex) => slots[slotIndex + barSlotIndex].Item;

    internal static void MarkDirty() => isDirty = true;

    internal static void CreateBar()
    {
        hotBarRect = QuickBars.InstantiateHotKeyBar(barName);

        if (hotBar = hotBarRect.GetComponent<HotkeyBar>())
        {
            hotBar.m_selected = -1;

            foreach (HotkeyBar.ElementData element in hotBar.m_elements)
                UnityEngine.Object.Destroy(element.m_go);

            for (int i = hotBarRect.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(hotBarRect.GetChild(i).gameObject);

            hotBar.m_elements.Clear();
            hotBar.m_items.Clear();
        }
    }

    // Runs every frame Hud.Update
    internal static bool Refresh()
    {
        if (!isDirty)
            return false;

        if (IsEnabled() && hotBarRect == null)
            CreateBar();
        else if (!IsEnabled() && hotBarRect != null)
            ClearBar();

        if (hotBarRect != null)
        {
            hotBarRect.localScale = Vector3.one * ammoSlotsHotBarScale.Value;
            hotBarRect.SetAnchor(ammoSlotsHotBarAnchor.Value);
            hotBarRect.anchoredPosition = ammoSlotsHotBarOffset.Value;
        }

        isDirty = false;

        return true;
    }

    internal static void ClearBar()
    {
        if (hotBar != null)
            UnityEngine.Object.Destroy(hotBar.gameObject);

        if (hotBarRect != null)
            UnityEngine.Object.Destroy(hotBarRect.gameObject);

        hotBar = null;
        hotBarRect = null;
    }

    internal static bool IsEnabled() => ammoSlotsHotBarEnabled.Value;

    internal static bool IsShortcutDownWithItem(Slot slot) => slot.IsShortcutDown() && slot.Item != null;

    internal static Slot GetSlotWithShortcutDown() => IsEnabled() ? hotBarSlots.FirstOrDefault(IsShortcutDownWithItem) : null;

    internal static IEnumerable<Slot> GetSlotsWithShortcutDown() => IsEnabled() ? hotBarSlots.Where(IsShortcutDownWithItem) : null;

}
