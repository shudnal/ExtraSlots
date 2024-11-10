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
                return;

            LoadInventory(EquipmentSlotInventory, equipItem: true, loadOnly: Player.m_localPlayer == null);
            
            LoadInventory(QuickSlotInventory, equipItem: false, loadOnly: Player.m_localPlayer == null);
        }

        private static void LoadInventory(Inventory inventory, bool equipItem, bool loadOnly)
        {
            if (LoadValue(playerToLoad, inventory.m_name, out string data))
            {
                var pkg = new ZPackage(data);
                inventory.Load(pkg);

                TransferItemsToPlayerInventory(inventory, equipItem);

                if (!loadOnly)
                {
                    pkg = new ZPackage();
                    inventory.Save(pkg);
                    SaveValue(playerToLoad, inventory.m_name, pkg.GetBase64());
                }
            }
        }

        private static void TransferItemsToPlayerInventory(Inventory fromInventory, bool equipItem)
        {
            foreach (ItemDrop.ItemData item in fromInventory.GetAllItemsInGridOrder().Where(item => item != null))
            {
                if (!(TryFindSlotForItem(item, out Slot slot) ? PlayerInventory.AddItem(item, slot.GridPosition) : PlayerInventory.AddItem(item)))
                {
                    if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
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
                }

                LogMessage($"Item {item.m_shared.m_name} was loaded from EquipmentAndQuickSlots {fromInventory.m_name}");

                if (equipItem)
                    playerToLoad.UseItem(playerToLoad.GetInventory(), item, false);
            }

            fromInventory.m_inventory.Clear();
        }

        private static bool TryFindSlotForItem(ItemDrop.ItemData item, out Slot slot) => TryFindFreeEquipmentSlotForItem(item, out slot) || TryFindFreeSlotForItem(item, out slot);

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
        public static class Player_Load_TryLoadEaQSData
        {
            [HarmonyPriority(Priority.LowerThanNormal)]
            public static void Postfix(Player __instance)
            {
                if (__instance.m_customData.ContainsKey(nameof(EquipmentSlotInventory)) && (FejdStartup.instance || IsValidPlayer(__instance)))
                {
                    playerToLoad = __instance;
                    Load();
                    playerToLoad = null;
                }
            }
        }
    }
}