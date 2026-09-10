# Valheim 1.0.7 inventory integration contracts

Game source: `shudnal/assemblies_combined/master` at
`02f009229ca3d045153d8e9b3a516af6cf68c158`.

This source review supplements PR #53. It does not establish compilation,
Harmony patch application, or runtime correctness. The mod was not built or run.

## Transport records are not gameplay items

`Inventory(bool _)` always sets `m_temoraryInventory`, including when passed
`false`. The four-argument constructor creates a normal, materializing inventory.
`ZDOMan.ConvertInventories` uses the boolean constructor to convert old container
strings into byte-array saves without instantiating gameplay items.

`Inventory.AddTempItem` fills serialized fields and `m_dropPrefab` on a new
`ItemData`, but leaves `m_shared` null. The resulting records are valid inputs to
native serialization. Do not remove them, globally filter `GetAllItems`, set
`m_shared` from a prefab opportunistically, or adopt them into player deferred
storage just because they lack runtime data.

ExtraSlots must leave native temporary inventory loading and saving intact. Its
positional AddItem hooks return before topology, classification, and diagnostic
formatting. The load context is scoped to the actual inventory: a nested load
suspends and later restores the outer context; a temporary load cannot activate
player recovery or container resizing.

`GetAllItems()` returns the list without reading SharedData. Grid-order sorting,
coordinates, stack counts, and equipped flags can also be read without SharedData.
Name/type filters, weight, icons, durability limits, equipment logic, and slot
validators require a materialized item. Guard the consumer, not the native list.
Diagnostic formatting must be safe even before a disabled logger is invoked.

## Runtime adoption boundaries

Slot classifiers and `Slot.ItemFits` reject `item?.m_shared == null` before
calling custom validators. Direct player insertion, fit simulation, and extra
utility assignment similarly reject incomplete records. These checks do not
cancel native transport AddItem calls or remove source data.

Plugin-owned recovery readers use normal four-argument inventories. The shared
reader rejects a temporary destination and incomplete returned records before
migration, preview projection, or source consumption. Missing-prefab counts
remain the responsibility of the existing source-retention checks; they are not
silently treated as a successful complete recovery.

BBH quiver lookup excludes temporary inventories and incomplete items. The cache
is tied to the player reference and elapsed time, not an exact modulo boundary.
A temporary provider result is not cached. An absent native ammo result must not
be treated as a match for an absent equipped-ammo item.

## Recovery transactions and nested calls

A manual recovery merge owns snapshots of each modified resident stack and its
cheated flag. Every incomplete or exceptional tentative merge restores those
snapshots and the original detached stack. A provider-supplied stack must still
belong to the destination inventory and match name, quality, and world level.
Only a completed merge publishes its change notification. Deferred source
consumption and final persistence retain their existing caller-level batching.

Finalizers restore a call context only when its prefix actually opened that
context. A foreign prefix may skip another mutating prefix while postfixes and
finalizers still execute. In particular, a skipped nested creation/crafting call
must not clear an active outer replacement context. Pickup and weight scopes use
an optional snapshot to distinguish a skipped prefix from a captured false flag.

## Extra utility equipping

Do not change `SharedData.m_itemType` to a synthetic type to bypass the native
utility branch. SharedData may be shared by multiple instances; a postfix also
must not force success after the original rejected broken, unavailable, low-world-
level, or otherwise ineligible equipment.

Redirect only the native `m_utilityItem` load/store inside `Humanoid.EquipItem`
to the selected extra slot. The native eligibility checks, return value,
unequipping, effects, equipment setup, and final equipped flag remain in place.
The redirect is scoped to the exact humanoid and item, nests explicitly, and is
restored on exceptions. The transpiler requires the reviewed one-load/one-store
layout instead of silently applying an uncertain transformation.

## Reviewed integration paths

The repeat pass traced the game's constructors, AddTempItem/AddItem overloads,
Load/Save/Changed, ZDO inventory conversion, container persistence, tombstone
interaction/fit/despawn, and native equipment checks against the mod consumers.
The mod scan included all C# files for inventory construction, item enumeration,
SharedData consumers, and patch contexts. Detailed reasoning concentrated on the
entry points above, recovery/migration, player topology, and their callers; this
is not an exhaustive audit of every game system or third-party mod.

The final tree retains the existing StackAll implementation and all hotbar
sources unchanged. No hotbar dragging or deferred-only grave creation is added.
`Container.Save` remains an explicit write; the native `m_loading` guard belongs
to `OnContainerChanged`, not to Save itself. No change is needed for the previously
rejected review findings based on the opposite assumption.

## Local runtime verification

- Convert a disposable pre-1.0 world with slotted items outside a 4x4 grid, with
  debug logging both enabled and disabled, on client and dedicated server.
- Verify temporary Load/Save keeps all transport records and their coordinates;
  normal recovery, missing-prefab retention, backup, and preview still work.
- Exercise nested loads into different inventories and rejected nested crafting
  calls; the outer operation must retain its exact context and item provenance.
- Interrupt a recovery merge after its first resident stack, then retry. Check
  exact quantities and cheated flags, without duplication or loss.
- Query a BBH quiver with no equipped ammunition, after character changes, and
  without queries at exact five-second boundaries. Include a temporary provider
  result followed by a normal inventory.
- Equip extra utilities during native refusal conditions and normal replacement.
  Verify main utility retention, unique groups, effects, result, and unchanged
  shared item types; check nested calls, foreign vetoes, and exception cleanup.
- Recheck full-inventory upgrades, grave ownership transfer/reload, dynamic slot
  changes, protected StackAll items, and full/empty hotbars with the existing PR
  runtime checklist. Source parsing is not a substitute for these checks.
