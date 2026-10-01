# Architecture

How the mod plugs into Normal Golf Game, and why each piece looks the way it does.

Target: **Normal Golf Game** (Steam app `3510740`), game build *"Build 9:16AM September 21"* (Steam build `25426758`),
Unity `6000.3.11f1`, **Mono** scripting backend. BepInEx 5.4.23.5, LiteNetLib 2.1.4, plugin targets netstandard2.1.

## Layout

```
src/
  Plugin.cs                  BepInEx entry; creates one persistent GameObject holding the components below
  ModConfig.cs               BepInEx config (name, colour, ports, keys, visuals)
  Net/Protocol.cs            Wire format: message ids, PlayerInfo, PlayerState (~45 bytes), ScoreCard, events
  Net/NetSession.cs          LiteNetLib listen-server: handshake, host relay, disconnect reasons
  Net/TeeHonours.cs          Previous-hole gross-score ranking with stable ties from the previous tee order
  Game/LocalPlayer.cs        Reads the local pose and ball out of the game each tick; "Go to" teleport
  Game/ScoreTracker.cs       Mirrors the local Front Nine round (the game's LMUGC system) and broadcasts it
  Game/GamePatches.cs        Harmony hooks (see below)
  Game/ShotFeedback.cs       Captures physical launch/finish positions, contact, shape and stroke for shot notifications
  Remote/SnapshotBuffer.cs   Per-player interpolation buffer with clock-offset estimation
  Remote/RemotePlayerView.cs Golfer avatar, animation, remote ball + trail + labels
  Remote/GolferModel.cs      Detailed golfer geometry, clothing, face, hands, equipment and joint rig
  Remote/BallTurnIndicators.cs Green/grey ground rings and native resume-ball marker tint
  Remote/RemoteWorld.cs      Owns remote players across scene loads; per-camera label billboarding
  Remote/Visuals.cs          Materials/meshes/fonts/sounds borrowed from the game's own assets
  UI/MultiplayerUI.cs        uGUI canvas: menu (F8), scoreboard (F9), HUD, toasts, chat (T)
  UI/UiKit.cs                Rounded 9-slice sprites, the game's font, shared uGUI factories
  DevTools.cs                Test flags and a command file for driving running instances
  SaveSandbox.cs             -ngmp-profile save redirection for side-by-side test instances
  Diagnostics.cs             Scene/camera/layer dump, used while reverse engineering
```

## Networking

A **listen server**: the host is player 1 and relays every client's messages to the other clients, so clients only
ever talk to the host. Transport is UDP via LiteNetLib.

* **Handshake inside the connection request.** The client puts magic, protocol version, password and its player info
  into the LiteNetLib connect request, so the host can accept or reject with a readable reason before a peer exists.
* **State**: `Msg.State`, unreliable, 20 Hz, ~57 bytes — position, yaw, pitch, flags (in-world, golfing, crouched,
  ball visible/trail/moving, Play Nine, hole known), club, ball position, a ball "epoch" and the current cup. Each carries a sequence number and
  the sender's session clock.
* **Events**: `Shot`, `ShotResult`, `Holed`, `Chat`, `PlayerInfo`, `Score` and host-authored `Turn` are reliable-ordered. The welcome packet includes the current turn so late joiners see the same shot order. The host tracks each golfer's ball and the cup of the hole they are playing: everyone plays from the tee once, then the farthest resting ball plays, and a holed-out golfer drops out until the next hole. The cup is resolved from the round's current hole (the course names its cup magnets "hole1".."hole9"), falling back to the nearest cup in free play, so the distance comparison is against the pin actually being played rather than whatever the game's distance overlay happens to point at. Protocol v7 enforces the turn before swing detection, shot startup and physical launch. The host rejects out-of-turn shot packets; accepted shots keep launch permission through the video delay.
* **Interpolation**: receivers render each player ~100 ms in the past (`SnapshotBuffer`), estimating the clock offset
  from the fastest recent packets, so jitter and a lost packet don't produce stutter. Teleports would otherwise be
  interpolated as a slide across the map, so the ball carries an **epoch** that bumps on every reset, and large
  jumps snap instead of lerping.
* Bump `Protocol.Version` whenever the wire format changes; mismatched peers are refused with a clear message.

Protocol v8 adds an ID to `Shot` and a reliable `ShotResult` matched to each accepted shot.
Since v0.3.10 the host reconstructs Front Nine tee honours by stable sorting recorded gross
scores in hole order. The immediately previous hole decides the new order, and ties retain
the previous tee order. A score of zero is unknown and delays the next tee. Connected
round golfers absent from that tee retain a placeholder in the opening order, so slow
arrivals cannot lose their honour. The ball must be visible and settled before its tee turn
is awarded. After everyone tees off, ordinary farthest-ball ordering resumes.
The sender captures the game's `ShotType`, `HitManager.GetSpinType()` and `m_currentShot + 1`
at physical launch. Shot distance is horizontal start-to-finish displacement including roll;
remaining distance is the physical ball-to-active-cup distance. Completion is captured before
challenge checks can advance the hole. Cup feedback reports zero remaining distance; killed
balls defer one frame so cup feedback wins. The host relays one valid result per accepted shot,
even after turn changes, and receivers show a three-line toast for ten seconds.

Protocol v5 adds a reliable pre-shot `PlayerState` to `Shot`, a shot-in-progress bit to state,
and the host's shared flag to `Turn` and `Welcome`. The active Front Nine cup is resolved directly
from `RunSave.lmugcScore` while `isInLMUGC` is true. There is no nearest-cup fallback in a round.
Distance uses the physical rigidbody position, rather than an interpolated ball Transform or golfer position.
The host re-evaluates eligible balls while no shot is pending, so retakes and late tee arrivals cannot leave
a stale turn selected. `HitSequence` starts the pending lifecycle; `CompleteSequence` or hole-out ends it.
HUD and Room values use the last network ball state and the shared flag, matching the host's comparison.

## Hooks into the game

Protocol v6 adds host-authored `Wind`: a sequence number, horizontal wind vector, ball force
multiplier, forced-wind lock and calm seconds. It is sent at 20 Hz after the host's `WindPanel.LateUpdate`
resolves its challenge/item overrides, with a reliable snapshot immediately after a new client's welcome.
Clients reject invalid or older samples and never relay their wind. `WindSync` applies the latest sample
before `WindPanel.Update`, replaces client `LateUpdate`, and applies it immediately before `Ball.FixedUpdate`.
This covers both the random coroutine and local challenge overrides. Session shutdown clears the shared
sample; normal local wind resumes and the original ball force multiplier is restored.

Patches are applied **individually** in `GamePatches.Apply`, so a game update that renames one method disables that
one feature with a warning in the log instead of stopping the whole mod from loading.

| Target | Why |
|---|---|
| `HitManager.ResetBall` (postfix) | Bumps the ball epoch: a reset teleports the ball, and receivers must snap |
| `HitSequeneceManager.HitSequence` (prefix) | The coroutine every real swing starts — broadcasts the shot |
| `HitSequeneceManager.ShowBallInHoleFeedback` (prefix) | Every hole-out path reports through it |
| `LMUGC.StartChallenge` (postfix) | The red button at the first tee: a new Front Nine round |
| `LMUGC.CompleteHole` (prefix) | Records each hole score, including ticket-skipped holes |
| `SaveManager.AddScoreToCard` (postfix) | End of a Play Nine round, while the final card is still intact |
| `ClubSwing.Update`, `ClubSwingFPS.Update`, `HitSequeneceManager.RotateCamera` (prefix) | Skipped while our UI owns the mouse |

## Things the game does that shaped the design

* **There is no player model.** The hands and the golfer are FMV video clips rendered to textures, so a remote player
  had to be built from scratch: `RemotePlayerView` assembles a golfer from shared shaped meshes and primitives, then
  clones the ball's URP/Lit material (which guarantees the shader variant exists in the build).
* **Pose comes from two different places.** Walking uses the ECM2 `Character` (`MoveAndHitController.m_fpsCharacter`).
  Golfing disables that object entirely and positions `m_golferHolder` instead, with the ball at the holder's local
  +X (0.79), so a golfing avatar stands at the holder and faces holder yaw + 90°.
* **Several cameras render the world each frame.** Avatars live on the `IgnoreForRTEXCams` layer, which the
  first-person camera, the golfer (FMV) camera and the ball-chase camera all draw, but the small swing/spin/club
  panel cameras do not. Name tags are re-oriented per camera in `RenderPipelineManager.beginCameraRendering`.
* **Scoring is the game's own.** A hole is `Ball.m_currentShot + 1`, and a retake after water does `m_currentShot++`,
  so penalties are already included; nothing is recomputed. The catch: `LMUGC.CompleteNormalGolfRound` **clears**
  `RunSave.lmugcScore` immediately after the ninth hole, so scores are captured as they are recorded, and the final
  card is taken at `SaveManager.AddScoreToCard` while it still exists. A round already in progress is imported from
  the save on load, so the mod can be enabled mid-round.
* **Input is one `PlayerInput`**, deactivated while the menu or chat is open. The swing panel additionally reads the
  *legacy* mouse API, which that can't block, so `ClubSwing.Update` is skipped during that time — otherwise clicking
  a button in the menu could start a swing.
* **Saves**: `SaveSandbox` can redirect every `SaveManager` path into a profile folder. That exists purely so two
  copies can run on one PC during testing without touching real saves.

## Ideas for later

* Spectate: follow another player's ball with your chase camera
* Show players on the in-game map
* Batch every player's snapshot into one packet per tick for large sessions
* Shared round mode: one card, alternating turns
