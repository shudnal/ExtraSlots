using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class AmmoSlotsHotBar
    {
        public const string barName = "ExtraSlotsAmmoHotBar";
        public static bool isDirty = true;
        private static HotkeyBar hotBar = null;
        private static RectTransform hotBarRect = null; 
        private static Slot[] ammoSlots = new Slot[0];

        internal static void UpdateAmmoSlots() => ammoSlots = GetAmmoSlots();

        public static void GetItems(List<ItemDrop.ItemData> bound)
        {
            if (PlayerInventory == null)
                return;

            bound.AddRange(GetItems());
        }

        public static List<ItemDrop.ItemData> GetItems()
        {
            return ammoSlots.Where(slot => slot.IsActive).Select(slot => slot.Item).Where(item => item != null).ToList();
        }

        public static ItemDrop.ItemData GetItemInSlot(int slotIndex) => slots[slotIndex].Item;

        internal static void MarkDirty() => isDirty = true;

        internal static void CreateBar()
        {
            hotBarRect = UnityEngine.Object.Instantiate(Hud.instance.m_rootObject.transform.Find("HotKeyBar"), Hud.instance.m_rootObject.transform, true).GetComponent<RectTransform>();
            hotBarRect.name = barName;
            hotBarRect.localPosition = Vector3.zero;

            hotBar = hotBarRect.GetComponent<HotkeyBar>();
        }

        internal static void UpdateBar()
        {
            if (!isDirty)
                return;

            if (ammoSlotsHotBarEnabled.Value && hotBarRect == null)
                CreateBar();
            else if (!ammoSlotsHotBarEnabled.Value && hotBarRect != null)
                ClearBar();

            if (hotBar)
            {
                foreach (HotkeyBar.ElementData element in hotBar.m_elements)
                    UnityEngine.Object.Destroy(element.m_go);

                hotBar.m_elements.Clear();
            }

            HotkeyBarController.ClearBars();

            isDirty = false;
        }

        internal static void ClearBar()
        {
            if (hotBarRect != null)
                UnityEngine.Object.Destroy(hotBarRect);

            hotBar = null;
            hotBarRect = null;
        }

        // Runs every frame Player.Update
        internal static void UpdateItemUse()
        {
            if (!Player.m_localPlayer.TakeInput())
                return;

            if (ammoSlots.Length == 0)
                return;

            int hotkey = 0;
            while (!ammoSlots[hotkey].IsShortcutDown())
                if (++hotkey == ammoSlots.Length)
                    return;

            Player.m_localPlayer.UseItem(PlayerInventory, ammoSlots[hotkey].Item, fromInventoryGui: false);
        }

        // Runs every frame Hud.Update
        internal static void UpdatePosition()
        {
            if (!hotBar)
                return;

            hotBarRect.anchoredPosition = new Vector2(ammoSlotsHotBarOffset.Value.x, -ammoSlotsHotBarOffset.Value.y);
            hotBarRect.localScale = Vector3.one * ammoSlotsHotBarScale.Value;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update_UpdateammoSlotsUse
        {
            private static void Postfix(Player __instance)
            {
                if (!IsValidPlayer(__instance))
                    return;

                UpdateItemUse();
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
        private static class Hud_Update_UpdateammoSlotsPosition
        {
            private static void Postfix() => UpdatePosition();
        }
    }
}
