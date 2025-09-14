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
        public const string trinketSlotID = "Trinket";

        public static readonly Vector2i emptyPosition = new Vector2i(-1, -1);

        public static readonly string VanillaOrder = $"{helmetSlotID},{chestSlotID},{legsSlotID},{shoulderSlotID},{utilitySlotID},{trinketSlotID}";
        public static readonly HashSet<string> vanillaSlots = new HashSet<string>(VanillaOrder.Split(','));
        public static readonly HashSet<string> miscItemsList = new HashSet<string>();
        public static readonly HashSet<string> ammoItemsList = new HashSet<string>();
        public static readonly HashSet<string> foodItemsList = new HashSet<string>();

        public const string customKeyPlayerID = "ExtraSlotsEquippedBy";
        public const string customKeySlotID = "ExtraSlotsEquippedSlot";
        internal const string customKeyWeaponShield = "ExtraSlotsEquippedWeaponShield";

        private static readonly List<ItemDrop.ItemData> itemsInGridOrder = new List<ItemDrop.ItemData>();

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
            public int EquipmentIndex => IsCustomSlot ? _index + 200 : (IsTrinketSlot || IsVanillaSlot) ? _index : _index + 100;

            public bool IsHotkeySlot => _getShortcut != null;
            public bool IsQuickSlot => _index < 6;
            public bool IsMiscSlot => 6 <= _index && _index <= 7;
            public bool IsAmmoSlot => 8 <= _index && _index <= 10;
            public bool IsFoodSlot => 11 <= _index && _index <= 13;
            public bool IsVanillaSlot => 16 <= _index && _index <= 20;
            public bool IsTrinketSlot => _index == 23;
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

            public bool IsShortcutDown() => IsActive && _getShortcut != null && Player.m_localPlayer?.TakeInput() == true && IsShortcutDown(_getShortcut());
            public bool IsShortcutDownWithItem() => IsShortcutDown() && Item != null;

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
            internal static int customSlotStartingIndex = 24;

            internal static bool TryAddNewSlotBefore(string[] slotIDs, string slotID, Func<string> getName = null, Func<ItemDrop.ItemData, bool> itemIsValid = null, Func<bool> isActive = null)
            {
                Slot slotToAdd = slots.FirstOrDefault(slot => slot.IsCustomSlot && slotIDs.Contains(GetSlotID(slotID)));
                if (slotToAdd != null)
                    return TryAddNewSlotWithIndex(slotID, slotToAdd.Index, getName, itemIsValid, isActive);

                return TryAddNewSlotWithIndex(slotID, -1, getName, itemIsValid, isActive);
            }

            internal static bool TryAddNewSlotAfter(string[] slotIDs, string slotID, Func<string> getName = null, Func<ItemDrop.ItemData, bool> itemIsValid = null, Func<bool> isActive = null)
            {
                Slot slotToAdd = slots.LastOrDefault(slot => slot.IsCustomSlot && slotIDs.Contains(GetSlotID(slotID)));
                if (slotToAdd != null)
                    return TryAddNewSlotWithIndex(slotID, slotToAdd.Index, getName, itemIsValid, isActive);

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
        public const int VanillaInventoryHeight = 4;

        public static Player loadedPlayer;

        public static PlayerProfile CurrentPlayerProfile => Game.instance?.GetPlayerProfile() ?? FejdStartup.instance?.m_profiles[FejdStartup.instance.m_profileIndex];
        public static Player CurrentPlayer => Player.m_localPlayer ?? Compatibility.EquipmentAndQuickSlotsCompat.playerToLoad ?? loadedPlayer ?? FejdStartup.instance?.GetPreviewPlayer();
        public static Inventory PlayerInventory => CurrentPlayer?.GetInventory();
        public static int ExtraRowsPlayer => GetExtraRows();
        public static int InventoryWidth => PlayerInventory != null ? PlayerInventory.GetWidth() : 8;
        public static int InventoryHeightPlayer => VanillaInventoryHeight + ExtraRowsPlayer;
        public static int InventoryHeightFull => InventoryHeightPlayer + GetTargetInventoryHeight(slots.Length, InventoryWidth);
        public static int InventorySizePlayer => InventoryHeightPlayer * InventoryWidth;
        public static int InventorySizeFull => InventoryHeightFull * InventoryWidth;
        public static int InventorySizeActive => InventorySizePlayer + slots.Count(slot => slot.IsActive);

        public static int GetTargetInventoryHeight(int inventorySize, int inventoryWidth) => GetExtraRowsForItemsToFit(inventorySize, inventoryWidth);
        public static int GetExtraRowsForItemsToFit(int itemsAmount, int rowWidth) => ((itemsAmount - 1) / rowWidth) + 1;
        public static bool IsValidPlayer(Character character) => character != null && character.IsPlayer() && Player.m_localPlayer == character && character.m_nview && character.m_nview.IsValid() && character.m_nview.IsOwner();

        public static int GetEquipmentSlotsCount() => slots.Count(slot => slot.IsEquipmentSlot && slot.IsActive);
        public static int GetQuickSlotsCount() => slots.Count(slot => slot.IsQuickSlot && slot.IsActive);

        public static Slot[] GetEquipmentSlots(bool onlyActive = true) => slots.Where(slot => slot.IsEquipmentSlot && (!onlyActive || slot.IsActive)).OrderBy(slot => slot.EquipmentIndex).ToArray();

        public static Slot[] GetQuickSlots() => Array.FindAll(slots, slot => slot.IsQuickSlot).OrderBy(slot => slot.Index).ToArray();
        public static Slot[] GetFoodSlots() => Array.FindAll(slots, slot => slot.IsFoodSlot).OrderBy(slot => slot.Index).ToArray();
        public static Slot[] GetAmmoSlots() => Array.FindAll(slots, slot => slot.IsAmmoSlot).OrderBy(slot => slot.Index).ToArray();
        public static Slot[] GetMiscSlots() => Array.FindAll(slots, slot => slot.IsMiscSlot).OrderBy(slot => slot.Index).ToArray();

        public static bool TryGetSavedPlayerSlot(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;

            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerID) && item.m_customData.TryGetValue(customKeySlotID, out string slotID) && playerID == CurrentPlayerProfile?.GetPlayerID().ToString())
                if ((slot = API.FindSlot(slotID)) != null)
                {
                    LogDebug($"Previous slot {slot} found for item {item.m_shared.m_name}");
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

            int index = Array.FindIndex(slots, slot => slot.IsCustomSlot && slot.IsActive && slot.IsFree && slot.ItemFits(item));
            if (index == -1)
                index = Array.FindIndex(slots, slot => slot.IsActive && slot.IsFree && slot.ItemFits(item));

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

            Slot[] equipmentSlots = GetEquipmentSlots();

            slot = equipmentSlots.FirstOrDefault(slot => slot.IsCustomSlot && slot.ItemFits(item) && slot.IsFree) ?? equipmentSlots.FirstOrDefault(slot => slot.ItemFits(item) && slot.IsFree);
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
            Slot[] equipmentSlots = GetEquipmentSlots();

            slot = equipmentSlots.FirstOrDefault(slot => slot.IsCustomSlot && slot.ItemFits(item) && slot.Item != null && !CurrentPlayer.IsItemEquiped(slot.Item)) ?? equipmentSlots.FirstOrDefault(slot => slot.ItemFits(item) && slot.Item != null && !CurrentPlayer.IsItemEquiped(slot.Item));
            return slot != null;
        }

        public static bool TryMakeFreeSpaceInPlayerInventory(bool tryFindRegularInventorySlot, out Vector2i gridPos)
        {
            gridPos = emptyPosition;

            itemsInGridOrder.Clear();
            if (tryFindRegularInventorySlot)
                for (int i = InventoryHeightPlayer - 1; i >= 0; i--)
                    for (int j = InventoryWidth - 1; j >= 0; j--)
                        if (PlayerInventory.GetItemAt(j, i) is not ItemDrop.ItemData item)
                            return (gridPos = new Vector2i(j, i)) != emptyPosition;
                        else
                            itemsInGridOrder.Add(item);
            else
                itemsInGridOrder.AddRange(
                            PlayerInventory.GetAllItemsInGridOrder()
                                .Where(item => item.m_gridPos.y < InventoryHeightPlayer)
                                .OrderByDescending(item => item.m_gridPos.y)
                                .ThenByDescending(item => item.m_gridPos.x)
                        );

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
            if (!Game.instance)
                return;

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
                LogDebug($"Last slot was saved for {savedItems} items at extra slots. Player ID: {playerID}");
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
            PruneLastEquippeWeaponShieldFromItem(item);

            if (item == null || !item.m_customData.ContainsKey(customKeySlotID))
                return;

            item.m_customData.Remove(customKeyPlayerID);
            item.m_customData.Remove(customKeySlotID);

            LogDebug($"{item.m_shared.m_name} {item.m_gridPos} pruned last taken slot");
        }

        internal static void PruneLastEquippeWeaponShieldFromItem(ItemDrop.ItemData item)
        {
            if (item != null && item.m_customData.Remove(customKeyWeaponShield))
                LogDebug($"{item.m_shared.m_name} {item.m_gridPos} pruned last equipped weapon/shield");
        }

        private static string KeyTextOr(string customText, KeyboardShortcut key, string fallbackLabel)
            => string.IsNullOrEmpty(customText)
               ? (key.Equals(KeyboardShortcut.Empty) ? fallbackLabel : key.ToString())
               : customText;

        public static string GetQuickSlot1Text() => string.IsNullOrEmpty(quickSlotHotKey1Text.Value) ? quickSlotHotKey1.Value.ToString() : quickSlotHotKey1Text.Value;
        public static string GetQuickSlot2Text() => string.IsNullOrEmpty(quickSlotHotKey2Text.Value) ? quickSlotHotKey2.Value.ToString() : quickSlotHotKey2Text.Value;
        public static string GetQuickSlot3Text() => string.IsNullOrEmpty(quickSlotHotKey3Text.Value) ? quickSlotHotKey3.Value.ToString() : quickSlotHotKey3Text.Value;
        public static string GetQuickSlot4Text() => string.IsNullOrEmpty(quickSlotHotKey4Text.Value) ? quickSlotHotKey4.Value.ToString() : quickSlotHotKey4Text.Value;
        public static string GetQuickSlot5Text() => string.IsNullOrEmpty(quickSlotHotKey5Text.Value) ? quickSlotHotKey5.Value.ToString() : quickSlotHotKey5Text.Value;
        public static string GetQuickSlot6Text() => string.IsNullOrEmpty(quickSlotHotKey6Text.Value) ? quickSlotHotKey6.Value.ToString() : quickSlotHotKey6Text.Value;

        private static string GetQuickSlot1Label() => quickSlotsShowLabel.Value ? GetQuickSlot1Text() : "";
        private static string GetQuickSlot2Label() => quickSlotsShowLabel.Value ? GetQuickSlot2Text() : "";
        private static string GetQuickSlot3Label() => quickSlotsShowLabel.Value ? GetQuickSlot3Text() : "";
        private static string GetQuickSlot4Label() => quickSlotsShowLabel.Value ? GetQuickSlot4Text() : "";
        private static string GetQuickSlot5Label() => quickSlotsShowLabel.Value ? GetQuickSlot5Text() : "";
        private static string GetQuickSlot6Label() => quickSlotsShowLabel.Value ? GetQuickSlot6Text() : "";

        private static bool IsQuickSlot1Available() => IsQuickSlotAvailable(0);
        private static bool IsQuickSlot2Available() => IsQuickSlotAvailable(1);
        private static bool IsQuickSlot3Available() => IsQuickSlotAvailable(2);
        private static bool IsQuickSlot4Available() => IsQuickSlotAvailable(3);
        private static bool IsQuickSlot5Available() => IsQuickSlotAvailable(4);
        private static bool IsQuickSlot6Available() => IsQuickSlotAvailable(5);

        private static KeyboardShortcut GetQuickSlot1Key() => quickSlotHotKey1.Value;
        private static KeyboardShortcut GetQuickSlot2Key() => quickSlotHotKey2.Value;
        private static KeyboardShortcut GetQuickSlot3Key() => quickSlotHotKey3.Value;
        private static KeyboardShortcut GetQuickSlot4Key() => quickSlotHotKey4.Value;
        private static KeyboardShortcut GetQuickSlot5Key() => quickSlotHotKey5.Value;
        private static KeyboardShortcut GetQuickSlot6Key() => quickSlotHotKey6.Value;

        public static string GetAmmoSlot1Text() => KeyTextOr(ammoSlotHotKey1Text.Value, ammoSlotHotKey1.Value, "$exsl_slot_ammo_label");
        public static string GetAmmoSlot2Text() => KeyTextOr(ammoSlotHotKey2Text.Value, ammoSlotHotKey2.Value, "$exsl_slot_ammo_label");
        public static string GetAmmoSlot3Text() => KeyTextOr(ammoSlotHotKey3Text.Value, ammoSlotHotKey3.Value, "$exsl_slot_ammo_label");
        
        public static string GetFoodSlot1Text() => KeyTextOr(foodSlotHotKey1Text.Value, foodSlotHotKey1.Value, "$exsl_slot_food_label");
        public static string GetFoodSlot2Text() => KeyTextOr(foodSlotHotKey2Text.Value, foodSlotHotKey2.Value, "$exsl_slot_food_label");
        public static string GetFoodSlot3Text() => KeyTextOr(foodSlotHotKey3Text.Value, foodSlotHotKey3.Value, "$exsl_slot_food_label");

        private static string GetMiscSlotLabel() => miscSlotsShowLabel.Value ? "$exsl_slot_misc_label" : "";

        private static string GetAmmoSlot1Label() => ammoSlotsShowLabel.Value ? GetAmmoSlot1Text() : "";
        private static string GetAmmoSlot2Label() => ammoSlotsShowLabel.Value ? GetAmmoSlot2Text() : "";
        private static string GetAmmoSlot3Label() => ammoSlotsShowLabel.Value ? GetAmmoSlot3Text() : "";

        private static KeyboardShortcut GetAmmoSlot1Key() => ammoSlotHotKey1.Value;
        private static KeyboardShortcut GetAmmoSlot2Key() => ammoSlotHotKey2.Value;
        private static KeyboardShortcut GetAmmoSlot3Key() => ammoSlotHotKey3.Value;

        private static string GetFoodSlot1Label() => foodSlotsShowLabel.Value ? GetFoodSlot1Text() : "";
        private static string GetFoodSlot2Label() => foodSlotsShowLabel.Value ? GetFoodSlot2Text() : "";
        private static string GetFoodSlot3Label() => foodSlotsShowLabel.Value ? GetFoodSlot3Text() : "";

        private static KeyboardShortcut GetFoodSlot1Key() => foodSlotHotKey1.Value;
        private static KeyboardShortcut GetFoodSlot2Key() => foodSlotHotKey2.Value;
        private static KeyboardShortcut GetFoodSlot3Key() => foodSlotHotKey3.Value;

        private static bool IsUtilitySlot1Available() => IsExtraUtilitySlotAvailable(0);
        private static bool IsUtilitySlot2Available() => IsExtraUtilitySlotAvailable(1);
        private static bool IsUtilitySlot3Available() => IsExtraUtilitySlotAvailable(2);
        private static bool IsUtilitySlot4Available() => IsExtraUtilitySlotAvailable(3);

        internal static void InitializeSlots()
        {
            int index = 0;

            // First row
            AddHotkeySlot($"{quickSlotID}1", GetQuickSlot1Label, null, IsQuickSlot1Available, GetQuickSlot1Key, GetQuickSlot1Text);
            AddHotkeySlot($"{quickSlotID}2", GetQuickSlot2Label, null, IsQuickSlot2Available, GetQuickSlot2Key, GetQuickSlot2Text);
            AddHotkeySlot($"{quickSlotID}3", GetQuickSlot3Label, null, IsQuickSlot3Available, GetQuickSlot3Key, GetQuickSlot3Text);
            AddHotkeySlot($"{quickSlotID}4", GetQuickSlot4Label, null, IsQuickSlot4Available, GetQuickSlot4Key, GetQuickSlot4Text);
            AddHotkeySlot($"{quickSlotID}5", GetQuickSlot5Label, null, IsQuickSlot5Available, GetQuickSlot5Key, GetQuickSlot5Text);
            AddHotkeySlot($"{quickSlotID}6", GetQuickSlot6Label, null, IsQuickSlot6Available, GetQuickSlot6Key, GetQuickSlot6Text);

            AddSlot($"{miscSlotID}1", GetMiscSlotLabel, IsMiscSlotItem, IsFirstMiscSlotAvailable);
            AddSlot($"{miscSlotID}2", GetMiscSlotLabel, IsMiscSlotItem, IsSecondMiscSlotAvailable);

            // Second row
            AddHotkeySlot($"{ammoSlotID}1", GetAmmoSlot1Label, IsAmmoSlotItem, IsAmmoSlotAvailable, GetAmmoSlot1Key, GetAmmoSlot1Text);
            AddHotkeySlot($"{ammoSlotID}2", GetAmmoSlot2Label, IsAmmoSlotItem, IsAmmoSlotAvailable, GetAmmoSlot2Key, GetAmmoSlot2Text);
            AddHotkeySlot($"{ammoSlotID}3", GetAmmoSlot3Label, IsAmmoSlotItem, IsAmmoSlotAvailable, GetAmmoSlot3Key, GetAmmoSlot3Text);

            AddHotkeySlot($"{foodSlotID}1", GetFoodSlot1Label, IsFoodSlotItem, IsFoodSlotAvailable, GetFoodSlot1Key, GetFoodSlot1Text);
            AddHotkeySlot($"{foodSlotID}2", GetFoodSlot2Label, IsFoodSlotItem, IsFoodSlotAvailable, GetFoodSlot2Key, GetFoodSlot2Text);
            AddHotkeySlot($"{foodSlotID}3", GetFoodSlot3Label, IsFoodSlotItem, IsFoodSlotAvailable, GetFoodSlot3Key, GetFoodSlot3Text);

            AddSlot($"{extraUtilitySlotID}1", () => "$exsl_slot_equipment_utility_label", IsUtilitySlotItem, IsUtilitySlot1Available);
            AddSlot($"{extraUtilitySlotID}2", () => "$exsl_slot_equipment_utility_label", IsUtilitySlotItem, IsUtilitySlot2Available);

            // Third row
            AddSlot(helmetSlotID,   () => "$exsl_slot_equipment_helmet_label",      IsHelmetSlotItem,       IsHelmetSlotKnown);
            AddSlot(chestSlotID,    () => "$exsl_slot_equipment_chest_label",       IsChestSlotItem,        IsChestSlotKnown);
            AddSlot(legsSlotID,     () => "$exsl_slot_equipment_legs_label",        IsLegsSlotItem,         IsLegsSlotKnown);
            AddSlot(shoulderSlotID, () => "$exsl_slot_equipment_shoulders_label",   IsShoulderSlotItem,     IsShoulderSlotKnown);
            AddSlot(utilitySlotID,  () => "$exsl_slot_equipment_utility_label",     IsUtilitySlotItem,      IsUtilitySlotKnown);

            AddSlot($"{extraUtilitySlotID}3", () => "$exsl_slot_equipment_utility_label", IsUtilitySlotItem, IsUtilitySlot3Available);
            AddSlot($"{extraUtilitySlotID}4", () => "$exsl_slot_equipment_utility_label", IsUtilitySlotItem, IsUtilitySlot4Available);

            AddSlot(trinketSlotID, () => "$exsl_slot_equipment_trinket_label", IsTrinketSlotItem, IsTrinketSlotKnown);

            CustomSlot.customSlotStartingIndex = index;

            for (int i = index; i < slots.Length; i++)
                AddSlot(emptySlotID, () => "", (item) => false, () => false);

            UpdateSlotsGridPosition();

            UpdateMiscSlotCustomItemList();

            HotBars.QuickSlotsHotBar.UpdateSlots();
            HotBars.AmmoSlotsHotBar.UpdateSlots();
            HotBars.FoodSlotsHotBar.UpdateSlots();
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
        
        internal static bool IsExtraRowAvailable(int index) => extraRows.Value > index && IsExtraRowKnown(index) && (index == 0 || IsExtraRowAvailable(index - 1));

        internal static int GetExtraRows()
        {
            for (int i = extraRows.Value - 1; i >= 0; i--)
                if (IsExtraRowAvailable(i))
                    return i + 1;

            return 0;
        }

        internal static void UpdateSlotsGridPosition()
        {
            ClearCachedItems();
            Dictionary<Slot, ItemDrop.ItemData> slotItems = slots.ToDictionary(slot => slot, slot => slot.CacheItem());

            slots.Do(slot => slot.UpdateGridPosition());

            ClearCachedItems();

            InventoryInteraction.UpdatePlayerInventorySize();
        }

        internal static void UpdateMiscSlotCustomItemList() 
        {
            miscItemsList.Clear();
            miscItemsList.Add("$item_coins");
            miscSlotsItemList.Value.Split(',').Do(item => miscItemsList.Add(item));
        }

        internal static void UpdateAmmoSlotCustomItemList()
        {
            ammoItemsList.Clear();
            ammoSlotsItemList.Value.Split(',').Do(item => ammoItemsList.Add(item));
        }

        internal static void UpdateFoodSlotCustomItemList()
        {
            foodItemsList.Clear();
            foodSlotsItemList.Value.Split(',').Do(item => foodItemsList.Add(item));
        }

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
            return item != null &&
                   (
                       item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
                       ammoItemsList.Contains(item.m_shared.m_name) ||
                       ammoSlotsAllowThrowables.Value && IsThrowable(item.m_shared)
                   );

            static bool IsThrowable(ItemDrop.ItemData.SharedData item) => item.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon && 
                                                                          item.m_animationState == ItemDrop.ItemData.AnimationState.Unarmed && 
                                                                          item.m_attack?.m_attackAnimation == "throw_bomb";
        }

        public static bool IsMiscSlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   (
                       item.m_shared.m_questItem ||
                       item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy ||
                       item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Misc ||
                       item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Fish ||
                       miscItemsList.Contains(item.m_shared.m_name)
                   );
        }

        public static bool IsFoodSlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   (
                       item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable &&
                       (
                           item.m_shared.m_food > 0 ||
                           item.m_shared.m_foodStamina > 0 ||
                           item.m_shared.m_foodEitr > 0 ||
                           item.m_shared.m_isDrink ||
                           item.m_shared.m_consumeStatusEffect is SE_Stats se &&
                           (
                               se.m_healthOverTime + se.m_staminaOverTime + se.m_eitrOverTime > 0
                           )
                       ) ||
                       foodItemsList.Contains(item.m_shared.m_name)
                   );
        }

        public static bool IsHelmetSlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet;
        }

        public static bool IsChestSlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest;
        }

        public static bool IsLegsSlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs;
        }

        public static bool IsShoulderSlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder;
        }

        public static bool IsUtilitySlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility &&
                   !Compatibility.MagicPluginCompat.IsMagicPluginCustomSlotItem(item);
        }

        public static bool IsTrinketSlotItem(ItemDrop.ItemData item)
        {
            return item != null &&
                   item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket;
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
