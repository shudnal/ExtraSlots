using HarmonyLib;
using System.Linq;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    internal class EquipmentAndQuickSlotsCompat
    {
        public const int QuickSlotCount = 3;
        public const int EquipSlotCount = 5;

        public static Inventory QuickSlotInventory = new Inventory(nameof(QuickSlotInventory), null, QuickSlotCount, 1);
        public static Inventory EquipmentSlotInventory = new Inventory(nameof(EquipmentSlotInventory), null, EquipSlotCount, 1);

        internal static Player playerToLoad;

        public const string Sentinel = "<|>";

        public static void Load()
        {
            if (playerToLoad == null)
            {
                LogWarning("Tried to load an ExtendedPlayerData with a null player!");
                return;
            }

            if (LoadValue(playerToLoad, nameof(QuickSlotInventory), out string quickSlotData))
            {
                var pkg = new ZPackage(quickSlotData);
                QuickSlotInventory.Load(pkg);

                TransferItemsToPlayerInventory(QuickSlotInventory, equipItem: false);

                pkg = new ZPackage();
                QuickSlotInventory.Save(pkg);
                SaveValue(playerToLoad, nameof(QuickSlotInventory), pkg.GetBase64());
            }

            if (LoadValue(playerToLoad, nameof(EquipmentSlotInventory), out string equipSlotData))
            {
                var pkg = new ZPackage(equipSlotData);
                EquipmentSlotInventory.Load(pkg);

                TransferItemsToPlayerInventory(EquipmentSlotInventory, equipItem: true);

                pkg = new ZPackage();
                EquipmentSlotInventory.Save(pkg);
                SaveValue(playerToLoad, nameof(EquipmentSlotInventory), pkg.GetBase64());
            }

            static void TransferItemsToPlayerInventory(Inventory fromInventory, bool equipItem)
            {
                foreach (ItemDrop.ItemData item in fromInventory.GetAllItemsInGridOrder().Where(item => item != null))
                {
                    if (!(TryFindFreeSlotForItem(item, out Slot slot) ? PlayerInventory.AddItem(item, slot.GridPosition) : PlayerInventory.AddItem(item)))
                    {
                        if (TryMakeFreeSpaceInPlayerInventory(out Vector2i gridPos))
                        {
                            LogInfo($"Item {item.m_shared.m_name} from EaQS was put to created free space {gridPos}");
                            item.m_gridPos = gridPos;
                        }
                        else
                        {
                            // Put item out of grid. Eventually it will be put into first free slot.
                            LogWarning($"Item {item.m_shared.m_name} was temporary put out of grid. It will return to inventory first free slot.");
                            item.m_gridPos = new Vector2i(0, InventoryHeightFull);
                        }

                        PlayerInventory.m_inventory.Add(item);
                        LogInfo($"Item {item.m_shared.m_name} from EquipmentAndQuickSlots was moved into regular inventory");
                    }

                    if (equipItem)
                        playerToLoad.UseItem(playerToLoad.GetInventory(), item, false);
                }
            }
        }

        private static bool LoadValue(Player player, string key, out string value)
        {
            if (player.m_customData.TryGetValue(key, out value))
                return true;

            var foundInKnownTexts = player.m_knownTexts.TryGetValue(key, out _);
            if (!foundInKnownTexts)
                key = Sentinel + key;

            foundInKnownTexts = player.m_knownTexts.TryGetValue(key, out value);
            if (foundInKnownTexts)
                LogWarning("Loaded data from knownTexts. Will be converted to customData on save.");

            return foundInKnownTexts;
        }

        private static void SaveValue(Player player, string key, string value)
        {
            if (player.m_knownTexts.ContainsKey(key))
            {
                LogWarning("Found KnownText for save data, converting to customData");
                player.m_knownTexts.Remove(key);
            }

            if (player.m_customData.ContainsKey(key))
                player.m_customData[key] = value;
            else
                player.m_customData.Add(key, value);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load_TryLoadEaQSInventories
        {
            static void Postfix(Player __instance)
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("randyknapp.mods.equipmentandquickslots", out _))
                {
                    if (FejdStartup.instance || IsValidPlayer(__instance))
                    {
                        playerToLoad = __instance;
                        Load();
                        playerToLoad = null;
                    }
                }
            }
        }
    }
}
