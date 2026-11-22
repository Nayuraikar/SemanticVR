using System.Linq;
using NUnit.Framework;
using SemanticVR.Core;
using SemanticVR.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SemanticVR.Tests
{
    public class SceneValidatorTests
    {
        SemanticFixture fx;
        [SetUp] public void SetUp() => fx = new SemanticFixture();
        [TearDown] public void TearDown() => fx.Cleanup();

        static Material Lit() => new Material(Shader.Find("Universal Render Pipeline/Lit"));

        /// <summary>A scene that is broken in every way the validator knows about.</summary>
        Scene BuildBrokenScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var a = fx.MakeClass(7, "ClassA", ("On", "#FF0000"));
            var b = fx.MakeClass(7, "ClassB", ("On", "#00FF00"));                    // duplicate class ID 7
            var empty = fx.MakeClass(8, "Hollow");                                    // no states
            var lamp = fx.MakeClass(9, "Lamp", ("On", "#FFAA00"), ("Off", "#222222"));
            lamp.states[0].emissionIntensity = 2f;                                   // emissive, but material has no _EMISSION

            var mat = Lit(); fx.Created.Add(mat);
            GameObject Cube(string name)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.GetComponent<Renderer>().sharedMaterial = mat;
                fx.Created.Add(go); return go;
            }

            Cube("Untagged");                                                         // UNTAGGED_RENDERER
            var ign = Cube("IgnoredDecor"); ign.AddComponent<SemanticIgnore>();       // fine
            var noMat = Cube("NoMaterial"); noMat.GetComponent<Renderer>().sharedMaterial = null; noMat.AddComponent<SemanticIgnore>(); // MISSING_MATERIAL
            var holes = Cube("MaterialSlotHole"); holes.GetComponent<Renderer>().sharedMaterials = new Material[] { mat, null }; holes.AddComponent<SemanticIgnore>();

            var t1 = Cube("TagA"); var tag1 = t1.AddComponent<SemanticTag>(); tag1.StableId = 5; tag1.Configure(a, "On");
            var t2 = Cube("TagB"); var tag2 = t2.AddComponent<SemanticTag>(); tag2.StableId = 5; tag2.Configure(b, "On");           // DUPLICATE_TAG_ID
            var t3 = Cube("TagHollow"); var tag3 = t3.AddComponent<SemanticTag>(); tag3.Configure(empty, 0);                        // CLASS_NO_STATES, NO_STABLE_ID
            var t4 = Cube("TagNoClass"); t4.AddComponent<SemanticTag>();                                                            // TAG_NO_CLASS
            var t5 = Cube("TagLamp"); var tag5 = t5.AddComponent<SemanticTag>(); tag5.StableId = 6; tag5.Configure(lamp, "On");     // EMISSION_KEYWORD_OFF
            var t6 = new GameObject("TagNoRenderer"); fx.Created.Add(t6); { var tag6 = t6.AddComponent<SemanticTag>(); tag6.TargetRenderer = null; tag6.Configure(a, "On"); }   // TAG_NO_RENDERER

            var f = new GameObject("BadFsm"); fx.Created.Add(f);
            var fsm = f.AddComponent<SemanticStateMachine>();
            fsm.AddStep("On", 1).AddStep("Blinking", 1).AddTarget(tag5);                                                             // FSM_UNKNOWN_STATE
            var e = new GameObject("EmptyFsm"); fx.Created.Add(e); e.AddComponent<SemanticStateMachine>();                           // FSM_EMPTY
            return scene;
        }

        [Test]
        public void BrokenScene_ReportsEveryProblem()
        {
            var scene = BuildBrokenScene();
            var issues = SceneValidator.Validate(scene);
            string Dump() => string.Join("\n", issues.Select(i => i.ToString()));
            int Count(string code) => issues.Count(i => i.code == code);

            Assert.AreEqual(1, Count(SceneValidator.Untagged), "only 'Untagged' (ignored/tagged renderers are fine)\n" + Dump());
            Assert.IsTrue(issues.Any(i => i.code == SceneValidator.Untagged && i.message.Contains("Untagged")));
            Assert.AreEqual(2, Count(SceneValidator.MissingMaterial), "null material + null slot\n" + Dump());
            Assert.AreEqual(1, Count(SceneValidator.DupClassId), Dump());
            Assert.IsTrue(issues.Any(i => i.code == SceneValidator.DupClassId && i.message.Contains("7")));
            Assert.AreEqual(1, Count(SceneValidator.DupTagId), Dump());
            Assert.AreEqual(1, Count(SceneValidator.ClassNoStates), Dump());
            Assert.AreEqual(1, Count(SceneValidator.TagNoClass), Dump());
            Assert.AreEqual(1, Count(SceneValidator.TagNoRenderer), Dump());
            Assert.AreEqual(1, Count(SceneValidator.NoEmission), Dump());
            Assert.AreEqual(1, Count(SceneValidator.FsmBadState), Dump());
            Assert.AreEqual(1, Count(SceneValidator.FsmEmpty), Dump());
            Assert.Greater(Count(SceneValidator.NoStableId), 0);
            Assert.IsTrue(issues.Where(i => i.code == SceneValidator.Untagged).All(i => i.severity == IssueSeverity.Warning));
            Assert.IsTrue(issues.Where(i => i.code == SceneValidator.DupClassId || i.code == SceneValidator.MissingMaterial).All(i => i.severity == IssueSeverity.Error));
        }

        [Test]
        public void CleanScene_HasNoErrorsOrWarnings()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cls = fx.Signal();
            var mat = Lit(); fx.Created.Add(mat);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); fx.Created.Add(go);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var tag = go.AddComponent<SemanticTag>(); tag.StableId = 1; tag.Configure(cls, "Red");
            var deco = GameObject.CreatePrimitive(PrimitiveType.Sphere); fx.Created.Add(deco);
            deco.GetComponent<Renderer>().sharedMaterial = mat; deco.AddComponent<SemanticIgnore>();
            var issues = SceneValidator.Validate(scene);
            Assert.IsEmpty(issues.Where(i => i.severity != IssueSeverity.Info).Select(i => i.ToString()).ToArray());
        }

        static readonly string[] Samples = { "01_Street", "02_Signals", "03_Forest" };

        [Test]
        public void SampleScenes_AreValid([ValueSource(nameof(Samples))] string name)
        {
            string path = $"{SceneKit.HostRoot}/{name}/{name}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) Assert.Ignore($"{path} missing: run SemanticVR > Build Sample Scenes.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var issues = SceneValidator.Validate(scene, includeProjectClasses: true);
            var bad = issues.Where(i => i.severity != IssueSeverity.Info).ToList();
            Assert.IsEmpty(bad.Select(i => i.ToString()).ToArray(), name + " must validate clean");
        }
    }
}
