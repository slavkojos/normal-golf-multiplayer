using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using NormalGolfMultiplayer.Game;
using UnityEngine;

namespace NormalGolfMultiplayer.Net
{
    internal enum SessionMode
    {
        Offline,
        Hosting,
        Connecting,
        Connected,
    }

    /// <summary>
    /// Listen-server session over LiteNetLib (UDP). The host is player 1 and relays every
    /// client's messages to the other clients, so clients only ever talk to the host.
    /// </summary>
    internal class NetSession : MonoBehaviour
    {
        public static NetSession Instance { get; private set; }

        public SessionMode Mode { get; private set; }
        public byte LocalId { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public string Endpoint { get; private set; } = "";
        public PlayerInfo LocalInfo { get; private set; } = new PlayerInfo();
        public byte ActiveTurnId { get; private set; }
        public Vector3 TurnHole { get; private set; }
        public bool HasTurnHole { get; private set; }
        public bool HasWind { get; private set; }
        public WindState CurrentWind { get; private set; }
        private float _nextWindSend;
        private readonly Dictionary<byte, PlayerState> _latestStates = new Dictionary<byte, PlayerState>();

        /// <summary>Everyone in the session, including us.</summary>
        public readonly Dictionary<byte, PlayerInfo> Players = new Dictionary<byte, PlayerInfo>();

        public event Action<PlayerInfo> PlayerJoined;
        public event Action<PlayerInfo> PlayerLeft;
        public event Action<PlayerInfo> PlayerInfoChanged;
        public event Action<byte, PlayerState> StateReceived;
        public event Action<byte, ShotEvent> ShotReceived;
        public event Action<byte, ShotResult> ShotResultReceived;
        private readonly Dictionary<byte, uint> _resultPending = new Dictionary<byte, uint>();
        private uint _nextShotId;
        public event Action<byte, HoledEvent> HoledReceived;
        public event Action<byte, string> ChatReceived;
        public event Action<byte, ScoreCard> ScoreReceived;
        public event Action<byte> TurnChanged;
        public event Action ShotBlocked;
        public bool CanShoot(byte id) => Mode == SessionMode.Offline ||
            (InSession && id != 0 && (Players.Count < 2 || ActiveTurnId == id));
        public bool CanLocalShoot => CanShoot(LocalId) && (!InSession || !LocalPlayer.ShotPending);
        public int SessionGeneration { get; private set; }
        public void NotifyShotBlocked() => ShotBlocked?.Invoke();
        /// <summary>Raised with a human-readable reason when a session ends or a join fails.</summary>
        public event Action<string> SessionEnded;
        public event Action SessionStarted;

        public bool IsHost => Mode == SessionMode.Hosting;
        public bool InSession => Mode == SessionMode.Hosting || (Mode == SessionMode.Connected && LocalId != 0);
        public int PingMs => _server != null ? _server.RoundTripTime : 0;
        public float SessionTime => (float)(Time.realtimeSinceStartupAsDouble - _timeBase);

        private readonly NetDataWriter _w = new NetDataWriter();
        private readonly Dictionary<int, byte> _peerToPlayer = new Dictionary<int, byte>();
        private readonly Dictionary<byte, NetPeer> _playerToPeer = new Dictionary<byte, NetPeer>();
        private readonly Dictionary<byte, ChatLimiter> _chatLimits = new Dictionary<byte, ChatLimiter>();
        private sealed class TurnPlayer
        {
            public PlayerState State;
            public float UpdatedAt;
            public bool HasState;
            public Vector3 Hole;
            public bool HasHole;
            public int Shots;
            public bool Holed;
            public Vector3 PendingHole;
            public float PendingHoleSince;
            public ScoreCard Card;
        }
        private readonly Dictionary<byte, TurnPlayer> _turnPlayers = new Dictionary<byte, TurnPlayer>();
        private byte _pendingShot;
        private float _pendingShotAt;
        private Vector3 _pendingHole;
        private const float SameHoleDistanceSquared = 4f; // different samples of the same cup may vary slightly
        private const float HoleSwitchDebounce = 1f; // a different cup must be reported this long, ball settled
        private EventBasedNetListener _listener;
        private NetManager _net;
        private NetPeer _server;
        private double _timeBase;
        private float _nextSend;
        private float _nextInfoCheck;
        private ushort _seq;
        private bool _prevRunInBackground;
        private bool _sessionBegan;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            Shutdown("Game closing");
        }

        private void OnApplicationQuit()
        {
            Shutdown("Game closing");
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Starts hosting. Returns null on success or an error message.</summary>
        public string Host(int port)
        {
            if (Mode != SessionMode.Offline)
                return "Already in a session";
            if (port < 1 || port > 65535)
                return "Port must be 1-65535";

            CreateManager();
            bool started = DevTools.LoopbackOnly
                ? _net.Start(System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback, port)
                : _net.Start(port);
            if (!started)
            {
                _net = null;
                return $"Could not listen on UDP port {port} (already in use?)";
            }

            Mode = SessionMode.Hosting;
            LocalId = Protocol.HostId;
            RefreshLocalInfo(includeName: true);
            Players.Clear();
            _turnPlayers.Clear();
            _latestStates.Clear();
            HasTurnHole = false;
            HasWind = false;
            _nextWindSend = 0f;
            _pendingShot = 0;
            Players[LocalId] = LocalInfo;
            SetActiveTurn(LocalId);
            Endpoint = $"port {port}";
            Status = $"Hosting on UDP port {port}";
            OnSessionBegan();
            Plugin.Log.LogInfo(Status);
            return null;
        }

        /// <summary>Starts connecting to a host. Returns null if the attempt started, or an error message.</summary>
        public string Join(string address, int port)
        {
            if (Mode != SessionMode.Offline)
                return "Already in a session";
            address = address?.Trim();
            if (string.IsNullOrEmpty(address))
                return "Enter the host's IP address";
            if (port < 1 || port > 65535)
                return "Port must be 1-65535";

            CreateManager();
            if (!_net.Start())
            {
                _net = null;
                return "Could not open a UDP socket";
            }

            RefreshLocalInfo(includeName: true);
            var hello = new NetDataWriter();
            hello.Put(Protocol.Magic);
            hello.Put(Protocol.Version);
            hello.Put(ModConfig.Password.Value ?? "", 64);
            LocalInfo.Write(hello);

            _server = _net.Connect(address, port, hello);
            if (_server == null)
            {
                Shutdown(null);
                return $"Could not resolve '{address}'";
            }

            Mode = SessionMode.Connecting;
            LocalId = 0;
            Players.Clear();
            SetActiveTurn(0);
            Endpoint = $"{address}:{port}";
            Status = $"Connecting to {Endpoint}...";
            _timeBase = Time.realtimeSinceStartupAsDouble;
            Plugin.Log.LogInfo(Status);
            return null;
        }

        public void Leave()
        {
            Shutdown("You left the session");
        }

        private void CreateManager()
        {
            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnReceive;
            _listener.NetworkErrorEvent += (ep, err) => Plugin.Log.LogWarning($"Network error from {ep}: {err}");

            _net = new NetManager(_listener)
            {
                AutoRecycle = true,
                IPv6Enabled = true,
                DisconnectTimeout = 15000, // tolerate long scene loads
                PingInterval = 1000,
                MaxConnectAttempts = 12,
                ReconnectDelay = 500,
            };
        }

        private void OnSessionBegan()
        {
            SessionGeneration++;
            _resultPending.Clear();
            _nextShotId = 0;
            LocalPlayer.ShotPending = false;
            _timeBase = Time.realtimeSinceStartupAsDouble;
            // Keep sending/receiving when alt-tabbed, otherwise we'd freeze for everyone else.
            _prevRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            _sessionBegan = true;
            SessionStarted?.Invoke();
        }

        private void Shutdown(string reason)
        {
            SessionGeneration++;
            _resultPending.Clear();
            LocalPlayer.ShotPending = false;
            bool wasActive = Mode != SessionMode.Offline;

            if (_net != null)
            {
                try
                {
                    _net.Stop(true);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Error stopping network: " + e.Message);
                }
            }
            _net = null;
            _server = null;
            _peerToPlayer.Clear();
            _playerToPeer.Clear();
            _chatLimits.Clear();
            _turnPlayers.Clear();
            _latestStates.Clear();
            HasTurnHole = false;
            HasWind = false;
            _pendingShot = 0;

            foreach (var p in Players.Values.Where(p => p.Id != LocalId).ToList())
                PlayerLeft?.Invoke(p);
            Players.Clear();
            SetActiveTurn(0);

            if (_sessionBegan)
                Application.runInBackground = _prevRunInBackground;
            _sessionBegan = false;

            Mode = SessionMode.Offline;
            LocalId = 0;
            if (reason != null)
                Status = reason;
            if (wasActive && reason != null)
            {
                Plugin.Log.LogInfo("Session ended: " + reason);
                SessionEnded?.Invoke(reason);
            }
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (_net == null)
                return;
            _net.PollEvents();
            if (_net == null || !InSession)
                return;

            float now = Time.unscaledTime;
            if (now >= _nextSend)
            {
                _nextSend = now + Protocol.SendInterval;
                SendLocalState();
            }
            if (now >= _nextInfoCheck)
            {
                _nextInfoCheck = now + 1f;
                CheckLocalInfoChanged();
            }
            if (IsHost && _pendingShot != 0)
                FinishPendingShot(now);
            // Re-evaluate against current balls, including a late tee arrival or a retake. Previously,
            // a golfer excluded by a transient wrong cup stayed excluded until somebody shot again.
            if (IsHost && _pendingShot == 0 && HasTurnHole &&
                !(_turnPlayers.TryGetValue(ActiveTurnId, out var active) && active.State.ShotInProgress))
                ChooseTurn(0, TurnHole, TurnHole);
        }

        private void SendLocalState()
        {
            PlayerState s = LocalPlayer.Capture();
            s.Seq = ++_seq;
            s.Time = SessionTime;
            _latestStates[LocalId] = s;
            if (IsHost)
                TrackTurnState(LocalId, s);
            Begin(Msg.State, LocalId);
            s.Write(_w);
            SendFromLocal(DeliveryMethod.Unreliable);
        }

        private void CheckLocalInfoChanged()
        {
            var old = LocalInfo.Clone();
            RefreshLocalInfo(includeName: IsHost);
            if (old.Name == LocalInfo.Name && old.Color.Equals(LocalInfo.Color) && old.BallLook == LocalInfo.BallLook)
                return;
            Begin(Msg.PlayerInfo, LocalId);
            LocalInfo.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
        }

        /// <param name="includeName">
        /// False for joined clients mid-session: they keep the name the host assigned (which may carry a " (2)" suffix).
        /// </param>
        private void RefreshLocalInfo(bool includeName)
        {
            LocalInfo.Id = LocalId;
            if (includeName)
            {
                LocalInfo.Name = Protocol.Clean(ModConfig.ResolveName(), Protocol.MaxNameLength);
                if (LocalInfo.Name.Length == 0)
                    LocalInfo.Name = "Golfer";
            }
            LocalInfo.Color = ModConfig.GetColor();
            LocalInfo.BallLook = LocalPlayer.CurrentBallLook();
        }

        // ------------------------------------------------------------------ outgoing events

        public uint SendShot(ShotEvent shot)
        {
            if (!InSession || !CanShoot(LocalId))
                return 0;
            shot.Id = ++_nextShotId;
            if (shot.Id == 0) shot.Id = ++_nextShotId;
            _resultPending[LocalId] = shot.Id;
            shot.State = LocalPlayer.Capture();
            shot.State.Seq = ++_seq;
            shot.State.Time = SessionTime;
            _latestStates[LocalId] = shot.State;
            if (IsHost)
                TrackTurnState(LocalId, shot.State);
            Begin(Msg.Shot, LocalId);
            shot.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
            if (IsHost)
                AfterShot(LocalId);
            return shot.Id;
        }

        public void SendShotResult(ShotResult result)
        {
            // The turn may already have moved on while this ball was in flight.
            if (!InSession || !AcceptShotResult(LocalId, result)) return;
            Begin(Msg.ShotResult, LocalId);
            result.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
        }

        private bool AcceptShotResult(byte id, ShotResult result)
        {
            if (!Players.ContainsKey(id) || !result.Valid || !_resultPending.TryGetValue(id, out uint expected) || expected != result.Id)
                return false;
            _resultPending.Remove(id); // completion and hole feedback can both fire; publish once
            return true;
        }

        public void SendHoled(HoledEvent holed)
        {
            if (!InSession)
                return;
            Begin(Msg.Holed, LocalId);
            holed.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
            if (IsHost)
                AfterHoled(LocalId);
        }

        public void SendScore(ScoreCard card)
        {
            if (!InSession)
                return;
            if (IsHost)
                TrackTurnScore(LocalId, card);
            Begin(Msg.Score, LocalId);
            card.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
        }

        public void SendChat(string text)
        {
            text = Protocol.Clean(text, Protocol.MaxChatLength);
            if (!InSession || text.Length == 0)
                return;
            Begin(Msg.Chat, LocalId);
            _w.Put(text, Protocol.MaxChatLength * 2);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
            ChatReceived?.Invoke(LocalId, text);
        }

        private void Begin(Msg msg, byte playerId)
        {
            _w.Reset();
            _w.Put((byte)msg);
            _w.Put(playerId);
        }

        private void SendFromLocal(DeliveryMethod method)
        {
            if (IsHost)
                _net.SendToAll(_w, method);
            else
                _server?.Send(_w, method);
        }

        public void PublishWind(WindState wind)
        {
            if (!IsHost || !wind.Valid || Time.unscaledTime < _nextWindSend)
                return;
            _nextWindSend = Time.unscaledTime + Protocol.SendInterval;
            wind.Seq = (ushort)(CurrentWind.Seq + 1);
            CurrentWind = wind;
            HasWind = true;
            Begin(Msg.Wind, Protocol.HostId);
            wind.Write(_w);
            _net.SendToAll(_w, DeliveryMethod.Unreliable);
        }

        // ------------------------------------------------------------------ LiteNetLib callbacks

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (!IsHost)
            {
                request.RejectForce();
                return;
            }

            string error = null;
            PlayerInfo info = null;
            try
            {
                var r = request.Data;
                string magic = r.GetString(16);
                ushort version = r.GetUShort();
                string password = r.GetString(64);
                info = PlayerInfo.Read(r);

                if (magic != Protocol.Magic)
                    error = "Not a Normal Golf Multiplayer client";
                else if (version != Protocol.Version)
                    error = $"Version mismatch: host uses protocol v{Protocol.Version}, you have v{version}. Update the mod.";
                else if (!string.IsNullOrEmpty(ModConfig.Password.Value) && password != ModConfig.Password.Value)
                    error = "Wrong password";
                else if (_peerToPlayer.Count + 1 >= ModConfig.MaxPlayers.Value) // +1 = the host; counts joins still handshaking
                    error = "Session is full";
            }
            catch (Exception)
            {
                error = "Malformed join request";
            }

            byte id = error == null ? AllocateId() : (byte)0;
            if (error == null && id == 0)
                error = "Session is full";

            if (error != null)
            {
                Plugin.Log.LogInfo($"Rejected {request.RemoteEndPoint}: {error}");
                var w = new NetDataWriter();
                w.Put(error);
                request.Reject(w);
                return;
            }

            info.Id = id;
            info.Name = UniqueName(info.Name);
            NetPeer peer = request.Accept();
            if (peer == null)
                return;
            peer.Tag = info;
            // Reserve the id immediately so two simultaneous joins can't be handed the same one.
            _peerToPlayer[peer.Id] = id;
        }

        private void OnPeerConnected(NetPeer peer)
        {
            if (IsHost)
            {
                if (!(peer.Tag is PlayerInfo info))
                {
                    _net.DisconnectPeer(peer);
                    return;
                }

                _playerToPeer[info.Id] = peer;

                Begin(Msg.Welcome, Protocol.HostId);
                info.Write(_w);
                _w.Put(ActiveTurnId);
                WriteTurnHole(_w);
                _w.Put((byte)Players.Count);
                foreach (var p in Players.Values)
                    p.Write(_w);
                peer.Send(_w, DeliveryMethod.ReliableOrdered);

                if (HasWind)
                {
                    Begin(Msg.Wind, Protocol.HostId);
                    CurrentWind.Write(_w);
                    peer.Send(_w, DeliveryMethod.ReliableOrdered);
                }

                Players[info.Id] = info;

                Begin(Msg.PlayerJoined, info.Id);
                info.Write(_w);
                _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);

                Plugin.Log.LogInfo($"{info.Name} joined from {peer} as player {info.Id}");
                PlayerJoined?.Invoke(info);
            }
            else if (peer == _server)
            {
                Mode = SessionMode.Connected;
                Status = $"Connected to {Endpoint}, waiting for welcome...";
            }
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (IsHost)
            {
                if (!_peerToPlayer.TryGetValue(peer.Id, out byte id))
                    return;
                _peerToPlayer.Remove(peer.Id);
                _playerToPeer.Remove(id);
                _chatLimits.Remove(id);
                _turnPlayers.TryGetValue(id, out var departingTurn);
                _turnPlayers.Remove(id);
                _resultPending.Remove(id);
                if (!Players.TryGetValue(id, out var player))
                    return; // disconnected before finishing the handshake
                Players.Remove(id);

                Begin(Msg.PlayerLeft, id);
                _net.SendToAll(_w, DeliveryMethod.ReliableOrdered);

                Plugin.Log.LogInfo($"{player.Name} left ({info.Reason})");
                PlayerLeft?.Invoke(player);
                bool wasPending = _pendingShot == id;
                if (wasPending)
                    _pendingShot = 0;
                if (ActiveTurnId == id || wasPending)
                {
                    if (departingTurn != null && departingTurn.HasHole && departingTurn.HasState)
                        ChooseTurn(id, departingTurn.Hole, departingTurn.State.BallPos);
                    else if (ActiveTurnId == id)
                        PublishTurn(0);
                }
                return;
            }

            if (peer != _server)
                return;

            string reason;
            switch (info.Reason)
            {
                case DisconnectReason.ConnectionRejected:
                    reason = "Join refused: " + (ReadReason(info) ?? "rejected by host");
                    break;
                case DisconnectReason.RemoteConnectionClose:
                    reason = ReadReason(info) ?? "The host ended the session";
                    break;
                case DisconnectReason.ConnectionFailed:
                    reason = $"Could not reach {Endpoint}. Check the IP/port, that the host is hosting, and that UDP {Endpoint.Split(':').Last()} is forwarded/allowed.";
                    break;
                case DisconnectReason.Timeout:
                    reason = "Connection timed out";
                    break;
                case DisconnectReason.HostUnreachable:
                case DisconnectReason.NetworkUnreachable:
                    reason = "Host unreachable (network error)";
                    break;
                case DisconnectReason.UnknownHost:
                    reason = $"Unknown host '{Endpoint}'";
                    break;
                default:
                    reason = "Disconnected (" + info.Reason + ")";
                    break;
            }
            Shutdown(reason);
        }

        private static string ReadReason(DisconnectInfo info)
        {
            try
            {
                var data = info.AdditionalData;
                if (data != null && data.AvailableBytes > 0)
                    return data.GetString();
            }
            catch
            {
                // no readable reason attached
            }
            return null;
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            try
            {
                var msg = (Msg)reader.GetByte();
                byte id = reader.GetByte();
                if (IsHost)
                    HandleOnHost(peer, msg, reader);
                else if (peer == _server)
                    HandleOnClient(msg, id, reader);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Bad packet from {peer}: {e.Message}");
            }
        }

        private void HandleOnHost(NetPeer peer, Msg msg, NetDataReader r)
        {
            if (!_peerToPlayer.TryGetValue(peer.Id, out byte from) || !Players.TryGetValue(from, out var player))
                return;

            switch (msg)
            {
                case Msg.State:
                {
                    var s = PlayerState.Read(r);
                    RememberState(from, s);
                    TrackTurnState(from, s);
                    StateReceived?.Invoke(from, s);
                    Begin(Msg.State, from);
                    s.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.Unreliable, peer);
                    break;
                }
                case Msg.PlayerInfo:
                {
                    var info = PlayerInfo.Read(r);
                    // Name stays whatever the host assigned at join; only cosmetics can change.
                    player.Color = info.Color;
                    player.BallLook = info.BallLook;
                    Begin(Msg.PlayerInfo, from);
                    player.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    PlayerInfoChanged?.Invoke(player);
                    break;
                }
                case Msg.Shot:
                {
                    var shot = ShotEvent.Read(r);
                    if (!CanShoot(from) || shot.Id == 0)
                    {
                        Plugin.Log.LogWarning($"Rejected out-of-turn shot from player {from}; active golfer is {ActiveTurnId}");
                        break;
                    }
                    RememberState(from, shot.State);
                    _resultPending[from] = shot.Id;
                    TrackTurnState(from, shot.State);
                    StateReceived?.Invoke(from, shot.State);
                    ShotReceived?.Invoke(from, shot);
                    Begin(Msg.Shot, from);
                    shot.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    AfterShot(from);
                    break;
                }
                case Msg.ShotResult:
                {
                    var result = ShotResult.Read(r);
                    if (!AcceptShotResult(from, result)) break;
                    ShotResultReceived?.Invoke(from, result);
                    Begin(Msg.ShotResult, from);
                    result.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
                case Msg.Holed:
                {
                    var holed = HoledEvent.Read(r);
                    AfterHoled(from);
                    HoledReceived?.Invoke(from, holed);
                    Begin(Msg.Holed, from);
                    holed.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
                case Msg.Score:
                {
                    var card = ScoreCard.Read(r);
                    TrackTurnScore(from, card);
                    ScoreReceived?.Invoke(from, card);
                    Begin(Msg.Score, from);
                    card.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
                case Msg.Chat:
                {
                    string text = Protocol.Clean(r.GetString(Protocol.MaxChatLength * 2), Protocol.MaxChatLength);
                    if (text.Length == 0)
                        break;
                    if (!_chatLimits.TryGetValue(from, out var limiter))
                        _chatLimits[from] = limiter = new ChatLimiter();
                    if (!limiter.Allow(Time.unscaledTime))
                        break;
                    ChatReceived?.Invoke(from, text);
                    Begin(Msg.Chat, from);
                    _w.Put(text, Protocol.MaxChatLength * 2);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
            }
        }

        private void HandleOnClient(Msg msg, byte id, NetDataReader r)
        {
            switch (msg)
            {
                case Msg.Wind:
                {
                    var wind = WindState.Read(r);
                    if (id == Protocol.HostId && wind.Valid &&
                        (!HasWind || (short)(wind.Seq - CurrentWind.Seq) > 0))
                    {
                        CurrentWind = wind;
                        HasWind = true;
                    }
                    break;
                }
                case Msg.Welcome:
                {
                    var me = PlayerInfo.Read(r);
                    byte activeTurn = r.GetByte();
                    ReadTurnHole(r);
                    LocalId = me.Id;
                    LocalInfo = me;
                    Players.Clear();
                    Players[LocalId] = LocalInfo;
                    int count = r.GetByte();
                    var others = new List<PlayerInfo>();
                    for (int i = 0; i < count; i++)
                    {
                        var p = PlayerInfo.Read(r);
                        if (p.Id == LocalId)
                            continue;
                        Players[p.Id] = p;
                        others.Add(p);
                    }
                    Status = $"Connected to {Endpoint}";
                    Plugin.Log.LogInfo($"Joined {Endpoint} as player {LocalId} '{LocalInfo.Name}' with {others.Count} other player(s)");
                    OnSessionBegan();
                    foreach (var p in others)
                        PlayerJoined?.Invoke(p);
                    SetActiveTurn(activeTurn == 0 || Players.ContainsKey(activeTurn) ? activeTurn : LocalId);
                    break;
                }
                case Msg.PlayerJoined:
                {
                    var p = PlayerInfo.Read(r);
                    if (p.Id == LocalId)
                        break;
                    Players[p.Id] = p;
                    PlayerJoined?.Invoke(p);
                    break;
                }
                case Msg.PlayerLeft:
                    if (Players.TryGetValue(id, out var left) && id != LocalId)
                    {
                        Players.Remove(id);
                        _resultPending.Remove(id);
                        PlayerLeft?.Invoke(left);
                    }
                    break;
                case Msg.PlayerInfo:
                {
                    var p = PlayerInfo.Read(r);
                    if (p.Id == LocalId)
                        break;
                    if (Players.TryGetValue(p.Id, out var existing))
                    {
                        existing.Name = p.Name;
                        existing.Color = p.Color;
                        existing.BallLook = p.BallLook;
                        PlayerInfoChanged?.Invoke(existing);
                    }
                    break;
                }
                case Msg.State:
                    if (id != LocalId && id != 0)
                    {
                        var state = PlayerState.Read(r);
                        RememberState(id, state);
                        StateReceived?.Invoke(id, state);
                    }
                    break;
                case Msg.Shot:
                    if (id != LocalId)
                    {
                        var shot = ShotEvent.Read(r);
                        if (shot.Id == 0 || !Players.ContainsKey(id)) break;
                        _resultPending[id] = shot.Id;
                        RememberState(id, shot.State);
                        StateReceived?.Invoke(id, shot.State);
                        ShotReceived?.Invoke(id, shot);
                    }
                    break;
                case Msg.ShotResult:
                    if (id != LocalId)
                    {
                        var result = ShotResult.Read(r);
                        if (AcceptShotResult(id, result)) ShotResultReceived?.Invoke(id, result);
                    }
                    break;
                case Msg.Holed:
                    if (id != LocalId)
                        HoledReceived?.Invoke(id, HoledEvent.Read(r));
                    break;
                case Msg.Score:
                    if (id != LocalId)
                        ScoreReceived?.Invoke(id, ScoreCard.Read(r));
                    break;
                case Msg.Chat:
                    if (id != LocalId)
                        ChatReceived?.Invoke(id, Protocol.Clean(r.GetString(Protocol.MaxChatLength * 2), Protocol.MaxChatLength));
                    break;
                case Msg.Turn:
                {
                    byte activeTurn = r.GetByte();
                    ReadTurnHole(r);
                    if (activeTurn == 0 || Players.ContainsKey(activeTurn))
                        SetActiveTurn(activeTurn);
                    break;
                }
            }
        }

        private void SetActiveTurn(byte id)
        {
            if (ActiveTurnId == id)
                return;
            ActiveTurnId = id;
            TurnChanged?.Invoke(id);
        }

        /// <summary>The host keeps one current ball and cup per golfer, resetting tee order when the cup changes.</summary>
        private void TrackTurnState(byte id, PlayerState state)
        {
            if (!_turnPlayers.TryGetValue(id, out var player))
                _turnPlayers[id] = player = new TurnPlayer();
            // Ignore delayed UDP samples: they can otherwise resurrect an old ball position after landing.
            if (player.HasState && (short)(state.Seq - player.State.Seq) <= 0)
                return;
            player.State = state;
            player.UpdatedAt = Time.unscaledTime;
            player.HasState = true;
            if (!state.Has(StateFlags.HoleKnown) || !Finite(state.HolePos))
                return;

            if (!player.HasHole)
            {
                player.Hole = state.HolePos;
                player.HasHole = true;
                player.PendingHole = state.HolePos;
                player.PendingHoleSince = Time.unscaledTime;
            }
            else if ((player.Card == null || !player.Card.HasRound || player.Card.Active) &&
                     (player.Shots == 0 || player.Holed) &&
                     (player.Hole - state.HolePos).sqrMagnitude > SameHoleDistanceSquared)
            {
                // The reported cup changed (next hole, or a skipped hole): accept it once it is reported
                // steadily with the ball settled, so a ball in flight cannot race the switch.
                float now = Time.unscaledTime;
                if (!state.Has(StateFlags.BallMoving) &&
                    (state.HolePos - player.PendingHole).sqrMagnitude <= SameHoleDistanceSquared)
                {
                    if (now - player.PendingHoleSince >= HoleSwitchDebounce)
                    {
                        player.Hole = state.HolePos;
                        player.Shots = 0;
                        player.Holed = false;
                    }
                }
                else
                {
                    player.PendingHole = state.HolePos;
                    player.PendingHoleSince = now;
                }
            }
            if (ActiveTurnId == 0 && _pendingShot == 0 && state.Has(StateFlags.InWorld))
                ChooseTurn(0, player.Hole, state.BallPos);
            else if (!HasTurnHole && state.Has(StateFlags.InWorld))
                SetTurnHole(player.Hole);
        }

        private static bool Finite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) && Mathf.Abs(v.x) < 100000f &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) && Mathf.Abs(v.y) < 100000f &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z) && Mathf.Abs(v.z) < 100000f;

        private void TrackTurnScore(byte id, ScoreCard card)
        {
            if (!_turnPlayers.TryGetValue(id, out var player))
                _turnPlayers[id] = player = new TurnPlayer();
            var old = player.Card;
            bool changedHole = card.HasRound && (old == null || !old.HasRound ||
                old.RoundId != card.RoundId || old.CurrentHole != card.CurrentHole);
            Vector3 previousHole = player.Hole;
            bool hadHole = player.HasHole;
            if (changedHole)
            {
                player.HasHole = false;
                player.Shots = 0;
                player.Holed = false;
            }
            // Rejoining during a round must not grant another opening shot. Penalties never undo tee-off.
            player.Shots = Math.Max(player.Shots, card.Strokes);
            if (card.HasRound && !card.Active)
                player.Holed = true;
            player.Card = new ScoreCard();
            player.Card.CopyFrom(card);
            if (changedHole && hadHole && (ActiveTurnId == id || _pendingShot == id))
            {
                _pendingShot = 0;
                ChooseTurn(id, previousHole, player.State.BallPos);
            }
        }

        private List<TurnOrder.Candidate> TurnCandidates(Vector3 hole, Vector3 referenceBall)
        {
            var candidates = new List<TurnOrder.Candidate>();
            float now = Time.unscaledTime;
            foreach (var entry in _turnPlayers)
            {
                var p = entry.Value;
                if (!Players.ContainsKey(entry.Key) || !p.HasHole || !p.HasState ||
                    now - p.UpdatedAt > 5f || !p.State.Has(StateFlags.InWorld) ||
                    !p.State.Has(StateFlags.HoleKnown) || !Finite(p.State.BallPos))
                    continue;
                // Ball spread is irrelevant: a short drive must stay in the same group as a long drive.
                bool sameHole = (p.Hole - hole).sqrMagnitude <= SameHoleDistanceSquared;
                if (!sameHole)
                    continue;
                candidates.Add(new TurnOrder.Candidate
                {
                    Id = entry.Key,
                    Shots = p.Shots,
                    Holed = p.Holed,
                    // BallTrail lingers after the ball rests, so only the rigidbody counts as "moving".
                    Settled = !p.State.Has(StateFlags.BallMoving) && !p.State.ShotInProgress,
                    // Everyone is measured against the same cup, so a drifted pick cannot skew the order.
                    DistanceSquared = (p.State.BallPos - hole).sqrMagnitude,
                });
            }
            ApplyTeeHonours(hole, candidates);
            return candidates;
        }

        private void ApplyTeeHonours(Vector3 hole, List<TurnOrder.Candidate> candidates)
        {
            int holeNumber = 0;
            foreach (var candidate in candidates)
            {
                var card = _turnPlayers[candidate.Id].Card;
                if (!candidate.Holed && card != null && card.HasRound && card.Active)
                    holeNumber = Math.Max(holeNumber, card.CurrentHole);
            }
            if (holeNumber == 0)
            {
                // Front Nine can load before its reliable scorecard. Wait for that initial
                // card instead of accidentally granting a resumed hole to the host.
                bool awaitingCard = candidates.Any(c => _turnPlayers[c.Id].State.Has(StateFlags.PlayNine) &&
                    (_turnPlayers[c.Id].Card == null || !_turnPlayers[c.Id].Card.HasRound));
                if (awaitingCard)
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var candidate = candidates[i];
                        if (candidate.Shots == 0) candidate.AwaitingTee = true;
                        candidates[i] = candidate;
                    }
                return; // unscored free play retains the agreed opening order
            }

            var golfers = new List<TeeHonours.Golfer>();
            foreach (var entry in _turnPlayers)
            {
                var player = entry.Value;
                var card = player.Card;
                if (Players.ContainsKey(entry.Key) && player.HasState && player.State.Has(StateFlags.InWorld) &&
                    ((card != null && card.HasRound && card.Active && card.CurrentHole > 0 && card.CurrentHole <= holeNumber) ||
                     ((card == null || !card.HasRound) && player.State.Has(StateFlags.PlayNine))))
                    golfers.Add(new TeeHonours.Golfer { Id = entry.Key, Card = card });
            }
            bool scoresReady = TeeHonours.TryOrder(golfers, holeNumber, out var order);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                int rank = order.IndexOf(candidate.Id);
                candidate.TeeRank = rank >= 0 ? rank : order.Count;
                if (rank >= 0 && candidate.Shots == 0)
                    candidate.AwaitingTee = !scoresReady || !candidate.Settled ||
                        !_turnPlayers[candidate.Id].State.Has(StateFlags.BallVisible);
                candidates[i] = candidate;
            }
            // A golfer still finishing the previous hole or walking to this tee keeps their
            // earned place. Do not let an earlier arrival play ahead of them.
            for (int rank = 0; rank < order.Count; rank++)
            {
                byte id = order[rank];
                if (candidates.Any(p => p.Id == id)) continue;
                var player = _turnPlayers[id];
                bool onThisHole = player.HasHole && (player.Hole - hole).sqrMagnitude <= SameHoleDistanceSquared;
                int shots = onThisHole ? player.Shots : 0;
                candidates.Add(new TurnOrder.Candidate { Id = id, TeeRank = rank, Shots = shots,
                    Holed = onThisHole && player.Holed, AwaitingTee = shots == 0,
                    Settled = false, DistanceSquared = -1f });
            }
        }

        private void AfterShot(byte id)
        {
            if (!_turnPlayers.TryGetValue(id, out var player) || !player.HasState ||
                !player.State.Has(StateFlags.HoleKnown) || !Finite(player.State.HolePos))
            {
                if (player != null)
                    player.Shots++;
                PublishTurn(0); // No flag means no defensible distance comparison.
                return;
            }
            // The first shot on a hole is played from the tee with the golfer aiming at the pin, so its
            // pick is the hole being played. Later shots aim wherever the golfer faces (often just the
            // ball), so mid-hole the captured cup is kept and only a hole-out unlocks a new capture.
            Vector3 hole = player.Hole;
            bool newHole = false;
            if (!player.HasHole || player.Holed || player.Shots == 0)
            {
                hole = player.State.HolePos;
                newHole = !player.HasHole || (player.Hole - hole).sqrMagnitude > SameHoleDistanceSquared;
                player.Hole = hole;
                player.HasHole = true;
            }
            player.Shots = newHole ? 1 : player.Shots + 1;
            player.Holed = false;
            player.PendingHole = hole;
            player.PendingHoleSince = Time.unscaledTime;
            _pendingShot = id;
            _pendingShotAt = Time.unscaledTime;
            _pendingHole = hole;
            SetTurnHole(hole);

            byte opening = TurnOrder.NextOpeningShot(TurnCandidates(player.Hole, player.State.BallPos));
            if (opening != 0)
            {
                _pendingShot = 0;
                PublishTurn(opening);
            }
            else
            {
                // The last tee shot (or any later shot) must land before its distance can be compared.
                PublishTurn(0);
            }
        }

        private void AfterHoled(byte id)
        {
            if (!_turnPlayers.TryGetValue(id, out var player) || !player.HasHole)
                return;
            player.Holed = true;
            if (_pendingShot == id)
                _pendingShot = 0;
            ChooseTurn(id, player.Hole, player.State.BallPos);
        }

        private void FinishPendingShot(float now)
        {
            Vector3 referenceBall = _turnPlayers.TryGetValue(_pendingShot, out var shooter2) && shooter2.HasState
                ? shooter2.State.BallPos
                : _pendingHole;
            var candidates = TurnCandidates(_pendingHole, referenceBall);
            byte opening = TurnOrder.NextOpeningShot(candidates);
            if (opening != 0)
            {
                _pendingShot = 0;
                PublishTurn(opening);
                return;
            }

            if (now - _pendingShotAt < 0.9f)
                return;
            // A long drive can take more than 18 seconds. Keep waiting until fresh samples say it rests.
            if (_turnPlayers.TryGetValue(_pendingShot, out var shooter) &&
                now - shooter.UpdatedAt <= 5f &&
                (shooter.UpdatedAt <= _pendingShotAt + 0.1f || shooter.State.Has(StateFlags.BallMoving) || shooter.State.ShotInProgress))
                return;
            if (candidates.Any(c => !c.Holed && !c.Settled))
                return;
            _pendingShot = 0;
            PublishTurn(TurnOrder.FarthestBall(candidates));
        }

        private void ChooseTurn(byte after, Vector3 hole, Vector3 referenceBall)
        {
            SetTurnHole(hole);
            var candidates = TurnCandidates(hole, referenceBall);
            byte next = TurnOrder.NextOpeningShot(candidates);
            if (next == 0 && candidates.Any(c => !c.Holed && !c.Settled))
            {
                _pendingShot = after;
                _pendingHole = hole;
                _pendingShotAt = Time.unscaledTime - 0.9f;
                PublishTurn(0);
                return;
            }
            if (next == 0)
                next = TurnOrder.FarthestBall(candidates);
            PublishTurn(next);
        }

        private void PublishTurn(byte next)
        {
            if (!IsHost || ActiveTurnId == next)
                return;
            SetActiveTurn(next);
            Begin(Msg.Turn, Protocol.HostId);
            _w.Put(next);
            WriteTurnHole(_w);
            _net.SendToAll(_w, DeliveryMethod.ReliableOrdered);
        }

        private void SetTurnHole(Vector3 hole)
        {
            if (!HasTurnHole || (TurnHole - hole).sqrMagnitude > SameHoleDistanceSquared)
            {
                TurnHole = hole;
                HasTurnHole = true;
                // The flag is part of turn state even when the same golfer has the next tee honour.
                Begin(Msg.Turn, Protocol.HostId);
                _w.Put(ActiveTurnId);
                WriteTurnHole(_w);
                _net.SendToAll(_w, DeliveryMethod.ReliableOrdered);
            }
        }

        private void WriteTurnHole(NetDataWriter w)
        {
            w.Put(HasTurnHole);
            w.Put(TurnHole.x); w.Put(TurnHole.y); w.Put(TurnHole.z);
        }

        private void ReadTurnHole(NetDataReader r)
        {
            HasTurnHole = r.GetBool();
            TurnHole = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
            HasTurnHole &= Finite(TurnHole);
        }

        private void RememberState(byte id, PlayerState state)
        {
            if (!_latestStates.TryGetValue(id, out var old) || (short)(state.Seq - old.Seq) > 0)
                _latestStates[id] = state;
        }

        public bool TryGetTurnDistance(byte id, out float metres)
        {
            metres = 0f;
            if (!HasTurnHole || !_latestStates.TryGetValue(id, out var state) ||
                !state.Has(StateFlags.InWorld) || !Finite(state.BallPos))
                return false;
            metres = Mathf.Sqrt((state.BallPos - TurnHole).sqrMagnitude);
            return true;
        }

        // ------------------------------------------------------------------ helpers

        private byte AllocateId()
        {
            for (int i = Protocol.HostId + 1; i < 255; i++)
            {
                byte id = (byte)i;
                if (!Players.ContainsKey(id) && !_peerToPlayer.ContainsValue(id))
                    return id;
            }
            return 0;
        }

        private string UniqueName(string name)
        {
            string candidate = name;
            for (int n = 2; Players.Values.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
                candidate = Protocol.Clean(name, Protocol.MaxNameLength - 4) + $" ({n})";
            return candidate;
        }

        public int GetPlayerPing(byte id)
        {
            if (id == LocalId)
                return 0;
            if (IsHost)
                return _playerToPeer.TryGetValue(id, out var peer) ? peer.RoundTripTime : 0;
            return PingMs; // clients only know their own latency to the host
        }

        private class ChatLimiter
        {
            private readonly Queue<float> _times = new Queue<float>();

            public bool Allow(float now)
            {
                while (_times.Count > 0 && now - _times.Peek() > 5f)
                    _times.Dequeue();
                if (_times.Count >= 6)
                    return false;
                _times.Enqueue(now);
                return true;
            }
        }
    }
}
