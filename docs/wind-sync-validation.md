# Wind synchronization validation — v0.3.6

Validated on 2026-09-30 with Unity 6000.3.11f1, BepInEx 5 and Unity Explorer.
Two isolated save profiles (`windHost`, `windClient`) connected over loopback UDP port 17881.
The client joined an already running host and then loaded the course.

Unity Explorer inspected the real `WindPanel` and network snapshots in both processes.
With ordinary changing wind, sequence 2095 arrived with vector `(4.356544, 2.720479)`
and force multiplier `5`; the client's actual wind matched that snapshot and displayed `13 mph`.
With the host's debug wind enabled, both panels and snapshots reported `(-5, 0)`, multiplier `5`,
and `13 mph`. A host calm period then produced `(0, 0)`, `0 mph` and matching
`CalmSeconds=14.51944` at sequence 4401 on both peers.

On the client, the console deliberately overwrote the actual panel wind with `(99, -99)`.
Invoking the game's patched `Ball.FixedUpdate` restored the host vector before physics;
overwriting it again and invoking the patched `WindPanel.LateUpdate` restored the same vector
and updated the display. Both checks passed during nonzero wind and calm wind.
These checks inspect runtime state; they do not constitute screenshot comparisons or full-shot
trajectory equality tests.

The source regression executable passed 31 checks, including six wind checks: wire round trip,
sequence wrap, stale-packet rejection, invalid float rejection, sender authority and reset on leave.
Release build completed with zero warnings and errors. Protocol is now v6: all peers must update.
