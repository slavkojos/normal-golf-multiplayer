# Shot notifications — v0.3.9

Validated on 2026-10-01 with Unity 6000.3.11f1, Unity Explorer, two isolated save
profiles (`shotHost` / `shotClient`) and loopback UDP port 17884.

Real game `HitSequence` shots delivered results in both directions:

| Shooter | Game classification | Stroke | Ground distance | Distance to active cup |
|---|---|---:|---:|---:|
| Alice (host) | Thin / Straight | 1 | 147.9575 m | 16.04777 m |
| Bob (client) | Fat / Hook | 1 | 72.10874 m | 76.28586 m |

The host's turn had already advanced to Bob when Alice's result arrived. Each
receiving peer emitted one result event and showed the corresponding three-line
toast. TMP reported `lineCount=3`, `isTextTruncated=false`, with an 88-unit card.
The local ball/cup coordinates independently reproduced both reported distances.

Alice's start was `(82.3301, 2.5404, -309.5000)`, finish
`(81.2969, 4.0270, -161.5461)`, and active cup `(87.2101, 3.1899, -176.4412)`.
Distance travelled uses horizontal displacement including roll; remaining distance
uses the physical rigidbody and the cup selected from the round save.

A further real shot after `LMUGC.RetakeShot(false)` reported the game's expected
stroke number 2 and `Fade` classification to the host: 51.84961 m travelled,
88.17298 m remaining. The three-line toast again fit without truncation.

The actual canvas was cloned and rendered with a temporary camera to inspect the
notification visually because hidden test windows produce black screen captures.
The preview is [13_shot_notification.png](../screenshots/13_shot_notification.png).
The temporary camera/canvas were destroyed immediately after capture.

The final source regression executable passes 85 checks covering turns, permissions,
wind and shot feedback. Shot checks include independent contact/shape labels,
serialization, numeric validation, stroke capture, physical distances, missing cups,
unmatched/duplicate results, turn advancement, session resets, cup feedback versus
the deferred ball-kill callback, terminal-shot completion and clearing pending swings.
Release compilation succeeds with zero warnings and errors. Protocol v8 requires
every player to install v0.3.9 and restart.
