# Extra Slots

More inventory slots dedicated for equipment, food, ammo and misc items. Extra utility slots. Quick slots. Custom slots API. Player inventory resize. Gamepad friendly. Slot obtaining progression.

[Mod description](package/thunderstore/ExtraSlots/README.md)

# Extra Slots API

I can add more methods to API if you miss something reasonable. Open a [github issue](https://github.com/shudnal/ExtraSlots/issues).

You can either use this API with ExtraSlots set as BepInEx hard dependency or soft dependency.

[API methods with summary](https://github.com/shudnal/ExtraSlots/blob/master/API/API.cs)

## Hard dependency

Hard dependency means your mod will not work without ExtraSlots installed.

Add attribute
```[BepInDependency("shudnal.ExtraSlots", BepInDependency.DependencyFlags.HardDependency)]```
before your `BaseUnityPlugin` declaration

Add reference to ExtraSlots.dll and use its API methods like `ExtraSlots.API.IsLoaded()`.

## Soft dependency

Soft dependency means you will be able to use `ExtraSlots.API` methods wherever main ExtraSlots mod is installed or not.

Add attribute
```[BepInDependency("shudnal.ExtraSlots", BepInDependency.DependencyFlags.SoftDependency)]```
before your `BaseUnityPlugin` declaration

In this case you need to merge ExtraSlotsAPI.dll into your mod's dll.

* [Download ExtraSlotsAPI.dll (github)](https://github.com/shudnal/ExtraSlots/releases/download/ExtraSlotsAPI/ExtraSlotsAPI.dll)
* Add it into your project
* Set the file property "Copy to output directory" to "Copy if newer" or "Copy always".

You need NuGet Package `ILRepack.Lib.MSBuild.Task` to merge your dll with ExtraSlotsAPI.dll.

If you use VisualStudio:
* right click on the project
* Manage NuGet packages
* set tab to Browse
* find `ILRepack.Lib.MSBuild.Task` and install most downloaded version

ILRepacks read the contents of file `ILRepack.targets` located in the root folder of your project.

* Add new file to your project and name it `ILRepack.targets`
* Insert following block in the file and save it
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
	<Target Name="ILRepacker" AfterTargets="Build">
		<ItemGroup>
			<InputAssemblies Include="$(TargetPath)" />
			<InputAssemblies Include="$(OutputPath)ExtraSlotsAPI.dll" />
		</ItemGroup>
		<ILRepack Parallel="true" DebugInfo="true" Internalize="true" InputAssemblies="@(InputAssemblies)" OutputFile="$(TargetPath)" TargetKind="SameAsPrimaryAssembly" LibraryPath="$(OutputPath)" />
	</Target>
</Project>
```

Try to build a project and see if you are getting similar lines in build output
```
1>  Added assembly 'bin\Debug\ExtraSlotsAPI.dll'
1>  Merging 2 assembies to '...\bin\Debug\YourModName.dll'
```

Now you can be sure your `ExtraSlots.API` is always available.

For more info about what API method result see [API methods with summary](https://github.com/shudnal/ExtraSlots/blob/master/API/API.cs).

In short you can add/remove slots and get various slots related info.

Now to API calls:
* Add a reference to ExtraSlotsAPI.dll to your project
* Paste line `ExtraSlots.API.IsLoaded()` into your mod Awake function and see if it works as correct method call

## Example

I use ExtraSlotsAPI to add custom slot in my [CircletExtended](https://github.com/shudnal/CircletExtended) and [HipLantern](https://thunderstore.io/c/valheim/p/shudnal/HipLantern/) mods.

Next will be an example from CircletExtended mod.

I need my Circlet slot to appear before Lantern slot for more intuitive layout.

I define related config entries that state should mod create an extra slot, how it should be named and should it wait for item discovery for slot to be active.
```c#
public static ConfigEntry<bool> itemSlotExtraSlots; // Enable or disable slot entirely
public static ConfigEntry<string> itemSlotNameExtraSlots; // Slot name (any string)
public static ConfigEntry<int> itemSlotIndexExtraSlots; // Slot position in layout
public static ConfigEntry<bool> itemSlotExtraSlotsDiscovery; // Should slot be active only after circlet discovery
```
then I initialize config variables
```c#
itemSlotExtraSlots = Config.Bind("Circlet - Custom slot", "ExtraSlots - Create slot", true, "Create custom equipment slot with ExtraSlots.");
itemSlotNameExtraSlots = Config.Bind("Circlet - Custom slot", "ExtraSlots - Slot name", "Circlet", "Custom equipment slot name.");
itemSlotIndexExtraSlots = Config.Bind("Circlet - Custom slot", "ExtraSlots - Slot index", -1, "Slot index (position). Game restart is required to apply changes.");
itemSlotExtraSlotsDiscovery = Config.Bind("Circlet - Custom slot", "ExtraSlots - Available after discovery", true, "If enabled - slot will be active only if you know circlet item.");

itemSlotExtraSlots.SettingChanged += (s, e) => ExtraSlots.API.UpdateSlots(); // After enabling/disabling slot call a method to update slots layout
```
And then all I need is to call this in Awake function
```c#
if (ExtraSlots.API.IsLoaded())
    if (itemSlotIndexExtraSlots.Value < 0)
        ExtraSlots.API.AddSlotBefore("CircletExtended", () => itemSlotNameExtraSlots.Value, item => IsCircletItem(item), () => IsCircletSlotAvailable(), "HipLantern");
    else
        ExtraSlots.API.AddSlotWithIndex("CircletExtended", itemSlotIndexExtraSlots.Value, () => itemSlotNameExtraSlots.Value, item => IsCircletItem(item), () => IsCircletSlotAvailable());
```
This defines slot name as a function and if you change `itemSlotNameExtraSlots` config value it will be automatically updated.

`IsCircletItem` is the function to check if item type is correct. It should return if item fits the slot.

`IsCircletSlotAvailable` is the function that return should slot be active. If the result is changed for best result you should call `ExtraSlots.API.UpdateSlots()` to update layout. In this example it is handled by action on `itemSlotExtraSlots.SettingChanged`.

Used functions (example):
```c#
internal static bool IsCircletItem(ItemDrop item) => item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet; // There could be more intricate logic

internal static bool IsCircletSlotAvailable() => itemSlotExtraSlots.Value && (!itemSlotExtraSlotsDiscovery.Value || IsCircletKnown());

internal static bool IsCircletKnown()
{
    if (!Player.m_localPlayer || Player.m_localPlayer.m_isLoading) // m_isLoading check is recommended to properly load inventory layout without item to be moved to other slot, even if only temporarily.
        return true;

    return Player.m_localPlayer.IsKnownMaterial("$item_helmet_dverger"); // There could be more intricate logic
}
```

This is the basic example.

If you have questions feel free to reach me at [discord](https://discord.com/users/shudnal), Nexus or just open github issue.

There are similar API by [AzuEPI](https://github.com/AzumattDev/AzuEPI/wiki/API-Home) that works exactly the same. Maybe you will find your answer there.