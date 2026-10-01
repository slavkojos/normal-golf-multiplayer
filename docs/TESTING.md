# Testing

The v0.4.0 release build, HUD clearance and ZIP checks are recorded in
[release-0.4-validation.md](release-0.4-validation.md).

The mod ships with the tooling that was used to build it: you can run two or three copies of the game on one PC
and drive them from the outside, which is how every feature here was checked.

## Two copies on one PC

1. Put a `steam_appid.txt` containing `3510740` next to `Normal Golf Game.exe`, so the exe starts directly instead
   of relaunching through Steam. (Delete it afterwards if you like; it changes nothing else.)
2. Launch copies with separate save sandboxes. Each profile copies your real saves once and never writes to them:

```powershell
$exe = "D:\SteamLibrary\steamapps\common\Normal Golf Game\Normal\Normal Golf Game.exe"
& $exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -ngmp-profile hostA   -ngmp-name Alice -ngmp-loopback -ngmp-host 7777 -ngmp-autoplay nine
& $exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -ngmp-profile clientB -ngmp-name Bob   -ngmp-join 127.0.0.1:7777        -ngmp-autoplay nine
```

Two things to know: Unity remembers the test window size, so set your resolution back in the game's settings
afterwards; and with `steam_appid.txt` present Steam Cloud will sync the save folder around those launches
(contents stay yours, timestamps change).

### Flags

| Flag | |
|---|---|
| `-ngmp-profile NAME` | Redirect all saves into `ngmp_profiles\NAME`, and use a separate config file |
| `-ngmp-name NAME` | Player name for this copy |
| `-ngmp-host PORT` / `-ngmp-join IP:PORT` | Host or join as soon as the main menu loads |
| `-ngmp-loopback` | Host on 127.0.0.1 only: no firewall prompt |
| `-ngmp-autoplay story\|nine` | Start a game from the main menu automatically |
| `-ngmp-quit SECONDS` | Quit this long after the course loads |

## Driving a running copy

Write lines into `Normal\BepInEx\ngmp_cmd_<profile>.txt`; the game runs them within half a second, deletes the file,
and logs the result to `BepInEx\LogOutput.log` (`.log.1` for the second copy, `.log.2` for the third).

```powershell
"startnine"          > "...\Normal\BepInEx\ngmp_cmd_hostA.txt"
"hole 1 3"           > "...\Normal\BepInEx\ngmp_cmd_hostA.txt"
"screenshot mytest"  > "...\Normal\BepInEx\ngmp_cmd_hostA.txt"   # saved under BepInEx\ngmp_debug
```

| Verb | |
|---|---|
| `state` | Local pose/ball/flags plus every remote player's, and cursor/input state |
| `pin` | The cup of the hole in play, the ball, and the distance |
| `uitree` | Dumps the mod's whole canvas (positions, texts, sprites) to `BepInEx/ngmp_debug/uitree_<profile>.txt` |
| `holes` | The current Front Nine hole, its cup, and every golfer's ball distance to it (the inputs the shot order compares) |
| `scores` | Local and remote Front Nine cards, and the course pars |
| `buf ID` | Snapshot buffer for a player: contents, clock offset, render time |
| `dump` | Full scene report: layers, every camera with culling mask, player/ball hierarchies, shaders, fonts |
| `screenshot NAME` | Writes `BepInEx\ngmp_debug\<profile>_NAME.png` |
| `tp X Y Z [YAW]`, `tpnear ID DIST`, `walkto X Z [SPEED]`, `yaw DEG`, `goto ID` | Move around |
| `golf`, `walk`, `hit POWER`, `ball N`, `retake`, `holed PAR` | Drive gameplay |
| `startnine`, `hole N SCORE` | Front Nine round control |
| `host [PORT]`, `join IP:PORT`, `leave`, `password X`, `chat TEXT` | Session control |
| `menu [off]`, `scoreboard [off]`, `tomenu`, `play story\|nine`, `timescale N`, `bindings`, `quit` | Misc |

### Checking shot order

The deterministic host regression suite runs the actual session and turn-order sources with engine services
stubbed. Build the plugin into the local test folder first, then run:

```powershell
dotnet build NormalGolfMultiplayer.csproj -c Release -p:GameDir="PATH_TO_GAME" -p:PluginOutDir="bin/test-plugin/"
dotnet restore tests/TurnOrder.Tests/TurnOrder.Tests.csproj --configfile tests/TurnOrder.Tests/NuGet.Config
dotnet run --project tests/TurnOrder.Tests -c Release --no-restore
```

It covers opening shots, widely separated drives, consecutive turns, hole-outs, long flights,
cup drift, reordered UDP samples, mid-round score import, round resets, invalid distances and stable ties.
It also checks retakes without another swing, the video phase before ball motion, reliable shot snapshots
and the UI's ball-to-flag calculation.
Wind checks cover protocol round trips, sequence wrap, stale/invalid samples, host authority and session reset.
Shot permission checks cover active/waiting golfers, the ball-settle wait, connecting/offline/solo sessions,
duplicate swings and session generation resets. Live enforcement results are in [turn-lock-validation.md](turn-lock-validation.md).
Live two-instance Unity Explorer results are recorded in [wind-sync-validation.md](wind-sync-validation.md).
Tee-honour checks cover gross-score ranking, ties across successive holes, partial ties,
round restarts, resumed scorecards, delayed score/tee arrivals, hidden balls, slow/loading
golfers, disconnects and returning to farthest-ball order after all tee shots.
Live two-instance checks are recorded in [tee-honours-validation.md](tee-honours-validation.md).
Shot-result checks cover serialization, contact/shape labels, invalid metrics, duplicate and unmatched
results, delivery after turn advancement, physical distance calculations, stroke capture, cup/kill
callback order, resets and session changes. Live results are in [shot-notification-validation.md](shot-notification-validation.md).
Rendering and real network delivery still require the game checks below.

With two copies connected and both on the tee, `state` on both copies shows `turn=1` (the host). Use `hit POWER`
on the host: the turn should move to the joining player's ID (everyone plays from the tee once). Take a shot on
the joining copy too: once both balls rest, `state` should show the turn of whoever is farther from the pin
(`holes` prints the cup in play and every golfer's distance to it), and that golfer keeps the turn while their ball stays farthest. Holing out
drops a golfer from the order; when the group moves to the next hole the tee rotation starts again. Joining mid-
round shows the current turn immediately; disconnecting the active player hands the turn on. The indicator is
enforced: an out-of-turn swing leaves the ball and strokes unchanged. Green ground rings mark the active
golfer and grey rings mark waiting golfers, including the game's local resume-ball particles. Check that
an accepted swing still launches after the next tee turn is announced during its video delay, and that
the local indicators return to their original appearance after leaving multiplayer.

## Cleaning up after testing

Test profiles live in `%USERPROFILE%\AppData\LocalLow\Luke Muscat\Normal Golf Game\ngmp_profiles\`, with per-profile
config files in `BepInEx\config\normalgolf.multiplayer.<profile>.cfg`, screenshots and dumps in `BepInEx\ngmp_debug\`.
All of it is safe to delete.
