using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    internal static class QueuedEquipIndicator
    {
        private static readonly Dictionary<Image, float> baseAlphas = new Dictionary<Image, float>();

        private static bool IsFadeEnabled()
        {
            if (queuedEquipFade.Value)
                return true;

            // A live config change can happen while the current action is already partially faded.
            // Restore the source alpha before disabling the feature so the modified alpha can never
            // become the next cached baseline when fading is enabled again.
            if (baseAlphas.Count > 0)
                RestoreAndClearCache();

            return false;
        }

        internal static void Update(Image image, ItemDrop.ItemData item, Player player)
        {
            if (!IsFadeEnabled() || image == null || player == null || item == null || player.m_actionQueue.Count == 0)
                return;

            if (!TryGetQueuedAction(player, item, out Player.MinorActionData action, out int actionIndex))
                return;

            Update(image, action, actionIndex);
        }

        private static void Update(Image image, Player.MinorActionData action, int actionIndex)
        {
            float baseAlpha = GetBaseAlpha(image);
            Color color = image.color;
            color.a = baseAlpha * GetAlpha(action, actionIndex);
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

        internal static void Update(GameObject queuedObject, ItemDrop.ItemData item, Player player)
        {
            if (!IsFadeEnabled() || queuedObject == null || player == null || item == null || player.m_actionQueue.Count == 0)
                return;

            Update(queuedObject.GetComponent<Image>(), item, player);
        }

        private static bool TryGetQueuedAction(Player player, ItemDrop.ItemData item, out Player.MinorActionData action, out int actionIndex)
        {
            action = null;
            actionIndex = -1;

            for (int i = 0; i < player.m_actionQueue.Count; i++)
            {
                Player.MinorActionData queuedAction = player.m_actionQueue[i];
                if (!ReferenceEquals(queuedAction.m_item, item)
                    || queuedAction.m_type != Player.MinorActionData.ActionType.Equip
                    && queuedAction.m_type != Player.MinorActionData.ActionType.Unequip)
                    continue;

                action = queuedAction;
                actionIndex = i;
                return true;
            }

            return false;
        }

        private static float GetAlpha(Player.MinorActionData action, int actionIndex)
        {
            if (actionIndex != 0 || action.m_duration <= 0f)
                return 1f;

            return 1f - Mathf.Clamp01(action.m_time / action.m_duration);
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        private static class InventoryGrid_UpdateGui_FadeQueuedIndicator
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(InventoryGrid __instance, Player player)
            {
                if (!IsFadeEnabled())
                    return;

                if (!player || player.m_actionQueue.Count == 0 || __instance?.m_inventory == null || __instance.m_inventory != player.GetInventory())
                    return;

                foreach (InventoryGrid.Element element in __instance.m_elements)
                {
                    if (element?.m_queued == null || element.m_equiped == null || !element.m_used || !element.m_queued.enabled)
                        continue;

                    ItemDrop.ItemData item = __instance.m_inventory.GetItemAt(element.m_pos.x, element.m_pos.y);
                    if (item == null || !TryGetQueuedAction(player, item, out Player.MinorActionData action, out int actionIndex))
                        continue;

                    element.m_equiped.enabled = action.m_type == Player.MinorActionData.ActionType.Equip;
                    Update(element.m_queued, action, actionIndex);
                }
            }
        }

        [HarmonyPatch]
        private static class QuickBars_UpdateQueuedIndicators_UpdateEquippedState
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(HotBars.QuickBars), "UpdateQueuedIndicators");

            private static void Postfix(HotkeyBar bar, Player player)
            {
                if (!IsFadeEnabled() || !bar || player == null || player.m_actionQueue.Count == 0)
                    return;

                int slotOffset;
                if (bar.name == HotBars.AmmoSlotsHotBar.barName)
                    slotOffset = HotBars.AmmoSlotsHotBar.barSlotIndex;
                else if (bar.name == HotBars.FoodSlotsHotBar.barName)
                    slotOffset = HotBars.FoodSlotsHotBar.barSlotIndex;
                else if (bar.name == HotBars.QuickSlotsHotBar.barName)
                    slotOffset = HotBars.QuickSlotsHotBar.barSlotIndex;
                else
                    return;

                for (int i = 0; i < bar.m_elements.Count; i++)
                {
                    HotkeyBar.ElementData element = bar.m_elements[i];
                    int slotIndex = i + slotOffset;
                    if (element?.m_equiped == null || slotIndex < 0 || slotIndex >= slots.Length)
                        continue;

                    Slot slot = slots[slotIndex];
                    ItemDrop.ItemData item = slot?.IsActive == true ? slot.Item : null;
                    if (item == null || !TryGetQueuedAction(player, item, out Player.MinorActionData action, out _))
                        continue;

                    // HotkeyBar.ElementData.m_equiped is a GameObject, unlike InventoryGrid's Image.
                    element.m_equiped.SetActive(action.m_type == Player.MinorActionData.ActionType.Equip);
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

        private static void ClearCache() => RestoreAndClearCache();

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        private static class Hud_OnDestroy_ClearQueuedIndicatorCache
        {
            private static void Postfix() => ClearCache();
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnDestroy))]
        private static class InventoryGui_OnDestroy_ClearQueuedIndicatorCache
        {
            private static void Postfix() => ClearCache();
        }
    }
}
