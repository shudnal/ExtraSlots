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
        return Slots.ExtraRowsPlayer;
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
        return Slots.InventoryHeightFull;
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
        return GetSlotInGrid(gridPos) != null;
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
        return GetItemSlot(item) != null;
#else
        return false;
#endif
    }

    /// <summary>
    /// Adds new custom equipment slot
    /// </summary>
    /// <param name="slotID">To add new slot ID should be unique. If given ID is not unique returns true as like slot is already created</param>
    /// <param name="slotIndex"></param>
    /// <param name="getName"></param>
    /// <param name="itemIsValid"></param>
    /// <param name="isActive"></param>
    /// <returns></returns>
    public static bool AddSlot(string slotID, int slotIndex, Func<string> getName, Func<ItemDrop.ItemData, bool> itemIsValid, Func<bool> isActive)
    {
#if !API
        return CustomSlot.TryAddNewSlot(slotID, slotIndex, getName, itemIsValid, isActive);
#else
        return false;
#endif
    }

    public static bool RemoveSlot(string slotID)
    {
#if !API
        return CustomSlot.TryRemoveSlot(slotID);
#else
        return false;
#endif
    }
}
