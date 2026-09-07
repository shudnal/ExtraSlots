# 1.2.0
* fixed error spam and stale slot state when loading a character in a world with fewer inventory rows or available extra slots
* improved automatic recovery of items left in invalid, inactive, hidden, overlapping, or otherwise unavailable extra slot cells after slot or inventory layout changes
* tombstone auto-loot now simulates actual stacking and valid destination cells before taking everything, reducing partial recovery when inventory or slot availability changed after death
* fixed upgrading items in extra slots with a full regular inventory, preserving their slot and equipped state
* added an option to apply tombstone auto-equip settings after using the Take All button on an opened tombstone
* added an option to keep the equipped state of items that are kept on death
* tombstones now preserve their inventory dimensions across reloads when inventory size settings change before recovery
* optimized hotbar refreshes to reduce repeated UI work, especially with mods that decorate hotbars
* occupied custom slots now safely relocate their item or preserve it in deferred inventory when the slot is removed
* the equipment panel can now be repositioned by dragging and optionally snap to its default position or nearby inventory UI edges; hotbar positions remain configurable through offsets and anchors
* added an option, enabled by default, to fade the queued equip indicator as the current equip action progresses
* added independent options to keep empty available Quick, Ammo, and Food hotbar slots visible, each disabled by default
* fixed repeated hotbar element rebuilding when empty slots are shown and reduced idle queued-indicator work
* added deferred inventory recovery to prevent items from being lost when inventory topology changes and no valid slot is immediately available
* deferred items are preserved across character saves and can be recovered after running the character without ExtraSlots
* deferred items are moved into an automatically expanded tombstone on death when possible
* improved migration and recovery from EquipmentAndQuickSlots 2.x/3.x, ComfyQuickSlots and InventorySlots
* improved upgrade safety for items stored in ExtraSlots slots when inventory topology changes during crafting
* added a compatibility option to disable Inventory.Changed batching when diagnosing interactions with other inventory mods
* external localization overrides are now intentionally loaded only from BepInEx/config
* fixed extra-slot backups merging separate stacks and incorrectly restoring additional items on later loads
* protected slot items are now restored even when capacity checks, Stack All, or SimpleSort are interrupted by another mod
* fixed custom slot insertion IDs and AddSlotAfter ordering, and preserved the removed slot return address during recovery
* recovered backup equipment is now shown immediately on the character selection preview before entering a world

# 1.1.21
* updated Epic Loot compatibility for legacy versions and the 0.13.0+ API, including magic effects, set bonuses, and sacrifice filtering in extra slots

# 1.1.20
* some configs made unconditionally server controlled

# 1.1.19
* hotkeys handling optimizations and fixes
* migrated configuration synchronization from ServerSync to ConditionalConfigSync
* server administrators can override the ownership of policy-controlled settings through ConditionalConfigSync policy

# 1.1.18
* new config "Custom slot items can use regular equipment slots", disabled by default. Custom items go into custom slots only by default.

# 1.1.17
* improved items in extra slots from being lost of moved into a grave on death in certain scenarios

# 1.1.16
* fixed compatibility with balrond_Runeforging and potentially other mods changing inventory items visuals
* minor optimizations

# 1.1.15
* fixed lost compatibility with PlantEasily overriding gamepad input

# 1.1.14
* fixed on death issue and upgraded code for extra utility slots

# 1.1.13
* fixed custom item lists in several configs not applied initially if set up with prefab names
* minor optimizations here and there

# 1.1.12
* fixed rare issue with this mod allowing adding to an extra slot an item that was declined by another mod

# 1.1.11
* hotbar visibility for quick, ammo, and food slots is now server-synced

# 1.1.10
* fixed hotkeys issues, final optimizations

# 1.1.9
* minor optimizations to hotkey usage and similar hotkey prevention

# 1.1.8
* fixed ZenBeehive compat
* minor fixes

# 1.1.7
* patch 0.221.10

# 1.1.6
* fixed hovers for custom slot items making backpacks unusable in certain circumstances

# 1.1.5
* changed behaviour of "fixed item list" in "Death tweaks" section to always keep configured items regardless of group configs "Keep items at X slots".

# 1.1.4
* config SyncEquipmentSlots changed to SyncOtherModsSettings with expanded functionality
* some death tweaks

# 1.1.3
* equipped item used in craft will be equipped back

# 1.1.2
* added identification of slots with similar hotkeys (if several slots have same hotkey only one will be used if config "Use only one hotkey item" is on)

# 1.1.1
* added option to use only one item for pressed hotkey
* added option to prevent changing equipped item from regular inventory when there is an equipped item in slot
* item dragged from extra slot to regular inventory as the same item will return back to slot
* misc slots now accept equipped items (wishbone, demister, etc)
* multiple Equipped Status Effects are supported in custom slots (except for Extra Utility slots)
* fixed slot custom config items not applying properly at loading

# 1.1.0
* ItemDataManager removed as dependency
* ServerSync removed as dependency
* conditional configs (from mods with ConditionalConfigSync) are local or server defined by policy file, policies are found in ConditionalConfigSync.yml

# 1.0.55
* fixed custom named item equip key hint display
* dropped ConditionalConfigSync library in favor of item customData
* custom equipment slot names are localized
* Extra Utility config moved in config sections
* ItemDataManager integration replaced with ItemData customData integration
* extra items rearrangement after amount of items change
* Extra utility, helmet, cape, chest, legs, trinket and custom slots items are now added to trophy list for game to register the texture for player profile outside current world
* added support for [conditionalconfigsync](https://thunderstore.io/c/valheim/p/shudnal/ConditionalConfigSync/) as optional dependency with config sync mode policies

# 1.0.54
* extra utility slots amount increased up to 8
* project compiled with .NET Framework 4.8

# 1.0.53
* Epic Loot 0.11.4 dropped legacy compatibility type `ExtendedItemDataFramework` in favor of `ItemData` directly

# 1.0.52
* added full equipped unique keys support of Epic Loot

# 1.0.51
* fixed internal errors

# 1.0.50
* fixed hotbar anchoring to lower right corner
* added alternative option to set regular rows amount
* added panels alignment and free space configs
* item position in slot is now defaulted to taken slot position
* prevented dropping equipped item to regular inventory if it is in equipment slot
* stack all prevention for regular hotbar items
* fixed equipment slots tooltips not updating correctly

# 1.0.49
* added option to make vanilla-like item weight discounts for equipped items and other slot groups
* added option to make extra inventory row contain extra slots (to keep inventory slots hidden)
* added item positions inventory backup to restore slot items if you run game without ExtraSlots mod and those items get deleted
* removed AzuEPI compatibility until codebase redesign

# 1.0.48
* extra rows amount can now be set up to -3 to reduce regular inventory size
* new configs for regular inventory rows progression
* items in extra utility slots made visible on player (disableable in config)

# 1.0.47
* fixed extra slots panels (especially tooltips and item dragging) shifted in certain cases when crafting panel is on the left
* config to sync extra slots with other mods
* fixed custom equipment slot items visibility when config is disabled

# 1.0.46
* added new config "Custom slot items could go into regular equipment slots" disabled by default
* fixed possible issue with not picking up previously unknown item into quick slot
* list changes now applied instantly in game and consistently between config managers

# 1.0.45
* automatic grid width adjustment to visible slots
* custom config list saving/loading made culture invariant

# 1.0.44
* fixed plugin compatibility

# 1.0.43
* equipment panel dragging is limited by main inventory screen rect
* fixed custom slot items returned from other inventories in case of not placing them to custom slot with regular inventory filled

# 1.0.42
* added drag-and-drop equipment panel offsets
* added option to prevent slots dragging from UI when item is equipped

# 1.0.41
* added option to set custom Equipment slot items with item list or customData filters

# 1.0.40
* added API for custom slots
* added gamepad slots navigation

# 1.0.39
* added server synced config to disable custom slots

# 1.0.38
* fixed ArmorStand gear interactions with custom slots
* fixed tooltips for equipment slots while using controller

# 1.0.37
* extra slot amount increase to 16

# 1.0.36
* left and right equipment slot panel positioning
* optional equipment panel offset control
* added icons to custom slots

# 1.0.35
* added item quality overlay in equipment slots

# 1.0.34
* switched mod network version check to CSync

# 1.0.33
* fixed custom slots positions after load

# 1.0.32
* added custom slot active control
* item can be placed into an empty custom slot manually regardless of item filters

# 1.0.31
* custom slot API

# 1.0.30
* moved equipment slots to separate rows by default
* added config to hide arrows and equipped weapon/shield slots from equipment panel
* readme rewrite

# 1.0.29
* added support for ExtendedPlayerInventory and aedenthorn's EquipMultipleUtilityItems
* players without mod installed should now see extra items in Tombstone on interaction
* Czech translation added
* custom item lists for food and ammo slot items (by default includes items required to summon bosses)
* vanilla slot order and Unique utility items configs now use custom config drawers to easier format handling

# 1.0.28
* custom item list for misc slot items (by default includes items required to summon bosses)

# 1.0.27
* added no-item-drop mode for death

# 1.0.26
* equipment slots custom rows order

# 1.0.25
* added custom equipment slots

# 1.0.24
* fixed ancient bark stacking

# 1.0.23
* added external config for ammo slot items

# 1.0.22
* config sync refactor

# 1.0.21
* compatibility fixes

# 1.0.20
* inventory hotkeys prevent game hotkeys conflicts

# 1.0.19
* food and ammo slots visual updates

# 1.0.18
* extended equipment slots features

# 1.0.17
* fixed inventory in other mods compatibility

# 1.0.16
* fixed on death inventory slotting

# 1.0.15
* item slots custom config

# 1.0.14
* extra slot display changes

# 1.0.13
* misc slots fixes

# 1.0.12
* slots amount config

# 1.0.11
* hotbar first release

# 1.0.10
* added food slots

# 1.0.9
* added ammo slots

# 1.0.8
* added quick slots

# 1.0.7
* added equipment slot panel

# 1.0.6
* inventory size config

# 1.0.5
* compatibility updates

# 1.0.4
* initial inventory extension

# 1.0.3
* release setup

# 1.0.2
* initial public build

# 1.0.1
* package fixes

# 1.0.0
* initial release