using System.Collections.Generic;
using SemanticVR.Confusion;
using Unity.Mathematics;
using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>One graph edge: a tag pair with the CIEDE2000 difference of their colours, as seen by normal vision and under the
    /// simulated CVD profile. Both scorings are always present: the albedo-only baseline (<see cref="DeltaEOriginal"/>,
    /// <see cref="DeltaESimulated"/>) and the C1 predicted lit colour (<see cref="LitDeltaEOriginal"/>, <see cref="LitDeltaESimulated"/>).</summary>
    public struct PairEdge
    {
        public SemanticTag A, B;
        public float Distance;
        /// <summary>Albedo-only baseline: dE00 of the base albedos, normal vision.</summary>
        public float DeltaEOriginal;
        /// <summary>Albedo-only baseline: dE00 of the base albedos after CVD simulation.</summary>
        public float DeltaESimulated;
        /// <summary>C1: dE00 of the predicted lit colours, normal vision.</summary>
        public float LitDeltaEOriginal;
        /// <summary>C1: dE00 of the predicted lit colours after CVD simulation.</summary>
        public float LitDeltaESimulated;

        public float OriginalFor(ConfusionScoring mode) => mode == ConfusionScoring.Lit ? LitDeltaEOriginal : DeltaEOriginal;
        public float SimulatedFor(ConfusionScoring mode) => mode == ConfusionScoring.Lit ? LitDeltaESimulated : DeltaESimulated;
        public bool IsConfusable(ConfusionScoring mode, float simThreshold, float minOriginal) =>
            OriginalFor(mode) >= minOriginal && SimulatedFor(mode) <= simThreshold;

        /// <summary>True when normal vision can tell the pair apart but the CVD observer cannot (the pairs we care about). Albedo-only baseline.</summary>
        public bool IsConfusable(float simThreshold, float minOriginal) =>
            DeltaEOriginal >= minOriginal && DeltaESimulated <= simThreshold;
    }

    /// <summary>Pair-graph data for the debug gizmos (and, later, the solver). Uses only <see cref="ColorMath"/>.</summary>
    public static class SemanticGraph
    {
        public static float3 SrgbToLinear(Color c)
        {
            return new float3(Lin(c.r), Lin(c.g), Lin(c.b));
            static float Lin(float v) => v <= 0.04045f ? v / 12.92f : math.pow((v + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>Lab of an sRGB colour as seen by normal vision.</summary>
        public static float3 Lab(Color srgb) => ColorMath.RgbLinToLab(SrgbToLinear(srgb));

        /// <summary>Lab of an sRGB colour after CVD simulation (clamped to the displayable range, as on screen).</summary>
        public static float3 LabSimulated(Color srgb, CvdType type, float severity) =>
            ColorMath.RgbLinToLab(math.saturate(ColorMath.SimulateCvd(SrgbToLinear(srgb), type, severity)));

        /// <summary>Lab of a LINEAR displayed colour (clamped to [0,1]) as seen by normal vision.</summary>
        public static float3 LabLinear(float3 rgbLin) => ColorMath.RgbLinToLab(math.saturate(rgbLin));

        /// <summary>Lab of a LINEAR displayed colour after CVD simulation (clamped before and after, as on screen).</summary>
        public static float3 LabLinearSimulated(float3 rgbLin, CvdType type, float severity) =>
            ColorMath.RgbLinToLab(math.saturate(ColorMath.SimulateCvd(math.saturate(rgbLin), type, severity)));

        public static float DeltaE(Color a, Color b) => ColorMath.Ciede2000(Lab(a), Lab(b));

        public static float DeltaESimulated(Color a, Color b, CvdType type, float severity) =>
            ColorMath.Ciede2000(LabSimulated(a, type, severity), LabSimulated(b, type, severity));

        /// <summary>
        /// Fill <paramref name="edges"/> with every registry pair whose bounds are closer than <paramref name="maxDistance"/> metres
        /// (tags with a null class, or with includeInGraph off, are skipped). Distance is the gap between bounding boxes. Lab values are computed once per tag.
        /// </summary>
        public static void Build(List<PairEdge> edges, CvdType type, float severity, float maxDistance) =>
            Build(edges, type, severity, maxDistance, LitColorPredictor.Capture());

        /// <summary>The viewer used to break ties between equal faces when predicting lit colour (the main camera, if any).</summary>
        public static Vector3? ViewerPosition() { var c = Camera.main; return c != null ? c.transform.position : (Vector3?)null; }

        /// <summary>As <see cref="Build(List{PairEdge},CvdType,float,float)"/> with an explicit lighting snapshot (tests, lighting what-ifs).</summary>
        public static void Build(List<PairEdge> edges, CvdType type, float severity, float maxDistance, LightingSnapshot lighting)
        {
            edges.Clear();
            var tags = SemanticRegistry.Tags;
            int n = tags.Count;
            var lab = new float3[n]; var labSim = new float3[n]; var litLab = new float3[n]; var litLabSim = new float3[n]; var ctr = new Vector3[n]; var ext = new Vector3[n]; var ok = new bool[n];
            var viewer = ViewerPosition();
            for (int i = 0; i < n; i++)
            {
                var t = tags[i];
                ok[i] = t.includeInGraph && t.CurrentState != null;
                if (!ok[i]) continue;
                lab[i] = Lab(t.CurrentAlbedo);
                labSim[i] = LabSimulated(t.CurrentAlbedo, type, severity);
                float3 lit = LitColorPredictor.PredictDominant(t, lighting, viewer);
                litLab[i] = LabLinear(lit);
                litLabSim[i] = LabLinearSimulated(lit, type, severity);
                var bounds = t.Bounds; ctr[i] = bounds.center; ext[i] = bounds.extents;
            }
            float max2 = maxDistance * maxDistance;
            for (int i = 0; i < n; i++)
            {
                if (!ok[i]) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (!ok[j]) continue;
                    // distance = gap between the two bounding boxes (0 if they touch), so large objects (lawns) still link to neighbours
                    var dv = new Vector3(Mathf.Max(0f, Mathf.Abs(ctr[i].x - ctr[j].x) - ext[i].x - ext[j].x),
                                         Mathf.Max(0f, Mathf.Abs(ctr[i].y - ctr[j].y) - ext[i].y - ext[j].y),
                                         Mathf.Max(0f, Mathf.Abs(ctr[i].z - ctr[j].z) - ext[i].z - ext[j].z));
                    float d2 = dv.sqrMagnitude;
                    if (d2 > max2) continue;
                    var a = tags[i]; var b = tags[j]; int ia = i, ib = j;
                    if (a.Id > b.Id) { (a, b) = (b, a); (ia, ib) = (ib, ia); }
                    edges.Add(new PairEdge
                    {
                        A = a, B = b, Distance = Mathf.Sqrt(d2),
                        DeltaEOriginal = ColorMath.Ciede2000(lab[ia], lab[ib]),
                        DeltaESimulated = ColorMath.Ciede2000(labSim[ia], labSim[ib]),
                        LitDeltaEOriginal = ColorMath.Ciede2000(litLab[ia], litLab[ib]),
                        LitDeltaESimulated = ColorMath.Ciede2000(litLabSim[ia], litLabSim[ib]),
                    });
                }
            }
        }

        /// <summary>Gizmo ramp: orange (ΔE 0, indistinguishable) to blue (ΔE >= <paramref name="maxDeltaE"/>). Orange/blue
        /// stays distinguishable under protan/deutan simulation.</summary>
        public static Color EdgeColor(float deltaE, float maxDeltaE)
        {
            float t = Mathf.Clamp01(deltaE / Mathf.Max(maxDeltaE, 1e-3f));
            return Color.Lerp(new Color(1f, 0.45f, 0.05f), new Color(0.23f, 0.49f, 0.85f), t);
        }
    }
}
