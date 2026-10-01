# Strict turns and ball markers — v0.3.8

Validated on 2026-09-30 using two isolated save profiles (`turnHost`, `turnClient`),
loopback UDP port 17882, Unity 6000.3.11f1 and Unity Explorer.

With player 1 active, player 2's actual `HitSequence` returned an empty enumerator,
direct `HitManager.HitBall` was blocked, and swing detection was disabled. The rigidbody
position, kinematic state, ball-hit flag, stroke count and save shot count were unchanged.
A deliberately injected player-2 `Shot` packet was rejected by the host, with active player 1
recorded in the rejection log.

The host's accepted swing advanced the tee turn to player 2 before its video delay completed.
It still launched successfully: the ball traveled 179.5549 m from the saved starting position,
and the authorization was consumed. The shot completed normally.

Both peers inspected the local ground ring, the game's real resume-ball particle system
(`Waiting`) and the remote ball ring. Initially player 1's markers were green
`(0.30, 1.00, 0.65)` and player 2's grey `(0.38, 0.42, 0.47)`.
After the accepted shot, player 2's local ring and native particles were green and
player 1's remote ring was grey.

The source regression executable passed 43 checks, including twelve permission checks for
offline/connecting/active/waiting states, unknown player ID, duplicate swing, turn-zero wait,
blocked network publication, solo host and session reset. Release build passed with zero
warnings/errors. Protocol v7 requires every player to update to v0.3.8.
