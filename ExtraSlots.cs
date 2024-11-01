using APIManager;
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

        public static ConfigEntry<int> extraRows;
        public static ConfigEntry<int> quickSlotsAmount;
        public static ConfigEntry<int> extraUtilitySlotsAmount;
        public static ConfigEntry<bool> foodSlotsEnabled;
        public static ConfigEntry<bool> miscSlotsEnabled;
        public static ConfigEntry<bool> ammoSlotsEnabled;

        public static ConfigEntry<bool> adventureBackpacksSlotEnabled;
        public static ConfigEntry<string> adventureBackpacksSlotName;
        public static ConfigEntry<int> adventureBackpacksSlotIndex;

        public static ConfigEntry<string> vanillaSlotsOrder;
        public static ConfigEntry<SlotsAlignment> equipmentSlotsAlignment;
        public static ConfigEntry<Vector2> equipmentPanelOffset;
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

        public static string configDirectory;

        public enum SlotsAlignment
        {
            VerticalTopHorizontalLeft,
            VerticalTopHorizontalMiddle,
            VerticalMiddleHorizontalLeft
        }

        private void Awake()
        {
            instance = this;

            Patcher.Patch();

            harmony.PatchAll();

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            configDirectory = Path.Combine(Paths.ConfigPath, pluginID);

            Game.isModded = true;

            LoadIcons();

            Slots.InitializeSlots();

            EquipmentPanel.ReorderVanillaSlots();

            Localizer.Load();

            if (AdventureBackpacks.API.ABAPI.IsLoaded())
                API.AddSlotWithIndex("AdventureBackpacks", adventureBackpacksSlotIndex.Value, () => adventureBackpacksSlotName.Value, item => AdventureBackpacks.API.ABAPI.IsBackpack(item), () => adventureBackpacksSlotEnabled.Value);
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
            config("General", "NexusID", 2901, "Nexus mod ID for updates");

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only. [Synced with Server]", synchronizedSetting: true);
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging.");
            fixContainerPosition = config("General", "Fix container position for extra rows", defaultValue: true, "Moves container lower if there are extra inventory rows." +
                                                                                                                "\nDisable this if you have other mods repositioning the container grid element");

            quickSlotsAmount = config("Extra slots", "Quick slots", defaultValue: 3, new ConfigDescription("How much quick slots should be added. [Synced with Server]", new AcceptableValueRange<int>(0, 6)), synchronizedSetting: true);
            extraUtilitySlotsAmount = config("Extra slots", "Extra utility slots", defaultValue: 1, new ConfigDescription("How much utility slots should be added [Synced with Server]", new AcceptableValueRange<int>(0, 2)), synchronizedSetting: true);
            extraRows = config("Extra slots", "Extra inventory rows", defaultValue: 0, new ConfigDescription("How much rows to add in regular inventory [Synced with Server]", new AcceptableValueRange<int>(0, 2)), synchronizedSetting: true);
            foodSlotsEnabled = config("Extra slots", "Food slots", defaultValue: true, "Enable 3 slots for food [Synced with Server]", synchronizedSetting: true);
            miscSlotsEnabled = config("Extra slots", "Misc slots", defaultValue: true, "Enable up to 2 slots for trophies, miscellaneous, keys and quest items." +
                                                                         "\n1 slot comes with Food slots, 1 slot comes with Ammo slots." +
                                                                         "\nIf both Food and Ammo slots are disabled there will be no Misc slots [Synced with Server]", synchronizedSetting: true);
            ammoSlotsEnabled = config("Extra slots", "Ammo slots", defaultValue: true, "Enable 3 slots for ammo [Synced with Server]", synchronizedSetting: true);

            extraRows.SettingChanged += (s, e) => API.UpdateSlots();
            extraUtilitySlotsAmount.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            quickSlotsAmount.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            foodSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            miscSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            ammoSlotsEnabled.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

            adventureBackpacksSlotEnabled = config("Extra slots - Adventure Backpacks", "Enable custom slot", defaultValue: false, "Enable custom slot for backpack [Synced with Server]", synchronizedSetting: true);
            adventureBackpacksSlotName = config("Extra slots - Adventure Backpacks", "Slot name", defaultValue: "Backpack", "Custom slot name");
            adventureBackpacksSlotIndex = config("Extra slots - Adventure Backpacks", "Slot index", defaultValue: 0, "Custom slot index");

            adventureBackpacksSlotEnabled.SettingChanged += (s, e) => API.UpdateSlots();

            vanillaSlotsOrder = config("Panels - Equipment slots", "Regular equipment slots order", Slots.VanillaOrder, "Comma separated list defining order of vanilla equipment slots");
            equipmentSlotsAlignment = config("Panels - Equipment slots", "Equipment slots alignment", SlotsAlignment.VerticalTopHorizontalMiddle, "Equipment slots alignment");
            equipmentPanelOffset = config("Panels - Equipment slots", "Offset", Vector2.zero, "Offset relative to the upper right corner of the inventory (side elements included)");
            quickSlotsAlignmentCenter = config("Panels - Equipment slots", "Quick slots alignment middle", defaultValue: false, "Place quickslots in the middle of the panel");
            equipmentSlotsShowTooltip = config("Panels - Equipment slots", "Show help tooltip", defaultValue: true, "Show tooltip with slot info");

            vanillaSlotsOrder.SettingChanged += (s, e) => EquipmentPanel.ReorderVanillaSlots();
            equipmentSlotsAlignment.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();
            equipmentPanelOffset.SettingChanged += (s, e) => EquipmentPanel.UpdatePanel();

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

            ammoSlotsHotBarEnabled.SettingChanged += (s, e) => AmmoSlotsHotBar.MarkDirty();

            ammoSlotHotKey1 = config("Hotkeys", "Ammo 1", new KeyboardShortcut(KeyCode.Alpha1, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            ammoSlotHotKey2 = config("Hotkeys", "Ammo 2", new KeyboardShortcut(KeyCode.Alpha2, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            ammoSlotHotKey3 = config("Hotkeys", "Ammo 3", new KeyboardShortcut(KeyCode.Alpha3, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");

            ammoSlotHotKey1Text = config("Hotkeys", "Ammo 1 Text", "Alt + 1", "Hotkey 1 Display Text. Leave blank to use the hotkey itself.");
            ammoSlotHotKey2Text = config("Hotkeys", "Ammo 2 Text", "Alt + 2", "Hotkey 2 Display Text. Leave blank to use the hotkey itself.");
            ammoSlotHotKey3Text = config("Hotkeys", "Ammo 3 Text", "Alt + 3", "Hotkey 3 Display Text. Leave blank to use the hotkey itself.");

            ammoSlotHotKey1.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();
            ammoSlotHotKey2.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();
            ammoSlotHotKey3.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();

            quickSlotsHotBarEnabled = config("Panels - Quick slots", "Enabled", defaultValue: true, "Enable hotbar with quick slots");
            quickSlotsHotBarOffset = config("Panels - Quick slots", "Offset", defaultValue: new Vector2(230f, 923f), "On screen position of quick slots hotbar panel");
            quickSlotsHotBarScale = config("Panels - Quick slots", "Scale", defaultValue: 1f, "Relative size");
            quickSlotsShowLabel = config("Panels - Quick slots", "Show label", defaultValue: false, "Show slot label");
            quickSlotsShowHintImage = config("Panels - Quick slots", "Show hint image", defaultValue: true, "Show slot background hint image");
            quickSlotsShowTooltip = config("Panels - Quick slots", "Show help tooltip", defaultValue: true, "Show tooltip with slot info");

            quickSlotsHotBarEnabled.SettingChanged += (s, e) => QuickSlotsHotBar.MarkDirty();

            quickSlotHotKey1 = config("Hotkeys", "Quickslot 1", new KeyboardShortcut(KeyCode.Z, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey2 = config("Hotkeys", "Quickslot 2", new KeyboardShortcut(KeyCode.X, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey3 = config("Hotkeys", "Quickslot 3", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey4 = config("Hotkeys", "Quickslot 4", new KeyboardShortcut(KeyCode.V, KeyCode.LeftAlt), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey5 = config("Hotkeys", "Quickslot 5", new KeyboardShortcut(KeyCode.B), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            quickSlotHotKey6 = config("Hotkeys", "Quickslot 6", new KeyboardShortcut(KeyCode.N), "https://docs.unity3d.com/Manual/ConventionalGameInput.html");

            quickSlotHotKey1.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey2.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey3.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey4.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey5.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();
            quickSlotHotKey6.SettingChanged += (s, e) => PreventSimilarHotkeys.FillSimilarHotkey();

            quickSlotHotKey1Text = config("Hotkeys", "Quickslot 1 Text", "Alt + Z", "Hotkey 1 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey2Text = config("Hotkeys", "Quickslot 2 Text", "Alt + X", "Hotkey 2 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey3Text = config("Hotkeys", "Quickslot 3 Text", "Alt + C", "Hotkey 3 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey4Text = config("Hotkeys", "Quickslot 4 Text", "Alt + V", "Hotkey 4 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey5Text = config("Hotkeys", "Quickslot 5 Text", "", "Hotkey 5 Display Text. Leave blank to use the hotkey itself.");
            quickSlotHotKey6Text = config("Hotkeys", "Quickslot 6 Text", "", "Hotkey 6 Display Text. Leave blank to use the hotkey itself.");

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
    }
}
