# Native radial Quick bars

The Hotbar page of the game's radial menu gains a Quick bars group at 180 degrees. The original eight entries stay in their original left-to-right order. Back stays at the top. This uses the game's `RadialBase`, `IRadialConfig`, `GroupElement`, `ItemElement` and `EmptyElement`; the deprecated ValheimRadial plugin is not needed.

## Availability and layout

Availability comes from `Slot.IsActive`, including progression, not from HUD hotbar components or their Enabled/Hide hotbar settings. Empty food and ammo cells retain their positions. Quick slots are filtered for active, occupied cells. All layouts include up to six quick slots. With both side groups active, occupied quick slots are centered below the side groups. The first occupied slot is on the lower left; ascending slot order runs counterclockwise across the bottom to the lower right. With only one or no side group, quick slots keep their clockwise order starting on the right.

| Active slot groups | Left | Right | Bottom |
| --- | --- | --- | --- |
| Food and ammo, with or without quick slots | Three food positions | Three ammo positions | Up to six quick items, centered, counterclockwise |
| Food, with or without quick slots | Three food positions | Up to six quick items, clockwise | Part of the clockwise quick group |
| Ammo, with or without quick slots | Three ammo positions | Up to six quick items, clockwise | Part of the clockwise quick group |
| Quick only | None | Up to six quick items, clockwise | Part of the clockwise quick group |
| None | No Quick bars entry | | |

A page can be empty when its quick slots are active but contain no items. It still has Back. Food/ammo slots that have not unlocked yet are inert placeholders when another slot in their group is active. No inventory item is cloned, moved or assigned a temporary coordinate.

## Geometry and input

The parent page has twelve native positions, with non-interactive gaps on either side of the bottom entry. Its other sectors keep native layout and input handling. Construction is intercepted only for an eight-entry Hotbar page; an incompatible replacement made by another mod is left untouched.

The child page contains at most thirteen real entries, including Back. Its layer capacity is separate from the parent's twelve-position grid. Thirteen visual sectors use the shader's 14-segment width (about 25.71 degrees), leaving four quarter-sector gaps (about 6.43 degrees each) in the full layout. These are gaps, not additional selectable cells or an extra radial layer. The radius and icon scale remain unchanged.

An odd number of bottom items has a center item at 180 degrees. With an even number, including six, the two middle items straddle 180 degrees. Empty quick slots are omitted before centering; a lone item in quick slot 6 is therefore still shown at the bottom.

Only this child config uses explicit angle-based cursor selection. Drawing, hit areas and cursor targets use the same cell angles, including centering even-sized quick groups. Native confirmation, hold/release, flick, hover, Back, item tooltips and item use still run through the game's radial controller. Item use closes the page, as on the game's Hotbar page.

The page snapshots slot references and their contents on construction. While open, it requests a native refresh when those contents or activation states change. Interactions recheck the player, inventory membership and the original slot resident before acting. This also protects a retained LastUsed item from acting on an old character or a detached item. Existing item data, equipped state, stack sizes and gameplay operations are not rewritten.

## Localization

`exsl_radial_quick_bars` is stored in the localization CSV and all 36 language resources. The Abenaki resource keeps the same English fallback used for its existing Quick/Quick slot terminology; a verified Abenaki translation is still needed.
