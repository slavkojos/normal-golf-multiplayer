# Golfer model validation — v0.3.7

Validated on 2026-09-30 in an isolated `modelQA` save profile with Unity 6000.3.11f1
and Unity Explorer. An actual `RemotePlayerView` was rendered with a separate studio camera;
the standing preview is in `screenshots/10_golfer_model.png`.

The runtime model has 50 MeshRenderers, including its name tag, and four SkinnedMeshRenderers.
Its inspected meshes contain 61,726 triangles; the largest individual mesh has 4,303 vertices.
Details are combined by material and rigid bone. A second differently coloured golfer reused
all 49 rigid mesh references and all four skinned limb meshes; changing player colour retained
the cloth weave material.

Walking, crouching, address, backswing and follow-through all produced finite transforms.
Each pose also baked all four skinned meshes without invalid or out-of-range vertices.
The hands remained within 4.3 cm of the grip pivot throughout the three golf poses.
No collider is attached to the avatar, so it cannot deflect a ball or block gameplay raycasts.

The release build passed with zero warnings/errors and the existing 31 turn/wind checks passed.
The preview verifies the model visually; it is not an eight-player frame-rate benchmark.
Protocol remains v6 and is compatible with v0.3.6.
