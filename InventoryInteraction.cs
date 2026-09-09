using HarmonyLib;
using System;
using static ExtraSlots.Slots;
using static ExtraSlots.ExtraSlots;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ExtraSlots
{
    public static class InventoryInteraction
    {
        public static void UpdatePlayerInventorySize()
        {
            if (CurrentPlayer == null)
                return;

            if (CurrentPlayer.m_inventory.m_height != InventoryHeightFull)
            {
                LogInfo($"Player inventory height changed {CurrentPlayer.m_inventory.m_height} -> {InventoryHeightFull}");
                CurrentPlayer.m_inventory.m_height = InventoryHeightFull;
                CurrentPlayer.m_inventory.Changed();
            }
            
            if (CurrentPlayer.m_tombstone?.GetComponent<Container>() is Container tombstone)
                tombstone.m_height = Mathf.Max(tombstone.m_height, InventoryHeightFull);

            ClearCachedItems();
            ItemsSlotsValidation.ValidateItems();
            ItemsSlotsValidation.ValidateSlots();
        }

        public static float GetItemWeightFactor(Slot slot)
        {
            if (slot.IsEquipmentSlot)
                return itemWeightFactorEquipmentSlots.Value;

            if (slot.IsQuickSlot)
                return itemWeightFactorQuickSlots.Value;

            if (slot.IsAmmoSlot)
                return itemWeightFactorAmmoSlots.Value;

            if (slot.IsFoodSlot)
                return itemWeightFactorFoodSlots.Value;

            if (slot.IsMiscSlot)
                return itemWeightFactorMiscSlots.Value;

            return 1f;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
        private static class Player_Awake_SetInventoryHeight
        {
            private static void Postfix(Player __instance)
            {
                __instance.m_inventory.m_height = InventoryHeightFull;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
        private static class Player_SetInventorySize_PreserveExtraSlots
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance, int rows)
            {
                if (__instance != CurrentPlayer)
                    return true;

                using (PlayerInventoryOperations.Batch(__instance.GetInventory()))
                {
                    rows = Mathf.Clamp(rows, 0, 9);
                    __instance.AddUniqueKeyValue(Player.InventoryRowsKey, rows.ToString());
                    UpdateSlotsGridPosition();
                    LightenedSlots.UpdateState();
                    if (InventoryGui.instance)
                        InventoryGui.instance.SetInventorySize(rows);
                    EquipmentPanel.MarkDirty();
                    EquipmentPanel.UpdateSidePanels();
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetInventorySize))]
        private static class InventoryGui_SetInventorySize_PreservePanelBaseline
        {
            private static void Prefix(ref int rows)
            {
                // ExtraSlots extends the original four-row background/anchors itself. Letting the
                // native setter also expand the root would apply the visible-row delta twice.
                if (PlayerInventory != null)
                    rows = VanillaInventoryHeight;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropInvalidItems))]
        private static class Humanoid_DropInvalidItems_ReconcilePlayerItems
        {
            private static bool Prefix(Humanoid __instance)
            {
                if (__instance != CurrentPlayer)
                    return true;

                using (PlayerInventoryOperations.Batch(__instance.GetInventory()))
                {
                    PlayerInventoryOperations.EnsureCurrentGeometry();
                    ItemsSlotsValidation.ValidateItems();
                    ItemsSlotsValidation.ValidateSlots();
                    ItemsSlotsValidation.Validate();
                }
                // Deferred or temporarily staged player items must never become ground drops.
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned_UpdateInventoryOnSpawn
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                if (!IsAwaitingForSlotsUpdate())
                    UpdatePlayerInventorySize();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update_UpdateInventoryHeight
        {
            private static Container tombstoneContainer = null!;

            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                __instance.m_inventory.m_height = InventoryHeightFull;

                tombstoneContainer ??= __instance.m_tombstone.GetComponent<Container>();
                if (tombstoneContainer != null)
                    tombstoneContainer.m_height = Mathf.Max(tombstoneContainer.m_height, __instance.m_inventory.m_height);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        private static class Player_Save_SaveLastEquippedSlots
        {
            private static void Prefix(Player __instance)
            {
                if (__instance.GetInventory() != PlayerInventory)
                    return;

                SaveLastEquippedSlotsToItems();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
        public static class Player_AutoPickup_PreventAutoPickupInExtraSlots
        {
            public static bool preventAddItem = false;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance, out bool __state)
            {
                __state = preventAddItem;
                preventAddItem = preventAutoPickup.Value && __instance == CurrentPlayer;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(bool __state) => preventAddItem = __state;

            private static Exception Finalizer(bool __state, Exception __exception)
            {
                preventAddItem = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.SlotsUsedPercentage))]
        private static class Inventory_SlotsUsedPercentage_ExcludeRedundantSlots
        {
            private static void Postfix(Inventory __instance, ref float __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __result = (float)__instance.m_inventory.Count / InventorySizeActive * 100f;
                LogDebug($"Inventory.SlotsUsedPercentage: {__result}");
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
        private static class Inventory_GetEmptySlots_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref int __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __result = InventoryHeightPlayer * __instance.m_width - __instance.m_inventory.Count(item => !API.IsItemInSlot(item)) + (Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem ? 0 :GetEmptyQuickSlots());
                LogDebug($"Inventory.GetEmptySlots: {__result}, PreventAutoPickupInQuickSlots: {Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem}");
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.FindEmptySlot))]
        private static class Inventory_FindEmptySlot_FindAppropriateSlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightPlayer;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref Vector2i __result)
            {
                if (__instance != PlayerInventory)
                    return;

                __instance.m_height = InventoryHeightFull;

                bool upgradePrecheckBypass = Inventory_AddItem_ByName_FindAppropriateSlot.ConsumeUpgradePrecheckBypass();

                if (__result == emptyPosition
                    && Inventory_AddItem_ByName_FindAppropriateSlot.IsReplacementCall
                    && InventoryGui_DoCrafting_UpgradeInSlot.UpgradeSourceSlot is Slot sourceSlot
                    && Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot is ItemDrop.ItemData upgradeItem)
                {
                    sourceSlot.ClearItemCache();
                    if (sourceSlot.IsFree && sourceSlot.ItemFits(upgradeItem))
                    {
                        __result = sourceSlot.GridPosition;
                        LogDebug($"Inventory.FindEmptySlot for upgraded item {upgradeItem.m_shared.m_name} {__result}");
                    }
                }

                if (__result == emptyPosition && TryFindFreeEquipmentSlotForItem(Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot, out Slot slot1))
                {
                    __result = slot1.GridPosition;
                    LogDebug($"Inventory.FindEmptySlot free equipment slot for AddItem_ByName item {Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot.m_shared.m_name} {__result}");
                }

                if (__result == emptyPosition && TryFindFreeSlotForItem(Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot, out Slot slot2))
                {
                    __result = slot2.GridPosition;
                    LogDebug($"Inventory.FindEmptySlot free slot for AddItem_ByName item {Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot.m_shared.m_name} {__result}");
                }

                if (__result == emptyPosition && !Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem)
                {
                    __result = FindEmptyQuickSlot();
                    LogDebug($"Inventory.FindEmptySlot free quick slot {__result}");
                }

                if (__result == emptyPosition && Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot != null && TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                {
                    __result = gridPos;
                    LogDebug($"Inventory.FindEmptySlot made free space for AddItem_ByName item {Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot.m_shared.m_name} {__result}");
                }

                // Inventory.AddItem(name, ...) performs an early FindEmptySlot precheck before it
                // creates the upgraded ItemData. Let that one precheck pass so the actual replacement
                // reaches the scoped AddItem recovery below, where it can be deferred losslessly.
                if (__result == emptyPosition && upgradePrecheckBypass
                    && InventoryGui_DoCrafting_UpgradeInSlot.IsExpectedReplacementPrefab(Inventory_AddItem_ByName_FindAppropriateSlot.itemToFindSlot))
                {
                    __result = new Vector2i(0, 0);
                    LogDebug("Inventory.FindEmptySlot allowed the upgrade replacement creation to continue to deferred recovery.");
                }
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.First)]
            private static void Finalizer(Inventory __instance)
            {
                if (__instance == PlayerInventory)
                    __instance.m_height = InventoryHeightFull;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        internal static class InventoryGui_DoCrafting_UpgradeInSlot
        {
            internal enum UpgradeOutcome { Regular, Pending, Success, Downgraded, Destroyed }

            internal sealed class UpgradeState
            {
                internal UpgradeState Previous;
                internal Player Player;
                internal Inventory Inventory;
                internal Slot SourceSlot;
                internal ItemDrop.ItemData Source;
                internal ItemDrop.ItemData Snapshot;
                internal ItemDrop.ItemData Replacement;
                internal ItemDrop.ItemData DetachedReplacement;
                internal DeferredInventory.DeferredEntry DeferredHandle;
                internal IDisposable Mutation;
                internal string PrefabName;
                internal bool WasEquipped;
                internal bool Accepted;
                internal UpgradeOutcome Outcome;
                internal int Quality => Outcome == UpgradeOutcome.Downgraded ? Snapshot.m_quality - 1 : Snapshot.m_quality + 1;
            }

            internal static UpgradeState Current;
            internal static Slot UpgradeSourceSlot => Current?.SourceSlot;
            internal static ItemDrop.ItemData UpgradeSourceItem => Current?.Source;
            internal static bool IsActive => Current?.SourceSlot != null;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(InventoryGui __instance, Player player, out UpgradeState __state)
            {
                __state = new UpgradeState { Previous = Current };
                Current = __state;
                if (player == null || player.GetInventory() != PlayerInventory
                    || __instance.m_craftUpgradeItem is not ItemDrop.ItemData item
                    || !PlayerInventory.ContainsItem(item)
                    || GetSlotInGrid(item.m_gridPos) is not Slot sourceSlot)
                    return;

                __state.Player = player;
                __state.Inventory = player.GetInventory();
                __state.SourceSlot = sourceSlot;
                __state.Source = item;
                __state.Snapshot = item.Clone();
                __state.PrefabName = __instance.m_craftRecipe?.m_item?.gameObject?.name ?? item.m_dropPrefab?.name;
                __state.WasEquipped = item.m_equipped || player.IsItemEquiped(item);
                __state.Outcome = player.GetCurrentCraftingStation()?.m_upgrader == true
                    ? UpgradeOutcome.Pending : UpgradeOutcome.Regular;
                // Keep observers from restoring/consuming the escrow entry before every crafting
                // postfix has finished enriching the replacement's custom item data.
                __state.Mutation = PlayerInventoryOperations.Batch(__state.Inventory);
            }

            private static void MarkOutcome(UpgradeOutcome outcome)
            {
                if (IsActive)
                    Current.Outcome = outcome;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> code = instructions.ToList();
                Dictionary<string, UpgradeOutcome> outcomes = new Dictionary<string, UpgradeOutcome>
                {
                    { "$msg_upgrader_success", UpgradeOutcome.Success },
                    { "$msg_upgrader_failed", UpgradeOutcome.Downgraded },
                    { "$msg_upgrader_broke", UpgradeOutcome.Destroyed }
                };
                // Observe the actual branch before refunds or callbacks. A missing replacement alone
                // cannot distinguish intentional destruction from an aborted/failed insertion.
                foreach (string token in outcomes.Keys)
                    if (code.Count(instruction => instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, token)) != 1)
                        throw new InvalidOperationException($"Unsupported DoCrafting upgrader outcome marker: {token}.");

                MethodInfo mark = AccessTools.Method(typeof(InventoryGui_DoCrafting_UpgradeInSlot), nameof(MarkOutcome));
                foreach (CodeInstruction instruction in code)
                {
                    if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string token
                        && outcomes.TryGetValue(token, out UpgradeOutcome outcome))
                    {
                        CodeInstruction marker = new CodeInstruction(OpCodes.Ldc_I4, (int)outcome);
                        marker.labels.AddRange(instruction.labels);
                        marker.blocks.AddRange(instruction.blocks);
                        instruction.labels.Clear();
                        instruction.blocks.Clear();
                        yield return marker;
                        yield return new CodeInstruction(OpCodes.Call, mark);
                    }
                    yield return instruction;
                }
            }

            internal static bool IsReplacementRequest(string name, int quality, int variant, Vector2i position) =>
                IsActive && Current.Outcome != UpgradeOutcome.Pending && Current.Outcome != UpgradeOutcome.Destroyed
                && !Current.Inventory.ContainsItem(Current.Source)
                && string.Equals(name, Current.PrefabName, StringComparison.Ordinal)
                && quality == Current.Quality && variant == Current.Snapshot.m_variant
                && position == Current.Snapshot.m_gridPos;

            internal static bool IsExpectedReplacementPrefab(ItemDrop.ItemData item) =>
                IsActive && Inventory_AddItem_ByName_FindAppropriateSlot.IsReplacementCall
                && item != null && string.Equals(item.m_dropPrefab?.name, Current.PrefabName, StringComparison.Ordinal);

            internal static void ObserveReplacementCreation(ItemDrop.ItemData result)
            {
                if (!IsActive || !Inventory_AddItem_ByName_FindAppropriateSlot.IsReplacementCall || result == null)
                    return;

                // dropIfFullInv can return detached ItemData even after spawning a default prefab.
                // Only resident output or a successful, explicit escrow adoption is authoritative.
                if (Current.Inventory.ContainsItem(result))
                {
                    Current.Accepted = true;
                    Current.Replacement = result;
                }
            }

            internal static void HandleReplacementAddResult(ItemDrop.ItemData item, bool originalRan, ref bool result)
            {
                if (!IsExpectedReplacementPrefab(item) || Current.Accepted
                    || item.m_variant != Current.Snapshot.m_variant || item.m_quality != Current.Quality)
                    return;

                if (result)
                {
                    Current.Accepted = true;
                    Current.Replacement = Current.Inventory.ContainsItem(item) ? item : null;
                    return;
                }

                // Preserve an intentional foreign-prefix rejection. Roll back the original instead.
                if (!originalRan)
                    return;

                if (DeferredInventory.EnqueueDetached(Current.Player, item, out DeferredInventory.DeferredEntry handle,
                    Current.SourceSlot.ID, Current.WasEquipped, "upgrade replacement could not be inserted after topology changed", pendingFinalization: true))
                {
                    Current.Accepted = true;
                    Current.Replacement = null;
                    Current.DetachedReplacement = item;
                    Current.DeferredHandle = handle;
                    result = true;
                }
            }

            private static void Complete(UpgradeState state)
            {
                if (state?.SourceSlot == null || state.Outcome == UpgradeOutcome.Destroyed)
                    return;

                Player player = state.Player;
                Inventory inventory = state.Inventory;
                if (!player || inventory == null)
                    return;

                if (state.DeferredHandle != null)
                {
                    // Crafting postfixes can enrich custom data after the first durable adoption.
                    // Refresh that exact entry, or relinquish it if a provider made the output resident.
                    if (!DeferredInventory.FinalizeDetachedReplacement(player, state.DeferredHandle, state.DetachedReplacement))
                        LogWarning("Unable to refresh the deferred upgrade output after crafting postfixes; its previously saved representation was retained.");
                    if (inventory.ContainsItem(state.DetachedReplacement))
                        state.Replacement = state.DetachedReplacement;
                }

                if (!state.Accepted && !inventory.ContainsItem(state.Source))
                {
                    ItemDrop.ItemData original = state.Snapshot.Clone();
                    original.m_equipped = false;
                    original.m_customData[customKeyPlayerID] = player.GetPlayerID().ToString();
                    original.m_customData[customKeySlotID] = state.SourceSlot.ID;

                    // Preserve ownership before invoking slot validators or equipment providers.
                    if (!DeferredInventory.EnqueueDetached(player, original, state.SourceSlot.ID, state.WasEquipped, "upgrade rollback"))
                    {
                        using (PlayerInventoryOperations.Batch(inventory))
                        {
                            bool restored = false;
                            ItemDrop.ItemData placed = null;
                            try
                            {
                                PlayerInventoryOperations.TryInsertDetachedToBestAvailable(original, state.SourceSlot.ID,
                                    state.WasEquipped, out placed, out restored, out _);
                            }
                            catch (Exception ex)
                            {
                                restored = inventory.ContainsItem(original);
                                LogWarning($"Failed to place an upgrade rollback item normally:\n{ex}");
                            }

                            if (!restored)
                            {
                                PlayerInventoryOperations.InsertForReconciliation(original,
                                    new Vector2i(InventoryWidth - 1, InventoryHeightFull - 1));
                                LogWarning($"Upgrade rollback for {original.m_shared.m_name} used emergency reconciliation staging because deferred persistence was unavailable.");
                            }
                            else if (state.WasEquipped && placed != null && placed.IsEquipable() && !player.IsItemEquiped(placed))
                                player.EquipItem(placed, triggerEquipEffects: false);
                        }
                    }
                }
                else if (state.Accepted && state.WasEquipped && state.Replacement != null
                    && inventory.ContainsItem(state.Replacement) && !player.IsItemEquiped(state.Replacement))
                    player.EquipItem(state.Replacement, triggerEquipEffects: false);

                ItemsSlotsValidation.ValidateItems();
                ItemsSlotsValidation.ValidateSlots();
            }

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(UpgradeState __state, Exception __exception)
            {
                // Detach before calling providers, but do not expose an outer craft to these callbacks.
                Current = null;
                try
                {
                    Complete(__state);
                }
                catch (Exception ex)
                {
                    LogWarning($"Failed to finalize ExtraSlots upgrade recovery:\n{ex}");
                }
                finally
                {
                    try
                    {
                        __state?.Mutation?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Failed to notify upgrade inventory changes:\n{ex}");
                    }
                    finally
                    {
                        Current = __state?.Previous;
                    }
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
        private static class Inventory_HaveEmptySlot_CheckRegularInventoryAndQuickSlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref bool __result)
            {
                __result = __instance.GetEmptySlots() > 0;
            }
        }

        private static bool PassDropItem(string source, InventoryGrid grid, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos)
        {
            if (item.m_gridPos == pos)
                return true;

            // If the equipped item from slot is dropped at player inventory
            if (grid.m_inventory == PlayerInventory && GetItemSlot(item) is Slot itemSlot && itemSlot.IsEquipmentSlot && Player.m_localPlayer.IsItemEquiped(item))
            {
                if (GetSlotInGrid(pos) is not Slot posSlot)
                {
                    LogDebug($"{source} Prevented dropping equipped item {item.m_shared.m_name} {item.m_gridPos} into regular inventory {pos}");
                    return false;
                };

                if (!IsSameSlotType(itemSlot, posSlot))
                {
                    LogDebug($"{source} Prevented dropping equipped item {item.m_shared.m_name} {item.m_gridPos} into slot with other type {posSlot}");
                    return false;
                }
            }

            // If target slot is in player inventory and is extra slot
            if (grid.m_inventory == PlayerInventory && GetSlotInGrid(pos) is Slot targetSlot)
            {
                // If the dropped item is unfit for target slot
                if (!targetSlot.ItemFits(item))
                {
                    LogDebug($"{source} Prevented dropping {item.m_shared.m_name} {item.m_gridPos} into unfit slot {targetSlot}");
                    return false;
                }

                // If the dropped item is not from equipment slot and target item is equipped item at equipment slot
                if (targetSlot.IsEquipmentSlot && targetSlot.Item != null && Player.m_localPlayer.IsItemEquiped(targetSlot.Item) && (GetItemSlot(item) is not Slot fromSlot || !fromSlot.IsEquipmentSlot))
                {
                    LogDebug($"{source} Prevented dropping {item.m_shared.m_name} {item.m_gridPos} into occupied equipment slot {targetSlot}");
                    return false;
                }
            }

            ItemDrop.ItemData itemAt = grid.m_inventory.GetItemAt(pos.x, pos.y);

            // If dropped item is in slot and interchanged item is unfit for dragged item slot
            if (itemAt != null && fromInventory == PlayerInventory && GetSlotInGrid(item.m_gridPos) is Slot slot1 && !slot1.ItemFits(itemAt))
            {
                LogDebug($"{source} Prevented swapping {item.m_shared.m_name} {slot1} with unfit item {itemAt.m_shared.m_name} {pos}");
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
        public static class InventoryGui_OnSelectedItem_GetEquippedDragItem
        {
            public static bool Prefix(InventoryGui __instance, InventoryGrid grid, Vector2i pos)
            {
                if (Player.m_localPlayer && !Player.m_localPlayer.IsTeleporting() && __instance.m_dragGo && __instance.m_dragItem != null && __instance.m_dragInventory != null)
                    return PassDropItem("InventoryGui.OnSelectedItem", grid, __instance.m_dragInventory, __instance.m_dragItem, pos);

                return true;
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        public static class InventoryGrid_DropItem_DropPrevention
        {
            public static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos) => PassDropItem("InventoryGrid.DropItem", __instance, fromInventory, item, pos);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool))]
        private static class Inventory_AddItem_ItemData_amount_x_y_TargetPositionRerouting
        {
            [HarmonyPriority(Priority.Last)]
            private static void Prefix(Inventory __instance, ItemDrop.ItemData item, ref int x, ref int y)
            {
                if (item == null)
                    return;

                if (__instance != PlayerInventory)
                {
                    // There is some nasty behaviour when a tombstone inventory is created with dimensions smaller than its saved item positions.
                    // Slot metadata identifies items that came from the player slot region, so keep the loading inventory large enough to preserve them.
                    if (Inventory_AddItem_OnLoad_FindAppropriateSlot.inCall
                        && item.m_customData.ContainsKey(customKeySlotID)
                        && item.m_customData.ContainsKey(customKeyPlayerID)
                        && (x >= __instance.m_width || y >= __instance.m_height))
                    {
                        int oldWidth = __instance.m_width;
                        int oldHeight = __instance.m_height;
                        __instance.m_width = Mathf.Max(__instance.m_width, x + 1);
                        __instance.m_height = Mathf.Max(__instance.m_height, y + 1);
                        LogDebug($"Inventory \"{__instance.m_name}\" loading: expanded {oldWidth}x{oldHeight} -> {__instance.m_width}x{__instance.m_height} for {item.m_shared.m_name} at {x},{y}");
                    }
                    return;
                }

                // Known materials and player keys are not yet loaded, custom components are not initialized, skip validation
                if (Inventory_AddItem_OnLoad_FindAppropriateSlot.inCall && CurrentPlayer.m_isLoading)
                    return;

                if (item == null)
                    return;

                // If another item is at grid - let stack logic go
                if (__instance.GetItemAt(x, y) is ItemDrop.ItemData gridTakenItem)
                {
                    LogDebug($"Inventory.AddItem X Y item {item.m_shared.m_name} adding at {x},{y} position is taken {gridTakenItem.m_shared.m_name}");
                    return;
                }

                // If the dropped item fits for target slot
                if (GetSlotInGrid(new Vector2i(x, y)) is not Slot slot || (slot.IsActive && !slot.IsEmptySlot && slot.ItemFits(item)))
                    return;

                LogDebug($"Inventory.AddItem X Y item {item.m_shared.m_name} adding at {x},{y} unfits slot {slot} {slot.GridPosition}");

                if (TryFindFreeSlotForItem(item, out Slot freeSlot))
                {
                    LogDebug($"Inventory.AddItem X Y Rerouted {item.m_shared.m_name} from {x},{y} to free slot {freeSlot} {freeSlot.GridPosition}");
                    x = freeSlot.GridPosition.x;
                    y = freeSlot.GridPosition.y;
                    return;
                }

                if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                {
                    LogDebug($"Inventory.AddItem X Y Rerouted {item.m_shared.m_name} from {x},{y} to created free space {gridPos}");
                    x = gridPos.x;
                    y = gridPos.y;
                }
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int x, int y, int amount, ref bool __result)
            {
                if (__instance == PlayerInventory && Inventory_AddItem_OnLoad_FindAppropriateSlot.inCall && !__result)
                {
                    amount = Mathf.Min(amount, item.m_stack);

                    // Prevent item disappearing
                    ItemDrop.ItemData itemData = item.Clone();
                    itemData.m_stack = amount;

                    LogMessage($"Item dissappearing prevention at Inventory.AddItem_OnLoad -> Inventory.AddItem_ItemData_amount_x_y: item {item.m_shared.m_name} at {x},{y} amount {amount}");

                    Vector2i target = emptyPosition;
                    if (TryFindFreeEquipmentSlotForItem(itemData, out Slot equipmentSlot))
                    {
                        target = equipmentSlot.GridPosition;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y found free equipment slot for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {target}");
                    }
                    else if (TryFindFreeSlotForItem(itemData, out Slot slot))
                    {
                        target = slot.GridPosition;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y found free slot for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {target}");
                    }
                    else if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                    {
                        target = gridPos;
                        LogDebug($"Inventory.AddItem_ItemData_amount_x_y made free space for item {itemData.m_shared.m_name}. Position rerouted {x},{y} -> {target}");
                    }

                    bool inserted = target != emptyPosition && PlayerInventoryOperations.InsertExisting(itemData, target);
                    if (!inserted)
                    {
                        // Player custom data is loaded after Inventory.Load, so deferred persistence
                        // is not available yet. Keep the item represented just outside the grid only
                        // for this load scope; the first full validation must place or defer it.
                        Vector2i temporary = new Vector2i(0, InventoryHeightFull);
                        inserted = PlayerInventoryOperations.InsertForReconciliation(itemData, temporary);
                        LogWarning($"Inventory.AddItem_ItemData_amount_x_y temporarily staged {itemData.m_shared.m_name} at {temporary} for reconciliation");
                    }

                    if (inserted)
                    {
                        item.m_stack -= amount;
                        __result = true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class Inventory_CanAddItem_ItemData_TryFindAppropriateExtraSlot
        {
            // Capacity checks can be nested by other patches. Pool per-call snapshots instead of
            // sharing one scratch list, and always restore the exact inventory in the finalizer.
            private static readonly Stack<CapacityQueryState> statePool = new Stack<CapacityQueryState>();

            private sealed class CapacityQueryState
            {
                private readonly List<(int Index, ItemDrop.ItemData Item)> removedItems = new List<(int, ItemDrop.ItemData)>(40);
                private Inventory inventory;
                private int originalHeight;
                private bool restored;
                private bool released;

                internal void Begin(Inventory target)
                {
                    inventory = target;
                    originalHeight = target.m_height;
                    restored = false;
                    released = false;
                    target.m_height = InventoryHeightPlayer;

                    for (int i = target.m_inventory.Count - 1; i >= 0; i--)
                    {
                        ItemDrop.ItemData item = target.m_inventory[i];
                        if (item == null || !API.IsItemInSlot(item))
                            continue;

                        removedItems.Add((i, item));
                        target.m_inventory.RemoveAt(i);
                    }
                }

                internal void Restore()
                {
                    if (restored || inventory == null)
                        return;

                    inventory.m_height = originalHeight;
                    // Entries were removed in descending index order. Restore ascending order so
                    // querying capacity cannot reorder resource consumption or identical stacks.
                    for (int i = removedItems.Count - 1; i >= 0; i--)
                    {
                        (int index, ItemDrop.ItemData item) = removedItems[i];
                        if (!inventory.m_inventory.Contains(item))
                            inventory.m_inventory.Insert(Math.Min(index, inventory.m_inventory.Count), item);
                    }

                    restored = true;
                }

                internal void Release()
                {
                    if (released)
                        return;

                    released = true;
                    removedItems.Clear();
                    inventory = null;
                    if (statePool.Count < 8)
                        statePool.Push(this);
                }
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, out CapacityQueryState __state)
            {
                __state = null;
                if (__instance != PlayerInventory)
                    return;

                __state = statePool.Count > 0 ? statePool.Pop() : new CapacityQueryState();
                __state.Begin(__instance);
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, bool __runOriginal, CapacityQueryState __state, ref bool __result)
            {
                __state?.Restore();
                if (__state == null || !__runOriginal || __result || item?.m_shared == null)
                    return;

                int requestedStack = stack > 0 ? stack : item.m_stack;
                int freeStackSpace = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel);
                long freeQuickSlotStackSpace = (long)Math.Max(0, __instance.GetEmptySlots()) * item.m_shared.m_maxStackSize;
                long sizeCombined = Math.Max(0, freeStackSpace) + freeQuickSlotStackSpace;

                if (__result = sizeCombined >= requestedStack)
                {
                    LogDebug($"Inventory.CanAddItem_ItemData_int item {item.m_shared.m_name} result {__result}, free stack space: {freeStackSpace}, free quick slot stack space: {freeQuickSlotStackSpace}, have free stack space");
                }
                else if (requestedStack <= item.m_shared.m_maxStackSize && !Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem)
                {
                    if (__result = TryFindFreeSlotForItem(item, out Slot slot))
                        LogDebug($"Inventory.CanAddItem_ItemData_int item {item.m_shared.m_name} result {__result}, free stack space: {freeStackSpace}, free quick slot stack space: {freeQuickSlotStackSpace}, no free stack space, free single slot found {slot} {slot.GridPosition}");
                }
            }

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(CapacityQueryState __state, Exception __exception)
            {
                __state?.Restore();
                __state?.Release();
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        private static class Inventory_AddItem_ItemData_TryFindAppropriateExtraSlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, bool __runOriginal, ref bool __result)
            {
                if (__instance != PlayerInventory)
                    return;

                if (!__result && __runOriginal && !Player_AutoPickup_PreventAutoPickupInExtraSlots.preventAddItem
                    && TryFindFreeSlotForItem(item, out Slot slot))
                {
                    LogDebug($"Inventory.AddItem_Item item {item.m_shared.m_name} found free slot {slot} {slot.GridPosition}");

                    if (PlayerInventoryOperations.InsertExisting(item, slot.GridPosition))
                        __result = true;
                }

                InventoryGui_DoCrafting_UpgradeInSlot.HandleReplacementAddResult(item, __runOriginal, ref __result);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
        private static class Inventory_AddItem_ItemData_pos_TargetPositionRerouting
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref Vector2i pos, ref bool __result)
            {
                if (__instance != PlayerInventory)
                    return true;

                if (item == null)
                    return true;

                bool inBounds = pos.x >= 0 && pos.x < InventoryWidth && pos.y >= 0 && pos.y < InventoryHeightFull;
                Slot slot = inBounds ? GetSlotInGrid(pos) : null;
                bool validDestination = inBounds && (pos.y < InventoryHeightPlayer
                    || (slot != null && slot.IsActive && !slot.IsEmptySlot && slot.ItemFits(item)));
                // Keep native occupied-cell handling, but never admit an orphaned tail cell or
                // negative position merely because it has no Slot descriptor.
                if (inBounds && __instance.GetItemAt(pos.x, pos.y) != null || validDestination)
                    return true;

                // If inventory has available free stack items with the same quality - let stack logic go
                if (item.m_shared.m_maxStackSize > 1)
                {
                    int freeStacks = __instance.GetAllItems()
                        .Where(itemInv => item.m_shared.m_name == itemInv.m_shared.m_name && item.m_quality == itemInv.m_quality && item.m_worldLevel == itemInv.m_worldLevel)
                        .Sum(itemInv => itemInv.m_shared.m_maxStackSize - itemInv.m_stack);

                    if (freeStacks >= item.m_stack)
                        return true;

                    LogDebug($"Inventory.AddItem_Item_Vector2i item {item.m_shared.m_name}x{item.m_stack} adding at {pos} not enough free stack space {freeStacks}");
                }

                if (TryFindFreeSlotForItem(item, out Slot freeSlot))
                {
                    LogDebug($"Inventory.AddItem_Item_Vector2i Rerouted {item.m_shared.m_name} from {pos} to free slot {freeSlot} {freeSlot.GridPosition}");
                    pos = freeSlot.GridPosition;
                    return true;
                }

                if (TryMakeFreeSpaceInPlayerInventory(tryFindRegularInventorySlot: true, out Vector2i gridPos))
                {
                    LogDebug($"Inventory.AddItem_Item_Vector2i Rerouted {item.m_shared.m_name} from {pos} to created free space {gridPos}");
                    pos = gridPos;
                    return true;
                }

                // This overload accepts negative coordinates without validating them. Use the
                // non-positional path rather than inserting into an invalid sentinel cell.
                __result = __instance.AddItem(item);
                return false;
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, bool __runOriginal, ref bool __result)
            {
                if (__instance == PlayerInventory)
                    InventoryGui_DoCrafting_UpgradeInSlot.HandleReplacementAddResult(item, __runOriginal, ref __result);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool))]
        public static class Inventory_AddItem_ByName_FindAppropriateSlot
        {
            internal sealed class CallState
            {
                internal CallState Previous;
                internal Inventory Inventory;
                internal ItemDrop.ItemData Candidate;
                internal ItemDrop.ItemData PendingDrop;
                internal InventoryGui_DoCrafting_UpgradeInSlot.UpgradeState Upgrade;
                internal bool PrecheckPending;
            }

            private static CallState current;
            public static ItemDrop.ItemData itemToFindSlot;
            internal static bool IsReplacementCall => current?.Upgrade != null
                && ReferenceEquals(current.Upgrade, InventoryGui_DoCrafting_UpgradeInSlot.Current);

            internal static bool ConsumeUpgradePrecheckBypass()
            {
                bool pending = current?.PrecheckPending == true;
                if (current != null)
                    current.PrecheckPending = false;
                return pending && IsReplacementCall;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, string name, int quality, int variant, Vector2i position,
                ref bool dropIfFullInv, out CallState __state)
            {
                __state = new CallState { Previous = current, Inventory = __instance };
                current = __state;
                itemToFindSlot = null;
                if (__instance != PlayerInventory)
                    return;

                if (InventoryGui_DoCrafting_UpgradeInSlot.IsReplacementRequest(name, quality, variant, position))
                {
                    __state.Upgrade = InventoryGui_DoCrafting_UpgradeInSlot.Current;
                    __state.PrecheckPending = true;
                    // Ordinary crafting/refunds keep the caller's policy. Only a tracked replacement
                    // uses lossless escrow or rollback instead of vanilla's detached default-prefab drop.
                    dropIfFullInv = false;
                }

                ItemDrop component = ObjectDB.instance?.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                if (component == null || component.m_itemData.m_shared.m_maxStackSize > 1)
                    return;

                __state.Candidate = component.m_itemData.Clone();
                __state.Candidate.m_dropPrefab = component.gameObject;
                __state.Candidate.m_quality = quality;
                __state.Candidate.m_variant = variant;
                itemToFindSlot = __state.Candidate;
            }

            internal static void ObserveAddResult(Inventory inventory, ItemDrop.ItemData item, bool result)
            {
                if (current != null && current.Inventory == inventory)
                    current.PendingDrop = result ? null : item;
            }

            private static GameObject DropCreatedItem(GameObject prefab, Vector3 position, Quaternion rotation)
            {
                ItemDrop.ItemData item = current?.PendingDrop;
                if (item == null || item.m_dropPrefab != prefab)
                    return UnityEngine.Object.Instantiate(prefab, position, rotation);

                // Vanilla instantiates the default prefab here, losing the remaining stack, quality,
                // variant and custom data. Its drop policy stays unchanged; only the representation changes.
                return ItemDrop.DropItem(item, item.m_stack, position, rotation).gameObject;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> code = instructions.ToList();
                List<CodeInstruction> drops = code.Where(instruction => instruction.operand is MethodInfo method
                    && method.DeclaringType == typeof(UnityEngine.Object) && method.Name == nameof(UnityEngine.Object.Instantiate)
                    && method.ReturnType == typeof(GameObject)
                    && method.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) })).ToList();
                if (drops.Count != 1)
                    throw new InvalidOperationException($"Expected one AddItem overflow drop, found {drops.Count}.");

                drops[0].opcode = OpCodes.Call;
                drops[0].operand = AccessTools.Method(typeof(Inventory_AddItem_ByName_FindAppropriateSlot), nameof(DropCreatedItem));
                return code;
            }

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(ItemDrop.ItemData __result, CallState __state, Exception __exception)
            {
                try
                {
                    if (IsReplacementCall)
                        InventoryGui_DoCrafting_UpgradeInSlot.ObserveReplacementCreation(__result);
                }
                finally
                {
                    current = __state?.Previous;
                    itemToFindSlot = current?.Candidate;
                }
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class Inventory_AddItem_ObserveCreatedItemOverflow
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) });
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) });
            }

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Inventory __instance, ItemDrop.ItemData item, bool __result, Exception __exception)
            {
                Inventory_AddItem_ByName_FindAppropriateSlot.ObserveAddResult(__instance, item, __result);
                return __exception;
            }
        }

        [HarmonyPatch]
        public static class Inventory_AddItem_OnLoad_FindAppropriateSlot
        {
            public static bool inCall = false;

            private static IEnumerable<MethodBase> TargetMethods()
            {
                Type[] metadata = { typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int),
                    typeof(long), typeof(string), typeof(Dictionary<string, string>), typeof(int), typeof(bool), typeof(bool), typeof(bool) };
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(string) }.Concat(metadata).ToArray());
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(int) }.Concat(metadata).ToArray());
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(int), typeof(ItemDrop.ItemData), typeof(bool) });
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out bool __state)
            {
                __state = inCall;
                inCall = true;
            }

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(bool __state, Exception __exception)
            {
                inCall = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave))]
        private static class Inventory_MoveInventoryToGrave_UpdateGraveInventory
        {
            private static void Prefix(Inventory original)
            {
                if (original != PlayerInventory)
                    return;

                original.m_height = InventoryHeightFull;
            }
        }

        internal static void UpdateTotalWeight() => PlayerInventory?.UpdateTotalWeight();

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.UpdateTotalWeight))]
        public static class Inventory_UpdateTotalWeight_ApplyWeightFactor
        {
            public static bool inCall = false;

            private static void Prefix(Inventory __instance, out bool __state)
            {
                __state = inCall;
                inCall = __instance == PlayerInventory;
            }

            private static void Postfix(bool __state) => inCall = __state;

            private static Exception Finalizer(bool __state, Exception __exception)
            {
                inCall = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
        public static class ItemDrop_ItemData_GetWeight_UpdateTotalWeight_ApplyWeightFactor
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                if (!Inventory_UpdateTotalWeight_ApplyWeightFactor.inCall)
                    return;

                if (LightenedSlots.IsRowAffected(__instance.m_gridPos.y))
                    __result *= LightenedSlots.WeightFactor;
                else if (GetItemSlot(__instance) is Slot slot)
                    __result *= GetItemWeightFactor(slot);
            }
        }
    }
}
