using System;
using System.Collections.Generic;
using System.IO;

namespace ExtraSlots
{
    internal static class InventorySerialization
    {
        internal static int ReadItemCount(ZPackage header)
        {
            int version = header.ReadInt();
            if (version < 100 || version > (int)global::Version.c_ItemDataVersion)
                throw new InvalidDataException($"Unsupported inventory format {version}; the original payload must be retained.");

            int count = version >= (int)global::Version.Item.Smaller ? header.ReadUShort() : header.ReadInt();
            if (count < 0)
                throw new InvalidDataException($"Invalid inventory item count {count}.");
            return count;
        }

        /// <summary>
        /// One original inventory record, including its original format version. The payload
        /// is never reserialized from a prefab, so an unavailable mod cannot erase its data.
        /// </summary>
        internal sealed class RecoveryRecord
        {
            internal string Payload;
            internal int PrefabHash;
            internal string PrefabName;
            internal int Stack;
            internal bool IsReadable;

            internal string Description => string.IsNullOrEmpty(PrefabName)
                ? $"prefab hash {PrefabHash}" : PrefabName;

            internal ItemDrop.ItemData Materialize(string inventoryName)
            {
                // Recovery inventories are detached from gameplay. Allow every coordinate
                // representable by the current format, including old backup-relative cells.
                Inventory inventory = new Inventory(inventoryName, null, 256, 256);
                bool forceDisableInit = ZNetView.m_forceDisableInit;
                try
                {
                    Load(inventory, new ZPackage(Payload).ReadCompressedPackage());
                }
                finally
                {
                    ZNetView.m_forceDisableInit = forceDisableInit;
                }
                if (inventory.m_inventory.Count != 1)
                    return null;

                ItemDrop.ItemData item = inventory.m_inventory[0];
                if (item?.m_dropPrefab == null || item.m_shared == null || Stack <= 0
                    || item.m_stack != Stack || item.m_dropPrefab.name.GetStableHashCode() != PrefabHash)
                    return null;

                return item;
            }
        }

        /// <summary>
        /// Split before materialization: a missing prefab must not remove an entry from the
        /// source or change the identity/order of its neighbors. Current-format boundaries
        /// come from the game's ItemData reader; only the legacy layout needs a skip reader.
        /// An unreadable unframed package is left whole by the caller, never guessed past.
        /// </summary>
        internal static List<RecoveryRecord> ReadRecoveryRecords(string payload)
        {
            ZPackage package = new ZPackage(payload).ReadCompressedPackage();
            byte[] bytes = package.GetArray();
            int count = ReadItemCount(new ZPackage(bytes));
            global::Version.Item version = (global::Version.Item)package.ReadInt();
            if (version >= global::Version.Item.Smaller)
                package.ReadUShort();
            else
                package.ReadInt();

            if (count > bytes.Length)
                throw new InvalidDataException("Inventory record count exceeds its payload size.");

            List<RecoveryRecord> records = new List<RecoveryRecord>();
            for (int i = 0; i < count; i++)
            {
                int start = package.GetPos();
                RecoveryRecord record;
                if (version >= global::Version.Item.Smaller)
                {
                    var (prefabHash, item) = ItemDrop.ItemData.Load(package, version);
                    record = new RecoveryRecord { PrefabHash = prefabHash, Stack = item.m_stack };
                }
                else
                {
                    record = ReadLegacyRecord(package, version);
                }

                int length = package.GetPos() - start;
                if (length <= 0 || start > bytes.Length - length)
                    throw new InvalidDataException($"Invalid inventory record boundary at index {i}.");

                using (MemoryStream stream = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write((int)version);
                        if (version >= global::Version.Item.Smaller)
                            writer.Write((ushort)1);
                        else
                            writer.Write(1);
                        writer.Write(bytes, start, length);
                        writer.Flush();
                        ZPackage compressed = new ZPackage();
                        compressed.WriteCompressed(new ZPackage(stream.ToArray()));
                        record.Payload = compressed.GetBase64();
                    }
                }
                record.IsReadable = true;
                records.Add(record);
            }

            if (package.GetPos() != bytes.Length)
                throw new InvalidDataException("Inventory payload has unrecognized trailing data.");
            return records;
        }

        private static RecoveryRecord ReadLegacyRecord(ZPackage package, global::Version.Item version)
        {
            // Matches Inventory.LoadOld for formats 100-107. Values are only skipped to
            // locate boundaries; materialization still uses the native Inventory.Load path.
            string name = package.ReadString();
            int stack = package.ReadInt();
            package.ReadSingle();
            package.ReadVector2i();
            package.ReadBool();
            if (version >= global::Version.Item.Quality)
                package.ReadInt();
            if (version >= global::Version.Item.Variant)
                package.ReadInt();
            if (version >= global::Version.Item.CrafterID)
            {
                package.ReadLong();
                package.ReadString();
            }
            if (version >= global::Version.Item.CustomData)
            {
                int count = package.ReadInt();
                if (count < 0 || count > package.Size())
                    throw new InvalidDataException("Invalid legacy item custom-data count.");
                for (int i = 0; i < count; i++)
                {
                    package.ReadString();
                    package.ReadString();
                }
            }
            if (version >= global::Version.Item.WorldLevel)
                package.ReadInt();
            if (version >= global::Version.Item.PickedUp)
                package.ReadBool();
            if (version == global::Version.Item.AbandonedDN || version >= global::Version.Item.ChunksNCheats)
                package.ReadBool();

            return new RecoveryRecord
            {
                PrefabName = name,
                PrefabHash = string.IsNullOrEmpty(name) ? 0 : name.GetStableHashCode(),
                Stack = stack
            };
        }

        internal static ItemDrop.ItemData GetSavedRepresentation(ItemDrop.ItemData item)
        {
            ZPackage package = new ZPackage();
            item.Save(package);
            var (_, stored) = ItemDrop.ItemData.Load(new ZPackage(package.GetArray()), global::Version.c_ItemDataVersion);
            stored.m_dropPrefab = item.m_dropPrefab;
            stored.m_shared = item.m_shared;
            return stored;
        }

        internal static void Load(Inventory inventory, ZPackage package)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (inventory.m_temoraryInventory)
                throw new InvalidOperationException("Recovery requires a materialized inventory, not Inventory(bool). The original payload must be retained.");

            // These packages contain a standalone Inventory.Save record at offset zero.
            // Validate without advancing the package passed to the game's version-aware parser.
            ReadItemCount(new ZPackage(package.GetArray()));
            inventory.Load(package);

            // Other load patches may return transport-only ItemData. Do not let recovery sources
            // be consumed, projected, classified, or equipped until every returned item is usable.
            // Missing prefabs remain absent and are handled by each caller's source-count checks.
            foreach (ItemDrop.ItemData item in inventory.m_inventory)
                if (item?.m_shared == null)
                    throw new InvalidDataException("Recovery returned an unmaterialized item. The original payload must be retained.");
        }
    }
}
