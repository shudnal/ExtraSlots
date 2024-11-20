# Extra Slots
![](https://staticdelivery.nexusmods.com/mods/3667/images/headers/2901_1731281003.jpg)

More inventory slots dedicated for equipment, food, ammo and misc items. Extra utility slots. Quick slots. Custom slots API. Player inventory resize. Gamepad friendly. Slot obtaining progression.

## Summary

This mod expands the player's inventory, not by just adding more rows but by creating extra slots specifically designated for certain types of items.

You are already familiar with equipment slots and quick slots. This mod also adds specific slots for food, ammo and miscellaneous items.

You can add up to 6 quick slots, 3 food slots, 3 ammo slots and up to 2 slots for misc items.

Quick slots and ammo slots will create a dedicated hotbars for quick use.

Miscellaneous items includes quest items, keys, fish, trophies and coins.

You can equip up to 5 utility items simultaneously.

You can add up to 5 extra rows in regular player inventory in case you really need large inventory.

Both equipment panel and hotbars are totally gamepad friendly.

Slots obtaining progression is enabled by default. It means not every slot could be available from the start. It requires obtaining certain items or killing bosses to get more slots.

There is public API to access some mod's functions and to dynamically add up to 9 custom equipment slots which can be enabled/disabled on the fly for it to be part of progression.

This mod is incompatible with other similar purpose mods which singular purpose is inventory management. Valheim Plus is supported yet inventory part is better be disabled. BetterArchery's quiver will be disabled. You will not lose your arrows.

If you had items in slots and then run the game without this mod installed the items will be lost. But you will be able to restore it from automatic backup. Remove incompatible mods, run the game with ExtraSlots installed and your items will be back.

### Migration from EquipmentAndQuickSlots
If you just disable EaQS and load this mod you won't see a difference. Your items will be automatically moved into appropriate slots.

In some scenarios items will be moved into regular inventory but highly unlikely.

For backward migration you should clear all extra slots beforehand or you will lose the items.

### Migration from AzuExtendedPlayerInventory
If you just disable AzuEPI and load this mod you could see some items moved into different slots.

Items from regular equipment slots will be automatically moved into appropriate slots.

Items from quick slots will be moved into quick slots.

Items from custom equipment slots added via API will be moved into most fitting slot or regular inventory if there are none. 

If mod that added custom slot compatible with both AzuEPI and ExtraSlots custom API there should be no issues.

For backward migration you should clear all extra slots beforehand or you will lose the items.

### Custom slots API

There is [ExtraSlotsCustomSlots](https://thunderstore.io/c/valheim/p/shudnal/ExtraSlotsCustomSlots/) mod that adds custom slots for various mods.

You can add/remove custom equipment slots on the fly but most stable way is to do it on Awake.

You should set slot ID as fixed string unique among other slots and you can set slot name as a function which result can be changed on the runtime.

Slot item is validated by function set with slot addition.

You can set other slot IDs for your slot to be added before or after slots with set IDs.
You can set specific index for your slot.

* [Wiki (thunderstore)](https://thunderstore.io/c/valheim/p/shudnal/ExtraSlots/wiki/)
* [Wiki (github)](https://github.com/shudnal/ExtraSlots)
* [Download ExtraSlotsAPI.dll (github)](https://github.com/shudnal/ExtraSlots/releases/download/ExtraSlotsAPI/ExtraSlotsAPI.dll)

## Server synchronization for config values

Mod can be installed on client only.

If mod installed on a server then client and server versions should be equal.

In case mod installed on a server, some crucial config values will be synchronized from a server:
* amount of quick slots (0-6)
* amount of extra inventory rows (0-5)
* amount of extra utility items (0-4)
* should ammo slots be enabled
* should food slots be enabled
* should misc slots be enabled
* should slots backup be enabled
* should slot progression be enabled and its details

Everything else is not server synced and can differ on every client. It means you can change appearance and hotkeys as you wish.

## Slots backup

Every time you save your character the backup of extra slots current state will be saved with it. You will not lose your extra items.

There is no items history, only last state on extra slots inventory. It will be overwritten on character save.

If you disable this mod and run the game without it you will lose any items that were in extra slots. But slots backup will be there until it is pruned deliberately.

If you want your items back:
* delete any incompatible mod that change inventory (don't forget to move items in regular inventory or chest)
* run the game without this mods and without(!) ExtraSlots (you can run vanilla game if you're not sure)
* login in any world with character you want to restore items
* save this character
* run the game with ExtraSlots installed and all items will be restored as it was on the moment you disabled the mod.

## Equipment Panel

Default position of equipment panel is next to inventory. You can change `Offset` config value to adjust its position.

Gamepad tooltip will be moved under the panel and in case you have huge tooltips there is `Gamepad Tooltip Offset` config option to change its position too.

You can change panel background image. Put `background.png` file into `...\BepInEx\config\shudnal.ExtraSlots\background.png`. Vanilla resolution is 922x966.

You can change the order of vanilla slots. You can not disable vanilla slots.

You can change layout of equipment slots. 

There are 3 options. Imagine you have reqular 5 vanilla slots. It could be positioned in order:
#### Vertical Top Horizontal Left
This mod default style
```
▢ ▢

▢ ▢

▢
```
#### Vertical Top Horizontal Middle
AzuEPI style
```
▢ ▢

▢ ▢

 ▢
```
#### Vertical Middle Horizontal Left
EaQS style
```
▢ 
  ▢
▢
  ▢
▢
```

### Quick panels (Hot bars)

Quick slot and Ammo slots could have corresponding hotkey bars similar to regular 1-8 hotkey bar.

You can change its position and scale.

You can set any hotkey with the same key that is already in use by game and there will be no conflict.

Default ammo slot hotkeys are Left Alt + (1-3). It means if you press Alt + 1 you will not use your regular 1 item from regular inventory hotbar.

Default quick slot hotkeys are Left Alt + (Z-C). It means if you press Alt + X you will not sit. And if you press Alt + C you will not start Walking.

It works for any hotkey.

## Slot progression

You can disable slot progression to get all the slots enabled at any moment.

You can change it whatever you want if you have other global keys or item names to create your own experience.

Default progression is designed to gradually get slots the moment you obtain items that fits in particular slot.

* First 2 quick slots are obtained after Elder kill (or touching Crypt Key)
* Third quick slot are obtained after Bonemass kill (or touching Wishbone)
* Food slots are obtained when you get your first food
* Ammo slots are obtained when you get your first ammo or fishing bait
* Misc slots are obtained alongside with quick, ammo and food slots
* Every vanilla equipment slots are obtained when you touch corresponding item
* Utility slot are obtained when you get your first utility item
* First extra utility slot are obtained after Bonemass kill (or touching Wishbone)
* Second extra utility slot are obtained after Yagluth kill (or touching Wisplight)

### Fresh character progression design

* You spawn with some default items. If there are any items you can equip you will get appropriate slot for that items.
* The moment you gather your first raspberry or mushroom or roasted meat you will get 3 food slots.
* The moment you pick up first arrows you will get 3 ammo slots.
* By the time you go to the Eikthyr, you should have 3-4 equipment slots (depends on you having cape).
* This will be your slots amount until you kill The Elder or loot a Crypt Key.
* This moment you are ready for Swamp and you will get 2 quick slots useful for meads and 2 misc slots useful for a Crypt Key and coins.
* This should get you to the Bonemass and after its kill you will get 3rd quick slot and first extra utility slot for a Wishbone
* At last you will have second extra utility slot for a Wisplight when you kill the Yagluth.

You can add more quickslots and set globalkeys or items to unlock.

## Slots auto equip on tombstone interaction

Tombstone interaction has several configurable options of what items should be auto equipped if tombstone was successfully picked up at once.

* should items that increase max carry weight be auto equipped
* should all items in equipment slots be auto equipped
* should your weapon/shield that was last equipped on death be auto equipped

## Incompatibility

Mod is incompatible with other mods altering inventory in similar way or allowing multiple utility items:

* [AzuExtendedPlayerInventory](https://thunderstore.io/c/valheim/p/Azumatt/AzuExtendedPlayerInventory/)
* [Equipment And Quick Slots](https://thunderstore.io/c/valheim/p/RandyKnapp/EquipmentAndQuickSlots/)
* [Comfy QuickSlots](https://thunderstore.io/c/valheim/p/ComfyMods/ComfyQuickSlots/)
* [Extended Player Inventory (aedenthorn)](https://www.nexusmods.com/valheim/mods/1356)
* [Equip Multiple Utility Items (aedenthorn)](https://www.nexusmods.com/valheim/mods/1348)
* [EquipMultipleUtilityItems (toombe)](https://thunderstore.io/c/valheim/p/JackFrostCC/ToombeEquipMultipleUtilityItemsUnofficialUpdate/)
* [RequipMe](https://thunderstore.io/c/valheim/p/Neobotics/RequipMe/)

## Localization
To add your own localization create a file with the name **Extra Slots.LanguageName.yml** or **Extra Slots.LanguageName.json** anywhere inside of the Bepinex folder.
For example, to add a French translation you could create a **Extra Slots.French.yml** file inside of the config folder and add French translations there.

Localization file will be loaded on the next game launch or on the next language change.

You can send me a file with your localization at [GitHub](https://github.com/shudnal/ExtraSlots/issues) or [Nexus](https://www.nexusmods.com/valheim/mods/2901?tab=posts) so I can add it to mod's bundle.

[Language list](https://valheim-modding.github.io/Jotunn/data/localization/language-list.html).

English localization example is located in `Extra Slots.English.json` file next to plugin dll.

## Installation (manual)
extract ExtraSlots.dll into your BepInEx\Plugins\ folder

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2901)