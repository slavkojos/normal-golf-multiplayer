using System.Collections.Generic;

namespace NormalGolfMultiplayer.Net
{
    /// <summary>Stroke-play honours: previous-hole gross scores, preserving previous tee order for ties.</summary>
    internal static class TeeHonours
    {
        internal struct Golfer
        {
            public byte Id;
            public ScoreCard Card;
        }

        public static bool TryOrder(IReadOnlyList<Golfer> golfers, int hole, out List<byte> order)
        {
            var sorted = new List<Golfer>(golfers);
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id)); // agreed first-tee order: host, then join order
            bool ready = hole >= 1 && hole <= ScoreCard.HoleCount;
            for (int previous = 0; ready && previous < hole - 1; previous++)
            {
                bool complete = sorted.TrueForAll(p => p.Card != null && p.Card.Scores[previous] > 0);
                if (!complete)
                {
                    // A mid-round arrival may have no earlier history. Never invent the score
                    // on the immediately previous hole; it must arrive before awarding honours.
                    if (previous == hole - 2) ready = false;
                    continue;
                }
                var priorRank = new Dictionary<byte, int>();
                for (int i = 0; i < sorted.Count; i++) priorRank[sorted[i].Id] = i;
                sorted.Sort((a, b) => {
                    int score = a.Card.Scores[previous].CompareTo(b.Card.Scores[previous]);
                    return score != 0 ? score : priorRank[a.Id].CompareTo(priorRank[b.Id]);
                });
            }
            order = new List<byte>();
            foreach (var golfer in sorted) order.Add(golfer.Id);
            return ready;
        }
    }
}
