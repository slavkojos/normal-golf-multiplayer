# Live turn-order validation — 30 September 2026

Tested with two isolated game profiles over localhost, Unity 6000.3.11f1, mod v0.3.4 / protocol v5.
Used [Unity Explorer's Unity 6 Mono build](https://github.com/UnloadedHangar/UnityExplorer-Unity6.BIE.Mono),
its C# console evaluator, and its `InspectorManager.Inspect` API on the actual local ball GameObject.

The original live inspection exposed a tracked `miniIsland` cup at `(152.7041, 1.5472, -288.6847)`
before the mod's scorecard initialized. The actual `hole1` magnet is `(87.2101, 3.1899, -176.4412)`.
From the tee ball `(82.3301, 2.5404, -309.5000)`, these give 73.3947 m and 133.1499 m respectively.
A stale target can exclude the other golfer from the host's group and leave an obsolete turn selected.

The corrected code reads the game's active round save and measures `Rigidbody.position` against `hole1`.
The host distributes that same flag to clients. Controlled console checks used the actual ball rigidbodies,
paused physics, and the real session's reliable shot messages:

| Alice's ball | Bob's ball | Result on both copies |
| --- | --- | --- |
| 120 m, opening shot taken | Opening shot outstanding | Bob tees off |
| 120 m | 160 m, opening shot taken | Bob plays |
| 120 m | 140 m, next shot taken | Bob plays again |
| 120 m | 90 m, next shot taken | Alice plays |

On both copies, `TryGetTurnDistance` reported 120 m and 90.00001 m in the last case,
matching Unity Explorer's independent `Vector3.Distance(ball.m_rb.position, hole1.transform.position)`.

Then physics was resumed and both rounds restarted. Actual `hit 5` / `hit 3` swings exercised the
Harmony shot-start and shot-completion hooks. While Bob's video prepared the shot, his rigidbody was
still resting but `ShotInProgress` was true and turn was 0. After both balls settled, both copies
reported Alice 72.80804 m, Bob 72.45241 m, and turn 1 (Alice). Each local measurement matched
the corresponding UI distance on both copies.

The deterministic suite additionally covers long flights, delayed UDP states, ties, hole-outs,
round changes and retakes. Hidden test windows did not provide usable rendered screenshots;
these checks establish live object, network and UI-data correctness rather than visual rendering.

To inspect again, press F7 for Unity Explorer and use its C# console:

```csharp
var flag = System.Linq.Enumerable.First(
    UnityEngine.Resources.FindObjectsOfTypeAll<HoleMagnet>(),
    c => c.m_id == "hole1" && c.gameObject.scene.IsValid());
UnityExplorer.InspectorManager.Inspect(HitManager.instance.m_ball.gameObject);
UnityExplorer.ExplorerCore.Log(UnityEngine.Vector3.Distance(
    HitManager.instance.m_ball.m_rb.position, flag.transform.position));
```

Use the actual current hole ID for later holes. The `holes` developer command also reports the host's
shared flag and the authoritative distance for every network ball. Raw console logs and inspection
dumps are in the game's `BepInEx/ngmp_debug` folder and `BepInEx/LogOutput.log` files.
