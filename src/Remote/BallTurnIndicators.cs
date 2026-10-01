using System.Collections.Generic;
using NormalGolfMultiplayer.Game;
using NormalGolfMultiplayer.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace NormalGolfMultiplayer.Remote
{
    internal class BallTurnIndicators : MonoBehaviour
    {
        public static readonly Color ActiveColor = new Color(0.3f, 1f, 0.65f);
        public static readonly Color WaitingColor = new Color(0.38f, 0.42f, 0.47f);
        private static Mesh _ringMesh;
        private Transform _localRing;
        private Ball _ball;
        private bool _tinted;
        private bool _active;
        private readonly Dictionary<Renderer, MaterialPropertyBlock> _originalBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        private readonly Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> _originalParticles = new Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();
        private readonly MaterialPropertyBlock _tintBlock = new MaterialPropertyBlock();

        public static bool ShowMarkers => NetSession.Instance != null && NetSession.Instance.InSession && NetSession.Instance.Players.Count > 1;
        public static bool IsActive(byte id) => NetSession.Instance != null && id != 0 && NetSession.Instance.ActiveTurnId == id;

        public static Transform CreateRing(Transform parent)
        {
            if (_ringMesh == null)
            {
                const int sides = 64;
                var vertices = new Vector3[sides * 2];
                var normals = new Vector3[vertices.Length];
                var triangles = new int[sides * 12];
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices[i * 2] = direction * 0.19f; vertices[i * 2 + 1] = direction * 0.23f;
                    normals[i * 2] = Vector3.up; normals[i * 2 + 1] = Vector3.up;
                    int a = i * 2, b = (i * 2 + 2) % vertices.Length, c = b + 1, d = a + 1;
                    int t = i * 12;
                    triangles[t] = a; triangles[t+1] = b; triangles[t+2] = c;
                    triangles[t+3] = a; triangles[t+4] = c; triangles[t+5] = d;
                    triangles[t+6] = c; triangles[t+7] = b; triangles[t+8] = a;
                    triangles[t+9] = d; triangles[t+10] = c; triangles[t+11] = a;
                }
                _ringMesh = new Mesh { name = "NGMP_TurnRing", vertices = vertices, triangles = triangles, normals = normals };
                _ringMesh.RecalculateBounds();
            }
            var ring = Visuals.MeshPart("TurnRing", _ringMesh, parent, Vector3.zero, Vector3.one, WaitingColor, shadows: false);
            ring.GetComponent<MeshRenderer>().receiveShadows = false;
            ring.gameObject.SetActive(false);
            return ring;
        }

        public static void UpdateRing(Transform ring, Vector3 ballPosition, bool settled, byte id)
        {
            bool show = ShowMarkers && settled;
            ring.gameObject.SetActive(show);
            ring.GetComponent<MeshRenderer>().sharedMaterial = Visuals.Lit(IsActive(id) ? ActiveColor : WaitingColor);
            if (!show) return;
            Vector3 position = ballPosition + Vector3.down * 0.025f;
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(ballPosition + Vector3.up * 0.3f, Vector3.down, out var hit, 1f, LayerMask.GetMask("GolfCourse")))
            {
                position = hit.point + hit.normal * 0.012f;
                normal = hit.normal;
            }
            ring.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, normal));
        }

        private void LateUpdate()
        {
            var currentBall = LocalPlayer.InWorld ? HitManager.instance.m_ball : null;
            if (currentBall != _ball)
            {
                RestoreNativeIndicator();
                _ball = currentBall;
            }
            if (_ball == null || !ShowMarkers)
            {
                RestoreNativeIndicator();
                if (_localRing != null) _localRing.gameObject.SetActive(false);
                return;
            }
            if (_localRing == null)
            {
                Visuals.Prepare();
                _localRing = CreateRing(null);
            }
            var state = LocalPlayer.Capture();
            UpdateRing(_localRing, state.BallPos, state.Has(StateFlags.BallVisible) &&
                !state.Has(StateFlags.BallMoving) && !state.ShotInProgress, NetSession.Instance.LocalId);
            bool active = IsActive(NetSession.Instance.LocalId);
            if (!_tinted || _active != active)
                TintNativeIndicator(active);
        }

        private void TintNativeIndicator(bool active)
        {
            _tinted = true; _active = active;
            if (_ball.m_resumeBallEffect == null) return;
            Color color = active ? ActiveColor : WaitingColor;
            foreach (var renderer in _ball.m_resumeBallEffect.GetComponentsInChildren<Renderer>(true))
            {
                if (!_originalBlocks.ContainsKey(renderer))
                {
                    var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original);
                    _originalBlocks[renderer] = original;
                }
                renderer.GetPropertyBlock(_tintBlock);
                _tintBlock.SetColor("_BaseColor", color); _tintBlock.SetColor("_Color", color);
                _tintBlock.SetColor("_TintColor", color);
                renderer.SetPropertyBlock(_tintBlock);
            }
            foreach (var particles in _ball.m_resumeBallEffect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                if (!_originalParticles.ContainsKey(particles)) _originalParticles[particles] = main.startColor;
                main.startColor = color;
                var live = new ParticleSystem.Particle[particles.particleCount];
                int count = particles.GetParticles(live);
                for (int i = 0; i < count; i++)
                {
                    Color32 tint = color; tint.a = live[i].startColor.a; live[i].startColor = tint;
                }
                particles.SetParticles(live, count);
            }
        }

        private void RestoreNativeIndicator()
        {
            if (!_tinted) return;
            foreach (var entry in _originalBlocks) if (entry.Key != null) entry.Key.SetPropertyBlock(entry.Value);
            foreach (var entry in _originalParticles)
                if (entry.Key != null) { var main = entry.Key.main; main.startColor = entry.Value; }
            _originalBlocks.Clear(); _originalParticles.Clear(); _tinted = false;
        }

        private void OnDestroy()
        {
            RestoreNativeIndicator();
            if (_localRing != null) Destroy(_localRing.gameObject);
        }
    }
}
