# Native radial Quick bars

The Hotbar page of the game's radial menu gains a Quick bars group at 180 degrees. The original eight entries stay in their original left-to-right order. Back stays at the top. This uses the game's `RadialBase`, `IRadialConfig`, `GroupElement`, `ItemElement` and `EmptyElement`; the deprecated ValheimRadial plugin is not needed.

## Availability and layout

Availability comes from `Slot.IsActive`, including progression, not from HUD hotbar components or their Enabled/Hide hotbar settings. Empty food and ammo cells retain their positions. Quick slots are filtered for active, occupied cells. With both side groups active, the first five quick slots are eligible (filtering does not pull slot 6 into a vacant position). Other layouts permit all six.

| Active slot groups | Left | Right | Bottom |
| --- | --- | --- | --- |
| Food and ammo, with or without quick slots | Three food positions | Three ammo positions | Up to five quick items, centered |
| Food, with or without quick slots | Three food positions | Up to six quick items, clockwise | Part of the clockwise quick group |
| Ammo, with or without quick slots | Three ammo positions | Up to six quick items, clockwise | Part of the clockwise quick group |
| Quick only | None | Up to six quick items, clockwise | Part of the clockwise quick group |
| None | No Quick bars entry | | |

A page can be empty when its quick slots are active but contain no items. It still has Back. Food/ammo slots that have not unlocked yet are inert placeholders when another slot in their group is active. No inventory item is cloned, moved or assigned a temporary coordinate.

## Geometry and input

The parent page has twelve native positions, with non-interactive gaps on either side of the bottom entry. Its other sectors keep native layout and input handling. Construction is intercepted only for an eight-entry Hotbar page; an incompatible replacement made by another mod is left untouched.

The child page contains at most twelve real entries, including Back. Small group gaps need angular space too: twelve visual sectors use the shader's 13-segment width (about 27.69 degrees), leaving four quarter-sector gaps in the full layout. These are gaps, not additional selectable cells or an extra radial layer. This slightly narrows the normal 30-degree sectors while retaining the requested five-item lower limit.

Only this child config uses explicit angle-based cursor selection. Drawing, hit areas and cursor targets use the same cell angles, including centering even-sized quick groups. Native confirmation, hold/release, flick, hover, Back, item tooltips and item use still run through the game's radial controller. Item use closes the page, as on the game's Hotbar page.

The page snapshots slot references and their contents on construction. While open, it requests a native refresh when those contents or activation states change. Interactions recheck the player, inventory membership and the original slot resident before acting. This also protects a retained LastUsed item from acting on an old character or a detached item. Existing item data, equipped state, stack sizes and gameplay operations are not rewritten.

## Localization

`exsl_radial_quick_bars` is stored in the localization CSV and all 36 language resources. The Abenaki resource keeps the same English fallback used for its existing Quick/Quick slot terminology; a verified Abenaki translation is still needed.
