# Release v0.4.0 validation

The turn panel and its shot-notification column start 160 reference pixels below
the top of the screen, increased from 56. The bottom-right session chip retains
its existing position.

Unity Explorer measured the actual RectTransform corners in Unity 6000.3.11f1
using the isolated `releaseHud` save profile. The native wind canvas uses world
space, so its corners were projected through the game's main UI camera before
comparison with the mod's screen overlay.

| Resolution | Wind indicator bottom, from screen top | Turn panel top | Clearance |
|---|---:|---:|---:|
| 960 × 540 | 56.47 px | 80 px | 23.53 px |
| 1920 × 1080 | 112.94 px | 160 px | 47.06 px |

Both checks passed. Notifications sit below the turn card and share its lowered
root, leaving the same wind area clear even when the turn card is hidden.

The Release build passed with zero warnings and errors. The source-linked test
executable passed all 114 turn-order, shot-permission, wind and shot-result
regression checks. Earlier live multiplayer checks are recorded in the turn,
wind, golfer-model, shot-notification and tee-honours validation documents.

The installation ZIP was checked for the BepInEx loader/core, the v0.4.0 plugin,
LiteNetLib and player instructions. Its plugin SHA256 must match the release
build. Archive paths use forward slashes and include `.doorstop_version`;
Unity Explorer, test scripts, personal saves, config files and logs are excluded.
