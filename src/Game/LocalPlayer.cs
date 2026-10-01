using System;
using System.Collections.Generic;
using ECM2;
using ECM2.Examples.FirstPerson;
using NormalGolfMultiplayer.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NormalGolfMultiplayer.Game
{
    /// <summary>Reads the local player's pose and ball out of the game each network tick.</summary>
    internal static class LocalPlayer
    {
        public const string CourseScene = "Main";
        private static List<Cup> _cups;

        private readonly struct Cup
        {
            public Cup(Transform transform, string id, bool targetOnly)
            {
                Transform = transform;
                Id = id;
                TargetOnly = targetOnly;
            }

            public readonly Transform Transform;
            public readonly string Id;
            public readonly bool TargetOnly;
        }

        /// <summary>Incremented whenever the game teleports/resets the ball (see GamePatches).</summary>
        public static byte BallEpoch;
        public static bool ShotPending;

        /// <summary>True for one frame after a scene load, so the cup cache is rebuilt for the new course.</summary>
        public static void OnSceneChanged() => ResetCups();

        public static bool InWorld =>
            SceneManager.GetActiveScene().name == CourseScene &&
            MoveAndHitController.instance != null &&
            HitManager.instance != null &&
            HitManager.instance.m_ball != null;

        public static PlayerState Capture()
        {
            var s = new PlayerState();
            if (!InWorld)
                return s;

            var mhc = MoveAndHitController.instance;
            s.Flags |= StateFlags.InWorld;

            if (mhc.m_mode == ControlMode.Golf)
            {
                s.Flags |= StateFlags.Golfing;
                s.Pos = mhc.m_golferHolder.position;
                s.Yaw = mhc.m_golferHolder.eulerAngles.y;
                s.Pitch = 25f; // looking down at the ball
            }
            else
            {
                Character ch = mhc.m_fpsCharacter;
                s.Pos = ch.GetPosition();
                s.Yaw = ch.GetRotation().eulerAngles.y;
                if (ch is FirstPersonCharacter fpc)
                    s.Pitch = fpc._cameraPitch;
                if (ch.IsCrouched())
                    s.Flags |= StateFlags.Crouched;
            }

            s.Club = CurrentClub();

            Ball ball = HitManager.instance.m_ball;
            s.BallPos = ball.m_rb != null ? ball.m_rb.position : ball.transform.position;
            if (ball.gameObject.activeInHierarchy && ball.m_meshRenderer != null && ball.m_meshRenderer.enabled)
                s.Flags |= StateFlags.BallVisible;
            if (ball.m_trail != null && ball.m_trail.emitting)
                s.Flags |= StateFlags.BallTrail;
            if (ball.m_rb != null && !ball.m_rb.isKinematic)
                s.Flags |= StateFlags.BallMoving;
            s.BallEpoch = BallEpoch;
            s.ShotInProgress = ShotPending;
            if (TryGetHolePosition(s.BallPos, out var holePos))
            {
                s.HolePos = holePos;
                s.Flags |= StateFlags.HoleKnown;
            }

            var save = SaveManager.instance;
            if (save != null && save.m_gamemodeState != null && save.m_gamemodeState.mode == GameMode.JustGolf)
                s.Flags |= StateFlags.PlayNine;

            return s;
        }

        /// <summary>
        /// The cup of the Front Nine hole currently in play. The distance overlay's pick just follows the
        /// camera to the nearest challenge cup (a minigolf target counts as readily as hole 1), so the mod
        /// resolves the real hole from the game's active round save. Only free play without a specific
        /// challenge target falls back to the nearest cup.
        /// </summary>
        internal static bool TryGetHolePosition(Vector3 ballPos, out Vector3 holePos)
        {
            holePos = Vector3.zero;
            if (!CollectCups())
                return false;

            // Read the game's round directly. Scene-load and scorecard callbacks can arrive after the
            // first network tick; using the mod's card here used to silently select a nearby minigolf cup.
            var save = SaveManager.instance;
            if (save != null && save.m_run.isInLMUGC)
            {
                var run = save.m_run;
                int hole = 0;
                if (run.lmugcScore != null)
                    for (int i = 0; i < Math.Min(ScoreCard.HoleCount, run.lmugcScore.Length); i++)
                        if (run.lmugcScore[i] == 0) { hole = i + 1; break; }
                string wanted = "hole" + hole;
                foreach (var cup in _cups)
                    if (cup.Transform != null && string.Equals(cup.Id, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        holePos = cup.Transform.position;
                        return true;
                    }
                return false; // Missing round target is unknown, never another hole.
            }

            string target = ChallengeSystem.instance?.GetCurrentChallenge()?.m_finishDistanceHoleID;
            if (!string.IsNullOrEmpty(target))
                foreach (var cup in _cups)
                    if (cup.Transform != null && string.Equals(cup.Id, target, StringComparison.OrdinalIgnoreCase))
                    {
                        holePos = cup.Transform.position;
                        return true;
                    }

            float nearest = float.MaxValue;
            Transform best = null;
            foreach (var cup in _cups)
            {
                if (cup.Transform == null || cup.TargetOnly)
                    continue; // target-only magnets are challenge markers, not holes
                float distance = (cup.Transform.position - ballPos).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    best = cup.Transform;
                }
            }
            if (best == null)
                return false;
            holePos = best.position;
            return true;
        }

        /// <summary>Caches the scene's cup magnets directly, including inactive course holes.</summary>
        private static bool CollectCups()
        {
            if (_cups != null && _cups.Count > 0)
                return true;
            var found = new List<Cup>();
            foreach (var magnet in UnityEngine.Object.FindObjectsByType<HoleMagnet>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (magnet == null)
                    continue;
                try
                {
                    found.Add(new Cup(magnet.transform,
                        magnet.m_id ?? magnet.name, magnet.m_targetOnly));
                }
                catch (Exception)
                {
                    // magnet still initialising
                }
            }
            if (found.Count == 0)
                return false;
            _cups = found;
            return true;
        }

        /// <summary>The scene changed, so the cached cups belong to another course (or none).</summary>
        internal static void ResetCups()
        {
            _cups = null;
            ShotPending = false;
        }

        /// <summary>Teleporting mid-swing or during a cutscene would confuse the game, so only while freely walking.</summary>
        public static bool CanTeleport =>
            InWorld && MoveAndHitController.instance.m_mode == ControlMode.Walk && !MoveAndHitController.instance.m_moveLocked;

        /// <summary>Puts us a couple of metres from <paramref name="target"/> (on our side of it), facing it.</summary>
        public static void TeleportNear(Vector3 target)
        {
            if (!CanTeleport)
                return;
            Character ch = MoveAndHitController.instance.m_fpsCharacter;
            Vector3 away = ch.GetPosition() - target;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.back;
            Vector3 pos = target + away.normalized * 2.2f + Vector3.up * 0.3f; // a little high; gravity settles us
            ch.TeleportPosition(pos, interpolating: false, updateGround: true);
            Vector3 look = target - pos;
            look.y = 0f;
            ch.TeleportRotation(Quaternion.LookRotation(look), interpolating: false);
        }

        public static byte CurrentClub()
        {
            try
            {
                Club club = PanelManager.instance?.m_clubSwingPanel?.m_currentClub;
                if (club != null && Enum.TryParse(club.clubName, true, out Clubs c))
                    return (byte)c;
            }
            catch (NullReferenceException)
            {
                // panels not built yet
            }
            return (byte)Clubs.Iron;
        }

        public static byte CurrentBallLook()
        {
            var save = SaveManager.instance;
            if (save == null)
                return 0;
            return (byte)Mathf.Clamp(save.m_unlocks.selectedBall, 0, 254);
        }
    }
}
