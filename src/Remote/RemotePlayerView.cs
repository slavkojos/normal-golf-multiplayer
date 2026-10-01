using System.Collections.Generic;
using NormalGolfMultiplayer.Net;
using TMPro;
using UnityEngine;

namespace NormalGolfMultiplayer.Remote
{
    /// <summary>Everything we know about one other player; survives scene changes (views don't).</summary>
    internal class RemotePlayer
    {
        public PlayerInfo Info;
        public readonly SnapshotBuffer Buffer = new SnapshotBuffer();
        public RemotePlayerView View;

        public float SwingStart = -1000f; // local time the swing animation starts
        public Clubs SwingClub = Clubs.Iron;

        /// <summary>Their Front Nine scorecard, or null if they haven't played a round this session.</summary>
        public ScoreCard Score;

        public bool HasPose;
        public StateFlags Flags;
        public Vector3 Position;
        public Vector3 BallPosition;
    }

    /// <summary>
    /// A remote player's avatar and ball on the course. The game has no player model (hands and golfer are FMV),
    /// so the avatar is a lightweight golfer built from shared meshes, coloured with the player's colour.
    /// Root sits at the feet and faces +Z.
    /// </summary>
    internal partial class RemotePlayerView : MonoBehaviour
    {
        private const float BallOffsetFromGolfer = 0.79f; // MoveAndHitController.m_ballPosition.localPosition.x

        private RemotePlayer _player;

        // avatar rig
        private Transform _scaler, _legL, _legR, _kneeL, _kneeR, _ankleL, _ankleR, _torso, _head, _swing, _armL, _armR, _handR, _hands, _club, _clubParent;
        private readonly List<MeshRenderer> _colorParts = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _darkColorParts = new List<MeshRenderer>();
        private TextMeshPro _nameTag;

        // ball
        private Transform _ballRoot, _ballMesh;
        private Transform _turnRing;
        private MeshRenderer _ballRenderer;
        private TrailRenderer _trail;
        private TextMeshPro _ballLabel;
        private float _ballRadius = 0.04f;
        private bool _ballPlaced;
        private byte _ballEpoch;
        private Vector3 _lastBallPos;

        // animation
        private Vector3 _lastPos;
        private bool _hasLastPos;
        private float _speed, _walkPhase, _crouch, _golfBlend;
        private bool _visible = true;

        // what's currently shown, to detect PlayerInfo changes
        private string _shownName;
        private Color32 _shownColor;
        private int _shownLook = -1;

        public static RemotePlayerView Create(RemotePlayer player, Transform parent)
        {
            var go = new GameObject($"NGMP Player {player.Info.Id}") { layer = Visuals.Layer };
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RemotePlayerView>();
            view._player = player;
            view.BuildAvatar();
            view.BuildBall(parent);
            view.RefreshInfo();
            return view;
        }

        private void OnDestroy()
        {
            if (_ballRoot != null)
                Destroy(_ballRoot.gameObject);
        }

        // ------------------------------------------------------------------ construction

        private void BuildAvatar() => BuildDetailedAvatar();

        private void BuildBall(Transform parent)
        {
            var root = new GameObject($"NGMP Ball {_player.Info.Id}") { layer = Visuals.Layer };
            root.transform.SetParent(parent, false);
            _ballRoot = root.transform;

            Mesh mesh = Visuals.BallMesh(out Vector3 scale);
            var meshGo = new GameObject("Mesh") { layer = Visuals.Layer };
            meshGo.transform.SetParent(_ballRoot, false);
            meshGo.transform.localScale = scale;
            meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            _ballRenderer = meshGo.AddComponent<MeshRenderer>();
            _ballMesh = meshGo.transform;
            _ballRadius = Mathf.Max(0.01f, mesh.bounds.extents.x * scale.x);

            _trail = root.AddComponent<TrailRenderer>();
            TrailRenderer template = Visuals.TrailTemplate;
            if (template != null)
            {
                _trail.sharedMaterial = template.sharedMaterial;
                _trail.time = template.time;
                _trail.widthMultiplier = template.widthMultiplier;
                _trail.widthCurve = template.widthCurve;
                _trail.minVertexDistance = template.minVertexDistance;
                _trail.numCapVertices = template.numCapVertices;
                _trail.numCornerVertices = template.numCornerVertices;
                _trail.textureMode = template.textureMode;
                _trail.alignment = template.alignment;
            }
            else
            {
                _trail.time = 6f;
                _trail.widthMultiplier = 0.25f;
            }
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.emitting = false;

            _ballLabel = Visuals.Label("BallLabel", _ballRoot, 1.6f, Color.white);
            _turnRing = BallTurnIndicators.CreateRing(_ballRoot);
        }

        private void Colored(Transform part) => _colorParts.Add(part.GetComponent<MeshRenderer>());
        private void DarkColored(Transform part) => _darkColorParts.Add(part.GetComponent<MeshRenderer>());

        /// <summary>Applies name/colour/ball-cosmetic changes.</summary>
        public void RefreshInfo()
        {
            var info = _player.Info;
            if (info.Name == _shownName && info.Color.Equals(_shownColor) && info.BallLook == _shownLook)
                return;
            _shownName = info.Name;
            _shownColor = info.Color;
            _shownLook = info.BallLook;

            Color c = info.Color;
            var mat = Visuals.DetailedMaterial(c, Visuals.Surface.Cloth);
            var dark = Visuals.DetailedMaterial(Color.Lerp(c, new Color(0.05f, 0.06f, 0.06f), 0.22f), Visuals.Surface.Cloth);
            foreach (var r in _colorParts) r.sharedMaterial = mat;
            foreach (var r in _darkColorParts) r.sharedMaterial = dark;

            Color labelColor = Color.Lerp(c, Color.white, 0.5f);
            _nameTag.text = info.Name;
            _nameTag.color = labelColor;
            _ballLabel.text = info.Name;
            _ballLabel.color = labelColor;

            _ballRenderer.sharedMaterial = Visuals.BallMaterial(info.BallLook);
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.15f, 1f) });
            _trail.colorGradient = grad;
        }

        // ------------------------------------------------------------------ per frame

        public void Tick(float now, float delay, float dt)
        {
            RefreshInfo();

            if (!_player.Buffer.Sample(now, delay, out PlayerState a, out PlayerState b, out float t) || !b.Has(StateFlags.InWorld))
            {
                SetVisible(false);
                SetBallVisible(false);
                _player.HasPose = false;
                return;
            }
            SetVisible(true);

            bool golfing = b.Has(StateFlags.Golfing);
            // Mode switches and teleports (bunkers, respawns) must snap, not slide across the course.
            bool snap = a.Has(StateFlags.Golfing) != golfing || (b.Pos - a.Pos).sqrMagnitude > 64f;
            Vector3 pos = snap ? b.Pos : Vector3.Lerp(a.Pos, b.Pos, t);
            float yaw = snap ? b.Yaw : Mathf.LerpAngle(a.Yaw, b.Yaw, t);
            float pitch = Mathf.Lerp(a.Pitch, b.Pitch, t);

            if (_hasLastPos && !snap && dt > 0f)
            {
                Vector3 d = pos - _lastPos;
                d.y = 0f;
                _speed = Mathf.Lerp(_speed, Mathf.Min(d.magnitude / dt, 12f), 1f - Mathf.Exp(-dt * 8f));
            }
            else
            {
                _speed = 0f;
            }
            _lastPos = pos;
            _hasLastPos = true;

            // Golfing: pos/yaw are the golfer holder's; the golfer faces the ball, 90 degrees right of the target line.
            float bodyYaw = golfing ? yaw + 90f : yaw;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, bodyYaw, 0f));

            _crouch = Mathf.MoveTowards(_crouch, b.Has(StateFlags.Crouched) ? 1f : 0f, dt * 5f);
            _golfBlend = Mathf.MoveTowards(_golfBlend, golfing ? 1f : 0f, dt * 4f);
            Animate(now, dt, pitch, golfing);
            _nameTag.gameObject.SetActive(ModConfig.ShowNameTags.Value);
            // Leaning over the ball (or crouching) lowers the head, so the tag follows it down.
            float tagHeight = Mathf.Lerp(2.22f, 2.02f, _golfBlend) - 0.23f * _crouch;
            _nameTag.transform.localPosition = new Vector3(0f, tagHeight, 0f);

            UpdateBall(a, b, t, dt);

            _player.HasPose = true;
            _player.Flags = b.Flags;
            _player.Position = pos;
            _player.BallPosition = _ballRoot.position;
        }

        private void Animate(float now, float dt, float pitch, bool golfing)
        {
            float walk = golfing ? 0f : Mathf.Clamp01(_speed / 3f);
            _walkPhase += dt * _speed * 3.2f;
            float legSwing = Mathf.Sin(_walkPhase) * 38f * walk * (1f - _crouch);
            float kneeBend = 40f * _crouch;
            _legL.localRotation = Quaternion.Euler(legSwing - kneeBend, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-legSwing - kneeBend, 0f, 0f);
            _kneeL.localRotation = Quaternion.Euler(kneeBend * 2f, 0f, 0f);
            _kneeR.localRotation = Quaternion.Euler(kneeBend * 2f, 0f, 0f);
            _ankleL.localRotation = Quaternion.Euler(-legSwing - kneeBend, 0f, 0f);
            _ankleR.localRotation = Quaternion.Euler(legSwing - kneeBend, 0f, 0f);
            float bob = Mathf.Abs(Mathf.Cos(_walkPhase)) * 0.05f * walk * (1f - _crouch);
            float hipHeight = Mathf.Lerp(0.95f, 0.73f, _crouch) + bob;
            float stance = Mathf.Lerp(0.115f, 0.16f, _golfBlend);
            _legL.localPosition = new Vector3(-stance, hipHeight, 0f);
            _legR.localPosition = new Vector3(stance, hipHeight, 0f);
            _torso.localPosition = new Vector3(0f, hipHeight, 0f);

            float swing = 0f;
            float st = now - _player.SwingStart;
            if (golfing && st >= 0f && st < SwingDuration)
                swing = SwingCurve(st) * ClubAmplitude(_player.SwingClub);

            float lean = Mathf.Lerp(walk * 6f, 28f, _golfBlend) + _crouch * 15f;
            float turn = swing * 0.3f;
            _torso.localRotation = Quaternion.Euler(lean, turn, 0f);
            float look = golfing ? 22f : Mathf.Clamp(pitch, -60f, 60f);
            _head.localRotation = Quaternion.Euler(look - lean * 0.7f, -turn * 0.8f, 0f);

            _swing.localRotation = Quaternion.identity;
            if (_golfBlend > 0.5f)
            {
                SetClubParent(_hands);
                AimArm(_armL, _elbowL, _hands.localPosition + new Vector3(-0.023f, 0.035f, 0f), -1f);
                AimArm(_armR, _elbowR, _hands.localPosition + new Vector3(0.023f, -0.018f, 0.015f), 1f);
                // Address pose: shaft from the hands to the ball, face square to the target (golfer's left).
                Vector3 ballTarget = transform.TransformPoint(new Vector3(0f, 0.04f, BallOffsetFromGolfer));
                Vector3 dir = (ballTarget - _hands.position).normalized;
                _club.rotation = Quaternion.LookRotation(-transform.right, -dir);
                _club.localScale = Vector3.one * Vector3.Distance(ballTarget, _hands.position);
                // The swing rotates arms+club around the (leaned) chest axis; positive = backswing to the right.
                _swing.localRotation = Quaternion.Euler(0f, 0f, swing);
            }
            else
            {
                SetClubParent(_handR);
                float armSwing = Mathf.Sin(_walkPhase) * 32f * walk;
                _armL.localRotation = Quaternion.Euler(-armSwing, 0f, -5f);
                _armR.localRotation = Quaternion.Euler(armSwing, 0f, 5f);
                _elbowL.localRotation = Quaternion.Euler(-12f - Mathf.Max(0f, armSwing) * 0.35f, 0f, 0f);
                _elbowR.localRotation = Quaternion.Euler(-12f - Mathf.Max(0f, -armSwing) * 0.35f, 0f, 0f);
                _club.localScale = Vector3.one;
                _club.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            }
        }

        private void SetClubParent(Transform parent)
        {
            if (_clubParent == parent)
                return;
            _club.SetParent(parent, false);
            _club.localPosition = Vector3.zero;
            _clubParent = parent;
        }

        private static void AimArm(Transform arm, Transform elbow, Vector3 targetInParent, float side)
        {
            Vector3 delta = targetInParent - arm.localPosition;
            float distance = Mathf.Clamp(delta.magnitude, 0.08f, UpperArmLength + ForearmLength - 0.001f);
            Vector3 direction = delta.normalized;
            // Solve two rigid arm segments with an outward elbow pole. Hands reach the grip
            // without scaling the arm or losing the silhouette of the elbow.
            Vector3 pole = Vector3.ProjectOnPlane(new Vector3(side * 0.7f, -0.25f, -1f), direction).normalized;
            float along = (UpperArmLength * UpperArmLength - ForearmLength * ForearmLength + distance * distance) / (2f * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0f, UpperArmLength * UpperArmLength - along * along));
            Vector3 upper = direction * along + pole * bend;
            arm.localRotation = Quaternion.FromToRotation(Vector3.down, upper.normalized);
            Vector3 lower = direction * distance - upper;
            elbow.localRotation = Quaternion.FromToRotation(Vector3.down, Quaternion.Inverse(arm.localRotation) * lower.normalized);
        }

        private const float SwingDuration = 2.6f;

        /// <summary>Swing angle in degrees (+ = backswing). Impact at 0.87 s matches HitSequence's launch delay.</summary>
        private static float SwingCurve(float t)
        {
            const float top = 0.55f, impact = 0.87f, finish = 1.15f, hold = 2.05f;
            if (t < top) return Mathf.SmoothStep(0f, 110f, t / top);
            if (t < impact)
            {
                float u = (t - top) / (impact - top);
                return Mathf.Lerp(110f, 0f, u * u); // accelerate into the ball
            }
            if (t < finish)
            {
                float u = (t - impact) / (finish - impact);
                return Mathf.Lerp(0f, -125f, 1f - (1f - u) * (1f - u));
            }
            if (t < hold) return -125f;
            return Mathf.SmoothStep(-125f, 0f, (t - hold) / (SwingDuration - hold));
        }

        private static float ClubAmplitude(Clubs club)
        {
            switch (club)
            {
                case Clubs.Putter: return 0.22f;
                case Clubs.Wedge: return 0.75f;
                case Clubs.Iron: return 0.88f;
                case Clubs.Hybrid: return 0.95f;
                default: return 1f;
            }
        }

        private void UpdateBall(PlayerState a, PlayerState b, float t, float dt)
        {
            bool visible = b.Has(StateFlags.BallVisible);
            SetBallVisible(visible);
            if (!visible)
            {
                _turnRing.gameObject.SetActive(false);
                _ballPlaced = false;
                return;
            }

            // A reset/teleport shows up as a new epoch (or an impossible jump): snap and wipe the trail.
            bool jump = a.BallEpoch != b.BallEpoch || (b.BallPos - a.BallPos).sqrMagnitude > 400f;
            Vector3 target = jump ? b.BallPos : Vector3.Lerp(a.BallPos, b.BallPos, t);
            bool snap = !_ballPlaced || b.BallEpoch != _ballEpoch || (jump && (target - _ballRoot.position).sqrMagnitude > 1f);

            if (snap)
            {
                _trail.emitting = false;
                _ballRoot.position = target;
                _trail.Clear();
            }
            else
            {
                if (dt > 0f && b.Has(StateFlags.BallMoving))
                {
                    Vector3 v = (target - _lastBallPos) / dt;
                    v.y = 0f;
                    if (v.sqrMagnitude > 0.01f)
                        _ballMesh.Rotate(Vector3.Cross(Vector3.up, v.normalized), v.magnitude / _ballRadius * Mathf.Rad2Deg * dt, Space.World);
                }
                _ballRoot.position = target;
                _trail.emitting = b.Has(StateFlags.BallTrail);
            }

            _ballPlaced = true;
            _ballEpoch = b.BallEpoch;
            _lastBallPos = target;
            // Right at its owner's feet the name tag already says whose ball it is.
            bool nearOwner = (target - transform.position).sqrMagnitude < 16f;
            _ballLabel.gameObject.SetActive(ModConfig.ShowBallLabels.Value && !nearOwner);
            BallTurnIndicators.UpdateRing(_turnRing, target, !b.Has(StateFlags.BallMoving) && !b.ShotInProgress, _player.Info.Id);
            _ballLabel.color = BallTurnIndicators.ShowMarkers && !BallTurnIndicators.IsActive(_player.Info.Id)
                ? BallTurnIndicators.WaitingColor : Color.Lerp(_player.Info.Color, Color.white, 0.5f);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;
            _visible = visible;
            _scaler.gameObject.SetActive(visible);
            _nameTag.gameObject.SetActive(visible && ModConfig.ShowNameTags.Value);
            if (!visible)
                _hasLastPos = false;
        }

        private void SetBallVisible(bool visible)
        {
            if (_ballRoot.gameObject.activeSelf != visible)
                _ballRoot.gameObject.SetActive(visible);
        }

        /// <summary>Called right before each camera renders, so labels face whichever view is drawing them.</summary>
        public void FaceCamera(Camera cam)
        {
            if (_visible && _nameTag.gameObject.activeInHierarchy)
                Billboard(_nameTag.transform, cam, 10f, 1f, 8f, 0f);
            if (_ballLabel.gameObject.activeInHierarchy)
                Billboard(_ballLabel.transform, cam, 6f, 1f, 60f, 0.18f);
        }

        private static void Billboard(Transform label, Camera cam, float referenceDistance, float minScale, float maxScale, float liftPerScale)
        {
            Transform ct = cam.transform;
            float scale = Mathf.Clamp(Vector3.Distance(label.parent.position, ct.position) / referenceDistance, minScale, maxScale);
            label.localScale = Vector3.one * scale;
            if (liftPerScale > 0f)
                label.localPosition = new Vector3(0f, 0.12f + liftPerScale * scale, 0f);
            label.rotation = Quaternion.LookRotation(ct.forward, ct.up);
        }

        public Vector3 AvatarPosition => transform.position;
    }
}
