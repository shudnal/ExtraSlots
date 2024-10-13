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
            private readonly Func<ItemDrop.ItemData, bool> _itemIsValid;
            private readonly Func<bool> _isActive;

            private int _index = -1;
            private Vector2 _position;
            private Vector2i _gridPos = emptyPosition;

            public string ID => _id;

            public string Name => _getName != null ? _getName() : "";

            public bool IsActive => _isActive == null || _isActive();

            public Vector2 Position => _position;

            public Vector2i GridPosition => _gridPos;

            public bool IsHotkeySlot => _getShortcut != null;
            public bool IsEquipmentSlot => _index > 13;
            public bool IsQuickSlot => _index < 6;
            public bool IsCustomSlot => _index > 20;

            internal void SetPosition(Vector2 newPosition)
            {
                if (_position != (_position = newPosition))
                    EquipmentPanel.MarkDirty();
            }

            internal void UpdateGridPosition() => _gridPos = new Vector2i(_index % InventoryWidth, _index / InventoryWidth + InventoryHeightPlayer);

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

            public ItemDrop.ItemData Item => PlayerInventory == null || _gridPos == emptyPosition ? null : PlayerInventory.GetItemAt(_gridPos.x, _gridPos.y);
            public bool IsFree => Item != null;

            public bool ItemFit(ItemDrop.ItemData item) => IsActive && (_itemIsValid == null || _itemIsValid(item));

            public bool IsFreeQuickSlot() => IsQuickSlot && IsActive && IsFree;

            public Slot(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
            {
                _id = slotID;
                _index = slotIndex;
                _getName = getName;
                _itemIsValid = itemIsValid;
                _isActive = isActive;
            }

            public Slot(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive, Func<KeyboardShortcut> getShortcut)
            {
                _id = slotID;
                _index = slotIndex;
                _getName = getName;
                _itemIsValid = itemIsValid;
                _getShortcut = getShortcut;
                _isActive = isActive;
            }

            public override string ToString() => (Name == "" ? ID : Name) + (IsActive ? "" : " (inactive)");

            private static bool IsShortcutDown(KeyboardShortcut shortcut) => shortcut.IsDown() || UnityInput.Current.GetKeyDown(shortcut.MainKey) && !shortcut.Modifiers.Any();
        }

        public class CustomSlot : Slot
        {
            public CustomSlot(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive) : base(slotID, slotIndex + 20, getName, itemIsValid, isActive)
            {
            }
        }

        public static readonly Slot[] slots = new Slot[36];
        
        public const int vanillaInventoryHeight = 4;

        public static Inventory PlayerInventory => Player.m_localPlayer?.GetInventory();
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
            equipment.AddRange(Array.FindAll(slots, slot => slot.IsVanillaEquipment()));
            equipment.AddRange(Array.FindAll(slots, slot => slot.IsActive && slot.ID.StartsWith(extraUtilitySlotID)));
            equipment.AddRange(Array.FindAll(slots, slot => slot.IsActive && slot.IsCustomSlot));

            return equipment.ToArray();
        }
        public static Slot[] GetQuickSlots() => Array.FindAll(slots, slot => slot.IsQuickSlot);
        public static Slot[] GetFoodSlots() => Array.FindAll(slots, slot => slot.ID.StartsWith(foodSlotID));
        public static Slot[] GetAmmoSlots() => Array.FindAll(slots, slot => slot.ID.StartsWith(ammoSlotID));
        public static Slot[] GetMiscSlots() => Array.FindAll(slots, slot => slot.ID.StartsWith(miscSlotID));

        public static bool TryFindFreeSlotForItem(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item == null)
                return false;

            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerID) && playerID == Game.instance.GetPlayerProfile().GetPlayerID().ToString() && item.m_customData.TryGetValue(customKeySlotID, out string slotID))
            {
                int oldSlotIndex = Array.FindIndex(slots, slot => slot.ID == slotID);
                if (oldSlotIndex > -1)
                {
                    Slot oldSlot = slots[oldSlotIndex];
                    if (oldSlot.IsActive && oldSlot.ItemFit(item) && oldSlot.IsFree)
                    {
                        slot = oldSlot;
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

        public static bool HaveEmptyQuickSlot() => slots.Any(slot => slot.IsFreeQuickSlot());

        public static int GetEmptyQuickSlots() => slots.Count(slot => slot.IsFreeQuickSlot());

        public static Vector2i FindEmptyQuickSlot() => TryFindEmptyQuickSlot(out Slot slot) ? slot.GridPosition : emptyPosition;

        private static bool TryFindEmptyQuickSlot(out Slot slot)
        {
            slot = slots.First(slot => slot.IsFreeQuickSlot());
            return slot != null;
        }

        internal static void SaveCurrentEquippedSlotsToItems()
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

        public static void InitializeSlots()
        {
            int index = 0;

            // First row
            AddHotkeySlot($"{quickSlotID}1", () => quickSlotHotKey1Text.Value == "" ? quickSlotHotKey1.Value.ToString() : quickSlotHotKey1Text.Value, null, () => quickSlotsAmount.Value > 0, () => quickSlotHotKey1.Value);
            AddHotkeySlot($"{quickSlotID}2", () => quickSlotHotKey2Text.Value == "" ? quickSlotHotKey2.Value.ToString() : quickSlotHotKey2Text.Value, null, () => quickSlotsAmount.Value > 1, () => quickSlotHotKey2.Value);
            AddHotkeySlot($"{quickSlotID}3", () => quickSlotHotKey3Text.Value == "" ? quickSlotHotKey3.Value.ToString() : quickSlotHotKey3Text.Value, null, () => quickSlotsAmount.Value > 2, () => quickSlotHotKey3.Value);
            AddHotkeySlot($"{quickSlotID}4", () => quickSlotHotKey4Text.Value == "" ? quickSlotHotKey4.Value.ToString() : quickSlotHotKey4Text.Value, null, () => quickSlotsAmount.Value > 3, () => quickSlotHotKey4.Value);
            AddHotkeySlot($"{quickSlotID}5", () => quickSlotHotKey5Text.Value == "" ? quickSlotHotKey5.Value.ToString() : quickSlotHotKey5Text.Value, null, () => quickSlotsAmount.Value > 4, () => quickSlotHotKey5.Value);
            AddHotkeySlot($"{quickSlotID}6", () => quickSlotHotKey6Text.Value == "" ? quickSlotHotKey6.Value.ToString() : quickSlotHotKey6Text.Value, null, () => quickSlotsAmount.Value > 5, () => quickSlotHotKey6.Value);

            AddSlot($"{miscSlotID}1", () => miscLabel.Value, IsMiscSlotItem, () => foodSlotsEnabled.Value || ammoSlotsEnabled.Value);
            AddSlot($"{miscSlotID}2", () => miscLabel.Value, IsMiscSlotItem, () => ammoSlotsEnabled.Value);

            // Second row
            AddHotkeySlot($"{ammoSlotID}1", 
                          () => ammoSlotHotKey1.Value.Equals(KeyboardShortcut.Empty) ? ammoLabel.Value : ammoSlotHotKey1.Value.ToString(), 
                          (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo, 
                          () => ammoSlotsEnabled.Value, 
                          () => ammoSlotHotKey1.Value);

            AddHotkeySlot($"{ammoSlotID}2",
                          () => ammoSlotHotKey2.Value.Equals(KeyboardShortcut.Empty) ? ammoLabel.Value : ammoSlotHotKey2.Value.ToString(),
                          (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo,
                          () => ammoSlotsEnabled.Value,
                          () => ammoSlotHotKey2.Value);

            AddHotkeySlot($"{ammoSlotID}3",
                          () => ammoSlotHotKey3.Value.Equals(KeyboardShortcut.Empty) ? ammoLabel.Value : ammoSlotHotKey3.Value.ToString(),
                          (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo,
                          () => ammoSlotsEnabled.Value,
                          () => ammoSlotHotKey3.Value);

            AddSlot($"{foodSlotID}1", () => foodLabel.Value, IsFoodSlotItem, () => foodSlotsEnabled.Value);
            AddSlot($"{foodSlotID}2", () => foodLabel.Value, IsFoodSlotItem, () => foodSlotsEnabled.Value);
            AddSlot($"{foodSlotID}3", () => foodLabel.Value, IsFoodSlotItem, () => foodSlotsEnabled.Value);

            AddSlot($"{extraUtilitySlotID}1", () => utilityLabel.Value, (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => extraUtilitySlotsAmount.Value > 0);
            AddSlot($"{extraUtilitySlotID}2", () => utilityLabel.Value, (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => extraUtilitySlotsAmount.Value > 1);

            // Third row
            AddSlot(helmetSlotID, () => helmetLabel.Value, (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet, null);
            AddSlot(chestSlotID, () => chestLabel.Value, (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest, null);
            AddSlot(legsSlotID, () => legsLabel.Value, (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs, null);
            AddSlot(shoulderSlotID, () => shoulderLabel.Value, (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder, null);
            AddSlot(utilitySlotID, () => utilityLabel.Value, (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, null);

            for (int i = ++index; i < slots.Length; i++)
                AddSlot($"{emptySlotID}{i}", null, (item) => false, () => false);

            slots.Do(slot => slot.UpdateGridPosition());

            QuickSlotsHotBar.UpdateQuickSlots();
            EquipmentPanel.UpdateSlotsCount();

            void AddSlot(string id, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
            {
                slots[index] = new Slot(id, index, getName, itemIsValid, isActive);
                index++;
            }

            void AddHotkeySlot(string id, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive, Func<KeyboardShortcut> getShortcut)
            {
                slots[index] = new Slot(id, index, getName, itemIsValid, isActive, getShortcut);
                index++;
            }
        }

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

        public static void DeinitializeSlots()
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i] = null;
        }
    }
}
