using SemanticVR.Accessibility;
using SemanticVR.Assist;
using SemanticVR.ColourProcessing;
using SemanticVR.Perception;
using SemanticVR.Player;
using SemanticVR.Semantics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SemanticVR.DemoUI
{
    /// <summary>
    /// Application UI for the walkable demo. While walking: a minimal status pill, a crosshair and a small floating
    /// "Settings" button. Tab / Esc frees the cursor and opens the settings panel (or click the button once the cursor is
    /// free). The panel has four sections (Colour Vision, Correction, Semantic Priority, Display and Comparison), a header
    /// with a close button, a scrolling body and a fixed footer. Everything is applied live (runtime copies of the CVD
    /// profile and correction settings, the assist view and the priority layer), no restart. The layout is computed from the
    /// canvas size every time it changes, so the panel always fits the Game view and scrolls when its content is taller.
    /// It talks to the colour pipeline only through public properties. Debug output lives in <see cref="DemoDebugOverlay"/>.
    /// </summary>
    public sealed class DemoUiController : MonoBehaviour
    {
        public ColourProcessor processor;
        public ColourAssistView assistView;
        public SemanticAssistLayer assistLayer;
        public PerceptionPipeline perception;
        public DesktopPlayerInput input;
        public DemoDebugOverlay debug;

        // Layout constants, in canvas units (the CanvasScaler maps them to pixels).
        const float RefW = 1024f, RefH = 576f;
        const float Margin = 12f, GearW = 124f, GearH = 34f, PanelMaxW = 344f, HeaderH = 58f, TabsH = 44f, FooterH = 54f, PanelMinH = 220f;

        Canvas canvas; CanvasScaler scaler; float[] pageH = new float[4]; bool measureAll = true; RectTransform canvasRt, panel, hud, hint, gear, content; ScrollRect scroll;
        Text hudTitle, hudLine, hintText, headerSummary, gearLabel; Image dot, gearBg, hintBg; Image[] gearBars = new Image[3];
        UiKit.Chip[] tabChips = new UiKit.Chip[4], viewChips = new UiKit.Chip[3], familyChips = new UiKit.Chip[5], typeChips = new UiKit.Chip[2], modeChips = new UiKit.Chip[4], ctxChips = new UiKit.Chip[3];
        UiKit.Switch compareSwitch, prioSwitch;
        Slider severity, strength, focus; Text severityValue, strengthValue, focusValue, severityNote, modeNote, ctxNote;
        CanvasGroup severityGroup, focusGroup;
        GameObject[] pages = new GameObject[4]; GameObject[] dividers = new GameObject[2]; Text[] paneLabels = new Text[3]; GameObject compareOverlay, crosshair;
        float hintTimer = 18f, refreshTimer; bool built, panelOpen, layoutDirty = true; int tab; Vector2 lastCanvasSize;

        static readonly string[] TabNames = { "Vision", "Correction", "Priority", "Display" };
        static readonly string[] PageTitles = { "Colour Vision", "Correction", "Semantic Priority", "Display and Comparison" };
        static readonly string[] FamilyNames = { "Normal", "Protan", "Deutan", "Tritan", "Achroma" };
        static readonly string[] ModeNames = { "Balanced", "Max distinction", "Natural", "High contrast" };
        static readonly string[] ModeNotes =
        {
            "Balances colour separation against a natural look.",
            "Pushes colours apart as far as possible.",
            "Keeps colours close to the original image.",
            "Raises contrast to help separate similar colours.",
        };
        static readonly string[] ContextNames = { "Road safety", "Navigation", "General" };
        static readonly string[] ContextNotes =
        {
            "Favours traffic lights, signs, vehicles and pedestrians.",
            "Favours road markings, signs, roads and sidewalks.",
            "A balanced mix across all object types.",
        };
        static readonly CvdType[] Complete = { CvdType.Normal, CvdType.Protanopia, CvdType.Deuteranopia, CvdType.Tritanopia, CvdType.Achromatopsia };
        static readonly CvdType[] Partial = { CvdType.Normal, CvdType.Protanomaly, CvdType.Deuteranomaly, CvdType.Tritanomaly, CvdType.Achromatopsia };

        public bool SettingsOpen => panelOpen;

        void Awake()
        {
            if (processor == null) processor = GetComponent<ColourProcessor>();
            if (assistView == null) assistView = GetComponent<ColourAssistView>();
            if (assistLayer == null) assistLayer = GetComponent<SemanticAssistLayer>();
            if (perception == null) perception = GetComponent<PerceptionPipeline>();
            if (debug == null) debug = GetComponent<DemoDebugOverlay>();
            if (input == null) input = FindFirstObjectByType<DesktopPlayerInput>();
            EnsureEventSystem();
            Build();
        }

        void OnEnable() { if (input != null) input.MenuChanged += OnMenu; }
        void OnDisable() { if (input != null) input.MenuChanged -= OnMenu; }
        void Start() { OnMenu(input != null && input.MenuOpen); Refresh(); }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // ============================================================== build
        void Build()
        {
            var go = new GameObject("DemoUiCanvas"); go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var cs = scaler = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(RefW, RefH);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; cs.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            canvasRt = (RectTransform)go.transform;

            BuildCompareOverlay(go.transform);
            BuildHud(go.transform);
            BuildHint(go.transform);
            BuildGear(go.transform);
            BuildPanel(go.transform);
            var cross = UiKit.Box("Crosshair", go.transform, new Color(1, 1, 1, 0.55f), false); cross.sprite = UiKit.Circle;
            var cr = cross.rectTransform; cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0.5f); cr.sizeDelta = new Vector2(5, 5);
            crosshair = cross.gameObject;
            built = true;
            ApplyLayout();
        }

        void BuildCompareOverlay(Transform parent)
        {
            var root = UiKit.Rect("CompareOverlay", parent); UiKit.Stretch(root); compareOverlay = root.gameObject;
            for (int i = 0; i < 2; i++)
            {
                var line = UiKit.Box("Divider" + i, root, new Color(1, 1, 1, 0.85f), false); var r = line.rectTransform;
                r.anchorMin = new Vector2((i + 1) / 3f, 0); r.anchorMax = new Vector2((i + 1) / 3f, 1); r.sizeDelta = new Vector2(2, 0); dividers[i] = line.gameObject;
            }
            string[] names = { "Original", "CVD simulated", "Corrected" };
            for (int i = 0; i < 3; i++)
            {
                var pill = UiKit.PillBox("Tag" + i, root, new Color(0.05f, 0.06f, 0.08f, 0.72f)); var r = pill.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2((i + 0.5f) / 3f, 0); r.pivot = new Vector2(0.5f, 0); r.anchoredPosition = new Vector2(0, 52); r.sizeDelta = new Vector2(132, 26);
                var t = UiKit.Label(pill.transform, names[i], 13, UiKit.Text, FontStyle.Bold, TextAnchor.MiddleCenter, 0, true); UiKit.Stretch(t.rectTransform, 2f);
                paneLabels[i] = t;
            }
            compareOverlay.SetActive(false);
        }

        void BuildHud(Transform parent)
        {
            var box = UiKit.Box("Hud", parent, new Color(0.055f, 0.065f, 0.085f, 0.70f)); hud = box.rectTransform;
            hud.anchorMin = hud.anchorMax = hud.pivot = new Vector2(0, 1); hud.anchoredPosition = new Vector2(Margin, -Margin); hud.sizeDelta = new Vector2(300, 46);
            dot = UiKit.Box("Dot", hud, Color.gray, false); dot.sprite = UiKit.Circle; var dr = dot.rectTransform;
            dr.anchorMin = dr.anchorMax = new Vector2(0, 1); dr.pivot = new Vector2(0.5f, 0.5f); dr.anchoredPosition = new Vector2(15, -16); dr.sizeDelta = new Vector2(9, 9);
            hudTitle = UiKit.Label(hud, "SemanticVR", 13, UiKit.Text, FontStyle.Bold, TextAnchor.MiddleLeft, 0, true); hudTitle.supportRichText = true;
            var t = hudTitle.rectTransform; t.anchorMin = new Vector2(0, 1); t.anchorMax = new Vector2(1, 1); t.pivot = new Vector2(0, 1); t.offsetMin = new Vector2(28, -28); t.offsetMax = new Vector2(-10, -6);
            hudLine = UiKit.Label(hud, "", 12, UiKit.Muted, FontStyle.Normal, TextAnchor.MiddleLeft, 0, true);
            var l = hudLine.rectTransform; l.anchorMin = new Vector2(0, 0); l.anchorMax = new Vector2(1, 0); l.pivot = new Vector2(0, 0); l.offsetMin = new Vector2(14, 5); l.offsetMax = new Vector2(-10, 24);
        }

        void BuildHint(Transform parent)
        {
            hintBg = UiKit.PillBox("Hint", parent, new Color(0.055f, 0.065f, 0.085f, 0.70f)); hint = hintBg.rectTransform;
            hint.anchorMin = hint.anchorMax = hint.pivot = new Vector2(0.5f, 0); hint.anchoredPosition = new Vector2(0, 16); hint.sizeDelta = new Vector2(560, 28);
            hintText = UiKit.Label(hint, "WASD move  ·  Mouse look  ·  Shift run  ·  Tab settings  ·  1 2 3 view  ·  C compare", 13, UiKit.Text, FontStyle.Normal, TextAnchor.MiddleCenter, 0, true);
            UiKit.Stretch(hintText.rectTransform, 6f);
        }

        /// <summary>Floating settings button (top right). While walking it shows the Tab shortcut; with the cursor free it toggles the panel.</summary>
        void BuildGear(Transform parent)
        {
            gearBg = UiKit.PillBox("SettingsButton", parent, new Color(0.055f, 0.065f, 0.085f, 0.78f), true); gear = gearBg.rectTransform;
            gear.anchorMin = gear.anchorMax = gear.pivot = new Vector2(1, 1); gear.anchoredPosition = new Vector2(-Margin, -Margin); gear.sizeDelta = new Vector2(GearW, GearH);
            var btn = gearBg.gameObject.AddComponent<Button>(); btn.targetGraphic = gearBg; btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var cs = btn.colors; cs.highlightedColor = new Color(1f, 1f, 1f, 0.85f); cs.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cs.disabledColor = Color.white; btn.colors = cs;
            btn.onClick.AddListener(() => { if (input != null && input.MenuOpen) { if (panelOpen) CloseSettings(false); else OpenSettings(); } });
            // icon: three little slider tracks
            var icon = UiKit.Rect("Icon", gear); icon.anchorMin = icon.anchorMax = new Vector2(0, 0.5f); icon.pivot = new Vector2(0, 0.5f); icon.anchoredPosition = new Vector2(12, 0); icon.sizeDelta = new Vector2(16, 14);
            for (int i = 0; i < 3; i++)
            {
                var bar = UiKit.PillBox("Bar" + i, icon, UiKit.Text); var r = bar.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(16, 2); r.anchoredPosition = new Vector2(0, -i * 6f); gearBars[i] = bar;
                var knob = UiKit.Box("Knob", bar.transform, UiKit.Text, false); knob.sprite = UiKit.Circle; var k = knob.rectTransform; k.anchorMin = k.anchorMax = new Vector2(i == 1 ? 0.7f : (i == 0 ? 0.3f : 0.5f), 0.5f); k.sizeDelta = new Vector2(6, 6);
            }
            gearLabel = UiKit.Label(gear, "Settings", 13, UiKit.Text, FontStyle.Bold, TextAnchor.MiddleLeft, 0, true);
            var lr = gearLabel.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(36, 2); lr.offsetMax = new Vector2(-44, -2);
            var key = UiKit.Box("Key", gear, new Color(1, 1, 1, 0.14f)); var kr2 = key.rectTransform; kr2.anchorMin = kr2.anchorMax = new Vector2(1, 0.5f); kr2.pivot = new Vector2(1, 0.5f); kr2.anchoredPosition = new Vector2(-8, 0); kr2.sizeDelta = new Vector2(28, 18);
            var kt = UiKit.Label(key.transform, "Tab", 10, UiKit.Muted, FontStyle.Bold, TextAnchor.MiddleCenter, 0, true); UiKit.Stretch(kt.rectTransform);
        }

        void BuildPanel(Transform parent)
        {
            var bg = UiKit.Box("SettingsPanel", parent, UiKit.Panel, true, true); panel = bg.rectTransform;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 1);
            var edge = bg.gameObject.AddComponent<Outline>(); edge.effectColor = UiKit.PanelEdge; edge.effectDistance = new Vector2(1, -1);
            var col = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(0, 0, 0, 0); col.spacing = 0; col.childControlWidth = true; col.childControlHeight = true; col.childForceExpandWidth = true; col.childForceExpandHeight = false;

            BuildHeader(panel);
            BuildTabs(panel);
            scroll = UiKit.MakeScroll(panel, out content, new RectOffset(16, 8, 4, 16));
            BuildPages();
            BuildFooter(panel);
            SelectTab(0);
        }

        void BuildHeader(Transform parent)
        {
            var h = UiKit.Rect("Header", parent); UiKit.Size(h, minH: HeaderH, prefH: HeaderH);
            var title = UiKit.Label(h, "Colour assist", 18, UiKit.Text, FontStyle.Bold, TextAnchor.MiddleLeft);
            var tr = title.rectTransform; tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0, 1); tr.offsetMin = new Vector2(16, -34); tr.offsetMax = new Vector2(-54, -8);
            headerSummary = UiKit.Label(h, "", 12, UiKit.Muted, FontStyle.Normal, TextAnchor.MiddleLeft, 0, true);
            var sr = headerSummary.rectTransform; sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 0); sr.pivot = new Vector2(0, 0); sr.offsetMin = new Vector2(16, 6); sr.offsetMax = new Vector2(-54, 24);
            // close button
            var cb = UiKit.Box("Close", h, UiKit.ChipOff, true, true); var cr = cb.rectTransform; cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1, 1); cr.anchoredPosition = new Vector2(-12, -12); cr.sizeDelta = new Vector2(32, 32);
            var btn = cb.gameObject.AddComponent<Button>(); btn.targetGraphic = cb; btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var cs = btn.colors; cs.highlightedColor = new Color(1f, 0.55f, 0.55f, 1f); cs.pressedColor = new Color(0.85f, 0.45f, 0.45f, 1f); btn.colors = cs;
            var x = UiKit.Label(cb.transform, "×", 24, UiKit.Text, FontStyle.Normal, TextAnchor.MiddleCenter); UiKit.Stretch(x.rectTransform); x.rectTransform.offsetMax = new Vector2(0, 2);
            btn.onClick.AddListener(() => CloseSettings(true));
            var sep = UiKit.Box("Sep", h, new Color(1, 1, 1, 0.08f), false); var sp = sep.rectTransform; sp.anchorMin = new Vector2(0, 0); sp.anchorMax = new Vector2(1, 0); sp.pivot = new Vector2(0.5f, 0); sp.sizeDelta = new Vector2(0, 1);
        }

        void BuildTabs(Transform parent)
        {
            var row = UiKit.HRow(parent, TabsH, 5f, "Tabs"); row.padding = new RectOffset(12, 12, 6, 4);
            for (int i = 0; i < 4; i++) { int k = i; tabChips[i] = UiKit.MakeChip(row.transform, TabNames[i], () => SelectTab(k), 13, 32); }
        }

        Transform NewPage(int index)
        {
            var v = UiKit.VStack(content, "Page_" + PageTitles[index].Replace(' ', '_'), 10, new RectOffset(0, 0, 4, 0));
            pages[index] = v.gameObject;
            UiKit.Label(v.transform, PageTitles[index], 17, UiKit.Text, FontStyle.Bold, TextAnchor.MiddleLeft, 26);
            return v.transform;
        }

        static void Sub(Transform p, string text) { var t = UiKit.Label(p, text.ToUpperInvariant(), 11, UiKit.Muted, FontStyle.Bold, TextAnchor.LowerLeft, 20); }
        static Text Note(Transform p, string text) { var t = UiKit.Label(p, text, 12, UiKit.Muted, FontStyle.Normal, TextAnchor.UpperLeft, 0, false, true); return t; }

        /// <summary>Slider with a caption and live value on one line above it. The whole block dims with its CanvasGroup.</summary>
        Slider SliderBlock(Transform parent, string caption, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange, out Text valueLabel, out CanvasGroup group)
        {
            var block = UiKit.VStack(parent, caption.Replace(' ', '_'), 2); group = block.gameObject.AddComponent<CanvasGroup>();
            var row = UiKit.HRow(block.transform, 22, 6f);
            var l = UiKit.Label(row.transform, caption, 13, UiKit.Text, FontStyle.Normal, TextAnchor.MiddleLeft, 0, true); UiKit.Size(l, flexW: 1);
            valueLabel = UiKit.Label(row.transform, "", 13, UiKit.Accent, FontStyle.Bold, TextAnchor.MiddleRight, 0, true); UiKit.Size(valueLabel, minW: 110, prefW: 140);
            return UiKit.MakeSlider(block.transform, min, max, value, onChange);
        }

        void BuildPages()
        {
            // ---- Colour Vision
            var p = NewPage(0);
            Note(p, "Pick the colour vision type to simulate and correct for.");
            Sub(p, "Type");
            familyChips = UiKit.ChipGrid(p, FamilyNames, 5, SetFamily, 13);
            Sub(p, "Form");
            typeChips = UiKit.ChipGrid(p, new[] { "Partial (anomaly)", "Complete (-opia)" }, 2, i => SetComplete(i == 1), 13);
            Sub(p, "Severity");
            severity = SliderBlock(p, "Severity", 0, 100, 100, v => { processor.Profile.severityPercent = Mathf.Round(v); Refresh(); }, out severityValue, out severityGroup);
            severityNote = Note(p, "");

            // ---- Correction
            p = NewPage(1);
            Note(p, "How colours are shifted so they stay distinguishable.");
            Sub(p, "Mode");
            modeChips = UiKit.ChipGrid(p, ModeNames, 2, SetMode, 14, 38);
            modeNote = Note(p, "");
            Sub(p, "Strength");
            strength = SliderBlock(p, "Strength", 0, 1, 1, v => { processor.Settings.strength = v; Refresh(); }, out strengthValue, out _);

            // ---- Semantic Priority
            p = NewPage(2);
            Note(p, "Give extra correction to the objects that matter most for your task.");
            prioSwitch = UiKit.MakeSwitchRow(p, "Prioritise important objects", () => { assistLayer.prioritise = !assistLayer.prioritise; Refresh(); });
            focus = SliderBlock(p, "Priority focus", 0, 1, 0.5f, v => { assistLayer.focus = v; Refresh(); }, out focusValue, out focusGroup);
            Sub(p, "Importance context");
            ctxChips = UiKit.ChipGrid(p, ContextNames, 3, SetContext, 13);
            ctxNote = Note(p, "");

            // ---- Display and Comparison
            p = NewPage(3);
            Note(p, "Choose what is shown on screen.");
            Sub(p, "View");
            string[] vn = { "Original", "Simulated", "Corrected" };
            viewChips = UiKit.ChipGrid(p, vn, 3, i => SetView((AssistViewMode)i), 13);
            compareSwitch = UiKit.MakeSwitchRow(p, "Compare side by side  [C]", () => SetCompare(!assistView.Compare));
            Note(p, "Compare shows Original, CVD simulated and Corrected next to each other.");
            Sub(p, "Developer");
            UiKit.MakeChip(p, "Debug info  [F1]", () => debug?.Toggle(), 13, 34);
        }

        void BuildFooter(Transform parent)
        {
            var f = UiKit.Rect("Footer", parent); UiKit.Size(f, minH: FooterH, prefH: FooterH);
            var sep = UiKit.Box("Sep", f, new Color(1, 1, 1, 0.08f), false); var sp = sep.rectTransform; sp.anchorMin = new Vector2(0, 1); sp.anchorMax = new Vector2(1, 1); sp.pivot = new Vector2(0.5f, 1); sp.sizeDelta = new Vector2(0, 1);
            var row = UiKit.HRow(f, 34, 8f); var rr = (RectTransform)row.transform; rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = new Vector2(16, 10); rr.offsetMax = new Vector2(-16, -10);
            UiKit.MakeChip(row.transform, "Reset to defaults", ResetAll, 13, 34);
            UiKit.MakeChip(row.transform, "Done", () => CloseSettings(true), 13, 34).Set(true);
        }

        // ============================================================== layout
        void SelectTab(int i)
        {
            tab = i;
            for (int k = 0; k < 4; k++) { pages[k].SetActive(k == i); tabChips[k].Set(k == i); }
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            layoutDirty = true;
        }

        void LateUpdate()
        {
            if (!built) return;
            // Landscape views match the canvas to the screen height (consistent vertical room, so the panel never loses height on ultrawide);
            // portrait/square views blend width and height so the panel still fits the width.
            var px = canvas.pixelRect.size;
            if (px.y > 1f) { float m = px.x / px.y >= 1.25f ? 1f : 0.5f; if (!Mathf.Approximately(scaler.matchWidthOrHeight, m)) scaler.matchWidthOrHeight = m; }
            var size = canvasRt.rect.size;
            if (size != lastCanvasSize) { lastCanvasSize = size; layoutDirty = true; measureAll = true; }
            if (layoutDirty) { layoutDirty = false; ApplyLayout(); }
        }

        /// <summary>Sizes every floating element from the current canvas rectangle so nothing can leave the visible screen.</summary>
        void ApplyLayout()
        {
            float w = canvasRt.rect.width, h = canvasRt.rect.height;
            if (w <= 1f || h <= 1f) return;
            // status pill (top left) leaves room for the settings button
            hud.sizeDelta = new Vector2(Mathf.Clamp(w - GearW - 3f * Margin, 120f, 300f), 46f);
            // hint (bottom centre)
            hint.sizeDelta = new Vector2(Mathf.Min(580f, w - 2f * Margin), 28f);
            // settings panel under the button, right aligned, never taller than the space left
            float top = Margin + GearH + 8f;
            float pw = Mathf.Min(PanelMaxW, w - 2f * Margin);
            float maxH = Mathf.Max(120f, h - top - Margin);
            panel.anchoredPosition = new Vector2(-Margin, -top);
            if (panelOpen)
            {
                panel.sizeDelta = new Vector2(pw, panel.sizeDelta.y);            // width first: wrapped notes depend on it
                if (measureAll)
                {
                    // measure every page once per size change so the panel is as tall as the tallest page and does not jump between tabs
                    for (int k = 0; k < 4; k++) { for (int j = 0; j < 4; j++) pages[j].SetActive(j == k); LayoutRebuilder.ForceRebuildLayoutImmediate(content); pageH[k] = LayoutUtility.GetPreferredHeight(content); }
                    for (int j = 0; j < 4; j++) pages[j].SetActive(j == tab);
                    measureAll = false;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                pageH[tab] = LayoutUtility.GetPreferredHeight(content);
                float body = Mathf.Max(pageH);
                float need = HeaderH + TabsH + body + FooterH;
                panel.sizeDelta = new Vector2(pw, Mathf.Min(maxH, Mathf.Max(PanelMinH, need)));
            }
            else panel.sizeDelta = new Vector2(pw, Mathf.Min(maxH, 300f));
        }

        // ============================================================== actions (also used by tests)
        public void OpenSettings() { panelOpen = true; ApplySettingsVisibility(); }
        /// <param name="resumeWalking">true: also re-lock the cursor and go back to walking (Close / Done). false: just hide the panel.</param>
        public void CloseSettings(bool resumeWalking)
        {
            panelOpen = false; ApplySettingsVisibility();
            if (resumeWalking && input != null && input.MenuOpen) input.SetMenu(false);
        }

        void ApplySettingsVisibility()
        {
            if (!built) return;
            panel.gameObject.SetActive(panelOpen);
            layoutDirty = true;
            bool menu = input != null && input.MenuOpen;
            var gb = gear.GetComponent<Button>(); gb.interactable = menu;
            gearBg.color = new Color(0.055f, 0.065f, 0.085f, panelOpen ? 0.95f : (menu ? 0.85f : 0.55f));
            var tint = panelOpen ? UiKit.Accent : UiKit.Text; foreach (var b in gearBars) { b.color = tint; foreach (var k in b.GetComponentsInChildren<Image>()) k.color = tint; }
            gearLabel.color = panelOpen ? Color.white : UiKit.Text;
        }

        public void SetView(AssistViewMode v) { assistView.View = v; assistView.Compare = false; Refresh(); }
        public void SetCompare(bool on) { assistView.Compare = on; Refresh(); }

        public void SetFamily(int family)
        {
            bool complete = IsComplete(processor.Profile.type);
            processor.Profile.type = (complete ? Complete : Partial)[family];
            Refresh();
        }

        public void SetComplete(bool complete)
        {
            int f = FamilyOf(processor.Profile.type); if (f < 1 || f > 3) return;
            processor.Profile.type = (complete ? Complete : Partial)[f];
            Refresh();
        }

        public void ToggleComplete()
        {
            int f = FamilyOf(processor.Profile.type); if (f < 1 || f > 3) return;
            SetComplete(!IsComplete(processor.Profile.type));
        }

        public void SetMode(int i) { processor.Settings.mode = (CorrectionMode)i; Refresh(); }

        public void SetContext(int i)
        {
            if (perception != null && perception.Importance != null) perception.Importance.ActiveProfile = (ImportanceProfileType)i;
            Refresh();
        }

        void ResetAll()
        {
            processor.ResetToAssets();
            assistLayer.prioritise = true; assistLayer.focus = 0.5f; assistView.View = AssistViewMode.Corrected; assistView.Compare = false;
            Refresh();
        }

        static bool IsComplete(CvdType t) => t == CvdType.Protanopia || t == CvdType.Deuteranopia || t == CvdType.Tritanopia || t == CvdType.Achromatopsia || t == CvdType.Normal;
        static int FamilyOf(CvdType t)
        {
            switch (t)
            {
                case CvdType.Normal: return 0;
                case CvdType.Protanopia: case CvdType.Protanomaly: return 1;
                case CvdType.Deuteranopia: case CvdType.Deuteranomaly: return 2;
                case CvdType.Tritanopia: case CvdType.Tritanomaly: return 3;
                case CvdType.Achromatopsia: return 4;
                default: return -1;
            }
        }

        public static string ProfileName(CvdProfile p)
        {
            string n = p.type.ToString();
            bool sev = !IsComplete(p.type) && p.type != CvdType.Custom;
            return sev ? $"{n} {p.severityPercent:0}%" : n;
        }

        // ============================================================== state sync
        void OnMenu(bool open)
        {
            if (!built) return;
            measureAll = true;
            if (open) panelOpen = true; else panelOpen = false;   // Tab/Esc opens the panel; resuming walking hides it
            crosshair.SetActive(!open);
            ApplySettingsVisibility();
            if (open) hintTimer = 0f; else hintTimer = Mathf.Max(hintTimer, 5f);
            Refresh();
        }

        void Update()
        {
            if (!built || processor == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) SetView(AssistViewMode.Original);
                if (kb.digit2Key.wasPressedThisFrame) SetView(AssistViewMode.Simulated);
                if (kb.digit3Key.wasPressedThisFrame) SetView(AssistViewMode.Corrected);
                if (kb.cKey.wasPressedThisFrame) SetCompare(!assistView.Compare);
                if (kb.f1Key.wasPressedThisFrame) debug?.Toggle();
                if (kb.f2Key.wasPressedThisFrame) debug?.ToggleDetections();
            }
            hintTimer -= Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(hintTimer);
            hintBg.color = new Color(0.055f, 0.065f, 0.085f, a * 0.70f); hintText.color = new Color(1, 1, 1, a);
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f) { refreshTimer = 0.2f; RefreshHud(); }
            bool cmp = assistView.Compare && assistView.ProcessedOnScreen;
            if (compareOverlay.activeSelf != cmp) compareOverlay.SetActive(cmp);
        }

        void RefreshHud()
        {
            var p = processor.Profile; var st = processor.Settings;
            string view = assistView.Compare ? "Compare" : assistView.View.ToString();
            hudLine.text = $"{view}  ·  {ProfileName(p)}  ·  {ModeLabel(st.mode)}";
            string ai; Color c;
            switch (perception != null ? perception.Status : PipelineStatus.Uninitialised)
            {
                case PipelineStatus.Ready: ai = "AI ready"; c = new Color(0.25f, 0.85f, 0.45f); break;
                case PipelineStatus.Initialising: ai = "AI loading…"; c = new Color(1f, 0.75f, 0.2f); break;
                case PipelineStatus.Failed: ai = "AI unavailable (tags only)"; c = new Color(1f, 0.35f, 0.3f); break;
                default: ai = "AI off"; c = Color.gray; break;
            }
            hudTitle.text = "SemanticVR   <color=#9aa3b0>" + ai + "</color>"; dot.color = c;
        }

        static string ModeLabel(CorrectionMode m)
        {
            switch (m) { case CorrectionMode.Balanced: return "Balanced"; case CorrectionMode.MaxDistinguishability: return "Max distinction"; case CorrectionMode.NaturalAppearance: return "Natural"; case CorrectionMode.HighContrast: return "High contrast"; default: return "Custom"; }
        }

        public void Refresh()
        {
            if (!built || processor == null || processor.Profile == null) return;
            var p = processor.Profile; var st = processor.Settings;
            for (int i = 0; i < 3; i++) viewChips[i].Set(!assistView.Compare && (int)assistView.View == i);
            compareSwitch.Set(assistView.Compare);
            int fam = FamilyOf(p.type);
            for (int i = 0; i < 5; i++) familyChips[i].Set(i == fam);
            bool partialAllowed = fam >= 1 && fam <= 3;
            typeChips[0].Set(partialAllowed && !IsComplete(p.type)); typeChips[1].Set(partialAllowed && IsComplete(p.type));
            foreach (var c in typeChips) c.button.interactable = partialAllowed;
            bool sevActive = p.type == CvdType.Protanomaly || p.type == CvdType.Deuteranomaly || p.type == CvdType.Tritanomaly || p.type == CvdType.Custom;
            severity.SetValueWithoutNotify(p.severityPercent);
            severityGroup.interactable = sevActive; severityGroup.blocksRaycasts = sevActive; severityGroup.alpha = sevActive ? 1f : 0.4f;
            severityValue.text = sevActive ? $"{p.severityPercent:0}%" : "fixed by type";
            severityNote.text = sevActive ? "Lower values simulate a milder deficiency." : (partialAllowed ? "Complete types have a fixed severity. Choose Partial to adjust it." : "This type has no severity setting.");
            for (int i = 0; i < 4; i++) modeChips[i].Set((int)st.mode == i);
            modeNote.text = (int)st.mode >= 0 && (int)st.mode < ModeNotes.Length ? ModeNotes[(int)st.mode] : "";
            strength.SetValueWithoutNotify(st.strength); strengthValue.text = $"{st.strength * 100f:0}%";
            prioSwitch.Set(assistLayer.prioritise);
            focus.SetValueWithoutNotify(assistLayer.focus); focusValue.text = $"{assistLayer.focus * 100f:0}%";
            focusGroup.interactable = assistLayer.prioritise; focusGroup.blocksRaycasts = assistLayer.prioritise; focusGroup.alpha = assistLayer.prioritise ? 1f : 0.4f;
            int ctx = perception != null && perception.Importance != null ? (int)perception.Importance.ActiveProfile : -1;
            for (int i = 0; i < 3; i++) ctxChips[i].Set(i == ctx);
            ctxNote.text = ctx >= 0 && ctx < ContextNotes.Length ? ContextNotes[ctx] : "Custom importance weights are active.";
            headerSummary.text = $"{ProfileName(p)}  ·  {ModeLabel(st.mode)}  ·  {(assistLayer.prioritise ? "priority-aware" : "uniform")}";
            layoutDirty = true;
            RefreshHud();
        }
    }
}
