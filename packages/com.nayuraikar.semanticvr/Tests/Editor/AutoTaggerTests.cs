using System.Linq;
using NUnit.Framework;
using SemanticVR.Core;
using SemanticVR.Editor;
using UnityEngine;

namespace SemanticVR.Tests
{
    public class AutoTaggerTests
    {
        SemanticFixture fx;
        [SetUp] public void SetUp() => fx = new SemanticFixture();
        [TearDown] public void TearDown() => fx.Cleanup();

        [TestCase("RoadSign_Stop", true)] [TestCase("trafficSignalHead", true)] [TestCase("Cube", false)]
        public void Tokenizer_And_NameRules(string name, bool expectHit)
        {
            var classes = DefaultClassLibrary.LoadAllProjectClasses();
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; fx.Created.Add(go);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); fx.Created.Add(mat);
            mat.SetColor("_BaseColor", new Color(0.3f, 0.05f, 0.6f)); // purple: matches nothing by colour
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var s = AutoTagger.SuggestFor(go.GetComponent<Renderer>(), classes);
            if (expectHit) { Assert.IsNotNull(s); Assert.GreaterOrEqual(s.confidence, 0.9f); }
            else Assert.IsNull(s);
        }

        [Test]
        public void Suggest_PicksNearestState_AndApplyCreatesTags()
        {
            var classes = DefaultClassLibrary.LoadAllProjectClasses();
            var signal = classes.First(c => c.id == DefaultClassLibrary.TrafficSignal);
            var root = new GameObject("Signals"); fx.Created.Add(root);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); fx.Created.Add(mat);
            mat.SetColor("_BaseColor", signal.GetAlbedo(signal.IndexOfState("Green")));
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); lamp.name = "TrafficLamp"; lamp.transform.SetParent(root.transform);
            lamp.GetComponent<Renderer>().sharedMaterial = mat;

            var list = AutoTagger.Suggest(new[] { root }, classes);
            Assert.AreEqual(1, list.Count);
            Assert.AreSame(signal, list[0].cls);
            Assert.AreEqual("Green", list[0].cls.states[list[0].state].name);

            Assert.AreEqual(1, AutoTagger.Apply(list));
            var tag = lamp.GetComponent<SemanticTag>();
            Assert.IsNotNull(tag); Assert.AreEqual("Green", tag.StateName); Assert.Greater(tag.StableId, 0);
            Assert.AreEqual(0, AutoTagger.Suggest(new[] { root }, classes).Count, "already tagged renderers are skipped");
        }
    }
}
