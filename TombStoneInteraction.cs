using HarmonyLib;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;
using System.Collections.Generic;
using System.Linq;

namespace ExtraSlots
{
    public static class TombStoneInteraction
    {
        public static void EquipItemsInSlots()
        {
            ClearCachedItems();
            GetEquipmentSlots(onlyActive: false).Select(slot => slot.Item).Where(IsItemToEquip).Do(TryEquipItem);
        }

        private static bool IsItemToEquip(ItemDrop.ItemData item) => slotsTombstoneAutoEquipEnabled.Value ||
                   slotsTombstoneAutoEquipCarryWeightItemsEnabled.Value && item != null && item.m_shared.m_equipStatusEffect is SE_Stats se && se.m_addMaxCarryWeight > 0;
        
        private static bool IsWeaponShieldToEquip(ItemDrop.ItemData item) => slotsTombstoneAutoEquipWeaponShield.Value && 
                item.m_customData.TryGetValue(customKeyWeaponShield, out string value) && value == Game.instance.GetPlayerProfile().GetPlayerID().ToString();

        private static void TryEquipItem(ItemDrop.ItemData item)
        {
            if (item != null && !CurrentPlayer.IsItemEquiped(item))
                if (CurrentPlayer.EquipItem(item))
                    LogDebug($"Item {item.m_shared.m_name} was equipped on tombstone interaction");
        }

        public static void EquipWeaponShield()
        {
            PlayerInventory.GetAllItems().Where(IsWeaponShieldToEquip).Do(TryEquipItem);
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.OnTakeAllSuccess))]
        private static class TombStone_OnTakeAllSuccess_AutoEquip
        {
            private static void Postfix(TombStone __instance)
            {
                if (PlayerInventory == null)
                    return;

                if (Player.m_enableAutoPickup && __instance.m_body.transform.root.gameObject != __instance.gameObject && __instance.TryGetComponent(out FloatingTerrain floatingTerrain))
                {
                    LogDebug($"Destroyed tombstone component {__instance.m_body?.gameObject} to prevent NRE on AutoPickup");
                    floatingTerrain.m_lastHeightmap = null;
                    UnityEngine.Object.Destroy(__instance.m_body?.gameObject);
                }

                if (!slotsTombstoneAutoEquipEnabled.Value && !slotsTombstoneAutoEquipCarryWeightItemsEnabled.Value)
                    return;

                EquipItemsInSlots();

                EquipWeaponShield();
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static class Container_Awake_TombstoneContainerHeightAdjustment
        {
            private static void Prefix(Container __instance)
            {
                // Patch tombstone container to always fit player inventory even with custom tombstone container size
                if (!__instance.GetComponent<TombStone>())
                    return;

                int targetHeight = GetTargetInventoryHeight(InventorySizeFull, __instance.m_width);
                // Let it be if height is sufficient
                if (targetHeight > __instance.m_height)
                {
                    LogDebug($"TombStone Container Awake height {__instance.m_height} -> {targetHeight}");
                    __instance.m_height = targetHeight;
                }
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
        internal static class TombStone_Interact_AdjustHeightAndStopAutoPickup
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(TombStone __instance, bool hold)
            {
                if (hold)
                    return;

                int targetHeight = GetTargetInventoryHeight(InventorySizeFull, __instance.m_container.m_width);
                if (targetHeight > __instance.m_container.m_height)
                {
                    LogDebug($"TombStone Interact height {__instance.m_container.m_height} -> {targetHeight}. Inventory reloaded.");
                    __instance.m_container.m_height = targetHeight;
                    __instance.m_container.m_inventory.m_height = targetHeight;

                    __instance.m_container.m_lastRevision = 0;
                    __instance.m_container.m_lastDataString = "";
                    __instance.m_container.Load();
                }
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory))]
        private static class TombStone_EasyFitInInventory_HeightAdjustment
        {
            private static void Prefix(TombStone __instance, Player player, ref float __state)
            {
                if (!IsValidPlayer(player))
                    return;

                __state = (__instance.m_lootStatusEffect as SE_Stats)?.m_addMaxCarryWeight ?? 0f;

                if (slotsTombstoneAutoEquipCarryWeightItemsEnabled.Value || slotsTombstoneAutoEquipEnabled.Value)
                {
                    __state += __instance.m_container.GetInventory().GetAllItems()
                        .Where(item => item != null && item.m_shared.m_equipStatusEffect is SE_Stats se && se.m_addMaxCarryWeight > 0 && GetSlotInGrid(item.m_gridPos) is Slot slot && slot.IsEquipmentSlot)
                        .Sum(item => (item.m_shared.m_equipStatusEffect as SE_Stats).m_addMaxCarryWeight);
                };

                Player.m_localPlayer.m_maxCarryWeight += __state;
            }

            private static void Postfix(TombStone __instance, Player player, float __state, ref bool __result)
            {
                if (!IsValidPlayer(player))
                    return;

                Player.m_localPlayer.m_maxCarryWeight -= __state;
                if (__result)
                    return;

                if (__instance.m_container.GetInventory().NrOfItems() > InventorySizeActive)
                    return;

                int nrOfItems = 0; HashSet<Slot> takenSlots = new HashSet<Slot>();
                foreach (ItemDrop.ItemData item in __instance.m_container.GetInventory().GetAllItemsInGridOrder())
                {
                    if (item.m_gridPos.y < InventoryHeightPlayer)
                    {
                        nrOfItems++;
                        continue;
                    }

                    Slot slot = GetSlotInGrid(item.m_gridPos);
                    if (slot == null)
                    {
                        nrOfItems++;
                        continue;
                    }
                        
                    if (takenSlots.Contains(slot))
                    {
                        nrOfItems++;
                        continue;
                    }

                    takenSlots.Add(slot);
                    if (slot.IsQuickSlot)
                    {
                        nrOfItems++;
                        continue;
                    }

                    if (!slot.ItemFits(item))
                    {
                        nrOfItems++;
                        continue;
                    }

                    // Item position is slot, slot is free, item fits slot, slot is dedicated slot (not quick slot).
                    // At least this item will be moved into that slot on inventory transfer
                    // This item can be excluded from items amount counting
                }

                __result = nrOfItems <= PlayerInventory.GetEmptySlots() && player.GetInventory().GetTotalWeight() + __instance.m_container.GetInventory().GetTotalWeight() < player.GetMaxCarryWeight() + __state;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        private static class Player_CreateTombStone_SaveItemSlotsPosition
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance)
            {
                if (!IsValidPlayer(__instance))
                    return;

                SaveLastEquippedSlotsToItems();

                SaveLastEquippedWeaponShieldToItems(__instance);
            }
        }
    }
}
