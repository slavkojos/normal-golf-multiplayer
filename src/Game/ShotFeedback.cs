using NormalGolfMultiplayer.Net;
using UnityEngine;

namespace NormalGolfMultiplayer.Game
{
    /// <summary>Captures a real launch and reports its settled position before the game advances the hole.</summary>
    internal static class ShotFeedback
    {
        private static uint _id;
        private static int _generation;
        private static Ball _ball;
        private static Vector3 _start, _hole;
        private static ShotResult _result;

        public static void Queue(uint id)
        {
            Cancel();
            _id = id;
            _generation = NetSession.Instance.SessionGeneration;
        }

        public static void Launch(ShotType contact)
        {
            var session = NetSession.Instance;
            if (_id == 0 || session == null || !session.InSession || session.SessionGeneration != _generation) return;
            _ball = HitManager.instance.m_ball;
            _start = Position(_ball);
            bool known = LocalPlayer.TryGetHolePosition(_start, out _hole);
            _result = new ShotResult {
                Id = _id,
                Stroke = (ushort)Mathf.Clamp(_ball.m_currentShot + 1, 1, 1000),
                Contact = (byte)contact,
                Shape = (byte)HitManager.instance.GetSpinType(),
                HasHole = known,
            };
        }

        public static void Finish(Ball ball, bool holed = false)
        {
            var session = NetSession.Instance;
            if (_id == 0 || _ball == null || ball != _ball) return;
            if (session != null && session.InSession && session.SessionGeneration == _generation)
            {
                Vector3 end = Position(ball);
                // Golf's total shot distance is the displacement on the ground, including roll.
                Vector3 travel = end - _start;
                travel.y = 0f;
                _result.Distance = travel.magnitude;
                _result.Holed = holed;
                _result.HasHole |= holed;
                _result.Remaining = !holed && _result.HasHole ? Vector3.Distance(end, _hole) : 0f;
                session.SendShotResult(_result);
                LocalPlayer.ShotPending = false;
            }
            Cancel();
        }

        private static Vector3 Position(Ball ball) => ball.m_rb != null ? ball.m_rb.position : ball.transform.position;

        public static System.Collections.IEnumerator FinishKilledBall(Ball ball)
        {
            uint id = _id;
            // Cup triggers kill the ball immediately before reporting a hole-out. Let that
            // callback publish zero remaining distance first; other terminal shots still report.
            yield return null;
            if (id != 0 && id == _id) Finish(ball);
        }

        public static void Cancel() { _id = 0; _ball = null; }
    }
}
