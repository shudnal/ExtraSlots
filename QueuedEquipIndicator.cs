using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots
{
    internal static class QueuedEquipIndicator
    {
        private static readonly Dictionary<Image, float> baseAlphas = new Dictionary<Image, float>();

        internal static void Update(Image image, Image background, ItemDrop.ItemData item, Player player)
        {
            if (image == null || background == null)
                return;

            float baseAlpha = GetBaseAlpha(image);
            Color color = image.color;
            color.a = queuedEquipFade.Value ? baseAlpha * GetAlpha(player, item) : baseAlpha;
            image.color = color;
        }

        private static float GetBaseAlpha(Image image)
        {
            if (baseAlphas.TryGetValue(image, out float alpha))
                return alpha;

            if (baseAlphas.Count > 256)
                baseAlphas.Where(entry => !entry.Key).Select(entry => entry.Key).ToList().ForEach(key => baseAlphas.Remove(key));

            alpha = image.color.a;
            baseAlphas[image] = alpha;
            return alpha;
        }

        private static float GetAlpha(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null)
                return 1f;

            for (int i = 0; i < player.m_actionQueue.Count; i++)
            {
                Player.MinorActionData action = player.m_actionQueue[i];
                if (!ReferenceEquals(action.m_item, item)
                    || action.m_type != Player.MinorActionData.ActionType.Equip)
                    continue;

                if (i != 0 || action.m_duration <= 0f)
                    return 1f;

                return 1f - Mathf.Clamp01(action.m_time / action.m_duration);
            }

            return 1f;
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        private static class InventoryGrid_UpdateGui_FadeQueuedIndicator
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(InventoryGrid __instance, Player player)
            {
                if (!player || __instance?.m_inventory == null || __instance.m_inventory != player.GetInventory())
                    return;

                foreach (InventoryGrid.Element element in __instance.m_elements)
                {
                    if (element?.m_queued == null || !element.m_used || !element.m_queued.enabled || element?.m_equiped == null)
                        continue;

                    ItemDrop.ItemData item = __instance.m_inventory.GetItemAt(element.m_pos.x, element.m_pos.y);
                    Update(element.m_queued, element.m_equiped, item, player);
                }
            }
        }

        private static void RestoreAndClearCache()
        {
            foreach (KeyValuePair<Image, float> entry in baseAlphas)
            {
                if (!entry.Key)
                    continue;

                Color color = entry.Key.color;
                color.a = entry.Value;
                entry.Key.color = color;
            }

            baseAlphas.Clear();
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        private static class Hud_OnDestroy_ClearQueuedIndicatorCache
        {
            private static void Postfix() => RestoreAndClearCache();
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnDestroy))]
        private static class InventoryGui_OnDestroy_ClearQueuedIndicatorCache
        {
            private static void Postfix() => RestoreAndClearCache();
        }
    }
}
