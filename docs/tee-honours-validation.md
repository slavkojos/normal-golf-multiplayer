# Tee honours — v0.3.10

Implements stroke-play honours from [R&A Rule 6.4b](https://www.randa.org/en/rog/the-rules-of-golf/rule-6):
order by gross score on the previous hole, with equal scores preserving the order
at the previous tee. Gross scores come from the game's recorded hole totals, including penalties.

The deterministic regression executable runs the actual host session, turn-order and
tee-honours sources. All 114 checks pass. Honours coverage includes:

* Initial agreed order, lowest previous-hole score first and cumulative score having no priority.
* Complete and partial ties across successive holes, preserving the previous tee order.
* Delayed/missing scorecards, resumed rounds and restarting a new round.
* The honour holder still walking to the tee, a hidden ball after holing out, and a slow/loading peer.
* Disconnecting the honour holder and retaining the remaining relative order.
* Returning to farthest-ball-first after everyone has teed off.

Release build passes with zero warnings and errors. Protocol remains v8; the host must update
to v0.3.10 to calculate honours.

Live validation uses Unity Explorer, Unity 6000.3.11f1, two isolated save profiles
(`honoursHost`, `honoursClient`) and loopback UDP port 17886. Hole transitions use the
game's actual `LMUGC.CompleteHole` and `TeleportBallToHole` paths with controlled recorded scores.

On the first tee both peers showed player 1, the agreed initial order. The host recorded
5 on hole 1 and moved to tee 2 early. The client recorded 3 but stayed behind with its
ball hidden. Both peers then showed turn 0 and the hole-2 cup
`(379.2268, 18.5134, -83.6558)`: the host could not steal the honour. When the client
arrived at the tee, both peers awarded player 2 the first shot.

The client then took a real 4-power shot through `HitSequence`; the host was awarded
the second tee shot and took a real 2-power shot. Once both balls rested, both peers
awarded player 1 the next shot: the host was 244.80 m from the cup and the client
214.89 m away. This confirms that tee honours give way to ordinary distance order.

Both golfers recorded 4 on hole 2. The host arrived at tee 3 first, but both peers
waited until the client arrived and then awarded player 2 the first shot again,
preserving the previous tee order through the tie.

On hole 3 the host recorded 3 and the client 5. Both cumulative totals were then
12, but both peers awarded the host the first shot on tee 4. The immediately
previous hole's better score took priority over tied round totals and the client's
earlier honour.
