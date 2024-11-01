using JetBrains.Annotations;
using System;
using System.Collections.Generic;
#if ! API
using System.Linq;
using static ExtraSlots.Slots;
#endif

namespace ExtraSlots;

[PublicAPI]
public class API
{
    /// <summary>
    /// Notifies if the ExtraSlots plugin is active or not.
    /// </summary>
    /// <returns>true or false</returns>
    public static bool IsLoaded()
    {
#if !API
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// Returns list of all slots
    /// </summary>
    public static List<ExtraSlot> GetExtraSlots()
    {
#if !API
        return slots.Select(slot => slot.ToExtraSlot()).ToList();
#else
        return new List<ExtraSlot>();
#endif
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<ExtraSlot> GetEquipmentSlots()
    {
#if !API
        return slots.Where(slot => slot.IsEquipmentSlot).Select(slot => slot.ToExtraSlot()).ToList();
#else
        return new List<ExtraSlot>();
#endif
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<ExtraSlot> GetQuickSlots()
    {
#if !API
        return slots.Where(slot => slot.IsQuickSlot).Select(slot => slot.ToExtraSlot()).ToList();
#else
        return new List<ExtraSlot>();
#endif
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<ExtraSlot> GetFoodSlots()
    {
#if !API
        return slots.Where(slot => slot.IsFoodSlot).Select(slot => slot.ToExtraSlot()).ToList();
#else
        return new List<ExtraSlot>();
#endif
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<ExtraSlot> GetAmmoSlots()
    {
#if !API
        return slots.Where(slot => slot.IsAmmoSlot).Select(slot => slot.ToExtraSlot()).ToList();
#else
        return new List<ExtraSlot>();
#endif
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<ExtraSlot> GetMiscSlots()
    {
#if !API
        return slots.Where(slot => slot.IsMiscSlot).Select(slot => slot.ToExtraSlot()).ToList();
#else
        return new List<ExtraSlot>();
#endif
    }

    /// <summary>
    /// Returns slot with given ID
    /// </summary>
    /// <param name="slotID"></param>
    public static ExtraSlot FindSlot(string slotID)
    {
#if !API
        return slots.Where(slot => slot.ID == slotID || slot.ID == CustomSlot.GetSlotID(slotID)).Select(slot => slot.ToExtraSlot()).FirstOrDefault();
#else
        return null;
#endif
    }

    /// <summary>
    /// Returns list of items located in extra slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetAllExtraSlotsItems()
    {
#if !API
        return slots.Select(slot => slot.Item).Where(item => item != null).ToList();
#else
        return new List<ItemDrop.ItemData>();
#endif
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetEquipmentSlotsItems()
    {
#if !API
        return GetEquipmentSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
#else
        return new List<ItemDrop.ItemData>();
#endif
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetQuickSlotsItems()
    {
#if !API
        return GetQuickSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
#else
        return new List<ItemDrop.ItemData>();
#endif
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetFoodSlotsItems()
    {
#if !API
        return GetFoodSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
#else
        return new List<ItemDrop.ItemData>();
#endif
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetAmmoSlotsItems()
    {
#if !API
        return GetAmmoSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
#else
        return new List<ItemDrop.ItemData>();
#endif
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetMiscSlotsItems()
    {
#if !API
        return GetMiscSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
#else
        return new List<ItemDrop.ItemData>();
#endif
    }

    /// <summary>
    /// Returns amount of extra rows added to player available inventory
    /// </summary>
    public static int GetExtraRows()
    {
#if !API
        return ExtraRowsPlayer;
#else
        return 0;
#endif
    }

    /// <summary>
    /// Returns full height of inventory
    /// </summary>
    public static int GetInventoryHeight()
    {
#if !API
        return InventoryHeightFull;
#else
        return 0;
#endif
    }

    /// <summary>
    /// Returns if given position is extra slot
    /// </summary>
    /// <param name="gridPos">Position in inventory grid</param>
    public static bool IsGridPositionASlot(Vector2i gridPos)
    {
#if !API
        return Slots.IsGridPositionASlot(gridPos);
#else
        return false;
#endif
    }

    /// <summary>
    /// Returns if given item is in extra slot
    /// </summary>
    /// <param name="item"></param>
    public static bool IsItemInSlot(ItemDrop.ItemData item)
    {
#if !API
        return Slots.IsItemInSlot(item);
#else
        return false;
#endif
    }

    /// <summary>
    /// Returns if given item is in equipment slot
    /// </summary>
    /// <param name="item"></param>
    public static bool IsItemInEquipmentSlot(ItemDrop.ItemData item)
    {
#if !API
        return Slots.IsItemInEquipmentSlot(item);
#else
        return false;
#endif
    }

    /// <summary>
    /// Adds new custom equipment slot at first available position
    /// </summary>
    /// <param name="slotID">To add new slot ID should be unique. If given ID is not unique returns true if slot is already created</param>
    /// <param name="getName">function that return slot name how it should be seen in the UI. Localization is recommended.</param>
    /// <param name="itemIsValid">function to check of item fits the slot</param>
    /// <param name="isActive">function to check if slot should be available in equipment panel. If you need live update - call UpdateSlots.</param>
    /// <returns></returns>
    public static bool AddSlot(string slotID, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
    {
#if !API
        return CustomSlot.TryAddNewSlotWithIndex(slotID, slotIndex: -1, getName, itemIsValid, isActive);
#else
        return false;
#endif
    }

    /// <summary>
    /// Adds new custom equipment slot with set position
    /// </summary>
    /// <param name="slotID">To add new slot ID should be unique. If given ID is not unique returns true if slot is already created</param>
    /// <param name="slotIndex">-1 to take first available empty slot. Otherwise shift other slots to the right and insert into position after vanilla equipment slots</param>
    /// <param name="getName">function that return slot name how it should be seen in the UI. Localization is recommended.</param>
    /// <param name="itemIsValid">function to check of item fits the slot</param>
    /// <param name="isActive">function to check if slot should be available in equipment panel. If you need live update - call UpdateSlots.</param>
    /// <returns></returns>
    public static bool AddSlotWithIndex(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
    {
#if !API
        return CustomSlot.TryAddNewSlotWithIndex(slotID, slotIndex, getName, itemIsValid, isActive);
#else
        return false;
#endif
    }

    /// <summary>
    /// Adds new custom equipment slot with set position
    /// </summary>
    /// <param name="slotID">To add new slot ID should be unique. If given ID is not unique returns true if slot is already created</param>
    /// <param name="getName">function that return slot name how it should be seen in the UI. Localization is recommended.</param>
    /// <param name="itemIsValid">function to check of item fits the slot</param>
    /// <param name="isActive">function to check if slot should be available in equipment panel. If you need live update - call UpdateSlots.</param>
    /// <param name="slotIDs">slot IDs to add the slot before</param>
    /// <returns></returns>
    public static bool AddSlotBefore(string slotID, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive, params string[] slotIDs)
    {
#if !API
        return CustomSlot.TryAddNewSlotBefore(slotIDs, slotID, getName, itemIsValid, isActive);
#else
        return false;
#endif
    }

    /// <summary>
    /// Adds new custom equipment slot with set position
    /// </summary>
    /// <param name="slotID">To add new slot ID should be unique. If given ID is not unique returns true if slot is already created</param>
    /// <param name="getName">function that return slot name how it should be seen in the UI. Localization is recommended.</param>
    /// <param name="itemIsValid">function to check of item fits the slot</param>
    /// <param name="isActive">function to check if slot should be available in equipment panel. If you need live update - call UpdateSlots.</param>
    /// <param name="slotIDs">slot IDs after which the slot should be added</param>
    /// <returns></returns>
    public static bool AddSlotAfter(string slotID, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive, params string[] slotIDs)
    {
#if !API
        return CustomSlot.TryAddNewSlotAfter(slotIDs, slotID, getName, itemIsValid, isActive);
#else
        return false;
#endif
    }

    /// <summary>
    /// Tries to remove custom slot with given ID
    /// </summary>
    /// <param name="slotID"></param>
    public static bool RemoveSlot(string slotID)
    {
#if !API
        return CustomSlot.TryRemoveSlot(slotID);
#else
        return false;
#endif
    }

    /// <summary>
    /// Calls an update to slots layout and equipment panel
    /// Should be called if slot active state was changed to update panel
    /// </summary>
    public static void UpdateSlots()
    {
#if !API
        UpdateSlotsGridPosition();
        EquipmentPanel.UpdatePanel();
#endif
    }
}

[PublicAPI]
public class ExtraSlot
{
    internal Func<string> _id;
    internal Func<string> _name;
    internal Func<Vector2i> _gridPosition;
    internal Func<ItemDrop.ItemData> _item;
    internal Func<ItemDrop.ItemData, bool> _itemFits;
    internal Func<bool> _isActive;
    internal Func<bool> _isFree;
    internal Func<bool> _isHotkeySlot;
    internal Func<bool> _isEquipmentSlot;
    internal Func<bool> _isQuickSlot;
    internal Func<bool> _isMiscSlot;
    internal Func<bool> _isAmmoSlot;
    internal Func<bool> _isFoodSlot;
    internal Func<bool> _isCustomSlot;
    internal Func<bool> _isEmptySlot;

    public static readonly Vector2i emptyPosition = new Vector2i(-1, -1);

    public string ID => _id == null ? "" : _id();
    public string Name => _name == null ? "" : _name();
    public Vector2i GridPosition => _gridPosition == null ? emptyPosition : _gridPosition();
    public ItemDrop.ItemData Item => _item == null ? null : _item();
    public bool ItemFits(ItemDrop.ItemData item) => _itemFits != null && _itemFits(item);
    public bool IsActive => _isActive != null && _isActive();
    public bool IsFree => _isFree != null && _isFree();
    public bool IsHotkeySlot => _isHotkeySlot != null && _isHotkeySlot();
    public bool IsEquipmentSlot => _isEquipmentSlot != null && _isEquipmentSlot();
    public bool IsQuickSlot => _isQuickSlot != null && _isQuickSlot();
    public bool IsMiscSlot => _isMiscSlot != null && _isMiscSlot();
    public bool IsAmmoSlot => _isAmmoSlot != null && _isAmmoSlot();
    public bool IsFoodSlot => _isFoodSlot != null && _isFoodSlot();
    public bool IsCustomSlot => _isCustomSlot != null && _isCustomSlot();
    public bool IsEmptySlot => _isEmptySlot != null && _isEmptySlot();
}