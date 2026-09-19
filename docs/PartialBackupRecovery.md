# Partial ExtraSlots backup recovery

## Scope

Fixes ExtraSlots issue #61 on top of ExtraSlots 1.2.11
(`02dd63ab60d4bb130677ca9fe48e24287ed73df1`). The game serialization paths were
reviewed at Valheim 1.0.15 in `shudnal/assemblies_combined`
(`d1374bfd9175ac8f733ae483b0a06e5c8b75906e`).

One unavailable item must not prevent other ExtraSlots backup records from being
recovered. This change replaces the whole-backup rollback for
`ExtraSlotsInventoryBackup` with per-record progress. It does not change the
migration policies for backups owned by other inventory mods, server character
policy, or the deferred envelope format.

## Behavior

- Available records are independently matched against existing physical and
  deferred items. Each existing representation satisfies at most one source
  record; identical source records are not collapsed into one item.
- Missing records are imported into deferred storage for normal placement and
  equipment restoration. A source record is removed only after successful
  adoption, or after confirming an existing representation.
- Unavailable prefabs, a failed materialization, changed stack limits, or an
  adoption failure leave only the affected record pending. Earlier successful
  records are not rolled back.
- Successfully processed source records cannot be replayed from the retained
  original snapshot after a recovered item is used, moved away, or destroyed.
- New saves still create fresh backups of the current slots while carrying the
  unprocessed records. One permanently missing mod does not freeze the backup of
  the player's new equipment.
- Character-selection previews do not consume backup records or persist a
  recovery result. Repeated previews cannot exhaust the real recovery source.
- ServerCharacters and authoritative ServerManager sessions still skip automatic
  duplicate-backup recovery. Their existing behavior is not bypassed.

There is no additional user configuration. An existing readable backup is
converted when its ordinary recovery path runs. No game downgrade or reinstall
of an unavailable item mod is needed to recover the other records.

## Storage and commit boundary

The original `ExtraSlotsBackup` JSON fields remain. Two optional fields are added:

- `recoveryVersion`: 0 for the old format, 1 for this format.
- `pendingInventories`: compressed Base64 inventory packages. Normally each has
  exactly one original serialized item. A package whose boundaries cannot be
  read safely is retained whole, without guessing where its next record starts.

Item bytes retain their original inventory-format version. Current-format record
boundaries use the game's `ItemData.Load` reader. Legacy formats 100-107 use a
skip reader matching `Inventory.LoadOld`; actual materialization still calls
`Inventory.Load` on a detached, non-temporary inventory. Records are not rebuilt
from default prefab values. A materialized record must preserve its saved stack
and prefab hash before it can be consumed.

During real recovery, the old snapshot is first represented entirely by pending
records and its snapshot field becomes an empty valid inventory package. For
each record, the updated source JSON is prepared before adoption. Deferred data
and source consumption are committed synchronously to the same player's custom
data. Failure restores only the current record's prior source and deferred data.
Deferred restoration is suspended during the pass, and the normal backup-save
prefix does not replace a source while it is being processed. Normal character
saving persists this state; this is not an independent disk write or an additional
crash/durability guarantee.

The outer envelope is preserved unchanged if it is unreadable or uses an
unsupported recovery version. Corruption inside an unframed inventory stream may
prevent recovery of that stream, because later byte boundaries cannot be trusted;
it is different from a missing prefab, whose record can still be parsed normally.
Other independent pending packages remain recoverable.

Do not downgrade ExtraSlots while pending records exist without keeping a copy
of the character: older versions do not understand `pendingInventories`. Do not
remove the backup custom-data key to suppress warnings; it contains the remaining
unavailable records.

## Manual verification

No mod build, game launch, or runtime tests were performed for this change. Use a
disposable character and copied backup data when running these checks.

| Scenario | Expected result |
| --- | --- |
| A readable 19-record backup with one absent prefab in the middle | The other 18 records are recovered or recognized as present; only the unavailable record remains pending. |
| Use or discard a recovered item, save, and reload while the prefab is still missing | The old consumed source record does not give the item again. |
| Obtain different equipment and save while a record remains pending | The new equipment appears in the fresh snapshot and the pending record is retained. |
| Restore the previously missing mod and reload | Its remaining record can be recovered once, with its original item data. |
| Two identical source records with only one matching existing item | The existing instance satisfies only one record; the other is recovered separately. |
| Every prefab is missing | All records remain pending; new normal slot backups still work. |
| One prefab throws or one deferred adoption fails after other records succeed | Only that record is retained; earlier successes stay committed and later records are still attempted. |
| Not enough inventory space for a recovered item | The consumed backup record is represented in deferred storage, not dropped or duplicated. |
| Repeated character-selection previews before entering a world | No persistent source consumption or deferred mutation by backup recovery. |
| Old-format 106/107, compact 108, and current 109 backups | Readable records use their original version and can be recovered independently. |
| A fully unreadable pending package beside a readable fresh snapshot | The raw unreadable package is retained; readable snapshot records are processed. |
| Backup JSON with an unknown recovery version | Its original value is retained instead of being overwritten by the next save. |
| ServerCharacters or authoritative ServerManager is active | Existing backup recovery exclusion still applies. |

The underlying reason for the original inventory loss, and unrelated enforcement
performed by other mods, are outside this fix. Recovery is possible only while a
corresponding source record still exists.
