using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace ExtraSlots
{
    [BepInIncompatibility("aedenthorn.ExtendedPlayerInventory")]
    [BepInIncompatibility("Azumatt.AzuExtendedPlayerInventory")]
    [BepInIncompatibility("com.bruce.valheim.comfyquickslots")]
    [BepInIncompatibility("randyknapp.mods.equipmentandquickslots")]
    [BepInIncompatibility("moreslots")]
    [BepInIncompatibility("randyknapp.mods.auga")]
    [BepInDependency("vapok.mods.adventurebackpacks", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("ishid4.mods.betterarchery", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class ExtraSlots : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.ExtraSlots";
        public const string pluginName = "Extra Slots";
        public const string pluginVersion = "1.0.0";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        internal static ExtraSlots instance;

        internal static ConfigEntry<bool> configLocked;
        internal static ConfigEntry<bool> loggingEnabled;

        public static ConfigEntry<string> helmetLabel;
        public static ConfigEntry<string> chestLabel;
        public static ConfigEntry<string> legsLabel;
        public static ConfigEntry<string> shoulderLabel;
        public static ConfigEntry<string> utilityLabel;
        public static ConfigEntry<string> foodLabel;
        public static ConfigEntry<string> miscLabel;
        public static ConfigEntry<string> ammoLabel;

        public static ConfigEntry<int> extraRows;
        public static ConfigEntry<int> quickSlotsAmount;
        public static ConfigEntry<int> extraUtilitySlotsAmount;
        public static ConfigEntry<bool> foodSlotsEnabled;
        public static ConfigEntry<bool> miscSlotsEnabled;
        public static ConfigEntry<bool> ammoSlotsEnabled;

        public static ConfigEntry<string> vanillaSlotsOrder;
        public static ConfigEntry<SlotsAlignment> equipmentSlotsAlignment;
        public static ConfigEntry<Vector2> equipmentPanelOffset;
        public static ConfigEntry<bool> quickSlotsAlignmentCenter;

        public static ConfigEntry<bool> quickSlotsEnabled;
        public static ConfigEntry<Vector2> quickSlotsOffset;
        public static ConfigEntry<float> quickSlotsScale;

        public static ConfigEntry<KeyboardShortcut> ammoSlotHotKey1;
        public static ConfigEntry<KeyboardShortcut> ammoSlotHotKey2;
        public static ConfigEntry<KeyboardShortcut> ammoSlotHotKey3;

        public static ConfigEntry<string> ammoSlotHotKey1Text;
        public static ConfigEntry<string> ammoSlotHotKey2Text;
        public static ConfigEntry<string> ammoSlotHotKey3Text;

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

        public enum SlotsAlignment
        {
            VerticalTopHorizontalLeft,
            VerticalTopHorizontalMiddle,
            VerticalMiddleHorizontalLeft
        }

        private void Awake()
        {
            harmony.PatchAll();

            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            Game.isModded = true;

            Slots.InitializeSlots();

            EquipmentPanel.ReorderVanillaSlots();
        }

        private void LateUpdate()
        {
            if (InventoryGui.instance)
            {
                ItemsSlotsValidation.ItemsValidation.Validate();
                ItemsSlotsValidation.SlotsValidation.Validate();
            }
        }

        private void OnDestroy()
        {
            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

        public void ConfigInit()
        {
            config("General", "NexusID", 0, "Nexus mod ID for updates");

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only. [Synced with Server]", synchronizedSetting: true);
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging.");

            quickSlotsAmount = config("Extra slots", "Quick slots", 3, new ConfigDescription("How much quick slots should be added. [Synced with Server]", new AcceptableValueRange<int>(0, 6)), synchronizedSetting: true);
            extraUtilitySlotsAmount = config("Extra slots", "Extra utility slots", 1, new ConfigDescription("How much utility slots should be added [Synced with Server]", new AcceptableValueRange<int>(0, 2)), synchronizedSetting: true);
            extraRows = config("Extra slots", "Extra inventory rows", 0, new ConfigDescription("How much rows to add in regular inventory [Synced with Server]", new AcceptableValueRange<int>(0, 2)), synchronizedSetting: true);
            foodSlotsEnabled = config("Extra slots", "Food slots", true, "Enable 3 slots for food", synchronizedSetting: true);
            miscSlotsEnabled = config("Extra slots", "Misc slots", true, "Enable up to 2 slots for trophies, miscellaneous, keys and quest items." +
                                                                         "\n1 slot comes with Food slots, 1 slot comes with Ammo slots." +
                                                                         "\nIf both Food and Ammo slots are disabled there will be no Misc slots [Synced with Server]", synchronizedSetting: true);
            ammoSlotsEnabled = config("Extra slots", "Ammo slots", true, "Enable 3 slots for ammo [Synced with Server]", synchronizedSetting: true);

            extraRows.SettingChanged += (s, e) => { Slots.UpdateSlotsGridPosition(); EquipmentPanel.UpdatePanel(); };
            extraUtilitySlotsAmount.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotsAmount.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            foodSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            miscSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            ammoSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            helmetLabel = config("Extra slots - Labels", "Helmet", "Head", "Text for helmet slot.");
            chestLabel = config("Extra slots - Labels", "Chest", "Chest", "Text for chest slot.");
            legsLabel = config("Extra slots - Labels", "Legs", "Legs", "Text for legs slot.");
            shoulderLabel = config("Extra slots - Labels", "Shoulders", "Back", "Text for back slot.");
            utilityLabel = config("Extra slots - Labels", "Utility", "Utility", "Text for utility slots.");
            foodLabel = config("Extra slots - Labels", "Food", "Food", "Text for food slots.");
            miscLabel = config("Extra slots - Labels", "Misc", "Misc", "Text for misc slot.");
            ammoLabel = config("Extra slots - Labels", "Ammo", "Ammo", "Text for ammo slot.");

            vanillaSlotsOrder = config("Equipment slots - Panel", "Regular equipment slots order", Slots.VanillaOrder, "Comma separated list defining order of vanilla equipment slots");
            equipmentSlotsAlignment = config("Equipment slots - Panel", "Equipment slots alignment", SlotsAlignment.VerticalTopHorizontalMiddle, "Equipment slots alignment");
            equipmentPanelOffset = config("Equipment slots - Panel", "Offset", Vector2.zero, "Offset relative to the upper right corner of the inventory (side elements included)");
            quickSlotsAlignmentCenter = config("Equipment slots - Panel", "Quick slots alignment middle", defaultValue: false, "Place quickslots in the middle of the panel");

            vanillaSlotsOrder.SettingChanged += (s, e) => EquipmentPanel.ReorderVanillaSlots();
            equipmentSlotsAlignment.SettingChanged += (s,e) => EquipmentPanel.UpdatePanel();
            equipmentPanelOffset.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            quickSlotsEnabled = config("Quick slots - Panel", "Enabled", defaultValue: true, "Enable hotbar with quick slots");
            quickSlotsOffset = config("Quick slots - Panel", "Offset", defaultValue: new Vector2(230f, 923f), "On screen position of quickslots hotbar panel");
            quickSlotsScale = config("Quick slots - Panel", "Scale", defaultValue: 1f, "Relative size");

            quickSlotsEnabled.SettingChanged += (s, e) => QuickSlotsHotBar.MarkDirty();

            ammoSlotHotKey1 = config("Hotkeys", "Ammo 1", new KeyboardShortcut(KeyCode.Alpha1, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            ammoSlotHotKey2 = config("Hotkeys", "Ammo 2", new KeyboardShortcut(KeyCode.Alpha2, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            ammoSlotHotKey3 = config("Hotkeys", "Ammo 3", new KeyboardShortcut(KeyCode.Alpha3, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");

            ammoSlotHotKey1Text = config("Hotkeys", "Ammo 1 Text", "", "Hotkey 1 Display Text. Leave blank to use the hotkey itself.");
            ammoSlotHotKey2Text = config("Hotkeys", "Ammo 2 Text", "", "Hotkey 2 Display Text. Leave blank to use the hotkey itself.");
            ammoSlotHotKey3Text = config("Hotkeys", "Ammo 3 Text", "", "Hotkey 3 Display Text. Leave blank to use the hotkey itself.");

            quickSlotHotKey1 = config("Hotkeys", "Quickslot 1", new KeyboardShortcut(KeyCode.Z), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey2 = config("Hotkeys", "Quickslot 2", new KeyboardShortcut(KeyCode.X), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey3 = config("Hotkeys", "Quickslot 3", new KeyboardShortcut(KeyCode.C), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey4 = config("Hotkeys", "Quickslot 4", new KeyboardShortcut(KeyCode.V), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey5 = config("Hotkeys", "Quickslot 5", new KeyboardShortcut(KeyCode.B), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey6 = config("Hotkeys", "Quickslot 6", new KeyboardShortcut(KeyCode.N), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");

            quickSlotHotKey1Text = config("Hotkeys", "Quickslot 1 Text", "", "Hotkey 1 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey2Text = config("Hotkeys", "Quickslot 2 Text", "", "Hotkey 2 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey3Text = config("Hotkeys", "Quickslot 3 Text", "", "Hotkey 3 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey4Text = config("Hotkeys", "Quickslot 4 Text", "", "Hotkey 4 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey5Text = config("Hotkeys", "Quickslot 5 Text", "", "Hotkey 5 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey6Text = config("Hotkeys", "Quickslot 6 Text", "", "Hotkey 6 Display Text. Leave blank to use the hotkey itself.");

            equipmentSlotLabelAlignment = config("Equipment slots - Panel - Label style", "Horizontal alignment", TMPro.HorizontalAlignmentOptions.Left, "Horizontal alignment of text component in equipment slot label");
            equipmentSlotLabelWrappingMode = config("Equipment slots - Panel - Label style", "Text wrapping mode", TMPro.TextWrappingModes.Normal, "Size of text component in slot label");
            equipmentSlotLabelMargin = config("Equipment slots - Panel - Label style", "Margin", new Vector4(5f, 0f, 5f, 0f), "Margin: left top right bottom");
            equipmentSlotLabelFontSize = config("Equipment slots - Panel - Label style", "Font size", new Vector2(12f, 16f), "Min and Max text size in slot label");
            equipmentSlotLabelFontColor = config("Equipment slots - Panel - Label style", "Font color", new Color(0.596f, 0.816f, 1f), "Text color in slot label");

            equipmentSlotLabelAlignment.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelWrappingMode.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelMargin.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelFontSize.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            equipmentSlotLabelFontColor.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();

            quickSlotLabelAlignment = config("Quick slots - Panel - Label style", "Horizontal alignment", TMPro.HorizontalAlignmentOptions.Left, "Horizontal alignment of text component in slot label");
            quickSlotLabelWrappingMode = config("Quick slots - Panel - Label style", "Text wrapping mode", TMPro.TextWrappingModes.Normal, "Size of text component in slot label");
            quickSlotLabelMargin = config("Quick slots - Panel - Label style", "Margin", new Vector4(5f, 0f, 5f, 0f), "Margin: left top right bottom");
            quickSlotLabelFontSize = config("Quick slots - Panel - Label style", "Font size", new Vector2(12f, 16f), "Min and Max text size in slot label");
            quickSlotLabelFontColor = config("Quick slots - Panel - Label style", "Font color", new Color(0.596f, 0.816f, 1f), "Text color in slot label");

            quickSlotLabelAlignment.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelWrappingMode.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelMargin.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelFontSize.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
            quickSlotLabelFontColor.SettingChanged += (s, e) => EquipmentPanel.MarkDirty();
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        public static void LogWarning(object data)
        {
            instance.Logger.LogWarning(data);
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = false)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = false) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);
    }
}
