using System;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.Slots;

namespace ExtraSlots;

public static class API
{
    /// <summary>
    /// Returns list of all slots
    /// </summary>
    public static List<Slot> GetExtraSlots()
    {
        return slots.ToList();
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<Slot> GetEquipmentSlots()
    {
        return slots.Where(slot => slot.IsEquipmentSlot).ToList();
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<Slot> GetQuickSlots()
    {
        return slots.Where(slot => slot.IsQuickSlot).ToList();
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<Slot> GetFoodSlots()
    {
        return slots.Where(slot => slot.IsFoodSlot).ToList();
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<Slot> GetAmmoSlots()
    {
        return slots.Where(slot => slot.IsAmmoSlot).ToList();
    }

    /// <summary>
    /// Returns list of corresponding slots
    /// </summary>
    public static List<Slot> GetMiscSlots()
    {
        return slots.Where(slot => slot.IsMiscSlot).ToList();
    }

    /// <summary>
    /// Returns slot with given ID
    /// </summary>
    /// <param name="slotID"></param>
    public static Slot FindSlot(string slotID)
    {
        return slots.Where(slot => slot.ID == slotID || slot.ID == CustomSlot.GetSlotID(slotID)).FirstOrDefault();
    }

    /// <summary>
    /// Returns list of items located in extra slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetAllExtraSlotsItems()
    {
        return slots.Select(slot => slot.Item).Where(item => item != null).ToList();
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetEquipmentSlotsItems()
    {
        return GetEquipmentSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetQuickSlotsItems()
    {
        return GetQuickSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetFoodSlotsItems()
    {
        return GetFoodSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetAmmoSlotsItems()
    {
        return GetAmmoSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
    }

    /// <summary>
    /// Returns list of items located in corresponding slots
    /// </summary>
    /// <returns>List of not null ItemDrop.ItemData</returns>
    public static List<ItemDrop.ItemData> GetMiscSlotsItems()
    {
        return GetMiscSlots().Select(slot => slot.Item).Where(item => item != null).ToList();
    }

    /// <summary>
    /// Returns amount of extra rows added to player available inventory
    /// </summary>
    public static int GetExtraRows()
    {
        return ExtraRowsPlayer;
    }

    /// <summary>
    /// Returns full height of inventory
    /// </summary>
    public static int GetInventoryHeightFull()
    {
        return InventoryHeightFull;
    }

    /// <summary>
    /// Returns full height of inventory
    /// </summary>
    public static int GetInventoryHeightPlayer()
    {
        return InventoryHeightPlayer;
    }

    /// <summary>
    /// Returns if given position is extra slot
    /// </summary>
    /// <param name="gridPos">Position in inventory grid</param>
    public static bool IsGridPositionASlot(Vector2i gridPos)
    {
        return Slots.IsGridPositionASlot(gridPos);
    }

    /// <summary>
    /// Returns if given item is in extra slot
    /// </summary>
    /// <param name="item"></param>
    public static bool IsItemInSlot(ItemDrop.ItemData item)
    {
        return Slots.IsItemInSlot(item);
    }

    /// <summary>
    /// Returns if given item is in equipment slot
    /// </summary>
    /// <param name="item"></param>
    public static bool IsItemInEquipmentSlot(ItemDrop.ItemData item)
    {
        return Slots.IsItemInEquipmentSlot(item);
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
        return CustomSlot.TryAddNewSlotWithIndex(slotID, slotIndex: -1, getName, itemIsValid, isActive);
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
        return CustomSlot.TryAddNewSlotWithIndex(slotID, slotIndex, getName, itemIsValid, isActive);
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
        return CustomSlot.TryAddNewSlotBefore(slotIDs, slotID, getName, itemIsValid, isActive);
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
        return CustomSlot.TryAddNewSlotAfter(slotIDs, slotID, getName, itemIsValid, isActive);
    }

    /// <summary>
    /// Tries to remove custom slot with given ID
    /// </summary>
    /// <param name="slotID"></param>
    public static bool RemoveSlot(string slotID)
    {
        return CustomSlot.TryRemoveSlot(slotID);
    }

    /// <summary>
    /// Calls an update to slots layout and equipment panel
    /// Should be called if slot active state was changed to update panel
    /// </summary>
    public static void UpdateSlots()
    {
        UpdateSlotsGridPosition();
        EquipmentPanel.UpdatePanel();
    }
}