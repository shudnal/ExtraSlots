using HarmonyLib;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;
using System.Collections.Generic;

namespace ExtraSlots
{
    internal static class TombStoneInteraction
    {
        private const string megingjordName = "BeltStrength";
        
        [HarmonyPatch(typeof(TombStone), nameof(TombStone.OnTakeAllSuccess))]
        private static class TombStone_OnTakeAllSuccess_CheckMegingjordAutoEquip
        {
            private static void Prefix()
            {
                if (PlayerInventory == null)
                    return;

                ItemDrop.ItemData belt = PlayerInventory.GetItem(megingjordName, isPrefabName: true);
                if (belt != null && !CurrentPlayer.IsItemEquiped(belt))
                    CurrentPlayer.EquipItem(belt);
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
                    LogInfo($"TombStone Container Awake height {__instance.m_height} -> {targetHeight}");
                    __instance.m_height = targetHeight;
                }
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
        private static class TombStone_Interact_HeightAdjustment
        {
            private static void Prefix(TombStone __instance, bool hold)
            {
                if (hold)
                    return;

                int targetHeight = GetTargetInventoryHeight(InventorySizeFull, __instance.m_container.m_width);

                if (targetHeight > __instance.m_container.m_height)
                {
                    LogInfo($"TombStone Interact height {__instance.m_container.m_height} -> {targetHeight}. Inventory reloaded.");
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
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.RPC_TakeAllRespons))]
        private static class Container_RPC_TakeAllRespons_AutoPickupPreventNRE
        {
            private static void Prefix(Container __instance, bool granted, ref bool __state)
            {
                // Check only tombstone container causing NRE
                if (!granted || !__instance.GetComponent<TombStone>())
                    return;

                // Game version 0.219.13 Bog Witch, Player.AutoPickup NullReferenceException prevention
                if (__state = Player.m_enableAutoPickup)
                    Player.m_enableAutoPickup = false; 
            }

            private static void Postfix(bool __state)
            {
                if (__state)
                    Player.m_enableAutoPickup = true;
            }
        }
    }
}
