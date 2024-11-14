using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    public static class EpicLootCompat
    {
        [HarmonyPatch]
        public static class EpicLoot_Player_GetEquipment_AddItemsFromExtraUtilitySlots
        {
            public static MethodBase target;

            public static bool Prepare()
            {
                if (!isEpicLootEnabled)
                    return false;

                target = AccessTools.Method("EpicLoot.PlayerExtensions:GetEquipment");
                if (target == null)
                    return false;

                LogInfo("EpicLoot.PlayerExtensions:GetEquipment method will be patched to add extra utility items");
                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Postfix(Player player, List<ItemDrop.ItemData> __result)
            {
                if (!Slots.IsValidPlayer(player))
                    return;

                __result.AddRange(ExtraUtilitySlots.GetEquippedItems());
            }
        }
    }
}
