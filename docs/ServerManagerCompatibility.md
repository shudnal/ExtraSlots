# ServerManager compatibility

## Scope

This integration targets ServerManager 1.0.9 from
`sighsorry1029/ServerManager` at commit
`f10303721fad4c85a3ec11c823e9f015f26ade33`. The ExtraSlots base is 1.2.9,
commit `563e0e29af35543d8bb9f2271bc71e1e32342e25`.

It covers remote clients and the local player in a world opened with **Start
Server**. Both authoritative characters (`loadServerCharacterOnJoin: true`) and
backup-only sessions (`false`) are recognized from the active session, not just
from the presence of the plugin. Ordinary single-player and character previews
retain their existing behavior.

ServerCharacters support remains available separately. Do not run
ServerCharacters and ServerManager together; both manage character persistence.

## What is fixed

### Slot positions in inventory-only saves

ServerManager captures frequent inventory-only updates with
`ValheimPlayerProfileCodec.CaptureInventoryToBytes`. This calls `Inventory.Save`
without the `Player.Save` prefix that normally stamps ExtraSlots slot addresses.
The server inserts those bytes into its previous full character profile.

The shared inventory-save hook now also recognizes ServerManager's active
managed profile. It temporarily stamps `ExtraSlotsEquippedBy` and
`ExtraSlotsEquippedSlot` on actual slot residents before serialization. A Harmony
finalizer restores the exact prior values, including absent keys and exception
paths. Nested saves retain their own restoration state. No extra
`Inventory.Changed()` notification is emitted.

The hook reads the live item list rather than cached slot residents, so replacing
that list with a character snapshot cannot stamp detached old items. It excludes
ordinary containers, temporary serialization inventories and regular cells.
Quick, food, ammo, utility, standard equipment and custom slots use the same path.

### Stale duplicate backups during authoritative loading

`ExtraSlotsInventoryBackup` is a duplicate snapshot. An inventory-only server
update can remove an item without updating that older backup. Automatic recovery
from that backup is therefore disabled while loading an authoritative
ServerManager profile, just as it is for ServerCharacters.

The backup payload is not deleted. Backup-only ServerManager sessions retain
ExtraSlots' ordinary local recovery policy. This guard does not disable or erase
`ExtraSlotsDeferredInventory`, which is a different ownership store.

### Physical inventory and deferred ownership

Deferred items exist in `Player.m_customData["ExtraSlotsDeferredInventory"]`, not
in the physical inventory. ServerManager's inventory-only byte replacement does
not update that player field. Combining a newer physical inventory with an older
deferred queue can lose a newly deferred item or restore an already recovered
item twice.

Once a nonempty deferred payload is observed, ExtraSlots switches that managed
session to full-profile updates for inventory changes:

- Remote clients promote the native inventory save offer to a full profile
  captured through `CaptureProfileToBytes`. The payload and native save reason
  change together, before the existing queue assigns a capture ID or revision.
- The listen host skips the partial capture and requests the existing
  `ScheduleFull` path. Its normal full capture, acceptance and retry logic remain
  responsible for the result.

Observation also occurs before `DeferredInventory.EnsureLoaded`, so a queue
restored and cleared immediately after joining is not missed. The full-profile
requirement stays latched for the rest of that session, including respawns and
an empty queue. An empty local queue is not proof that the server has already
accepted its removal. A new connection gets a new decision; weak session keys do
not keep disconnected sessions alive.

Ordinary sessions that never encounter deferred storage retain the native fast
inventory path. Sessions using deferred storage trade update frequency for
consistent ownership: remote full offers obey ServerManager's full-save pacing
(one routine full start per 11-second window in the reviewed version); the listen
host obeys its existing full-save safety interval (30 seconds). These intervals
are not crash-loss guarantees, and queued/in-flight saves can add delay.

## Preserved boundaries

The integration does not change ServerManager's wire format, session identity,
revision sequence, acknowledgements, validation, forbidden-item rules, payload
limits, disk checkpoints or disconnect decisions. It does not write character
files directly, fabricate acceptance, restore an unacknowledged local snapshot
or add a new network message.

Full profile capture still runs the native `Player.Save` path and its existing
save/load guards. Any capture exception reaches ServerManager's existing failure
handling. Already inconsistent character files are not automatically repaired:
there is no reliable basis to delete or manufacture items to guess what happened.
A client or server crash can still roll back changes that never reached the
relevant accepted or durable save boundary.

## Initialization and installation

Bindings are resolved from the installed plugin assembly at `ZNet.Awake`, after
BepInEx plugin initialization and before character loading. No hard dependency,
ServerManager DLL reference or additional configuration is introduced. Required
types and method signatures are checked before installing the two dynamic
client/host hooks. A failed initialization rolls back those hooks and emits a
warning; such a run must not be treated as fully compatible.

With ExtraSlots **General / Logging enabled**, the initialization message is:

```text
ServerManager 1.0.9 compatibility enabled: slot snapshots, authoritative backup guard and deferred inventory saves.
```

When deferred storage is first observed in a managed session:

```text
ServerManager deferred inventory detected: using full character snapshots for the rest of this session to keep physical and deferred items together.
```

Install the updated ExtraSlots on affected clients and on a listen host. Keep the
server's installed version consistent with its modpack policy. ServerManager also
checks approved plugin DLL hashes: update any ExtraSlots reference copy in the
server's `required` or `optional` folder after rebuilding. Reference copies are
not installed plugins. No modified ServerManager build is required.

## Manual verification checklist

No mod build or runtime tests were performed for this change. Use a disposable
character and world, with backups, for these checks. Do not toggle inventory mods
on an irreplaceable character.

| Scenario | Expected result |
| --- | --- |
| Remote character with items in quick, food, ammo, equipment and custom slots; allow an inventory update, then reconnect | The accepted snapshot preserves slot assignments, including unequipped slot residents. |
| Remove all slot items after a full save; allow the later inventory update, then reconnect | An older duplicate ExtraSlots backup does not resurrect those removed items. |
| Defer an item by removing its available destination with no free space; allow a full update, then reconnect | The item exists once in deferred storage, not zero times or both there and in a physical cell. |
| Restore the last deferred item; allow a full update, then reconnect | It exists once in physical inventory; an older deferred entry does not return. |
| Join with a saved deferred queue that can be restored immediately | Full-profile mode is selected before the queue is cleared. |
| Die and respawn after deferred storage was used in the session | The session remains in full-profile mode; normal tombstone ownership rules still apply. |
| Repeat slot and deferred scenarios as the Start Server host | Host updates follow its native full-save scheduler; no fabricated remote session is used. |
| ServerManager backup-only mode | Local backup recovery remains available; deferred ownership still uses consistent full snapshots. |
| Single-player with ServerManager installed but inactive | Normal ExtraSlots save, backup and deferred behavior is unchanged. |
| ServerCharacters without ServerManager | The existing temporary slot-provenance fix still works. |
| Inventory serialization throws or is nested | Preexisting live item metadata is restored; no additional Changed notifications are generated. |
| Server rejects a save, or the transport closes before acceptance | Its existing rejection/rollback policy applies; compatibility must not bypass it or synthesize success. |

For crash checks, distinguish the latest accepted RAM snapshot from the last
world-save disk checkpoint. First verify consistency after an accepted full
update, then separately inspect the expected rollback window before acceptance.

## Source locations reviewed

ServerManager: `Character/ValheimPlayerProfileCodec.cs`,
`Character/ClientCharacterSavePipeline.cs`,
`Networking/ServerManagerRuntime.cs`,
`Networking/LocalHostCharacterRuntime.cs`, `RuntimePatches.cs` and `README.md`.

ExtraSlots: `Compatibility/ServerCharactersCompat.cs`, `InventoryBackup.cs`,
`DeferredInventory.cs`, `Slots.cs`, and the existing player-load reconciliation
and save paths.
