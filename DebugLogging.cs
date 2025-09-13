using HarmonyLib;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    internal static class DebugLogging
    {
        internal static bool IsDebugEnabled => loggingEnabled.Value && loggingDebugEnabled.Value;

        internal static void LogItem(ItemDrop.ItemData item)
        {
            LogDebug($"{item.m_shared.m_name} {item.m_gridPos} " + (Slots.GetItemSlot(item) is Slots.Slot slot ? $"slot: {slot} {slot.GridPosition}" : ""));
        }

        internal static string GetInventoryState(Inventory inventory) => $"name:{inventory.m_name} isPlayer:{Player.m_localPlayer?.GetInventory() == inventory} size:{inventory.m_width}x{inventory.m_height} weight:{inventory.m_totalWeight} items:{inventory.m_inventory.Count}";

        internal static void LogInventory(this Inventory inventory) => inventory.GetAllItemsInGridOrder().Do(LogItem);

        [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        internal static class Player_CreateTombstone_LoggingItems
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance)
            {
                if (!IsDebugEnabled)
                    return;
                
                if (!IsValidPlayer(__instance))
                    return;

                LogDebug("Player.CreateTombStone:Prefix Player Item List:");
                __instance.GetInventory().LogInventory();
            }

            [HarmonyPriority(Priority.Last)]
            private static void Finalizer(Player __instance)
            {
                if (!IsDebugEnabled)
                    return;

                if (!IsValidPlayer(__instance))
                    return;

                LogDebug("Player.CreateTombStone:Finalizer Player Item List:");
                __instance.GetInventory().LogInventory();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave))]
        internal static class Inventory_MoveInventoryToGrave_LoggingItems
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, Inventory original)
            {
                if (!IsDebugEnabled)
                    return;

                LogDebug("Inventory.MoveInventoryToGrave:Prefix");
                LogDebug($"From inventory {GetInventoryState(original)}");
                LogInventory(original);
                LogDebug($"To inventory {GetInventoryState(__instance)}");
                LogInventory(__instance);
            }

            [HarmonyPriority(Priority.Last)]
            private static void Finalizer(Inventory __instance, Inventory original)
            {
                if (!IsDebugEnabled)
                    return;

                LogDebug("Inventory.MoveInventoryToGrave:Finalizer");
                LogDebug($"From inventory {GetInventoryState(original)}");
                LogInventory(original);
                LogDebug($"To inventory {GetInventoryState(__instance)}");
                LogInventory(__instance);
            }
        }
    }
}
