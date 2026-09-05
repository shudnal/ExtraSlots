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
    /// Clears ExtraSlots' slot cache synchronously for every real player-inventory Changed() call.
    /// Automatic notification batching is selected directly by PlayerInventoryOperations; explicit
    /// topology batches remain correctness boundaries regardless of the compatibility setting.
    /// </summary>
    internal static class InventoryChangedBatchingCompatibility
    {
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
    /// Makes legacy EaQS 2.x side-inventory adoption transactional from the character-data point of
    /// view. Individual EnqueueDetached calls persist eagerly for loss safety, so a later failure must
    /// restore the deferred payload that existed before this authoritative source was processed. This
    /// keeps the retained legacy source retryable without duplicating entries adopted before the failure.
    /// </summary>
    internal static class LegacyEaQSMigrationRetrySafety
    {
        private sealed class LegacyMigrationState
        {
            internal Player Player;
            internal bool HadDeferredPayload;
            internal string DeferredPayload;
            internal bool RolledBack;
        }

        [HarmonyPatch]
        private static class EquipmentAndQuickSlotsCompat_MigrateLegacyInventory_RollbackPartialAdoption
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(EquipmentAndQuickSlotsCompat), "MigrateLegacyInventory");

            private static void Prefix(out LegacyMigrationState __state)
            {
                __state = null;

                Player player = EquipmentAndQuickSlotsCompat.playerToLoad;
                // Character-select preview intentionally returns false but never adopts deferred data.
                if (player == null || Player.m_localPlayer == null)
                    return;

                bool hadDeferredPayload = player.m_customData.TryGetValue(global::ExtraSlots.DeferredInventory.CustomDataKey, out string deferredPayload);
                __state = new LegacyMigrationState
                {
                    Player = player,
                    HadDeferredPayload = hadDeferredPayload,
                    DeferredPayload = deferredPayload
                };
            }

            private static void Postfix(bool __result, LegacyMigrationState __state)
            {
                if (!__result)
                    Rollback(__state);
            }

            private static Exception Finalizer(Exception __exception, LegacyMigrationState __state)
            {
                if (__exception != null)
                    Rollback(__state);

                return __exception;
            }

            private static void Rollback(LegacyMigrationState state)
            {
                if (state?.Player == null || state.RolledBack)
                    return;

                state.RolledBack = true;
                if (state.HadDeferredPayload)
                    state.Player.m_customData[global::ExtraSlots.DeferredInventory.CustomDataKey] = state.DeferredPayload;
                else
                    state.Player.m_customData.Remove(global::ExtraSlots.DeferredInventory.CustomDataKey);

                // Force DeferredInventory's in-memory queue back to the exact pre-migration payload.
                // The legacy EaQS source key was not removed on failure, so the whole authoritative
                // side inventory can now be retried safely on the next load.
                global::ExtraSlots.DeferredInventory.EnsureLoaded(state.Player);
                global::ExtraSlots.ExtraSlots.LogWarning("Rolled back partial legacy EaQS side-inventory adoption; source data remains intact for a later retry.");
            }
        }
    }
}
