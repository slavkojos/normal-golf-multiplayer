using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NormalGolfMultiplayer.Game;
using NormalGolfMultiplayer.Net;
using NormalGolfMultiplayer.Remote;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NormalGolfMultiplayer.UI
{
    /// <summary>IMGUI multiplayer menu (host/join by IP:port), HUD, toasts and chat.</summary>
    internal class MultiplayerUI : MonoBehaviour
    {
        private static MultiplayerUI _instance;

        /// <summary>True while our menu or chat box owns the keyboard/mouse; game input is paused meanwhile.</summary>
        public static bool CapturingInput => _instance != null && (_instance._menuOpen || _instance._chatOpen);

        private const float WindowWidth = 560f;
        private const float WindowHeight = 720f;
        private const float WindowChromeHeight = 144f;
        private const int ChatHistory = 40;
        private static readonly Color Accent = new Color(0.46f, 0.91f, 0.72f);
        private static readonly Color ErrorColor = new Color(1f, 0.49f, 0.46f);
        private static readonly Color Muted = new Color(0.62f, 0.69f, 0.72f);

        private bool _menuOpen;
        private bool _scoreboardOpen;
        private bool _chatOpen;
        private int _chatOpenedFrame;
        private string _chatText = "";
        private Rect _windowRect = new Rect(40f, 90f, WindowWidth, 10f);
        private Vector2 _menuScroll;
        private bool _menuPlaced;

        private string _nameField;
        private string _hostPortField;
        private string _joinAddressField;
        private string _joinPortField;
        private string _passwordField;
        private string _feedback = "";
        private bool _feedbackIsError;
        private string[] _lanAddresses;

        private bool _captured;
        private CursorLockMode _savedLockMode;
        private bool _savedCursorVisible;

        private readonly List<ToastEntry> _toasts = new List<ToastEntry>();
        private readonly List<ChatEntry> _chat = new List<ChatEntry>();
        private float _hintUntil;

        private GUIStyle _window, _card, _playerRow, _title, _header, _label, _small, _button, _bigButton, _dangerButton, _field, _swatch, _hud, _chatStyle;
        private GUIStyle _eyebrow, _turnName, _pill, _turnPanel;
        private GUIStyle _scoreHead, _scoreRowHead, _scorePar, _scoreCell, _scoreName;
        private Texture2D _white;

        private struct ToastEntry
        {
            public string Text;
            public Color Color;
            public float Until;
        }

        private struct ChatEntry
        {
            public string Name;
            public Color Color;
            public string Text;
            public float Time;
        }

        private void Awake()
        {
            _instance = this;
            _nameField = ModConfig.PlayerName.Value;
            _hostPortField = ModConfig.HostPort.Value.ToString(CultureInfo.InvariantCulture);
            _joinAddressField = ModConfig.JoinAddress.Value;
            _joinPortField = ModConfig.JoinPort.Value.ToString(CultureInfo.InvariantCulture);
            _passwordField = ModConfig.Password.Value;
            _hintUntil = Time.unscaledTime + 25f;
        }

        private void Start()
        {
            var s = NetSession.Instance;
            s.ChatReceived += OnChat;
            s.SessionEnded += reason =>
            {
                SetFeedback(reason, error: !reason.StartsWith("You left", StringComparison.Ordinal));
                Toast(reason, ErrorColor);
            };
            s.SessionStarted += () =>
            {
                SetFeedback(s.IsHost ? "Hosting! Share your IP and port with friends." : "Connected!", error: false);
                _hintUntil = Time.unscaledTime + 12f;
            };
            s.TurnChanged += id =>
            {
                if (s.InSession && s.Players.Count > 1 && id == s.LocalId)
                    Toast("Your turn to shoot", Accent);
            };
        }

        public static void SetMenuOpen(bool open)
        {
            if (_instance != null)
                _instance._menuOpen = open;
        }

        public static void SetScoreboardOpen(bool open)
        {
            if (_instance != null)
                _instance._scoreboardOpen = open;
        }

        public static void Toast(string text, Color color)
        {
            if (_instance == null)
                return;
            _instance._toasts.Add(new ToastEntry { Text = text, Color = color, Until = Time.unscaledTime + 6f });
            if (_instance._toasts.Count > 6)
                _instance._toasts.RemoveAt(0);
        }

        private void OnChat(byte id, string text)
        {
            var s = NetSession.Instance;
            string name = s.Players.TryGetValue(id, out var p) ? p.Name : "?";
            Color color = p != null ? (Color)p.Color : Color.white;
            _chat.Add(new ChatEntry { Name = name, Color = color, Text = text, Time = Time.unscaledTime });
            if (_chat.Count > ChatHistory)
                _chat.RemoveAt(0);
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (!_chatOpen && kb[ModConfig.MenuKey.Value].wasPressedThisFrame)
                    _menuOpen = !_menuOpen;
                else if (!_chatOpen && kb[ModConfig.ScoreboardKey.Value].wasPressedThisFrame)
                    _scoreboardOpen = !_scoreboardOpen;
                else if (!_menuOpen && !_chatOpen && kb[ModConfig.ChatKey.Value].wasPressedThisFrame)
                {
                    if (NetSession.Instance.InSession)
                        OpenChat();
                    else
                        Toast("Host or join a game to chat", Muted);
                }
            }
            UpdateInputCapture();
        }

        private void OpenChat()
        {
            _menuOpen = false;
            _chatOpen = true;
            _chatText = "";
            _chatOpenedFrame = Time.frameCount;
        }

        private void UpdateInputCapture()
        {
            bool want = CapturingInput;
            if (want && !_captured)
            {
                _captured = true;
                _savedLockMode = Cursor.lockState;
                _savedCursorVisible = Cursor.visible;
                SetGameInput(false);
            }
            else if (!want && _captured)
            {
                _captured = false;
                SetGameInput(true);
                Cursor.lockState = _savedLockMode;
                Cursor.visible = _savedCursorVisible;
            }

            if (_captured)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static void SetGameInput(bool active)
        {
            var im = InputManager.instance;
            if (im == null || im.input == null)
                return;
            if (active)
                im.input.ActivateInput();
            else
                im.input.DeactivateInput();
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;
            var overlayRows = _scoreboardOpen && !_menuOpen ? CollectScoreRows(NetSession.Instance) : null;

            if (!_menuOpen && (overlayRows == null || overlayRows.Count == 0))
            {
                DrawHud(w);
                DrawToasts(w);
            }
            DrawChat(h);
            if (overlayRows != null && overlayRows.Count > 0)
                DrawScoreboardOverlay(w, overlayRows);

            if (_menuOpen)
            {
                _windowRect.width = Mathf.Min(WindowWidth, Mathf.Max(1f, w - 24f));
                _windowRect.height = Mathf.Min(WindowHeight, Mathf.Max(1f, h - 24f));
                if (!_menuPlaced)
                {
                    _windowRect.x = Mathf.Max(12f, (w - _windowRect.width) * 0.5f);
                    _windowRect.y = Mathf.Max(12f, (h - _windowRect.height) * 0.5f);
                    _menuPlaced = true;
                }
                _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, w - _windowRect.width));
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, h - _windowRect.height));
                _windowRect = GUILayout.Window(0x4E474D50, _windowRect, DrawWindow, GUIContent.none, _window,
                    GUILayout.Width(_windowRect.width), GUILayout.Height(_windowRect.height));
            }
        }

        private void DrawHud(float screenW)
        {
            var s = NetSession.Instance;
            string line;
            if (s.Mode == SessionMode.Offline)
            {
                if (Time.unscaledTime > _hintUntil)
                    return;
                line = $"Multiplayer: press {ModConfig.MenuKey.Value}";
            }
            else if (s.Mode == SessionMode.Connecting || (s.Mode == SessionMode.Connected && !s.InSession))
            {
                line = "Multiplayer: connecting...";
            }
            else
            {
                int n = s.Players.Count;
                string who = n == 1 ? "just you" : $"{n} players";
                line = s.IsHost ? $"HOSTING  ·  {who}" : $"CONNECTED  ·  {who}  ·  {s.PingMs} ms";
            }
            bool showTurn = s.InSession && s.Players.Count > 1 && s.Players.ContainsKey(s.ActiveTurnId);
            var rect = new Rect(16f, 82f, Mathf.Min(324f, screenW - 32f), showTurn ? 100f : 42f);
            GUI.Box(rect, GUIContent.none, _turnPanel);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, 24f), line, _hud);

            if (!showTurn)
                return;
            var active = s.Players[s.ActiveTurnId];
            GUI.Label(new Rect(rect.x + 12f, rect.y + 37f, rect.width - 24f, 18f), "UP NEXT TO SHOOT", _eyebrow);
            var old = GUI.contentColor;
            GUI.contentColor = Color.Lerp(active.Color, Color.white, 0.35f);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 55f, rect.width - 24f, 32f),
                active.Id == s.LocalId ? "Your turn" : Truncate(active.Name, 18) + "'s turn", _turnName);
            GUI.contentColor = old;
        }

        private void DrawToasts(float screenW)
        {
            float now = Time.unscaledTime;
            _toasts.RemoveAll(t => t.Until < now);
            float y = NetSession.Instance.InSession && NetSession.Instance.Players.Count > 1 ? 192f : 134f;
            foreach (var t in _toasts)
            {
                float alpha = Mathf.Clamp01((t.Until - now) / 0.6f);
                var c = Color.Lerp(t.Color, Color.white, 0.3f);
                c.a = alpha;
                ShadowLabel(new Rect(20f, y, Mathf.Min(500f, screenW - 40f), 26f), t.Text, _chatStyle, c);
                y += 26f;
            }
        }

        private void DrawChat(float screenH)
        {
            float now = Time.unscaledTime;
            var visible = _chatOpen ? _chat.Skip(Math.Max(0, _chat.Count - 10)).ToList()
                : _chat.Where(c => now - c.Time < 12f).Skip(Math.Max(0, _chat.Count - 6)).ToList();

            float y = screenH - 200f - visible.Count * 24f;
            foreach (var c in visible)
            {
                float alpha = _chatOpen ? 1f : Mathf.Clamp01((12f - (now - c.Time)) / 1.5f);
                var nameColor = Color.Lerp(c.Color, Color.white, 0.3f);
                nameColor.a = alpha;
                var nameSize = _chatStyle.CalcSize(new GUIContent(c.Name + ":"));
                ShadowLabel(new Rect(20f, y, nameSize.x + 4f, 24f), c.Name + ":", _chatStyle, nameColor);
                ShadowLabel(new Rect(26f + nameSize.x, y, 700f, 24f), c.Text, _chatStyle, new Color(1f, 1f, 1f, alpha));
                y += 24f;
            }

            if (!_chatOpen)
                return;

            var e = Event.current;
            // The key that opened chat would otherwise be typed into the box on the same frame.
            if (Time.frameCount == _chatOpenedFrame && e.type == EventType.KeyDown)
            {
                e.Use();
                return;
            }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                NetSession.Instance.SendChat(_chatText);
                _chatOpen = false;
                e.Use();
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _chatOpen = false;
                e.Use();
                return;
            }

            float chatWidth = Mathf.Max(40f, Mathf.Min(720f, Screen.width / Mathf.Max(1f, Screen.height / 1080f) - 32f));
            GUI.Box(new Rect(16f, screenH - 190f, chatWidth, 36f), GUIContent.none, _window);
            GUI.SetNextControlName("ngmp_chat");
            _chatText = GUI.TextField(new Rect(24f, screenH - 185f, chatWidth - 16f, 26f), _chatText, Protocol.MaxChatLength, _field);
            GUI.FocusControl("ngmp_chat");
        }

        private void DrawWindow(int id)
        {
            var s = NetSession.Instance;

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("NORMAL GOLF  /  ONLINE", _eyebrow);
            GUILayout.Label("Multiplayer", _title);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (s.InSession && GUILayout.Button("Chat", _button, GUILayout.Width(70f), GUILayout.Height(30f)))
                OpenChat();
            if (GUILayout.Button("Close  ×", _button, GUILayout.Width(88f), GUILayout.Height(30f)))
                _menuOpen = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(12f);

            _menuScroll = GUILayout.BeginScrollView(_menuScroll, false, false,
                GUILayout.Height(Mathf.Max(1f, _windowRect.height - WindowChromeHeight)));
            GUILayout.BeginVertical(_card);
            SectionHeading("YOUR GOLFER", "Choose how friends see you");
            DrawIdentity(s);
            GUILayout.EndVertical();
            GUILayout.Space(10f);

            if (s.Mode == SessionMode.Offline)
            {
                GUILayout.BeginVertical(_card);
                DrawHostSection();
                GUILayout.EndVertical();
                GUILayout.Space(10f);
                GUILayout.BeginVertical(_card);
                DrawJoinSection();
                GUILayout.EndVertical();
            }
            else if (!s.InSession)
            {
                GUILayout.BeginVertical(_card);
                SectionHeading("CONNECTING", "Establishing a multiplayer session");
                GUILayout.Label(s.Status, _label);
                GUILayout.Space(6f);
                if (GUILayout.Button("Cancel", _button, GUILayout.Width(120f)))
                    s.Leave();
                GUILayout.EndVertical();
            }
            else
            {
                DrawSession(s);
            }

            if (!string.IsNullOrEmpty(_feedback))
            {
                GUILayout.Space(10f);
                GUILayout.BeginVertical(_card);
                var prev = GUI.contentColor;
                GUI.contentColor = _feedbackIsError ? ErrorColor : Accent;
                GUILayout.Label(_feedback, _label);
                GUI.contentColor = prev;
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.Space(10f);
            string chatHint = s.InSession ? $"{ModConfig.ChatKey.Value} chat (close menu)" : "Host or join to chat";
            GUILayout.Label($"{ModConfig.MenuKey.Value} menu   ·   {chatHint}   ·   {ModConfig.ScoreboardKey.Value} scores      v{Plugin.Version}", _small);

            GUI.DragWindow(new Rect(0, 0, 10000, 56));
        }

        private void SectionHeading(string heading, string subtitle)
        {
            GUILayout.Label(heading, _header);
            GUILayout.Label(subtitle, _small);
            GUILayout.Space(8f);
        }

        private void DrawIdentity(NetSession s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _label, GUILayout.Width(70f));
            GUI.enabled = !s.InSession || s.IsHost;
            string newName = GUILayout.TextField(_nameField ?? "", Protocol.MaxNameLength, _field, GUILayout.Width(220f));
            GUI.enabled = true;
            if (newName != _nameField)
            {
                _nameField = newName;
                ModConfig.PlayerName.Value = newName.Trim();
            }
            if (string.IsNullOrWhiteSpace(_nameField))
                GUILayout.Label($"(using \"{ModConfig.ResolveName()}\")", _small);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Colour", _label, GUILayout.Width(70f));
            Color current = ModConfig.GetColor();
            foreach (var c in ModConfig.Palette)
            {
                bool selected = ColorsClose(c, current);
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = c;
                if (GUILayout.Button(selected ? "✓" : "", _swatch, GUILayout.Width(30f), GUILayout.Height(24f)))
                    ModConfig.PlayerColor.Value = "#" + ColorUtility.ToHtmlStringRGB(c);
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawHostSection()
        {
            SectionHeading("HOST A GAME", "Create a room and invite friends");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Port", _label, GUILayout.Width(70f));
            _hostPortField = GUILayout.TextField(_hostPortField, 5, _field, GUILayout.Width(80f));
            GUILayout.Space(16f);
            GUILayout.Label("Password", _label, GUILayout.Width(80f));
            _passwordField = GUILayout.PasswordField(_passwordField ?? "", '•', 32, _field, GUILayout.Width(150f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host game", _bigButton, GUILayout.Width(160f)))
                DoHost();
            GUILayout.Space(10f);
            GUILayout.Label("Password is optional.", _small);
            GUILayout.EndHorizontal();

            var lan = LanAddresses();
            if (lan.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Your LAN IP: " + string.Join(", ", lan), _small);
                if (GUILayout.Button("Copy", _button, GUILayout.Width(60f)))
                    GUIUtility.systemCopyBuffer = $"{lan[0]}:{_hostPortField}";
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4f);
            GUILayout.Label("Playing over the internet? Forward the UDP port on your router, or use a shared VPN.", _small);
        }

        private void DrawJoinSection()
        {
            SectionHeading("JOIN A GAME", "Enter the host's address and port");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", _label, GUILayout.Width(70f));
            _joinAddressField = GUILayout.TextField(_joinAddressField ?? "", 100, _field, GUILayout.Width(220f));
            GUILayout.Space(8f);
            GUILayout.Label("Port", _label, GUILayout.Width(40f));
            _joinPortField = GUILayout.TextField(_joinPortField, 5, _field, GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Join", _bigButton, GUILayout.Width(160f)))
                DoJoin();
            GUILayout.Space(10f);
            GUILayout.Label("Uses the password above, if the host set one.", _small);
            GUILayout.EndHorizontal();
        }

        private void DrawSession(NetSession s)
        {
            GUILayout.BeginVertical(_card);
            SectionHeading(s.IsHost ? "YOUR ROOM IS LIVE" : "CONNECTED TO ROOM", s.IsHost ? "Share your address to invite friends" : s.Endpoint);
            if (s.IsHost)
            {
                var lan = LanAddresses();
                if (lan.Length > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("LAN: " + string.Join(", ", lan.Select(a => $"{a}:{ModConfig.HostPort.Value}")), _small);
                    if (GUILayout.Button("Copy", _button, GUILayout.Width(60f)))
                        GUIUtility.systemCopyBuffer = $"{lan[0]}:{ModConfig.HostPort.Value}";
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label($"Ping {s.PingMs} ms", _small);
            }
            GUILayout.EndVertical();
            GUILayout.Space(10f);

            GUILayout.BeginVertical(_card);
            SectionHeading("SHOT ORDER", "A guide for taking turns · shots remain open to everyone");
            if (s.Players.Count > 1 && s.Players.TryGetValue(s.ActiveTurnId, out var active))
            {
                GUILayout.BeginVertical(_turnPanel);
                GUILayout.Label("UP NEXT TO SHOOT", _eyebrow);
                var old = GUI.contentColor;
                GUI.contentColor = Color.Lerp(active.Color, Color.white, 0.35f);
                GUILayout.Label(active.Id == s.LocalId ? "Your turn" : Truncate(active.Name, 18) + "'s turn", _turnName);
                GUI.contentColor = old;
                GUILayout.EndVertical();
                GUILayout.Space(8f);
            }
            else
            {
                GUILayout.Label("Waiting for another golfer to join", _small);
            }
            GUILayout.Label($"PLAYERS  ·  {s.Players.Count}", _header);
            GUILayout.Space(4f);
            Vector3? me = LocalPlayer.InWorld ? LocalPlayer.Capture().Pos : (Vector3?)null;
            foreach (var info in s.Players.Values.OrderBy(p => p.Id))
            {
                RemotePlayer rp = null;
                string detail = "";
                if (info.Id != s.LocalId && RemoteWorld.Instance != null && RemoteWorld.Instance.Players.TryGetValue(info.Id, out rp))
                {
                    detail = RemoteWorld.Describe(rp);
                    if (me.HasValue && rp.HasPose)
                        detail += $" · {Vector3.Distance(me.Value, rp.Position):0}m";
                }

                GUILayout.BeginVertical(_playerRow);
                GUILayout.BeginHorizontal();
                var prev = GUI.contentColor;
                GUI.contentColor = Color.Lerp(info.Color, Color.white, 0.25f);
                GUILayout.Label("●", _label, GUILayout.Width(18f));
                GUI.contentColor = prev;

                string role = info.Id == Protocol.HostId ? " (host)" : "";
                GUILayout.Label(info.Id == s.LocalId ? $"{info.Name}{role} — you" : $"{info.Name}{role}", _label);
                GUILayout.FlexibleSpace();
                if (s.Players.Count > 1 && info.Id == s.ActiveTurnId)
                    GUILayout.Label("UP NEXT", _pill, GUILayout.Width(72f), GUILayout.Height(24f));
                if (info.Id != s.LocalId)
                {
                    GUI.enabled = rp != null && rp.HasPose && LocalPlayer.CanTeleport;
                    if (GUILayout.Button("Go to", _button, GUILayout.Width(65f), GUILayout.Height(26f)))
                    {
                        LocalPlayer.TeleportNear(rp.Position);
                        _menuOpen = false;
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
                if (info.Id != s.LocalId)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(string.IsNullOrEmpty(detail) ? "In menus" : detail, _small);
                    GUILayout.FlexibleSpace();
                    if (s.IsHost || info.Id == Protocol.HostId)
                        GUILayout.Label($"{s.GetPlayerPing(info.Id)} ms", _small);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }

            if (s.Players.Count > 1 && LocalPlayer.InWorld && !LocalPlayer.CanTeleport)
                GUILayout.Label("\"Go to\" works while you're walking (not golfing or in a cutscene).", _small);
            GUILayout.EndVertical();

            GUILayout.Space(10f);
            GUILayout.BeginVertical(_card);
            DrawScoreboard(s);
            GUILayout.EndVertical();

            GUILayout.Space(10f);
            if (GUILayout.Button(s.IsHost ? "Stop hosting" : "Leave session", _dangerButton, GUILayout.Width(180f), GUILayout.Height(36f)))
                s.Leave();
        }

        // ------------------------------------------------------------------ Front Nine scoreboard

        private const float ScoreNameWidth = 108f;
        private const float ScoreCellWidth = 26f;
        private const float ScoreTotalWidth = 44f;

        private struct ScoreRow
        {
            public string Name;
            public Color Color;
            public ScoreCard Card;
        }

        private List<ScoreRow> CollectScoreRows(NetSession s)
        {
            var rows = new List<ScoreRow>();
            var local = ScoreTracker.Instance;
            if (local != null && local.Local.HasRound)
                rows.Add(new ScoreRow { Name = s.LocalInfo.Name, Color = s.LocalInfo.Color, Card = local.Local });
            if (RemoteWorld.Instance != null)
            {
                foreach (var p in RemoteWorld.Instance.Players.Values.OrderBy(p => p.Info.Id))
                    if (p.Score != null && p.Score.HasRound)
                        rows.Add(new ScoreRow { Name = p.Info.Name, Color = p.Info.Color, Card = p.Score });
            }
            return rows;
        }

        private void DrawScoreboard(NetSession s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("FRONT NINE", _header);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{ModConfig.ScoreboardKey.Value} for overlay", _small);
            GUILayout.EndHorizontal();

            var rows = CollectScoreRows(s);
            if (rows.Count == 0)
            {
                GUILayout.Label("No round yet. Press the red button at the first tee to start one; scores appear here as holes are completed.", _small);
                return;
            }
            DrawScoreTable(rows);
        }

        private void DrawScoreTable(List<ScoreRow> rows)
        {
            int[] pars = ScoreTracker.Pars;

            GUILayout.BeginHorizontal();
            GUILayout.Label("HOLE", _scoreRowHead, GUILayout.Width(ScoreNameWidth));
            for (int h = 1; h <= ScoreCard.HoleCount; h++)
                GUILayout.Label(h.ToString(), _scoreHead, GUILayout.Width(ScoreCellWidth));
            GUILayout.Label("TOT", _scoreHead, GUILayout.Width(ScoreTotalWidth));
            GUILayout.Label("+/-", _scoreHead, GUILayout.Width(ScoreTotalWidth));
            GUILayout.EndHorizontal();

            if (pars != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("PAR", _scoreRowHead, GUILayout.Width(ScoreNameWidth));
                int parTotal = 0;
                for (int h = 1; h <= ScoreCard.HoleCount; h++)
                {
                    GUILayout.Label(pars[h - 1].ToString(), _scorePar, GUILayout.Width(ScoreCellWidth));
                    parTotal += pars[h - 1];
                }
                GUILayout.Label(parTotal.ToString(), _scorePar, GUILayout.Width(ScoreTotalWidth));
                GUILayout.Label("", _scorePar, GUILayout.Width(ScoreTotalWidth));
                GUILayout.EndHorizontal();
            }

            Color previous = GUI.contentColor;
            foreach (var row in rows)
            {
                var card = row.Card;
                GUILayout.BeginHorizontal();

                GUI.contentColor = Color.Lerp(row.Color, Color.white, 0.35f);
                GUILayout.Label(Truncate(row.Name, 13), _scoreName, GUILayout.Width(ScoreNameWidth));

                for (int h = 1; h <= ScoreCard.HoleCount; h++)
                {
                    int score = card.Scores[h - 1];
                    string text;
                    if (score > 0)
                    {
                        text = score.ToString();
                        GUI.contentColor = ScoreTracker.ColorFor(score, h);
                    }
                    else if (card.Active && card.CurrentHole == h)
                    {
                        // Playing this hole now: show strokes used so far.
                        text = card.Strokes > 0 ? card.Strokes.ToString() : "·";
                        GUI.contentColor = new Color(0.78f, 0.80f, 0.84f); // playing now, not a final score
                    }
                    else
                    {
                        text = "-";
                        GUI.contentColor = ScoreTracker.NoScore;
                    }
                    GUILayout.Label(text, _scoreCell, GUILayout.Width(ScoreCellWidth));
                }

                int played = card.PlayedCount;
                GUI.contentColor = card.Active ? Color.white : Accent;
                GUILayout.Label(played > 0 ? card.Total.ToString() : "-", _scoreCell, GUILayout.Width(ScoreTotalWidth));

                int diff = card.Total - ScoreTracker.ParForPlayed(card);
                string diffText = played == 0 || ScoreTracker.Pars == null ? "" : diff == 0 ? "E" : diff > 0 ? "+" + diff : diff.ToString();
                GUI.contentColor = diff < 0 ? ScoreTracker.UnderPar : diff > 0 ? ScoreTracker.OverPar : ScoreTracker.AtPar;
                GUILayout.Label(diffText, _scoreCell, GUILayout.Width(ScoreTotalWidth));

                GUILayout.EndHorizontal();
            }
            GUI.contentColor = previous;
        }

        /// <summary>Passive overlay: no controls, so it never steals the mouse while you play.</summary>
        private void DrawScoreboardOverlay(float screenW, List<ScoreRow> rows)
        {
            var session = NetSession.Instance;
            float width = ScoreNameWidth + ScoreCard.HoleCount * ScoreCellWidth + ScoreTotalWidth * 2f + 36f;
            // Generous row height: clipping a player's row is much worse than a little extra padding.
            const float titleHeight = 30f, rowHeight = 23f, padding = 30f;
            float height = titleHeight + rowHeight * (1 + (ScoreTracker.Pars != null ? 1 : 0) + rows.Count) + padding;
            var area = new Rect((screenW - width) * 0.5f, 8f, width, height);
            GUI.Box(area, GUIContent.none, _window);
            GUILayout.BeginArea(new Rect(area.x + 18f, area.y + 12f, area.width - 36f, area.height - 16f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("FRONT NINE", _header);
            GUILayout.FlexibleSpace();
            if (session.Players.Count > 1 && session.Players.TryGetValue(session.ActiveTurnId, out var active))
                GUILayout.Label("NEXT: " + (active.Id == session.LocalId ? "YOU" : Truncate(active.Name, 12)), _eyebrow);
            GUILayout.EndHorizontal();
            DrawScoreTable(rows);
            GUILayout.EndArea();
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        // ------------------------------------------------------------------ actions

        private void DoHost()
        {
            if (!TryParsePort(_hostPortField, out int port))
                return;
            ModConfig.HostPort.Value = port;
            ModConfig.Password.Value = _passwordField ?? "";
            string err = NetSession.Instance.Host(port);
            if (err != null)
                SetFeedback(err, error: true);
        }

        private void DoJoin()
        {
            string address = (_joinAddressField ?? "").Trim();
            string portText = _joinPortField;
            // Accept a pasted "ip:port" (IPv4 or hostname) or "[ipv6]:port" in the address box.
            int colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon && int.TryParse(address.Substring(colon + 1), out _))
            {
                portText = address.Substring(colon + 1);
                address = address.Substring(0, colon);
            }
            else if (address.StartsWith("[") && address.Contains("]:"))
            {
                int end = address.IndexOf("]:", StringComparison.Ordinal);
                portText = address.Substring(end + 2);
                address = address.Substring(1, end - 1);
            }
            _joinAddressField = address;
            _joinPortField = portText;

            if (!TryParsePort(portText, out int port))
                return;
            ModConfig.JoinAddress.Value = address;
            ModConfig.JoinPort.Value = port;
            ModConfig.Password.Value = _passwordField ?? "";
            string err = NetSession.Instance.Join(address, port);
            SetFeedback(err ?? $"Connecting to {address}:{port}...", error: err != null);
        }

        private bool TryParsePort(string text, out int port)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port >= 1 && port <= 65535)
                return true;
            SetFeedback("Port must be a number from 1 to 65535", error: true);
            return false;
        }

        private void SetFeedback(string text, bool error)
        {
            _feedback = text;
            _feedbackIsError = error;
        }

        // ------------------------------------------------------------------ helpers

        private string[] LanAddresses()
        {
            if (_lanAddresses != null)
                return _lanAddresses;
            var found = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            found.Add(ua.Address.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug("LAN address lookup failed: " + e.Message);
            }
            // Typical home-network ranges first; VPN/virtual adapters after.
            _lanAddresses = found.Distinct()
                .OrderBy(a => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : 2)
                .Take(3).ToArray();
            return _lanAddresses;
        }

        private static bool ColorsClose(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        private void Separator()
        {
            GUILayout.Space(6f);
            var r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
            GUILayout.Space(6f);
        }

        private void ShadowLabel(Rect r, string text, GUIStyle style, Color color)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.75f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = prev;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static Texture2D RoundedTex(Color color, int radius = 10)
        {
            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[size * size];
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - center) - (center - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - center) - (center - radius), 0f);
                    Color pixel = color;
                    pixel.a *= Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = pixel;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void EnsureStyles()
        {
            if (_window != null)
                return;
            _white = Tex(Color.white);
            var panel = RoundedTex(new Color(0.055f, 0.078f, 0.09f, 0.97f), 16);
            var card = RoundedTex(new Color(0.12f, 0.15f, 0.17f, 0.97f));
            var row = RoundedTex(new Color(0.18f, 0.21f, 0.23f, 0.92f), 8);
            var fieldBg = RoundedTex(new Color(0.18f, 0.22f, 0.24f, 1f), 7);
            var fieldFocus = RoundedTex(new Color(0.24f, 0.30f, 0.31f, 1f), 7);
            var btn = RoundedTex(new Color(0.23f, 0.31f, 0.31f, 1f), 8);
            var btnHover = RoundedTex(new Color(0.31f, 0.40f, 0.39f, 1f), 8);
            var btnActive = RoundedTex(new Color(0.17f, 0.24f, 0.24f, 1f), 8);
            var primary = RoundedTex(new Color(0.20f, 0.59f, 0.45f, 1f), 8);
            var primaryHover = RoundedTex(new Color(0.25f, 0.68f, 0.52f, 1f), 8);
            var danger = RoundedTex(new Color(0.36f, 0.19f, 0.20f, 1f), 8);
            var pill = RoundedTex(new Color(0.15f, 0.40f, 0.33f, 1f), 6);
            var turn = RoundedTex(new Color(0.07f, 0.24f, 0.23f, 0.97f), 10);

            _window = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panel },
                padding = new RectOffset(18, 18, 18, 16),
                border = new RectOffset(17, 17, 17, 17),
            };
            _card = new GUIStyle(GUI.skin.box)
            {
                normal = { background = card },
                border = new RectOffset(11, 11, 11, 11),
                padding = new RectOffset(14, 14, 12, 12),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _playerRow = new GUIStyle(_card)
            {
                normal = { background = row },
                border = new RectOffset(9, 9, 9, 9),
                padding = new RectOffset(8, 8, 7, 7),
                margin = new RectOffset(0, 0, 2, 2),
            };
            _turnPanel = new GUIStyle(_card)
            {
                normal = { background = turn },
                padding = new RectOffset(12, 12, 9, 9),
            };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _eyebrow = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
            _turnName = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _header = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, normal = { textColor = new Color(0.94f, 0.97f, 0.97f) } };
            _small = new GUIStyle(_label) { fontSize = 12, normal = { textColor = Muted } };
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(9, 9, 7, 7),
                normal = { background = fieldBg, textColor = Color.white },
                focused = { background = fieldFocus, textColor = Color.white },
                hover = { background = fieldFocus, textColor = Color.white },
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                border = new RectOffset(9, 9, 9, 9),
                padding = new RectOffset(10, 10, 6, 6),
                normal = { background = btn, textColor = Color.white },
                hover = { background = btnHover, textColor = Color.white },
                active = { background = btnActive, textColor = Color.white },
            };
            _bigButton = new GUIStyle(_button)
            {
                fontSize = 15, fontStyle = FontStyle.Bold, fixedHeight = 38f,
                normal = { background = primary, textColor = Color.white },
                hover = { background = primaryHover, textColor = Color.white },
            };
            _dangerButton = new GUIStyle(_button)
            {
                normal = { background = danger, textColor = Color.white },
                hover = { background = danger, textColor = Color.white },
            };
            var swatch = RoundedTex(Color.white, 6);
            _swatch = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                border = new RectOffset(7, 7, 7, 7),
                normal = { background = swatch, textColor = Color.black },
                hover = { background = swatch, textColor = Color.black },
                active = { background = swatch, textColor = Color.black },
                margin = new RectOffset(2, 2, 2, 2),
            };
            _pill = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(7, 7, 7, 7),
                normal = { background = pill, textColor = Accent },
            };
            _hud = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _chatStyle = new GUIStyle(_hud) { fontSize = 16, alignment = TextAnchor.UpperLeft };
            _scoreHead = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.62f, 0.66f, 0.7f) },
                margin = new RectOffset(0, 0, 1, 1), padding = new RectOffset(0, 0, 0, 0),
            };
            _scoreRowHead = new GUIStyle(_scoreHead) { alignment = TextAnchor.MiddleLeft };
            _scoreCell = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white },
                margin = new RectOffset(0, 0, 1, 1), padding = new RectOffset(0, 0, 0, 0),
            };
            _scorePar = new GUIStyle(_scoreCell) { fontSize = 13, normal = { textColor = new Color(0.86f, 0.78f, 0.45f) } };
            _scoreName = new GUIStyle(_scoreCell) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        }
    }
}
