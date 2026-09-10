using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static ExtraSlots.ExtraSlots;

namespace ExtraSlots.Compatibility;

internal static class InventoryMigration
{
    private sealed class DeferredPayloadSnapshot
    {
        internal Player Player;
        internal bool HadPayload;
        internal string Payload;
    }

    private sealed class RecoverySourceState
    {
        internal DeferredPayloadSnapshot Deferred;
        internal readonly Dictionary<string, (bool Exists, string Value)> CustomData = new Dictionary<string, (bool, string)>(StringComparer.Ordinal);
        internal readonly Dictionary<string, (bool Exists, string Value)> KnownTexts = new Dictionary<string, (bool, string)>(StringComparer.Ordinal);
        internal bool RolledBack;
    }

    private sealed class AvailableRepresentation
    {
        internal string Key;
        internal ItemDrop.ItemData PlayerItem;
        internal string DeferredPreferredSlotId;
    }

    private readonly struct DeferredRepresentation
    {
        internal readonly string Key;
        internal readonly string PreferredSlotId;

        internal DeferredRepresentation(string key, string preferredSlotId)
        {
            Key = key;
            PreferredSlotId = preferredSlotId;
        }
    }

    internal static int ImportMissingItemsToDeferred(
        Player player,
        IEnumerable<ItemDrop.ItemData> sourceItems,
        string sourceName,
        Func<ItemDrop.ItemData, string> preferredSlot = null,
        Func<ItemDrop.ItemData, bool> restoreEquipped = null)
    {
        return ImportMissingItemsToDeferred(player, sourceItems, sourceName, preferredSlot, restoreEquipped, out _);
    }

    internal static int ImportMissingItemsToDeferred(
        Player player,
        IEnumerable<ItemDrop.ItemData> sourceItems,
        string sourceName,
        Func<ItemDrop.ItemData, string> preferredSlot,
        Func<ItemDrop.ItemData, bool> restoreEquipped,
        out bool allRepresented,
        Func<ItemDrop.ItemData, ItemDrop.ItemData, string, bool> representationMatchesSource = null)
    {
        allRepresented = true;
        if (player == null || sourceItems == null)
        {
            allRepresented = false;
            return 0;
        }

        DeferredPayloadSnapshot deferredBeforeImport = CaptureDeferredPayload(player);
        List<AvailableRepresentation> available = new List<AvailableRepresentation>();
        IEnumerable<ItemDrop.ItemData> playerItems = player.GetInventory()?.m_inventory ?? new List<ItemDrop.ItemData>();
        foreach (ItemDrop.ItemData item in playerItems)
        {
            string key = DeferredInventory.GetMigrationKey(item);
            if (!string.IsNullOrEmpty(key))
                available.Add(new AvailableRepresentation { Key = key, PlayerItem = item });
        }

        foreach (DeferredRepresentation representation in GetMaterializedDeferredRepresentations(player))
        {
            if (!string.IsNullOrEmpty(representation.Key))
                available.Add(new AvailableRepresentation
                {
                    Key = representation.Key,
                    DeferredPreferredSlotId = representation.PreferredSlotId
                });
        }

        int imported = 0;
        try
        {
            foreach (ItemDrop.ItemData item in sourceItems.Where(item => item != null))
            {
                string key = DeferredInventory.GetMigrationKey(item);
                string storedKey = DeferredInventory.GetMigrationKey(InventorySerialization.GetSavedRepresentation(item));
                int representedIndex = available.FindIndex(candidate =>
                    (string.Equals(candidate.Key, key, StringComparison.Ordinal)
                        || string.Equals(candidate.Key, storedKey, StringComparison.Ordinal))
                    && (representationMatchesSource == null
                        || representationMatchesSource(item, candidate.PlayerItem, candidate.DeferredPreferredSlotId)));

                if (representedIndex >= 0)
                {
                    available.RemoveAt(representedIndex);
                    continue;
                }

                string slotId = preferredSlot?.Invoke(item);
                bool shouldRestoreEquipped = restoreEquipped?.Invoke(item) ?? item.m_equipped;
                if (!DeferredInventory.EnqueueDetached(player, item, slotId, shouldRestoreEquipped, $"imported from {sourceName}"))
                {
                    allRepresented = false;
                    RestoreDeferredPayload(deferredBeforeImport);
                    LogWarning($"Recovery import from {sourceName} was rolled back after a deferred item could not be adopted.");
                    return 0;
                }

                imported++;
            }
        }
        catch (Exception ex)
        {
            allRepresented = false;
            RestoreDeferredPayload(deferredBeforeImport);
            LogWarning($"Recovery import from {sourceName} was rolled back after an unexpected error:\n{ex}");
            return 0;
        }

        if (imported > 0)
            LogMessage($"Imported {imported} missing item(s) from {sourceName} into deferred inventory.");

        return imported;
    }

    private static List<DeferredRepresentation> GetMaterializedDeferredRepresentations(Player player)
    {
        List<DeferredRepresentation> result = new List<DeferredRepresentation>();
        if (player == null || !DeferredInventory.EnsureLoaded(player)
            || !player.m_customData.TryGetValue(DeferredInventory.CustomDataKey, out string payload)
            || string.IsNullOrEmpty(payload))
        {
            return result;
        }

        try
        {
            ZPackage envelope = new ZPackage(payload);
            if (envelope.ReadInt() != DeferredInventory.EnvelopeVersion)
                return result;

            int count = envelope.ReadInt();
            if (count < 0)
                return result;

            for (int i = 0; i < count; i++)
            {
                string prefabName = envelope.ReadString();
                string preferredSlotId = envelope.ReadString();
                envelope.ReadBool();
                int originalStack = envelope.ReadInt();
                string itemPackageBase64 = envelope.ReadString();

                if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(itemPackageBase64)
                    || ObjectDB.instance?.GetItemPrefab(prefabName) == null)
                {
                    continue;
                }

                try
                {
                    ZPackage compressedItemPackage = new ZPackage(itemPackageBase64);
                    Inventory singleItemInventory = new Inventory(DeferredInventory.CustomDataKey, null, 1, 1);
                    InventorySerialization.Load(singleItemInventory, compressedItemPackage.ReadCompressedPackage());
                    ItemDrop.ItemData item = singleItemInventory.m_inventory.Count == 1 ? singleItemInventory.m_inventory[0] : null;
                    if (item == null || item.m_stack != originalStack
                        || !string.Equals(item.m_dropPrefab?.name, prefabName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add(new DeferredRepresentation(DeferredInventory.GetMigrationKey(item), preferredSlotId));
                }
                catch
                {
                    // DeferredInventory owns diagnostics and preservation for malformed/unavailable entries.
                    // A representation that cannot be materialized must not suppress recovery from a backup.
                }
            }
        }
        catch
        {
            // EnsureLoaded already validates and reports the authoritative deferred envelope.
        }

        return result;
    }

    internal static bool TryLoadInventoryPackage(string base64, string name, int width, int height, out Inventory inventory)
    {
        inventory = null;
        if (string.IsNullOrWhiteSpace(base64) || width <= 0 || height <= 0)
            return false;

        try
        {
            inventory = new Inventory(name, null, width, height);
            InventorySerialization.Load(inventory, new ZPackage(base64).ReadCompressedPackage());
            return true;
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to read {name} inventory package:\n{ex}");
            inventory = null;
            return false;
        }
    }

    private static DeferredPayloadSnapshot CaptureDeferredPayload(Player player)
    {
        string payload = null;
        bool hadPayload = player != null && player.m_customData.TryGetValue(DeferredInventory.CustomDataKey, out payload);
        return new DeferredPayloadSnapshot
        {
            Player = player,
            HadPayload = hadPayload,
            Payload = payload
        };
    }

    private static void RestoreDeferredPayload(DeferredPayloadSnapshot snapshot)
    {
        if (snapshot?.Player == null)
            return;

        if (snapshot.HadPayload)
            snapshot.Player.m_customData[DeferredInventory.CustomDataKey] = snapshot.Payload;
        else
            snapshot.Player.m_customData.Remove(DeferredInventory.CustomDataKey);

        DeferredInventory.EnsureLoaded(snapshot.Player);
    }

    private static RecoverySourceState CaptureRecoverySource(Player player, IEnumerable<string> customDataKeys = null, IEnumerable<string> knownTextKeys = null)
    {
        if (player == null)
            return null;

        RecoverySourceState state = new RecoverySourceState
        {
            Deferred = CaptureDeferredPayload(player)
        };

        if (customDataKeys != null)
        {
            foreach (string key in customDataKeys)
            {
                bool exists = player.m_customData.TryGetValue(key, out string value);
                state.CustomData[key] = (exists, value);
            }
        }

        if (knownTextKeys != null)
        {
            foreach (string key in knownTextKeys)
            {
                bool exists = player.m_knownTexts.TryGetValue(key, out string value);
                state.KnownTexts[key] = (exists, value);
            }
        }

        return state;
    }

    private static void RollbackRecoverySource(RecoverySourceState state, string sourceName)
    {
        if (state?.Deferred?.Player == null || state.RolledBack)
            return;

        state.RolledBack = true;
        Player player = state.Deferred.Player;

        foreach (KeyValuePair<string, (bool Exists, string Value)> entry in state.CustomData)
        {
            if (entry.Value.Exists)
                player.m_customData[entry.Key] = entry.Value.Value;
            else
                player.m_customData.Remove(entry.Key);
        }

        foreach (KeyValuePair<string, (bool Exists, string Value)> entry in state.KnownTexts)
        {
            if (entry.Value.Exists)
                player.m_knownTexts[entry.Key] = entry.Value.Value;
            else
                player.m_knownTexts.Remove(entry.Key);
        }

        RestoreDeferredPayload(state.Deferred);
        LogWarning($"Rolled back partial recovery adoption from {sourceName}; the original source remains intact for a later retry.");
    }

    private static bool HasNonEmptyValue(Dictionary<string, string> values, string key) =>
        values != null && values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value);

    private static bool HasMatchingFingerprint(Player player, string sourceKey, string markerKey)
    {
        if (player == null || !player.m_customData.TryGetValue(sourceKey, out string payload) || string.IsNullOrWhiteSpace(payload))
            return true;

        string fingerprint = $"{payload.Length}:{payload.GetStableHashCode()}";
        return player.m_customData.TryGetValue(markerKey, out string migratedFingerprint)
            && migratedFingerprint == fingerprint;
    }

    /// <summary>
    /// Recovery snapshots are deliberately retained when an item prefab is unavailable. Any materialized
    /// entries tentatively adopted during that same load must therefore be rolled back unless the caller
    /// marks/removes the complete source as successfully adopted. Otherwise consuming one of those items
    /// before the missing prefab returns would make the stale snapshot resurrect it on the next load.
    /// </summary>
    [HarmonyPatch]
    private static class ComfyQuickSlots_RecoveryTransaction
    {
        private const string SnapshotKey = "ComfyQuickSlotsInventory";

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ComfyQuickSlotsCompat), "ImportSnapshot");

        private static void Prefix(Player player, out RecoverySourceState __state) =>
            __state = CaptureRecoverySource(player, new[] { SnapshotKey }, new[] { SnapshotKey });

        private static void Postfix(Player player, RecoverySourceState __state)
        {
            if (HasNonEmptyValue(player?.m_customData, SnapshotKey) || HasNonEmptyValue(player?.m_knownTexts, SnapshotKey))
                RollbackRecoverySource(__state, "ComfyQuickSlots snapshot");
        }

        private static Exception Finalizer(Exception __exception, RecoverySourceState __state)
        {
            if (__exception != null)
                RollbackRecoverySource(__state, "ComfyQuickSlots snapshot");
            return __exception;
        }
    }

    [HarmonyPatch]
    private static class InventorySlots_RecoveryTransaction
    {
        private const string BackupKey = "InventorySlotsBackup";
        private const string MigrationKey = "ExtraSlotsMigrationInventorySlotsBackup";

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(InventorySlotsCompat), "ImportBackup");

        private static void Prefix(Player player, out RecoverySourceState __state) =>
            __state = CaptureRecoverySource(player, new[] { BackupKey, MigrationKey });

        private static void Postfix(Player player, RecoverySourceState __state)
        {
            if (!HasMatchingFingerprint(player, BackupKey, MigrationKey))
                RollbackRecoverySource(__state, "InventorySlots backup");
        }

        private static Exception Finalizer(Exception __exception, RecoverySourceState __state)
        {
            if (__exception != null)
                RollbackRecoverySource(__state, "InventorySlots backup");
            return __exception;
        }
    }

    [HarmonyPatch]
    private static class EquipmentAndQuickSlotsBackup_RecoveryTransaction
    {
        private const string BackupKey = "eaqs_backup";
        private const string MigrationKey = "ExtraSlotsMigrationEaQSBackup";

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(EquipmentAndQuickSlotsCompat), "ImportCurrentBackup");

        private static void Prefix(Player player, out RecoverySourceState __state) =>
            __state = CaptureRecoverySource(player, new[] { BackupKey, MigrationKey });

        private static void Postfix(Player player, RecoverySourceState __state)
        {
            if (!HasMatchingFingerprint(player, BackupKey, MigrationKey))
                RollbackRecoverySource(__state, "EaQS 3.x backup");
        }

        private static Exception Finalizer(Exception __exception, RecoverySourceState __state)
        {
            if (__exception != null)
                RollbackRecoverySource(__state, "EaQS 3.x backup");
            return __exception;
        }
    }

    [HarmonyPatch]
    private static class ExtraSlotsBackup_RecoveryTransaction
    {
        private const string BackupKey = "ExtraSlotsInventoryBackup";
        private static readonly FieldInfo preserveRawBackupForPlayer = AccessTools.Field(typeof(global::ExtraSlots.InventoryBackup), "preserveRawBackupForPlayer");

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(global::ExtraSlots.InventoryBackup), "TryRestoreBackup");

        private static void Prefix(Player player, out RecoverySourceState __state) =>
            __state = CaptureRecoverySource(player, new[] { BackupKey });

        private static void Postfix(Player player, RecoverySourceState __state)
        {
            if (ReferenceEquals(preserveRawBackupForPlayer?.GetValue(null), player))
                RollbackRecoverySource(__state, "ExtraSlots backup");
        }

        private static Exception Finalizer(Exception __exception, RecoverySourceState __state)
        {
            if (__exception != null)
                RollbackRecoverySource(__state, "ExtraSlots backup");
            return __exception;
        }
    }
}
