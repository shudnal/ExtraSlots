using HarmonyLib;
using System;
using System.Collections.Generic;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots.Compatibility
{
    internal static class ServerCharactersCompat
    {
        internal const string GUID = "org.bepinex.plugins.servercharacters";

        private sealed class ItemSlotMetadata
        {
            internal ItemDrop.ItemData Item;
            internal bool HadPlayerId;
            internal string PlayerId;
            internal bool HadSlotId;
            internal string SlotId;
        }

        private sealed class SaveState
        {
            internal readonly List<ItemSlotMetadata> Items = new List<ItemSlotMetadata>();
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Save))]
        private static class Inventory_Save_PersistSlotProvenance
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, out SaveState __state)
            {
                __state = null;

                if (!HasServerCharactersActive || __instance != PlayerInventory || !Game.instance)
                    return;

                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile == null)
                    return;

                SaveState state = new SaveState();
                __state = state;

                HashSet<ItemDrop.ItemData> captured = new HashSet<ItemDrop.ItemData>();
                string currentPlayerId = profile.GetPlayerID().ToString();

                foreach (Slot slot in slots)
                {
                    ItemDrop.ItemData item = slot.Item;
                    if (item == null || !captured.Add(item))
                        continue;

                    bool hadPlayerId = item.m_customData.TryGetValue(customKeyPlayerID, out string playerId);
                    bool hadSlotId = item.m_customData.TryGetValue(customKeySlotID, out string slotId);
                    state.Items.Add(new ItemSlotMetadata
                    {
                        Item = item,
                        HadPlayerId = hadPlayerId,
                        PlayerId = playerId,
                        HadSlotId = hadSlotId,
                        SlotId = slotId
                    });

                    // ServerCharacters serializes the live player inventory directly after
                    // Inventory.Changed(), bypassing Player.Save where ExtraSlots normally writes
                    // this return address. Persist it only into the serialized snapshot.
                    item.m_customData[customKeyPlayerID] = currentPlayerId;
                    item.m_customData[customKeySlotID] = slot.ID;
                }

            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(SaveState __state, Exception __exception)
            {
                if (__state == null)
                    return __exception;

                foreach (ItemSlotMetadata metadata in __state.Items)
                {
                    if (metadata.Item?.m_customData == null)
                        continue;

                    if (metadata.HadPlayerId)
                        metadata.Item.m_customData[customKeyPlayerID] = metadata.PlayerId;
                    else
                        metadata.Item.m_customData.Remove(customKeyPlayerID);

                    if (metadata.HadSlotId)
                        metadata.Item.m_customData[customKeySlotID] = metadata.SlotId;
                    else
                        metadata.Item.m_customData.Remove(customKeySlotID);
                }

                return __exception;
            }
        }
    }
}
