# Hotbar compatibility

## Contract in 1.2.18

ExtraSlots Quick, Ammo and Food bars retain real inventory `ItemDrop.ItemData` references in `HotkeyBar.m_items`. Rendering does not clone items, move items, alter `m_gridPos`, or write item custom data.

While an extra bar is rendered for a living local player, `m_elements` is a full-width table indexed by the inventory column. Every position has its own vanilla `ElementData`, GameObject and normal children. For an item represented by the bar, `bar.m_elements[item.m_gridPos.x]` is its actual visual element. This remains true for third-party postfixes and outside the render call; it does not depend on a temporary coordinate projection or a particular postfix order.

Unused columns are complete inactive elements, not null entries or aliases of other elements. Their inactive roots hide overlay children too. `m_elements.Count` is therefore not the number of visible or navigable cells. Death clears the table through the usual game path; recreation follows on the next valid update. A changed inventory width can rebuild the table.

At inventory width 8, Food elements occupy indices 3, 4 and 5 but are laid out as the first, second and third cells on screen. Columns 0, 1, 2, 6 and 7 are hidden padding. A food item in column 5 stays at that column and is displayed by element 5, not element 2.

## Display and input

The registered slot order determines screen position, custom labels, row wrapping and fill direction. It is separate from physical collection indices. Empty active slots before the last occupied slot remain visible. `Always show empty slots` also shows trailing active slots; inactive slots and padding are never selectable.

`m_selected` remains an index into `m_elements`. The controller moves it through visible columns in registered slot order, including layouts that wrap across an inventory row. Touch clicks and queued-item indicators use the same column-to-slot mapping. Existing public `GetItemInSlot` methods on the three bar classes still take a logical slot ordinal; they are not element-index APIs.

`Hide hotbar` remains independent for each panel. Hidden bars leave rendering and gamepad bar selection; direct slot shortcuts keep their existing behavior and depend on `Enabled`, not visibility.

## Integration API

Use `ExtraSlots.API.TryGetHotbarElement(bar, item, out element)` to resolve an existing element on a vanilla or ExtraSlots bar. It returns false when the bar is unsupported, the item is no longer represented, or the element is unavailable. It does not create UI or alter the inventory. Calls should run on the Unity thread, normally from a completed `UpdateIcons` postfix. Do not retain an element across a rebuild or HUD destruction.

The ordinary patched `HotkeyBar.UpdateIcons` still runs, including other mods' overlays. ExtraSlots only substitutes the item source for its own bars and ensures the full-width element table. It no longer replaces inventory-column reads in the game method. No PortablePals-specific patches or dependencies are added.

This contract does not make arbitrary vanilla-only assumptions valid. A patch that rereads row 0 from the player's inventory instead of using `bar.m_items`, assumes every element is visible, or treats an extra bar's selected column as a vanilla hotkey action still needs its own integration. Two slots in one bar cannot share an inventory column: such a layout is rejected with the existing rate-limited diagnostic rather than aliasing items or moving them.

## Manual verification

The mod has not been built or run as part of this source change. Check the following in game:

- With PortablePals 0.3.2 item overlays enabled, fill each Food slot individually, then all three. Verify vanilla and modded food, plus a PalStone in Quick, without exceptions or misplaced badges.
- Check a lone item in the last slot, gaps, empty panels, progression-disabled slots and both `Always show empty slots` values. Padding must not appear or intercept pointer input.
- Navigate left and right across all bars with a gamepad, including backward entry into Food and wrapping from its last slot. Use the selected item and verify that a vanilla hotbar item is not used instead. Check touch selection too.
- Change row width, fill direction, element spacing, scale and offsets; switch input layouts and confirm custom labels remain on the correct cells.
- Toggle each `Hide hotbar`, including hiding all three; verify direct shortcuts still work when `Enabled` stays on. Restore visibility, die and respawn, reconnect and recreate the HUD.
- With a changed inventory width, verify physical indices and visual order independently, especially when a group's columns wrap across an inventory row. Verify native and other mods' bars retain their existing behavior.
