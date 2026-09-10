using System;
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
