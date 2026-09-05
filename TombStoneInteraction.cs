using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class TombStoneInteraction
    {
        private class KeptItem
        {
            public ItemDrop.ItemData item;
            public bool wasEquipped;
        }

        private static readonly List<KeptItem> itemsToKeep = new List<KeptItem>();
        private static IDisposable keepItemsMutation;

        public static List<string> autoEquipItemList = new List<string>();
        public static List<string> autoEquipWhiteList = new List<string>();
        public static List<string> autoEquipBlackList = new List<string>();

        public static List<string> keepItemList = new List<string>();
        public static List<string> keepItemWhiteList = new List<string>();
        public static List<string> keepItemBlackList = new List<string>();

        public static readonly int AfterdeathGhost = "Afterdeath Ghost".GetStableHashCode();

        public static void UpdateItemLists()
        {
            UpdateItemList(autoEquipItemList, slotsTombstoneAutoEquipItemList);
            UpdateItemList(autoEquipWhiteList, slotsTombstoneAutoEquipWhiteList);
            UpdateItemList(autoEquipBlackList, slotsTombstoneAutoEquipBlackList);
            UpdateItemList(keepItemList, keepOnDeathItemList);
            UpdateItemList(keepItemWhiteList, keepOnDeathWhiteList);
            UpdateItemList(keepItemBlackList, keepOnDeathBlackList);
        }

        private static void UpdateItemList(List<string> list, ConfigEntry<string> config)
        {
            list.Clear();
            config.Value.Split(',').Select(p => p.GetItemName()).Where(p => !string.IsNullOrWhiteSpace(p)).Do(list.Add);
        }

        internal static bool ItemFitLists(ItemDrop.ItemData item, List<string> itemList, List<string> whiteList, List<string> blackList)
        {
            if (item == null)
                return true;

            if (itemList.Contains(item.m_shared.m_name.ToLower()))
                return true;

            if (whiteList.Contains(item.m_shared.m_name.ToLower()))
                return true;

            if (whiteList.Count > 0)
                return false;

            if (blackList.Contains(item.m_shared.m_name.ToLower()))
                return false;

            return true;
        }

        private static float GetCarryWeightChange(StatusEffect effect)
        {
            if (effect == null)
                return 0f;

            float value = 0f;
            effect.ModifyMaxCarryWeight(0f, ref value);
            return value;
        }

        private static float GetItemCarryWeightChange(ItemDrop.ItemData item) =>
            item?.m_shared?.m_equipStatusEffect != null ? GetCarryWeightChange(item.m_shared.m_equipStatusEffect) : 0f;

        private static bool CanGuaranteeAdditionalEquipEffect(Player player, ItemDrop.ItemData item, Slot destinationSlot)
        {
            if (player == null || item == null || destinationSlot == null || !destinationSlot.IsEquipmentSlot || !IsItemToEquip(item))
                return false;

            // Custom-slot equipment semantics belong to the provider mod. Do not assume its
            // EquipItem patch will add an effect without replacing another runtime item.
            if (IsCustomSlotItem(item))
                return false;

            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility)
            {
                if (player.m_utilityItem == null)
                    return true;

                int utilityIndex = ExtraUtilitySlots.GetSlotForItem(item);
                return utilityIndex >= 0 && ExtraUtilitySlots.GetItem(utilityIndex) == null;
            }

            // For vanilla single-instance equipment types, only count the incoming effect when
            // nothing of that type is currently equipped. Replacement deltas are intentionally
            // treated conservatively so EasyFit never relies on a bonus that may disappear.
            return !player.GetInventory().m_inventory.Any(existing => existing != null
                && !ReferenceEquals(existing, item)
                && player.IsItemEquiped(existing)
                && existing.m_shared.m_itemType == item.m_shared.m_itemType);
        }

        private static bool CanAccountForAutoEquipCarryEffect(Player player, ItemDrop.ItemData item, Slot destinationSlot, out float carryDelta)
        {
            carryDelta = 0f;
            if (player == null || item == null || destinationSlot == null || !IsItemToEquip(item))
                return true;

            StatusEffect incomingEffect = item.m_shared?.m_equipStatusEffect;
            float incomingDelta = GetCarryWeightChange(incomingEffect);
            if (CanGuaranteeAdditionalEquipEffect(player, item, destinationSlot))
            {
                carryDelta = incomingDelta;
                return true;
            }

            // If auto-equip may replace an already equipped item, the exact post-equip carry limit
            // depends on provider/vanilla replacement semantics. Never let EasyFit rely on a carry
            // delta we cannot prove. Zero-delta replacements are safe only when the displaced
            // same-type equipment also has no carry effect.
            if (Mathf.Abs(incomingDelta) > 0.0001f || IsCustomSlotItem(item))
                return false;

            return !player.GetInventory().m_inventory.Any(existing => existing != null
                && !ReferenceEquals(existing, item)
                && player.IsItemEquiped(existing)
                && existing.m_shared.m_itemType == item.m_shared.m_itemType
                && Mathf.Abs(GetItemCarryWeightChange(existing)) > 0.0001f);
        }

        public static IEnumerator EquipItemsInSlots()
        {
            ClearCachedItems();
            foreach (Slot slot in GetEquipmentSlots(onlyActive: false).ToList())
            {
                var item = slot.Item;

                if (IsItemToEquip(item))
                    TryEquipItem(item);

                yield return null;
            }
        }

        private static bool IsItemToEquip(ItemDrop.ItemData item)
        {
            if (slotsTombstoneAutoEquipCarryWeightItemsEnabled.Value && GetItemCarryWeightChange(item) > 0f)
                return true;

            if (!slotsTombstoneAutoEquipEnabled.Value)
                return false;

            return ItemFitLists(item, autoEquipItemList, autoEquipWhiteList, autoEquipBlackList);
        }

        private static bool IsWeaponShieldToEquip(ItemDrop.ItemData item) => slotsTombstoneAutoEquipWeaponShield.Value &&
                item.m_customData.TryGetValue(customKeyWeaponShield, out string value) && value == Game.instance.GetPlayerProfile().GetPlayerID().ToString();

        private static void TryEquipItem(ItemDrop.ItemData item)
        {
            if (item != null && !CurrentPlayer.IsItemEquiped(item))
                if (CurrentPlayer.EquipItem(item))
                    LogDebug($"Item {item.m_shared.m_name} was equipped on tombstone interaction");
        }

        public static IEnumerator EquipWeaponShield()
        {
            var items = PlayerInventory.GetAllItems()
                .Where(IsWeaponShieldToEquip)
                .ToList();

            foreach (var item in items)
            {
                TryEquipItem(item);
                yield return null;
            }
        }

        public static void OnDeathPrefix(Player player)
        {
            if (itemsToKeep.Count != 0)
            {
                LogDebug($"Character.CheckDeath.Prefix: skipped because {itemsToKeep.Count} item(s) are already pending death cleanup.");
                return;
            }

            SaveLastEquippedSlotsToItems();
            SaveLastEquippedWeaponShieldToItems(player);

            List<Slot> slotsToKeep = slots.Where(IsSlotToKeep).ToList();
            if (slotsToKeep.Count == 0)
                return;

            keepItemsMutation ??= PlayerInventoryOperations.Batch(player.GetInventory());
            foreach (Slot slot in slotsToKeep)
            {
                ItemDrop.ItemData item = slot.Item;
                if (item == null)
                    continue;

                itemsToKeep.Add(new KeptItem
                {
                    item = item,
                    wasEquipped = item.m_equipped || player.IsItemEquiped(item)
                });

                if (PlayerInventoryOperations.Remove(item))
                    LogDebug($"Character.CheckDeath.Prefix: On death drop prevented for item {item.m_shared.m_name} from slot {slot}. Item temporarily removed from player inventory.");
            }

            ClearCachedItems();
        }

        public static bool IsSlotToKeep(Slot slot)
        {
            if (slot.Item is not ItemDrop.ItemData item)
                return false;

            bool keepSlot = slot.IsEquipmentSlot && keepOnDeathEquipmentSlots.Value ||
                            slot.IsQuickSlot && keepOnDeathQuickSlots.Value ||
                            slot.IsAmmoSlot && keepOnDeathAmmoSlots.Value ||
                            slot.IsFoodSlot && keepOnDeathFoodSlots.Value ||
                            slot.IsMiscSlot && keepOnDeathMiscSlots.Value ||
                            keepItemList.Contains(item.m_shared.m_name.ToLower());

            if (!keepSlot)
                return false;

            return ItemFitLists(item, keepItemList, keepItemWhiteList, keepItemBlackList);
        }

        public static void OnDeathPostfix(Player player, string reason = "unknown")
        {
            if (itemsToKeep.Count == 0)
            {
                keepItemsMutation?.Dispose();
                keepItemsMutation = null;
                return;
            }

            try
            {
                foreach (KeptItem keptItem in itemsToKeep)
                {
                    if (!PlayerInventoryOperations.InsertExisting(keptItem.item, keptItem.item.m_gridPos))
                    {
                        // Keeping the exact ItemData is more important than preserving a stale cell.
                        // The invariant pass below will settle any exceptional conflict safely.
                        PlayerInventoryOperations.InsertForReconciliation(keptItem.item, keptItem.item.m_gridPos);
                    }

                    if (keepOnDeathEquippedState.Value && keptItem.wasEquipped)
                        keptItem.item.m_equipped = true;
                }

                ClearCachedItems();
                ItemsSlotsValidation.ValidateItems();
                ItemsSlotsValidation.ValidateSlots();
                ItemsSlotsValidation.Validate();

                LogDebug($"Death wrapping cleanup from {reason}: {itemsToKeep.Count} item(s) returned to player inventory.");
            }
            finally
            {
                itemsToKeep.Clear();
                ClearCachedItems();
                keepItemsMutation?.Dispose();
                keepItemsMutation = null;
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.OnTakeAllSuccess))]
        private static class TombStone_OnTakeAllSuccess_AutoEquip
        {
            private static void Postfix(TombStone __instance)
            {
                if (PlayerInventory == null)
                    return;

                CurrentPlayer?.StartCoroutine(AutoEquipItemsOnTombstoneTakeAll());
            }
        }

        [HarmonyPatch(typeof(FloatingTerrain), nameof(FloatingTerrain.OnDestroy))]
        private static class FloatingTerrain_OnDestroy_DisableTombstonePickupDummy
        {
            private static void Prefix(FloatingTerrain __instance)
            {
                if (__instance == null || __instance.GetComponentInParent<TombStone>() == null)
                    return;

                // Destroying the dummy is deferred by Unity. Disable its pickup/physics surface
                // synchronously while the parent still exists. This covers manual Take All, automatic
                // recovery and scene unload without destroying the body of a still-live grave.
                if (__instance.m_dummyCollider)
                    __instance.m_dummyCollider.enabled = false;
                if (__instance.m_dummyBody)
                    __instance.m_dummyBody.detectCollisions = false;

                if (__instance.m_dummy)
                    __instance.m_dummy.gameObject.SetActive(false);
                else if (__instance.m_dummyBody)
                    __instance.m_dummyBody.gameObject.SetActive(false);

                // Leave the references intact for FloatingTerrain.OnDestroy to destroy the dummy.
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTakeAll))]
        private static class InventoryGui_OnTakeAll_TombstoneAutoEquip
        {
            private static void Prefix(InventoryGui __instance, ref long __state)
            {
                __state = -1L;
                if (!slotsTombstoneAutoEquipManualTakeAll.Value
                    || __instance.m_currentContainer == null
                    || __instance.m_currentContainer.GetComponentInParent<TombStone>() == null)
                    return;

                __state = __instance.m_currentContainer.GetInventory().GetAllItems().Sum(item => (long)item.m_stack);
            }

            private static void Postfix(InventoryGui __instance, long __state)
            {
                if (__state < 0 || __instance.m_currentContainer == null)
                    return;

                long remaining = __instance.m_currentContainer.GetInventory().GetAllItems().Sum(item => (long)item.m_stack);
                if (remaining < __state)
                    CurrentPlayer?.StartCoroutine(AutoEquipItemsOnTombstoneTakeAll());
            }
        }

        public static IEnumerator AutoEquipItemsOnTombstoneTakeAll()
        {
            float timeoutTime = Time.time + 5f;

            yield return new WaitUntil(() =>
                CurrentPlayer?.GetSEMan()?.HaveStatusEffect(AfterdeathGhost) == false && CurrentPlayer?.IsDead() == false || Time.time >= timeoutTime);

            yield return null;

            yield return EquipItemsInSlots();

            yield return EquipWeaponShield();
        }

        private static bool PersistTombstoneDimensions(Container container, int width, int height)
        {
            if (container?.m_nview?.IsValid() != true || !container.m_nview.IsOwner() || container.GetComponentInParent<TombStone>() == null)
                return false;

            container.m_width = Mathf.Max(container.m_width, width);
            container.m_height = Mathf.Max(container.m_height, height);

            string typeName = container.GetType().Name;
            ZDO zdo = container.m_nview.GetZDO();
            zdo.Set(ZNetView.CustomFieldsStr, true);
            zdo.Set((ZNetView.CustomFieldsStr + typeName).GetStableHashCode(), true);
            zdo.Set(typeName + ".m_width", container.m_width);
            zdo.Set(typeName + ".m_height", container.m_height);
            return true;
        }

        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static class Container_Awake_TombstoneContainerHeightAdjustment
        {
            private static void Prefix(Container __instance)
            {
                // Patch tombstone container to always fit player inventory even with custom tombstone container size
                if (__instance.m_name is not "Grave" && !__instance.GetComponentInParent<TombStone>())
                    return;

                int targetHeight = GetTargetInventoryHeight(InventorySizeFull, __instance.m_width);
                // Let it be if height is sufficient
                if (targetHeight > __instance.m_height)
                {
                    LogDebug($"TombStone Container Awake height {__instance.m_height} -> {targetHeight}");
                    __instance.m_height = targetHeight;
                }
                else
                {
                    LogDebug($"TombStone Container Awake current height {__instance.m_height}, target height {targetHeight}");
                }
            }

            private static void Postfix(Container __instance)
            {
                if (__instance.GetComponentInParent<TombStone>() == null)
                    return;

                if (PersistTombstoneDimensions(__instance, __instance.m_width, __instance.m_height))
                    LogDebug($"TombStone Container Awake dimensions {__instance.m_width}x{__instance.m_height} saved with {ZNetView.CustomFieldsStr}");
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
        private static class TombStone_Setup_PersistDimensions
        {
            private static void Postfix(TombStone __instance)
            {
                Container container = __instance.m_container != null ? __instance.m_container : __instance.GetComponent<Container>();
                if (container?.m_inventory == null)
                    return;

                PersistTombstoneDimensions(container, container.m_inventory.m_width, container.m_inventory.m_height);
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

                int targetHeight = Mathf.Max(
                    GetTargetInventoryHeight(InventorySizeFull, __instance.m_container.m_width),
                    __instance.m_container.m_inventory?.m_height ?? 0);
                if (targetHeight > __instance.m_container.m_height)
                {
                    LogDebug($"TombStone Interact height {__instance.m_container.m_height} -> {targetHeight}. Inventory reloaded.");
                    __instance.m_container.m_height = targetHeight;
                    __instance.m_container.m_inventory.m_height = targetHeight;

                    __instance.m_container.m_lastRevision = 0;
                    __instance.m_container.m_lastDataString = "";
                    __instance.m_container.Load();
                }

                PersistTombstoneDimensions(__instance.m_container, __instance.m_container.m_inventory?.m_width ?? __instance.m_container.m_width, __instance.m_container.m_inventory?.m_height ?? __instance.m_container.m_height);
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory))]
        private static class TombStone_EasyFitInInventory_ExactSimulation
        {
            private static void Postfix(TombStone __instance, Player player, ref bool __result)
            {
                if (!IsValidPlayer(player))
                    return;

                Inventory tombstoneInventory = __instance.m_container?.GetInventory();
                Inventory playerInventory = player.GetInventory();
                if (tombstoneInventory == null || playerInventory == null)
                {
                    __result = false;
                    return;
                }

                // Simulate the real two-pass Inventory.MoveAll behavior, stack consumption and the
                // final ExtraSlots placement invariant. A positive result therefore means every
                // incoming stack has a concrete, semantically valid destination.
                if (!PlayerInventoryOperations.CanFitItems(
                        tombstoneInventory.GetAllItems(),
                        out List<PlayerInventoryOperations.SimulatedEquipmentPlacement> equipmentPlacements))
                {
                    __result = false;
                    return;
                }

                float effectiveMaxCarryWeight = player.GetMaxCarryWeight();
                HashSet<int> accountedEffects = new HashSet<int>();

                // GetMaxCarryWeight already includes every active status effect, not just equipment.
                // A second grave refreshes an active Corpse Run; it does not grant another copy.
                foreach (StatusEffect activeEffect in player.GetSEMan().GetStatusEffects())
                    if (activeEffect != null)
                        accountedEffects.Add(activeEffect.NameHash());

                foreach (ItemDrop.ItemData equipped in playerInventory.m_inventory)
                {
                    if (equipped == null || !player.IsItemEquiped(equipped) || equipped.m_shared.m_equipStatusEffect == null)
                        continue;

                    accountedEffects.Add(equipped.m_shared.m_equipStatusEffect.NameHash());
                }

                if (__instance.m_lootStatusEffect != null)
                {
                    int effectHash = __instance.m_lootStatusEffect.NameHash();
                    if (accountedEffects.Add(effectHash))
                        effectiveMaxCarryWeight += GetCarryWeightChange(__instance.m_lootStatusEffect);
                }

                foreach (PlayerInventoryOperations.SimulatedEquipmentPlacement placement in equipmentPlacements)
                {
                    ItemDrop.ItemData item = placement.Item;
                    if (item == null || !IsItemToEquip(item))
                        continue;

                    if (!CanAccountForAutoEquipCarryEffect(player, item, placement.Slot, out float carryDelta))
                    {
                        __result = false;
                        return;
                    }

                    StatusEffect effect = item.m_shared?.m_equipStatusEffect;
                    if (effect != null && accountedEffects.Add(effect.NameHash()))
                        effectiveMaxCarryWeight += carryDelta;
                }

                __result = playerInventory.GetTotalWeight() + tombstoneInventory.GetTotalWeight() <= effectiveMaxCarryWeight;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.CheckDeath))]
        private static class Character_CheckDeath_OnDeathWrapping
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance)
            {
                if (!IsValidPlayer(__instance))
                    return;

                if (!__instance.IsDead() && __instance.GetHealth() <= 0f)
                    OnDeathPrefix((Player)__instance); // remove items before other mods can touch inventory
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Character __instance, Exception __exception)
            {
                if (IsValidPlayer(__instance))
                    OnDeathPostfix((Player)__instance, "Character.CheckDeath.Finalizer");

                return __exception;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class Player_OnDeath_RestoreItemsToKeep
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance)
            {
                if (!IsValidPlayer(__instance))
                    return;

                OnDeathPostfix(__instance, "Player.OnDeath.Postfix");
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Player __instance, Exception __exception)
            {
                if (IsValidPlayer(__instance))
                    OnDeathPostfix(__instance, "Player.OnDeath.Finalizer");

                return __exception;
            }
        }
    }
}
