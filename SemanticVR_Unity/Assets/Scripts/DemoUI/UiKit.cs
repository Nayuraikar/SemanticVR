using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SemanticVR.DemoUI
{
    /// <summary>
    /// Small code-only UGUI toolkit for the demo UI: dark translucent panels, chips, switches, sliders, a scroll view.
    /// Procedural sprites and the built-in font, no assets. Every text that can be squeezed uses best-fit so a narrow
    /// layout shrinks the text rather than letting it spill outside its control.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Panel = new Color(0.055f, 0.065f, 0.085f, 0.88f);
        public static readonly Color PanelEdge = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Card = new Color(1f, 1f, 1f, 0.05f);
        public static readonly Color Text = new Color(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Color Muted = new Color(0.64f, 0.69f, 0.76f, 1f);
        public static readonly Color Accent = new Color(0.30f, 0.60f, 1f, 1f);
        public static readonly Color ChipOff = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.16f);

        static Sprite rounded, pill, circle; static Font font;

        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        static Sprite MakeRounded(int size, float radius, bool full)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply();
            float b = full ? 0 : radius;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        public static Sprite Rounded => rounded != null ? rounded : (rounded = MakeRounded(48, 12f, false));
        public static Sprite Pill => pill != null ? pill : (pill = MakeRounded(64, 32f, false));
        public static Sprite Circle => circle != null ? circle : (circle = MakeRounded(32, 16f, true));

        // ------------------------------------------------------------------ primitives
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float pad = 0f) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad); }

        public static LayoutElement Size(Component c, float minW = -1, float minH = -1, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (minW >= 0) le.minWidth = minW; if (minH >= 0) le.minHeight = minH; if (prefW >= 0) le.preferredWidth = prefW; if (prefH >= 0) le.preferredHeight = prefH;
            if (flexW >= 0) le.flexibleWidth = flexW; if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static Image Box(string name, Transform parent, Color c, bool rounded = true, bool raycast = false)
        {
            var rt = Rect(name, parent); var img = rt.gameObject.AddComponent<Image>();
            img.color = c; img.raycastTarget = raycast;
            if (rounded) { img.sprite = Rounded; img.type = Image.Type.Sliced; }
            return img;
        }

        public static Image PillBox(string name, Transform parent, Color c, bool raycast = false)
        {
            var img = Box(name, parent, c, false, raycast); img.sprite = Pill; img.type = Image.Type.Sliced; return img;
        }

        /// <summary>Text. Default overflow is Overflow (single line); bestFit shrinks to the rect instead; wrap breaks lines.</summary>
        public static Text Label(Transform parent, string text, int size = 14, Color? color = null, FontStyle style = FontStyle.Normal,
                                 TextAnchor anchor = TextAnchor.MiddleLeft, float height = 0, bool bestFit = false, bool wrap = false)
        {
            var rt = Rect("Label", parent); var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = size; t.color = color ?? Text; t.fontStyle = style; t.alignment = anchor; t.raycastTarget = false;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            if (bestFit) { t.resizeTextForBestFit = true; t.resizeTextMinSize = Mathf.Max(8, size - 5); t.resizeTextMaxSize = size; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; }
            if (height > 0) Size(t, minH: height, prefH: height);
            return t;
        }

        public static VerticalLayoutGroup VStack(Transform parent, string name, float spacing, RectOffset pad = null)
        {
            var rt = Rect(name, parent); var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = pad ?? new RectOffset(0, 0, 0, 0);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HRow(Transform parent, float height, float spacing = 6f, string name = "Row")
        {
            var rt = Rect(name, parent); var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            Size(h, minH: height, prefH: height, flexH: 0);   // flexH 0: a row never steals spare height from a scroll view
            return h;
        }

        // ------------------------------------------------------------------ chips (segmented / pill buttons)
        public sealed class Chip
        {
            public Button button; public Image bg; public Text label; public bool selected;
            public void Set(bool on)
            {
                selected = on; bg.color = on ? Accent : ChipOff; label.color = on ? Color.white : Text;
                label.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        static void StyleButton(Button btn)
        {
            var cs = btn.colors; cs.normalColor = Color.white; cs.highlightedColor = new Color(1f, 1f, 1f, 0.80f); cs.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f); cs.selectedColor = Color.white;
            cs.disabledColor = new Color(1f, 1f, 1f, 0.35f); cs.colorMultiplier = 1f; cs.fadeDuration = 0.08f; btn.colors = cs;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        public static Chip MakeChip(Transform parent, string text, UnityAction onClick, int size = 14, float height = 34)
        {
            var bg = Box("Chip_" + text, parent, ChipOff, true, true);
            var btn = bg.gameObject.AddComponent<Button>(); btn.targetGraphic = bg; StyleButton(btn);
            var t = Label(bg.transform, text, size, Text, FontStyle.Normal, TextAnchor.MiddleCenter, 0, true);
            Stretch(t.rectTransform, 4f);
            Size(bg, minW: 0, minH: height, prefH: height, flexW: 1);
            btn.onClick.AddListener(onClick);
            var chip = new Chip { button = btn, bg = bg, label = t }; chip.Set(false);
            return chip;
        }

        /// <summary>Equal-width chips in rows of <paramref name="columns"/>. Every row is filled, so the chips of the last row grow to share the width.</summary>
        public static Chip[] ChipGrid(Transform parent, string[] names, int columns, System.Action<int> onPick, int size = 14, float height = 34)
        {
            var chips = new Chip[names.Length]; HorizontalLayoutGroup row = null;
            for (int i = 0; i < names.Length; i++)
            {
                if (i % columns == 0) row = HRow(parent, height, 6f);
                int k = i; chips[i] = MakeChip(row.transform, names[i], () => onPick(k), size, height);
            }
            return chips;
        }

        // ------------------------------------------------------------------ switch row
        public sealed class Switch
        {
            public Button button; public Image track, knob; public Text label; public bool on;
            public void Set(bool v)
            {
                on = v; track.color = v ? Accent : new Color(1f, 1f, 1f, 0.22f);
                var kr = knob.rectTransform; float x = v ? 1f : 0f; kr.anchorMin = kr.anchorMax = new Vector2(x, 0.5f); kr.pivot = new Vector2(x, 0.5f); kr.anchoredPosition = new Vector2(v ? -3f : 3f, 0f);
            }
        }

        public static Switch MakeSwitchRow(Transform parent, string text, UnityAction onClick, float height = 40)
        {
            var bg = Box("Switch_" + text, parent, Card, true, true);
            var btn = bg.gameObject.AddComponent<Button>(); btn.targetGraphic = bg; StyleButton(btn);
            Size(bg, minH: height, prefH: height, flexW: 1);
            var l = Label(bg.transform, text, 14, Text, FontStyle.Normal, TextAnchor.MiddleLeft, 0, true);
            var lr = l.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(12, 2); lr.offsetMax = new Vector2(-62, -2);
            var track = PillBox("Track", bg.transform, Track); var tr = track.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(1, 0.5f); tr.pivot = new Vector2(1, 0.5f); tr.sizeDelta = new Vector2(40, 22); tr.anchoredPosition = new Vector2(-12, 0);
            var knob = Box("Knob", track.transform, Color.white, false); knob.sprite = Circle; knob.rectTransform.sizeDelta = new Vector2(16, 16);
            btn.onClick.AddListener(onClick);
            var sw = new Switch { button = btn, track = track, knob = knob, label = l }; sw.Set(false);
            return sw;
        }

        // ------------------------------------------------------------------ slider (whole row is the hit area)
        public static Slider MakeSlider(Transform parent, float min, float max, float value, UnityAction<float> onChange)
        {
            var root = Rect("Slider", parent);
            Size(root, minH: 30, prefH: 30, flexW: 1);
            var hit = root.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;   // clicking anywhere on the row grabs the slider
            var track = PillBox("Track", root, Track); var tr = track.rectTransform;
            tr.anchorMin = new Vector2(0, 0.5f); tr.anchorMax = new Vector2(1, 0.5f); tr.sizeDelta = new Vector2(-12, 6); tr.anchoredPosition = Vector2.zero;
            var fillArea = Rect("Fill Area", root); fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f); fillArea.sizeDelta = new Vector2(-12, 6); fillArea.anchoredPosition = Vector2.zero;
            var fill = PillBox("Fill", fillArea, Accent); fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1); fill.rectTransform.sizeDelta = new Vector2(6, 0);
            // zero-height slide area centred on the row: Slider stretches the handle across the area's height, so the handle's own sizeDelta.y (20) becomes its height
            var handleArea = Rect("Handle Slide Area", root); handleArea.anchorMin = new Vector2(0, 0.5f); handleArea.anchorMax = new Vector2(1, 0.5f); handleArea.sizeDelta = new Vector2(-18, 0); handleArea.anchoredPosition = Vector2.zero;
            var handle = Box("Handle", handleArea, Color.white, false, true); handle.sprite = Circle;
            handle.rectTransform.sizeDelta = new Vector2(20, 20);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle; s.direction = Slider.Direction.LeftToRight; s.transition = Selectable.Transition.None;
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            s.minValue = min; s.maxValue = max; s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(onChange);
            return s;
        }

        // ------------------------------------------------------------------ scroll view
        /// <summary>Vertical scroll view: masked viewport, auto-sized content column, slim scrollbar. Returns the ScrollRect; <paramref name="content"/> is where to add children.</summary>
        public static ScrollRect MakeScroll(Transform parent, out RectTransform content, RectOffset contentPad)
        {
            var root = Rect("Scroll", parent); Size(root, minH: 60, flexW: 1, flexH: 1);
            var sr = root.gameObject.AddComponent<ScrollRect>();
            var catcher = root.gameObject.AddComponent<Image>(); catcher.color = new Color(0, 0, 0, 0); catcher.raycastTarget = true;   // lets the wheel / drag work on empty areas
            var vp = Rect("Viewport", root); Stretch(vp); vp.offsetMax = new Vector2(-8, 0); vp.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", vp); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>(); v.padding = contentPad; v.spacing = 0;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var barRt = Rect("Scrollbar", root); barRt.anchorMin = new Vector2(1, 0); barRt.anchorMax = new Vector2(1, 1); barRt.pivot = new Vector2(1, 0.5f); barRt.sizeDelta = new Vector2(5, -8); barRt.anchoredPosition = new Vector2(-2, 0);
            var barBg = barRt.gameObject.AddComponent<Image>(); barBg.sprite = Pill; barBg.type = Image.Type.Sliced; barBg.color = new Color(1, 1, 1, 0.06f);
            var slide = Rect("Sliding Area", barRt); Stretch(slide);
            var handle = PillBox("Handle", slide, new Color(1, 1, 1, 0.30f), true); Stretch(handle.rectTransform);
            var sb = barRt.gameObject.AddComponent<Scrollbar>(); sb.handleRect = handle.rectTransform; sb.targetGraphic = handle; sb.direction = Scrollbar.Direction.BottomToTop;
            sb.navigation = new Navigation { mode = Navigation.Mode.None };

            sr.viewport = vp; sr.content = content; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 28f;
            sr.verticalScrollbar = sb; sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide; sr.verticalScrollbarSpacing = 0;
            return sr;
        }
    }
}
