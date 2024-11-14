using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class EquipmentPanel
    {
        private const string BackgroundName = "ExtraSlotsEquipmentPanel";

        private const float tileSpace = 6f;
        private const float tileSize = 64f + tileSpace;
        private const float interslotSpaceInTiles = 0.25f;
        private static int equipmentSlotsCount = 0;
        internal static int quickSlotsCount = 0;    

        private static float InventoryPanelWidth => InventoryGui.instance ? InventoryGui.instance.m_player.rect.width : 0;
        private static float PanelWidth => (Math.Max(quickSlotsCount, SlotPositions.LastEquipmentColumn() + 1) + FoodAmmoSlotsWidthInTiles) * tileSize + tileSpace / 2;
        private static float PanelHeight => (((quickSlotsCount > 0 || IsFirstMiscSlotAvailable()) ? 1f + interslotSpaceInTiles : 0f) + EquipmentHeight) * tileSize + tileSpace / 2;
        private static Vector2 PanelOffset => new Vector2(equipmentPanelOffset.Value.x, -equipmentPanelOffset.Value.y);
        private static Vector2 PanelPosition => new Vector2(InventoryPanelWidth + 100f, 0f) + PanelOffset;
        private static float FoodAmmoSlotsWidthInTiles => (IsFoodSlotAvailable() || IsAmmoSlotAvailable() ? interslotSpaceInTiles : 0) + (IsFoodSlotAvailable() ? 1f : 0) + (IsAmmoSlotAvailable() ? 1f : 0);
        private static int EquipmentHeight => equipmentSlotsCount > 3 || IsFoodSlotAvailable() || IsAmmoSlotAvailable() ? 3 : equipmentSlotsCount;

        public static RectTransform inventoryDarken = null;
        public static RectTransform inventoryBackground = null;
        public static Image inventoryBackgroundImage = null;
        public static RectTransform equipmentBackground = null;
        public static Image equipmentBackgroundImage = null;
        public static RectTransform selectedFrame = null;
        public static RectTransform inventorySelectedFrame = null;

        private static bool isDirty = true;

        private static Color normalColor = Color.clear;
        private static Color highlightedColor = Color.clear;
        private static Color normalColorUnfit = Color.clear;
        private static Color highlightedColorUnfit = Color.clear;

        private static Material iconMaterial;
        private static Vector3 originalScale = Vector3.zero;

        private static Vector2 originalTooltipPosition = Vector2.zero;

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
            if (originalScale == Vector3.zero && InventoryGui.instance.m_playerGrid.m_elements.Count > 0 
                                              && InventoryGui.instance.m_playerGrid.m_elements[0].m_icon.material != null 
                                              && InventoryGui.instance.m_playerGrid.m_elements[0].m_icon.transform.localScale != Vector3.one)
                originalScale = InventoryGui.instance.m_playerGrid.m_elements[0].m_icon.transform.localScale;

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

            bool regularInventoryUnfitsForDragItem = InventoryGui.instance.m_dragItem != null && CurrentPlayer.IsItemEquiped(InventoryGui.instance.m_dragItem);
            for (int i = 0; i < Math.Min(InventoryGui.instance.m_playerGrid.m_elements.Count, startIndex); i++)
                SetSlotColor(InventoryGui.instance.m_playerGrid.m_elements[i]?.m_go?.GetComponent<Button>(), regularInventoryUnfitsForDragItem);

            if (originalTooltipPosition == Vector2.zero)
                originalTooltipPosition = InventoryGui.instance.m_playerGrid.m_tooltipAnchor.anchoredPosition;

            if (equipmentPanelTooltipOffset.Value == Vector2.zero)
                InventoryGui.instance.m_playerGrid.m_tooltipAnchor.anchoredPosition = originalTooltipPosition - new Vector2(0, PanelHeight + 3.5f * tileSpace);
            else
                InventoryGui.instance.m_playerGrid.m_tooltipAnchor.anchoredPosition = originalTooltipPosition + new Vector2(equipmentPanelTooltipOffset.Value.x, -equipmentPanelTooltipOffset.Value.y);

            isDirty = false;
        }

        public static void UpdateBackground()
        {
            if (!equipmentBackground)
                return;

            equipmentBackground.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            equipmentBackground.anchoredPosition = PanelPosition + new Vector2(PanelWidth / 2, -PanelHeight / 2);

            selectedFrame.sizeDelta = equipmentBackground.sizeDelta + Vector2.one * 26f;
            selectedFrame.anchoredPosition = equipmentBackground.anchoredPosition;
        }

        internal static void SetSlotElement(InventoryGrid.Element element, Slot slot)
        {
            GameObject currentChild = element?.m_go;
            if (!currentChild)
                return;

            currentChild.gameObject.SetActive(slot.IsActive);
            currentChild.GetComponent<RectTransform>().anchoredPosition = slot.Position;
            SetSlotLabel(currentChild.transform.Find("binding"), slot);
            SetSlotColor(currentChild.GetComponent<Button>(), InventoryGui.instance.m_dragItem != null && 
                                                              slot.IsActive && 
                                                              (!slot.ItemFits(InventoryGui.instance.m_dragItem) || !slot.IsEquipmentSlot && CurrentPlayer.IsItemEquiped(InventoryGui.instance.m_dragItem)));
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
            textComp.text = hotbarElement ? (ZInput.IsGamepadActive() ? "" : slot.GetShortcutText()) : slot.Name;
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

        internal static void SetSlotColor(Button button, bool useUnfitColor)
        {
            if (!button)
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
            buttonColors.normalColor = useUnfitColor ? normalColorUnfit : normalColor;
            buttonColors.highlightedColor = useUnfitColor ? highlightedColorUnfit : highlightedColor;
            button.colors = buttonColors;
        }

        private static void SetSlotBackgroundImage(InventoryGrid.Element element, Slot slot)
        {
            if (iconMaterial == null && element.m_icon.material != null)
                iconMaterial = element.m_icon.material;

            if (element.m_icon.material == null)
                element.m_icon.material = iconMaterial;

            bool freeSlot = slot.IsFree;
            if (!freeSlot)
            {
                ItemDrop.ItemData item = slot.Item;
                element.m_tooltip.Set(item.m_shared.m_name, item.GetTooltip(), InventoryGui.instance.m_playerGrid.m_tooltipAnchor); // Fix possible tooltip lose
                element.m_icon.transform.localScale = originalScale == Vector3.zero ? Vector3.one: originalScale;

                if (isEpicLootEnabled && epicLootMagicItemUnequippedAlpha.Value != 1f && !item.m_equipped && element.m_go.transform.Find("magicItem") is Transform magicItem && magicItem.GetComponent<Image>() is Image magicItemImage)
                    magicItemImage.color = new Color(magicItemImage.color.r, magicItemImage.color.g, magicItemImage.color.b, epicLootMagicItemUnequippedAlpha.Value);
                return;
            }

            if (slot.IsEquipmentSlot)
            {
                if (equipmentSlotsShowTooltip.Value)
                    element.m_tooltip.Set("$exsl_slot_equipment", "$exsl_slot_equipment_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
            else if (slot.IsAmmoSlot)
            {
                element.m_icon.enabled = ammoSlotsShowHintImage.Value;
                element.m_icon.material = null;
                element.m_icon.sprite = ammoSlot;
                element.m_icon.transform.localScale = Vector3.one * 0.8f;
                element.m_icon.color = Color.grey - new Color(0f, 0f, 0f, 0.1f);
                if (ammoSlotsShowTooltip.Value)
                    element.m_tooltip.Set($"$exsl_slot_ammo ({slot.GetShortcutText()})", "$exsl_slot_ammo_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
            else if (slot.IsQuickSlot)
            {
                element.m_icon.enabled = quickSlotsShowHintImage.Value;
                element.m_icon.material = null;
                element.m_icon.sprite = quickSlot;
                element.m_icon.transform.localScale = Vector3.one * 0.6f;
                element.m_icon.color = Color.grey - new Color(0f, 0f, 0f, 0.6f);
                if (quickSlotsShowTooltip.Value)
                    element.m_tooltip.Set($"$exsl_slot_quick ({slot.GetShortcutText()})", "$exsl_slot_quick_desc", InventoryGui.instance.m_playerGrid.m_tooltipAnchor);
            }
            else if (slot.IsMiscSlot)
            {
                element.m_icon.enabled = miscSlotsShowHintImage.Value;
                element.m_icon.material = null;
                element.m_icon.sprite = miscSlot;
                element.m_icon.transform.localScale = Vector3.one * 0.8f;
                element.m_icon.color = Color.grey - new Color(0f, 0f, 0f, 0.75f);
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
                int y = EquipmentHeight * 4 + 1;
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
                int x = Math.Max(LastEquipmentColumn() + 1, quickSlotsCount) * 4 + 1 + (IsFoodSlotAvailable() ? 4 : 0);
                int y = i * 4;
                return GetSlotPosition(x, y);
            }
            internal static Vector2 GetMiscSlotTileOffset(int i)
            {
                int x = Math.Max(LastEquipmentColumn() + 1, quickSlotsCount) * 4 + i * 4 + 1;
                int y = EquipmentHeight * 4 + 1;
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

            if (!equipmentBackground)
            {
                Transform selected_frames = InventoryGui.instance.m_player.GetComponent<UIGroupHandler>()?.m_enableWhenActiveAndGamepad.transform;
                inventoryDarken = InventoryGui.instance.m_player.Find("Darken").GetComponent<RectTransform>();

                equipmentBackground = new GameObject(BackgroundName, typeof(RectTransform)).GetComponent<RectTransform>();
                equipmentBackground.gameObject.layer = inventoryBackground.gameObject.layer;
                equipmentBackground.SetParent(InventoryGui.instance.m_player, worldPositionStays: false);
                equipmentBackground.SetSiblingIndex(1 + (selected_frames == null ? inventoryDarken.GetSiblingIndex(): selected_frames.GetSiblingIndex())); // In front of Darken and selected_frame elements
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

                inventorySelectedFrame = selected_frames.GetChild(0) as RectTransform;
                selectedFrame = UnityEngine.Object.Instantiate(inventorySelectedFrame, selected_frames);
                selectedFrame.name = "selected (ExtraSlots)";

                selectedFrame.offsetMin = equipmentBackground.offsetMin;
                selectedFrame.offsetMax = equipmentBackground.offsetMax;
                selectedFrame.sizeDelta = equipmentBackground.sizeDelta;
                selectedFrame.anchoredPosition = equipmentBackground.anchoredPosition;
                selectedFrame.anchorMin = equipmentBackground.anchorMin;
                selectedFrame.anchorMax = equipmentBackground.anchorMax;

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

            inventoryBackground.anchorMin = new Vector2(0.0f, -1f * ((float)ExtraRowsPlayer / vanillaInventoryHeight - 0.01f * Math.Max(ExtraRowsPlayer - 1, 0)));
            inventorySelectedFrame.anchorMin = inventoryBackground.anchorMin;

            if (fixContainerPosition.Value)
                InventoryGui.instance.m_container.pivot = new Vector2(0f, 1f + ExtraRowsPlayer * 0.2f);
        }

        internal static void ClearPanel()
        {
            inventoryDarken = null;
            inventoryBackground = null;
            equipmentBackground = null;
            equipmentBackgroundImage = null;
            inventoryBackgroundImage = null;

            selectedFrame = null;
            inventorySelectedFrame = null;

            normalColor = Color.clear;
            highlightedColor = Color.clear;
            normalColorUnfit = Color.clear;
            highlightedColorUnfit = Color.clear;

            iconMaterial = null;
            originalScale = Vector3.zero;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        static class InventoryGui_Show_UpdatePanel
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

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        internal static class InventoryGrid_UpdateGui_UpdateSlotsOnDirty
        {
            private static void Prefix(InventoryGrid __instance, ref int __state)
            {
                if (__instance != InventoryGui.instance.m_playerGrid)
                    return;

                __state = __instance.m_elements.Count;
            }

            private static void Postfix(InventoryGrid __instance, int __state)
            {
                if (__instance != InventoryGui.instance.m_playerGrid)
                    return;

                if (__state != __instance.m_elements.Count)
                    MarkDirty();

                UpdateInventorySlots();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupDragItem))]
        private static class InventoryGui_SetupDragItem_UpdateSlotsOnItemDrag
        {
            private static void Postfix() => MarkDirty();
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGamepad))]
        public static class InventoryGrid_UpdateGamepad_GamepadSupport
        {
            public static Vector2i FindEquipmentSlot(int row = -1, Slot slotRow = null, int col = -1, Slot slotCol = null, bool right = false, Slot before = null, Slot after = null)
            {
                Slot[] equipmentSlots = GetEquipmentSlots();

                if (row == -1 && slotRow != null)
                    row = Array.IndexOf(equipmentSlots, slotRow) % 3;

                if (col == -1 && slotCol != null)
                    col = Array.IndexOf(equipmentSlots, slotCol) / 3;

                int beforeIndex = before == null ? -1 : Array.IndexOf(equipmentSlots, before);
                int afterIndex  = after  == null ? -1 : Array.IndexOf(equipmentSlots, after);

                int i = right ? equipmentSlots.Length : -1;

                while (true)
                {
                    if (right) i--; else i++;

                    if (i < 0 || i == equipmentSlots.Length)
                        break;

                    if (beforeIndex > -1 && i >= beforeIndex || afterIndex > -1 && i <= afterIndex)
                        continue;

                    if (row == -1 && col == -1)
                        return equipmentSlots[i].GridPosition;

                    if ((row != -1) && (row == i % 3) || (col != -1) && (col == i / 3))
                        return equipmentSlots[i].GridPosition;
                }

                return emptyPosition;
            }

            private static bool Prefix(InventoryGrid __instance)
            {
                if (__instance != InventoryGui.instance.m_playerGrid || !__instance.m_uiGroup.IsActive || Console.IsVisible())
                    return true;

                /* Extra slots inventory grid looks like this

                QQQQQQMM
                AAAFFFUU
                VVVVVUUC
                CCCCCCCC

                Extra slots equipment layout (maxed slots variant)

                VVUCCC FA
                VVUCCC FA
                VUUCCC FA
                QQQQQQ MM

                Q - quickslot, M - misc slot
                A - ammo slot, F - food slot, U - extra utility slot
                V - vanilla equipment slot, C - custom slot added via API

                Misc slot depends on both ammo or food AND quick slot. 
                No quick slots == No misc slots
                No ammo slots  == No misc slot under ammo slots
                No food slots  == No misc slot under food slots */

                Slot slot = GetSlotInGrid(__instance.m_selected);

                if (ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyLStickLeft"))
                {
                    LogDebug($"From {__instance.m_selected} {slot} left");

                    if (slot != null)
                    {
                        if (slot.IsAmmoSlot)
                        {
                            // Food slots is next 3 slots to ammo slots in grid
                            __instance.m_selected.x += 3;
                            if (GetSlotInGrid(__instance.m_selected) is not Slot leftSlot || !leftSlot.IsActive)
                            {
                                int slotRow = __instance.m_selected.x % 3;
                                __instance.m_selected = FindEquipmentSlot(row: slotRow, right: true);
                                if (__instance.m_selected == emptyPosition)
                                    __instance.m_selected = new Vector2i(InventoryWidth - 1, slotRow);
                            }
                        }
                        else if (slot.IsFoodSlot)
                        {
                            int slotRow = __instance.m_selected.x % 3;

                            __instance.m_selected = FindEquipmentSlot(row: slotRow, right: true);

                            if (__instance.m_selected == emptyPosition)
                                __instance.m_selected = new Vector2i(InventoryWidth - 1, slotRow);
                        }
                        else if (slot.IsQuickSlot)
                        {
                            __instance.m_selected.x--;
                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = new Vector2i(InventoryWidth - 1, 3);
                        }
                        else if (slot.IsMiscSlot)
                        {
                            __instance.m_selected.x--;
                            if (GetSlotInGrid(__instance.m_selected) is not Slot leftToMiscSlot)
                                __instance.m_selected = new Vector2i(InventoryWidth - 1, 3);
                            else
                            {
                                if (!leftToMiscSlot.IsActive)
                                {
                                    if (leftToMiscSlot.IsQuickSlot)
                                    {
                                        IEnumerable<Slot> quickSlots = GetQuickSlots().Where(slot => slot.IsActive);
                                        if (quickSlots.Any())
                                            __instance.m_selected = quickSlots.Last().GridPosition;
                                        else
                                            __instance.m_selected = new Vector2i(InventoryWidth - 1, 3);
                                    }
                                    else if (leftToMiscSlot.IsMiscSlot && quickSlotsCount == 0)
                                    {
                                        __instance.m_selected = new Vector2i(InventoryWidth - 1, 3);
                                    }
                                    else
                                    {
                                        __instance.m_selected = new Vector2i(InventoryWidth - 1, 3);
                                    }
                                }
                            }
                        }
                        else // Equipment slot
                        {
                            __instance.m_selected = FindEquipmentSlot(slotRow: slot, right: true, before: slot);
                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = new Vector2i(InventoryWidth - 1, Array.IndexOf(GetEquipmentSlots(), slot) % 3);
                        }
                           
                        return false;
                    }
                }

                if (ZInput.GetButtonDown("JoyDPadRight") || ZInput.GetButtonDown("JoyLStickRight"))
                {
                    LogDebug($"From {__instance.m_selected} {slot} right");

                    if (slot == null)
                    {
                        if (__instance.m_selected.x >= InventoryWidth - 1)
                        {
                            if (__instance.m_selected.y > 2)
                            {
                                IEnumerable<Slot> quickSlots = GetQuickSlots().Where(slot => slot.IsActive);
                                if (quickSlots.Any())
                                {
                                    __instance.m_selected = quickSlots.First().GridPosition;
                                    return false;
                                }
                                else
                                {
                                    IEnumerable<Slot> miscSlots = GetMiscSlots().Where(slot => slot.IsActive);
                                    if (miscSlots.Any())
                                    {
                                        __instance.m_selected = miscSlots.First().GridPosition;
                                        return false;
                                    }
                                }
                                __instance.m_selected = new Vector2i(0, InventoryHeightPlayer);
                            }
                            else
                            {
                                Vector2i newPos = FindEquipmentSlot(row: Math.Min(__instance.m_selected.y, equipmentSlotsCount - 1));
                                if (newPos != emptyPosition)
                                    __instance.m_selected = newPos;
                            }
                            return false;
                        }
                    }
                    else
                    {
                        if (slot.IsFoodSlot)
                        {
                            if (GetSlotInGrid(new Vector2i(__instance.m_selected.x - 3, __instance.m_selected.y)) is Slot rightSlot && rightSlot.IsActive)
                                __instance.m_selected = rightSlot.GridPosition;
                        }
                        else if (slot.IsAmmoSlot)
                        {
                            return false;
                        }
                        else if (slot.IsMiscSlot)
                        {
                            if (GetSlotInGrid(new Vector2i(__instance.m_selected.x + 1, __instance.m_selected.y)) is Slot rightSlot && rightSlot.IsActive)
                                __instance.m_selected = rightSlot.GridPosition;
                        }
                        else if (slot.IsQuickSlot)
                        {
                            if (__instance.m_selected.x < quickSlotsCount - 1)
                            {
                                if (GetSlotInGrid(new Vector2i(__instance.m_selected.x + 1, __instance.m_selected.y)) is Slot rightSlot && rightSlot.IsActive)
                                    __instance.m_selected = rightSlot.GridPosition;
                            }
                            else
                            {
                                IEnumerable<Slot> miscSlots = GetMiscSlots().Where(slot => slot.IsActive);
                                if (miscSlots.Any())
                                    __instance.m_selected = miscSlots.First().GridPosition;
                            }
                        }
                        else // Equipment slot
                        {
                            __instance.m_selected = FindEquipmentSlot(slotRow: slot, after: slot);
                            if (__instance.m_selected.x < 0)
                            {
                                Slot[] foodSlots = GetFoodSlots().Where(slot => slot.IsActive).ToArray();
                                if (foodSlots.Length == 3)
                                {
                                    __instance.m_selected = foodSlots[Array.IndexOf(GetEquipmentSlots(), slot) % 3].GridPosition;
                                    return false;
                                }
                                else
                                {
                                    Slot[] ammoSlots = GetAmmoSlots().Where(slot => slot.IsActive).ToArray();
                                    if (ammoSlots.Length == 3)
                                    {
                                        __instance.m_selected = ammoSlots[Array.IndexOf(GetEquipmentSlots(), slot) % 3].GridPosition;
                                        return false;
                                    }
                                }
                            }

                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = slot.GridPosition;
                        }

                        return false;
                    }
                }

                if (ZInput.GetButtonDown("JoyDPadUp") || ZInput.GetButtonDown("JoyLStickUp"))
                {
                    LogDebug($"From {__instance.m_selected} {slot} up");

                    if (slot != null)
                    {
                        if (slot.IsFoodSlot)
                        {
                            __instance.m_selected.x--;
                            if (__instance.m_selected.x < 3)
                                __instance.m_selected = slot.GridPosition;
                        }
                        else if (slot.IsAmmoSlot)
                        {
                            __instance.m_selected.x--;
                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = slot.GridPosition;
                        }
                        else if (slot.IsMiscSlot)
                        {
                            if (slot.Index == 6)
                                __instance.m_selected = (GetFoodSlots().Last() is Slot upMiscSlot && upMiscSlot.IsActive ? upMiscSlot : GetAmmoSlots().Last()).GridPosition;
                            else if (slot.Index == 7)
                                __instance.m_selected = GetAmmoSlots().Last().GridPosition;

                            if (GetSlotInGrid(__instance.m_selected) is not Slot upSlot || !upSlot.IsActive)
                                __instance.m_selected = slot.GridPosition;
                        }
                        else if (slot.IsQuickSlot)
                        {
                            __instance.m_selected = FindEquipmentSlot(col: Math.Min(__instance.m_selected.x, equipmentSlotsCount / 3), right: true);
                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = FindEquipmentSlot(right: true);

                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = slot.GridPosition;
                        }
                        else // Equipment slot
                        {
                            __instance.m_selected = FindEquipmentSlot(col: Array.IndexOf(GetEquipmentSlots(), slot) / 3, right: true, before: slot);
                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = slot.GridPosition;
                        }
                        return false;
                    }
                }

                if (ZInput.GetButtonDown("JoyDPadDown") || ZInput.GetButtonDown("JoyLStickDown"))
                {
                    LogDebug($"From {__instance.m_selected} {slot} down");

                    if (slot != null)
                    {
                        if (slot.IsQuickSlot || slot.IsMiscSlot)
                        {
                            if (!__instance.jumpToNextContainer)
                                return false;

                            __instance.OnMoveToLowerInventoryGrid?.Invoke(__instance.m_selected);
                            return false;
                        }
                        else if (slot.IsAmmoSlot)
                        {
                            __instance.m_selected.x++;
                            if (__instance.m_selected.x > 2)
                                __instance.m_selected = (GetMiscSlots()[1] is Slot downAmmoSlot && downAmmoSlot.IsActive ? downAmmoSlot : GetMiscSlots()[0]).GridPosition;
                        }
                        else if (slot.IsFoodSlot)
                        {
                            __instance.m_selected.x++;
                            if (__instance.m_selected.x > 5)
                                __instance.m_selected = GetMiscSlots()[0].GridPosition;
                        }
                        else if (slot.IsEquipmentSlot)
                        {
                            int col = Array.IndexOf(GetEquipmentSlots(), slot) / 3;
                            __instance.m_selected = FindEquipmentSlot(col: col, after: slot);
                            if (__instance.m_selected.x < 0)
                                __instance.m_selected = GetQuickSlots()[col].GridPosition;
                        }

                        if (GetSlotInGrid(__instance.m_selected) is not Slot downSlot || !downSlot.IsActive)
                        {
                            if (!__instance.jumpToNextContainer)
                                return false;

                            __instance.OnMoveToLowerInventoryGrid?.Invoke(__instance.m_selected);
                        }
                        return false;
                    }
                    else
                    {
                        if (__instance.m_selected.y >= InventoryHeightPlayer - 1)
                        {
                            __instance.OnMoveToLowerInventoryGrid?.Invoke(__instance.m_selected);
                            return false;
                        }
                    }
                }

                return true;
            }

            private static void Postfix(InventoryGrid __instance, bool __runOriginal)
            {
                if (!__runOriginal)
                    LogDebug($"Selected {__instance.m_selected}");
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.SetSelection))]
        public static class InventoryGrid_SetSelection_GamepadSupport
        {
            private static void Postfix(Vector2i pos)
            {
                pos.y = Math.Min(pos.y, InventoryHeightPlayer - 1);
                LogDebug($"SetSelection {pos}");
            }
        }
    }
}
