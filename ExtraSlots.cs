using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LocalizationManager;
using ServerSync;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ExtraSlots
{
    [BepInIncompatibility("aedenthorn.ExtendedPlayerInventory")]
    [BepInIncompatibility("Azumatt.AzuExtendedPlayerInventory")]
    [BepInIncompatibility("com.bruce.valheim.comfyquickslots")]
    [BepInIncompatibility("randyknapp.mods.equipmentandquickslots")]
    [BepInIncompatibility("moreslots")]
    [BepInIncompatibility("randyknapp.mods.auga")]
    [BepInIncompatibility("toombe.EquipMultipleUtilityItemsUpdate")] // https://thunderstore.io/c/valheim/p/JackFrostCC/ToombeEquipMultipleUtilityItemsUnofficialUpdate/
    [BepInIncompatibility("aedenthorn.EquipMultipleUtilityItems")] // https://www.nexusmods.com/valheim/mods/1348
    [BepInIncompatibility("neobotics.valheim_mod.requipme")]
    [BepInDependency(Compatibility.EpicLootCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compatibility.BetterArcheryCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compatibility.ValheimPlusCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compatibility.PlantEasilyCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compatibility.BetterProgressionCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class ExtraSlots : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.ExtraSlots";
        public const string pluginName = "Extra Slots";
        public const string pluginVersion = "1.0.11";

        internal readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        internal static ExtraSlots instance;

        internal static ConfigEntry<bool> configLocked;
        internal static ConfigEntry<bool> loggingEnabled;
        internal static ConfigEntry<bool> loggingDebugEnabled;

        public static ConfigEntry<int> extraRows;
        public static ConfigEntry<int> quickSlotsAmount;
        public static ConfigEntry<int> extraUtilitySlotsAmount;
        public static ConfigEntry<bool> foodSlotsEnabled;
        public static ConfigEntry<bool> miscSlotsEnabled;
        public static ConfigEntry<bool> ammoSlotsEnabled;
        public static ConfigEntry<bool> backupEnabled;
        public static ConfigEntry<string> preventUniqueUtilityItemsEquip;

        public static ConfigEntry<bool> slotsTombstoneAutoEquipWeaponShield;
        public static ConfigEntry<bool> slotsTombstoneAutoEquipEnabled;
        public static ConfigEntry<bool> slotsTombstoneAutoEquipCarryWeightItemsEnabled;

        public static ConfigEntry<string> vanillaSlotsOrder;
        public static ConfigEntry<SlotsAlignment> equipmentSlotsAlignment;
        public static ConfigEntry<Vector2> equipmentPanelOffset;
        public static ConfigEntry<Vector2> equipmentPanelTooltipOffset;
        public static ConfigEntry<bool> quickSlotsAlignmentCenter;
        public static ConfigEntry<bool> equipmentSlotsShowTooltip;
        public static ConfigEntry<bool> fixContainerPosition;

        public static ConfigEntry<bool> foodSlotsShowLabel;
        public static ConfigEntry<bool> foodSlotsShowHintImage;
        public static ConfigEntry<bool> foodSlotsShowTooltip;
        
        public static ConfigEntry<bool> miscSlotsShowLabel;
        public static ConfigEntry<bool> miscSlotsShowHintImage;
        public static ConfigEntry<bool> miscSlotsShowTooltip;

        public static ConfigEntry<bool> ammoSlotsHotBarEnabled;
        public static ConfigEntry<Vector2> ammoSlotsHotBarOffset;
        public static ConfigEntry<float> ammoSlotsHotBarScale;
        public static ConfigEntry<bool> ammoSlotsShowLabel;
        public static ConfigEntry<bool> ammoSlotsShowHintImage;
        public static ConfigEntry<bool> ammoSlotsShowTooltip;
        public static ConfigEntry<bool> ammoSlotsHideStackSize;

        public static ConfigEntry<KeyboardShortcut> ammoSlotHotKey1;
        public static ConfigEntry<KeyboardShortcut> ammoSlotHotKey2;
        public static ConfigEntry<KeyboardShortcut> ammoSlotHotKey3;

        public static ConfigEntry<string> ammoSlotHotKey1Text;
        public static ConfigEntry<string> ammoSlotHotKey2Text;
        public static ConfigEntry<string> ammoSlotHotKey3Text;

        public static ConfigEntry<bool> quickSlotsHotBarEnabled;
        public static ConfigEntry<Vector2> quickSlotsHotBarOffset;
        public static ConfigEntry<float> quickSlotsHotBarScale;
        public static ConfigEntry<bool> quickSlotsShowLabel;
        public static ConfigEntry<bool> quickSlotsShowHintImage;
        public static ConfigEntry<bool> quickSlotsShowTooltip;
        public static ConfigEntry<bool> quickSlotsHideStackSize;

        public static ConfigEntry<KeyboardShortcut> quickSlotHotKey1;
        public static ConfigEntry<KeyboardShortcut> quickSlotHotKey2;
        public static ConfigEntry<KeyboardShortcut> quickSlotHotKey3;
        public static ConfigEntry<KeyboardShortcut> quickSlotHotKey4;
        public static ConfigEntry<KeyboardShortcut> quickSlotHotKey5;
        public static ConfigEntry<KeyboardShortcut> quickSlotHotKey6;

        public static ConfigEntry<string> quickSlotHotKey1Text;
        public static ConfigEntry<string> quickSlotHotKey2Text;
        public static ConfigEntry<string> quickSlotHotKey3Text;
        public static ConfigEntry<string> quickSlotHotKey4Text;
        public static ConfigEntry<string> quickSlotHotKey5Text;
        public static ConfigEntry<string> quickSlotHotKey6Text;

        public static ConfigEntry<TMPro.HorizontalAlignmentOptions> equipmentSlotLabelAlignment;
        public static ConfigEntry<TMPro.TextWrappingModes> equipmentSlotLabelWrappingMode;
        public static ConfigEntry<Vector4> equipmentSlotLabelMargin;
        public static ConfigEntry<Vector2> equipmentSlotLabelFontSize;
        public static ConfigEntry<Color> equipmentSlotLabelFontColor;

        public static ConfigEntry<TMPro.HorizontalAlignmentOptions> quickSlotLabelAlignment;
        public static ConfigEntry<TMPro.TextWrappingModes> quickSlotLabelWrappingMode;
        public static ConfigEntry<Vector4> quickSlotLabelMargin;
        public static ConfigEntry<Vector2> quickSlotLabelFontSize;
        public static ConfigEntry<Color> quickSlotLabelFontColor;

        public static ConfigEntry<TMPro.HorizontalAlignmentOptions> ammoSlotLabelAlignment;
        public static ConfigEntry<TMPro.TextWrappingModes> ammoSlotLabelWrappingMode;
        public static ConfigEntry<Vector4> ammoSlotLabelMargin;
        public static ConfigEntry<Vector2> ammoSlotLabelFontSize;
        public static ConfigEntry<Color> ammoSlotLabelFontColor;

        public static ConfigEntry<bool> slotsProgressionEnabled;

        public static ConfigEntry<string> quickSlotGlobalKey1;
        public static ConfigEntry<string> quickSlotGlobalKey2;
        public static ConfigEntry<string> quickSlotGlobalKey3;
        public static ConfigEntry<string> quickSlotGlobalKey4;
        public static ConfigEntry<string> quickSlotGlobalKey5;
        public static ConfigEntry<string> quickSlotGlobalKey6;

        public static ConfigEntry<string> ammoSlotsGlobalKey;
        public static ConfigEntry<string> foodSlotsGlobalKey;
        public static ConfigEntry<string> miscSlotsGlobalKey;

        public static ConfigEntry<string> utilitySlotGlobalKey1;
        public static ConfigEntry<string> utilitySlotGlobalKey2;
        public static ConfigEntry<string> utilitySlotGlobalKey3;
        public static ConfigEntry<string> utilitySlotGlobalKey4;

        public static ConfigEntry<string> utilitySlotItemDiscovered1;
        public static ConfigEntry<string> utilitySlotItemDiscovered2;
        public static ConfigEntry<string> utilitySlotItemDiscovered3;
        public static ConfigEntry<string> utilitySlotItemDiscovered4;

        public static ConfigEntry<string> quickSlotItemDiscovered1;
        public static ConfigEntry<string> quickSlotItemDiscovered2;
        public static ConfigEntry<string> quickSlotItemDiscovered3;
        public static ConfigEntry<string> quickSlotItemDiscovered4;
        public static ConfigEntry<string> quickSlotItemDiscovered5;
        public static ConfigEntry<string> quickSlotItemDiscovered6;

        public static ConfigEntry<bool> ammoSlotsAvailableAfterDiscovery;
        public static ConfigEntry<bool> utilitySlotAvailableAfterDiscovery;
        public static ConfigEntry<bool> foodSlotsAvailableAfterDiscovery;
        public static ConfigEntry<bool> equipmentSlotsAvailableAfterDiscovery;

        public static ConfigEntry<bool> rowsProgressionEnabled;

        public static ConfigEntry<string> extraRowPlayerKey1;
        public static ConfigEntry<string> extraRowPlayerKey2;
        public static ConfigEntry<string> extraRowPlayerKey3;
        public static ConfigEntry<string> extraRowPlayerKey4;
        public static ConfigEntry<string> extraRowPlayerKey5;

        public static ConfigEntry<string> extraRowItemDiscovered1;
        public static ConfigEntry<string> extraRowItemDiscovered2;
        public static ConfigEntry<string> extraRowItemDiscovered3;
        public static ConfigEntry<string> extraRowItemDiscovered4;
        public static ConfigEntry<string> extraRowItemDiscovered5;

        public static ConfigEntry<float> epicLootMagicItemUnequippedAlpha;
        public static ConfigEntry<bool> epicLootExcludeMiscItemsFromSacrifice;

        public static string configDirectory;

        public enum SlotsAlignment
        {
            VerticalTopHorizontalLeft,
            VerticalTopHorizontalMiddle,
            VerticalMiddleHorizontalLeft
        }

        private void Awake()
        {
            Localizer.Load();

            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            configDirectory = Path.Combine(Paths.ConfigPath, pluginID);

            Game.isModded = true;

            LoadIcons();

            Slots.InitializeSlots();

            EquipmentPanel.ReorderVanillaSlots();

            if (loggingDebugEnabled.Value || loggingEnabled.Value)
                LogCurrentLogLevel();

            Compatibility.EpicLootCompat.CheckForCompatibility();

            Compatibility.BetterArcheryCompat.CheckForCompatibility();

            Compatibility.PlantEasilyCompat.CheckForCompatibility();

            Compatibility.ValheimPlusCompat.CheckForCompatibility();

            Compatibility.BetterProgressionCompat.CheckForCompatibility();

            harmony.PatchAll();
        }

        private void LateUpdate()
        {
            if (InventoryGui.instance && !IsAwaitingForSlotsUpdate())
                ItemsSlotsValidation.Validate();
        }

        private void OnDestroy()
        {
            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

        public void ConfigInit()
        {
            config("General", "NexusID", 2901, "Nexus mod ID for updates");

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only. [Synced with Server]", synchronizedSetting: true);
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging.");
            loggingDebugEnabled = config("General", "Logging debug enabled", defaultValue: false, "Enable debug logging.");
            fixContainerPosition = config("General", "Fix container position for extra rows", defaultValue: true, "Moves container lower if there are extra inventory rows." +
                                                                                                                "\nDisable this if you have other mods repositioning the container grid element");

            loggingEnabled.SettingChanged += (s, e) => LogCurrentLogLevel();
            loggingDebugEnabled.SettingChanged += (s, e) => LogCurrentLogLevel();

            quickSlotsAmount = config("Extra slots", "Amount of quick slots", defaultValue: 3, new ConfigDescription("How much quick slots should be added. [Synced with Server]", new AcceptableValueRange<int>(0, 6)), synchronizedSetting: true);
            extraUtilitySlotsAmount = config("Extra slots", "Amount of extra utility slots", defaultValue: 2, new ConfigDescription("How much extra utility slots should be added [Synced with Server]", new AcceptableValueRange<int>(0, 4)), synchronizedSetting: true);
            extraRows = config("Extra slots", "Amount of extra inventory rows", defaultValue: 0, new ConfigDescription("How much rows to add in regular inventory [Synced with Server]", new AcceptableValueRange<int>(0, 5)), synchronizedSetting: true);
            ammoSlotsEnabled = config("Extra slots", "Enable ammo slots", defaultValue: true, "Enable 3 slots for ammo [Synced with Server]", synchronizedSetting: true);
            foodSlotsEnabled = config("Extra slots", "Enable food slots", defaultValue: true, "Enable 3 slots for food [Synced with Server]", synchronizedSetting: true);
            miscSlotsEnabled = config("Extra slots", "Enable misc slots", defaultValue: true, "Enable up to 2 slots for trophies, coins, fish, miscellaneous, keys and quest items. [Synced with Server]" +
                                                                         "\n1 slot comes with Food slots, 1 slot comes with Ammo slots." +
                                                                         "\nIf both Food and Ammo slots are disabled there will be no Misc slots." + 
                                                                         "\nIf there are no Quick slots there will be no Misc slots.", synchronizedSetting: true);
            backupEnabled = config("Extra slots", "Slots backup enabled", defaultValue: true, "Backup extra slots item on save. [Synced with Server]" +
                                                                                        "\nIt could be restored in case of loading character without mod installed leading to extra slots item loss." +
                                                                                        "\nWhen character is loaded with no extra slots items but has backup items the items from backup will be recover.", synchronizedSetting: true);
            slotsProgressionEnabled = config("Extra slots", "Slots progression enabled", defaultValue: true, "Enabled slot obtaining progression. If disabled - all enabled slots will be available from the start. [Synced with Server]", synchronizedSetting: true);
            rowsProgressionEnabled = config("Extra slots", "Inventory rows progression enabled", defaultValue: false, "Enabled inventory rows obtaining progression.  Use with caution and report bugs. [Synced with Server]", synchronizedSetting: true);
            preventUniqueUtilityItemsEquip = config("Extra slots", "Unique utility items", "$item_beltstrength:$belt_ymir_TW", "Comma-separated list of \":\" separated tuples of items that should not be equipped at the same time [Synced with Server]" +
                                                                                           "\nIf you just want one item to be unique-equipped just add its name without \":\"", synchronizedSetting: true);

            slotsTombstoneAutoEquipEnabled = config("Extra slots - Auto equip on tombstone pickup", "Equip all equipment slots", defaultValue: false, "Auto equip items in equipment slots if tombstone was successfully taken as whole. [Synced with Server]", synchronizedSetting: true);
            slotsTombstoneAutoEquipCarryWeightItemsEnabled = config("Extra slots - Auto equip on tombstone pickup", "Equip items increasing carry weight", defaultValue: true, "Auto equip items in equipment slots that increase max carry weight (like Megingjord) if tombstone was successfully taken as whole. [Synced with Server]", synchronizedSetting: true);
            slotsTombstoneAutoEquipWeaponShield = config("Extra slots - Auto equip on tombstone pickup", "Equip previous weapon and shield", defaultValue: false, "Auto equip weapon and shield that was equipped on death. [Synced with Server]", synchronizedSetting: true);

            extraRows.SettingChanged += (s, e) => API.UpdateSlots();
            rowsProgressionEnabled.SettingChanged += (s, e) => API.UpdateSlots();
            
            extraUtilitySlotsAmount.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotsAmount.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            foodSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            miscSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            ammoSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            slotsProgressionEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            preventUniqueUtilityItemsEquip.SettingChanged += (s, e) => ExtraUtilitySlots.UpdateUniqueEquipped();

            vanillaSlotsOrder = config("Panels - Equipment slots", "Regular equipment slots order", Slots.VanillaOrder, "Comma separated list defining order of vanilla equipment slots");
            equipmentSlotsAlignment = config("Panels - Equipment slots", "Equipment slots alignment", SlotsAlignment.VerticalTopHorizontalLeft, "Equipment slots alignment");
            equipmentPanelOffset = config("Panels - Equipment slots", "Offset", Vector2.zero, "Offset relative to the upper right corner of the inventory (side elements included)");
            quickSlotsAlignmentCenter = config("Panels - Equipment slots", "Quick slots alignment middle", defaultValue: false, "Place quickslots in the middle under equipment slots");
            equipmentSlotsShowTooltip = config("Panels - Equipment slots", "Show help tooltip", defaultValue: true, "Show tooltip with slot info");
            equipmentPanelTooltipOffset = config("Panels - Equipment slots", "Gamepad Tooltip Offset", Vector2.zero, "Offset relative to original position of tooltip at upper right corner of the inventory (side elements included)");

            vanillaSlotsOrder.SettingChanged += (s, e) => EquipmentPanel.ReorderVanillaSlots();
            equipmentSlotsAlignment.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            equipmentPanelOffset.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            equipmentPanelTooltipOffset.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();

            foodSlotsShowLabel = config("Panels - Food slots", "Show label", defaultValue: false, "Show slot label");
            foodSlotsShowHintImage = config("Panels - Food slots", "Show hint image", defaultValue: true, "Show slot background hint image");
            foodSlotsShowTooltip = config("Panels - Food slots", "Show help tooltip", defaultValue: true, "Show tooltip with slot info");

            miscSlotsShowLabel = config("Panels - Misc slots", "Show label", defaultValue: false, "Show slot label");
            miscSlotsShowHintImage = config("Panels - Misc slots", "Show hint image", defaultValue: true, "Show slot background hint image");
            miscSlotsShowTooltip = config("Panels - Misc slots", "Show help tooltip", defaultValue: true, "Show tooltip with slot info");

            ammoSlotsHotBarEnabled = config("Panels - Ammo slots", "Enabled", defaultValue: true, "Enable hotbar with Ammo slots");
            ammoSlotsHotBarOffset = config("Panels - Ammo slots", "Offset", defaultValue: new Vector2(230f, 850f), "On screen position of ammo slots hotbar panel");
            ammoSlotsHotBarScale = config("Panels - Ammo slots", "Scale", defaultValue: 1f, "Relative size");
            ammoSlotsShowLabel = config("Panels - Ammo slots", "Show label", defaultValue: false, "Show slot label");
            ammoSlotsShowHintImage = config("Panels - Ammo slots", "Show hint image", defaultValue: true, "Show slot background hint image");
            ammoSlotsShowTooltip = config("Panels - Ammo slots", "Show help tooltip", defaultValue: true, "Show tooltip with slot info");
            ammoSlotsHideStackSize = config("Panels - Ammo slots", "Hide stack size in hotbar", defaultValue: false, "Hide stack size and left only current amount for consumable and equipable items in hotbar");

            ammoSlotsHotBarEnabled.SettingChanged += (s, e) => HotBars.AmmoSlotsHotBar.MarkDirty();

            ammoSlotHotKey1 = config("Hotkeys", "Ammo 1", new KeyboardShortcut(KeyCode.Alpha1, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");
            ammoSlotHotKey2 = config("Hotkeys", "Ammo 2", new KeyboardShortcut(KeyCode.Alpha2, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");
            ammoSlotHotKey3 = config("Hotkeys", "Ammo 3", new KeyboardShortcut(KeyCode.Alpha3, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");

            ammoSlotHotKey1Text = config("Hotkeys", "Ammo 1 Text", "Alt + 1", "Hotkey 1 Display Text. Leave blank to use the hotkey itself.");
            ammoSlotHotKey2Text = config("Hotkeys", "Ammo 2 Text", "Alt + 2", "Hotkey 2 Display Text. Leave blank to use the hotkey itself.");
            ammoSlotHotKey3Text = config("Hotkeys", "Ammo 3 Text", "Alt + 3", "Hotkey 3 Display Text. Leave blank to use the hotkey itself.");

            ammoSlotHotKey1.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();
            ammoSlotHotKey2.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();
            ammoSlotHotKey3.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();

            quickSlotsHotBarEnabled = config("Panels - Quick slots", "Enabled", defaultValue: true, "Enable hotbar with quick slots");
            quickSlotsHotBarOffset = config("Panels - Quick slots", "Offset", defaultValue: new Vector2(230f, 923f), "On screen position of quick slots hotbar panel");
            quickSlotsHotBarScale = config("Panels - Quick slots", "Scale", defaultValue: 1f, "Relative size");
            quickSlotsShowLabel = config("Panels - Quick slots", "Show label", defaultValue: false, "Show slot label");
            quickSlotsShowHintImage = config("Panels - Quick slots", "Show hint image", defaultValue: true, "Show slot background hint image");
            quickSlotsShowTooltip = config("Panels - Quick slots", "Show help tooltip", defaultValue: true, "Show tooltip with slot info");
            quickSlotsHideStackSize = config("Panels - Quick slots", "Hide stack size in hotbar", defaultValue: false, "Hide stack size and left only current amount for consumable and equipable items in hotbar");

            quickSlotsHotBarEnabled.SettingChanged += (s, e) => HotBars.QuickSlotsHotBar.MarkDirty();

            quickSlotHotKey1 = config("Hotkeys", "Quickslot 1", new KeyboardShortcut(KeyCode.Z, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");
            quickSlotHotKey2 = config("Hotkeys", "Quickslot 2", new KeyboardShortcut(KeyCode.X, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");
            quickSlotHotKey3 = config("Hotkeys", "Quickslot 3", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");
            quickSlotHotKey4 = config("Hotkeys", "Quickslot 4", new KeyboardShortcut(KeyCode.V, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");
            quickSlotHotKey5 = config("Hotkeys", "Quickslot 5", new KeyboardShortcut(KeyCode.Q, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");
            quickSlotHotKey6 = config("Hotkeys", "Quickslot 6", new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt), "Use configuration manager to set shortcuts.");

            quickSlotHotKey1.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey2.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey3.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey4.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey5.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey6.SettingChanged += (s, e) => HotBars.PreventSimilarHotkeys.FillSimilarHotkey();

            quickSlotHotKey1Text = config("Hotkeys", "Quickslot 1 Text", "Alt + Z", "Hotkey 1 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey2Text = config("Hotkeys", "Quickslot 2 Text", "Alt + X", "Hotkey 2 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey3Text = config("Hotkeys", "Quickslot 3 Text", "Alt + C", "Hotkey 3 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey4Text = config("Hotkeys", "Quickslot 4 Text", "Alt + V", "Hotkey 4 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey5Text = config("Hotkeys", "Quickslot 5 Text", "Alt + Q", "Hotkey 5 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey6Text = config("Hotkeys", "Quickslot 6 Text", "Alt + R", "Hotkey 6 Display Text. Leave blank to use the hotkey itself.");

            equipmentSlotLabelAlignment = config("Panels - Equipment slots - Label style", "Horizontal alignment", TMPro.HorizontalAlignmentOptions.Left, "Horizontal alignment of text component in equipment slot label");
            equipmentSlotLabelWrappingMode = config("Panels - Equipment slots - Label style", "Text wrapping mode", TMPro.TextWrappingModes.Normal, "Size of text component in slot label");
            equipmentSlotLabelMargin = config("Panels - Equipment slots - Label style", "Margin", new Vector4(5f, 0f, 5f, 0f), "Margin: left top right bottom");
            equipmentSlotLabelFontSize = config("Panels - Equipment slots - Label style", "Font size", new Vector2(10f, 14f), "Min and Max text size in slot label");
            equipmentSlotLabelFontColor = config("Panels - Equipment slots - Label style", "Font color", new Color(0.596f, 0.816f, 1f), "Text color in slot label");

            equipmentSlotLabelAlignment.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelWrappingMode.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelMargin.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelFontSize.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelFontColor.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();

            quickSlotLabelAlignment = config("Panels - Quick slots - Label style", "Horizontal alignment", TMPro.HorizontalAlignmentOptions.Left, "Horizontal alignment of text component in slot label");
            quickSlotLabelWrappingMode = config("Panels - Quick slots - Label style", "Text wrapping mode", TMPro.TextWrappingModes.Normal, "Size of text component in slot label");
            quickSlotLabelMargin = config("Panels - Quick slots - Label style", "Margin", new Vector4(3f, 0f, 3f, 0f), "Margin: left top right bottom");
            quickSlotLabelFontSize = config("Panels - Quick slots - Label style", "Font size", new Vector2(10f, 14f), "Min and Max text size in slot label");
            quickSlotLabelFontColor = config("Panels - Quick slots - Label style", "Font color", new Color(0.596f, 0.816f, 1f), "Text color in slot label");

            quickSlotLabelAlignment.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelWrappingMode.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelMargin.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelFontSize.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelFontColor.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();

            ammoSlotLabelAlignment = config("Panels - Ammo slots - Label style", "Horizontal alignment", TMPro.HorizontalAlignmentOptions.Left, "Horizontal alignment of text component in slot label");
            ammoSlotLabelWrappingMode = config("Panels - Ammo slots - Label style", "Text wrapping mode", TMPro.TextWrappingModes.Normal, "Size of text component in slot label");
            ammoSlotLabelMargin = config("Panels - Ammo slots - Label style", "Margin", new Vector4(3f, 0f, 3f, 0f), "Margin: left top right bottom");
            ammoSlotLabelFontSize = config("Panels - Ammo slots - Label style", "Font size", new Vector2(10f, 14f), "Min and Max text size in slot label");
            ammoSlotLabelFontColor = config("Panels - Ammo slots - Label style", "Font color", new Color(0.596f, 0.816f, 1f), "Text color in slot label");

            ammoSlotLabelAlignment.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            ammoSlotLabelWrappingMode.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            ammoSlotLabelMargin.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            ammoSlotLabelFontSize.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            ammoSlotLabelFontColor.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();

            quickSlotGlobalKey1 = config("Progression - Global keys", "Quickslot 1", "defeated_gdking", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotGlobalKey2 = config("Progression - Global keys", "Quickslot 2", "defeated_gdking", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotGlobalKey3 = config("Progression - Global keys", "Quickslot 3", "defeated_bonemass", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotGlobalKey4 = config("Progression - Global keys", "Quickslot 4", "defeated_dragon", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotGlobalKey5 = config("Progression - Global keys", "Quickslot 5", "defeated_goblinking", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotGlobalKey6 = config("Progression - Global keys", "Quickslot 6", "defeated_queen", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);

            ammoSlotsGlobalKey = config("Progression - Global keys", "Ammo slots", "", "Comma-separated list of global keys and player unique keys. Slots will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            foodSlotsGlobalKey = config("Progression - Global keys", "Food slots", "", "Comma-separated list of global keys and player unique keys. Slots will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            miscSlotsGlobalKey = config("Progression - Global keys", "Misc slots", "", "Comma-separated list of global keys and player unique keys. Slots will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);

            utilitySlotGlobalKey1 = config("Progression - Global keys", "Extra utility slot 1", "defeated_bonemass", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            utilitySlotGlobalKey2 = config("Progression - Global keys", "Extra utility slot 2", "defeated_goblinking", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            utilitySlotGlobalKey3 = config("Progression - Global keys", "Extra utility slot 3", "defeated_queen", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            utilitySlotGlobalKey4 = config("Progression - Global keys", "Extra utility slot 4", "defeated_queen", "Comma-separated list of global keys and player unique keys. Slot will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);

            ammoSlotsAvailableAfterDiscovery = config("Progression - Discovery", "Ammo slots", true, "Ammo slots will be active after acquiring first ammo item [Synced with Server]", synchronizedSetting: true);
            utilitySlotAvailableAfterDiscovery = config("Progression - Discovery", "Utility slots", true, "Utility slots will be active after acquiring first utility item [Synced with Server]", synchronizedSetting: true);
            foodSlotsAvailableAfterDiscovery = config("Progression - Discovery", "Food slots", true, "Food slots will be active after acquiring first food item [Synced with Server]", synchronizedSetting: true);
            equipmentSlotsAvailableAfterDiscovery = config("Progression - Discovery", "Equipment slots", true, "Corresponding equipment slot will be active after acquiring first item [Synced with Server]", synchronizedSetting: true);

            quickSlotGlobalKey1.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotGlobalKey2.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotGlobalKey3.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotGlobalKey4.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotGlobalKey5.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotGlobalKey6.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            ammoSlotsGlobalKey.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            foodSlotsGlobalKey.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            miscSlotsGlobalKey.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            utilitySlotGlobalKey1.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            utilitySlotGlobalKey2.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            utilitySlotGlobalKey3.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            utilitySlotGlobalKey4.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            ammoSlotsAvailableAfterDiscovery.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            utilitySlotAvailableAfterDiscovery.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            foodSlotsAvailableAfterDiscovery.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            equipmentSlotsAvailableAfterDiscovery.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            utilitySlotItemDiscovered1 = config("Progression - Items", "Extra utility slot 1", "$item_wishbone", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            utilitySlotItemDiscovered2 = config("Progression - Items", "Extra utility slot 2", "$item_demister,$mod_epicloot_assets_goldrubyring,$mod_epicloot_assets_silverring", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            utilitySlotItemDiscovered3 = config("Progression - Items", "Extra utility slot 3", "", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            utilitySlotItemDiscovered4 = config("Progression - Items", "Extra utility slot 4", "", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);

            utilitySlotItemDiscovered1.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            utilitySlotItemDiscovered2.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            utilitySlotItemDiscovered3.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            utilitySlotItemDiscovered4.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            quickSlotItemDiscovered1 = config("Progression - Items", "Quickslot 1", "$item_cryptkey", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotItemDiscovered2 = config("Progression - Items", "Quickslot 2", "$item_cryptkey", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotItemDiscovered3 = config("Progression - Items", "Quickslot 3", "$item_wishbone", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotItemDiscovered4 = config("Progression - Items", "Quickslot 4", "", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotItemDiscovered5 = config("Progression - Items", "Quickslot 5", "", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            quickSlotItemDiscovered6 = config("Progression - Items", "Quickslot 6", "", "Comma-separated list of items. Slot will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);

            quickSlotItemDiscovered1.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotItemDiscovered2.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotItemDiscovered3.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotItemDiscovered4.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotItemDiscovered5.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotItemDiscovered6.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            extraRowPlayerKey1 = config("Progression - Inventory - Player keys", "Extra row 1", "GP_Bonemass", "Comma-separated list of Player unique keys. Extra inventory row will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowPlayerKey2 = config("Progression - Inventory - Player keys", "Extra row 2", "GP_Moder", "Comma-separated list of Player unique keys. Extra inventory row will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowPlayerKey3 = config("Progression - Inventory - Player keys", "Extra row 3", "GP_Yagluth", "Comma-separated list of Player unique keys. Extra inventory row will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowPlayerKey4 = config("Progression - Inventory - Player keys", "Extra row 4", "GP_Queen", "Comma-separated list of Player unique keys. Extra inventory row will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowPlayerKey5 = config("Progression - Inventory - Player keys", "Extra row 5", "GP_Fader", "Comma-separated list of Player unique keys. Extra inventory row will be active only if any key is enabled or list is not set. [Synced with Server]", synchronizedSetting: true);

            extraRowPlayerKey1.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowPlayerKey2.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowPlayerKey3.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowPlayerKey4.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowPlayerKey5.SettingChanged += (s, e) => API.UpdateSlots();

            extraRowItemDiscovered1 = config("Progression - Inventory - Items", "Extra row 1", "$item_wishbone", "Comma-separated list of items. Extra inventory row will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowItemDiscovered2 = config("Progression - Inventory - Items", "Extra row 2", "$item_dragontear", "Comma-separated list of items. Extra inventory row will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowItemDiscovered3 = config("Progression - Inventory - Items", "Extra row 3", "$item_yagluththing", "Comma-separated list of items. Extra inventory row will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowItemDiscovered4 = config("Progression - Inventory - Items", "Extra row 4", "$item_seekerqueen_drop", "Comma-separated list of items. Extra inventory row will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);
            extraRowItemDiscovered5 = config("Progression - Inventory - Items", "Extra row 5", "$item_fader_drop", "Comma-separated list of items. Extra inventory row will be active only if any item is discovered or list is not set. [Synced with Server]", synchronizedSetting: true);

            extraRowItemDiscovered1.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowItemDiscovered2.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowItemDiscovered3.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowItemDiscovered4.SettingChanged += (s, e) => API.UpdateSlots();
            extraRowItemDiscovered5.SettingChanged += (s, e) => API.UpdateSlots();

            epicLootMagicItemUnequippedAlpha = config("Mods compatibility", "EpicLoot unequipped item alpha", 0.2f, "Make unequipped enchanted item more visible in equipment panel by making its background image more transparent.");
            epicLootExcludeMiscItemsFromSacrifice = config("Mods compatibility", "EpicLoot exclude misc items from sacrifice", true, "If EpicLoot config ShowEquippedAndHotbarItemsInSacrificeTab is enabled then items in misc slots will be excluded from sacrifice.");
        }

        public static void LogDebug(object data)
        {
            if (loggingDebugEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        public static void LogMessage(object data)
        {
           instance.Logger.LogMessage(data);
        }

        public static void LogWarning(object data)
        {
            instance.Logger.LogWarning(data);
        }

        public static void LogCurrentLogLevel() => LogInfo($"Logging: Info {loggingEnabled.Value}, Debug {loggingDebugEnabled.Value}");

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = false)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = false) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        private void LoadIcons()
        {
            LoadIcon("ammoslot.png", ref EquipmentPanel.ammoSlot);
            LoadIcon("miscslot.png", ref EquipmentPanel.miscSlot);
            LoadIcon("quickslot.png", ref EquipmentPanel.quickSlot);

            LoadIcon("background.png", ref EquipmentPanel.background);
        }

        internal static void LoadIcon(string filename, ref Sprite icon)
        {
            Texture2D tex = new Texture2D(2, 2);
            if (LoadTexture(filename, ref tex))
                icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
        }

        internal static bool LoadTextureFromConfigDirectory(string filename, ref Texture2D tex)
        {
            string fileInConfigFolder = Path.Combine(configDirectory, filename);
            if (!File.Exists(fileInConfigFolder))
                return false;

            LogInfo($"Loaded image from config folder: {filename}");
            return tex.LoadImage(File.ReadAllBytes(fileInConfigFolder));
        }

        internal static bool LoadTexture(string filename, ref Texture2D tex)
        {
            if (LoadTextureFromConfigDirectory(filename, ref tex))
                return true;

            tex.name = Path.GetFileNameWithoutExtension(filename);
            return tex.LoadImage(GetEmbeddedFileData(filename), true);
        }

        internal static byte[] GetEmbeddedFileData(string filename)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string name = executingAssembly.GetManifestResourceNames().SingleOrDefault(str => str.EndsWith(filename));
            if (name.IsNullOrWhiteSpace())
                return null;

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            byte[] data = new byte[resourceStream.Length];
            resourceStream.Read(data, 0, data.Length);

            return data;
        }

        internal static bool IsAwaitingForSlotsUpdate() => instance.slotsUpdater != null;

        private System.Collections.IEnumerator slotsUpdater;

        internal void StartSlotsUpdateNextFrame()
        {
            if (IsAwaitingForSlotsUpdate())
                return;

            slotsUpdater = UpdateSlotsNextFrame();
            instance.StartCoroutine(slotsUpdater);
        }

        private System.Collections.IEnumerator UpdateSlotsNextFrame()
        {
            yield return new WaitForEndOfFrame();

            LogInfo("UpdateSlots delayed update");

            API.UpdateSlots();

            slotsUpdater = null;
        }
    }
}
