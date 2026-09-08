using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    /// <summary>
    /// Owns opaque deferred entries after death. Materializable deferred items are moved into the
    /// grave inventory by DeferredInventory first; anything that cannot currently be materialized
    /// is transferred verbatim to the tombstone ZDO so it cannot return directly to the respawned
    /// player. The grave retains that opaque ownership until the missing prefab becomes available.
    /// </summary>
    internal static class DeferredTombstone
    {
        private const string PayloadKey = "ExtraSlotsDeferredTombstone";
        private static readonly int payloadHash = PayloadKey.GetStableHashCode();
        private static readonly HashSet<string> unavailablePrefabsLogged = new HashSet<string>(StringComparer.Ordinal);

        private sealed class Entry
        {
            internal string PrefabName;
            internal string PreferredSlotId;
            internal bool RestoreEquipped;
            internal int OriginalStack;
            internal string ItemPackageBase64;
        }

        internal static bool HasPayload(TombStone tombstone) =>
            !string.IsNullOrEmpty(GetPayload(tombstone));

        private static string GetPayload(TombStone tombstone)
        {
            if (tombstone?.m_nview?.IsValid() != true)
                return "";

            return tombstone.m_nview.GetZDO()?.GetString(payloadHash, "") ?? "";
        }

        private static bool TransferRemainingPlayerPayload(TombStone tombstone)
        {
            Player player = CurrentPlayer;
            if (player == null || tombstone?.m_nview?.IsValid() != true || !tombstone.m_nview.IsOwner())
                return false;

            if (!player.m_customData.TryGetValue(DeferredInventory.CustomDataKey, out string payload) || string.IsNullOrEmpty(payload))
                return false;

            ZDO zdo = tombstone.m_nview.GetZDO();
            if (zdo == null)
                return false;

            string existing = zdo.GetString(payloadHash, "");
            if (!string.IsNullOrEmpty(existing) && !string.Equals(existing, payload, StringComparison.Ordinal))
            {
                LogWarning("Tombstone already contains a different opaque deferred payload. Player deferred data was preserved instead of overwriting grave ownership.");
                return false;
            }

            zdo.Set(payloadHash, payload);
            if (!string.Equals(zdo.GetString(payloadHash, ""), payload, StringComparison.Ordinal))
            {
                LogWarning("Failed to persist opaque deferred ownership on tombstone. Player deferred data was left intact.");
                return false;
            }

            player.m_customData.Remove(DeferredInventory.CustomDataKey);
            DeferredInventory.EnsureLoaded(player);
            LogMessage("Remaining opaque deferred item data was transferred to tombstone ownership.");
            return true;
        }

        private static int MaterializeAvailable(TombStone tombstone)
        {
            if (tombstone?.m_nview?.IsValid() != true || !tombstone.m_nview.IsOwner())
                return 0;

            string payload = GetPayload(tombstone);
            if (string.IsNullOrEmpty(payload))
                return 0;

            if (!TryReadPayload(payload, out List<Entry> graveEntries))
                return 0;

            Container container = tombstone.m_container != null ? tombstone.m_container : tombstone.GetComponent<Container>();
            Inventory inventory = container?.GetInventory();
            if (inventory == null)
                return 0;

            int appendStartHeight = inventory.m_height;
            int materialized = 0;
            long ownerId = tombstone.GetOwner();

            for (int i = 0; i < graveEntries.Count;)
            {
                Entry entry = graveEntries[i];
                if (!TryMaterialize(entry, out ItemDrop.ItemData item))
                {
                    i++;
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.PreferredSlotId) && ownerId != 0L)
                {
                    item.m_customData[customKeyPlayerID] = ownerId.ToString();
                    item.m_customData[customKeySlotID] = entry.PreferredSlotId;
                }
                item.m_equipped = false;

                Vector2i target = FindAppendPosition(inventory, appendStartHeight);
                if (target.x < 0)
                {
                    inventory.m_height++;
                    target = FindAppendPosition(inventory, appendStartHeight);
                }

                int amount = item.m_stack;
                if (target.x < 0 || !inventory.AddItem(item, amount, target.x, target.y))
                {
                    LogWarning($"Unable to materialize tombstone-owned deferred item {entry.PrefabName} into grave inventory. Opaque entry remains attached to the grave.");
                    i++;
                    continue;
                }

                graveEntries.RemoveAt(i);
                materialized++;
                PersistPayload(tombstone, graveEntries);
                LogMessage($"Tombstone-owned deferred item {entry.PrefabName} materialized into grave inventory.");
            }

            if (materialized > 0)
            {
                container.m_width = Math.Max(container.m_width, inventory.m_width);
                container.m_height = Math.Max(container.m_height, inventory.m_height);
                PersistDimensions(container);
            }

            return materialized;
        }

        private static bool TryReadPayload(string payload, out List<Entry> result)
        {
            result = new List<Entry>();
            try
            {
                ZPackage envelope = new ZPackage(payload);
                int version = envelope.ReadInt();
                if (version != DeferredInventory.EnvelopeVersion)
                {
                    LogWarning($"Tombstone deferred payload uses unsupported envelope version {version}. Opaque data will remain attached to the grave.");
                    return false;
                }

                int count = envelope.ReadInt();
                if (count < 0)
                    throw new InvalidOperationException($"Invalid tombstone deferred entry count {count}.");

                for (int i = 0; i < count; i++)
                {
                    string prefabName = envelope.ReadString();
                    string preferredSlotId = envelope.ReadString();
                    bool restoreEquipped = envelope.ReadBool();
                    int originalStack = envelope.ReadInt();
                    string itemPackageBase64 = envelope.ReadString();
                    if (string.IsNullOrEmpty(itemPackageBase64))
                        throw new InvalidOperationException($"Tombstone deferred entry {i} has an empty item package.");

                    result.Add(new Entry
                    {
                        PrefabName = prefabName,
                        PreferredSlotId = preferredSlotId,
                        RestoreEquipped = restoreEquipped,
                        OriginalStack = originalStack,
                        ItemPackageBase64 = itemPackageBase64
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                result.Clear();
                LogWarning($"Failed to read tombstone deferred payload. Opaque data will remain attached to the grave:\n{ex}");
                return false;
            }
        }

        private static bool TryMaterialize(Entry entry, out ItemDrop.ItemData item)
        {
            item = null;
            if (entry == null || string.IsNullOrEmpty(entry.PrefabName) || string.IsNullOrEmpty(entry.ItemPackageBase64))
                return false;

            if (ObjectDB.instance?.GetItemPrefab(entry.PrefabName) == null)
            {
                if (unavailablePrefabsLogged.Add(entry.PrefabName))
                    LogWarning($"Tombstone deferred item prefab {entry.PrefabName} is unavailable. Opaque grave ownership will be preserved.");
                return false;
            }

            try
            {
                ZPackage compressedItemPackage = new ZPackage(entry.ItemPackageBase64);
                Inventory singleItemInventory = new Inventory(PayloadKey, null, 1, 1);
                singleItemInventory.Load(compressedItemPackage.ReadCompressedPackage());
                item = singleItemInventory.m_inventory.Count == 1 ? singleItemInventory.m_inventory[0] : null;
                if (item == null || item.m_stack != entry.OriginalStack
                    || !string.Equals(item.m_dropPrefab?.name, entry.PrefabName, StringComparison.Ordinal))
                {
                    item = null;
                    LogWarning($"Tombstone deferred item {entry.PrefabName} did not materialize as the expected single stack. Opaque grave ownership will be preserved.");
                    return false;
                }

                singleItemInventory.m_inventory.Clear();
                item.m_equipped = false;
                return true;
            }
            catch (Exception ex)
            {
                item = null;
                LogWarning($"Failed to materialize tombstone deferred item {entry.PrefabName}. Opaque grave ownership will be preserved:\n{ex}");
                return false;
            }
        }

        private static void PersistPayload(TombStone tombstone, IReadOnlyList<Entry> graveEntries)
        {
            ZDO zdo = tombstone?.m_nview?.GetZDO();
            if (zdo == null)
                return;

            if (graveEntries == null || graveEntries.Count == 0)
            {
                zdo.Set(payloadHash, "");
                return;
            }

            ZPackage envelope = new ZPackage();
            envelope.Write(DeferredInventory.EnvelopeVersion);
            envelope.Write(graveEntries.Count);
            foreach (Entry entry in graveEntries)
            {
                envelope.Write(entry.PrefabName ?? "");
                envelope.Write(entry.PreferredSlotId ?? "");
                envelope.Write(entry.RestoreEquipped);
                envelope.Write(entry.OriginalStack);
                envelope.Write(entry.ItemPackageBase64 ?? "");
            }

            zdo.Set(payloadHash, envelope.GetBase64());
        }

        private static Vector2i FindAppendPosition(Inventory inventory, int startHeight)
        {
            for (int y = Math.Max(0, startHeight); y < inventory.m_height; y++)
                for (int x = 0; x < inventory.m_width; x++)
                    if (inventory.GetItemAt(x, y) == null)
                        return new Vector2i(x, y);

            return emptyPosition;
        }

        private static bool PersistDimensions(Container container)
        {
            if (container?.m_nview?.IsValid() != true || !container.m_nview.IsOwner() || container.GetComponentInParent<TombStone>() == null)
                return false;

            string typeName = container.GetType().Name;
            ZDO zdo = container.m_nview.GetZDO();
            zdo.Set(ZNetView.CustomFieldsStr, true);
            zdo.Set((ZNetView.CustomFieldsStr + typeName).GetStableHashCode(), true);
            zdo.Set(typeName + ".m_width", container.m_width);
            zdo.Set(typeName + ".m_height", container.m_height);
            return true;
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
        private static class TombStone_Setup_TransferOpaqueDeferred
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(TombStone __instance) => TransferRemainingPlayerPayload(__instance);
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
        private static class TombStone_Interact_MaterializeOpaqueDeferred
        {
            [HarmonyPriority(Priority.Last)]
            private static bool Prefix(TombStone __instance, Humanoid character, bool hold, bool alt, ref bool __result)
            {
                if (!HasPayload(__instance))
                    return true;

                MaterializeAvailable(__instance);
                if (!HasPayload(__instance) || __instance.m_container?.GetInventory()?.NrOfItems() > 0)
                    return true;

                if (hold)
                    return true;

                // An opaque-only grave cannot pass vanilla's initial NrOfItems check. Request the
                // container normally so ownership is transferred; RPC_OpenRespons will materialize
                // any entries whose prefabs are available before InventoryGui.Show runs.
                __instance.m_localOpened = true;
                __result = __instance.m_container != null && __instance.m_container.Interact(character, hold: false, alt);
                return false;
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory))]
        private static class TombStone_EasyFitInInventory_OpaqueDeferredGuard
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(TombStone __instance, ref bool __result)
            {
                if (HasPayload(__instance))
                    __result = false;
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.GetHoverText))]
        private static class TombStone_GetHoverText_ShowOpaqueDeferred
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(TombStone __instance, ref string __result)
            {
                if (!string.IsNullOrEmpty(__result) || !HasPayload(__instance) || __instance.m_nview?.IsValid() != true)
                    return;

                string text = __instance.m_text + " " + __instance.GetOwnerName();
                __result = Localization.instance.Localize(text + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open");
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.RPC_OpenRespons))]
        private static class Container_RPC_OpenRespons_MaterializeOpaqueDeferred
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Container __instance, bool granted)
            {
                if (!granted || __instance == null || __instance.GetComponentInParent<TombStone>() is not TombStone tombstone || !HasPayload(tombstone))
                    return;

                __instance.m_nview?.ClaimOwnership();
                MaterializeAvailable(tombstone);
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.UpdateDespawn))]
        private static class TombStone_UpdateDespawn_PreserveOpaqueDeferred
        {
            private static void Prefix(TombStone __instance, out bool __state)
            {
                __state = false;
                if (!HasPayload(__instance))
                    return;

                MaterializeAvailable(__instance);
                if (!HasPayload(__instance) || __instance.m_container == null || __instance.m_container.IsInUse()
                    || __instance.m_container.GetInventory()?.NrOfItems() > 0)
                {
                    return;
                }

                // Vanilla destroys an owner-side empty grave. Temporarily mark only this update as
                // in-use so all normal floating/position maintenance still runs while opaque data remains.
                __instance.m_container.m_inUse = true;
                __state = true;
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(TombStone __instance, bool __state, Exception __exception)
            {
                if (__state && __instance?.m_container != null)
                    __instance.m_container.m_inUse = false;

                return __exception;
            }
        }
    }
}
