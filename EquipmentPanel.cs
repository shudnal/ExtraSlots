using HarmonyLib;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    internal class EquipmentPanel
    {
        private const string BackgroundName = "ExtraSlotsEquipmentPanel";

        private const float tileSpace = 6f;
        private const float tileSize = 64f + tileSpace;
        private const float interslotSpaceInTiles = 0.25f;
        private static int equipmentSlotsCount = 0;
        private static int quickSlotsCount = 0;

        private static float InventoryPanelWidth => InventoryGui.instance ? InventoryGui.instance.m_player.rect.width : 0;
        private static float PanelWidth => (Math.Max(quickSlotsCount, SlotPositions.LastEquipmentColumn() + 1) + FoodAmmoSlotsWidthInTiles) * tileSize + tileSpace / 2;
        private static float PanelHeight => (quickSlotsCount > 0 || miscSlotsEnabled.Value && (foodSlotsEnabled.Value || ammoSlotsEnabled.Value) ? 4f + interslotSpaceInTiles : 3f) * tileSize + tileSpace / 2;
        private static Vector2 PanelOffset => new Vector2(equipmentPanelOffset.Value.x, -equipmentPanelOffset.Value.y);
        private static Vector2 PanelPosition => new Vector2(InventoryPanelWidth + 100f, 0f) + PanelOffset;
        private static float FoodAmmoSlotsWidthInTiles => (foodSlotsEnabled.Value || ammoSlotsEnabled.Value ? interslotSpaceInTiles : 0) + (foodSlotsEnabled.Value ? 1f : 0) + (ammoSlotsEnabled.Value ? 1f : 0);

        public static RectTransform inventoryDarken = null;
        public static RectTransform inventoryBackground = null;
        public static Image inventoryBackgroundImage = null;
        public static RectTransform equipmentBackground = null;
        public static Image equipmentBackgroundImage = null;

        private static bool isDirty = true;

        private static Color normalColor = Color.clear;
        private static Color highlightedColor = Color.clear;
        private static Color normalColorUnfit = Color.clear;
        private static Color highlightedColorUnfit = Color.clear;

        private static Material iconMaterial;
        
        internal static Sprite ammoSlot;
        internal static Sprite miscSlot;
        internal static Sprite quickSlot;
        internal static Sprite background;

        public static void MarkDirty() => isDirty = true;

        internal static void UpdateSlotsCount()
        {
            equipmentSlotsCount = GetEquipmentSlotsCount();
            quickSlotsCount = GetQuickSlotsCount();
        }

        internal static void ReorderVanillaSlots()
        {
            string[] newSlotsOrder = vanillaSlotsOrder.Value.Split(',').Select(str => str.Trim()).Distinct().ToArray();
            Slot[] currentOrder = slots.Where(slot => slot != null && slot.IsVanillaEquipment()).ToArray();

            for (int i = 0; i < Mathf.Min(newSlotsOrder.Length, currentOrder.Length); i++)
            {
                int newSlotIndex = Array.FindIndex(slots, slot => slot.ID == newSlotsOrder[i]);
                if (newSlotIndex < 0)
                    continue;

                int currentSlotIndex = Array.IndexOf(slots, currentOrder[i]);
                if (currentSlotIndex < 0)
                    continue;

                SwapSlots(newSlotIndex, currentSlotIndex);
            }

            SetSlotsPositions();
        }

        internal static void UpdatePanel()
        {
            UpdateSlotsCount();
            UpdateBackground();
            SetSlotsPositions();
            MarkDirty();
        }

        // Runs every frame InventoryGui.UpdateInventory if visible
        internal static void UpdateInventorySlots()
        {
            int startIndex = InventorySizePlayer;
            for (int i = 0; i < Math.Min(slots.Length, InventoryGui.instance.m_playerGrid.m_elements.Count - startIndex); ++i)
                SetSlotBackgroundImage(InventoryGui.instance.m_playerGrid.m_elements[startIndex + i], slots[i]);

            if (!isDirty)
                return;

            if (PlayerInventory == null)
                return;

            if (!InventoryGui.instance.m_playerGrid)
                return;

            for (int i = 0; i < Math.Min(slots.Length, InventoryGui.instance.m_playerGrid.m_elements.Count - startIndex); ++i)
                SetSlotElement(InventoryGui.instance.m_playerGrid.m_elements[startIndex + i], slots[i]);

            for (int i = startIndex + slots.Length; i < InventoryGui.instance.m_playerGrid.m_elements.Count; i++)
                InventoryGui.instance.m_playerGrid.m_elements[i]?.m_go?.SetActive(false);

            isDirty = false;
        }

        public static void UpdateBackground()
        {
            if (!equipmentBackground)
                return;

            equipmentBackground.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            equipmentBackground.anchoredPosition = PanelPosition + new Vector2(PanelWidth / 2, -PanelHeight / 2);
        }

        internal static void SetSlotElement(InventoryGrid.Element element, Slot slot)
        {
            GameObject currentChild = element?.m_go;
            if (!currentChild)
                return;

            currentChild.gameObject.SetActive(slot.IsActive);
            currentChild.GetComponent<RectTransform>().anchoredPosition = slot.Position;
            SetSlotLabel(currentChild.transform.Find("binding"), slot);
            SetSlotColor(currentChild.GetComponent<Button>(), slot);
        }

        internal static void SetSlotLabel(Transform binding, Slot slot, bool hotbarElement = false)
        {
            if (!binding || !slot.IsActive)
                return;

            // Make component size of parent to let TMP_Text do its job on text positioning
            RectTransform rt = binding.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            TMP_Text textComp = binding.GetComponent<TMP_Text>();
            textComp.enableAutoSizing = true;
            textComp.text = hotbarElement ? slot.GetShortcutText() : slot.Name;
            textComp.enabled = true;
            textComp.overflowMode = TextOverflowModes.Overflow;
            textComp.fontSizeMin = slot.IsHotkeySlot ? (slot.IsAmmoSlot ? ammoSlotLabelFontSize.Value.x : quickSlotLabelFontSize.Value.x) : equipmentSlotLabelFontSize.Value.x;
            textComp.fontSizeMax = slot.IsHotkeySlot ? (slot.IsAmmoSlot ? ammoSlotLabelFontSize.Value.y : quickSlotLabelFontSize.Value.y) : equipmentSlotLabelFontSize.Value.y;
            textComp.color = slot.IsHotkeySlot ? (slot.IsAmmoSlot ? ammoSlotLabelFontColor.Value : quickSlotLabelFontColor.Value) : equipmentSlotLabelFontColor.Value;
            textComp.margin = slot.IsHotkeySlot ? (slot.IsAmmoSlot ? ammoSlotLabelMargin.Value : quickSlotLabelMargin.Value) : equipmentSlotLabelMargin.Value;
            textComp.textWrappingMode = slot.IsHotkeySlot ? (slot.IsAmmoSlot ? ammoSlotLabelWrappingMode.Value : quickSlotLabelWrappingMode.Value) : equipmentSlotLabelWrappingMode.Value;
            textComp.horizontalAlignment = slot.IsHotkeySlot ? (slot.IsAmmoSlot ? ammoSlotLabelAlignment.Value : quickSlotLabelAlignment.Value) : equipmentSlotLabelAlignment.Value;
            textComp.verticalAlignment = VerticalAlignmentOptions.Top;
        }

        internal static void SetSlotColor(Button button, Slot slot)
        {
            if (!button || !slot.IsActive || InventoryGui.instance == null)
                return;

            if (normalColor == Color.clear)
                normalColor = button.colors.normalColor;

            if (highlightedColor == Color.clear)
                highlightedColor = button.colors.highlightedColor;

            if (normalColorUnfit == Color.clear)
                normalColorUnfit = button.colors.normalColor + new Color(0.3f, 0f, 0f, 0.1f);

            if (highlightedColorUnfit == Color.clear)
                highlightedColorUnfit = button.colors.highlightedColor + new Color(0.3f, 0f, 0f, 0.1f);

            ColorBlock buttonColors = button.colors;
            buttonColors.normalColor = InventoryGui.instance.m_dragItem != null && !slot.ItemFit(InventoryGui.instance.m_dragItem) ? normalColorUnfit : normalColor;
            buttonColors.highlightedColor = InventoryGui.instance.m_dragItem != null && !slot.ItemFit(InventoryGui.instance.m_dragItem) ? highlightedColorUnfit : highlightedColor;
            button.colors = buttonColors;
        }

        private static void SetSlotBackgroundImage(InventoryGrid.Element element, Slot slot)
        {
            bool freeSlot = slot.IsFree;
            if (!freeSlot)
            {
                ItemDrop.ItemData item = slot.Item;
                element.m_tooltip.Set(item.m_shared.m_name, item.GetTooltip(), InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
                return;
            }

            Image bkgImage = element.m_icon;

            if (iconMaterial == null)
                iconMaterial = bkgImage.material;

            if (bkgImage.material == null)
                bkgImage.material = iconMaterial;

            if (slot.IsEquipmentSlot)
            {
                if (equipmentSlotsShowTooltip.Value)
                    element.m_tooltip.Set("$exsl_slot_equipment", "$exsl_slot_equipment_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
            else if (slot.IsAmmoSlot)
            {
                bkgImage.enabled = ammoSlotsShowHintImage.Value;
                bkgImage.material = null;
                bkgImage.sprite = ammoSlot;
                bkgImage.transform.localScale = Vector3.one * 0.8f;
                bkgImage.color = Color.grey - new Color(0f, 0f, 0f, 0.1f);
                if (ammoSlotsShowTooltip.Value)
                    element.m_tooltip.Set("$exsl_slot_ammo", "$exsl_slot_ammo_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
            else if (slot.IsQuickSlot)
            {
                bkgImage.enabled = quickSlotsShowHintImage.Value;
                bkgImage.material = null;
                bkgImage.sprite = quickSlot;
                bkgImage.transform.localScale = Vector3.one * 0.6f;
                bkgImage.color = Color.grey - new Color(0f, 0f, 0f, 0.6f);
                if (quickSlotsShowTooltip.Value)
                    element.m_tooltip.Set("$exsl_slot_quick", "$exsl_slot_quick_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
            else if (slot.IsMiscSlot)
            {
                bkgImage.enabled = miscSlotsShowHintImage.Value;
                bkgImage.material = null;
                bkgImage.sprite = miscSlot;
                bkgImage.transform.localScale = Vector3.one * 0.8f;
                bkgImage.color = Color.grey - new Color(0f, 0f, 0f, 0.75f);
                if (miscSlotsShowTooltip.Value)
                    element.m_tooltip.Set("$exsl_slot_misc", "$exsl_slot_misc_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
            else if (slot.IsFoodSlot && freeSlot)
            {
                element.m_food.enabled = foodSlotsShowHintImage.Value;
                element.m_food.color = Color.grey - new Color(0f, 0f, 0f, 0.5f);
                if (foodSlotsShowTooltip.Value)
                    element.m_tooltip.Set("$exsl_slot_food", "$exsl_slot_food_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
        }

        internal static void SetSlotsPositions()
        {
            SetPosition(GetEquipmentSlots(), SlotPositions.GetEquipmentTileOffset);
            SetPosition(GetQuickSlots(), SlotPositions.GetQuickSlotTileOffset);
            SetPosition(GetFoodSlots(), SlotPositions.GetFoodSlotTileOffset);
            SetPosition(GetAmmoSlots(), SlotPositions.GetAmmoSlotTileOffset);
            SetPosition(GetMiscSlots(), SlotPositions.GetMiscSlotTileOffset);

            static void SetPosition(Slot[] collection, Func<int, Vector2> offsetFunc)
            {
                for (int i = 0; i < collection.Length; i++)
                    collection[i].SetPosition(offsetFunc(i));
            }
        }

        private static class SlotPositions
        {
            private static int Column(int i) => i / 3;
            private static int Row(int i) => i % 3;
            private static int LastEquipmentRow() => Row(equipmentSlotsCount - 1);
            internal static int LastEquipmentColumn() => Column(equipmentSlotsCount - 1);

            // Result in grid size of half tiles
            internal static Vector2 GetEquipmentTileOffset(int i)
            {
                int x = Column(i) * 4
                    // Horizontal offset for rows with insuccifient columns
                    + (equipmentSlotsAlignment.Value == SlotsAlignment.VerticalTopHorizontalMiddle && Row(i) > LastEquipmentRow() ? 1 : 0) * 2
                    // Offset for equipment positioning in the middle if quickslots amount is more than equipment columns
                    + Math.Max(quickSlotsCount - 1 - LastEquipmentColumn(), 0) * 2;

                int y = Row(i) * 4
                    // Offset for last column and vertical alignment in the middle
                    + Math.Max(equipmentSlotsAlignment.Value == SlotsAlignment.VerticalMiddleHorizontalLeft && Column(i) == LastEquipmentColumn() ? 2 - LastEquipmentRow() : 0, 0) * 2;
                return GetSlotPosition(x, y);
            }
            internal static Vector2 GetQuickSlotTileOffset(int i)
            {
                int x = i * 4
                    // Offset for quickslots positioning in the middle if equipment columns is more than quickslots
                    + Math.Max(quickSlotsAlignmentCenter.Value ? LastEquipmentColumn() + 1 - quickSlotsCount : 0, 0);
                int y = 3 * 4 + 1;
                return GetSlotPosition(x, y);
            }
            internal static Vector2 GetFoodSlotTileOffset(int i)
            {
                int x = Math.Max(LastEquipmentColumn() + 1, quickSlotsCount) * 4 + 1;
                int y = i * 4;
                return GetSlotPosition(x, y);
            }
            internal static Vector2 GetAmmoSlotTileOffset(int i)
            {
                int x = Math.Max(LastEquipmentColumn() + 1, quickSlotsCount) * 4 + 1 + (foodSlotsEnabled.Value ? 4 : 0);
                int y = i * 4;
                return GetSlotPosition(x, y);
            }
            internal static Vector2 GetMiscSlotTileOffset(int i)
            {
                int x = Math.Max(LastEquipmentColumn() + 1, quickSlotsCount) * 4 + i * 4 + 1;
                int y = 3 * 4 + 1;
                return GetSlotPosition(x, y);
            }
            private static Vector2 GetSlotPosition(int x, int y) => PanelPosition + new Vector2(x * tileSize / 4, -y * tileSize / 4);
        }
        
        // Runs every frame InventoryGui.Update if visible
        internal static void UpdateEquipmentBackground()
        {
            if (!InventoryGui.instance)
                return;

            inventoryBackground ??= InventoryGui.instance.m_player.Find("Bkg").GetComponent<RectTransform>();
            if (!inventoryBackground)
                return;

            inventoryBackground.anchorMin = new Vector2(0.0f, extraRows.Value * -1f / vanillaInventoryHeight);

            if (!equipmentBackground)
            {
                inventoryDarken = InventoryGui.instance.m_player.Find("Darken").GetComponent<RectTransform>();

                equipmentBackground = new GameObject(BackgroundName, typeof(RectTransform)).GetComponent<RectTransform>();
                equipmentBackground.gameObject.layer = inventoryBackground.gameObject.layer;
                equipmentBackground.SetParent(InventoryGui.instance.m_player, worldPositionStays: false);
                equipmentBackground.SetSiblingIndex(inventoryDarken.GetSiblingIndex() + 1); // In front of Darken element
                equipmentBackground.offsetMin = Vector2.zero;
                equipmentBackground.offsetMax = Vector2.zero;
                equipmentBackground.sizeDelta = Vector2.zero;
                equipmentBackground.anchoredPosition = Vector2.zero;
                equipmentBackground.anchorMin = new Vector2(0f, 1f);
                equipmentBackground.anchorMax = new Vector2(0f, 1f);

                RectTransform equipmentDarken = UnityEngine.Object.Instantiate(inventoryDarken, equipmentBackground);
                equipmentDarken.name = "Darken";
                equipmentDarken.sizeDelta = Vector2.one * 70f; // Original 100 is too much

                Transform equipmentBkg = UnityEngine.Object.Instantiate(inventoryBackground.transform, equipmentBackground);
                equipmentBkg.name = "Bkg";

                equipmentBackgroundImage = equipmentBkg.GetComponent<Image>();
                inventoryBackgroundImage = inventoryBackground.transform.GetComponent<Image>();

                UpdateBackground();
            }

            if (equipmentBackgroundImage)
            {
                if (inventoryBackgroundImage)
                {
                    equipmentBackgroundImage.sprite = inventoryBackgroundImage.sprite;
                    equipmentBackgroundImage.overrideSprite = inventoryBackgroundImage.overrideSprite;
                    equipmentBackgroundImage.color = inventoryBackgroundImage.color;
                }
                
                if (background)
                {
                    equipmentBackgroundImage.sprite = background;
                    equipmentBackgroundImage.overrideSprite = background;
                }
            }
        }

        internal static void ClearPanel()
        {
            inventoryDarken = null;
            inventoryBackground = null;
            equipmentBackground = null;
            equipmentBackgroundImage = null;
            inventoryBackgroundImage = null;

            normalColor = Color.clear;
            highlightedColor = Color.clear;
            normalColorUnfit = Color.clear;
            highlightedColorUnfit = Color.clear;

            iconMaterial = null;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        static class InventoryGuiShowPatch
        {
            static void Postfix()
            {
                if (Player.m_localPlayer == null)
                    return;

                UpdatePanel();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnDestroy))]
        static class InventoryGui_OnDestroy_ClearObjects
        {
            static void Postfix() => ClearPanel();
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        private static class InventoryGui_Update_UpdateEquipmentPanel
        {
            private static void Postfix()
            {
                if (!Player.m_localPlayer)
                    return;

                if (!InventoryGui.IsVisible())
                    return;

                UpdateEquipmentBackground();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateInventory))]
        internal static class InventoryGui_UpdateInventory_UpdateSlotsOnDirty
        {
            private static void Prefix(InventoryGui __instance, ref int __state)
            {
                __state = __instance.m_playerGrid.m_elements.Count;
            }

            private static void Postfix(InventoryGui __instance, int __state)
            {
                if (__state != __instance.m_playerGrid.m_elements.Count)
                    MarkDirty();

                UpdateInventorySlots();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupDragItem))]
        private static class InventoryGui_SetupDragItem_UpdateSlotsOnItemDrag
        {
            private static void Postfix() => MarkDirty();
        }
    }
}
