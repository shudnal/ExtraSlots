using BepInEx.Configuration;
using HarmonyLib;
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
        private static readonly List<ItemDrop.ItemData> itemsToKeep = new List<ItemDrop.ItemData>();
        private static readonly HashSet<Slot> takenSlots = new HashSet<Slot>();

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
            if (slotsTombstoneAutoEquipCarryWeightItemsEnabled.Value && item != null && item.m_shared.m_equipStatusEffect is SE_Stats se && se.m_addMaxCarryWeight > 0)
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
            itemsToKeep.Clear();
            slots.DoIf(IsSlotToKeep, KeepItem);
            ClearCachedItems();

            SaveLastEquippedSlotsToItems();

            SaveLastEquippedWeaponShieldToItems(player);

            void KeepItem(Slot slot)
            {
                ItemDrop.ItemData item = slot.Item;

                itemsToKeep.Add(item);
                player.GetInventory().m_inventory.Remove(item);
                LogDebug($"Character.CheckDeath.Prefix: On death drop prevented for item {item.m_shared.m_name} from slot {slot}. Item temporary removed from player inventory.");
            }
        }

        public static bool IsSlotToKeep(Slot slot)
        {
            if (slot.IsFree)
                return false;

            bool keepSlot = slot.IsEquipmentSlot && keepOnDeathEquipmentSlots.Value ||
                            slot.IsQuickSlot && keepOnDeathQuickSlots.Value ||
                            slot.IsAmmoSlot && keepOnDeathAmmoSlots.Value ||
                            slot.IsFoodSlot && keepOnDeathFoodSlots.Value ||
                            slot.IsMiscSlot && keepOnDeathMiscSlots.Value;

            if (!keepSlot)
                return false;

            return ItemFitLists(slot.Item, keepItemList, keepItemWhiteList, keepItemBlackList);
        }

        public static void OnDeathPostfix(Player player)
        {
            if (itemsToKeep.Count == 0)
                return;

            player.GetInventory().m_inventory.AddRange(itemsToKeep);

            LogDebug($"Player.OnDeath.Postfix: {itemsToKeep.Count} item(s) returned to player inventory after preventing on death drop.");

            itemsToKeep.Clear();
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
                    LogDebug($"Destroyed tombstone component {__instance.m_body?.gameObject} to prevent NullReferenceException on AutoPickup");
                    floatingTerrain.m_lastHeightmap = null;
                    UnityEngine.Object.Destroy(__instance.m_body?.gameObject);
                }

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
                if (__instance.m_nview?.IsValid() == true && __instance.m_nview.IsOwner() && __instance.GetComponent<TombStone>() is not null && __instance.m_height > VanillaInventoryHeight)
                {
                    string typeName = __instance.GetType().Name;
                    __instance.m_nview.GetZDO().Set(ZNetView.CustomFieldsStr, true);
                    __instance.m_nview.GetZDO().Set((ZNetView.CustomFieldsStr + typeName).GetStableHashCode(), true);
                    __instance.m_nview.GetZDO().Set(typeName + "." + "m_height", InventoryHeightFull);
                    LogDebug($"TombStone Container Awake Postfix height {InventoryHeightFull} saved with {ZNetView.CustomFieldsStr}");
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
            private static float GetDynamicWeightChange(StatusEffect se)
            {
                float limit = 0f;
                se.ModifyMaxCarryWeight(0f, ref limit);
                return limit;
            }

            private static void Prefix(TombStone __instance, Player player, ref float __state)
            {
                if (!IsValidPlayer(player))
                    return;

                __state = (__instance.m_lootStatusEffect as SE_Stats)?.m_addMaxCarryWeight ?? 0f;

                if (slotsTombstoneAutoEquipCarryWeightItemsEnabled.Value || slotsTombstoneAutoEquipEnabled.Value)
                {
                    __state += __instance.m_container.GetInventory().GetAllItems()
                        .Where(item => item != null && item.m_shared.m_equipStatusEffect is SE_Stats se && GetSlotInGrid(item.m_gridPos) is Slot slot && slot.IsEquipmentSlot)
                        .Sum(item => GetDynamicWeightChange(item.m_shared.m_equipStatusEffect));
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

                int nrOfItems = 0; takenSlots.Clear();
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

        [HarmonyPatch(typeof(Character), nameof(Character.CheckDeath))]
        private static class Character_CheckDeath_OnDeathWrapping
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance)
            {
                if (!IsValidPlayer(__instance))
                    return;

                if (!__instance.IsDead() && __instance.GetHealth() <= 0f)
                    OnDeathPrefix(__instance as Player);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class Player_OnDeath_RestoreItemsToKeep
        {
            private static void Postfix(Player __instance)
            {
                if (!IsValidPlayer(__instance))
                    return;

                OnDeathPostfix(__instance);
            }
        }
    }
}
