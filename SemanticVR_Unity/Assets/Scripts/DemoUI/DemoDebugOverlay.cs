using System.Text;
using SemanticVR.Assist;
using SemanticVR.ColourProcessing;
using SemanticVR.Perception;
using SemanticVR.UI;
using SemanticVR.World;
using UnityEngine;

namespace SemanticVR.DemoUI
{
    /// <summary>Developer read-out, hidden by default. F1 toggles this panel, F2 toggles the detection/mask overlay of <see cref="PerceptionDebugUI"/>.</summary>
    public sealed class DemoDebugOverlay : MonoBehaviour
    {
        public bool visible;
        public PerceptionPipeline perception; public ColourProcessor processor; public SemanticAssistLayer assist; public TrafficSimulation sim; public TrafficLightController lights;
        PerceptionDebugUI detections; GUIStyle style, box; float dt = 0.016f; readonly StringBuilder sb = new StringBuilder();

        void Awake()
        {
            perception = perception != null ? perception : GetComponent<PerceptionPipeline>();
            processor = processor != null ? processor : GetComponent<ColourProcessor>();
            assist = assist != null ? assist : GetComponent<SemanticAssistLayer>();
            detections = GetComponent<PerceptionDebugUI>();
            if (detections != null) detections.enabled = false;
            if (sim == null) sim = FindFirstObjectByType<TrafficSimulation>();
            if (lights == null) lights = FindFirstObjectByType<TrafficLightController>();
        }

        public void Toggle() { visible = !visible; }
        public void ToggleDetections() { if (detections != null) detections.enabled = !detections.enabled; }
        void Update() { dt = Mathf.Lerp(dt, Time.unscaledDeltaTime, 0.05f); }

        void OnGUI()
        {
            if (!visible) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = false };
                box = new GUIStyle(GUI.skin.box);
            }
            sb.Clear();
            sb.AppendLine($"<b>Frame</b> {dt * 1000f:0.0} ms ({1f / dt:0} fps)");
            if (perception != null)
            {
                sb.AppendLine($"<b>AI</b> {perception.Status}  {perception.BackendDescription}");
                sb.AppendLine($"inferences {perception.CompletedInferences}, interval {perception.InferenceIntervalMeasured:0.00} s, objects {perception.LatestObjects.Count}");
                if (perception.LatestResult != null) { var t = perception.LatestResult.Timings; sb.AppendLine($"inference wall {t.InferenceWallMs:0} ms, main-thread {t.InferenceMainMs:0.0} ms, max step {t.MaxStepMs:0.0} ms"); }
                if (perception.Status != PipelineStatus.Ready) sb.AppendLine(perception.StatusMessage);
            }
            if (processor != null) sb.AppendLine($"<b>Colour</b> {processor.LastProcessMs:0.00} ms CPU, matrix {processor.LastMatrixMs:0.000} ms, priority-aware {processor.LastCorrectionPriorityAware}");
            if (assist != null) sb.AppendLine($"assist: AI fade {assist.AiFade:0.00}, AI objects {assist.AiObjectCount}, tag pass {assist.LastPrepareMs:0.00} ms");
            if (lights != null) sb.AppendLine($"<b>Lights</b> NS {lights.NorthSouth}, EW {lights.EastWest}");
            if (sim != null) sb.AppendLine($"<b>Sim</b> {sim.VehicleCount} vehicles ({sim.VehiclesStopped} stopped), {sim.PedestrianCount} pedestrians ({sim.PedestriansCrossing} crossing), {sim.SimMs:0.000} ms");
            var r = new Rect(16, 76, 460, 18 * (sb.ToString().Split('\n').Length) + 14);
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 8), sb.ToString(), style);
        }
    }
}
