using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ExtraSlots.Compatibility
{
    internal static class CompatibilityHelper
    {
        internal static ConfigEntry<bool> InventoryChangedBatching { get; private set; }

        internal static void CheckForCompatibility()
        {
            InventoryChangedBatching ??= ExtraSlots.instance.Config.Bind(
                "Mods compatibility",
                "Inventory Changed batching",
                true,
                "Aggregate ordinary player Inventory.Changed() calls into one final notification during ExtraSlots inventory mutations. Disable this as a compatibility diagnostic if another mod depends on intermediate Inventory.Changed callbacks. Explicit ExtraSlots topology transactions remain atomic.");

            EpicLootCompat.CheckForCompatibility();

            BetterArcheryCompat.CheckForCompatibility();

            PlantEasilyCompat.CheckForCompatibility();

            ValheimPlusCompat.CheckForCompatibility();

            BetterProgressionCompat.CheckForCompatibility();

            ZenBeehiveCompat.CheckForCompatibility();

            BBHCompat.CheckForCompatibility();

            Recycle_N_Reclaim.CheckForCompatibility();

            RequipMeCompat.CheckForCompatibility();

            ZenUICompat.CheckForCompatibility();
        }

        internal static void RemoveHarmonyPatch(this Assembly assembly, Type patchedType, string patchedMethod, string patcherClassName, string patcherClassMethod, string reason)
        {
            Type patcherType = assembly.GetType(patcherClassName);
            if (patcherType == null)
            {
                ExtraSlots.LogInfo($"{patcherClassName} is not found.");
                return;
            }

            if (AccessTools.Method(patchedType, patchedMethod) is not MethodInfo method)
            {
                ExtraSlots.LogInfo($"Method {patchedType.Name}.{patchedMethod} is not found.");
                return;
            }

            if (AccessTools.Method(patcherType, patcherClassMethod) is not MethodInfo patch)
            {
                ExtraSlots.LogInfo($"Patch {patcherType.Name}.{patcherClassMethod} is not found.");
                return;
            }

            ExtraSlots.instance.harmony.Unpatch(method, patch);
            ExtraSlots.LogInfo($"{patcherClassName}:{patcherClassMethod} was unpatched to {reason}.");
        }

        internal static void RemoveHarmonyPatch(this Assembly assembly, MethodBase method, string patcherClassName, string patcherClassMethod, string reason)
        {
            Type patcherType = assembly.GetType(patcherClassName);
            if (patcherType == null)
            {
                ExtraSlots.LogInfo($"{patcherClassName} is not found.");
                return;
            }

            if (AccessTools.Method(patcherType, patcherClassMethod) is not MethodInfo patch)
            {
                ExtraSlots.LogInfo($"Patch {patcherType.Name}.{patcherClassMethod} is not found.");
                return;
            }

            ExtraSlots.instance.harmony.Unpatch(method, patch);
            ExtraSlots.LogInfo($"{patcherClassName}:{patcherClassMethod} was unpatched to {reason}.");
        }

        internal static void TryAddMethodToPatch(this Assembly assembly, List<MethodBase> list, string methodClassName, string methodName, string reason)
        {
            Type methodType = assembly.GetType(methodClassName);
            if (methodType == null)
            {
                ExtraSlots.LogInfo($"{methodClassName} is not found.");
                return;
            }

            if (AccessTools.Method(methodType, methodName) is not MethodInfo method)
            {
                ExtraSlots.LogInfo($"Method {methodType.Name}.{methodName} is not found.");
                return;
            }
            
            list.Add(method);
            ExtraSlots.LogInfo($"{methodClassName}:{methodName} is patched to {reason}.");
        }
    }

    /// <summary>
    /// Keeps observer-oriented automatic mutation batching optional for compatibility diagnostics and
    /// clears ExtraSlots' slot cache synchronously for every real player-inventory Changed() call.
    /// Explicit PlayerInventoryOperations.Batch() scopes are correctness boundaries for topology
    /// rebuilds and intentionally remain atomic regardless of this compatibility setting.
    /// </summary>
    internal static class InventoryChangedBatchingCompatibility
    {
        private sealed class NoopDisposable : IDisposable
        {
            internal static readonly NoopDisposable Instance = new NoopDisposable();
            public void Dispose() { }
        }

        [HarmonyPatch]
        private static class PlayerInventoryOperations_AutomaticBatch_Optional
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(global::ExtraSlots.PlayerInventoryOperations), "AutomaticBatch");

            private static bool Prefix(ref IDisposable __result)
            {
                if (CompatibilityHelper.InventoryChangedBatching?.Value != false)
                    return true;

                __result = NoopDisposable.Instance;
                return false;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        private static class Inventory_Changed_ClearExtraSlotsCacheImmediately
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance == global::ExtraSlots.Slots.PlayerInventory)
                    global::ExtraSlots.Slots.ClearCachedItems();
            }
        }
    }

    /// <summary>
    /// Small guards around deferred inventory that intentionally stay outside its persistence core:
    /// preserve an explicit slot return address over a transient physical cell, restore equipped
    /// state for any equipable deferred item regardless of its physical slot, append death escrow
    /// after the grave's existing grid, and immediately disable FloatingTerrain pickup dummies when
    /// a tombstone is recovered so Player.AutoPickup cannot observe a dummy with a destroyed parent.
    /// </summary>
    internal static class DeferredInventoryRuntimeGuards
    {
        private sealed class DeferredRestoreMergeState
        {
            internal readonly List<(ItemDrop.ItemData Item, int Stack)> Candidates = new List<(ItemDrop.ItemData, int)>();
        }

        private static Inventory tombstoneAppendInventory;
        private static int tombstoneAppendStartHeight;
        private static int deferredRestoreDepth;

        [HarmonyPatch]
        private static class DeferredInventory_CapturePreferredSlot_PreserveExplicitAddress
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(global::ExtraSlots.DeferredInventory), "CapturePreferredSlot");

            private static bool Prefix(ItemDrop.ItemData item, ref string __result)
            {
                if (item?.m_customData == null
                    || !item.m_customData.TryGetValue(global::ExtraSlots.Slots.customKeyPlayerID, out string playerID)
                    || !item.m_customData.TryGetValue(global::ExtraSlots.Slots.customKeySlotID, out string slotID)
                    || string.IsNullOrEmpty(slotID)
                    || playerID != global::ExtraSlots.Slots.CurrentPlayerProfile?.GetPlayerID().ToString())
                {
                    return true;
                }

                __result = slotID;
                return false;
            }
        }

        [HarmonyPatch]
        private static class DeferredInventory_TryRestoreAvailable_TrackScope
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(global::ExtraSlots.DeferredInventory), "TryRestoreAvailable");

            [HarmonyPriority(Priority.First)]
            private static void Prefix() => deferredRestoreDepth++;

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Exception __exception)
            {
                deferredRestoreDepth = Math.Max(0, deferredRestoreDepth - 1);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class PlayerInventoryOperations_TryInsertDetached_RestoreEquippedRepresentative
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(global::ExtraSlots.PlayerInventoryOperations), "TryInsertDetachedToBestAvailable");

            private static void Prefix(ItemDrop.ItemData item, bool preferEquipmentSlot, out DeferredRestoreMergeState __state)
            {
                __state = null;
                if (deferredRestoreDepth <= 0
                    || !preferEquipmentSlot
                    || item?.m_shared == null
                    || item.m_shared.m_maxStackSize <= 1
                    || global::ExtraSlots.Slots.PlayerInventory?.m_inventory == null)
                {
                    return;
                }

                DeferredRestoreMergeState state = new DeferredRestoreMergeState();
                foreach (ItemDrop.ItemData candidate in global::ExtraSlots.Slots.PlayerInventory.m_inventory)
                {
                    if (candidate == null
                        || ReferenceEquals(candidate, item)
                        || candidate.m_shared.m_name != item.m_shared.m_name
                        || candidate.m_quality != item.m_quality
                        || candidate.m_worldLevel != item.m_worldLevel
                        || candidate.m_stack >= candidate.m_shared.m_maxStackSize)
                    {
                        continue;
                    }

                    state.Candidates.Add((candidate, candidate.m_stack));
                }

                __state = state;
            }

            private static void Postfix(
                bool preferEquipmentSlot,
                ref ItemDrop.ItemData placedItem,
                ref bool fullyInserted,
                DeferredRestoreMergeState __state)
            {
                if (deferredRestoreDepth <= 0 || !preferEquipmentSlot || !fullyInserted)
                    return;

                Player player = global::ExtraSlots.Slots.CurrentPlayer;
                if (player == null)
                    return;

                if (placedItem == null && __state != null)
                {
                    ItemDrop.ItemData firstChangedStack = null;
                    foreach ((ItemDrop.ItemData candidate, int previousStack) in __state.Candidates)
                    {
                        if (candidate == null || candidate.m_stack <= previousStack)
                            continue;

                        if (player.IsItemEquiped(candidate))
                        {
                            placedItem = candidate;
                            break;
                        }

                        firstChangedStack ??= candidate;
                    }

                    placedItem ??= firstChangedStack;
                }

                if (placedItem != null && placedItem.IsEquipable() && !player.IsItemEquiped(placedItem))
                    player.EquipItem(placedItem, triggerEquipEffects: false);
            }
        }

        [HarmonyPatch]
        private static class DeferredInventory_MoveAllToTombstone_AppendAfterExistingGrid
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(global::ExtraSlots.DeferredInventory), "MoveAllToTombstone");

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory graveInventory)
            {
                tombstoneAppendInventory = graveInventory;
                tombstoneAppendStartHeight = graveInventory?.m_height ?? 0;
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Exception __exception)
            {
                tombstoneAppendInventory = null;
                tombstoneAppendStartHeight = 0;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.FindEmptySlot))]
        private static class Inventory_FindEmptySlot_AppendDeferredTombstoneRows
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Inventory __instance, ref Vector2i __result)
            {
                if (__instance == null || __instance != tombstoneAppendInventory)
                    return true;

                for (int y = Math.Max(0, tombstoneAppendStartHeight); y < __instance.m_height; y++)
                {
                    for (int x = 0; x < __instance.m_width; x++)
                    {
                        if (__instance.GetItemAt(x, y) != null)
                            continue;

                        __result = new Vector2i(x, y);
                        return false;
                    }
                }

                // MoveAllToTombstone reacts to the empty result by growing the container by one row.
                __result = new Vector2i(-1, -1);
                return false;
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.OnTakeAllSuccess))]
        private static class TombStone_OnTakeAllSuccess_DisableFloatingTerrainPickupDummy
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(TombStone __instance)
            {
                if (!Player.m_enableAutoPickup || __instance == null || !__instance.TryGetComponent(out FloatingTerrain floatingTerrain))
                    return;

                floatingTerrain.m_lastHeightmap = null;

                if (floatingTerrain.m_dummyCollider)
                    floatingTerrain.m_dummyCollider.enabled = false;
                if (floatingTerrain.m_dummyBody)
                    floatingTerrain.m_dummyBody.detectCollisions = false;

                GameObject dummy = null;
                if (floatingTerrain.m_dummy)
                    dummy = floatingTerrain.m_dummy.gameObject;
                else if (floatingTerrain.m_dummyBody)
                    dummy = floatingTerrain.m_dummyBody.gameObject;

                if (dummy)
                {
                    dummy.SetActive(false);
                    UnityEngine.Object.Destroy(dummy);
                    global::ExtraSlots.ExtraSlots.LogDebug("Disabled tombstone FloatingTerrain pickup dummy before AutoPickup could observe a stale parent reference.");
                }

                floatingTerrain.m_dummy = null;
                floatingTerrain.m_dummyBody = null;
                floatingTerrain.m_dummyCollider = null;
            }
        }
    }
}
