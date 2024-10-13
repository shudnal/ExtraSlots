using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    public static class ItemsSlotsValidation
    {
        private static bool PutIntoFirstEmptySlot(ItemDrop.ItemData item)
        {
            Vector2i gridPos = PlayerInventory.FindEmptySlot(true);
            if (gridPos.x > -1 && gridPos.y > -1)
            {
                LogInfo($"Item {item.m_shared.m_name} {item.m_gridPos} was put into first free slot {gridPos}");
                item.m_gridPos = gridPos;
                return true;
            }

            if (TryFindFreeSlotForItem(item, out Slot slot))
            {
                LogInfo($"Item {item.m_shared.m_name} {item.m_gridPos} was put into first free valid equipment slot {slot.GridPosition}");
                item.m_gridPos = slot.GridPosition;
                return true;
            }

            return false;
        }

        public static void ValidateSlots() => SlotsValidation.MarkDirty();
        public static void ValidateItems() => ItemsValidation.MarkDirty();

        internal static class SlotsValidation
        {
            private static bool isDirty = false;

            internal static void MarkDirty() => isDirty = true;

            internal static void Validate()
            {
                if (!isDirty || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
                    return;

                isDirty = false;

                for (int i = 0; i < slots.Length; i++)
                {
                    Slot slot = slots[i];
                    ItemDrop.ItemData item = slot.Item;
                    if (item == null || slot.ItemFit(item))
                        continue;
                
                    LogInfo($"Item {item.m_shared.m_name} unfits slot {slot}");
                
                    PutIntoFirstEmptySlot(item);
                }
            }

            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.SetupEquipment))]
            private static class Humanoid_SetupEquipment_MarkSlotsDirty
            {
                private static void Postfix(Humanoid __instance)
                {
                    if (__instance is Player player && IsValidPlayer(player) && !player.m_isLoading)
                        MarkDirty();
                }
            }

            [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
            private static class Player_OnInventoryChanged_ValidateInventory
            {
                private static void Postfix(Player __instance)
                {
                    if (IsValidPlayer(__instance) && !__instance.m_isLoading)
                        MarkDirty();
                }
            }
        }

        internal static class ItemsValidation
        {
            private static readonly HashSet<Vector2i> occupiedPositions = new HashSet<Vector2i>();
            private static readonly List<ItemDrop.ItemData> tempItems = new List<ItemDrop.ItemData>();

            private static bool isDirty = false;

            public static void Validate()
            {
                if (!isDirty || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading)
                    return;

                if (PlayerInventory == null || PlayerInventory.m_inventory == null)
                    return;

                PlayerInventory.m_inventory.RemoveAll(item => item == null || item.m_stack <= 0);

                occupiedPositions.Clear();
                tempItems.Clear();
                for (int index = 0; index < PlayerInventory.m_inventory.Count; index++)
                {
                    ItemDrop.ItemData item = PlayerInventory.m_inventory[index];
                    if (item == null)
                        continue;

                    if (ItemIsOverlapping(item))
                    {
                        LogWarning($"Item {item.m_shared.m_name} {item.m_gridPos} is overlapping other item");
                        tempItems.Add(item);
                    }
                    else if (ItemIsOutOfGrid(item))
                    {
                        LogWarning($"Item {item.m_shared.m_name} {item.m_gridPos} is out of inventory grid");
                        tempItems.Add(item);
                    }

                    occupiedPositions.Add(item.m_gridPos);
                }

                if (tempItems.Count(PutIntoFirstEmptySlot) > 0)
                    PlayerInventory.Changed();
            }

            private static bool ItemIsOverlapping(ItemDrop.ItemData itemData) => occupiedPositions.Contains(itemData.m_gridPos);

            private static bool ItemIsOutOfGrid(ItemDrop.ItemData itemData) => itemData.m_gridPos.x < 0 || itemData.m_gridPos.x >= InventoryWidth
                                                                            || itemData.m_gridPos.y < 0 || itemData.m_gridPos.y >= InventoryHeightFull;

            internal static void MarkDirty() => isDirty = true;

            [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
            internal static class Inventory_MoveAll_ValidateItemPositions
            {
                static void Postfix(Inventory __instance, Inventory fromInventory)
                {
                    if (__instance == PlayerInventory || fromInventory == PlayerInventory)
                        MarkDirty();
                }
            }

            [HarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory))]
            internal static class TombStone_EasyFitInInventory_ValidateItemPositions
            {
                static void Postfix(Player player)
                {
                    if (IsValidPlayer(player))
                        MarkDirty();
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        public static class InventoryGuiShowPatch
        {
            static void Postfix()
            {
                if (Player.m_localPlayer == null)
                    return;

                ValidateSlots();

                ValidateItems();
            }
        }
    }
}