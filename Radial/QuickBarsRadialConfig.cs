using System;
using System.Collections.Generic;
using UnityEngine;
using Valheim.UI;
using Object = UnityEngine.Object;
using Slot = ExtraSlots.Slots.Slot;

namespace ExtraSlots.Radial;

// A separate native radial page. It never reads or changes HUD hotbar visibility.
internal sealed class QuickBarsRadialConfig : IRadialConfig
{
    internal const string NameToken = "$exsl_radial_quick_bars";
    internal const int ElementsPerLayer = 12;

    // Twelve entries plus four quarter-cell group gaps fill one circle. The shader
    // accepts a segment count independently of the radial's uniform input grid.
    private const int ShapeSegments = 13;
    private const float CellAngle = 360f / ShapeSegments;
    private const float GroupGap = CellAngle / 4f;
    private const float FirstRightAngle = CellAngle + GroupGap;

    private sealed class Cell
    {
        internal readonly Slot Slot;
        internal readonly float Angle;
        internal readonly RadialMenuElement Element;

        internal Cell(Slot slot, float angle, RadialMenuElement element)
        {
            Slot = slot;
            Angle = angle;
            Element = element;
        }
    }

    private sealed class SlotSnapshot
    {
        internal readonly Slot[] Slots;
        internal readonly ItemDrop.ItemData[] Items;
        internal readonly bool[] Active;
        internal bool AnyActive { get; private set; }

        internal SlotSnapshot(Slot[] slots)
        {
            Slots = slots;
            Items = new ItemDrop.ItemData[slots.Length];
            Active = new bool[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                Active[i] = slots[i]?.IsActive == true;
                if (!Active[i])
                    continue;
                AnyActive = true;
                Items[i] = slots[i].Item;
            }
        }

        internal bool IsCurrent()
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Slot slot = Slots[i];
                if (slot == null)
                    continue;
                if (slot.Index < 0 || slot.Index >= global::ExtraSlots.Slots.slots.Length
                    || !ReferenceEquals(global::ExtraSlots.Slots.slots[slot.Index], slot)
                    || slot.IsActive != Active[i]
                    || !ReferenceEquals(Active[i] ? slot.Item : null, Items[i]))
                    return false;
            }
            return true;
        }
    }

    private readonly List<Cell> cells = new List<Cell>(ElementsPerLayer);
    private SlotSnapshot food;
    private SlotSnapshot ammo;
    private SlotSnapshot quick;
    private Inventory inventory;
    private Player player;

    public string LocalizedName => Localization.instance.Localize(NameToken);
    public Sprite Sprite => RadialData.SO.HotbarGroupConfig.Sprite;

    internal static bool HasAvailableSlots()
    {
        if (!Player.m_localPlayer || Player.m_localPlayer.IsDead() || Slots.PlayerInventory == null)
            return false;
        foreach (Slot slot in Slots.slots)
            if ((slot.IsQuickSlot || slot.IsFoodSlot || slot.IsAmmoSlot) && slot.IsActive)
                return true;
        return false;
    }

    internal bool NeedsRefresh => !ReferenceEquals(player, Player.m_localPlayer)
        || !ReferenceEquals(inventory, Slots.PlayerInventory)
        || food == null || !food.IsCurrent() || !ammo.IsCurrent() || !quick.IsCurrent();

    public void InitRadialConfig(RadialBase radial)
    {
        Slot selectedSlot = radial.StartItemIndex >= 0 && radial.StartItemIndex < cells.Count
            ? cells[radial.StartItemIndex].Slot : null;
        cells.Clear();
        player = Player.m_localPlayer;
        inventory = player ? player.GetInventory() : null;
        if (inventory == null)
        {
            radial.QueuedClose();
            return;
        }

        food = new SlotSnapshot(Slots.GetFoodSlots());
        ammo = new SlotSnapshot(Slots.GetAmmoSlots());
        quick = new SlotSnapshot(Slots.GetQuickSlots());
        List<RadialMenuElement> elements = new List<RadialMenuElement>(ElementsPerLayer);
        bool handedToRadial = false;
        try
        {
            BackElement back = Object.Instantiate(RadialData.SO.BackElement);
            elements.Add(back);
            back.Init(radial);
            cells.Add(new Cell(null, 0f, back));

            bool bothSides = food.AnyActive && ammo.AnyActive;
            if (bothSides)
            {
                AddSide(elements, food, left: true);
                AddSide(elements, ammo, left: false);
            }
            else if (food.AnyActive || ammo.AnyActive)
            {
                AddSide(elements, food.AnyActive ? food : ammo, left: true);
            }

            int limit = Math.Min(bothSides ? 5 : 6, quick.Slots.Length);
            int occupied = 0;
            for (int i = 0; i < limit; i++)
                if (quick.Active[i] && quick.Items[i] != null)
                    occupied++;

            int order = 0;
            for (int i = 0; i < limit; i++)
            {
                if (!quick.Active[i] || quick.Items[i] == null)
                    continue;
                float angle = bothSides
                    ? 180f + (order - (occupied - 1) / 2f) * CellAngle
                    : FirstRightAngle + order * CellAngle;
                AddSlot(elements, quick.Slots[i], quick.Items[i], angle);
                order++;
            }

            radial.StartOffset = 0;
            // Keep the same slot after a live refresh, not an index that may now hold another item.
            radial.StartItemIndex = selectedSlot == null ? 0 : Math.Max(0, cells.FindIndex(cell => ReferenceEquals(cell.Slot, selectedSlot)));
            handedToRadial = true;
            radial.ConstructRadial(elements);
        }
        finally
        {
            if (!handedToRadial)
                foreach (RadialMenuElement element in elements)
                    if (element)
                        Object.Destroy(element.gameObject);
        }
    }

    private void AddSide(List<RadialMenuElement> elements, SlotSnapshot group, bool left)
    {
        // Retain all three positions, including empty or progression-locked cells.
        for (int i = 0; i < Math.Min(3, group.Slots.Length); i++)
        {
            float angle = FirstRightAngle + i * CellAngle;
            AddSlot(elements, group.Slots[i], group.Items[i], left ? 360f - angle : angle);
        }
    }

    private void AddSlot(List<RadialMenuElement> elements, Slot slot, ItemDrop.ItemData item, float angle)
    {
        RadialMenuElement element;
        if (item == null)
        {
            EmptyElement empty = Object.Instantiate(RadialData.SO.EmptyElement);
            elements.Add(empty);
            empty.Init();
            empty.Name = slot?.Name ?? string.Empty;
            element = empty;
        }
        else
        {
            ItemElement itemElement = Object.Instantiate(RadialData.SO.ItemElement);
            elements.Add(itemElement);
            itemElement.Init(item);
            Func<bool> interact = itemElement.Interact;
            Func<bool> secondaryInteract = itemElement.SecondaryInteract;
            Func<RadialBase, int, bool> openSubRadial = itemElement.TryOpenSubRadial;
            // An item can move or disappear while this page (or LastUsed) holds its reference.
            // Never act on a replacement item or a detached ItemData from a previous character.
            itemElement.Interact = () => IsCurrentItem(slot, item) && interact?.Invoke() == true;
            itemElement.SecondaryInteract = () => IsCurrentItem(slot, item) && secondaryInteract?.Invoke() == true;
            itemElement.TryOpenSubRadial = (menu, index) => IsCurrentItem(slot, item) && openSubRadial?.Invoke(menu, index) == true;
            itemElement.CloseOnInteract = () => true;
            itemElement.AdvancedCloseOnInteract = null;
            element = itemElement;
        }
        cells.Add(new Cell(slot, angle, element));
    }

    private bool IsCurrentItem(Slot slot, ItemDrop.ItemData item) => player && player == Player.m_localPlayer
        && !player.IsDead() && slot?.IsActive == true && ReferenceEquals(slot.Item, item)
        && ReferenceEquals(player.GetInventory(), inventory) && inventory.ContainsItem(item);

    internal void ApplyLayout(RadialBase radial)
    {
        if (!HasConstructedElements(radial))
            return;
        foreach (Cell cell in cells)
        {
            cell.Element.LocalPosition = UIMath.AngleToDirection(cell.Angle) * RadialData.SO.ElementsDistance;
            cell.Element.SetSegment(cell.Angle / 360f, ShapeSegments);
            if (cell.Element is BackElement)
                cell.Element.ElementTransform.GetChild(1).localRotation = UIMath.DirectionToRotation(cell.Element.LocalPosition.normalized);
        }
    }

    private bool HasConstructedElements(RadialBase radial)
    {
        if (radial.m_elements == null || radial.m_elements.Count != cells.Count)
            return false;
        for (int i = 0; i < cells.Count; i++)
            if (!ReferenceEquals(radial.m_elements.GetElement(i), cells[i].Element))
                return false;
        return true;
    }

    internal bool SelectStart(RadialBase radial)
    {
        if (!HasConstructedElements(radial))
            return false;
        int index = radial.StartItemIndex >= 0 && radial.StartItemIndex < cells.Count ? radial.StartItemIndex : 0;
        radial.m_currentLayer = 0;
        radial.m_cursorLocked = false;
        radial.m_previousClosestPoint = radial.m_closestPoint = index;
        radial.m_index = index;
        radial.m_previousCursorTarget = radial.m_cursorTarget = cells[index].Angle;
        radial.UpdateSelection();
        radial.UpdateCursorPosition(instant: true);
        radial.StartItemIndex = -1;
        return true;
    }

    internal bool UpdateCursor(RadialBase radial, Vector2 input)
    {
        if (!HasConstructedElements(radial))
            return false;

        radial.m_cursor.gameObject.SetActive(true);
        radial.m_inputAngle = UIMath.DirectionToAngleDegrees(input.normalized);
        int index = -1;
        float bestDistance = CellAngle / 2f;
        for (int i = 0; i < cells.Count; i++)
        {
            float distance = Mathf.Abs(Mathf.DeltaAngle(radial.m_inputAngle, cells[i].Angle));
            if (distance <= bestDistance)
            {
                index = i;
                bestDistance = distance;
            }
        }

        // Use the same angular bounds for drawing and selection. A group gap has no
        // selectable item, including when moving directly across it with a gamepad.
        radial.m_inputDirection = UIMath.RadialDirection(radial.m_previousInput.normalized, input.normalized);
        radial.m_previousClosestPoint = radial.m_closestPoint;
        radial.m_closestPoint = index;
        radial.m_index = index;
        radial.UpdateSelection();
        radial.m_previousCursorTarget = radial.m_cursorTarget;
        radial.m_cursorTarget = index >= 0 ? cells[index].Angle : radial.m_inputAngle;
        if (!Mathf.Approximately(radial.m_previousCursorTarget, radial.m_cursorTarget))
            radial.UpdateCursorPosition();
        return true;
    }
}
