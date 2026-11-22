using System;
using UnityEditor;
using UnityEngine;

namespace SemanticVR.Tests
{
    [Serializable] public class GoldenCase
    {
        public float[] rgb_lin; public string cvd_type; public float severity;
        public float[] simulated_rgb, lab_original, lab_simulated; public float de00_original_vs_simulated;
        public string name;
    }
    [Serializable] public class GoldenSharma
    {
        public float[] lab1, lab2; public float de00_expected_published, de00_reference;
    }
    [Serializable] public class GoldenMatrix
    {
        public string cvd_type; public float severity; public float[] matrix_row_major;
    }
    [Serializable] public class Golden
    {
        public GoldenCase[] random_cases, edge_cases; public GoldenSharma[] sharma_pairs; public GoldenMatrix[] machado_matrices;
    }

    static class GoldenLoader
    {
        public const string Dir = "Packages/com.nayuraikar.semanticvr/Tests/Runtime/";
        static Golden _cached;

        public static Golden Load()
        {
            if (_cached != null) return _cached;
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(Dir + "golden.json");
            if (ta == null) throw new InvalidOperationException("golden.json not found/imported at " + Dir);
            _cached = JsonUtility.FromJson<Golden>(ta.text);
            return _cached;
        }

        public static GoldenCase[] AllCvd(Golden g)
        {
            var all = new GoldenCase[g.random_cases.Length + g.edge_cases.Length];
            g.random_cases.CopyTo(all, 0);
            g.edge_cases.CopyTo(all, g.random_cases.Length);
            return all;
        }

        public static int CvdIndex(string s) => s == "protan" ? 0 : s == "deutan" ? 1 : s == "tritan" ? 2 : throw new ArgumentException(s);
    }

    /// <summary>Tracks max abs error per category and prints a report.</summary>
    class ErrorReport
    {
        readonly System.Collections.Generic.Dictionary<string, float> _max = new();
        readonly System.Collections.Generic.Dictionary<string, float> _tol = new();
        readonly System.Collections.Generic.Dictionary<string, string> _worst = new();
        public void Add(string cat, float err, float tol, string where = null)
        {
            _tol[cat] = tol;
            if (!_max.TryGetValue(cat, out var m) || err > m || float.IsNaN(err)) { _max[cat] = err; _worst[cat] = where; }
        }
        public void PrintAndAssert(string suite)
        {
            var sb = new System.Text.StringBuilder($"[{suite}] max abs error per category:\n");
            bool ok = true;
            foreach (var kv in _max)
            {
                bool pass = kv.Value <= _tol[kv.Key];
                ok &= pass;
                sb.AppendLine($"  {kv.Key,-28} {kv.Value:E3}  (tol {_tol[kv.Key]:E0}) {(pass ? "OK" : "FAIL")}{(_worst[kv.Key] != null ? "  worst: " + _worst[kv.Key] : "")}");
            }
            Debug.Log(sb.ToString());
            NUnit.Framework.Assert.IsTrue(ok, sb.ToString());
        }
    }
}
