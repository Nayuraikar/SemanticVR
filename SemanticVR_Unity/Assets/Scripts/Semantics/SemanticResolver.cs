using System;
using System.Collections.Generic;
using SemanticVR.Environment;
using SemanticVR.Perception;

namespace SemanticVR.Semantics
{
    /// <summary>How to choose between an AI category and the manual SemanticTag category when both exist.</summary>
    public enum ConflictPolicy { PreferManual, PreferAI, AiAboveConfidence }

    /// <summary>
    /// Combines AI detections (via <see cref="CocoToSemanticMap"/>) with the manual tag buffer and looks up the
    /// user's importance weights. Read-only with respect to the importance config and to every SemanticTag.
    /// </summary>
    public sealed class SemanticResolver
    {
        public ConflictPolicy Policy = ConflictPolicy.AiAboveConfidence;
        /// <summary>AI wins a conflict only if its confidence is at least this (policy AiAboveConfidence).</summary>
        public float AiConfidenceThreshold = 0.6f;

        readonly Stack<DetectedObject> pool = new Stack<DetectedObject>();

        /// <summary>Returns objects to the pool and clears the list.</summary>
        public void Release(List<DetectedObject> list) { foreach (var o in list) pool.Push(o); list.Clear(); }

        /// <summary>Fills <paramref name="output"/> from the result. <paramref name="manual"/> may be null (e.g. photo mode).</summary>
        public void Resolve(PerceptionResult result, CocoToSemanticMap map, ManualSemanticBuffer manual, SemanticImportanceConfig importance, List<DetectedObject> output)
        {
            Release(output);
            int ncat = SemanticImportanceConfig.CategoryCount;
            Span<int> hist = stackalloc int[ncat + 1];
            foreach (var d in result.Detections)
            {
                var o = pool.Count > 0 ? pool.Pop() : new DetectedObject();
                o.Detection = d; o.HasManualCategory = false; o.ManualShare = 0; o.Disagreement = false;
                o.HasAiCategory = map != null && map.TryGetCategory(d.CocoClassId, out o.AiCategory);

                if (manual != null && manual.HasData && result.InstanceIds != null)
                {
                    hist.Clear();
                    int S = result.IdSize, tagged = 0;
                    var ids = result.InstanceIds;
                    int x0 = Math.Max(0, (int)(d.Box.xMin * S)), x1 = Math.Min(S - 1, (int)(d.Box.xMax * S));
                    int y0 = Math.Max(0, (int)(d.Box.yMin * S)), y1 = Math.Min(S - 1, (int)(d.Box.yMax * S));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                            if (ids[y * S + x] == d.FrameIndex)
                            {
                                int v = manual.ValueAt((x + 0.5f) / S, (y + 0.5f) / S);
                                if (v > 0 && v <= ncat) { hist[v]++; tagged++; }
                            }
                    if (tagged > 0)
                    {
                        int best = 1;
                        for (int c = 2; c <= ncat; c++) if (hist[c] > hist[best]) best = c;
                        o.ManualCategory = (SemanticCategory)(best - 1); o.HasManualCategory = true; o.ManualShare = hist[best] / (float)tagged;
                    }
                }

                o.Disagreement = o.HasAiCategory && o.HasManualCategory && o.AiCategory != o.ManualCategory;
                Choose(o, Policy, AiConfidenceThreshold);
                o.Importance = importance == null ? 0f : (o.Source == CategorySource.None ? importance.UnmappedWeight : importance.GetWeight(o.FinalCategory));
                output.Add(o);
            }
        }

        /// <summary>Applies the conflict policy to one object (sets FinalCategory and Source). Public static for testing.</summary>
        public static void Choose(DetectedObject o, ConflictPolicy policy, float aiThreshold)
        {
            if (o.HasAiCategory && !o.HasManualCategory) { o.FinalCategory = o.AiCategory; o.Source = CategorySource.AI; return; }
            if (!o.HasAiCategory && o.HasManualCategory) { o.FinalCategory = o.ManualCategory; o.Source = CategorySource.ManualTag; return; }
            if (!o.HasAiCategory) { o.Source = CategorySource.None; return; }
            bool useAi;
            if (!o.Disagreement) useAi = true;
            else if (policy == ConflictPolicy.PreferAI) useAi = true;
            else if (policy == ConflictPolicy.PreferManual) useAi = false;
            else useAi = o.Detection.Confidence >= aiThreshold;
            o.FinalCategory = useAi ? o.AiCategory : o.ManualCategory;
            o.Source = useAi ? CategorySource.AI : CategorySource.ManualTag;
        }
    }
}
