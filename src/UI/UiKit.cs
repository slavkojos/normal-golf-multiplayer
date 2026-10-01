using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NormalGolfMultiplayer.UI
{
    /// <summary>
    /// Shared building blocks for the mod's uGUI: anti-aliased 9-sliced sprites tinted at draw time,
    /// the game's own font, and small factories so every panel and button stays consistent.
    /// A white sprite tinted by Image.color keeps the texture count at a handful of shapes.
    /// </summary>
    internal static class UiKit
    {
        public static readonly Color Accent = new Color(0.46f, 0.91f, 0.72f);
        public static readonly Color AccentDim = new Color(0.46f, 0.91f, 0.72f, 0.14f);
        public static readonly Color Error = new Color(1f, 0.49f, 0.46f);
        public static readonly Color Warning = new Color(1f, 0.78f, 0.35f);

        public static readonly Color Text = new Color(0.95f, 0.98f, 0.97f);
        public static readonly Color TextDim = new Color(0.68f, 0.78f, 0.75f);
        public static readonly Color TextMuted = new Color(0.47f, 0.57f, 0.55f);

        public static readonly Color Panel = new Color(0.035f, 0.06f, 0.07f, 0.94f);
        public static readonly Color Card = new Color(0.105f, 0.145f, 0.155f, 0.97f);
        public static readonly Color Row = new Color(0.155f, 0.205f, 0.215f, 0.97f);
        public static readonly Color Field = new Color(0.07f, 0.105f, 0.115f, 1f);
        public static readonly Color FieldFocus = new Color(0.12f, 0.17f, 0.18f, 1f);

        public static TMP_FontAsset Font { get; private set; }
        public static TMP_FontAsset FontBold { get; private set; }

        private static readonly Dictionary<int, Sprite> Fills = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> Rings = new Dictionary<int, Sprite>();

        /// <summary>Loads the game's own UI font once it has been streamed in by the game's panels.</summary>
        public static void Prepare()
        {
            if (Font == null)
            {
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                Font = fonts.FirstOrDefault(f => f.name == "Quantico-Regular SDF")
                       ?? TMP_Settings.defaultFontAsset
                       ?? fonts.FirstOrDefault();
            }
            if (FontBold == null && Font != null)
                FontBold = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.StartsWith("Quantico-Bold")) ?? Font;
        }

        /// <summary>A filled rounded square (or circle for large radii) that can be tinted per Image.</summary>
        public static Sprite Round(int radius)
        {
            if (Fills.TryGetValue(radius, out var s) && s != null)
                return s;
            s = Build(radius, false);
            Fills[radius] = s;
            return s;
        }

        /// <summary>The outline-only counterpart of <see cref="Round"/>, for highlights and selection marks.</summary>
        public static Sprite Ring(int radius)
        {
            if (Rings.TryGetValue(radius, out var s) && s != null)
                return s;
            s = Build(radius, true);
            Rings[radius] = s;
            return s;
        }

        private static Sprite Build(int radius, bool ring)
        {
            // Corner radius in texture pixels becomes the corner size in canvas units after slicing.
            int border = radius + 2;
            int size = border * 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = ring
                        ? Mathf.Clamp01(1.5f - Mathf.Abs(d - radius))
                        : Mathf.Clamp01(radius + 0.5f - d);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            var border4 = new Vector4(border, border, border, border);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0u,
                SpriteMeshType.FullRect, border4);
        }

        // ------------------------------------------------------------------ factories

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            // Top-anchored elements measure y downward, matching how the layouts are written.
            if (pivot.y > 0.5f && anchor.y > 0.5f)
                pos.y = -pos.y;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>A rounded, tintable panel image.</summary>
        public static Image PanelImage(Transform parent, string name, Color color, int radius,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, bool raycast = false)
        {
            var rt = Rect(parent, name, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Round(radius);
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color,
            TextAnchor align = TextAnchor.MiddleLeft, bool bold = false, bool wrap = false)
        {
            var rt = Rect(parent, name, Vector2.up, Vector2.up, Vector2.zero, new Vector2(300f, Mathf.Max(size + 8f, MinLineHeight(size))));
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyFont(tmp, bold);
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align switch
            {
                TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
                TextAnchor.UpperCenter => TextAlignmentOptions.Top,
                TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
                TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
                TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
                TextAnchor.MiddleRight => TextAlignmentOptions.Right,
                TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
                TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
                TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
                _ => TextAlignmentOptions.Left,
            };
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            // Never let text spill past its container; a single-line label ends with an ellipsis instead.
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static void ApplyFont(TMP_Text tmp, bool bold)
        {
            Prepare();
            if (Font != null)
                tmp.font = bold && FontBold != null ? FontBold : Font;
            if (bold && FontBold == null)
                tmp.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// Rect height one line of the given font size needs. TMP's Ellipsis overflow drops a whole line that
        /// does not fit vertically, so a label laid out shorter than this renders nothing at all — the game's
        /// Quantico face needs about 1.45em per line.
        /// </summary>
        public static float MinLineHeight(float fontSize)
        {
            if (Font == null)
                Prepare();
            var face = Font != null ? Font.faceInfo : default;
            return face.pointSize > 0f ? face.lineHeight * (fontSize / face.pointSize) : fontSize * 1.6f;
        }

        public static float MinLineHeight(TMP_Text tmp)
        {
            if (tmp == null)
                return 20f;
            var face = tmp.font != null ? tmp.font.faceInfo : default;
            return face.pointSize > 0f ? face.lineHeight * (tmp.fontSize / face.pointSize) : tmp.fontSize * 1.6f;
        }

        public enum ButtonVariant
        {
            Normal,
            Primary,
            Danger,
            Ghost,
        }

        public static (Button button, TextMeshProUGUI label) Button(Transform parent, string name, string text,
            Vector2 pos, Vector2 size, ButtonVariant variant = ButtonVariant.Normal, float fontSize = 15f,
            Vector2? anchor = null, Vector2? pivot = null)
        {
            Vector2 a = anchor ?? Vector2.up;
            Vector2 p = pivot ?? Vector2.up;
            Color baseColor = variant switch
            {
                ButtonVariant.Primary => Accent,
                ButtonVariant.Danger => new Color(0.42f, 0.16f, 0.15f, 1f),
                ButtonVariant.Ghost => new Color(1f, 1f, 1f, 0.05f),
                _ => new Color(0.21f, 0.27f, 0.28f, 1f),
            };
            Color textColor = variant == ButtonVariant.Primary ? new Color(0.03f, 0.14f, 0.1f, 1f)
                : variant == ButtonVariant.Ghost ? TextDim
                : Text;

            var img = PanelImage(parent, name, baseColor, 10, a, p, pos, size, raycast: true);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.07f;
            button.colors = colors;

            var label = Label(img.transform, "Label", text, fontSize, textColor, TextAnchor.MiddleCenter, variant != ButtonVariant.Normal);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return (button, label);
        }

        /// <summary>A one-line text field with placeholder; returns the TMP_InputField for wiring events.</summary>
        public static TMP_InputField InputField(Transform parent, string name, Vector2 pos, Vector2 size,
            string placeholder, float fontSize = 15f, bool password = false)
        {
            var img = PanelImage(parent, name, Field, 8, Vector2.up, Vector2.up, pos, size, raycast: true);
            var field = img.gameObject.AddComponent<TMP_InputField>();

            var area = Stretch(img.transform, "Area");
            area.gameObject.AddComponent<RectMask2D>();

            var phRect = Rect(area, "Placeholder", Vector2.up, Vector2.up, Vector2.zero, size);
            var ph = phRect.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyFont(ph, false);
            ph.text = placeholder;
            ph.fontSize = fontSize;
            ph.fontStyle = FontStyles.Italic;
            ph.color = TextMuted;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.textWrappingMode = TextWrappingModes.NoWrap;
            ph.raycastTarget = false;
            ph.margin = new Vector4(10f, 0f, 10f, 0f);

            var textRect = Rect(area, "Text", Vector2.up, Vector2.up, Vector2.zero, size);
            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyFont(text, false);
            text.fontSize = fontSize;
            text.color = Text;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.margin = new Vector4(10f, 0f, 10f, 0f);

            field.textComponent = text;
            field.placeholder = ph;
            field.textViewport = area;
            field.targetGraphic = img;
            field.richText = false;
            if (password)
                field.contentType = TMP_InputField.ContentType.Password;
            field.colors = buttonColorsFor(field);
            field.selectionColor = new Color(0.46f, 0.91f, 0.72f, 0.35f);
            return field;
        }

        private static ColorBlock buttonColorsFor(Selectable s)
        {
            var colors = s.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.selectedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
            colors.pressedColor = new Color(0.95f, 0.95f, 0.95f, 1f);
            colors.fadeDuration = 0.07f;
            s.colors = colors;
            return colors;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.Destroy(parent.GetChild(i).gameObject);
        }

        /// <summary>Colour names for chat-style rich text, safe to embed between tags.</summary>
        public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
