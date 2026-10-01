using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace NormalGolfMultiplayer.Remote
{
    /// <summary>Shared meshes, materials, fonts and sounds for remote players, built from the game's own assets.</summary>
    internal static class Visuals
    {
        /// <summary>
        /// "IgnoreForRTEXCams" is drawn by the first-person camera, the golfer (FMV) camera and the ball-chase
        /// camera, but not by the small swing/spin/club panel cameras, which is exactly where avatars belong.
        /// </summary>
        public static int Layer { get; private set; } = 0;

        private static readonly Dictionary<PrimitiveType, Mesh> Meshes = new Dictionary<PrimitiveType, Mesh>();
        private static readonly Dictionary<string, Mesh> ProfileMeshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<Color, Material> LitCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<string, Material> SurfaceCache = new Dictionary<string, Material>();
        private static Texture2D _clothWeave;
        private static readonly Dictionary<string, Mesh> AvatarBatches = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static Material _baseLit;
        private static TMP_FontAsset _font;

        public static readonly Color Skin = new Color(0.78f, 0.60f, 0.47f);
        public static readonly Color Pants = new Color(0.24f, 0.25f, 0.30f);
        public static readonly Color Shoes = new Color(0.93f, 0.93f, 0.90f);
        public static readonly Color Dark = new Color(0.08f, 0.08f, 0.1f);
        public static readonly Color Hair = new Color(0.16f, 0.12f, 0.095f);
        public static readonly Color Steel = new Color(0.78f, 0.79f, 0.82f);

        /// <summary>Call on each course load: materials are cloned from scene objects that only exist there.</summary>
        public static void Prepare()
        {
            int layer = LayerMask.NameToLayer("IgnoreForRTEXCams");
            Layer = layer >= 0 ? layer : 0;

            if (_baseLit == null)
            {
                var ballMat = HitManager.instance?.m_ball?.m_meshRenderer?.sharedMaterial;
                if (ballMat == null && Cosmetics.instance != null && Cosmetics.instance.m_balls.Length > 0)
                    ballMat = Cosmetics.instance.m_balls[0].mat;
                if (ballMat != null)
                {
                    // Cloning a material the game already renders guarantees the URP shader variant exists in the build.
                    _baseLit = new Material(ballMat) { name = "NGMP_BaseLit" };
                    if (_baseLit.HasProperty("_BaseMap")) _baseLit.SetTexture("_BaseMap", null);
                    if (_baseLit.HasProperty("_MainTex")) _baseLit.SetTexture("_MainTex", null);
                    if (_baseLit.HasProperty("_BumpMap")) _baseLit.SetTexture("_BumpMap", null);
                    if (_baseLit.HasProperty("_EmissionColor")) _baseLit.SetColor("_EmissionColor", Color.black);
                    if (_baseLit.HasProperty("_Smoothness")) _baseLit.SetFloat("_Smoothness", 0.2f);
                    // The ball uses the specular workflow; a bright specular colour steals from the diffuse and washes colours out.
                    if (_baseLit.HasProperty("_SpecColor")) _baseLit.SetColor("_SpecColor", new Color(0.06f, 0.06f, 0.06f));
                    Plugin.Log.LogInfo($"Avatar material from '{ballMat.name}' ({ballMat.shader.name}) keywords=[{string.Join(",", ballMat.shaderKeywords)}]");
                }
                else
                {
                    _baseLit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "NGMP_BaseLit" };
                    Plugin.Log.LogWarning("Ball material not found; using a fresh URP/Lit material");
                }
            }

            if (_font == null)
            {
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                _font = fonts.FirstOrDefault(f => f.name == "Quantico-Regular SDF")
                        ?? TMP_Settings.defaultFontAsset
                        ?? fonts.FirstOrDefault(f => f.name.StartsWith("LiberationSans"))
                        ?? fonts.FirstOrDefault();
            }
        }

        public static Mesh GetMesh(PrimitiveType type)
        {
            if (Meshes.TryGetValue(type, out var mesh) && mesh != null)
                return mesh;
            var tmp = GameObject.CreatePrimitive(type);
            mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            Meshes[type] = mesh;
            return mesh;
        }

        /// <summary>A smooth, elliptical profile for clothing. Each ring is (height, half-width, half-depth, depth offset).</summary>
        public static Mesh ProfileMesh(string name, params Vector4[] rings)
        {
            if (ProfileMeshes.TryGetValue(name, out var cached) && cached != null)
                return cached;

            // Subdivide the authored silhouette smoothly instead of exposing straight ring-to-ring facets.
            var smooth = new List<Vector4>();
            for (int i = 0; i < rings.Length - 1; i++)
            {
                Vector4 p0 = rings[Mathf.Max(0, i - 1)], p1 = rings[i];
                Vector4 p2 = rings[i + 1], p3 = rings[Mathf.Min(rings.Length - 1, i + 2)];
                for (int step = 0; step < 3; step++)
                {
                    float u = step / 3f;
                    Vector4 r = 0.5f * ((2f * p1) + (-p0 + p2) * u +
                        (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u +
                        (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
                    r.x = Mathf.Lerp(p1.x, p2.x, u);
                    r.y = Mathf.Max(0.002f, r.y); r.z = Mathf.Max(0.002f, r.z);
                    smooth.Add(r);
                }
            }
            smooth.Add(rings[rings.Length - 1]);
            rings = smooth.ToArray();
            const int sides = 32;
            var vertices = new Vector3[rings.Length * sides + 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(rings.Length - 1) * sides * 6 + sides * 6];
            for (int i = 0; i < rings.Length; i++)
            {
                Vector4 ring = rings[i];
                for (int j = 0; j < sides; j++)
                {
                    float angle = j * Mathf.PI * 2f / sides;
                    vertices[i * sides + j] = new Vector3(
                        Mathf.Cos(angle) * ring.y, ring.x, Mathf.Sin(angle) * ring.z + ring.w);
                    uv[i * sides + j] = new Vector2(j / (float)sides, i / (float)(rings.Length - 1));
                }
            }
            int bottom = rings.Length * sides;
            int top = bottom + 1;
            vertices[bottom] = new Vector3(0f, rings[0].x, rings[0].w);
            vertices[top] = new Vector3(0f, rings[rings.Length - 1].x, rings[rings.Length - 1].w);

            int t = 0;
            for (int i = 0; i < rings.Length - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * sides + j;
                    int d = i * sides + (j + 1) % sides;
                    int b = (i + 1) * sides + j;
                    int c = (i + 1) * sides + (j + 1) % sides;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                }
            for (int j = 0; j < sides; j++)
            {
                int next = (j + 1) % sides;
                triangles[t++] = bottom; triangles[t++] = j; triangles[t++] = next;
                triangles[t++] = top; triangles[t++] = (rings.Length - 1) * sides + next;
                triangles[t++] = (rings.Length - 1) * sides + j;
            }

            var mesh = new Mesh { name = "NGMP_" + name, vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ProfileMeshes[name] = mesh;
            return mesh;
        }

        public static Material Lit(Color c)
        {
            if (LitCache.TryGetValue(c, out var m) && m != null)
                return m;
            m = new Material(_baseLit) { name = "NGMP_Lit_" + ColorUtility.ToHtmlStringRGB(c) };
            m.color = c;
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", c);
            LitCache[c] = m;
            return m;
        }

        public enum Surface { Cloth, Skin, Leather, Metal }
        public static Mesh CollarMesh(int side)
        {
            string key = "GolferCollar" + side;
            if (ProfileMeshes.TryGetValue(key, out var cached) && cached != null) return cached;
            Vector3 a = new Vector3(side * 0.035f, 0.637f, 0.079f);
            Vector3 b = new Vector3(side * 0.125f, 0.602f, 0.095f);
            Vector3 c = new Vector3(side * 0.071f, 0.531f, 0.154f);
            if (Vector3.Cross(b - a, c - a).z < 0f) { var swap = b; b = c; c = swap; }
            Vector3 thickness = new Vector3(0f, 0f, -0.006f);
            var mesh = new Mesh { name = "NGMP_Collar", vertices = new[] { a, b, c, a + thickness, b + thickness, c + thickness },
                triangles = new[] { 0,1,2, 5,4,3, 0,3,4, 0,4,1, 1,4,5, 1,5,2, 2,5,3, 2,3,0 },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); ProfileMeshes[key] = mesh;
            return mesh;
        }
        public static Material DetailedMaterial(Color color, Surface surface)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color) + surface;
            if (SurfaceCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = new Material(Lit(color)) { name = "NGMP_" + key };
            float smoothness = surface == Surface.Metal ? 0.72f : surface == Surface.Skin ? 0.34f : surface == Surface.Leather ? 0.3f : 0.16f;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", surface == Surface.Metal ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.05f, 0.05f, 0.05f));
            if (surface == Surface.Cloth)
            {
                if (_clothWeave == null)
                {
                    _clothWeave = new Texture2D(64, 64, TextureFormat.RGB24, false) { name = "NGMP_PiqueWeave", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                    var pixels = new Color[64 * 64];
                    for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                    {
                        float shade = 0.95f + 0.025f * Mathf.Sin(x * Mathf.PI * 0.5f) * Mathf.Cos(y * Mathf.PI * 0.5f);
                        pixels[y * 64 + x] = new Color(shade, shade, shade);
                    }
                    _clothWeave.SetPixels(pixels); _clothWeave.Apply(false, true);
                }
                if (material.HasProperty("_BaseMap")) { material.SetTexture("_BaseMap", _clothWeave); material.SetTextureScale("_BaseMap", new Vector2(5f, 5f)); }
            }
            SurfaceCache[key] = material;
            return material;
        }

        public static Transform Detail(string name, Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Color color,
            Surface surface = Surface.Cloth, Quaternion? rotation = null)
        {
            var part = MeshPart(name, mesh, parent, pos, scale, color, rotation);
            part.GetComponent<MeshRenderer>().sharedMaterial = DetailedMaterial(color, surface);
            return part;
        }

        public static void SkinnedLimb(string name, Mesh mesh, Transform upper, Transform joint, float split, Color color, Surface surface)
        {
            if (mesh.boneWeights.Length == 0)
            {
                var weights = new BoneWeight[mesh.vertexCount];
                var vertices = mesh.vertices;
                for (int i = 0; i < weights.Length; i++)
                {
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((split + 0.075f - vertices[i].y) / 0.15f));
                    weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f - blend, boneIndex1 = 1, weight1 = blend };
                }
                mesh.boneWeights = weights;
                mesh.bindposes = new[] { Matrix4x4.identity,
                    Matrix4x4.TRS(new Vector3(0f, split, 0f), Quaternion.identity, Vector3.one).inverse };
            }
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(upper, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = DetailedMaterial(color, surface);
            renderer.bones = new[] { upper, joint };
            renderer.rootBone = upper;
            renderer.quality = SkinQuality.Bone2;
            renderer.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 1.2f);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        /// <summary>A narrow seam, lace or piping strip between two local points.</summary>
        public static Transform Strip(string name, Transform parent, Vector3 a, Vector3 b, float radius, Color color)
        {
            Vector3 delta = b - a;
            return Part(name, PrimitiveType.Cylinder, parent, (a + b) * 0.5f,
                new Vector3(radius * 2f, delta.magnitude * 0.5f, radius * 2f), color,
                Quaternion.FromToRotation(Vector3.up, delta), shadows: false);
        }

        /// <summary>Merge details on each rigid bone by material, keeping the joints and colour groups separate.</summary>
        public static void OptimizeAvatar(Transform root, List<MeshRenderer> colored, List<MeshRenderer> darkColored)
        {
            foreach (var parent in root.GetComponentsInChildren<Transform>(true))
            {
                if (parent == null) continue;
                var groups = new Dictionary<(Material, int, ShadowCastingMode), List<MeshFilter>>();
                for (int i = 0; i < parent.childCount; i++)
                {
                    var filter = parent.GetChild(i).GetComponent<MeshFilter>();
                    var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
                    if (renderer == null) continue;
                    int role = colored.Contains(renderer) ? 1 : darkColored.Contains(renderer) ? 2 : 0;
                    var key = (renderer.sharedMaterial, role, renderer.shadowCastingMode);
                    if (!groups.TryGetValue(key, out var parts)) groups[key] = parts = new List<MeshFilter>();
                    parts.Add(filter);
                }
                foreach (var group in groups)
                {
                    if (group.Value.Count < 2) continue;
                    string path = parent.name;
                    for (var ancestor = parent.parent; ancestor != null && ancestor != root.parent; ancestor = ancestor.parent)
                        path = ancestor.name + "/" + path;
                    string key = path + "/" + group.Key.Item1.name + "/" + group.Key.Item2 + "/" + group.Key.Item3;
                    if (!AvatarBatches.TryGetValue(key, out var mesh) || mesh == null)
                    {
                        var parts = new CombineInstance[group.Value.Count];
                        for (int i = 0; i < parts.Length; i++)
                        {
                            var part = group.Value[i];
                            parts[i] = new CombineInstance { mesh = part.sharedMesh,
                                transform = Matrix4x4.TRS(part.transform.localPosition, part.transform.localRotation, part.transform.localScale) };
                        }
                        mesh = new Mesh { name = "NGMP_GolferBatch_" + parent.name };
                        mesh.CombineMeshes(parts, true, true);
                        mesh.RecalculateBounds();
                        AvatarBatches[key] = mesh;
                    }
                    var merged = MeshPart("Details", mesh, parent, Vector3.zero, Vector3.one, Color.white);
                    var renderer = merged.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = group.Key.Item1;
                    renderer.shadowCastingMode = group.Key.Item3;
                    if (group.Key.Item2 == 1) colored.Add(renderer);
                    if (group.Key.Item2 == 2) darkColored.Add(renderer);
                    foreach (var part in group.Value) Object.DestroyImmediate(part.gameObject);
                }
            }
            colored.RemoveAll(renderer => renderer == null);
            darkColored.RemoveAll(renderer => renderer == null);
        }

        /// <summary>A mesh part with no collider, so it can never touch the player, the ball or raycasts.</summary>
        public static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color,
            Quaternion? rot = null, bool shadows = true)
            => MeshPart(name, GetMesh(type), parent, pos, scale, color, rot, shadows);

        public static Transform MeshPart(string name, Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Color color,
            Quaternion? rot = null, bool shadows = true)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Lit(color);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go.transform;
        }

        public static Transform Pivot(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        public static TextMeshPro Label(string name, Transform parent, float fontSize, Color color)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (_font != null)
                tmp.font = _font;
            tmp.richText = false; // names come from other players; never interpret tags
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(20f, 3f);
            tmp.color = color;
            tmp.outlineWidth = 0.22f;
            tmp.outlineColor = new Color32(0, 0, 0, 220);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tmp;
        }

        public static Mesh BallMesh(out Vector3 scale)
        {
            scale = Vector3.one * 0.1f;
            var renderer = HitManager.instance?.m_ball?.m_meshRenderer;
            if (renderer == null)
                return GetMesh(PrimitiveType.Sphere);
            scale = renderer.transform.lossyScale;
            var mf = renderer.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh : GetMesh(PrimitiveType.Sphere);
        }

        public static Material BallMaterial(int look)
        {
            var c = Cosmetics.instance;
            if (c != null && c.m_balls != null && c.m_balls.Length > 0)
                return c.m_balls[Mathf.Clamp(look, 0, c.m_balls.Length - 1)].mat;
            return HitManager.instance?.m_ball?.m_meshRenderer?.sharedMaterial;
        }

        public static TrailRenderer TrailTemplate => HitManager.instance?.m_ball?.m_trail;

        // ------------------------------------------------------------------ sound

        public static AudioClip Clip(string name)
        {
            if (Clips.TryGetValue(name, out var clip) && clip != null)
                return clip;
            var am = AudioManager.Instance;
            if (am == null)
                return null;
            var sounds = Traverse.Create(am).Field("m_sounds").GetValue<Sound[]>();
            clip = sounds?.FirstOrDefault(s => s.m_name == name)?.m_clip;
            Clips[name] = clip;
            return clip;
        }

        /// <summary>One-shot 3D sound routed through the game's SFX mixer, so the SFX volume slider applies.</summary>
        public static void PlayAt(string clipName, Vector3 pos, float volume = 1f)
        {
            if (!ModConfig.RemoteSounds.Value)
                return;
            var clip = Clip(clipName);
            if (clip == null)
                return;
            var go = new GameObject("NGMP_Sound_" + clipName);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 6f;
            src.maxDistance = 250f;
            src.dopplerLevel = 0f;
            if (AudioManager.Instance != null)
                src.outputAudioMixerGroup = AudioManager.Instance.m_sfxMixer;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        public static string HitClipFor(Clubs club, ShotType shot)
        {
            if (shot == ShotType.Ground)
                return "hitGround";
            switch (club)
            {
                case Clubs.Driver: return "driverHit";
                case Clubs.Putter: return "putterHit";
                case Clubs.Hybrid: return "hybridHit";
                default: return "ironHit";
            }
        }
    }
}
