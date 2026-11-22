using System.Collections.Generic;
using SemanticVR.Core;
using UnityEngine;

namespace SemanticVR.Tests
{
    /// <summary>Builds throw-away classes/tags for tests and cleans them up.</summary>
    public sealed class SemanticFixture
    {
        public readonly List<Object> Created = new List<Object>();

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public SemanticClass MakeClass(int id, string name, params (string state, string hex)[] states)
        {
            var c = ScriptableObject.CreateInstance<SemanticClass>();
            var list = new List<SemanticState>();
            foreach (var (s, h) in states) list.Add(SemanticClass.State(s, Hex(h)));
            c.Configure(id, name, 0.5f, 8f, false, list.ToArray());
            c.name = name;
            Created.Add(c);
            return c;
        }

        public SemanticClass Signal() => MakeClass(1, "Signal", ("Red", "#E5322D"), ("Amber", "#F2A516"), ("Green", "#3DBB5C"));

        /// <summary>Cube + SemanticTag. <paramref name="stableId"/> is assigned before the tag is enabled.</summary>
        public SemanticTag MakeTag(SemanticClass cls, string state, Vector3 pos, int stableId = 0, string name = "Tagged")
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.2f;
            go.SetActive(false);
            var tag = go.AddComponent<SemanticTag>();
            tag.StableId = stableId;
            tag.TargetRenderer = go.GetComponent<Renderer>();
            tag.Configure(cls, state);
            go.SetActive(true);
            Created.Add(go);
            return tag;
        }

        public void Cleanup()
        {
            foreach (var o in Created) if (o != null) Object.DestroyImmediate(o);
            Created.Clear();
        }
    }
}
