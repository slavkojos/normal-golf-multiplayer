using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using NormalGolfMultiplayer.Net;
using NormalGolfMultiplayer.UI;
using UnityEngine;

namespace NormalGolfMultiplayer.Game
{
    internal static class GamePatches
    {
        private static float _lastHoledTime = -10f;
        private static bool _launchAuthorized;
        private static int _launchGeneration;

        /// <summary>
        /// Patches are applied one by one so a game update that renames a single method only disables
        /// that one feature (with a warning) instead of stopping the whole mod from loading.
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            Patch(harmony, typeof(HitManager), nameof(HitManager.ResetBall), postfix: nameof(AfterResetBall));
            Patch(harmony, typeof(HitManager), nameof(HitManager.HitBall), prefix: nameof(BeforeBallLaunch));
            Patch(harmony, typeof(HitSequeneceManager), nameof(HitSequeneceManager.HitSequence), prefix: nameof(BeforeHitSequence));
            Patch(harmony, typeof(HitSequeneceManager), nameof(HitSequeneceManager.CompleteSequence),
                prefix: nameof(BeforeShotComplete), postfix: nameof(AfterShotComplete));
            Patch(harmony, typeof(HitSequeneceManager), nameof(HitSequeneceManager.ShowBallInHoleFeedback), prefix: nameof(BeforeHoleFeedback));
            Patch(harmony, typeof(Ball), nameof(Ball.KillBall), postfix: nameof(AfterBallKilled));
            Patch(harmony, typeof(LMUGC), nameof(LMUGC.StartChallenge), postfix: nameof(AfterStartChallenge));
            Patch(harmony, typeof(LMUGC), nameof(LMUGC.CompleteHole), prefix: nameof(BeforeCompleteHole));
            Patch(harmony, typeof(SaveManager), nameof(SaveManager.AddScoreToCard), postfix: nameof(AfterAddScoreToCard));
            Patch(harmony, typeof(ClubSwing), "Update", prefix: nameof(SkipWhileUiCapturesInput));
            Patch(harmony, typeof(ClubSwingFPS), "Update", prefix: nameof(SkipWhileUiCapturesInput));
            Patch(harmony, typeof(ClubSwing), "DetectShot", prefix: nameof(CanDetectShot));
            Patch(harmony, typeof(ClubSwingFPS), "DetectShot", prefix: nameof(CanDetectShot));
            Patch(harmony, typeof(ClubSwing), "HandleShot", prefix: nameof(CanDetectShot));
            Patch(harmony, typeof(HitSequeneceManager), "RotateCamera", prefix: nameof(SkipWhileUiCapturesInput));
            Patch(harmony, typeof(WindPanel), "Update", prefix: nameof(WindSync.BeforeVisuals), patchType: typeof(WindSync));
            Patch(harmony, typeof(WindPanel), "LateUpdate", prefix: nameof(WindSync.BeforeLateUpdate),
                postfix: nameof(WindSync.AfterLateUpdate), patchType: typeof(WindSync));
            Patch(harmony, typeof(Ball), "FixedUpdate", prefix: nameof(WindSync.BeforePhysics), patchType: typeof(WindSync));
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null, Type patchType = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, method);
                if (target == null)
                    throw new MissingMethodException(type.Name, method);
                harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(patchType ?? typeof(GamePatches), prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(patchType ?? typeof(GamePatches), postfix) : null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not patch {type.Name}.{method} (game updated?): {e.Message}");
            }
        }

        /// <summary>A reset teleports the ball to the player's feet; tell receivers to snap rather than slide it across the map.</summary>
        private static void AfterResetBall()
        {
            LocalPlayer.BallEpoch++;
            LocalPlayer.ShotPending = false;
            _launchAuthorized = false;
            ShotFeedback.Cancel();
        }

        /// <summary>HitSequence is the coroutine every real swing starts (ClubSwing.HandleShot).</summary>
        private static bool BeforeHitSequence(float power, bool isMiss, ShotType shot, ref IEnumerator __result)
        {
            var session = NetSession.Instance;
            if (session != null && !session.CanLocalShoot)
            {
                session.NotifyShotBlocked();
                __result = EmptyShot(); // StartCoroutine must receive a valid, harmless enumerator.
                return false;
            }
            if (isMiss || session == null)
                return true;
            _launchAuthorized = session.InSession;
            _launchGeneration = session.SessionGeneration;
            LocalPlayer.ShotPending = true;
            uint shotId = session.SendShot(new ShotEvent
            {
                Club = LocalPlayer.CurrentClub(),
                Power = power,
                ShotType = (byte)shot,
            });
            ShotFeedback.Queue(shotId);
            return true;
        }

        private static IEnumerator EmptyShot() { yield break; }
        private static bool CanDetectShot() => !MultiplayerUI.CapturingInput &&
            (NetSession.Instance == null || NetSession.Instance.CanLocalShoot);
        private static bool BeforeBallLaunch(bool isMiss, ShotType shot)
        {
            var session = NetSession.Instance;
            if (isMiss || session == null || session.Mode == SessionMode.Offline)
                return true;
            // The original coroutine launches after its video delay. A granted shot remains valid
            // after the host awards the next tee turn, but cannot authorize a second launch.
            if (_launchAuthorized && LocalPlayer.ShotPending && session.InSession && _launchGeneration == session.SessionGeneration)
            {
                _launchAuthorized = false;
                ShotFeedback.Launch(shot);
                return true;
            }
            session.NotifyShotBlocked();
            return false;
        }

        private static void BeforeShotComplete(Ball ballThatSentMessage) => ShotFeedback.Finish(ballThatSentMessage);

        private static void AfterBallKilled(Ball __instance)
        {
            if (NetSession.Instance != null && NetSession.Instance.InSession)
                NetSession.Instance.StartCoroutine(ShotFeedback.FinishKilledBall(__instance));
        }

        private static void AfterShotComplete(Ball ballThatSentMessage)
        {
            if (HitManager.instance != null && HitManager.instance.m_ball == ballThatSentMessage)
            {
                LocalPlayer.ShotPending = false;
                _launchAuthorized = false;
            }
        }

        /// <summary>Every "ball went in a hole" path (story holes, Play Nine, LMUGC) reports through here.</summary>
        private static void BeforeHoleFeedback(int par, bool showAnyway)
        {
            if (HitManager.instance != null)
                ShotFeedback.Finish(HitManager.instance.m_ball, holed: true);
            if (NetSession.Instance == null || Time.unscaledTime - _lastHoledTime < 2f)
                return;
            _lastHoledTime = Time.unscaledTime;
            LocalPlayer.ShotPending = false;
            _launchAuthorized = false;

            int strokes = 0;
            try
            {
                // Same condition the game uses to decide whether this hole is scored.
                bool scored = showAnyway || SaveManager.instance.m_run.isInLMUGC ||
                              ChallengeSystem.instance.GetCurrentChallenge().m_isMultiShot;
                if (scored)
                    strokes = HitManager.instance.m_ball.m_currentShot + 1;
            }
            catch (NullReferenceException)
            {
                // no active challenge; report an unscored hole
            }

            NetSession.Instance.SendHoled(new HoledEvent
            {
                Strokes = (byte)Mathf.Clamp(strokes, 0, 255),
                Par = (byte)Mathf.Clamp(par, 0, 255),
            });
        }

        /// <summary>The red button at the first tee starts a Front Nine round and clears the scorecard.</summary>
        private static void AfterStartChallenge()
        {
            ScoreTracker.Instance?.BeginRound();
        }

        /// <summary>
        /// Runs before the game records the hole, because on the ninth hole the game clears the whole
        /// scorecard moments later. `id` is a hole target name ("Hole4...") or a plain hole number for skips.
        /// </summary>
        private static void BeforeCompleteHole(string id, int score)
        {
            var match = System.Text.RegularExpressions.Regex.Match(id ?? "", "\\d+");
            if (match.Success && int.TryParse(match.Value, out int hole))
                ScoreTracker.Instance?.RecordHole(hole, score);
        }

        /// <summary>Only called when a Play Nine round is completed, while the final card is still intact.</summary>
        private static void AfterAddScoreToCard()
        {
            ScoreTracker.Instance?.FinishRound();
        }

        // The swing panel reads the mouse through legacy Input, which PlayerInput.DeactivateInput() can't block.
        // Skip it while our menu/chat owns the mouse so clicking a button can't swing the club.
        private static bool SkipWhileUiCapturesInput() => !MultiplayerUI.CapturingInput;
    }
}
