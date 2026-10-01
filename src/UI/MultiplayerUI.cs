using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BepInEx;
using NormalGolfMultiplayer.Game;
using NormalGolfMultiplayer.Net;
using NormalGolfMultiplayer.Remote;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NormalGolfMultiplayer.UI
{
    /// <summary>
    /// The mod's uGUI on one persistent overlay canvas: a bottom-right session chip and a turn card below the wind,
    /// toasts, chat (T), the Front Nine scoreboard overlay (F9) and the tabbed multiplayer menu (F8).
    /// Styled with the game's own font and rounded panels so it reads as part of the game.
    /// </summary>
    internal class MultiplayerUI : MonoBehaviour
    {
        private static MultiplayerUI _instance;

        /// <summary>True while our menu or chat box owns the keyboard/mouse; game input is paused meanwhile.</summary>
        public static bool CapturingInput => _instance != null && (_instance._menuOpen || _instance._chatOpen);

        private const int ChatHistory = 40;
        private const float ToastLife = 6f;

        private bool _menuOpen;
        private bool _scoreboardOpen;
        private bool _chatOpen;
        private int _chatOpenedFrame;
        private int _tab;
        private float _hintUntil;
        private float _turnToastSuppressedUntil;
        private float _lastOutOfTurnNudge = -60f;
        private string _distanceText = "";
        private float _nextDistancePoll;

        private Canvas _canvas;
        private bool _builtWithoutFont;
        private RectTransform _hudRoot;
        private RectTransform _toastBase;
        private GameObject _hintChip, _statusChip, _turnCard;
        private Image _statusDot, _turnRing, _turnBar;
        private TextMeshProUGUI _hintLabel, _statusLabel, _turnEyebrow, _turnTitle, _turnSub;
        private string _statusCache = "", _turnCache = "";

        private readonly List<ToastView> _toasts = new List<ToastView>();
        private struct ToastView
        {
            public GameObject Root;
            public CanvasGroup Group;
            public float Born, Until;
        }

        private RectTransform _chatRoot;
        private Image _chatBar, _chatBackdrop;
        private TMP_InputField _chatField;
        private readonly List<ChatEntry> _chat = new List<ChatEntry>();
        private readonly List<TextMeshProUGUI> _chatPool = new List<TextMeshProUGUI>();
        private readonly List<ChatEntry> _chatVisible = new List<ChatEntry>();
        private struct ChatEntry
        {
            public string Rich;
            public float Time;
        }

        private RectTransform _scoreTable;
        private Image _scoreOverlay;
        private TextMeshProUGUI _scoreNext;
        private string _scoreOverlaySig;
        private float _nextScorePoll;

        private RectTransform _menuRoot;
        private Image _window;
        private TextMeshProUGUI _feedbackLabel;
        private string _feedback = "";
        private bool _feedbackIsError;
        private readonly TextMeshProUGUI[] _tabLabels = new TextMeshProUGUI[3];
        private readonly Image[] _tabUnderlines = new Image[3];
        private RectTransform _playPage, _roomPage, _scoresPage, _playOffline, _playOnline;
        private RectTransform _scoreTabTable;
        private TextMeshProUGUI _roomTurnName, _roomTurnSub;
        private TextMeshProUGUI _sessionPing;
        private readonly List<GameObject> _colourSelections = new List<GameObject>();
        private readonly Dictionary<byte, TextMeshProUGUI> _rowStatus = new Dictionary<byte, TextMeshProUGUI>();
        private readonly Dictionary<byte, TextMeshProUGUI> _rowPing = new Dictionary<byte, TextMeshProUGUI>();
        private string _playSig = "", _roomSig = "", _scoreTabSig = "";

        private string _nameField;
        private string _hostPortField;
        private string _joinAddressField;
        private string _joinPortField;
        private string _passwordField;
        private string[] _lanAddresses;

        private bool _captured;
        private CursorLockMode _savedLockMode;
        private bool _savedCursorVisible;

        public void Awake()
        {
            _instance = this;
            _nameField = ModConfig.PlayerName.Value;
            _hostPortField = ModConfig.HostPort.Value.ToString(CultureInfo.InvariantCulture);
            _joinAddressField = ModConfig.JoinAddress.Value;
            _joinPortField = ModConfig.JoinPort.Value.ToString(CultureInfo.InvariantCulture);
            _passwordField = ModConfig.Password.Value;
            _hintUntil = Time.unscaledTime + 25f;
        }

        public void Start()
        {
            var s = NetSession.Instance;
            s.ChatReceived += OnChat;
            s.ShotBlocked += OnShotBlocked;
            s.SessionEnded += reason =>
            {
                SetFeedback(reason, error: !reason.StartsWith("You left", StringComparison.Ordinal));
                Toast(reason, UiKit.Error);
            };
            s.SessionStarted += () =>
            {
                SetFeedback(s.IsHost ? "Hosting! Share your IP and port with friends." : "Connected!", error: false);
                _turnToastSuppressedUntil = Time.unscaledTime + 4f;
                _hintUntil = Time.unscaledTime + 12f;
            };
            s.TurnChanged += id =>
            {
                if (s.InSession && s.Players.Count > 1 && id == s.LocalId && Time.unscaledTime >= _turnToastSuppressedUntil)
                    Toast("Your turn to shoot", UiKit.Accent);
                _distanceText = "";
            };
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureEventSystem();
            EnsureCanvas();
            DevTools.ExtraCommands["uitree"] = _ => DumpUiTree();
            DevTools.ExtraCommands["holes"] = _ => DumpHoleState();
            DevTools.ExtraCommands["pin"] = _ =>
            {
                var ball = HitManager.instance != null && HitManager.instance.m_ball != null
                    ? LocalPlayer.Capture().BallPos
                    : (Vector3?)null;
                if (ball == null || !Game.LocalPlayer.TryGetHolePosition(ball.Value, out var pin))
                    return $"pin=? ball={ball}";
                return $"pin={pin} ball={ball.Value} dist={Vector3.Distance(ball.Value, pin):0.0}";
            };
        }

        /// <summary>Writes the whole canvas hierarchy with positions and texts, for remote debugging.</summary>
        private string DumpUiTree()
        {
            var sb = new System.Text.StringBuilder();
            if (_canvas == null)
                return "no canvas";
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                var rt = canvas.transform as RectTransform;
                sb.AppendLine($"canvas '{canvas.name}' order={canvas.sortingOrder} mode={canvas.renderMode} enabled={canvas.enabled} scale={(rt != null ? rt.lossyScale.ToString() : "?")}");
            }
            foreach (var es in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            {
                sb.AppendLine($"eventSystem '{es.name}' enabled={es.enabled} current={(EventSystem.current == es)}");
                foreach (var module in es.GetComponents<BaseInputModule>())
                {
                    var actions = module as InputSystemUIInputModule;
                    sb.AppendLine($"  module {module.GetType().Name} enabled={module.enabled} actions={(actions != null && actions.actionsAsset != null ? actions.actionsAsset.name : "default")}");
                }
            }
            var im = InputManager.instance;
            if (im != null && im.input != null && im.input.actions != null)
                foreach (var map in im.input.actions.actionMaps)
                    sb.AppendLine($"inputMap '{map.name}' enabled={map.enabled} activeActions={string.Join(",", map.actions.Where(a => a.enabled).Select(a => a.name))}");
            Walk(_canvas.transform, sb, 0);
            var path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "ngmp_debug", $"uitree_{DevTools.Tag}.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, sb.ToString());
            return path;
        }

        /// <summary>
        /// The current Front Nine hole, its cup, and every golfer's ball distance to it — the exact
        /// inputs the host's shot order compares, for checking that the order is right.
        /// </summary>
        private static string DumpHoleState()
        {
            var s = NetSession.Instance;
            var sb = new System.Text.StringBuilder();
            try
            {
                int hole = Game.ScoreTracker.Instance != null ? Game.ScoreTracker.Instance.Local.CurrentHole : 0;
                sb.Append($"hole={hole}");
                var ball = HitManager.instance != null ? HitManager.instance.m_ball : null;
                Vector3 localBall = ball != null ? LocalPlayer.Capture().BallPos : Vector3.zero;
                if (Game.LocalPlayer.TryGetHolePosition(localBall, out var pin))
                {
                    sb.Append($" pin={pin} localDist={Vector3.Distance(localBall, pin):0.00}");
                    if (Remote.RemoteWorld.Instance != null)
                        foreach (var rp in Remote.RemoteWorld.Instance.Players.Values)
                            if (rp.HasPose)
                                sb.Append($" | #{rp.Info.Id} {rp.Info.Name} ball={rp.BallPosition} dist={Vector3.Distance(rp.BallPosition, pin):0.00}");
                }
                else
                {
                    sb.Append(" pin=?(no round, no cup)");
                }
                sb.Append($" turn={s.ActiveTurnId}");
                sb.Append($" sharedFlag={(s.HasTurnHole ? s.TurnHole.ToString() : "unknown")}");
                foreach (var player in s.Players.Values)
                    if (s.TryGetTurnDistance(player.Id, out float distance))
                        sb.Append($" | authoritative #{player.Id}={distance:0.00}m");
            }
            catch (Exception e)
            {
                sb.Append(" !! " + e.Message);
            }
            return sb.ToString();
        }

        private static void Walk(Transform t, System.Text.StringBuilder sb, int depth)
        {
            var rt = (RectTransform)t;
            sb.Append(' ', depth * 2)
              .Append(t.name)
              .Append(t.gameObject.activeInHierarchy ? "" : " [INACTIVE]")
              .Append($" pos={rt.anchoredPosition} size={rt.sizeDelta} aMin={rt.anchorMin} piv={rt.pivot}");
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            sb.Append($" world=({corners[0].x:0},{corners[0].y:0})..({corners[2].x:0},{corners[2].y:0})");
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                var mat = tmp.fontSharedMaterial;
                sb.Append($" text=\"{tmp.text}\" color={tmp.color} font={(tmp.font != null ? tmp.font.name : "null")}");
                sb.Append($" mat={(mat != null ? mat.shader.name : "null")}");
            }
            var img = t.GetComponent<Image>();
            if (img != null)
                sb.Append($" img={img.color} sprite={(img.sprite != null ? img.sprite.name : "null")}");
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), sb, depth + 1);
        }

        public void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_canvas != null)
                Destroy(_canvas.gameObject);
            if (_instance == this)
                _instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureEventSystem();
            // The game's font only exists once its own UI has loaded; rebuild the canvas once we can find it.
            UiKit.Prepare();
            if (_canvas != null && _builtWithoutFont && UiKit.Font != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                EnsureCanvas();
            }
        }

        public static void SetMenuOpen(bool open)
        {
            if (_instance == null || _instance._menuOpen == open)
                return;
            _instance._menuOpen = open;
            if (open)
            {
                _instance._chatOpen = false;
                var s = NetSession.Instance;
                _instance.SelectTab(s != null && s.InSession ? 1 : 0);
                _instance.ShowMenu(true);
            }
            else
            {
                _instance.ShowMenu(false);
            }
        }

        public static void SetScoreboardOpen(bool open)
        {
            if (_instance != null)
                _instance._scoreboardOpen = open;
        }

        public static void Toast(string text, Color color)
        {
            if (_instance == null || _instance._hudRoot == null)
                return;
            _instance.AddToast(text, color);
        }

        // ------------------------------------------------------------------ setup

        private static void EnsureEventSystem()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null)
                {
                    var go = new GameObject("NGMP EventSystem");
                    DontDestroyOnLoad(go);
                    es = go.AddComponent<EventSystem>();
                }
                if (es.GetComponent<InputSystemUIInputModule>() == null)
                    es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not set up the UI event system: " + e.Message);
            }
        }

        private void EnsureCanvas()
        {
            if (_canvas != null)
                return;
            UiKit.Prepare();
            _builtWithoutFont = UiKit.Font == null;
            // A root canvas survives scene loads and always draws above the game's own UI.
            var go = new GameObject("NGMP Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(go);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30000;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = 0.5f;

            BuildHud();
            BuildChat();
            BuildScoreOverlay();
            BuildMenu();
        }

        // ------------------------------------------------------------------ HUD

        private void BuildHud()
        {
            // Reserve the top strip for the game's wind indicator; shot notifications share this column.
            _hudRoot = UiKit.Rect(_canvas.transform, "HUD", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, 160f), Vector2.zero);

            // Bottom-right slot (the bank-bar corner): the offline hint and the session chip never
            // show at the same time, so they share one anchored position.
            _hintChip = UiKit.PanelImage(_canvas.transform, "Hint", UiKit.Panel, 14, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f, 12f), new Vector2(340f, 40f)).gameObject;
            _hintLabel = UiKit.Label(_hintChip.transform, "Text", "", 14, UiKit.TextDim, TextAnchor.MiddleLeft);
            Place(_hintLabel, 14f, 0f, 312f, 40f, middle: true);

            _statusChip = UiKit.PanelImage(_canvas.transform, "Status", UiKit.Panel, 14, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f, 12f), new Vector2(360f, 40f)).gameObject;
            _statusDot = Dot(_statusChip.transform, 16f);
            _statusLabel = UiKit.Label(_statusChip.transform, "Text", "", 14, UiKit.Text, TextAnchor.MiddleLeft);
            Place(_statusLabel, 34f, 0f, 316f, 40f, middle: true);

            _turnCard = UiKit.PanelImage(_hudRoot, "Turn", UiKit.Panel, 16, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(360f, 92f)).gameObject;
            var ringRt = UiKit.Stretch(_turnCard.transform, "Ring"); // child: a GameObject can hold only one Graphic
            _turnRing = ringRt.gameObject.AddComponent<Image>();
            _turnRing.sprite = UiKit.Ring(16);
            _turnRing.type = Image.Type.Sliced;
            _turnRing.color = new Color(1f, 1f, 1f, 0f);
            _turnRing.raycastTarget = false;
            _turnBar = UiKit.PanelImage(_turnCard.transform, "Bar", UiKit.Accent, 3, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(5f, 72f));
            _turnEyebrow = UiKit.Label(_turnCard.transform, "Eyebrow", "", 12, UiKit.TextMuted, TextAnchor.MiddleLeft, bold: true);
            _turnEyebrow.characterSpacing = 6f;
            Place(_turnEyebrow, 26f, 10f, 320f, 16f);
            _turnTitle = UiKit.Label(_turnCard.transform, "Title", "", 20, UiKit.Text, TextAnchor.MiddleLeft, bold: true);
            Place(_turnTitle, 26f, 29f, 322f, 26f);
            _turnSub = UiKit.Label(_turnCard.transform, "Sub", "", 13, UiKit.TextDim, TextAnchor.MiddleLeft);
            Place(_turnSub, 26f, 62f, 322f, 18f);

            _toastBase = UiKit.Rect(_hudRoot, "Toasts", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        }

        private static Image Dot(Transform parent, float x)
        {
            var dot = UiKit.PanelImage(parent, "Dot", UiKit.Accent, 5, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(x, 0f), new Vector2(10f, 10f));
            return dot;
        }

        private static void Place(TMP_Text tmp, float x, float y, float w, float h, bool middle = false)
        {
            var rt = (RectTransform)tmp.transform;
            rt.anchorMin = middle ? new Vector2(0f, 0.5f) : Vector2.up;
            rt.anchorMax = rt.anchorMin;
            rt.pivot = middle ? new Vector2(0f, 0.5f) : Vector2.up;
            rt.anchoredPosition = new Vector2(x, middle ? y : -y); // top-anchored: y measures downward
            // Never shorter than one line: TMP's ellipsis drops a whole line that does not fit vertically.
            rt.sizeDelta = new Vector2(w, Mathf.Max(h, UiKit.MinLineHeight(tmp)));
        }

        public static void ShotToast(string text, Color color)
        {
            if (_instance != null) _instance.AddToast(text, color, detailed: true);
        }

        private void AddToast(string text, Color color, bool detailed = false)
        {
            if (_toasts.Count >= 5)
            {
                var oldest = _toasts[0];
                if (oldest.Root != null)
                    Destroy(oldest.Root);
                _toasts.RemoveAt(0);
            }

            float width = detailed ? 430f : 360f;
            float height = detailed ? 88f : 42f;
            var bg = UiKit.PanelImage(_toastBase, "Toast", UiKit.Panel, 14, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(width, height));
            var dot = Dot(bg.transform, 14f);
            dot.color = color;
            var label = UiKit.Label(bg.transform, "Text", detailed ? text : Truncate(text, 42), 14,
                Color.Lerp(color, Color.white, 0.55f), TextAnchor.MiddleLeft);
            Place(label, 32f, 0f, width - 44f, height, middle: true);
            var group = bg.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            _toasts.Add(new ToastView { Root = bg.gameObject, Group = group, Born = Time.unscaledTime,
                Until = Time.unscaledTime + (detailed ? 10f : ToastLife) });
        }

        private void LayoutHud()
        {
            if (_hudRoot == null)
                return;
            var s = NetSession.Instance;
            bool inSession = s != null && s.InSession;

            // The bottom-right chips hold their fixed slot; the top-left column is just the turn card.
            bool hint = !inSession && s != null && s.Mode == SessionMode.Offline && Time.unscaledTime < _hintUntil;
            _hintChip.SetActive(hint);
            _statusChip.SetActive(s != null && s.Mode != SessionMode.Offline);

            bool turn = inSession && s.Players.Count > 1;
            _turnCard.SetActive(turn);
            ((RectTransform)_toastBase.transform).anchoredPosition = new Vector2(0f, turn ? -102f : -8f);
        }

        private void RefreshHud()
        {
            var s = NetSession.Instance;
            if (s == null || _statusLabel == null)
                return;

            string status;
            Color dot;
            switch (s.Mode)
            {
                case SessionMode.Offline:
                    status = $"MULTIPLAYER  ·  PRESS {ModConfig.MenuKey.Value}";
                    dot = UiKit.TextMuted;
                    break;
                case SessionMode.Connecting:
                    status = "CONNECTING…";
                    dot = UiKit.Warning;
                    break;
                default:
                    int n = s.Players.Count;
                    string who = n == 1 ? "JUST YOU" : $"{n} GOLFERS";
                    status = s.IsHost ? $"HOSTING  ·  {who}" : $"CONNECTED  ·  {who}  ·  {s.PingMs} MS";
                    dot = s.IsHost ? UiKit.Accent : new Color(0.45f, 0.75f, 1f);
                    break;
            }
            if (status != _statusCache)
            {
                _statusCache = status;
                _statusLabel.text = status;
                _statusDot.color = dot;
                _hintLabel.text = $"MULTIPLAYER  ·  PRESS {ModConfig.MenuKey.Value}";
            }

            RefreshTurnCard(s);
        }

        private void RefreshTurnCard(NetSession s)
        {
            bool show = s.InSession && s.Players.Count > 1;
            if (!show)
            {
                if (_turnCache != "")
                    _turnCache = "";
                return;
            }

            string distance = _distanceText;
            if (Time.unscaledTime >= _nextDistancePoll)
            {
                _nextDistancePoll = Time.unscaledTime + 1f;
                distance = ResolvePinDistance();
                _distanceText = distance;
            }

            string key;
            string eyebrow, title, sub;
            Color titleColor = UiKit.Text, barColor = UiKit.Accent;
            if (!s.Players.TryGetValue(s.ActiveTurnId, out var active))
            {
                key = "waiting";
                eyebrow = "SHOT ORDER";
                title = "Waiting for the next shot";
                sub = "Waiting for golfers and balls to be ready";
                barColor = UiKit.TextMuted;
            }
            else if (active.Id == s.LocalId)
            {
                key = "me|" + distance;
                eyebrow = "YOUR TURN";
                title = "You're up";
                sub = string.IsNullOrEmpty(distance) ? "Play when ready" : distance;
            }
            else
            {
                key = "other|" + active.Id + "|" + active.Name + "|" + active.Color + "|" + distance;
                eyebrow = "UP NEXT";
                title = Truncate(active.Name, 20) + "'s turn";
                sub = string.IsNullOrEmpty(distance) ? "Everyone tees off, then farthest plays" : distance;
                titleColor = Color.Lerp((Color)active.Color, Color.white, 0.35f);
                barColor = active.Color;
            }

            if (key != _turnCache)
            {
                _turnCache = key;
                _turnEyebrow.text = eyebrow;
                _turnEyebrow.color = active != null && active.Id == s.LocalId && s.Players.ContainsKey(s.ActiveTurnId)
                    ? UiKit.Accent
                    : UiKit.TextMuted;
                _turnTitle.text = title;
                _turnTitle.color = titleColor;
                _turnSub.text = sub;
                _turnBar.color = barColor;
            }

            bool mine = s.Players.TryGetValue(s.ActiveTurnId, out var me2) && me2.Id == s.LocalId;
            float pulse = mine ? 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
            _turnRing.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, pulse);
        }

        private string ResolvePinDistance()
        {
            var s = NetSession.Instance;
            return s != null && s.InSession && s.TryGetTurnDistance(s.ActiveTurnId, out float distance)
                ? $"Ball: {distance:0.0} m to the flag" : "";
        }

        private void RefreshToasts()
        {
            if (_toasts.Count == 0)
                return;
            float now = Time.unscaledTime;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                if (t.Root == null)
                {
                    _toasts.RemoveAt(i);
                    continue;
                }
                if (now > t.Until)
                {
                    Destroy(t.Root);
                    _toasts.RemoveAt(i);
                    continue;
                }
                float age = now - t.Born;
                float left = t.Until - now;
                t.Group.alpha = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01(left / 0.6f);
                var rt = (RectTransform)t.Root.transform;
                float top = 0f;
                for (int previous = 0; previous < i; previous++)
                    if (_toasts[previous].Root != null)
                        top += ((RectTransform)_toasts[previous].Root.transform).sizeDelta.y + 8f;
                rt.anchoredPosition = new Vector2(Mathf.Lerp(-30f, 0f, Mathf.Clamp01(age / 0.18f)), -top);
            }
        }

        // ------------------------------------------------------------------ chat

        private void BuildChat()
        {
            _chatRoot = UiKit.Rect(_canvas.transform, "Chat", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 150f), Vector2.zero);

            // Created before the pooled lines, so it renders behind them; sized to the visible history.
            _chatBackdrop = UiKit.PanelImage(_chatRoot, "Backdrop", new Color(0.035f, 0.06f, 0.07f, 0.86f), 12,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(-6f, 60f), new Vector2(572f, 10f));
            _chatBackdrop.gameObject.SetActive(false);

            _chatBar = UiKit.PanelImage(_chatRoot, "Bar", UiKit.Panel, 12, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(560f, 44f));
            _chatField = UiKit.InputField(_chatBar.transform, "Field", new Vector2(4f, 4f), new Vector2(552f, 36f),
                "Say something…  (Enter to send, Esc to close)", 15f);
            _chatField.onSubmit.AddListener(text =>
            {
                NetSession.Instance.SendChat(text);
                CloseChat();
            });
        }

        private void OnChat(byte id, string text)
        {
            var s = NetSession.Instance;
            PlayerInfo sender = null;
            if (s != null)
                s.Players.TryGetValue(id, out sender);
            string name = sender != null ? sender.Name : "?";
            Color color = sender != null ? (Color)sender.Color : Color.white;
            string html = UiKit.Html(Color.Lerp(color, Color.white, 0.3f));
            _chat.Add(new ChatEntry { Rich = $"<color={html}>{name}</color>  {Protocol.Clean(text, Protocol.MaxChatLength)}", Time = Time.unscaledTime });
            if (_chat.Count > ChatHistory)
                _chat.RemoveAt(0);
        }

        private void TryOpenChat()
        {
            if (NetSession.Instance.InSession)
            {
                _menuOpen = false;
                ShowMenu(false);
                _chatOpen = true;
                _chatOpenedFrame = Time.frameCount;
                StartCoroutine(ActivateChatNextFrame());
            }
            else
            {
                Toast("Host or join a game to chat", UiKit.TextDim);
            }
        }

        private System.Collections.IEnumerator ActivateChatNextFrame()
        {
            yield return null; // let the key that opened chat finish, or it types a "t" into the box
            if (_chatField != null && _chatOpen)
            {
                _chatField.text = "";
                _chatField.ActivateInputField();
            }
        }

        private void CloseChat()
        {
            _chatOpen = false;
            if (_chatField != null)
                _chatField.DeactivateInputField();
        }

        private void RefreshChat()
        {
            if (_chatRoot == null)
                return;
            float now = Time.unscaledTime;
            _chatVisible.Clear();
            int max = _chatOpen ? 10 : 6;
            for (int i = _chat.Count - 1; i >= 0 && _chatVisible.Count < max; i--)
            {
                var e = _chat[i];
                if (!_chatOpen && now - e.Time > 12f)
                    break;
                _chatVisible.Add(e);
            }
            _chatVisible.Reverse();

            float y = 64f;
            for (int i = 0; i < _chatVisible.Count; i++)
            {
                var line = ChatLine(i);
                var e = _chatVisible[i];
                line.text = e.Rich;
                float alpha = _chatOpen ? 1f : Mathf.Clamp01((12f - (now - e.Time)) / 1.5f);
                line.color = new Color(1f, 1f, 1f, alpha);
                line.gameObject.SetActive(true);
                ((RectTransform)line.transform).anchoredPosition = new Vector2(0f, y);
                y += 24f;
            }
            for (int i = _chatVisible.Count; i < _chatPool.Count; i++)
                if (_chatPool[i] != null)
                    _chatPool[i].gameObject.SetActive(false);

            if (_chatBar != null)
                _chatBar.gameObject.SetActive(_chatOpen);
            if (_chatBackdrop != null)
            {
                // Faded panel behind the lines, also while closed: chat otherwise sits straight on the
                // game's own bottom-left HUD and is unreadable.
                bool showBackdrop = _chatOpen || _chatVisible.Count > 0;
                _chatBackdrop.gameObject.SetActive(showBackdrop);
                _chatBackdrop.color = new Color(0.035f, 0.06f, 0.07f, _chatOpen ? 0.86f : 0.55f);
                if (showBackdrop)
                {
                    var backdropRt = (RectTransform)_chatBackdrop.transform;
                    backdropRt.sizeDelta = new Vector2(572f, 12f + _chatVisible.Count * 24f);
                }
            }
        }

        private TextMeshProUGUI ChatLine(int index)
        {
            while (_chatPool.Count <= index)
            {
                var tmp = UiKit.Label(_chatRoot, "Line", "", 16, UiKit.Text, TextAnchor.MiddleLeft);
                var rt = (RectTransform)tmp.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(720f, 24f);
                _chatPool.Add(tmp);            }
            return _chatPool[index];
        }

        // ------------------------------------------------------------------ scoreboard

        private struct ScoreRow
        {
            public string Name;
            public Color Color;
            public ScoreCard Card;
        }

        private void BuildScoreOverlay()
        {
            _scoreOverlay = UiKit.PanelImage(_canvas.transform, "ScoreOverlay", UiKit.Panel, 16, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 10f), new Vector2(716f, 200f));
            var title = UiKit.Label(_scoreOverlay.transform, "Title", "FRONT NINE", 15, UiKit.Accent, TextAnchor.MiddleLeft, bold: true);
            title.characterSpacing = 4f;
            Place(title, 20f, 14f, 300f, 22f);
            _scoreNext = UiKit.Label(_scoreOverlay.transform, "Next", "", 12, UiKit.TextDim, TextAnchor.MiddleRight);
            var rt = (RectTransform)_scoreNext.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-20f, -14f);
            rt.sizeDelta = new Vector2(320f, Mathf.Max(22f, UiKit.MinLineHeight(_scoreNext)));
            _scoreTable = UiKit.Rect(_scoreOverlay.transform, "Table", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, 44f), Vector2.zero);
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

        private string ScoreSignature(NetSession s)
        {
            var sb = new System.Text.StringBuilder();
            if (ScoreTracker.Pars != null)
                sb.Append(string.Join(",", ScoreTracker.Pars));
            foreach (var row in CollectScoreRows(s))
            {
                sb.Append('|').Append(row.Name).Append(',').Append(UiKit.Html(row.Color)).Append(',')
                  .Append(row.Card.RoundId).Append(',').Append(row.Card.Active ? 1 : 0).Append(',')
                  .Append(row.Card.CurrentHole).Append(',').Append(row.Card.Strokes);
                foreach (byte v in row.Card.Scores)
                    sb.Append(',').Append(v);
            }
            return sb.ToString();
        }

        private void RefreshScoreOverlay()
        {
            if (_scoreOverlay == null)
                return;
            var s = NetSession.Instance;
            bool show = _scoreboardOpen && !_menuOpen && s != null;
            _scoreOverlay.gameObject.SetActive(show);
            if (!show || Time.unscaledTime < _nextScorePoll)
                return;
            _nextScorePoll = Time.unscaledTime + 0.5f;

            string sig = ScoreSignature(s);
            if (sig == _scoreOverlaySig)
                return;
            _scoreOverlaySig = sig;

            var rows = CollectScoreRows(s);
            if (rows.Count == 0)
            {
                UiKit.DestroyChildren(_scoreTable);
                var none = UiKit.Label(_scoreTable, "None", "No round yet — press the red button at the first tee to start one.", 13, UiKit.TextDim);
                Place(none, 0f, 0f, 676f, 24f);
                _scoreNext.text = "";
                ((RectTransform)_scoreOverlay.transform).sizeDelta = new Vector2(716f, 96f);
                return;
            }

            float h = BuildScoreTable(_scoreTable, rows);
            if (s.Players.Count > 1 && s.Players.TryGetValue(s.ActiveTurnId, out var active))
                _scoreNext.text = "NEXT  ·  " + (active.Id == s.LocalId ? "YOU" : Truncate(active.Name, 14).ToUpperInvariant());
            else
                _scoreNext.text = "";
            ((RectTransform)_scoreOverlay.transform).sizeDelta = new Vector2(716f, 56f + h);
        }

        /// <summary>Draws the Front Nine table and returns its height.</summary>
        private float BuildScoreTable(RectTransform container, List<ScoreRow> rows, float width = 676f)
        {
            UiKit.DestroyChildren(container);
            const float cellW = 42f, totW = 52f, rowH = 30f, headH = 26f;
            float nameW = width - 8f - ScoreCard.HoleCount * cellW - 2f * totW;
            float cellX(int i) => nameW + 8f + i * cellW;
            float totX = nameW + 8f + 9 * cellW;
            float diffX = totX + totW;

            Cell(container, "HOLE", 0f, 0f, nameW, headH, UiKit.TextMuted, TextAnchor.MiddleLeft, 12, bold: true);
            for (int h = 1; h <= ScoreCard.HoleCount; h++)
                Cell(container, h.ToString(), cellX(h - 1), 0f, cellW, headH, UiKit.TextMuted, TextAnchor.MiddleCenter, 12, bold: true);
            Cell(container, "TOT", totX, 0f, totW, headH, UiKit.TextMuted, TextAnchor.MiddleCenter, 12, bold: true);
            Cell(container, "+/-", diffX, 0f, totW, headH, UiKit.TextMuted, TextAnchor.MiddleCenter, 12, bold: true);

            float y = headH;
            int[] pars = ScoreTracker.Pars;
            if (pars != null)
            {
                Cell(container, "PAR", 0f, y, nameW, headH, UiKit.TextDim, TextAnchor.MiddleLeft, 13);
                int parTotal = 0;
                for (int h = 1; h <= ScoreCard.HoleCount; h++)
                {
                    Cell(container, pars[h - 1].ToString(), cellX(h - 1), y, cellW, headH, UiKit.TextDim, TextAnchor.MiddleCenter, 13);
                    parTotal += pars[h - 1];
                }
                Cell(container, parTotal.ToString(), totX, y, totW, headH, UiKit.TextDim, TextAnchor.MiddleCenter, 13);
                y += headH;
            }

            foreach (var row in rows)
            {
                var card = row.Card;
                Cell(container, Truncate(row.Name, 14), 0f, y, nameW, rowH, Color.Lerp(row.Color, Color.white, 0.35f), TextAnchor.MiddleLeft, 14);
                for (int h = 1; h <= ScoreCard.HoleCount; h++)
                {
                    int score = card.Scores[h - 1];
                    string text;
                    Color color;
                    if (score > 0)
                    {
                        text = score.ToString();
                        color = ScoreTracker.ColorFor(score, h);
                    }
                    else if (card.Active && card.CurrentHole == h)
                    {
                        text = card.Strokes > 0 ? card.Strokes.ToString() : "·";
                        color = new Color(0.78f, 0.80f, 0.84f); // playing now, not a final score
                    }
                    else
                    {
                        text = "-";
                        color = ScoreTracker.NoScore;
                    }
                    Cell(container, text, cellX(h - 1), y, cellW, rowH, color, TextAnchor.MiddleCenter, 15);
                }

                int played = card.PlayedCount;
                Cell(container, played > 0 ? card.Total.ToString() : "-", totX, y, totW, rowH,
                    card.Active ? UiKit.Text : UiKit.Accent, TextAnchor.MiddleCenter, 15);

                int diff = card.Total - ScoreTracker.ParForPlayed(card);
                string diffText = played == 0 || ScoreTracker.Pars == null
                    ? ""
                    : diff == 0 ? "E" : diff > 0 ? "+" + diff : diff.ToString();
                Color diffColor = diff < 0 ? ScoreTracker.UnderPar : diff > 0 ? ScoreTracker.OverPar : ScoreTracker.AtPar;
                Cell(container, diffText, diffX, y, totW, rowH, diffColor, TextAnchor.MiddleCenter, 15);
                y += rowH;
            }
            return y;
        }

        private static TextMeshProUGUI Cell(Transform parent, string text, float x, float y, float w, float h,
            Color color, TextAnchor align, float size, bool bold = false)
        {
            var tmp = UiKit.Label(parent, "Cell", text, size, color, align, bold);
            var rt = (RectTransform)tmp.transform;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, Mathf.Max(h, UiKit.MinLineHeight(tmp)));
            return tmp;
        }

        // ------------------------------------------------------------------ menu

        private void BuildMenu()
        {
            _menuRoot = UiKit.Rect(_canvas.transform, "Menu", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            _menuRoot.anchorMin = Vector2.zero;
            _menuRoot.anchorMax = Vector2.one;
            _menuRoot.offsetMin = _menuRoot.offsetMax = Vector2.zero;

            // Dim + window are siblings: a click on the window must not bubble to the dim's close button.
            var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            var dimRt = (RectTransform)dimGo.transform;
            dimRt.SetParent(_menuRoot, false);
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
            var dim = dimGo.GetComponent<Image>();
            dim.sprite = UiKit.Round(1);
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            var dimButton = dimGo.GetComponent<Button>();
            dimButton.targetGraphic = dim;
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(() => SetMenuOpen(false));

            const float w = 660f, h = 780f;
            _window = UiKit.PanelImage(_menuRoot, "Window", UiKit.Panel, 20, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(w, h), raycast: true);

            var title = UiKit.Label(_window.transform, "Title", "MULTIPLAYER", 24, UiKit.Text, TextAnchor.MiddleLeft, bold: true);
            title.characterSpacing = 3f;
            Place(title, 26f, 20f, 400f, 34f);

            var version = UiKit.Label(_window.transform, "Version", "v" + Plugin.Version, 12, UiKit.TextMuted, TextAnchor.MiddleLeft);
            Place(version, 26f, 54f, 200f, 18f);

            var (closeButton, _) = UiKit.Button(_window.transform, "Close", "×", new Vector2(w - 50f, 18f), new Vector2(34f, 34f),
                UiKit.ButtonVariant.Ghost, 20f);
            closeButton.onClick.AddListener(() => SetMenuOpen(false));

            string[] tabs = { "PLAY", "ROOM", "SCORES" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var (button, label) = UiKit.Button(_window.transform, "Tab" + i, tabs[i], new Vector2(20f + i * 208f, 86f),
                    new Vector2(200f, 38f), UiKit.ButtonVariant.Ghost, 15f);
                int index = i;
                button.onClick.AddListener(() => SelectTab(index));
                _tabLabels[i] = label;
                _tabUnderlines[i] = UiKit.PanelImage(_window.transform, "Underline" + i, UiKit.Accent, 2, new Vector2(0f, 1f),
                    new Vector2(0f, 1f), new Vector2(20f + i * 208f, 124f), new Vector2(200f, 3f));
            }

            _playPage = UiKit.Rect(_window.transform, "PlayPage", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, 138f), new Vector2(w - 32f, 550f));
            _playOffline = UiKit.Rect(_playPage, "Offline", Vector2.up, Vector2.up, Vector2.zero, _playPage.sizeDelta);
            _playOnline = UiKit.Rect(_playPage, "Online", Vector2.up, Vector2.up, Vector2.zero, _playPage.sizeDelta);
            _roomPage = UiKit.Rect(_window.transform, "RoomPage", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, 138f), new Vector2(w - 32f, 500f));
            _scoresPage = UiKit.Rect(_window.transform, "ScoresPage", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, 138f), new Vector2(w - 32f, 500f));
            var scoresTitle = UiKit.Label(_scoresPage, "ScoresTitle", "FRONT NINE", 14, UiKit.Accent, TextAnchor.MiddleLeft, bold: true);
            scoresTitle.characterSpacing = 4f;
            Place(scoresTitle, 4f, 0f, 300f, 22f);
            var scoresHint = UiKit.Label(_scoresPage, "ScoresHint", Truncate($"{ModConfig.ScoreboardKey.Value} shows this as an overlay during play", 60), 12, UiKit.TextMuted, TextAnchor.MiddleRight);
            var hintRt = (RectTransform)scoresHint.transform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(1f, 1f);
            hintRt.pivot = new Vector2(1f, 1f);
            hintRt.anchoredPosition = new Vector2(-4f, 0f);
            hintRt.sizeDelta = new Vector2(420f, 22f);
            _scoreTabTable = UiKit.Rect(_scoresPage, "Table", Vector2.up, Vector2.up, new Vector2(0f, 30f), Vector2.zero);

            _feedbackLabel = UiKit.Label(_window.transform, "Feedback", "", 14, UiKit.Accent, TextAnchor.MiddleLeft);
            Place(_feedbackLabel, 26f, h - 74f, w - 52f, 22f);

            var footer = UiKit.Label(_window.transform, "Footer", "", 12, UiKit.TextMuted, TextAnchor.MiddleCenter);
            footer.text = $"{ModConfig.MenuKey.Value} menu  ·  {ModConfig.ChatKey.Value} chat  ·  {ModConfig.ScoreboardKey.Value} scores";
            Place(footer, 0f, h - 40f, w, 22f);

            ShowMenu(false);
            SelectTab(0);
        }

        private void ShowMenu(bool show)
        {
            if (_menuRoot == null)
                return;
            _menuRoot.gameObject.SetActive(show);
        }

        private void SelectTab(int index)
        {
            _tab = index;
            for (int i = 0; i < 3; i++)
            {
                if (_tabLabels[i] != null)
                    _tabLabels[i].color = i == index ? UiKit.Text : UiKit.TextDim;
                if (_tabUnderlines[i] != null)
                    _tabUnderlines[i].gameObject.SetActive(i == index);
            }
            if (_playPage != null)
            {
                _playPage.gameObject.SetActive(index == 0);
                _roomPage.gameObject.SetActive(index == 1);
                _scoresPage.gameObject.SetActive(index == 2);
            }
            _playSig = _roomSig = _scoreTabSig = "";
        }

        private void RefreshMenu()
        {
            if (!_menuOpen || _menuRoot == null)
                return;
            var s = NetSession.Instance;

            string feedbackColor = _feedbackIsError ? UiKit.Html(UiKit.Error) : UiKit.Html(UiKit.Accent);
            string feedbackText = string.IsNullOrEmpty(_feedback)
                ? ""
                : $"<color={feedbackColor}>{_feedback}</color>";
            if (_feedbackLabel.text != feedbackText)
                _feedbackLabel.text = feedbackText;

            string playSig = s.Mode.ToString() + "|" + (s.InSession ? "in" : "off");
            if (playSig != _playSig)
            {
                _playSig = playSig;
                RebuildPlayPage(s);
            }
            if (_sessionPing != null)
            {
                string ping = $"PING  ·  {s.PingMs} MS";
                if (_sessionPing.text != ping)
                    _sessionPing.text = ping;
            }
            RefreshColourSelection();

            if (_tab == 1)
            {
                string roomSig = RoomSignature(s);
                if (roomSig != _roomSig)
                {
                    _roomSig = roomSig;
                    RebuildRoomPage(s);
                }
                RefreshRoomRows(s);
                RefreshRoomTurn(s);
            }

            if (_tab == 2)
            {
                string sig = ScoreSignature(s);
                if (sig != _scoreTabSig)
                {
                    _scoreTabSig = sig;
                    var rows = CollectScoreRows(s);
                    if (rows.Count == 0)
                    {
                        UiKit.DestroyChildren(_scoreTabTable);
                        var hint = UiKit.Label(_scoreTabTable, "None", "No round yet. Press the red button at the first tee to start one.", 14, UiKit.TextDim);
                        Place(hint, 4f, 4f, 600f, 26f);
                    }
                    else
                    {
                        BuildScoreTable(_scoreTabTable, rows, _scoresPage.sizeDelta.x);
                    }
                }
            }
        }

        private string RoomSignature(NetSession s)
        {
            // Ping and remote status change every moment; they are updated in place rather than rebuilding
            // the page, which would recreate the rows (and their buttons) under the user's cursor.
            var sb = new System.Text.StringBuilder(s.ActiveTurnId.ToString()).Append('|').Append(LocalPlayer.CanTeleport);
            foreach (var p in s.Players.Values.OrderBy(p => p.Id))
                sb.Append('|').Append(p.Id).Append(',').Append(p.Name).Append(',').Append(UiKit.Html(p.Color));
            return sb.ToString();
        }

        private void RebuildPlayPage(NetSession s)
        {
            UiKit.DestroyChildren(_playOffline);
            UiKit.DestroyChildren(_playOnline);
            _sessionPing = null;
            bool offline = s.Mode == SessionMode.Offline;
            _playOffline.gameObject.SetActive(offline);
            _playOnline.gameObject.SetActive(!offline);

            if (offline)
            {
                float y = 0f;
                BuildIdentityCard(_playOffline, s, y);
                y += 152f;
                BuildHostCard(_playOffline, y);
                y += 206f;
                BuildJoinCard(_playOffline, y);
            }
            else
            {
                BuildSessionCard(_playOnline, s);
                BuildIdentityCard(_playOnline, s, 158f);
                var (leave, _) = UiKit.Button(_playOnline, "Leave", s.IsHost ? "STOP HOSTING" : "LEAVE SESSION",
                    new Vector2(0f, 310f), new Vector2(200f, 40f), UiKit.ButtonVariant.Danger, 14f);
                leave.onClick.AddListener(() => NetSession.Instance.Leave());
            }
        }

        private void BuildIdentityCard(Transform parent, NetSession s, float y)
        {
            var card = UiKit.PanelImage(parent, "Identity", UiKit.Card, 14, Vector2.up, Vector2.up, new Vector2(0f, y),
                new Vector2(_playPage.sizeDelta.x, 140f), raycast: true);
            Header(card.transform, "YOUR GOLFER", "Choose how friends see you");

            var nameLabel = UiKit.Label(card.transform, "NameLabel", "NAME", 12, UiKit.TextMuted, TextAnchor.MiddleLeft, bold: true);
            Place(nameLabel, 16f, 66f, 50f, 20f);

            var name = UiKit.InputField(card.transform, "Name", new Vector2(74f, 60f), new Vector2(240f, 34f),
                "Your name (blank = Steam name)", 14f);
            name.text = _nameField ?? "";
            name.characterLimit = Protocol.MaxNameLength;
            name.onValueChanged.AddListener(text =>
            {
                _nameField = text;
                ModConfig.PlayerName.Value = text.Trim();
            });
            bool nameLocked = s.InSession && !s.IsHost;
            name.interactable = !nameLocked;

            if (nameLocked)
            {
                var locked = UiKit.Label(card.transform, "Locked", "The host assigned your name", 12, UiKit.TextMuted);
                Place(locked, 326f, 66f, 240f, 20f);
            }

            var colourLabel = UiKit.Label(card.transform, "ColourLabel", "COLOUR", 12, UiKit.TextMuted, TextAnchor.MiddleLeft, bold: true);
            Place(colourLabel, 16f, 108f, 60f, 20f);

            Color current = ModConfig.GetColor();
            _colourSelections.Clear();
            for (int i = 0; i < ModConfig.Palette.Length; i++)
            {
                var c = ModConfig.Palette[i];
                bool selected = ColorsClose(c, current);
                var swatch = UiKit.PanelImage(card.transform, "Swatch" + i, c, 8, Vector2.up, Vector2.up,
                    new Vector2(82f + i * 32f, 104f), new Vector2(26f, 26f), raycast: true);
                var button = swatch.gameObject.AddComponent<Button>();
                button.targetGraphic = swatch;
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.fadeDuration = 0.07f;
                colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                button.colors = colors;
                var ring = UiKit.PanelImage(swatch.transform, "Sel", Color.white, 8, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(32f, 32f));
                ring.sprite = UiKit.Ring(8);
                ring.type = Image.Type.Sliced;
                ring.gameObject.SetActive(selected);
                _colourSelections.Add(ring.gameObject);
                var captured = c;
                button.onClick.AddListener(() =>
                {
                    ModConfig.PlayerColor.Value = "#" + ColorUtility.ToHtmlStringRGB(captured);
                    RefreshColourSelection();
                });
            }
        }

        private void RefreshColourSelection()
        {
            Color current = ModConfig.GetColor();
            for (int i = 0; i < _colourSelections.Count; i++)
            {
                var ring = _colourSelections[i];
                if (ring == null)
                    continue;
                bool selected = ColorsClose(ModConfig.Palette[i], current);
                if (ring.activeSelf != selected)
                    ring.SetActive(selected);
            }
        }

        private void BuildHostCard(Transform parent, float y)
        {
            var card = UiKit.PanelImage(parent, "Host", UiKit.Card, 14, Vector2.up, Vector2.up, new Vector2(0f, y),
                new Vector2(_playPage.sizeDelta.x, 194f), raycast: true);
            Header(card.transform, "HOST A GAME", "Create a room and invite friends");

            var portLabel = UiKit.Label(card.transform, "PortLabel", "PORT", 12, UiKit.TextMuted, TextAnchor.MiddleLeft, bold: true);
            Place(portLabel, 16f, 66f, 42f, 20f);
            var port = UiKit.InputField(card.transform, "Port", new Vector2(64f, 60f), new Vector2(86f, 34f), "7777", 14f);
            port.text = _hostPortField;
            port.characterLimit = 5;
            port.contentType = TMP_InputField.ContentType.IntegerNumber;
            port.onValueChanged.AddListener(text => _hostPortField = text);

            var passLabel = UiKit.Label(card.transform, "PassLabel", "PASSWORD", 12, UiKit.TextMuted, TextAnchor.MiddleLeft, bold: true);
            Place(passLabel, 172f, 66f, 82f, 20f);
            var pass = UiKit.InputField(card.transform, "Pass", new Vector2(260f, 60f), new Vector2(170f, 34f), "optional", 14f, password: true);
            pass.text = _passwordField ?? "";
            pass.characterLimit = 32;
            pass.onValueChanged.AddListener(text => _passwordField = text);

            var (hostButton, _) = UiKit.Button(card.transform, "Host", "HOST GAME", new Vector2(16f, 108f), new Vector2(150f, 40f),
                UiKit.ButtonVariant.Primary, 15f);
            hostButton.onClick.AddListener(DoHost);

            var lan = LanAddresses();
            if (lan.Length > 0)
            {
                var lanLabel = UiKit.Label(card.transform, "Lan", "YOUR LAN  ·  " + string.Join(",  ", lan), 12, UiKit.TextDim);
                Place(lanLabel, 180f, 118f, 330f, 20f);
                var (copy, _) = UiKit.Button(card.transform, "Copy", "COPY", new Vector2(520f, 112f), new Vector2(72f, 30f),
                    UiKit.ButtonVariant.Normal, 12f);
                copy.onClick.AddListener(() => GUIUtility.systemCopyBuffer = $"{lan[0]}:{_hostPortField}");
            }

            var note = UiKit.Label(card.transform, "Note", "Over the internet? Forward the UDP port on your router, or use a shared VPN.", 12, UiKit.TextMuted);
            Place(note, 16f, 160f, 592f, 20f);
        }

        private void BuildJoinCard(Transform parent, float y)
        {
            var card = UiKit.PanelImage(parent, "Join", UiKit.Card, 14, Vector2.up, Vector2.up, new Vector2(0f, y),
                new Vector2(_playPage.sizeDelta.x, 168f), raycast: true);
            Header(card.transform, "JOIN A GAME", "Enter the host's address and port");

            var addrLabel = UiKit.Label(card.transform, "AddrLabel", "ADDRESS", 12, UiKit.TextMuted, TextAnchor.MiddleLeft, bold: true);
            Place(addrLabel, 16f, 66f, 66f, 20f);
            var addr = UiKit.InputField(card.transform, "Addr", new Vector2(88f, 60f), new Vector2(220f, 34f), "192.168.1.10 or 1.2.3.4:7777", 14f);
            addr.text = _joinAddressField;
            addr.onValueChanged.AddListener(text => _joinAddressField = text);

            var portLabel = UiKit.Label(card.transform, "PortLabel", "PORT", 12, UiKit.TextMuted, TextAnchor.MiddleLeft, bold: true);
            Place(portLabel, 324f, 66f, 42f, 20f);
            var port = UiKit.InputField(card.transform, "Port", new Vector2(372f, 60f), new Vector2(86f, 34f), "7777", 14f);
            port.text = _joinPortField;
            port.characterLimit = 5;
            port.contentType = TMP_InputField.ContentType.IntegerNumber;
            port.onValueChanged.AddListener(text => _joinPortField = text);

            var (join, _) = UiKit.Button(card.transform, "Join", "JOIN GAME", new Vector2(16f, 108f), new Vector2(150f, 40f),
                UiKit.ButtonVariant.Primary, 15f);
            join.onClick.AddListener(DoJoin);

            var note = UiKit.Label(card.transform, "Note", "Uses the password above, if the host set one. You can also paste ip:port into the address.", 12, UiKit.TextMuted, TextAnchor.UpperLeft, wrap: true);
            Place(note, 180f, 108f, 430f, 44f);
        }

        private void BuildSessionCard(Transform parent, NetSession s)
        {
            var card = UiKit.PanelImage(parent, "Session", UiKit.Card, 14, Vector2.up, Vector2.up, new Vector2(0f, 0f),
                new Vector2(_playPage.sizeDelta.x, 146f), raycast: true);
            Header(card.transform, s.IsHost ? "YOUR ROOM IS LIVE" : "CONNECTED TO ROOM",
                s.IsHost ? "Share your address to invite friends" : s.Endpoint);

            if (s.IsHost)
            {
                var lan = LanAddresses();
                if (lan.Length > 0)
                {
                    var lanLabel = UiKit.Label(card.transform, "Lan", "LAN  ·  " + string.Join(",  ", lan.Select(a => $"{a}:{ModConfig.HostPort.Value}")), 13, UiKit.Text);
                    Place(lanLabel, 16f, 54f, 460f, 22f);
                    var (copy, _) = UiKit.Button(card.transform, "Copy", "COPY", new Vector2(540f, 48f), new Vector2(56f, 30f),
                        UiKit.ButtonVariant.Normal, 12f);
                    copy.onClick.AddListener(() => GUIUtility.systemCopyBuffer = $"{lan[0]}:{ModConfig.HostPort.Value}");
                }
            }
            else
            {
                _sessionPing = UiKit.Label(card.transform, "Ping", $"PING  ·  {s.PingMs} MS", 13, UiKit.Text);
                Place(_sessionPing, 16f, 54f, 300f, 22f);
            }

            var hint = UiKit.Label(card.transform, "Hint", "ROOM lists the golfers  ·  SCORES shows the Front Nine card", 12, UiKit.TextMuted);
            Place(hint, 16f, 96f, 500f, 20f);
        }

        private void RebuildRoomPage(NetSession s)
        {
            UiKit.DestroyChildren(_roomPage);
            _rowStatus.Clear();
            _rowPing.Clear();

            // Turn panel
            var turn = UiKit.PanelImage(_roomPage, "Turn", UiKit.Card, 14, Vector2.up, Vector2.up, Vector2.zero,
                new Vector2(_roomPage.sizeDelta.x, 112f), raycast: true);
            var eyebrow = UiKit.Label(turn.transform, "Eyebrow", "UP NEXT TO SHOOT", 12, UiKit.Accent, TextAnchor.MiddleLeft, bold: true);
            eyebrow.characterSpacing = 6f;
            Place(eyebrow, 18f, 12f, 300f, 18f);
            _roomTurnName = UiKit.Label(turn.transform, "Name", "", 24, UiKit.Text, TextAnchor.MiddleLeft, bold: true);
            Place(_roomTurnName, 18f, 34f, 420f, 34f);
            _roomTurnSub = UiKit.Label(turn.transform, "Sub", "Everyone tees off once, then the farthest ball from the pin plays.", 13, UiKit.TextDim);
            Place(_roomTurnSub, 18f, 78f, 560f, 20f);
            RefreshRoomTurn(s);

            // Sized so a full (8 golfer) session still fits inside the window above the footer.
            float y = 122f;
            float rowH = 42f;
            foreach (var info in s.Players.Values.OrderBy(p => p.Id))
            {
                BuildPlayerRow(s, info, y, rowH);
                y += rowH + 4f;
            }

            if (s.Players.Count > 1 && LocalPlayer.InWorld && !LocalPlayer.CanTeleport)
            {
                var note = UiKit.Label(_roomPage, "GoToNote", "\"Go to\" works while you're walking (not golfing or in a cutscene).", 12, UiKit.TextMuted);
                Place(note, 4f, y + 4f, 560f, 20f);
            }
        }

        /// <summary>Keeps the per-golfer status and ping current without rebuilding the rows.</summary>
        private void RefreshRoomRows(NetSession s)
        {
            foreach (var info in s.Players.Values)
            {
                if (_rowPing.TryGetValue(info.Id, out var ping) && ping != null)
                {
                    string text = info.Id == s.LocalId ? "" : $"{s.GetPlayerPing(info.Id)} ms";
                    if (ping.text != text)
                        ping.text = text;
                }
                if (!_rowStatus.TryGetValue(info.Id, out var status) || status == null)
                    continue;
                string line = "";
                if (info.Id != s.LocalId && RemoteWorld.Instance != null &&
                    RemoteWorld.Instance.Players.TryGetValue(info.Id, out var rp))
                {
                    line = RemoteWorld.Describe(rp);
                }
                if (s.TryGetTurnDistance(info.Id, out float flagDistance))
                    line = $"Ball: {flagDistance:0.0} m to flag";
                if (status.text != line)
                    status.text = line;
            }
        }

        private void RefreshRoomTurn(NetSession s)
        {
            if (_roomTurnName == null)
                return;
            if (s.Players.Count > 1 && s.Players.TryGetValue(s.ActiveTurnId, out var active))
            {
                _roomTurnName.text = active.Id == s.LocalId ? "Your turn" : Truncate(active.Name, 20) + "'s turn";
                _roomTurnName.color = Color.Lerp((Color)active.Color, Color.white, 0.35f);
                _roomTurnSub.text = active.Id == s.LocalId
                    ? "You're up — play when ready."
                    : "Everyone tees off once, then the farthest ball from the pin plays.";
                if (s.TryGetTurnDistance(active.Id, out float flagDistance))
                    _roomTurnSub.text = $"Ball: {flagDistance:0.0} m to the flag. Tee off once, then farthest plays.";
            }
            else if (s.Players.Count > 1)
            {
                _roomTurnName.text = "Waiting for the next shot";
                _roomTurnName.color = UiKit.TextDim;
                _roomTurnSub.text = "Let balls settle. Everyone tees off once, then the farthest ball plays.";
            }
            else
            {
                _roomTurnName.text = "Waiting for golfers";
                _roomTurnName.color = UiKit.TextDim;
                _roomTurnSub.text = "Share your address from the PLAY tab to invite friends.";
            }
        }

        private void BuildPlayerRow(NetSession s, PlayerInfo info, float y, float rowH)
        {
            float w = _roomPage.sizeDelta.x;
            var row = UiKit.PanelImage(_roomPage, "Row" + info.Id, UiKit.Row, 12, Vector2.up, Vector2.up, new Vector2(0f, y),
                new Vector2(w, rowH), raycast: true);

            var dot = UiKit.PanelImage(row.transform, "Dot", info.Color, 5, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(10f, 10f));

            string role = info.Id == Protocol.HostId ? " <color=" + UiKit.Html(UiKit.TextMuted) + ">· host</color>" : "";
            string you = info.Id == s.LocalId ? " <color=" + UiKit.Html(UiKit.TextDim) + ">· you</color>" : "";
            var name = UiKit.Label(row.transform, "Name", info.Name + role + you, 15, Color.Lerp((Color)info.Color, Color.white, 0.3f), TextAnchor.MiddleLeft);
            Place(name, 34f, 0f, 240f, rowH, middle: true);

            var statusLabel = UiKit.Label(row.transform, "Status", "", 12, UiKit.TextDim, TextAnchor.MiddleRight);
            Place(statusLabel, 282f, 0f, 174f, rowH, middle: true);
            _rowStatus[info.Id] = statusLabel;

            var pingLabel = UiKit.Label(row.transform, "Ping", "", 12, UiKit.TextMuted, TextAnchor.MiddleRight);
            Place(pingLabel, 462f, 0f, 44f, rowH, middle: true);
            _rowPing[info.Id] = pingLabel;

            if (info.Id != s.LocalId)
            {
                var (goTo, _) = UiKit.Button(row.transform, "GoTo", "GO TO", new Vector2(516f, (rowH - 32f) * 0.5f),
                    new Vector2(78f, 32f), UiKit.ButtonVariant.Normal, 12f);
                bool hasPose = RemoteWorld.Instance != null && RemoteWorld.Instance.Players.TryGetValue(info.Id, out var pose) && pose.HasPose;
                goTo.interactable = hasPose && LocalPlayer.CanTeleport;
                byte target = info.Id;
                goTo.onClick.AddListener(() =>
                {
                    if (RemoteWorld.Instance != null && RemoteWorld.Instance.Players.TryGetValue(target, out var p) && p.HasPose)
                    {
                        LocalPlayer.TeleportNear(p.Position);
                        SetMenuOpen(false);
                    }
                });
            }
        }

        // ------------------------------------------------------------------ input capture

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (!_chatOpen && kb[ModConfig.MenuKey.Value].wasPressedThisFrame)
                    SetMenuOpen(!_menuOpen);
                else if (!_chatOpen && kb[ModConfig.ScoreboardKey.Value].wasPressedThisFrame)
                    SetScoreboardOpen(!_scoreboardOpen);
                else if (!_menuOpen && !_chatOpen && kb[ModConfig.ChatKey.Value].wasPressedThisFrame)
                    TryOpenChat();

                if (_chatOpen && kb.escapeKey.wasPressedThisFrame)
                    CloseChat();
            }
            UpdateInputCapture();
            LayoutHud();
            RefreshHud();
            RefreshToasts();
            RefreshChat();
            RefreshScoreOverlay();
            RefreshMenu();
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

        private static readonly List<UnityEngine.InputSystem.InputAction> _disabledActions = new List<UnityEngine.InputSystem.InputAction>();

        /// <summary>
        /// Pauses gameplay input while our UI owns the mouse. Deactivating the whole PlayerInput would also
        /// disable the pointer actions the game's own EventSystem module listens through — killing every
        /// click in our menu — and the game keeps gameplay and UI actions in one map, so individual actions
        /// are disabled instead: everything except the module's pointer/UI actions.
        /// </summary>
        private static void SetGameInput(bool active)
        {
            var im = InputManager.instance;
            if (im == null || im.input == null || im.input.actions == null)
                return;
            if (!active)
            {
                var module = EventSystem.current != null ? EventSystem.current.GetComponent<BaseInputModule>() as InputSystemUIInputModule : null;
                _disabledActions.Clear();
                foreach (var action in im.input.actions)
                {
                    if (IsUiAction(action, module))
                        continue;
                    if (action.enabled)
                    {
                        action.Disable();
                        _disabledActions.Add(action);
                    }
                }
            }
            else
            {
                foreach (var action in _disabledActions)
                    action.Enable();
                _disabledActions.Clear();
            }
        }

        private static bool IsUiAction(UnityEngine.InputSystem.InputAction action, InputSystemUIInputModule module)
        {
            if (module == null)
                return false;
            if (ReferenceEquals(action, module.point) || ReferenceEquals(action, module.leftClick) ||
                ReferenceEquals(action, module.rightClick) || ReferenceEquals(action, module.middleClick) ||
                ReferenceEquals(action, module.scrollWheel) ||
                ReferenceEquals(action, module.submit) || ReferenceEquals(action, module.cancel))
                return true;
            // Different asset instances can hold structurally identical actions; match by path then.
            return MatchesUiAction(action, module.point, module.leftClick, module.rightClick, module.middleClick,
                module.scrollWheel, module.submit, module.cancel);
        }

        private static bool MatchesUiAction(UnityEngine.InputSystem.InputAction action, params UnityEngine.InputSystem.InputAction[] uiActions)
        {
            foreach (var ui in uiActions)
                if (ui != null && ui.name == action.name)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ actions

        private void OnShotBlocked()
        {
            var s = NetSession.Instance;
            if (s == null || !s.InSession || s.Players.Count < 2)
                return;
            if (Time.unscaledTime - _lastOutOfTurnNudge < 2f)
                return;
            _lastOutOfTurnNudge = Time.unscaledTime;
            Toast(s.Players.TryGetValue(s.ActiveTurnId, out var active) && active.Id != s.LocalId
                ? $"Shot blocked — {active.Name} is up"
                : "Shot blocked — wait for the next golfer", UiKit.Warning);
        }

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

        private void Header(Transform card, string heading, string subtitle)
        {
            var h = UiKit.Label(card, "Heading", heading, 13, UiKit.Accent, TextAnchor.MiddleLeft, bold: true);
            h.characterSpacing = 4f;
            Place(h, 16f, 12f, 400f, 20f);
            var sub = UiKit.Label(card, "Subheading", subtitle, 12, UiKit.TextMuted);
            Place(sub, 16f, 34f, 500f, 20f);
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
