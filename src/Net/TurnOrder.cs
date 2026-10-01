using System.Collections.Generic;

namespace NormalGolfMultiplayer.Net
{
    /// <summary>The host's deterministic shot order for golfers playing the same hole.</summary>
    internal static class TurnOrder
    {
        internal struct Candidate
        {
            public byte Id;
            public int Shots;
            public bool Holed;
            public bool Settled;
            public float DistanceSquared;
            public int TeeRank;
            public bool AwaitingTee;
        }

        /// <summary>Everyone plays from the tee once before any ball in play takes priority.</summary>
        public static byte NextOpeningShot(IReadOnlyList<Candidate> players)
        {
            Candidate next = default;
            bool found = false;
            foreach (var p in players)
            {
                if (p.Holed || p.Shots != 0)
                    continue;
                if (!found || p.TeeRank < next.TeeRank || (p.TeeRank == next.TeeRank && p.Id < next.Id))
                {
                    next = p;
                    found = true;
                }
            }
            return found && !next.AwaitingTee ? next.Id : (byte)0;
        }

        /// <summary>Once tee shots are done, the farthest resting ball plays, including consecutive turns.</summary>
        public static byte FarthestBall(IReadOnlyList<Candidate> players)
        {
            byte next = 0;
            float farthest = -1f;
            foreach (var p in players)
            {
                if (p.Holed)
                    continue;
                // Never award a turn from an incomplete comparison (including a tee shot still rolling).
                if (p.Shots == 0 || !p.Settled || p.DistanceSquared < 0f ||
                    float.IsNaN(p.DistanceSquared) || float.IsInfinity(p.DistanceSquared))
                    return 0;
                if (p.DistanceSquared > farthest ||
                    (p.DistanceSquared == farthest && (next == 0 || p.Id < next)))
                {
                    next = p.Id;
                    farthest = p.DistanceSquared;
                }
            }
            return next;
        }
    }
}
