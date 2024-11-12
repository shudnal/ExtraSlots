using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class QuickSlotsHotBar
    {
        public const string barName = "ExtraSlotsQuickSlotsHotBar";
        public static bool isDirty = true;
        private static HotkeyBar hotBar = null;
        private static RectTransform hotBarRect = null;
        private static Slot[] hotBarSlots = Array.Empty<Slot>();

        internal static void UpdateSlots() => hotBarSlots = GetQuickSlots();

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

        public static ItemDrop.ItemData GetItemInSlot(int slotIndex) => slots[slotIndex].Item;

        internal static void MarkDirty() => isDirty = true;

        internal static void CreateBar()
        {
            hotBarRect = UnityEngine.Object.Instantiate(Hud.instance.m_rootObject.transform.Find("HotKeyBar"), Hud.instance.m_rootObject.transform, true).GetComponent<RectTransform>();
            hotBarRect.name = barName;
            hotBarRect.localPosition = Vector3.zero;

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

        internal static bool Refresh()
        {
            if (!isDirty)
                return false;

            if (quickSlotsHotBarEnabled.Value && hotBarRect == null)
                CreateBar();
            else if (!quickSlotsHotBarEnabled.Value && hotBarRect != null)
                ClearBar();

            isDirty = false;

            return true;
        }

        internal static void ClearBar()
        {
            if (hotBarRect != null)
                UnityEngine.Object.DestroyImmediate(hotBarRect.gameObject);

            hotBar = null;
            hotBarRect = null;
        }

        // Runs every frame Player.Update
        internal static void UpdateItemUse()
        {
            if (!Player.m_localPlayer.TakeInput())
                return;

            if (hotBarSlots.Length == 0)
                return;

            hotBarSlots.DoIf(slot => slot.IsShortcutDown(), slot => Player.m_localPlayer.UseItem(PlayerInventory, slot.Item, fromInventoryGui: false));
        }

        // Runs every frame Hud.Update
        internal static void UpdatePosition()
        {
            if (!hotBar)
                return;

            hotBarRect.anchoredPosition = new Vector2(quickSlotsHotBarOffset.Value.x, -quickSlotsHotBarOffset.Value.y);
            hotBarRect.localScale = Vector3.one * quickSlotsHotBarScale.Value;
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

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        private static class Hud_Update_SlotsPosition
        {
            private static void Postfix() => UpdatePosition();
        }
    }
}
