# 1.0.43
* minor fixes

# 1.0.42
* fixed occasional error on loading

# 1.0.41
* fixed invisible element prevented drag'n'droping item out of inventory

# 1.0.40
* fixed binding of gamepad buttons to slots hotkeys (apparently it worked with some controllers)

# 1.0.39
* Wrong format hotkeys will be clear automatically to prevent issues. Use a configuration manager to set up hotkeys.
* fixed rare issue with incorrect item in items collection

# 1.0.38
* new config in Mods compatibility to rebind F2 (open connect panel)

# 1.0.37
* final SimpleSort compatibility

# 1.0.36
* fixed items lose on death inside a dungeon
* further SimpleSort compatibility (post sorting weight calculations)

# 1.0.35
* SimpleSort compatibility (exclude extra slots from sorting)
* Recycle_N_Reclaim compatibility (ignore hotbar config ignores extra slots items)

# 1.0.34
* Call To Arms patch (trinket slot)
* BowsBeforeHoes arrow finding and counting compatibility
* Bombs and throwables now also go to ammo slot

# 1.0.33
* compatibility with ZenBeehive
* minor optimization

# 1.0.32
* Quick Stack Store Sort Trash Restock mod compatibility for restock of quick, misc, food and ammo slots
* fix for error on mod initialization when language has not been set explicitly
* thunderstore version now has YamlDotNet as dependency

# 1.0.31
* another attempt at better ValheimPlus inventory rows count compatibility
* fixed hotbars cycling when alternative placing is up

# 1.0.30
* fixed duping things after death

# 1.0.29
* players without mod installed should now see extra items in Tombstone on interaction
* Czech translation added
* custom item lists for food and ammo slot items (you can now add bombs to ammo slots)

# 1.0.28
* custom item list for misc slot items (by default includes items required to summon bosses)
* vanilla slot order and Unique utility items configs now use custom config drawers to easier format handling

# 1.0.27
* Polish translation refined
* ServerSync updated
* more compat for mods altering original hotkey bar

# 1.0.26
* patch 0.220.3
* minor performance improvements

# 1.0.25
* Ukrainian translation
* fix for rare error related to equipment effects

# 1.0.24
* hotbars now has configurable anchor to make it work the same on clients with different resolutions and GUI scales (if you moved panels you may need to reposition it again)
* new config options to change weight of items in corresponding slots

# 1.0.23
* new config option to prevent auto pickup items to extra slots

# 1.0.22
* more compatibility for Valheim+ inventory

# 1.0.21
* fixed Backpacks and BBH's Quiver pushing some items to quick slots on player load

# 1.0.20
* new configs for slot groups to prevent "Stack All" from pulling items from configured slot types
* new config to prevent "Stack All" from pulling items from hotbar
* rare issue: some outdated custom meads now will properly go into Food slots
* default hotkeys for Food slots now: Alt + Q, Alt + E, Alt + R

# 1.0.19
* more intuitive handling for similar hotkeys use in quick bars

# 1.0.18
* new config option for stack size Color of slots in equipment panel
* quick slots hotbars will no longer overlap map window

# 1.0.17
* new config option to use several hotbar items at once
* changed the logic of checking quick slots and utility slots activity. If only one of the values ​​(global key or discovered item) is specified - checking for the unfilled one will not be performed
* keyboard shortcuts will be ordered in similar manner to prevent potential issues with button order and to be more homogenous for similar hotkeys usage

# 1.0.16
* ValheimPlus multiplayer compatibility
* tooltip names format for hotkey slots made configurable

# 1.0.15
* AzuAutoStore compatibility to ignore items in extra slots
* Quick Stack Store Sort Trash Restock compatibility to ignore items in extra slots

# 1.0.14
* ok fine there is food slots hotbar now
* and you can place potions in food slots
* hotbar gamepad selection will now properly cycle through hotbars in order (top -> bottom, left -> right) if hotbars were repositioned
* PlantEasily gamepad double selection fixed
* MagicPlugin custom slot compatibility

# 1.0.13
* ammo and quickslots hotbar made more configurable
* options to keep items in slots after death (compatible with Death Tweaks)

# 1.0.12
* fix for error on new character creation

# 1.0.11
* Chinese translations refined
* BetterProgression compatibility
* Valheim Enchantment System compatibility
* Inventory rows amount can now be changed ingame without issues
* Inventory rows obtaining progression
* Fix for spamming error on tooltip

# 1.0.10
* german translation refined
* new option to hide stack size in hot bars

# 1.0.9
* EpicLoot enchantments will work at any custom slot
* EpicLoot support for ignoring sacrifice of hotbar items (quickslots and misc slots could be excluded as well)

# 1.0.8
* new API methods
* translations fixed
* mod name in translation section fixed (it lacked space)
* Backpacks compatibility
* Extra Slots Custom Slots mod support

# 1.0.7
* fixed an issue where you could not drag unequipped item in slot

# 1.0.6
* incompatibility with RequipMe
* option to auto equip last equipped weapon/shield on tombstone interaction
* better check if tombstone easy fits into inventory

# 1.0.5
* even better ValheimPlus compat
* fix for tombstone extra utility items preventing tombstone spawn

# 1.0.4
* more built-in translations
* hotbars refinements
* prevent simultaneous hotbar item use with similar hotkeys
* quick and extra utility slots progression requires previous slots to be obtained
* PlantEasily gamepad compatibility
* dragging item visuals and logic refinements

# 1.0.3
* configurable autoequip on tombstone interaction
* fixed major issue with BetterArchery compatibility on tombstone interaction
* hotbars made more responsive and stable
* hotbars visibility can now be toggled ingame
* perfomance improvements
* +2 more extra utility slots (up to 5 total utility items)
* +3 more extra rows (up to 5 extra rows and total 9 rows of player inventory)

# 1.0.2
* compatibility with EpicLoot enchantments for extra utility slot items

# 1.0.1
* unique-equipped utility items configurable
* valheim plus better compatibility yet it's recommended to disable inventory section

# 1.0.0
 * Initial Release