using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SemanticVR.Assist;
using SemanticVR.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SemanticVR.DemoUI
{
    /// <summary>
    /// Layout test for <see cref="DemoUiController"/>. Inert unless SVR_UI_SHOTS names an output directory. For a list of Game-view
    /// resolutions and aspect ratios it renders the real UI off-screen (PNG per state), and checks: every visible control lies inside the
    /// screen (and inside the panel / scroll viewport), no single-line text spills out of its box, every control is reachable by scrolling,
    /// every control is the topmost raycast hit at its centre (so the mouse really reaches it), and a simulated press-and-drag moves the
    /// severity slider. Measures layout only; it does not simulate a physical mouse or a monitor.
    /// </summary>
    public sealed class UiLayoutCheck : MonoBehaviour
    {
        static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1366, 768), new Vector2Int(1024, 768), new Vector2Int(800, 600),
            new Vector2Int(640, 480), new Vector2Int(2560, 1080), new Vector2Int(900, 1200), new Vector2Int(1280, 400), new Vector2Int(480, 800),
        };

        string outDir; bool allPass = true; int failures;
        readonly List<string> lines = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SVR_UI_SHOTS"))) return;
            new GameObject("~UiLayoutCheck").AddComponent<UiLayoutCheck>();
        }

        void Log(string s) { lines.Add(s); Debug.Log("[UiCheck] " + s); }
        void Check(string name, bool pass, string detail = "") { if (!pass) { allPass = false; failures++; } Log($"{(pass ? "PASS" : "FAIL")} {name} {detail}"); }

        IEnumerator Start()
        {
            outDir = System.Environment.GetEnvironmentVariable("SVR_UI_SHOTS"); Directory.CreateDirectory(outDir);
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            DemoUiController ui = null;
            for (int i = 0; i < 600 && ui == null; i++) { ui = FindFirstObjectByType<DemoUiController>(); yield return null; }
            if (ui == null) { Check("DemoUiController present", false); Finish(); yield break; }
            for (int i = 0; i < 30; i++) yield return null;
            var desk = FindFirstObjectByType<DesktopPlayerInput>();
            desk.enabled = false;   // batch mode cannot lock a cursor, and the input script would re-open the menu; SetMenu() still works while disabled
            var canvas = ui.GetComponentInChildren<Canvas>(true);
            var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<PlayerController>().head.GetComponent<Camera>();
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            var panel = canvas.transform.Find("SettingsPanel") as RectTransform;
            var scroll = panel.GetComponentInChildren<ScrollRect>(true);
            Check("debug overlay hidden by default", FindFirstObjectByType<DemoDebugOverlay>() == null || !FindFirstObjectByType<DemoDebugOverlay>().visible);

            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.2f;
            foreach (var sz in Sizes)
            {
                string tag = $"{sz.x}x{sz.y}";
                var rt = new RenderTexture(sz.x, sz.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt; cam.aspect = (float)sz.x / sz.y;
                // 1) walking state: settings button + status pill + hint, panel closed
                desk.SetMenu(false, true); ui.CloseSettings(false);
                yield return Settle();
                Log($"{tag} walking: MenuOpen={desk.MenuOpen} panel.activeSelf={panel.gameObject.activeSelf} activeInHierarchy={panel.gameObject.activeInHierarchy}");
                yield return CheckState(canvas, panel, scroll, raycaster, cam, $"{tag} walking", false);
                yield return Settle();
                Shot(cam, rt, $"{tag}_walking");
                // 2) menu open, each section
                desk.SetMenu(true, true);
                yield return Settle();
                var tabs = panel.GetComponentsInChildren<Button>(true);
                string[] names = { "Vision", "Correction", "Priority", "Display" };
                for (int t = 0; t < 4; t++)
                {
                    var tabBtn = FindChipButton(panel, names[t]); tabBtn.onClick.Invoke();
                    yield return Settle();
                    var scrolled = new List<float>();
                    yield return CheckState(canvas, panel, scroll, raycaster, cam, $"{tag} {names[t]}", true);
                    scroll.verticalNormalizedPosition = 1f; yield return Settle();
                    Shot(cam, rt, $"{tag}_{names[t].ToLowerInvariant()}");
                }
                // 3) slider drag through the real raycaster, on the first (severity) slider
                if (sz.x == 1280 && sz.y == 720) yield return DragTest(ui, panel, scroll, raycaster, cam);
                cam.targetTexture = null; cam.ResetAspect(); rt.Release(); Destroy(rt);
            }
            desk.SetMenu(false, true);
            Finish();
        }

        IEnumerator Settle() { for (int i = 0; i < 4; i++) yield return null; Canvas.ForceUpdateCanvases(); yield return null; }

        static Button FindChipButton(Transform root, string label)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true)) { var t = b.GetComponentInChildren<Text>(true); if (t != null && t.text == label) return b; }
            return null;
        }

        // ------------------------------------------------------------------ checks
        static Rect CanvasLocalRect(RectTransform canvasRt, RectTransform rt)
        {
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            var min = (Vector2)canvasRt.InverseTransformPoint(c[0]); var max = (Vector2)canvasRt.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        static bool Inside(Rect outer, Rect inner, float tol = 0.75f) => inner.xMin >= outer.xMin - tol && inner.xMax <= outer.xMax + tol && inner.yMin >= outer.yMin - tol && inner.yMax <= outer.yMax + tol;

        IEnumerator CheckState(Canvas canvas, RectTransform panel, ScrollRect scroll, GraphicRaycaster raycaster, Camera cam, string label, bool panelOpen)
        {
            var canvasRt = (RectTransform)canvas.transform; var screen = canvasRt.rect;
            var graphics = canvas.GetComponentsInChildren<Graphic>(false);
            var vpRt = scroll.viewport; var contentRt = scroll.content;
            int bad = 0; var detail = new StringBuilder();
            // floating elements (outside the scroll content): fully on screen, and inside the panel when they belong to it
            foreach (var g in graphics)
            {
                if (!g.gameObject.activeInHierarchy || g.canvasRenderer.cull) continue;
                if (g.transform.IsChildOf(contentRt)) continue;
                if (g.color.a <= 0.001f) continue;
                var r = CanvasLocalRect(canvasRt, g.rectTransform);
                if (g.rectTransform.rect.width < 0.5f && g.rectTransform.rect.height < 0.5f) continue;
                if (g.transform.name == "Handle" && g.transform.IsChildOf(scroll.verticalScrollbar.transform)) continue;
                if (g.transform.IsChildOf(canvasRt.Find("CompareOverlay"))) continue;
                if (!Inside(screen, r)) { bad++; detail.Append($"[off-screen {PathOf(g.transform)} {r}] "); }
                if (panelOpen && g.transform.IsChildOf(panel) && g.transform != panel && !Inside(CanvasLocalRect(canvasRt, panel), r)) { bad++; detail.Append($"[outside panel {PathOf(g.transform)} {r}] "); }
            }
            Check($"{label}: floating controls on screen", bad == 0, bad == 0 ? $"(screen {screen.width:0}x{screen.height:0} units)" : detail.ToString());
            if (!panelOpen) { Check($"{label}: panel hidden", !panel.gameObject.activeSelf); yield break; }

            // chrome stays compact: tab row and footer keep their designed height (a layout bug once stretched the tabs to 200+ units)
            var tabsRt = panel.Find("Tabs") as RectTransform; var footRt = panel.Find("Footer") as RectTransform;
            Check($"{label}: tab row and footer keep their height", tabsRt.rect.height <= 50f && footRt.rect.height <= 60f, $"tabs {tabsRt.rect.height:0} footer {footRt.rect.height:0}");

            // panel itself inside the screen
            Check($"{label}: panel inside screen", Inside(screen, CanvasLocalRect(canvasRt, panel)), $"panel {CanvasLocalRect(canvasRt, panel)} screen {screen}");

            // scroll content: horizontal containment always, vertical reachability by stepping the scroll position
            var vp = CanvasLocalRect(canvasRt, vpRt);
            var items = new List<Graphic>();
            foreach (var g in contentRt.GetComponentsInChildren<Graphic>(false)) if (g.gameObject.activeInHierarchy && g.color.a > 0.001f && (g.raycastTarget || g is Text)) items.Add(g);
            var seen = new HashSet<Graphic>(); int hBad = 0; var hd = new StringBuilder();
            bool scrollable = LayoutUtility.GetPreferredHeight(contentRt) > vpRt.rect.height + 0.5f;
            const int Steps = 12;
            for (int s = 0; s <= Steps; s++)
            {
                scroll.verticalNormalizedPosition = 1f - s / (float)Steps; Canvas.ForceUpdateCanvases(); yield return null;
                vp = CanvasLocalRect(canvasRt, vpRt);
                foreach (var g in items)
                {
                    var r = CanvasLocalRect(canvasRt, g.rectTransform);
                    if (s == 0 && (r.xMin < vp.xMin - 0.75f || r.xMax > vp.xMax + 0.75f)) { hBad++; hd.Append($"[h-clip {PathOf(g.transform)} {r} vs {vp}] "); }
                    if (r.yMin >= vp.yMin - 0.75f && r.yMax <= vp.yMax + 0.75f) seen.Add(g);
                }
                if (!scrollable) break;
            }
            Check($"{label}: no horizontal clipping in scroll body", hBad == 0, hd.ToString());
            int unreachable = 0; var ud = new StringBuilder();
            foreach (var g in items) if (!seen.Contains(g) && g.rectTransform.rect.height > 0.5f && g.rectTransform.rect.width > 0.5f) { unreachable++; ud.Append($"[{PathOf(g.transform)}] "); }
            Check($"{label}: every control reachable by scrolling{(scrollable ? " (scrolls)" : " (fits)")}", unreachable == 0, ud.ToString());

            // single-line text must fit its own box
            int spill = 0; var sd = new StringBuilder();
            foreach (var t in panel.GetComponentsInChildren<Text>(false))
            {
                if (!t.gameObject.activeInHierarchy || t.resizeTextForBestFit || t.horizontalOverflow != HorizontalWrapMode.Overflow) continue;
                if (t.preferredWidth > t.rectTransform.rect.width + 1f) { spill++; sd.Append($"[{t.text} needs {t.preferredWidth:0} has {t.rectTransform.rect.width:0}] "); }
            }
            Check($"{label}: single-line text fits", spill == 0, sd.ToString());

            // mouse hit-testing: at every selectable's centre (scrolled into view) the topmost raycast hit is that control
            int miss = 0; var md = new StringBuilder(); int tested = 0;
            scroll.verticalNormalizedPosition = 1f; Canvas.ForceUpdateCanvases(); yield return null;
            var selectables = new List<Selectable>(panel.GetComponentsInChildren<Selectable>(false));
            foreach (var sel in selectables)
            {
                if (!sel.gameObject.activeInHierarchy || !sel.IsInteractable()) continue;
                if (sel is Scrollbar) continue;
                var rtSel = (RectTransform)sel.transform;
                if (sel.transform.IsChildOf(contentRt)) yield return BringIntoView(scroll, rtSel, canvasRt);
                var hit = TopHit(raycaster, cam, rtSel);
                tested++;
                if (hit == null || !(hit.transform == sel.transform || hit.transform.IsChildOf(sel.transform))) { miss++; md.Append($"[{PathOf(sel.transform)} hit {(hit == null ? "nothing" : PathOf(hit.transform))}] "); }
            }
            Check($"{label}: {tested} controls are the topmost mouse target", miss == 0, md.ToString());
        }

        static IEnumerator BringIntoView(ScrollRect scroll, RectTransform target, RectTransform canvasRt)
        {
            for (int s = 0; s <= 12; s++)
            {
                scroll.verticalNormalizedPosition = 1f - s / 12f; Canvas.ForceUpdateCanvases(); yield return null;
                var vp = CanvasLocalRect(canvasRt, scroll.viewport); var r = CanvasLocalRect(canvasRt, target);
                if (r.yMin >= vp.yMin - 0.75f && r.yMax <= vp.yMax + 0.75f) yield break;
            }
        }

        static GameObject TopHit(GraphicRaycaster rc, Camera cam, RectTransform target)
        {
            var c = new Vector3[4]; target.GetWorldCorners(c); var centre = (c[0] + c[2]) * 0.5f;
            var ped = new PointerEventData(EventSystem.current) { position = cam.WorldToScreenPoint(centre) };
            var res = new List<RaycastResult>(); rc.Raycast(ped, res);
            return res.Count > 0 ? res[0].gameObject : null;
        }

        IEnumerator DragTest(DemoUiController ui, RectTransform panel, ScrollRect scroll, GraphicRaycaster rc, Camera cam)
        {
            ui.SetFamily(2); ui.SetComplete(false); yield return Settle();
            FindChipButton(panel, "Vision").onClick.Invoke(); yield return Settle(); scroll.verticalNormalizedPosition = 1f; yield return Settle();
            var slider = panel.GetComponentInChildren<Slider>(false); var rt = (RectTransform)slider.transform;
            yield return BringIntoView(scroll, rt, (RectTransform)panel.parent);   // the mouse can only reach what is scrolled into view
            yield return Settle();
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            // the handle slide area is inset by 9 units each side; aim at 25 % and 75 % of the slide range
            float ScreenX(float t) { var p = Vector3.Lerp(c[0], c[3], t); p.y = (c[0].y + c[1].y) * 0.5f; return cam.WorldToScreenPoint(p).x; }
            float y = cam.WorldToScreenPoint((c[0] + c[2]) * 0.5f).y;
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ped.position = new Vector2(ScreenX(0.25f), y);
            var list = new List<RaycastResult>(); rc.Raycast(ped, list);
            if (list.Count == 0) { Check("slider drag: pointer hits slider", false, $"pos {ped.position}"); yield break; }
            Check("slider drag: pointer hits slider", list[0].gameObject.transform.IsChildOf(slider.transform) || list[0].gameObject == slider.gameObject, list[0].gameObject.name);
            ped.pointerCurrentRaycast = ped.pointerPressRaycast = list[0]; ped.pressPosition = ped.position; ped.pointerPress = ped.pointerDrag = slider.gameObject;
            ExecuteEvents.Execute(slider.gameObject, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(slider.gameObject, ped, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(slider.gameObject, ped, ExecuteEvents.beginDragHandler);
            float v1 = slider.value;
            ped.position = new Vector2(ScreenX(0.75f), y); ExecuteEvents.Execute(slider.gameObject, ped, ExecuteEvents.dragHandler);
            float v2 = slider.value;
            ExecuteEvents.Execute(slider.gameObject, ped, ExecuteEvents.endDragHandler); ExecuteEvents.Execute(slider.gameObject, ped, ExecuteEvents.pointerUpHandler);
            Check("slider drag: press near 25 % then drag to 75 % moves the value", v1 < 40f && v2 > 60f && v2 > v1 + 25f, $"after press {v1:0}, after drag {v2:0}");
            Check("slider drag: reaches the processor", Mathf.Abs(ui.processor.Profile.severityPercent - Mathf.Round(slider.value)) < 1f, $"profile severity {ui.processor.Profile.severityPercent:0}");
        }

        // ------------------------------------------------------------------ output
        void Shot(Camera cam, RenderTexture rt, string name)
        {
            Canvas.ForceUpdateCanvases(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tx = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tx.Apply(); RenderTexture.active = prev;
            File.WriteAllBytes(System.IO.Path.Combine(outDir, name + ".png"), tx.EncodeToPNG()); Destroy(tx);
        }

        static string PathOf(Transform t) { var s = new StringBuilder(t.name); for (var p = t.parent; p != null && p.name != "DemoUiCanvas"; p = p.parent) s.Insert(0, p.name + "/"); return s.ToString(); }

        void Finish()
        {
            Log($"DONE all_pass={allPass} failures={failures}");
            File.WriteAllText(System.IO.Path.Combine(outDir, "ui_layout_report.txt"), string.Join("\n", lines));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(allPass ? 0 : 1);
#else
            Application.Quit(allPass ? 0 : 1);
#endif
        }
    }
}
