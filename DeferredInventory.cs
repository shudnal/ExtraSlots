using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    /// <summary>
    /// Durable escrow for player-owned items that currently have no valid physical cell in the
    /// player inventory. Deferred items are intentionally absent from the gameplay Inventory and
    /// therefore have no weight, crafting, teleport, hotbar, equipment, or capacity semantics.
    ///
    /// Persistence format stored in Player.m_customData[CustomDataKey]:
    /// int envelopeVersion, int entryCount, then for every entry in queue order:
    /// string prefabName, string preferredSlotId, bool restoreEquipped, int originalStack,
    /// string itemPackageBase64. The last value is a ZPackage containing WriteCompressed() output
    /// for a vanilla Inventory.Save package with exactly one item. The prefab name is duplicated
    /// before that opaque payload so unavailable prefabs can be rejected without decompressing,
    /// parsing, or mutating the preserved vanilla item data.
    /// </summary>
    public static class DeferredInventory
    {
        public const string CustomDataKey = "ExtraSlotsDeferredInventory";
        public const int EnvelopeVersion = 1;

        private sealed class DeferredEntry
        {
            internal string PrefabName;
            internal string PreferredSlotId;
            internal bool RestoreEquipped;
            internal int OriginalStack;
            internal string ItemPackageBase64;
        }

        private static readonly List<DeferredEntry> entries = new List<DeferredEntry>();
        private static readonly HashSet<string> unavailablePrefabsLogged = new HashSet<string>(StringComparer.Ordinal);
        private static Player statePlayer;
        private static string statePayload = "";
        private static bool storageAvailable = true;
        private static int revision;
        private static int lastAttemptRevision = -1;
        private static int lastRestorationFingerprint;
        private static bool hasLastRestorationFingerprint;

        public static int Count(Player player = null)
        {
            player ??= CurrentPlayer;
            return EnsureLoaded(player) ? entries.Count : 0;
        }

        internal static void InvalidateRestorationOpportunity()
        {
            lastAttemptRevision = -1;
            hasLastRestorationFingerprint = false;
        }

        internal static bool EnsureLoaded(Player player)
        {
            if (player == null)
                return false;

            string payload = player.m_customData.TryGetValue(CustomDataKey, out string value) ? value ?? "" : "";
            if (ReferenceEquals(statePlayer, player) && string.Equals(statePayload, payload, StringComparison.Ordinal))
                return storageAvailable;

            statePlayer = player;
            statePayload = payload;
            entries.Clear();
            unavailablePrefabsLogged.Clear();
            storageAvailable = true;
            revision = 0;
            lastAttemptRevision = -1;
            hasLastRestorationFingerprint = false;

            if (string.IsNullOrEmpty(payload))
                return true;

            try
            {
                ZPackage envelope = new ZPackage(payload);
                int version = envelope.ReadInt();
                if (version != EnvelopeVersion)
                {
                    storageAvailable = false;
                    LogWarning($"Deferred inventory uses unsupported envelope version {version}. Existing data will be left untouched.");
                    return false;
                }

                int count = envelope.ReadInt();
                if (count < 0)
                    throw new InvalidOperationException($"Invalid deferred inventory entry count {count}.");

                for (int i = 0; i < count; i++)
                {
                    string prefabName = envelope.ReadString();
                    string preferredSlotId = envelope.ReadString();
                    bool restoreEquipped = envelope.ReadBool();
                    int originalStack = envelope.ReadInt();
                    string itemPackageBase64 = envelope.ReadString();
                    if (string.IsNullOrEmpty(itemPackageBase64))
                        throw new InvalidOperationException($"Deferred inventory entry {i} has an empty item package.");

                    entries.Add(new DeferredEntry
                    {
                        PrefabName = prefabName,
                        PreferredSlotId = preferredSlotId,
                        RestoreEquipped = restoreEquipped,
                        OriginalStack = originalStack,
                        ItemPackageBase64 = itemPackageBase64
                    });
                }

                LogDebug($"Deferred inventory loaded with {entries.Count} item(s).");
                return true;
            }
            catch (Exception ex)
            {
                entries.Clear();
                storageAvailable = false;
                LogWarning($"Failed to read deferred inventory. Existing data will be left untouched:\n{ex}");
                return false;
            }
        }

        internal static bool Flush(Player player = null)
        {
            player ??= statePlayer ?? CurrentPlayer;
            if (player == null || !EnsureLoaded(player) || !storageAvailable)
                return false;

            try
            {
                if (entries.Count == 0)
                {
                    player.m_customData.Remove(CustomDataKey);
                    statePayload = "";
                    return true;
                }

                ZPackage envelope = new ZPackage();
                envelope.Write(EnvelopeVersion);
                envelope.Write(entries.Count);

                foreach (DeferredEntry entry in entries)
                {
                    // Keep the prefab name before the opaque item payload in the documented format.
                    // Readers can decide whether the prefab exists before touching the item package.
                    envelope.Write(entry.PrefabName ?? "");
                    envelope.Write(entry.PreferredSlotId ?? "");
                    envelope.Write(entry.RestoreEquipped);
                    envelope.Write(entry.OriginalStack);
                    envelope.Write(entry.ItemPackageBase64 ?? "");
                }

                statePayload = envelope.GetBase64();
                player.m_customData[CustomDataKey] = statePayload;
                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to persist deferred inventory:\n{ex}");
                return false;
            }
        }

        private static bool TryCreateEntry(ItemDrop.ItemData item, string preferredSlotId, bool restoreEquipped, out DeferredEntry entry)
        {
            entry = null;
            if (item == null)
                return false;

            string prefabName = item.m_dropPrefab?.name;
            if (string.IsNullOrEmpty(prefabName))
            {
                LogWarning($"Cannot defer {item.m_shared?.m_name ?? "<unknown>"}: item has no prefab reference.");
                return false;
            }

            try
            {
                ItemDrop.ItemData storedItem = item.Clone();
                storedItem.m_equipped = false;
                storedItem.m_gridPos = new Vector2i(0, 0);

                Inventory singleItemInventory = new Inventory(CustomDataKey, null, 1, 1);
                singleItemInventory.m_inventory.Add(storedItem);
                ZPackage itemPackage = new ZPackage();
                singleItemInventory.Save(itemPackage);

                // Wrap the vanilla item package in a compressed ZPackage, but persist the wrapper as
                // an opaque Base64 string. Deferred envelope readers therefore need only read the
                // prefab name and can leave item bytes completely untouched while that prefab is absent.
                ZPackage compressedItemPackage = new ZPackage();
                compressedItemPackage.WriteCompressed(itemPackage);

                entry = new DeferredEntry
                {
                    PrefabName = prefabName,
                    PreferredSlotId = preferredSlotId ?? "",
                    RestoreEquipped = restoreEquipped,
                    OriginalStack = item.m_stack,
                    ItemPackageBase64 = compressedItemPackage.GetBase64()
                };
                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to serialize {item.m_shared?.m_name ?? prefabName} for deferred inventory:\n{ex}");
                return false;
            }
        }

        private static bool TryMaterialize(DeferredEntry entry, out ItemDrop.ItemData item)
        {
            item = null;
            if (entry == null || string.IsNullOrEmpty(entry.PrefabName) || string.IsNullOrEmpty(entry.ItemPackageBase64))
                return false;

            if (ObjectDB.instance?.GetItemPrefab(entry.PrefabName) == null)
            {
                if (unavailablePrefabsLogged.Add(entry.PrefabName))
                    LogWarning($"Deferred item prefab {entry.PrefabName} is unavailable. Its opaque item package will be preserved until the prefab exists again.");
                return false;
            }

            try
            {
                ZPackage compressedItemPackage = new ZPackage(entry.ItemPackageBase64);
                ZPackage itemPackage = compressedItemPackage.ReadCompressedPackage();
                Inventory singleItemInventory = new Inventory(CustomDataKey, null, 1, 1);
                singleItemInventory.Load(itemPackage);
                item = singleItemInventory.m_inventory.FirstOrDefault();
                if (item == null)
                    return false;

                singleItemInventory.m_inventory.Clear();
                item.m_equipped = false;

                // Inventory.Load clamps stack size to the currently loaded prefab definition. Do not
                // silently adopt a changed representation; keep the original package opaque instead.
                if (item.m_stack != entry.OriginalStack)
                {
                    LogWarning($"Deferred item {entry.PrefabName} stack changed while materializing ({entry.OriginalStack} -> {item.m_stack}). Original package will be preserved.");
                    item = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to materialize deferred item {entry.PrefabName}. Original package will be preserved:\n{ex}");
                item = null;
                return false;
            }
        }

        private static string CapturePreferredSlot(ItemDrop.ItemData item)
        {
            if (item == null)
                return "";

            if (GetItemSlot(item) is Slot currentSlot && !currentSlot.IsEmptySlot)
                return currentSlot.ID;

            if (item.m_customData.TryGetValue(customKeyPlayerID, out string playerID)
                && item.m_customData.TryGetValue(customKeySlotID, out string slotID)
                && playerID == CurrentPlayerProfile?.GetPlayerID().ToString())
            {
                return slotID;
            }

            return "";
        }

        private static void ApplyPreferredSlotMetadata(Player player, ItemDrop.ItemData item, string preferredSlotId)
        {
            if (player == null || item == null || string.IsNullOrEmpty(preferredSlotId))
                return;

            item.m_customData[customKeyPlayerID] = player.GetPlayerID().ToString();
            item.m_customData[customKeySlotID] = preferredSlotId;
        }

        private static void CancelTransientReferences(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null)
                return;

            while (player.IsEquipActionQueued(item))
                player.RemoveEquipAction(item);

            // IsEquipActionQueued/RemoveEquipAction are the normal game helpers for queued equips.
            // Also remove a matching queued unequip directly: once the item leaves PlayerInventory,
            // either queued action is broken and must not retain a stale ItemData reference.
            player.m_actionQueue.RemoveAll(action => action != null
                && ReferenceEquals(action.m_item, item)
                && (action.m_type == Player.MinorActionData.ActionType.Equip
                    || action.m_type == Player.MinorActionData.ActionType.Unequip));

            InventoryGui inventoryGui = InventoryGui.instance;
            if (inventoryGui != null && ReferenceEquals(inventoryGui.m_dragItem, item))
                inventoryGui.SetupDragItem(null, null, 1);
        }

        internal static bool DeferExisting(ItemDrop.ItemData item, string reason = null, string preferredSlotId = null, bool restoreEquipped = false)
        {
            Player player = CurrentPlayer;
            Inventory inventory = PlayerInventory;
            if (player == null || inventory == null || item == null || !inventory.ContainsItem(item) || !EnsureLoaded(player))
                return false;

            preferredSlotId ??= CapturePreferredSlot(item);
            bool wasEquipped = restoreEquipped || item.m_equipped || player.IsItemEquiped(item);

            ItemDrop.ItemData storedItem = item.Clone();
            storedItem.m_equipped = false;
            ApplyPreferredSlotMetadata(player, storedItem, preferredSlotId);
            if (!TryCreateEntry(storedItem, preferredSlotId, wasEquipped, out DeferredEntry entry))
                return false;

            entries.Add(entry);
            revision++;
            if (!Flush(player))
            {
                entries.Remove(entry);
                revision++;
                return false;
            }

            CancelTransientReferences(player, item);
            if (item.m_equipped || player.IsItemEquiped(item))
                player.UnequipItem(item, triggerEquipEffects: false);
            item.m_equipped = false;

            if (!inventory.m_inventory.Remove(item))
            {
                entries.Remove(entry);
                revision++;
                Flush(player);
                return false;
            }

            ClearCachedItems();
            PlayerInventoryOperations.MarkChanged(inventory);
            LogWarning($"Item {item.m_shared?.m_name ?? entry.PrefabName} was moved to deferred inventory{(string.IsNullOrEmpty(reason) ? "." : $" ({reason}).")}");
            return true;
        }

        internal static bool EnqueueDetached(Player player, ItemDrop.ItemData item, string preferredSlotId = null, bool restoreEquipped = false, string reason = null)
        {
            if (player == null || item == null || !EnsureLoaded(player))
                return false;

            ItemDrop.ItemData storedItem = item.Clone();
            storedItem.m_equipped = false;
            ApplyPreferredSlotMetadata(player, storedItem, preferredSlotId);

            if (!TryCreateEntry(storedItem, preferredSlotId, restoreEquipped || item.m_equipped, out DeferredEntry entry))
                return false;

            entries.Add(entry);
            revision++;
            if (!Flush(player))
            {
                entries.Remove(entry);
                revision++;
                return false;
            }

            LogWarning($"Item {item.m_shared?.m_name ?? entry.PrefabName} was added to deferred inventory{(string.IsNullOrEmpty(reason) ? "." : $" ({reason}).")}");
            return true;
        }

        internal static string GetMigrationKey(ItemDrop.ItemData item)
        {
            if (item == null)
                return "";

            // Migration sources are recovery data. Prefer a false negative (which can duplicate an
            // old backup) over a false positive that would suppress restoration of a distinct item.
            // Ignore only physical position and equipped state because those are topology/runtime
            // properties; preserve the complete item identity that matters to gameplay.
            StringBuilder key = new StringBuilder();
            Append(item.m_dropPrefab?.name ?? "");
            Append(item.m_stack.ToString(CultureInfo.InvariantCulture));
            Append(item.m_quality.ToString(CultureInfo.InvariantCulture));
            Append(item.m_variant.ToString(CultureInfo.InvariantCulture));
            Append(item.m_durability.ToString("R", CultureInfo.InvariantCulture));
            Append(item.m_crafterID.ToString(CultureInfo.InvariantCulture));
            Append(item.m_crafterName ?? "");
            Append(item.m_worldLevel.ToString(CultureInfo.InvariantCulture));
            Append(item.m_pickedUp ? "1" : "0");

            if (item.m_customData != null)
            {
                foreach (KeyValuePair<string, string> pair in item.m_customData.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    // ExtraSlots slot provenance is transport metadata, not intrinsic item identity.
                    // Deferred/migration adoption can add or prune these keys while the item itself
                    // remains exactly the same, so including them would make recovery non-idempotent.
                    if (pair.Key == customKeyPlayerID || pair.Key == customKeySlotID || pair.Key == customKeyWeaponShield)
                        continue;

                    Append(pair.Key ?? "");
                    Append(pair.Value ?? "");
                }
            }

            return key.ToString();

            void Append(string value)
            {
                key.Append(value.Length.ToString(CultureInfo.InvariantCulture));
                key.Append(':');
                key.Append(value);
                key.Append(';');
            }
        }

        internal static List<string> GetMaterializedMigrationKeys(Player player)
        {
            List<string> result = new List<string>();
            if (player == null || !EnsureLoaded(player))
                return result;

            foreach (DeferredEntry entry in entries)
                if (TryMaterialize(entry, out ItemDrop.ItemData item))
                    result.Add(GetMigrationKey(item));

            return result;
        }

        private static int ComputeRestorationFingerprint()
        {
            Inventory inventory = PlayerInventory;
            if (inventory?.m_inventory == null)
                return 0;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + inventory.m_width;
                hash = hash * 31 + InventoryHeightPlayer;
                hash = hash * 31 + InventoryHeightFull;

                foreach (ItemDrop.ItemData item in inventory.m_inventory)
                {
                    if (item == null)
                        continue;

                    hash = hash * 31 + (item.m_dropPrefab?.name?.GetStableHashCode() ?? item.m_shared?.m_name?.GetStableHashCode() ?? 0);
                    hash = hash * 31 + item.m_stack;
                    hash = hash * 31 + item.m_quality;
                    hash = hash * 31 + item.m_variant;
                    hash = hash * 31 + item.m_worldLevel;
                    // Physical movement between equally usable cells is not a reason to retry the
                    // deferred queue. Capacity/stack/slot state changes below are.
                    hash = hash * 31 + (item.m_gridPos.y < InventoryHeightPlayer ? 1 : 2);
                }

                foreach (Slot slot in slots)
                {
                    hash = hash * 31 + slot.ID.GetStableHashCode();
                    hash = hash * 31 + (slot.IsActive ? 1 : 0);
                    hash = hash * 31 + (slot.IsFree ? 1 : 0);
                }

                return hash;
            }
        }

        internal static bool TryRestoreAvailable()
        {
            Player player = CurrentPlayer;
            if (player == null || player.m_isLoading || PlayerInventory == null || !EnsureLoaded(player) || entries.Count == 0)
                return false;

            int fingerprint = ComputeRestorationFingerprint();
            if (lastAttemptRevision == revision && hasLastRestorationFingerprint && lastRestorationFingerprint == fingerprint)
                return false;

            bool changed = false;
            using (PlayerInventoryOperations.Batch(PlayerInventory))
            {
                for (int i = 0; i < entries.Count;)
                {
                    DeferredEntry entry = entries[i];
                    if (!TryMaterialize(entry, out ItemDrop.ItemData item))
                    {
                        i++;
                        continue;
                    }

                    ApplyPreferredSlotMetadata(player, item, entry.PreferredSlotId);
                    Compatibility.EquipmentAndQuickSlotsCompat.ApplyCurrentSlotMetadata(player, item);
                    Compatibility.InventorySlotsCompat.ApplyCurrentSlotMetadata(player, item);

                    if (!PlayerInventoryOperations.TryInsertDetachedToBestAvailable(item, entry.PreferredSlotId, entry.RestoreEquipped, out ItemDrop.ItemData placedItem, out bool fullyInserted, out bool madeProgress))
                    {
                        i++;
                        continue;
                    }

                    if (fullyInserted)
                    {
                        entries.RemoveAt(i);
                        revision++;
                        changed = true;

                        if (entry.RestoreEquipped && placedItem != null
                            && GetItemSlot(placedItem) is Slot placedSlot && placedSlot.IsEquipmentSlot
                            && !player.IsItemEquiped(placedItem))
                        {
                            player.EquipItem(placedItem, triggerEquipEffects: false);
                        }

                        LogMessage($"Deferred item {entry.PrefabName} was restored to player inventory.");
                        continue;
                    }

                    if (madeProgress && item.m_stack > 0)
                    {
                        if (TryCreateEntry(item, entry.PreferredSlotId, entry.RestoreEquipped, out DeferredEntry updatedEntry))
                        {
                            entries[i] = updatedEntry;
                            revision++;
                            changed = true;
                        }
                    }

                    i++;
                }
            }

            if (changed)
                Flush(player);

            lastAttemptRevision = revision;
            lastRestorationFingerprint = ComputeRestorationFingerprint();
            hasLastRestorationFingerprint = true;
            return changed;
        }

        internal static bool MoveAllToTombstone(Inventory graveInventory)
        {
            Player player = CurrentPlayer;
            if (player == null || graveInventory == null || !EnsureLoaded(player) || entries.Count == 0)
                return false;

            bool changed = false;
            for (int i = 0; i < entries.Count;)
            {
                DeferredEntry entry = entries[i];
                if (!TryMaterialize(entry, out ItemDrop.ItemData item))
                {
                    i++;
                    continue;
                }

                ApplyPreferredSlotMetadata(player, item, entry.PreferredSlotId);
                item.m_equipped = false;

                Vector2i target = graveInventory.FindEmptySlot(topFirst: true);
                if (target.x < 0)
                {
                    graveInventory.m_height++;
                    target = graveInventory.FindEmptySlot(topFirst: true);
                }

                // A materialized deferred entry always represents one valid vanilla stack. Put the
                // whole stack into a known-empty grave cell in one operation so tombstone transfer
                // cannot partially merge a deferred entry and then leave its authoritative payload
                // behind. Containers are allowed to grow; player inventory geometry never is.
                int amount = item.m_stack;
                if (target.x < 0 || !graveInventory.AddItem(item, amount, target.x, target.y))
                {
                    LogWarning($"Unable to place deferred item {entry.PrefabName} into expanded tombstone inventory. Item remains deferred.");
                    i++;
                    continue;
                }

                entries.RemoveAt(i);
                revision++;
                changed = true;
                LogMessage($"Deferred item {entry.PrefabName} was moved into tombstone inventory.");
            }

            if (changed)
                Flush(player);

            return changed;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load_ReadDeferredInventory
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance)
            {
                EnsureLoaded(__instance);
                ItemsSlotsValidation.ValidateItems();
                ItemsSlotsValidation.ValidateSlots();
                ItemsSlotsValidation.Validate();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        private static class Player_Save_FlushDeferredInventory
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance)
            {
                if (ReferenceEquals(__instance, statePlayer) || __instance == CurrentPlayer)
                    Flush(__instance);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave))]
        private static class Inventory_MoveInventoryToGrave_AppendDeferredItems
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Inventory __instance, Inventory original)
            {
                if (original != PlayerInventory)
                    return;

                MoveAllToTombstone(__instance);
            }
        }
    }
}
