# Changelog

## v0.4.0

* Lowered the turn panel and shot notifications to leave the game's wind indicator visible.
* This release includes the refreshed multiplayer menu and colour selection indicator, detailed golfer models, host-synchronised wind, enforced turns and active/waiting ball rings.
* Shot summaries show contact and shape, stroke number, distance travelled and distance to the cup. Turn order uses the physical ball and cup; subsequent holes use stroke-play honours, with ties retaining the previous tee order.
* Updated the installation guide to describe the current turn rules. **Protocol v8: everyone upgrading from the previous public release, v0.3.2, must update together.**

## v0.3.10

* Front Nine tee order now follows stroke-play honours: lowest gross score on the previous hole first, then next-lowest. Ties retain the previous tee order, including ties across successive holes. Penalties are included through the game's recorded score.
* Waits for previous-hole scorecards and for the honour holder's ball to reach the new tee; an early arrival cannot steal the first shot. Resumed rounds rebuild honours from score history, restarting a round restores the agreed first-tee order, and disconnecting a golfer releases their place.
* After all tee shots, the farthest resting ball still plays next. Waiting messages now cover both tee arrivals and balls settling.
* Protocol remains v8, compatible with v0.3.9. The host must update to apply the new honours rule.

## v0.3.9

* Other golfers see a ten-second notification when a shot finishes: player name, stroke number, contact (thin, fat, topped, ground hit or perfect), shape (straight, fade, draw, hook or slice), ground distance travelled including roll, and distance from the physical ball to the active cup.
* Hole-outs show "In the hole!". Shot IDs keep completion and hole callbacks from producing duplicate results, and results remain valid after the tee turn advances. Resets and session changes discard unfinished feedback.
* Three-line shot cards fit the complete summary; notifications of different heights stack without overlap.
* **Protocol v8 — all players must update to v0.3.9.**

## v0.3.8

* Turn order is enforced: waiting golfers cannot start a shot or launch a ball. Accepted swings retain permission through the video delay; another swing is blocked until the current one completes.
* The host rejects out-of-turn shot packets. A reliable shot arriving behind its UDP preparation sample retains its golfer's turn until processed.
* Resting balls have green ground rings for the active golfer and grey rings while waiting. The local game's resume-ball effect and remote ball labels also follow turn status; ordinary indicators return when leaving multiplayer.
* **Protocol v7 — all players must update to v0.3.8.** Older clients used advisory turns and cannot enforce this rule.

## v0.3.7

* Rebuilt the remote golfer with smooth shaped head/body meshes, more natural proportions, eyelids, pupils, nose and lip detail, ears and a stitched six-panel cap.
* Added fitted polo clothing, folded collar, buttons, belt hardware, trouser seams, layered golf shoes and laces, separate fingers, glove closure and detailed club grip/face.
* Arms now have elbow joints and solve the two-segment reach to the grip during address and swings. Continuous skinned arm and trouser meshes bend across elbows and knees without separate-part gaps. The club reaches the ball from the adjusted stance.
* Fabric weave and separate skin, leather and metal finishes improve lighting. Details are merged by bone/material and their meshes are shared between players.
* Protocol remains v6, compatible with v0.3.6.

## v0.3.6

* Wind now comes from the host: direction, strength, ball force multiplier, forced-wind lock and calm timer are broadcast at 20 Hz, with an immediate snapshot for late joiners.
* Clients apply shared wind before ball physics and wind visuals, overriding their independent random cycle and local challenge wind. Leaving restores ordinary local wind behavior.
* **Protocol v6 — all players must update to v0.3.6.**

## v0.3.5

* Colour selection outlines now move immediately when a swatch is clicked and follow configured colour changes while the menu is open, without rebuilding text fields.
* Spaced the Play cards' headings, subtitles and controls so input backgrounds no longer cover the instructional text. The taller cards fit within an expanded menu and stay clear of its footer.
* Protocol remains v5, compatible with v0.3.4.

## v0.3.4

* Verified cup selection with Unity Explorer. Front Nine now reads the active hole from the game's round save directly; an uninitialized mod scorecard can no longer substitute a nearby minigolf cup. Resumed rounds are imported after the game's round becomes available.
* Shots carry a reliable ball/cup snapshot. Turn selection waits through the swing video and ball flight until the game's completion callback, and recomputes when late tee arrivals or retakes change the order.
* HUD and Room distances measure each ball against the host's shared flag. The Room list no longer labels golfer-to-golfer distance as if it were ball-to-flag distance.
* **Protocol v5 — all players need v0.3.4.** The shared flag and shot lifecycle are included in network state.

## v0.3.3

* Fixed turn order losing golfers after widely separated drives, reopening tee shots when a cup pick drifts, and choosing the next golfer before long drives finish rolling. Delayed UDP samples no longer replace newer ball positions; round updates restore mid-round shot counts and reset opening shots for a new hole or round.
* Fixed the Scores tab spilling past the menu, kept score labels at a readable line height, fitted the canvas to narrow viewports, and made the HUD and Room tab agree while waiting for the next shot.

* Rebuilt the whole interface as a proper uGUI canvas using the game's own Quantico font and rounded panels: a turn card in the top-left, the session chip at the bottom-right corner (the bank-bar slot), a tabbed multiplayer menu (Play / Room / Scores) instead of one long window, a crisper Front Nine table, and toasts that slide in. Everything stays sharp at any resolution, labels clip with an ellipsis instead of overflowing, and the menu is fully clickable — the mod no longer suspends the pointer actions the game's own UI event system listens through while its menus are open.
* Fixed labels rendering as blank text: TextMeshPro's ellipsis overflow drops a whole line that does not fit vertically, so every label laid out shorter than one line of Quantico (~1.45em) drew nothing — the menu's "Multiplayer" title, the turn card's title and the Room tab's "Your turn" among them. Label rects are now always at least one line tall.
* The Play and Room tabs no longer rebuild their cards every time a ping value changes (which recreated buttons and input fields under the cursor and froze the golfer statuses): statuses and pings now update in place, and the Room rows are sized so a full eight-golfer session still fits inside the window.
* Chat history now sits on a faded panel while closed, so it is readable over the game's own clubs panel instead of drawn straight on top of it.
* Reworked the shot order into proper golf order: everyone plays from the tee once before any ball in play takes priority, then the farthest resting ball plays next (including consecutive turns while you stay farthest). The host measures each golfer's ball against the cup of the hole actually being played — resolved from the round's current hole, since the game's own distance overlay points at whichever challenge cup is nearest the camera (a minigolf target as readily as the pin) — and reopens the tee rotation when the group moves on. A gentle toast now warns when you swing out of turn.
* **Protocol v4 — everyone in a session needs v0.3.3.** Each state update now carries the current cup, so v0.3.2 players are refused with a message telling them to update.

## v0.3.2

* Reshaped remote golfers with tapered clothing, more natural face and cap proportions, and detailed shoes and hands.
* Replaced the crouch squash with bent knees and level feet.
* Kept protocol v3; players on v0.3.1 can still join the same session.

## v0.3.1

* Sized the F8 window to the game viewport and gave its scroll area the available height, including at smaller resolutions.
* Made the chat box fit narrow screens. Added a Chat button in connected sessions and clarified that chat becomes available after hosting or joining.
* Protocol remains v3, so v0.3.1 can play with v0.3.0.

## v0.3.0

* Source-only release: an installable Windows package could not be built in this workspace without the game assemblies.
* Redesigned the multiplayer menu with clearer sections, player cards, compact connection details and a scrollable layout for smaller screens.
* Added a visible shot order in the HUD and player list. The host keeps the current turn in sync, including for players who join mid-session, and advances it after each shot or when the active player leaves. Shot order is a guide; it does not block anyone from playing.
* **Protocol v3 — everyone in a session needs v0.3.0.** Older clients are refused with a message telling them to update.

## v0.2.0

* **Front Nine scoreboard.** Everyone's card side by side in the F8 menu and on a new **F9** overlay: hole, par,
  a row per player, totals and score against par. The hole a player is on shows their strokes so far.
  Scores come from the game's own counters, so water and retake penalties are included and skipped holes score par.
  A row resets when that player presses the red button, and stops when they finish the ninth hole.
* Cards are sent to players who join mid-round, so latecomers see the round so far.
* Avatar colours are more vivid, and name tags are larger and easier to read.
* A ball's name marker is hidden while the ball sits at its owner's feet.
* Harmony patches are now applied one by one, so a future game update disables a single feature instead of the whole mod.
* **Protocol v2 — everyone in a session needs v0.2.0.** Older clients are refused with a message telling them to update.

## v0.1.0

First release.

* See other players on the course: walking, crouching, looking around, with name tags in each player's colour.
* Golf stances and swings, animated in time with the real shot, plus the club-hit sound placed in the world.
* Every player's ball: flight, trail in their colour, where it lands, and the ball skin they picked.
* Hole-out announcements.
* Direct IP:port hosting and joining, with optional password, player list, ping, distances and chat.
* "Go to" button to teleport next to another player while walking.
