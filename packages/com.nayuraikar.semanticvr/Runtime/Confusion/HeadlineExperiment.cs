using System.Collections.Generic;
using System.Linq;
using System.Text;
using SemanticVR.Core;
using Unity.Mathematics;

namespace SemanticVR.Confusion
{
    /// <summary>Per-edge confusion verdicts: (a) albedo-only baseline, (b) C1 lit-colour prediction, (c) rendered ground truth.</summary>
    public sealed class HeadlineEdge
    {
        public PairEdge Edge;
        public float GtOriginal, GtSimulated;
        public bool AlbedoConfusable, LitConfusable, GtConfusable;
        public bool AlbedoWrong => AlbedoConfusable != GtConfusable;
        public bool LitWrong => LitConfusable != GtConfusable;
    }

    public sealed class HeadlineResult
    {
        public string Scene, Preset;
        public CvdType Type; public float Severity, SimThreshold, MinOriginal;
        public List<HeadlineEdge> Edges = new List<HeadlineEdge>();
        public int EdgesTotal, EdgesSkipped;
        public int Evaluated => Edges.Count;
        public int GtConfusable => Edges.Count(e => e.GtConfusable);
        public int AlbedoWrong => Edges.Count(e => e.AlbedoWrong);
        public int LitWrong => Edges.Count(e => e.LitWrong);
        public int AnyConfusable => Edges.Count(e => e.AlbedoConfusable || e.LitConfusable || e.GtConfusable);
        public int AlbedoFalsePositive => Edges.Count(e => e.AlbedoConfusable && !e.GtConfusable);
        public int AlbedoFalseNegative => Edges.Count(e => !e.AlbedoConfusable && e.GtConfusable);
        public int LitFalsePositive => Edges.Count(e => e.LitConfusable && !e.GtConfusable);
        public int LitFalseNegative => Edges.Count(e => !e.LitConfusable && e.GtConfusable);
        /// <summary>Mean |simulated dE00 - ground-truth simulated dE00| over evaluated edges.</summary>
        public float AlbedoSimError => Edges.Count == 0 ? float.NaN : Edges.Average(e => System.Math.Abs(e.Edge.DeltaESimulated - e.GtSimulated));
        public float LitSimError => Edges.Count == 0 ? float.NaN : Edges.Average(e => System.Math.Abs(e.Edge.LitDeltaESimulated - e.GtSimulated));
    }

    /// <summary>
    /// Headline C1 experiment. For every pair-graph edge whose two endpoints both have an unobstructed, unshadowed validation
    /// sample, compare the confusable/not verdict of (a) albedo-only, (b) lit-colour prediction (the deployed graph, which uses
    /// each object's dominant face normal, no readback) with (c) the verdict computed from the rendered pixels
    /// (same Machado -> Lab -> CIEDE2000 pipeline). Edges with a skipped endpoint are excluded and counted.
    /// </summary>
    public static class HeadlineExperiment
    {
        public static HeadlineResult Run(ValidationResult v, float maxEdgeDistance, CvdType type = CvdType.Deutan, float severity = 1f,
                                         float simThreshold = 15f, float minOriginal = 10f)
        {
            var r = new HeadlineResult { Scene = v.Scene, Preset = v.Preset, Type = type, Severity = severity, SimThreshold = simThreshold, MinOriginal = minOriginal };
            var edges = new List<PairEdge>();
            SemanticGraph.Build(edges, type, severity, maxEdgeDistance);
            var byTag = v.Ok.Where(s => s.Tag != null).ToDictionary(s => s.Tag, s => s);
            r.EdgesTotal = edges.Count;
            foreach (var e in edges)
            {
                if (!byTag.TryGetValue(e.A, out var sa) || !byTag.TryGetValue(e.B, out var sb)) { r.EdgesSkipped++; continue; }
                float gtO = ColorMath.Ciede2000(SemanticGraph.LabLinear(sa.RenderedLin), SemanticGraph.LabLinear(sb.RenderedLin));
                float gtS = ColorMath.Ciede2000(SemanticGraph.LabLinearSimulated(sa.RenderedLin, type, severity), SemanticGraph.LabLinearSimulated(sb.RenderedLin, type, severity));
                r.Edges.Add(new HeadlineEdge
                {
                    Edge = e, GtOriginal = gtO, GtSimulated = gtS,
                    AlbedoConfusable = e.IsConfusable(ConfusionScoring.Albedo, simThreshold, minOriginal),
                    LitConfusable = e.IsConfusable(ConfusionScoring.Lit, simThreshold, minOriginal),
                    GtConfusable = gtO >= minOriginal && gtS <= simThreshold,
                });
            }
            return r;
        }

        static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        public static string EdgeCsv(IEnumerable<HeadlineResult> runs)
        {
            var sb = new StringBuilder("scene,preset,tag_a,tag_b,class_a,class_b,distance_m,albedo_de_orig,albedo_de_sim,albedo_confusable,lit_de_orig,lit_de_sim,lit_confusable,gt_de_orig,gt_de_sim,gt_confusable,albedo_wrong,lit_wrong\n");
            foreach (var r in runs)
                foreach (var e in r.Edges)
                    sb.Append(string.Join(",", r.Scene, r.Preset, Q(e.Edge.A.name), Q(e.Edge.B.name), Q(e.Edge.A.Class.displayName), Q(e.Edge.B.Class.displayName), F(e.Edge.Distance),
                        F(e.Edge.DeltaEOriginal), F(e.Edge.DeltaESimulated), e.AlbedoConfusable ? 1 : 0,
                        F(e.Edge.LitDeltaEOriginal), F(e.Edge.LitDeltaESimulated), e.LitConfusable ? 1 : 0,
                        F(e.GtOriginal), F(e.GtSimulated), e.GtConfusable ? 1 : 0, e.AlbedoWrong ? 1 : 0, e.LitWrong ? 1 : 0)).Append('\n');
            return sb.ToString();
        }

        public static string SummaryCsv(IEnumerable<HeadlineResult> runs)
        {
            var sb = new StringBuilder("scene,preset,cvd,severity,edges_total,edges_skipped,edges_evaluated,edges_any_confusable,gt_confusable,albedo_wrong,albedo_false_pos,albedo_false_neg,lit_wrong,lit_false_pos,lit_false_neg,albedo_mean_abs_sim_err,lit_mean_abs_sim_err\n");
            foreach (var r in runs)
                sb.Append(string.Join(",", r.Scene, r.Preset, r.Type, F(r.Severity), r.EdgesTotal, r.EdgesSkipped, r.Evaluated, r.AnyConfusable, r.GtConfusable,
                    r.AlbedoWrong, r.AlbedoFalsePositive, r.AlbedoFalseNegative, r.LitWrong, r.LitFalsePositive, r.LitFalseNegative, F(r.AlbedoSimError), F(r.LitSimError))).Append('\n');
            return sb.ToString();
        }

        public static string Describe(HeadlineResult r) =>
            $"{r.Scene}{(string.IsNullOrEmpty(r.Preset) ? "" : "/" + r.Preset)}: {r.Evaluated} edges evaluated ({r.EdgesSkipped} skipped), ground-truth confusable {r.GtConfusable}; " +
            $"albedo-only wrong {r.AlbedoWrong} (FP {r.AlbedoFalsePositive}, FN {r.AlbedoFalseNegative}), lit-colour wrong {r.LitWrong} (FP {r.LitFalsePositive}, FN {r.LitFalseNegative})";
    }
}
