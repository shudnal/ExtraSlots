using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.SlotsProgression;

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
        public const string quickSlotID = "Quick";
        public const string foodSlotID = "Food";
        public const string ammoSlotID = "Ammo";
        public const string miscSlotID = "Misc";
        public const string customSlotID = "Custom";
        public const string emptySlotID = "Empty";

        public static readonly Vector2i emptyPosition = new Vector2i(-1, -1);

        public static readonly string VanillaOrder = $"{helmetSlotID},{chestSlotID},{legsSlotID},{shoulderSlotID},{utilitySlotID}";
        public static readonly HashSet<string> vanillaSlots = new HashSet<string>() { helmetSlotID, chestSlotID, legsSlotID, shoulderSlotID, utilitySlotID };

        private const string customKeyPlayerID = "ExtraSlotsEquippedBy";
        private const string customKeySlotID = "ExtraSlotsEquippedSlot";
        internal const string customKeyWeaponShield = "ExtraSlotsEquippedWeaponShield";

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
            
            public int Index => _index;

            public bool IsHotkeySlot => _getShortcut != null;
            public bool IsQuickSlot => _index < 6;
            public bool IsMiscSlot => 6 <= _index && _index <= 7;
            public bool IsAmmoSlot => 8 <= _index && _index <= 10;
            public bool IsFoodSlot => 11 <= _index && _index <= 13;
            public bool IsEquipmentSlot => _index > 13;
            public bool IsCustomSlot => _index >= CustomSlot.customSlotStartingIndex;
            public bool IsEmptySlot => _id == emptySlotID;

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
                // Cache slot item to be grid position independent when getting slot item
                CacheItem(); slot.CacheItem();

                (_index, slot._index) = (slot._index, _index);
                UpdateGridPosition();
                slot.UpdateGridPosition();
            }

            internal void SetSlotIndex(int index)
            {
                _index = index;
            }

            public bool IsVanillaEquipment() => vanillaSlots.Contains(_id);

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

                    return CacheItem();
                }
            }

            internal ItemDrop.ItemData CacheItem()
            {
                if (PlayerInventory == null)
                    return null;

                // Cache will be clear on inventory change
                ItemDrop.ItemData item = PlayerInventory.GetItemAt(_gridPos.x, _gridPos.y);
                cachedItems[_gridPos] = item;
                return item;
            }

            public bool IsFree => Item == null;

            public bool ItemFits(ItemDrop.ItemData item) => item != null && IsActive && (_itemIsValid == null || _itemIsValid(item));

            public bool IsFreeQuickSlot() => IsQuickSlot && IsActive && IsFree;

            public void ClearItemCache()
            {
                cachedItems.Remove(_gridPos);
            }

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

            public static bool IsShortcutDown(KeyboardShortcut shortcut) => shortcut.MainKey != KeyCode.None && ZInput.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(key => ZInput.GetKey(key));
        }

        internal static class CustomSlot
        {
            internal static int customSlotStartingIndex = 23;

            internal static bool TryAddNewSlotBefore(string[] slotIDs, string slotID, Func<string> getName = null, Func<ItemDrop.ItemData, bool> itemIsValid = null, Func<bool> isActive = null)
            {
                if (slotIDs.Length > 0)
                {
                    Slot slotToAdd = slots.FirstOrDefault(slot => slot.IsCustomSlot && slotIDs.Contains(GetSlotID(slotID)));
                    if (slotToAdd != null)
                        TryAddNewSlotWithIndex(slotID, slotToAdd.Index, getName, itemIsValid, isActive);
                }

                return TryAddNewSlotWithIndex(slotID, -1, getName, itemIsValid, isActive);
            }

            internal static bool TryAddNewSlotAfter(string[] slotIDs, string slotID, Func<string> getName = null, Func<ItemDrop.ItemData, bool> itemIsValid = null, Func<bool> isActive = null)
            {
                if (slotIDs.Length > 0)
                {
                    Slot slotToAdd = slots.LastOrDefault(slot => slot.IsCustomSlot && slotIDs.Contains(GetSlotID(slotID)));
                    if (slotToAdd != null)
                        TryAddNewSlotWithIndex(slotID, slotToAdd.Index, getName, itemIsValid, isActive);
                }

                return TryAddNewSlotWithIndex(slotID, -1, getName, itemIsValid, isActive);
            }

            internal static bool TryAddNewSlotWithIndex(string slotID, int slotIndex = -1, Func<string> getName = null, Func<ItemDrop.ItemData, bool> itemIsValid = null, Func<bool> isActive = null)
            {
                if (slots.Any(slot => slot.ID == GetSlotID(slotID)))
                    return true;

                // index < 0 - first available slot
                // index > 0 - clamp between custom slot starting index and max slots count then insert with shifting other slots right
                int index = slotIndex < 0 ? Array.FindIndex(slots, slot => slot.IsCustomSlot && slot.IsEmptySlot) : Mathf.Clamp(slotIndex + customSlotStartingIndex, customSlotStartingIndex, slots.Length - 1);
                if (index < 0)
                {
                    LogWarning($"Error adding new slot {slotID}. Too many custom slots.");
                    return false;
                }
                else if (index == slots.Length - 1 && !slots[index].IsEmptySlot)
                {
                    LogWarning($"Error adding new slot {slotID} with index {index}. Last available custom slot is taken.");
                    return false;
                }
                else if (!slots.Any(slot => slot.Index >= index && slot.IsEmptySlot))
                {
                    LogWarning($"Error adding new slot {slotID} with index {index}. All following slots are taken.");
                    return false;
                }

                if (slots[index].IsEmptySlot)
                    slots[index] = new Slot(GetSlotID(slotID), index, getName, itemIsValid, isActive);
                else
                    InsertSlot(index, GetSlotID(slotID), getName, itemIsValid, isActive);

                API.UpdateSlots();

                return slots[index] != null && !slots[index].IsEmptySlot;
            }

            internal static bool TryRemoveSlot(string slotID)
            {
                int index = Array.FindIndex(slots, slot => slot.IsCustomSlot && slot.ID == GetSlotID(slotID));
                if (index == -1)
                    return false;

                // Cache slot item to move them afterwards
                for (int i = index; i < slots.Length; i++)
                    slots[i].CacheItem();

                ItemDrop.ItemData item = slots[index].Item;

                // Shift slots left
                for (int i = index + 1; i < slots.Length; i++)
                {
                    slots[i - 1] = slots[i];
                    slots[i - 1].SetSlotIndex(i - 1);
                }

                slots[slots.Length - 1] = new Slot(emptySlotID, slots.Length - 1, null, (item) => false, () => false);

                for (int i = index; i < slots.Length; i++)
                    slots[i].UpdateGridPosition();

                if (item != null && TryFindFreeSlotForItem(slots[index].Item, out Slot newSlot))
                {
                    LogInfo($"While removing slot {slotID} item {item.m_shared.m_name} from {item.m_gridPos} was moved into first empty slot {newSlot} {newSlot.GridPosition}");
                    item.m_gridPos = newSlot.GridPosition;
                }

                API.UpdateSlots();

                return true;
            }

            internal static void InsertSlot(int startIndex, string slotID, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
            {
                int endIndex = Array.FindIndex(slots, slot => slot.Index >= startIndex && slot.IsEmptySlot); // find first empty slot to stop shifting
                LogInfo($"InsertSlot {slotID} at {startIndex} empty slot found {endIndex}");
                if (endIndex == -1)
                {
                    // It should not be the case but to prevent potential errors
                    LogWarning("Error trying to find empty slot to stop slots shifting after " + startIndex);
                    return;
                }

                // Cache slot item to move them afterwards
                for (int i = startIndex; i < endIndex; i++)
                    slots[i].CacheItem();

                // Shift slots right
                for (int i = endIndex; i > startIndex; i--)
                {
                    slots[i] = slots[i - 1];
                    slots[i].SetSlotIndex(i);
                }

                slots[startIndex] = new Slot(GetSlotID(slotID), startIndex, getName, itemIsValid, isActive);

                for (int i = endIndex; i >= startIndex; i--)
                    slots[i].UpdateGridPosition();
            }

            internal static string GetSlotID(string slotID) => $"{customSlotID}{slotID}";
        }

        public static readonly Slot[] slots = new Slot[32];
        public static readonly Dictionary<Vector2i, ItemDrop.ItemData> cachedItems = new Dictionary<Vector2i, ItemDrop.ItemData>();
        public const int vanillaInventoryHeight = 4;

        public static PlayerProfile PlayerProfile => Game.instance?.GetPlayerProfile() ?? FejdStartup.instance?.m_profiles[FejdStartup.instance.m_profileIndex];
        public static Player CurrentPlayer => Player.m_localPlayer ?? Compatibility.EquipmentAndQuickSlotsCompat.playerToLoad;
        public static Inventory PlayerInventory => CurrentPlayer?.GetInventory();
        public static int ExtraRowsPlayer => extraRows.Value;
        public static int InventoryWidth => PlayerInventory != null ? PlayerInventory.GetWidth() : 8;
        public static int InventoryHeightPlayer => vanillaInventoryHeight + ExtraRowsPlayer;
        public static int InventoryHeightFull => InventoryHeightPlayer + GetTargetInventoryHeight(slots.Length, InventoryWidth);
        public static int InventorySizePlayer => InventoryHeightPlayer * InventoryWidth;
        public static int InventorySizeFull => InventoryHeightFull * InventoryWidth;
        public static int InventorySizeActive => InventorySizePlayer + slots.Count(slot => slot.IsActive);

        public static int GetTargetInventoryHeight(int inventorySize, int inventoryWidth) => GetExtraRowsForItemsToFit(inventorySize, inventoryWidth);
        public static int GetExtraRowsForItemsToFit(int itemsAmount, int rowWidth) => ((itemsAmount - 1) / rowWidth) + 1;
        public static bool IsValidPlayer(Humanoid human) => human != null && human.IsPlayer() && Player.m_localPlayer == human && human.m_nview.IsValid() && human.m_nview.IsOwner();

        public static int GetEquipmentSlotsCount() => slots.Count(slot => slot.IsEquipmentSlot && slot.IsActive);
        public static int GetQuickSlotsCount() => slots.Count(slot => slot.IsQuickSlot && slot.IsActive);

        public static Slot[] GetEquipmentSlots(bool onlyActive = true)
        {
            List<Slot> equipment = new List<Slot>();
            equipment.AddRange(Array.FindAll(slots, slot => (!onlyActive || slot.IsActive) && slot.IsVanillaEquipment()).OrderBy(slot => slot.Index));
            equipment.AddRange(Array.FindAll(slots, slot => (!onlyActive || slot.IsActive) && slot.ID.StartsWith(extraUtilitySlotID)).OrderBy(slot => slot.Index));
            equipment.AddRange(Array.FindAll(slots, slot => (!onlyActive || slot.IsActive) && slot.IsCustomSlot).OrderBy(slot => slot.Index));

            return equipment.ToArray();
        }
        public static Slot[] GetQuickSlots() => Array.FindAll(slots, slot => slot.IsQuickSlot).OrderBy(slot => slot.Index).ToArray();
        public static Slot[] GetFoodSlots() => Array.FindAll(slots, slot => slot.IsFoodSlot).OrderBy(slot => slot.Index).ToArray();
        public static Slot[] GetAmmoSlots() => Array.FindAll(slots, slot => slot.IsAmmoSlot).OrderBy(slot => slot.Index).ToArray();
        public static Slot[] GetMiscSlots() => Array.FindAll(slots, slot => slot.IsMiscSlot).OrderBy(slot => slot.Index).ToArray();

        public static bool TryGetSavedPlayerSlot(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerID) && item.m_customData.TryGetValue(customKeySlotID, out string slotID) && playerID == PlayerProfile?.GetPlayerID().ToString())
                if ((slot = API.FindSlot(slotID)) != null)
                {
                    LogDebug($"Previous equipped slot {slot} found for item {item.m_shared.m_name}");
                    return true;
                }

            return false;
        }

        public static bool TryFindFreeSlotForItem(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item == null)
                return false;

            if (TryGetSavedPlayerSlot(item, out Slot prevSlot) && prevSlot.IsActive && prevSlot.ItemFits(item) && (prevSlot.IsFree || item == prevSlot.Item))
            {
                slot = prevSlot;
                return true;
            }

            int index = Array.FindIndex(slots, slot => slot.IsActive && slot.IsFree && slot.ItemFits(item));
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

            if (TryGetSavedPlayerSlot(item, out Slot prevSlot) && prevSlot.IsActive && prevSlot.IsEquipmentSlot && prevSlot.ItemFits(item) && (prevSlot.IsFree || item == prevSlot.Item))
            {
                slot = prevSlot;
                return true;
            }

            slot = GetEquipmentSlots().FirstOrDefault(slot => slot.ItemFits(item) && slot.IsFree);
            return slot != null;
        }

        public static bool TryFindFirstUnequippedSlotForItem(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item == null)
                return false;

            if (TryGetSavedPlayerSlot(item, out Slot prevSlot) && prevSlot.IsActive && prevSlot.IsEquipmentSlot && prevSlot.ItemFits(item) && (prevSlot.Item != null && !CurrentPlayer.IsItemEquiped(prevSlot.Item) || item == prevSlot.Item))
            {
                slot = prevSlot;
                return true;
            }

            slot = GetEquipmentSlots().FirstOrDefault(slot => slot.ItemFits(item) && (slot.Item != null && !CurrentPlayer.IsItemEquiped(slot.Item)));
            return slot != null;
        }

        public static bool TryMakeFreeSpaceInPlayerInventory(bool tryFindRegularInventorySlot, out Vector2i gridPos)
        {
            gridPos = emptyPosition;

            List<ItemDrop.ItemData> itemsInGridOrder = new List<ItemDrop.ItemData>();
            if (tryFindRegularInventorySlot)
                for (int i = InventoryHeightPlayer - 1; i >= 0; i--)
                    for (int j = InventoryWidth - 1; j >= 0; j--)
                        if (PlayerInventory.GetItemAt(j, i) is not ItemDrop.ItemData item)
                            return (gridPos = new Vector2i(j, i)) != emptyPosition;
                        else
                            itemsInGridOrder.Add(item);
            
            if (!tryFindRegularInventorySlot)
                itemsInGridOrder.AddRange(PlayerInventory.GetAllItemsInGridOrder().Where(item => item.m_gridPos.y < InventoryHeightPlayer).Reverse());

            // To be clear there is no cached items overlap
            ClearCachedItems();

            foreach (ItemDrop.ItemData item in itemsInGridOrder)
            {
                if (TryFindFreeEquipmentSlotForItem(item, out Slot equipmentSlot))
                {
                    LogDebug($"In attempt to create free space {item.m_shared.m_name} from {item.m_gridPos} was moved into equipment slot {equipmentSlot} {equipmentSlot.GridPosition}");
                    gridPos = item.m_gridPos;
                    item.m_gridPos = equipmentSlot.GridPosition;
                    return true;
                }

                if (TryFindFreeSlotForItem(item, out Slot slot))
                {
                    LogDebug($"In attempt to create free space {item.m_shared.m_name} from {item.m_gridPos} was moved into free slot {slot} {slot.GridPosition}");
                    gridPos = item.m_gridPos;
                    item.m_gridPos = slot.GridPosition;
                    return true;
                }
            }

            return false;
        }

        public static bool HaveEmptyQuickSlot() => slots.Any(slot => slot.IsFreeQuickSlot());

        public static int GetEmptyQuickSlots() => slots.Count(slot => slot.IsFreeQuickSlot());

        public static Vector2i FindEmptyQuickSlot() => TryFindEmptyQuickSlot(out Slot slot) ? slot.GridPosition : emptyPosition;

        public static bool TryFindEmptyQuickSlot(out Slot slot)
        {
            slot = slots.FirstOrDefault(slot => slot.IsFreeQuickSlot());
            return slot != null;
        }

        internal static void SaveLastEquippedSlotsToItems()
        {
            long playerID = Game.instance.GetPlayerProfile().GetPlayerID();

            int savedItems = 0;
            foreach (Slot slot in slots)
            {
                ItemDrop.ItemData item = slot.Item;
                if (item != null)
                {
                    item.m_customData[customKeyPlayerID] = playerID.ToString();
                    item.m_customData[customKeySlotID] = slot.ID;
                    savedItems++;
                }
            }

            if (savedItems > 0)
                LogDebug($"Last equpped slot was saved for {savedItems} items at extra slots. Player ID: {playerID}");
        }

        internal static void SaveLastEquippedWeaponShieldToItems(Player player)
        {
            long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
            
            if (player.LeftItem != null)
                player.LeftItem.m_customData[customKeyWeaponShield] = playerID.ToString();

            if (player.RightItem != null)
                player.RightItem.m_customData[customKeyWeaponShield] = playerID.ToString();

            if (player.LeftItem != null || player.RightItem != null)
                LogDebug($"Last equpped weapon/shield was saved to {(player.LeftItem != null ? player.LeftItem.m_shared.m_name + " ": "")}{(player.RightItem != null ? player.RightItem.m_shared.m_name : "")}. Player ID: {playerID}");
        }

        internal static void PruneLastEquippedSlotFromItem(ItemDrop.ItemData item)
        {
            if (item == null || !item.m_customData.ContainsKey(customKeySlotID))
                return;

            item.m_customData.Remove(customKeyPlayerID);
            item.m_customData.Remove(customKeySlotID);

            LogDebug($"{item.m_shared.m_name} {item.m_gridPos} pruned last equipped");
        }

        internal static void PruneLastEquippeWeaponShieldFromItem(ItemDrop.ItemData item)
        {
            if (item != null && item.m_customData.Remove(customKeyWeaponShield))
                LogDebug($"{item.m_shared.m_name} {item.m_gridPos} pruned last equipped weapon/shield");
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

        internal static void InitializeSlots()
        {
            int index = 0;

            // First row
            AddHotkeySlot($"{quickSlotID}1", () => quickSlotsShowLabel.Value ? GetQuickSlot1Text() : "", null, () => IsQuickSlotAvailable(0), () => quickSlotHotKey1.Value, () => GetQuickSlot1Text());
            AddHotkeySlot($"{quickSlotID}2", () => quickSlotsShowLabel.Value ? GetQuickSlot2Text() : "", null, () => IsQuickSlotAvailable(1), () => quickSlotHotKey2.Value, () => GetQuickSlot2Text());
            AddHotkeySlot($"{quickSlotID}3", () => quickSlotsShowLabel.Value ? GetQuickSlot3Text() : "", null, () => IsQuickSlotAvailable(2), () => quickSlotHotKey3.Value, () => GetQuickSlot3Text());
            AddHotkeySlot($"{quickSlotID}4", () => quickSlotsShowLabel.Value ? GetQuickSlot4Text() : "", null, () => IsQuickSlotAvailable(3), () => quickSlotHotKey4.Value, () => GetQuickSlot4Text());
            AddHotkeySlot($"{quickSlotID}5", () => quickSlotsShowLabel.Value ? GetQuickSlot5Text() : "", null, () => IsQuickSlotAvailable(4), () => quickSlotHotKey5.Value, () => GetQuickSlot5Text());
            AddHotkeySlot($"{quickSlotID}6", () => quickSlotsShowLabel.Value ? GetQuickSlot6Text() : "", null, () => IsQuickSlotAvailable(5), () => quickSlotHotKey6.Value, () => GetQuickSlot6Text());

            AddSlot($"{miscSlotID}1", () => miscSlotsShowLabel.Value ? "$exsl_slot_misc_label" : "", IsMiscSlotItem, () => IsFirstMiscSlotAvailable());
            AddSlot($"{miscSlotID}2", () => miscSlotsShowLabel.Value ? "$exsl_slot_misc_label" : "", IsMiscSlotItem, () => IsSecondMiscSlotAvailable());

            // Second row
            AddHotkeySlot($"{ammoSlotID}1", () => ammoSlotsShowLabel.Value ? GetAmmoSlot1Text() : "", IsAmmoSlotItem, () => IsAmmoSlotAvailable(), () => ammoSlotHotKey1.Value, () => GetAmmoSlot1Text());
            AddHotkeySlot($"{ammoSlotID}2", () => ammoSlotsShowLabel.Value ? GetAmmoSlot2Text() : "", IsAmmoSlotItem, () => IsAmmoSlotAvailable(), () => ammoSlotHotKey2.Value, () => GetAmmoSlot2Text());
            AddHotkeySlot($"{ammoSlotID}3", () => ammoSlotsShowLabel.Value ? GetAmmoSlot3Text() : "", IsAmmoSlotItem, () => IsAmmoSlotAvailable(), () => ammoSlotHotKey3.Value, () => GetAmmoSlot3Text());

            AddSlot($"{foodSlotID}1", () => foodSlotsShowLabel.Value ? "$exsl_slot_food_label" : "", IsFoodSlotItem, () => IsFoodSlotAvailable());
            AddSlot($"{foodSlotID}2", () => foodSlotsShowLabel.Value ? "$exsl_slot_food_label" : "", IsFoodSlotItem, () => IsFoodSlotAvailable());
            AddSlot($"{foodSlotID}3", () => foodSlotsShowLabel.Value ? "$exsl_slot_food_label" : "", IsFoodSlotItem, () => IsFoodSlotAvailable());

            AddSlot($"{extraUtilitySlotID}1", () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => IsExtraUtilitySlotAvailable(0));
            AddSlot($"{extraUtilitySlotID}2", () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => IsExtraUtilitySlotAvailable(1));

            // Third row
            AddSlot(helmetSlotID, () => "$exsl_slot_equipment_helmet_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet, () => IsHelmetSlotKnown());
            AddSlot(chestSlotID, () => "$exsl_slot_equipment_chest_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest, () => IsChestSlotKnown());
            AddSlot(legsSlotID, () => "$exsl_slot_equipment_legs_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs, () => IsLegsSlotKnown());
            AddSlot(shoulderSlotID, () => "$exsl_slot_equipment_shoulders_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder, () => IsShoulderSlotKnown());
            AddSlot(utilitySlotID, () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => IsUtilitySlotKnown());

            AddSlot($"{extraUtilitySlotID}3", () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => IsExtraUtilitySlotAvailable(2));
            AddSlot($"{extraUtilitySlotID}4", () => "$exsl_slot_equipment_utility_label", (item) => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility, () => IsExtraUtilitySlotAvailable(3));

            CustomSlot.customSlotStartingIndex = index;

            for (int i = index; i < slots.Length; i++)
                AddSlot(emptySlotID, null, (item) => false, () => false);

            UpdateSlotsGridPosition();

            HotBars.QuickSlotsHotBar.UpdateSlots();
            HotBars.AmmoSlotsHotBar.UpdateSlots();
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

        internal static bool IsQuickSlotAvailable(int index) => quickSlotsAmount.Value > index && IsQuickSlotKnown(index) && (index == 0 || IsQuickSlotAvailable(index - 1));

        internal static bool IsExtraUtilitySlotAvailable(int index) => extraUtilitySlotsAmount.Value > index && IsUtilitySlotKnown() && IsExtraUtilitySlotKnown(index) && (index == 0 || IsExtraUtilitySlotAvailable(index - 1));

        internal static bool IsFirstMiscSlotAvailable() => EquipmentPanel.quickSlotsCount > 0 && miscSlotsEnabled.Value && (IsFoodSlotAvailable() || IsAmmoSlotAvailable()) && IsAnyGlobalKeyActive(miscSlotsGlobalKey.Value);

        internal static bool IsSecondMiscSlotAvailable() => EquipmentPanel.quickSlotsCount > 0 && miscSlotsEnabled.Value && IsFoodSlotAvailable() && IsAmmoSlotAvailable() && IsAnyGlobalKeyActive(miscSlotsGlobalKey.Value);

        internal static bool IsFoodSlotAvailable() => foodSlotsEnabled.Value && IsAnyGlobalKeyActive(foodSlotsGlobalKey.Value) && IsFoodSlotKnown();

        internal static bool IsAmmoSlotAvailable() => ammoSlotsEnabled.Value && IsAnyGlobalKeyActive(ammoSlotsGlobalKey.Value) && IsAmmoSlotKnown();

        internal static void UpdateSlotsGridPosition() => slots.Do(slot => slot.UpdateGridPosition());

        internal static void SwapSlots(int index, int indexToExchange)
        {
            (slots[index], slots[indexToExchange]) = (slots[indexToExchange], slots[index]);
            slots[index].SwapIndexWith(slots[indexToExchange]);
        }

        public static bool IsEquipmentSlotItem(ItemDrop.ItemData item)
        {
            return slots.Any(slot => slot.IsEquipmentSlot && slot.IsActive && slot.ItemFits(item));
        }

        public static bool IsAmmoSlotItem(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo;
        }

        public static bool IsMiscSlotItem(ItemDrop.ItemData item)
        {
            return item != null && (item.m_shared.m_questItem || 
                                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy ||
                                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Misc ||
                                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Fish ||
                                    item.m_shared.m_name == "$item_coins");
        }

        public static bool IsFoodSlotItem(ItemDrop.ItemData item)
        {
            return item != null && 
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable &&
                   (item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0 || item.m_shared.m_foodEitr > 0);
        }

        public static bool IsGridPositionASlot(Vector2i gridPos)
        {
            return gridPos.y >= InventoryHeightPlayer;
        }

        public static bool IsItemInSlot(ItemDrop.ItemData item)
        {
            return item != null && IsGridPositionASlot(item.m_gridPos);
        }

        public static bool IsItemInEquipmentSlot(ItemDrop.ItemData item)
        {
            return (GetItemSlot(item) is Slot slot) && slot.IsEquipmentSlot;
        }

        public static Slot GetSlotInGrid(Vector2i pos)
        {
            if (!IsGridPositionASlot(pos))
                return null;

            foreach (Slot slot in slots)
                if (slot.GridPosition == pos)
                    return slot;

            return null;
        }

        public static Slot GetItemSlot(ItemDrop.ItemData item)
        {
            if (!IsItemInSlot(item))
                return null;

            if (PlayerInventory == null || !PlayerInventory.ContainsItem(item))
                return null;

            foreach (Slot slot in slots)
                if (slot.GridPosition == item.m_gridPos)
                    return slot;

            return null;
        }

        public static bool IsSameSlotType(Slot a, Slot b)
        {
            return a.IsEquipmentSlot == b.IsEquipmentSlot
                && a.IsFoodSlot == b.IsFoodSlot
                && a.IsQuickSlot == b.IsQuickSlot
                && a.IsAmmoSlot == b.IsAmmoSlot
                && a.IsMiscSlot == b.IsMiscSlot;
        }

        internal static void ClearCachedItems() => cachedItems.Clear();
    }
}
