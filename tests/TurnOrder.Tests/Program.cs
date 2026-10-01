using System;
using System.Reflection;
using NormalGolfMultiplayer.Net;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void TeeHonourChecks()
    {
        Action<bool, string> check = (condition, name) => { if (!condition) throw new Exception(name); _checks++; };
        var cards = new[] { new ScoreCard(), new ScoreCard(), new ScoreCard() };
        foreach (var card in cards) card.Reset(1, true);
        var golfers = new[] {
            new TeeHonours.Golfer { Id = 3, Card = cards[2] },
            new TeeHonours.Golfer { Id = 1, Card = cards[0] },
            new TeeHonours.Golfer { Id = 2, Card = cards[1] },
        };
        check(TeeHonours.TryOrder(golfers, 1, out var order) && string.Join(",", order) == "1,2,3", "First tee agreement changed");
        cards[0].Scores[0] = 5; cards[1].Scores[0] = 3; cards[2].Scores[0] = 4;
        check(TeeHonours.TryOrder(golfers, 2, out order) && string.Join(",", order) == "2,3,1", "Lowest previous-hole gross score does not get honour");
        foreach (var card in cards) card.Scores[1] = 4;
        check(TeeHonours.TryOrder(golfers, 3, out order) && string.Join(",", order) == "2,3,1", "Tie lost the previous tee order");
        cards[0].Scores[2] = 3; cards[1].Scores[2] = 5; cards[2].Scores[2] = 5;
        check(TeeHonours.TryOrder(golfers, 4, out order) && string.Join(",", order) == "1,2,3", "Partial tie lost the previous tee order");
        cards[0].Scores[0] = 15;
        check(TeeHonours.TryOrder(golfers, 4, out order) && order[0] == 1, "Cumulative score replaced previous-hole score");
        cards[1].Scores[2] = 0;
        check(!TeeHonours.TryOrder(golfers, 4, out order), "Missing previous-hole score invented honours");
        cards[1].Scores[2] = 5;
        check(TeeHonours.TryOrder(new[] { golfers[0], golfers[1] }, 4, out order) && string.Join(",", order) == "1,3", "Disconnect changed the remaining relative order");
        foreach (var card in cards) card.Reset(2, true);
        check(TeeHonours.TryOrder(golfers, 1, out order) && string.Join(",", order) == "1,2,3", "Restart kept previous-round honours");

        var candidates = new[] {
            new TurnOrder.Candidate { Id = 1, TeeRank = 2 },
            new TurnOrder.Candidate { Id = 2, TeeRank = 0 },
            new TurnOrder.Candidate { Id = 3, TeeRank = 1 },
        };
        Equal(2, TurnOrder.NextOpeningShot(candidates), "Honours overrides host ID");
        candidates[1].Shots = 1; Equal(3, TurnOrder.NextOpeningShot(candidates), "Second-best tee shot");
        candidates[2].Shots = 1; Equal(1, TurnOrder.NextOpeningShot(candidates), "Worst score tees last across ID wrap");
        candidates[1].Shots = 0; candidates[1].AwaitingTee = true;
        Equal(0, TurnOrder.NextOpeningShot(candidates), "Arriving early stole the honour");

        // Exercise actual host transitions while reliable cards and physical cups arrive separately.
        var session = Session();
        for (byte id = 1; id <= 3; id++) Call(session, "TrackTurnScore", id, cards[id - 1]);
        Shot(session, 1); Shot(session, 2); Shot(session, 3); Land(session, 3, 2, 20);
        for (byte id = 1; id <= 3; id++) Call(session, "AfterHoled", id);
        Equal(0, session.ActiveTurnId, "All holed players should wait");
        cards[0].Scores[0] = 5; cards[0].CurrentHole = 2;
        Call(session, "TrackTurnScore", (byte)1, cards[0]);
        State(session, 1, 3, 650, cup: 500);
        Equal(0, session.ActiveTurnId, "Early new tee arrival bypassed incomplete scores");
        cards[1].Scores[0] = 3; cards[1].CurrentHole = 2;
        cards[2].Scores[0] = 4; cards[2].CurrentHole = 2;
        Call(session, "TrackTurnScore", (byte)2, cards[1]); Call(session, "TrackTurnScore", (byte)3, cards[2]);
        State(session, 3, 3, 650, cup: 500);
        Equal(0, session.ActiveTurnId, "Second-best arrival bypassed winner walking to tee");
        Call(session, "TrackTurnState", (byte)2, new PlayerState { Seq = 3,
            Flags = StateFlags.InWorld | StateFlags.HoleKnown, BallPos = new Vector3(650, 0, 0), HolePos = new Vector3(500, 0, 0) });
        Equal(0, session.ActiveTurnId, "Hidden holed ball awarded next tee turn");
        State(session, 2, 4, 650, cup: 500); Equal(2, session.ActiveTurnId, "Previous-hole winner did not tee first");
        Time.unscaledTime += 6;
        State(session, 1, 4, 650, cup: 500); State(session, 3, 4, 650, cup: 500);
        Call(session, "ChooseTurn", (byte)0, new Vector3(500, 0, 0), new Vector3());
        Equal(0, session.ActiveTurnId, "Slow/loading honour holder lost priority to a fresh arrival");
        State(session, 2, 5, 650, cup: 500); Equal(2, session.ActiveTurnId, "Loading honour holder did not resume at the tee");
        Shot(session, 2); Equal(3, session.ActiveTurnId, "Host did not relay second-best tee turn");
        Shot(session, 3); Equal(1, session.ActiveTurnId, "Host did not relay last tee turn");
        Shot(session, 1); Equal(0, session.ActiveTurnId, "Last tee shot did not wait for landing");
        Time.unscaledTime += 2;
        State(session, 1, 6, 540, cup: 500); State(session, 2, 6, 590, cup: 500); State(session, 3, 6, 510, cup: 500);
        Call(session, "FinishPendingShot", Time.unscaledTime);
        Equal(2, session.ActiveTurnId, "Honours replaced farthest-ball order after tee shots");
        for (byte id = 1; id <= 3; id++) Call(session, "AfterHoled", id);
        for (byte id = 1; id <= 3; id++) {
            cards[id - 1].Scores[1] = 3; cards[id - 1].CurrentHole = 3;
            Call(session, "TrackTurnScore", id, cards[id - 1]);
        }
        for (byte id = 1; id <= 3; id++) State(session, id, 7, 1150, cup: 1000);
        Equal(2, session.ActiveTurnId, "Next-hole tie used last shooter rather than previous tee order");
        session.Players.Remove(2); Call(session, "ChooseTurn", (byte)0, new Vector3(1000, 0, 0), new Vector3());
        Equal(3, session.ActiveTurnId, "Disconnected honour holder stalled next tee");
        for (byte id = 1; id <= 3; id++) { cards[id - 1].Reset(3, true); Call(session, "TrackTurnScore", id, cards[id - 1]); }
        State(session, 1, 8, 150); State(session, 3, 8, 150);
        Equal(1, session.ActiveTurnId, "New match did not restore agreed first-tee order");

        session = Session();
        for (byte id = 1; id <= 3; id++) Call(session, "TrackTurnState", id, new PlayerState {
            Seq = 2, Flags = StateFlags.InWorld | StateFlags.PlayNine | StateFlags.HoleKnown | StateFlags.BallVisible,
            BallPos = new Vector3(650, 0, 0), HolePos = new Vector3(500, 0, 0),
        });
        Call(session, "ChooseTurn", (byte)0, new Vector3(500, 0, 0), new Vector3());
        Equal(0, session.ActiveTurnId, "Resumed round awarded turn before any scorecards arrived");
        foreach (var card in cards) { card.Reset(4, true); card.CurrentHole = 2; }
        cards[0].Scores[0] = 5; cards[1].Scores[0] = 3; cards[2].Scores[0] = 4;
        Call(session, "TrackTurnScore", (byte)1, cards[0]); State(session, 1, 3, 650, cup: 500);
        Equal(0, session.ActiveTurnId, "Missing remote scorecard did not delay honours");
        Call(session, "TrackTurnScore", (byte)2, cards[1]); Call(session, "TrackTurnScore", (byte)3, cards[2]);
        State(session, 2, 3, 650, cup: 500); State(session, 3, 3, 650, cup: 500);
        Equal(2, session.ActiveTurnId, "Imported scorecards did not establish resumed honours");
    }
    private static void ShotLifecycleChecks()
    {
        Action<bool, string> check = (condition, name) => { if (!condition) throw new Exception(name); _checks++; };
        var session = Session(); Call(session, "Awake");
        typeof(NetSession).GetProperty("LocalId").SetValue(session, (byte)1);
        var ball = HitManager.instance.m_ball = new Ball { m_currentShot = 2 };
        NormalGolfMultiplayer.Game.LocalPlayer.Hole = new Vector3(3, 7, 5);
        var trackerType = typeof(NormalGolfMultiplayer.Game.ShotFeedback);
        Func<ShotResult> outcome = () => (ShotResult)trackerType.GetField("_result", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        Func<uint> pending = () => (uint)trackerType.GetField("_id", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        Action queue = () => {
            Call(session, "SetActiveTurn", (byte)1);
            NormalGolfMultiplayer.Game.ShotFeedback.Queue(session.SendShot(new ShotEvent { ShotType = 8 }));
        };
        queue(); HitManager.instance.TestSpin = SpinType.Fade;
        NormalGolfMultiplayer.Game.ShotFeedback.Launch(ShotType.Fat);
        ball.m_rb.position = new Vector3(3, 7, 4);
        NormalGolfMultiplayer.Game.ShotFeedback.Finish(new Ball());
        check(pending() != 0, "Another ball completed the local shot");
        NormalGolfMultiplayer.Game.ShotFeedback.Finish(ball);
        check(outcome().Stroke == 3 && outcome().Quality == "Fat · Fade", "Game stroke/contact/shape capture incorrect");
        check(outcome().Distance == 5 && outcome().Remaining == 1, "Ground distance or actual cup distance incorrect");
        check(pending() == 0, "Completion did not clear shot feedback");
        NormalGolfMultiplayer.Game.ShotFeedback.Finish(ball, true);
        check(!outcome().Holed, "Duplicate callback rewrote an already reported result");

        queue(); NormalGolfMultiplayer.Game.ShotFeedback.Launch(ShotType.Thin);
        var killed = NormalGolfMultiplayer.Game.ShotFeedback.FinishKilledBall(ball);
        check(killed.MoveNext() && pending() != 0, "Ball kill did not wait for cup callback");
        NormalGolfMultiplayer.Game.ShotFeedback.Finish(ball, true);
        killed.MoveNext();
        check(outcome().Holed && outcome().Remaining == 0 && pending() == 0, "Cup result lost to kill callback");
        queue(); NormalGolfMultiplayer.Game.ShotFeedback.Launch(ShotType.Good);
        NormalGolfMultiplayer.Game.LocalPlayer.ShotPending = true;
        killed = NormalGolfMultiplayer.Game.ShotFeedback.FinishKilledBall(ball);
        killed.MoveNext(); killed.MoveNext();
        check(pending() == 0 && !outcome().Holed, "Other terminal shots were not reported");
        check(!NormalGolfMultiplayer.Game.LocalPlayer.ShotPending, "Removed ball leaves the golfer stuck in a pending shot");

        queue(); NormalGolfMultiplayer.Game.ShotFeedback.Launch(ShotType.Good);
        killed = NormalGolfMultiplayer.Game.ShotFeedback.FinishKilledBall(ball); killed.MoveNext();
        NormalGolfMultiplayer.Game.ShotFeedback.Cancel(); queue();
        uint nextId = pending(); killed.MoveNext();
        check(pending() == nextId, "Old kill callback finished a subsequent shot");
        NormalGolfMultiplayer.Game.ShotFeedback.Cancel();
        queue(); Call(session, "OnSessionBegan");
        NormalGolfMultiplayer.Game.ShotFeedback.Launch(ShotType.Good);
        check(trackerType.GetField("_ball", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null,
            "Queued feedback survives session change");
        NormalGolfMultiplayer.Game.ShotFeedback.Cancel();
        NormalGolfMultiplayer.Game.LocalPlayer.HasHole = false;
        queue(); NormalGolfMultiplayer.Game.ShotFeedback.Launch(ShotType.Good);
        NormalGolfMultiplayer.Game.ShotFeedback.Finish(ball);
        check(!outcome().HasHole && outcome().Remaining == 0, "Unknown cup incorrectly reported");
        NormalGolfMultiplayer.Game.LocalPlayer.HasHole = true;
    }
    private static void ShotResultChecks()
    {
        Action<bool, string> check = (condition, name) => { if (!condition) throw new Exception(name); _checks++; };
        var session = Session();
        typeof(NetSession).GetProperty("LocalId").SetValue(session, (byte)1);
        uint shotId = session.SendShot(new ShotEvent { Club = 2, Power = 10, ShotType = 8 });
        var result = new ShotResult { Id = shotId, Stroke = 3, Contact = 8, Shape = 16,
            Distance = 120.5f, Remaining = 7.25f, HasHole = true };
        check(result.Valid && result.Quality == "Fat · Fade", "Contact and shape are not independently reported");
        var writer = new LiteNetLib.Utils.NetDataWriter(); result.Write(writer);
        var decoded = ShotResult.Read(new LiteNetLib.Utils.NetDataReader(writer.CopyData()));
        check(decoded.Id == shotId && decoded.Stroke == 3 && decoded.Quality == result.Quality &&
            decoded.Distance == 120.5f && decoded.Remaining == 7.25f && decoded.HasHole && !decoded.Holed,
            "Shot result wire round trip failed");
        check(decoded.Describe("<Bob>").Contains("Bob · Stroke 3\nFat · Fade\n120.5 m travelled · 7.3 m to hole"),
            "Shot summary is incomplete or allows rich text in a name");
        check(session.ActiveTurnId != 1, "Test did not advance the turn before the result");
        var accept = typeof(NetSession).GetMethod("AcceptShotResult", BindingFlags.Instance | BindingFlags.NonPublic);
        check((bool)accept.Invoke(session, new object[] { (byte)1, decoded }), "Result blocked after turn advanced");
        check(!(bool)accept.Invoke(session, new object[] { (byte)1, decoded }), "Duplicate result accepted");
        result.Id++;
        check(!(bool)accept.Invoke(session, new object[] { (byte)1, result }), "Unmatched result accepted");
        check(!(bool)accept.Invoke(session, new object[] { (byte)0, result }), "Unknown golfer result accepted");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f }) {
            var bad = decoded; bad.Distance = invalid; check(!bad.Valid, "Invalid shot distance accepted");
            bad = decoded; bad.Remaining = invalid; check(!bad.Valid, "Invalid hole distance accepted");
        }
        var zero = decoded; zero.Stroke = 0; check(!zero.Valid, "Stroke zero accepted");
        zero = decoded; zero.Contact = 255; check(!zero.Valid, "Invalid contact accepted");
        zero = decoded; zero.Shape = 255; check(!zero.Valid, "Invalid shape accepted");
        foreach (var pair in new[] { (1, "Straight"), (2, "Slice"), (4, "Hook"), (8, "Draw"), (16, "Fade") }) {
            result.Contact = 4; result.Shape = (byte)pair.Item1; check(result.Quality == pair.Item2, "Wrong shot shape");
        }
        result.Contact = 2; result.Shape = 1; check(result.Quality == "Thin · Straight", "Thin contact lost");
        result.HasHole = false; check(result.Describe("Bob").Contains("Hole distance unavailable"), "Missing cup fabricated a distance");
        result.Holed = true; result.HasHole = true; result.Remaining = 0;
        check(result.Valid && result.Describe("Bob").Contains("In the hole!"), "Hole-out result wrong");
        result.Remaining = 1; check(!result.Valid, "Holed ball has nonzero remaining distance");

        // Exercise the real receiving path: accepted shot, invalid result, valid result, duplicate.
        session = new NetSession();
        typeof(NetSession).GetProperty("Mode").SetValue(session, SessionMode.Connected);
        typeof(NetSession).GetProperty("LocalId").SetValue(session, (byte)2);
        session.Players[1] = new PlayerInfo { Id = 1 };
        var shot = new ShotEvent { Id = 10, Club = 2, Power = 20, ShotType = 4 };
        writer.Reset(); shot.Write(writer);
        Call(session, "HandleOnClient", Msg.Shot, (byte)1, new LiteNetLib.Utils.NetDataReader(writer.CopyData()));
        int received = 0; session.ShotResultReceived += (id, outcome) => { check(id == 1 && outcome.Stroke == 2, "Wrong result golfer/stroke"); received++; };
        Action<ShotResult> receive = value => { writer.Reset(); value.Write(writer);
            Call(session, "HandleOnClient", Msg.ShotResult, (byte)1, new LiteNetLib.Utils.NetDataReader(writer.CopyData())); };
        result = new ShotResult { Id = 10, Stroke = 2, Contact = 4, Shape = 4, Distance = 90, Remaining = 4, HasHole = true };
        var invalidResult = result; invalidResult.Distance = float.NaN; receive(invalidResult);
        check(received == 0, "Invalid result emitted notification");
        receive(result); receive(result); check(received == 1, "Receiver did not publish exactly one result");
        writer.Reset(); shot.Id = 11; shot.Write(writer);
        Call(session, "HandleOnClient", Msg.Shot, (byte)1, new LiteNetLib.Utils.NetDataReader(writer.CopyData()));
        Call(session, "Shutdown", (object)null); result.Id = 11; receive(result);
        check(received == 1, "Result survived session exit");
    }
    private static void ShotPermissionChecks()
    {
        Action<bool, string> check = (condition, name) => { if (!condition) throw new Exception(name); _checks++; };
        var session = new NetSession();
        check(session.CanLocalShoot, "Offline swing blocked");
        typeof(NetSession).GetProperty("Mode").SetValue(session, SessionMode.Connecting);
        check(!session.CanLocalShoot, "Connecting golfer allowed to shoot");
        session = Session();
        typeof(NetSession).GetProperty("LocalId").SetValue(session, (byte)1);
        check(session.CanLocalShoot, "Active golfer blocked");
        check(!session.CanShoot(2), "Waiting golfer allowed to shoot");
        check(!session.CanShoot(0), "Unknown golfer allowed to shoot");
        NormalGolfMultiplayer.Game.LocalPlayer.ShotPending = true;
        check(!session.CanLocalShoot, "Second swing during video allowed");
        NormalGolfMultiplayer.Game.LocalPlayer.ShotPending = false;
        Call(session, "SetActiveTurn", (byte)0);
        check(!session.CanShoot(1) && !session.CanShoot(2), "Ball-settle wait allows swing");
        Call(session, "SetActiveTurn", (byte)2);
        ushort seq = (ushort)typeof(NetSession).GetField("_seq", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
        session.SendShot(default);
        check((ushort)typeof(NetSession).GetField("_seq", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session) == seq,
            "Blocked local shot changed network sequence");
        check(session.ActiveTurnId == 2, "Blocked local shot changed turn");
        session.Players.Remove(2); session.Players.Remove(3);
        check(session.CanShoot(1), "Solo host cannot shoot");
        NormalGolfMultiplayer.Game.LocalPlayer.ShotPending = true;
        int generation = session.SessionGeneration;
        Call(session, "Shutdown", (object)null);
        check(!NormalGolfMultiplayer.Game.LocalPlayer.ShotPending && session.SessionGeneration != generation,
            "Old shot authorization survives session exit");
        check(session.CanLocalShoot, "Leaving session did not restore offline play");
    }
    private static void WindChecks()
    {
        var session = new NetSession();
        typeof(NetSession).GetProperty("Mode").SetValue(session, SessionMode.Connected);
        var wind = new WindState { Seq = 65535, X = -2.75f, Y = 4.5f, ForceMultiplier = 7f, CalmSeconds = 22f, Locked = true };
        Action<WindState, byte> receive = (value, id) => {
            var writer = new LiteNetLib.Utils.NetDataWriter();
            value.Write(writer);
            Call(session, "HandleOnClient", Msg.Wind, id, new LiteNetLib.Utils.NetDataReader(writer.CopyData()));
        };
        receive(wind, Protocol.HostId);
        if (!session.HasWind || session.CurrentWind.X != wind.X || session.CurrentWind.Y != wind.Y ||
            session.CurrentWind.ForceMultiplier != 7f || session.CurrentWind.CalmSeconds != 22f || !session.CurrentWind.Locked)
            throw new Exception("Wind packet round trip failed");
        _checks++;
        wind.Seq = 0; wind.X = 3f; receive(wind, Protocol.HostId);
        if (session.CurrentWind.X != 3f) throw new Exception("Wind sequence wrap failed");
        _checks++;
        wind.Seq = 65535; wind.X = 8f; receive(wind, Protocol.HostId);
        if (session.CurrentWind.X != 3f) throw new Exception("Old wind accepted");
        _checks++;
        wind.Seq = 1; wind.X = float.NaN; receive(wind, Protocol.HostId);
        if (session.CurrentWind.X != 3f) throw new Exception("Invalid wind accepted");
        _checks++;
        wind.X = 9f; receive(wind, 2);
        if (session.CurrentWind.X != 3f) throw new Exception("Client wind accepted");
        _checks++;
        Call(session, "Shutdown", (object)null);
        if (session.HasWind) throw new Exception("Wind retained after leave");
        _checks++;
    }
    private static void Call(NetSession s, string name, params object[] args) =>
        typeof(NetSession).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, args);
    private static void Equal(byte expected, byte actual, string name)
    {
        if (expected != actual) throw new Exception($"{name}: expected {expected}, got {actual}");
        _checks++;
    }
    private static NetSession Session()
    {
        Time.unscaledTime = 10f;
        var s = new NetSession();
        if (s.Host(0) == null) throw new Exception("Invalid port accepted");
        // No socket or Unity player is required to exercise host state transitions.
        typeof(NetSession).GetProperty("Mode").SetValue(s, SessionMode.Hosting);
        Call(s, "CreateManager");
        for (byte id = 1; id <= 3; id++) s.Players[id] = new PlayerInfo { Id = id };
        for (byte id = 1; id <= 3; id++) State(s, id, 1, 150f);
        return s;
    }
    private static void State(NetSession s, byte id, ushort seq, float distance, bool moving = false, float cup = 0f)
    {
        Call(s, "TrackTurnState", id, new PlayerState {
            Seq = seq, Flags = StateFlags.InWorld | StateFlags.HoleKnown | StateFlags.BallVisible | (moving ? StateFlags.BallMoving : 0),
            BallPos = new Vector3(distance, 0, 0), HolePos = new Vector3(cup, 0, 0)
        });
    }
    private static void Shot(NetSession s, byte id) => Call(s, "AfterShot", id);
    private static void Land(NetSession s, byte id, ushort seq, float distance)
    {
        Time.unscaledTime += 2f;
        // Refresh all golfers; otherwise the host correctly drops stale participants.
        State(s, 1, seq, id == 1 ? distance : 110);
        State(s, 2, seq, id == 2 ? distance : 70);
        State(s, 3, seq, id == 3 ? distance : 20);
        Call(s, "FinishPendingShot", Time.unscaledTime);
    }
    private static void Main()
    {
        WindChecks();
        ShotPermissionChecks();
        ShotResultChecks();
        ShotLifecycleChecks();
        TeeHonourChecks();
        var s = Session();
        Equal(1, s.ActiveTurnId, "Initial tee order");
        Shot(s, 1); Equal(2, s.ActiveTurnId, "Second opening shot");
        Shot(s, 2); Equal(3, s.ActiveTurnId, "Third opening shot");
        Shot(s, 3); Equal(0, s.ActiveTurnId, "Wait for last tee shot");
        Land(s, 3, 2, 20); Equal(1, s.ActiveTurnId, "Farthest ball across wide spread");
        Shot(s, 1); Land(s, 1, 3, 100); Equal(1, s.ActiveTurnId, "Same golfer remains farthest");
        Shot(s, 1); Land(s, 1, 4, 10); Equal(2, s.ActiveTurnId, "Next farthest after approach");
        State(s, 2, 3, 0); // delayed state must not replace seq 4
        Call(s, "ChooseTurn", (byte)0, new Vector3(), new Vector3());
        Equal(2, s.ActiveTurnId, "Delayed UDP sample cannot change the current distances");
        Shot(s, 2); Land(s, 2, 5, 5); Equal(1, s.ActiveTurnId, "Old UDP state ignored");
        Call(s, "AfterHoled", (byte)1); Equal(3, s.ActiveTurnId, "Holed golfer excluded");
        Call(s, "AfterHoled", (byte)3); Equal(2, s.ActiveTurnId, "Last unholed golfer");
        Call(s, "AfterHoled", (byte)2); Equal(0, s.ActiveTurnId, "All golfers finished");

        s = Session();
        Shot(s, 1); Shot(s, 2); Shot(s, 3);
        Time.unscaledTime += 20;
        State(s, 1, 2, 110); State(s, 2, 2, 70); State(s, 3, 2, 250, moving: true);
        Call(s, "FinishPendingShot", Time.unscaledTime);
        Equal(0, s.ActiveTurnId, "Long drive still moving after 18 seconds");
        Land(s, 3, 3, 250); Equal(3, s.ActiveTurnId, "Long drive lands farthest");

        s = Session();
        Shot(s, 1);
        Time.unscaledTime += 2;
        State(s, 1, 2, 90, cup: 500);
        Time.unscaledTime += 2;
        State(s, 1, 3, 90, cup: 500);
        Shot(s, 2); Shot(s, 3); Land(s, 3, 4, 20);
        Equal(1, s.ActiveTurnId, "Cup drift cannot reset opening status");

        s = Session();
        var card = new ScoreCard(); card.Reset(1, true); card.Strokes = 2;
        Call(s, "TrackTurnScore", (byte)2, card);
        Shot(s, 1); Equal(3, s.ActiveTurnId, "Mid-round golfer already teed off");
        card.Reset(2, true);
        Call(s, "TrackTurnScore", (byte)2, card);
        State(s, 2, 2, 150);
        Shot(s, 3); Equal(2, s.ActiveTurnId, "New round resets opening shot");

        var c = new[] {
            new TurnOrder.Candidate { Id=3, Shots=1, Settled=true, DistanceSquared=100 },
            new TurnOrder.Candidate { Id=1, Shots=1, Settled=true, DistanceSquared=100 },
        };
        Equal(1, TurnOrder.FarthestBall(c), "Distance ties use stable player order");
        Array.Reverse(c); Equal(1, TurnOrder.FarthestBall(c), "Ties independent of input order");
        c[0].DistanceSquared = float.NaN; Equal(0, TurnOrder.FarthestBall(c), "Invalid distance waits");
        c[0].DistanceSquared = 100; c[0].Settled = false;
        Equal(0, TurnOrder.FarthestBall(c), "Any moving ball delays comparison");

        s = Session();
        typeof(NetSession).GetField("_nextSend", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(s, float.MaxValue);
        typeof(NetSession).GetField("_nextInfoCheck", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(s, float.MaxValue);
        Shot(s, 1); Shot(s, 2); Shot(s, 3); Land(s, 3, 2, 20);
        State(s, 2, 3, 180);
        Call(s, "Update"); Equal(2, s.ActiveTurnId, "Retake distance re-evaluated without another swing");

        Shot(s, 2);
        Time.unscaledTime += 2;
        var preparing = new PlayerState { Seq=4, Flags=StateFlags.InWorld | StateFlags.HoleKnown,
            BallPos=new Vector3(180,0,0), ShotInProgress=true };
        Call(s, "TrackTurnState", (byte)2, preparing);
        Call(s, "FinishPendingShot", Time.unscaledTime);
        Equal(0, s.ActiveTurnId, "Swing video cannot be mistaken for a landed shot");

        var w = new LiteNetLib.Utils.NetDataWriter();
        new ShotEvent { Club=4, Power=10, State=preparing }.Write(w);
        var decoded = ShotEvent.Read(new LiteNetLib.Utils.NetDataReader(w.CopyData()));
        if (!decoded.State.ShotInProgress || decoded.State.BallPos.x != 180 || decoded.State.Seq != 4)
            throw new Exception("Reliable shot snapshot lost ball state");
        _checks++;

        Call(s, "RememberState", (byte)2, preparing);
        if (!s.TryGetTurnDistance(2, out var metres) || Math.Abs(metres - 180f) > 0.001f)
            throw new Exception("UI distance disagrees with the host's flag");
        _checks++;
        Console.WriteLine($"Passed {_checks} turn-order, shot-permission, wind and shot-result regression checks.");
    }
}
