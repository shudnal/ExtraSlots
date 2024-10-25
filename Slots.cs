using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    public static class Slots
    {
        public const string helmetSlotID = "Helmet";
        public const string legsSlotID = "Legs";
        public const string utilitySlotID = "Utility";
        public const string chestSlotID = "Chest";
        public const string shoulderSlotID = "Shoulder";
        public const string extraUtilitySlotID = "ExtraUtility";
        public const string quickSlotID = "HotBar";
        public const string foodSlotID = "Food";
        public const string ammoSlotID = "Ammo";
        public const string miscSlotID = "Misc";
        public const string emptySlotID = "Empty";

        public static readonly Vector2i emptyPosition = new Vector2i(-1, -1);

        public static readonly string VanillaOrder = $"{helmetSlotID},{chestSlotID},{legsSlotID},{shoulderSlotID},{utilitySlotID}";

        private const string customKeyPlayerID = "ExtraSlotsEquippedBy";
        private const string customKeySlotID = "ExtraSlotsEquippedSlot";

        public class Slot
        {
            private readonly string _id;
            private readonly Func<string> _getName;
            private readonly Func<KeyboardShortcut> _getShortcut;
            private readonly Func<string> _getShortcutText;
            private readonly Func<ItemDrop.ItemData, bool> _itemIsValid;
            private readonly Func<bool> _isActive;

            private int _index = -1;
            private Vector2 _position;
            private Vector2i _gridPos = emptyPosition;

            public string ID => _id;

            public string Name => _getName != null ? Localization.instance.Localize(_getName()) : "";

            public bool IsActive => _isActive == null || _isActive();

            public Vector2 Position => _position;

            public Vector2i GridPosition => _gridPos;

            public bool IsHotkeySlot => _getShortcut != null;
            public bool IsEquipmentSlot => _index > 13;
            public bool IsQuickSlot => _index < 6;
            public bool IsMiscSlot => 6 <= _index && _index <= 7;
            public bool IsAmmoSlot => 8 <= _index && _index <= 10;
            public bool IsFoodSlot => 11 <= _index && _index <= 13;
            public bool IsCustomSlot => _index > 20;

            internal void SetPosition(Vector2 newPosition)
            {
                if (_position != (_position = newPosition))
                    EquipmentPanel.MarkDirty();
            }

            internal void UpdateGridPosition()
            {
                ItemDrop.ItemData item = Item;
                _gridPos = new Vector2i(_index % InventoryWidth, _index / InventoryWidth + InventoryHeightPlayer);
                if (item != null)
                    item.m_gridPos = _gridPos;
            }

            internal void SwapIndexWith(Slot slot)
            {
                (_index, slot._index) = (slot._index, _index);
                UpdateGridPosition();
                slot.UpdateGridPosition();
            }

            public bool IsVanillaEquipment() => _id == helmetSlotID
                                        || _id == legsSlotID
                                        || _id == utilitySlotID
                                        || _id == chestSlotID
                                        || _id == shoulderSlotID;

            public bool IsShortcutDown() => IsActive && _getShortcut != null && Player.m_localPlayer != null && Player.m_localPlayer.TakeInput() && IsShortcutDown(_getShortcut());

            public KeyboardShortcut GetShortcut() => _getShortcut == null ? KeyboardShortcut.Empty : _getShortcut();
            public string GetShortcutText() => _getShortcutText == null ? Name : Localization.instance.Localize(_getShortcutText());

            public ItemDrop.ItemData Item
            {
                get
                {
                    if (PlayerInventory == null || _gridPos == emptyPosition)
                        return null;

                    if (cachedItems.TryGetValue(_gridPos, out ItemDrop.ItemData item))
                        return item;

                    // Cache will be clear on inventory change
                    item = PlayerInventory.GetItemAt(_gridPos.x, _gridPos.y);
                    cachedItems[_gridPos] = item;
                    return item;
                }
            }

            public bool IsFree => Item == null;

            public bool ItemFit(ItemDrop.ItemData item) => item != null && IsActive && (_itemIsValid == null || _itemIsValid(item));

            public bool IsFreeQuickSlot() => IsQuickSlot && IsActive && IsFree;

            public Slot(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
            {
                _id = slotID;
                _index = slotIndex;
                _getName = getName;
                _itemIsValid = itemIsValid;
                _isActive = isActive;
            }

            public Slot(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive, Func<KeyboardShortcut> getShortcut, Func<string> getShortcutText)
            {
                _id = slotID;
                _index = slotIndex;
                _getName = getName;
                _itemIsValid = itemIsValid;
                _getShortcut = getShortcut;
                _getShortcutText = getShortcutText;
                _isActive = isActive;
            }

            public override string ToString() => (Name == "" ? ID : Name) + (IsActive ? "" : " (inactive)");

            private static bool IsShortcutDown(KeyboardShortcut shortcut) => shortcut.MainKey != KeyCode.None && ZInput.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(key => ZInput.GetKey(key));
        }

        public class CustomSlot : Slot
        {
            public CustomSlot(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive) : base(slotID, slotIndex + 20, getName, itemIsValid, isActive)
            {
            }
        }

        public static readonly Slot[] slots = new Slot[36];
        public static readonly Dictionary<Vector2i, ItemDrop.ItemData> cachedItems = new Dictionary<Vector2i, ItemDrop.ItemData>();
        public const int vanillaInventoryHeight = 4;

        public static PlayerProfile PlayerProfile => Game.instance?.GetPlayerProfile() ?? FejdStartup.instance?.m_profiles[FejdStartup.instance.m_profileIndex];
        public static Player CurrentPlayer => Player.m_localPlayer ?? EquipmentAndQuickSlotsCompat.playerToLoad;
        public static Inventory PlayerInventory => CurrentPlayer?.GetInventory();
        public static int InventoryWidth => PlayerInventory != null ? PlayerInventory.GetWidth() : 8;
        public static int InventoryHeightPlayer => vanillaInventoryHeight + extraRows.Value;
        public static int InventoryHeightFull => InventoryHeightPlayer + GetTargetInventoryHeight(slots.Length, InventoryWidth);
        public static int InventorySizePlayer => InventoryHeightPlayer * InventoryWidth;
        public static int InventorySizeFull => InventoryHeightFull * InventoryWidth;
        public static int InventorySizeActive => InventorySizePlayer + slots.Count(slot => slot.IsActive);

        public static int GetTargetInventoryHeight(int inventorySize, int inventoryWidth) => GetExtraRowsForItemsToFit(inventorySize, inventoryWidth);
        public static int GetExtraRowsForItemsToFit(int itemsAmount, int rowWidth) => ((itemsAmount - 1) / rowWidth) + 1;
        public static bool IsValidPlayer(Humanoid human) => human != null && human.IsPlayer() && Player.m_localPlayer == human && human.m_nview.IsValid() && human.m_nview.IsOwner();

        public static int GetEquipmentSlotsCount() => slots.Count(slot => slot.IsEquipmentSlot && slot.IsActive);
        public static int GetQuickSlotsCount() => slots.Count(slot => slot.IsQuickSlot && slot.IsActive);

        public static Slot[] GetEquipmentSlots()
        {
            List<Slot> equipment = new List<Slot>();
            equipment.AddRange(Array.FindAll(slots, slot => slot.IsActive && slot.IsVanillaEquipment()));
            equipment.AddRange(Array.FindAll(slots, slot => slot.IsActive && slot.ID.StartsWith(extraUtilitySlotID)));
            equipment.AddRange(Array.FindAll(slots, slot => slot.IsActive && slot.IsCustomSlot));

            return equipment.ToArray();
        }
        public static Slot[] GetQuickSlots() => Array.FindAll(slots, slot => slot.IsQuickSlot);
        public static Slot[] GetFoodSlots() => Array.FindAll(slots, slot => slot.IsFoodSlot);
        public static Slot[] GetAmmoSlots() => Array.FindAll(slots, slot => slot.IsAmmoSlot);
        public static Slot[] GetMiscSlots() => Array.FindAll(slots, slot => slot.IsMiscSlot);

        public static bool TryFindFreeSlotForItem(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item == null)
                return false;

            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerID) && item.m_customData.TryGetValue(customKeySlotID, out string slotID) && playerID == PlayerProfile?.GetPlayerID().ToString())
            {
                int prevSlotIndex = Array.FindIndex(slots, slot => slot.ID == slotID);
                if (prevSlotIndex > -1)
                {
                    Slot prevSlot = slots[prevSlotIndex];
                    if (prevSlot.IsActive && prevSlot.ItemFit(item) && prevSlot.IsFree)
                    {
                        slot = prevSlot;
                        return true;
                    }
                }
            }

            int index = Array.FindIndex(slots, slot => slot.IsActive && slot.IsFree && slot.ItemFit(item));
            if (index == -1)
                return false;

            slot = slots[index];
            return true;
        }

        public static bool TryFindFreeEquipmentSlotForItem(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item == null)
                return false;

            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerID) && item.m_customData.TryGetValue(customKeySlotID, out string slotID) && playerID == PlayerProfile?.GetPlayerID().ToString())
            {
                int prevSlotIndex = Array.FindIndex(slots, slot => slot.ID == slotID);
                if (prevSlotIndex > -1)
                {
                    Slot prevSlot = slots[prevSlotIndex];
                    if (prevSlot.IsActive && prevSlot.IsEquipmentSlot && prevSlot.ItemFit(item) && prevSlot.IsFree)
                    {
                        slot = prevSlot;
                        return true;
                    }
                }
            }

            slot = GetEquipmentSlots().FirstOrDefault(slot => slot.ItemFit(item) && slot.IsFree);
            return slot != null;
        }

        public static bool TryFindFirstUnequippedSlotForItem(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item == null)
                return false;

            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerID) && item.m_customData.TryGetValue(customKeySlotID, out string slotID) && playerID == PlayerProfile?.GetPlayerID().ToString())
            {
                int prevSlotIndex = Array.FindIndex(slots, slot => slot.ID == slotID);
                if (prevSlotIndex > -1)
                {
                    Slot prevSlot = slots[prevSlotIndex];
                    if (prevSlot.IsActive && prevSlot.IsEquipmentSlot && prevSlot.ItemFit(item) && !CurrentPlayer.IsItemEquiped(prevSlot.Item))
                    {
                        slot = prevSlot;
                        return true;
                    }
                }
            }

            slot = GetEquipmentSlots().FirstOrDefault(slot => slot.ItemFit(item) && !CurrentPlayer.IsItemEquiped(slot.Item));
            return slot != null;
        }

        public static bool HaveEmptyQuickSlot() => slots.Any(slot => slot.IsFreeQuickSlot());

        public static int GetEmptyQuickSlots() => slots.Count(slot => slot.IsFreeQuickSlot());

        public static Vector2i FindEmptyQuickSlot() => TryFindEmptyQuickSlot(out Slot slot) ? slot.GridPosition : emptyPosition;

        private static bool TryFindEmptyQuickSlot(out Slot slot)
        {
            slot = slots.FirstOrDefault(slot => slot.IsFreeQuickSlot());
            return slot != null;
        }

        internal static void SaveLastEquippedSlotsToItems()
        {
            long playerID = Game.instance.GetPlayerProfile().GetPlayerID();

            foreach (Slot slot in slots)
            {
                ItemDrop.ItemData item = slot.Item;
                if (item != null)
                {
                    item.m_customData[customKeyPlayerID] = playerID.ToString();
                    item.m_customData[customKeySlotID] = slot.ID;
                }
            }
        }

        internal static void PruneLastEquippedSlotFromItem(ItemDrop.ItemData item)
        {
            if (item == null)
                return;

            item.m_customData.Remove(customKeyPlayerID);
            item.m_customData.Remove(customKeySlotID);
        }

        public static string GetQuickSlot1Text() => quickSlotHotKey1Text.Value == "" ? quickSlotHotKey1.Value.ToString() : quickSlotHotKey1Text.Value;
        public static string GetQuickSlot2Text() => quickSlotHotKey2Text.Value == "" ? quickSlotHotKey2.Value.ToString() : quickSlotHotKey2Text.Value;
        public static string GetQuickSlot3Text() => quickSlotHotKey3Text.Value == "" ? quickSlotHotKey3.Value.ToString() : quickSlotHotKey3Text.Value;
        public static string GetQuickSlot4Text() => quickSlotHotKey4Text.Value == "" ? quickSlotHotKey4.Value.ToString() : quickSlotHotKey4Text.Value;
        public static string GetQuickSlot5Text() => quickSlotHotKey5Text.Value == "" ? quickSlotHotKey5.Value.ToString() : quickSlotHotKey5Text.Value;
        public static string GetQuickSlot6Text() => quickSlotHotKey6Text.Value == "" ? quickSlotHotKey6.Value.ToString() : quickSlotHotKey6Text.Value;

        public static string GetAmmoSlot1Text() => ammoSlotHotKey1Text.Value == "" ? (ammoSlotHotKey1.Value.Equals(KeyboardShortcut.Empty) ? "$exsl_slot_ammo_label" : ammoSlotHotKey1.Value.ToString()) : ammoSlotHotKey1Text.Value;
        public static string GetAmmoSlot2Text() => ammoSlotHotKey2Text.Value == "" ? (ammoSlotHotKey2.Value.Equals(KeyboardShortcut.Empty) ? "$exsl_slot_ammo_label" : ammoSlotHotKey2.Value.ToString()) : ammoSlotHotKey2Text.Value;
        public static string GetAmmoSlot3Text() => ammoSlotHotKey3Text.Value == "" ? (ammoSlotHotKey3.Value.Equals(KeyboardShortcut.Empty) ? "$exsl_slot_ammo_label" : ammoSlotHotKey3.Value.ToString()) : ammoSlotHotKey3Text.Value;

        public static void InitializeSlots()
        {
            int index = 0;

            // First row
            AddHotkeySlot($"{quickSlotID}1", () => quickSlotsShowLabel.Value ? GetQuickSlot1Text() : "", null, () => quickSlotsAmount.Value > 0, () => quickSlotHotKey1.Value, () => GetQuickSlot1Text());
            AddHotkeySlot($"{quickSlotID}2", () => quickSlotsShowLabel.Value ? GetQuickSlot2Text() : "", null, () => quickSlotsAmount.Value > 1, () => quickSlotHotKey2.Value, () => GetQuickSlot2Text());
            AddHotkeySlot($"{quickSlotID}3", () => quickSlotsShowLabel.Value ? GetQuickSlot3Text() : "", null, () => quickSlotsAmount.Value > 2, () => quickSlotHotKey3.Value, () => GetQuickSlot3Text());
            AddHotkeySlot($"{quickSlotID}4", () => quickSlotsShowLabel.Value ? GetQuickSlot4Text() : "", null, () => quickSlotsAmount.Value > 3, () => quickSlotHotKey4.Value, () => GetQuickSlot4Text());
            AddHotkeySlot($"{quickSlotID}5", () => quickSlotsShowLabel.Value ? GetQuickSlot5Text() : "", null, () => quickSlotsAmount.Value > 4, () => quickSlotHotKey5.Value, () => GetQuickSlot5Text());
            AddHotkeySlot($"{quickSlotID}6", () => quickSlotsShowLabel.Value ? GetQuickSlot6Text() : "", null, () => quickSlotsAmount.Value > 5, () => quickSlotHotKey6.Value, () => GetQuickSlot6Text());

            AddSlot($"{miscSlotID}1", () => miscSlotsShowLabel.Value ? "$exsl_slot_misc_label" : "", IsMiscSlotItem, () => miscSlotsEnabled.Value && (foodSlotsEnabled.Value || ammoSlotsEnabled.Value));
            AddSlot($"{miscSlotID}2", () => miscSlotsShowLabel.Value ? "$exsl_slot_misc_label" : "", IsMiscSlotItem, () => miscSlotsEnabled.Value && foodSlotsEnabled.Value && ammoSlotsEnabled.Value);

            // Second row
            AddHotkeySlot($"{ammoSlotID}1", 
                          () => ammoSlotsShowLabel.Value ? GetAmmoSlot1Text() : "", 
                          (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo, 
                          () => ammoSlotsEnabled.Value, 
                          () => ammoSlotHotKey1.Value,
                          () => GetAmmoSlot1Text());

            AddHotkeySlot($"{ammoSlotID}2",
                          () => ammoSlotsShowLabel.Value ? GetAmmoSlot2Text() : "",
                          (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo,
                          () => ammoSlotsEnabled.Value,
                          () => ammoSlotHotKey2.Value,
                          () => GetAmmoSlot2Text());

            AddHotkeySlot($"{ammoSlotID}3",
                          () => ammoSlotsShowLabel.Value ? GetAmmoSlot3Text() : "",
                          (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo,
                          () => ammoSlotsEnabled.Value,
                          () => ammoSlotHotKey3.Value,
                          () => GetAmmoSlot3Text());

            AddSlot($"{foodSlotID}1", () => foodSlotsShowLabel.Value ? "$exsl_slot_food_label" : "", IsFoodSlotItem, () => foodSlotsEnabled.Value);
            AddSlot($"{foodSlotID}2", () => foodSlotsShowLabel.Value ? "$exsl_slot_food_label" : "", IsFoodSlotItem, () => foodSlotsEnabled.Value);
            AddSlot($"{foodSlotID}3", () => foodSlotsShowLabel.Value ? "$exsl_slot_food_label" : "", IsFoodSlotItem, () => foodSlotsEnabled.Value);

            AddSlot($"{extraUtilitySlotID}1", () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => extraUtilitySlotsAmount.Value > 0);
            AddSlot($"{extraUtilitySlotID}2", () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => extraUtilitySlotsAmount.Value > 1);

            // Third row
            AddSlot(helmetSlotID, () => "$exsl_slot_equipment_helmet_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet, null);
            AddSlot(chestSlotID, () => "$exsl_slot_equipment_chest_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest, null);
            AddSlot(legsSlotID, () => "$exsl_slot_equipment_legs_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs, null);
            AddSlot(shoulderSlotID, () => "$exsl_slot_equipment_shoulders_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder, null);
            AddSlot(utilitySlotID, () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, null);

            for (int i = index; i < slots.Length; i++)
                AddSlot($"{emptySlotID}{i}", null, (item) => false, () => false);

            UpdateSlotsGridPosition();

            QuickSlotsHotBar.UpdateQuickSlots();
            AmmoSlotsHotBar.UpdateAmmoSlots();
            EquipmentPanel.UpdateSlotsCount();

            void AddSlot(string id, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
            {
                slots[index] = new Slot(id, index, getName, itemIsValid, isActive);
                index++;
            }

            void AddHotkeySlot(string id, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive, Func<KeyboardShortcut> getShortcut, Func<string> getShortcutText)
            {
                slots[index] = new Slot(id, index, getName, itemIsValid, isActive, getShortcut, getShortcutText);
                index++;
            }
        }

        internal static void UpdateSlotsGridPosition() => slots.Do(slot => slot.UpdateGridPosition());

        internal static void SwapSlots(int index, int indexToExchange)
        {
            (slots[index], slots[indexToExchange]) = (slots[indexToExchange], slots[index]);
            slots[index].SwapIndexWith(slots[indexToExchange]);
        }

        public static bool IsMiscSlotItem(ItemDrop.ItemData item)
        {
            return item != null && (item.m_shared.m_questItem || 
                                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy ||
                                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Misc ||
                                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Fish);
        }

        public static bool IsFoodSlotItem(ItemDrop.ItemData item)
        {
            return item != null && 
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable &&
                   (item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0 || item.m_shared.m_foodEitr > 0);
        }

        public static Slot GetSlotInGrid(Vector2i pos)
        {
            foreach (Slot slot in slots)
                if (slot.GridPosition == pos)
                    return slot;

            return null;
        }

        public static Slot GetItemSlot(ItemDrop.ItemData item)
        {
            foreach (Slot slot in slots)
                if (slot.Item == item)
                    return slot;

            return null;
        }

        internal static void ClearCachedItems() => cachedItems.Clear();
    }
}
