using HarmonyLib;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;

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
            private static float megingjordCarryWeight = 0f;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(TombStone __instance, ref bool __state)
            {
                megingjordCarryWeight = (ObjectDB.instance?.GetStatusEffect(megingjordName.GetStableHashCode()) as SE_Stats)?.m_addMaxCarryWeight ?? 150f;

                __state = __instance.m_container.GetInventory().ContainsItemByName(megingjordName);
                if (__state)
                    Player.m_localPlayer.m_maxCarryWeight += megingjordCarryWeight;
            }

            private static void Postfix(bool __state)
            {
                if (__state)
                    Player.m_localPlayer.m_maxCarryWeight -= megingjordCarryWeight;
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
    }
}
