using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SemanticVR.Core;
using UnityEngine;

namespace SemanticVR.Tests
{
    public class SemanticRegistryTests
    {
        SemanticFixture fx;
        [SetUp] public void SetUp() => fx = new SemanticFixture();
        [TearDown] public void TearDown() => fx.Cleanup();

        // Far away from any scene content so ambient tags cannot interfere.
        static readonly Vector3 Far = new Vector3(5000, 0, 5000);

        [Test]
        public void Enumeration_ListsEveryTag_AndEveryUnorderedPairOnce()
        {
            var cls = fx.Signal();
            int before = SemanticRegistry.Count;
            var mine = new[]
            {
                fx.MakeTag(cls, "Red", Far), fx.MakeTag(cls, "Green", Far + Vector3.right),
                fx.MakeTag(cls, "Amber", Far + Vector3.right * 2), fx.MakeTag(cls, "Red", Far + Vector3.right * 3),
            };
            Assert.AreEqual(before + 4, SemanticRegistry.Count);
            foreach (var t in mine) CollectionAssert.Contains(SemanticRegistry.Tags, t);

            var pairs = SemanticRegistry.EnumeratePairs().ToList();
            Assert.AreEqual(SemanticRegistry.PairCount, pairs.Count);
            var set = new HashSet<(int, int)>(mine.Length * mine.Length);
            foreach (var p in pairs)
            {
                Assert.Less(p.A.Id, p.B.Id, "pairs are reported lower Id first");
                Assert.IsTrue(set.Add((p.A.Id, p.B.Id)), "each unordered pair exactly once");
            }
            for (int i = 0; i < mine.Length; i++)
                for (int j = i + 1; j < mine.Length; j++)
                    Assert.IsTrue(set.Contains((Mathf.Min(mine[i].Id, mine[j].Id), Mathf.Max(mine[i].Id, mine[j].Id))), $"pair {i},{j}");

            int visited = 0; SemanticRegistry.ForEachPair((a, b) => visited++);
            Assert.AreEqual(pairs.Count, visited);
        }

        [Test]
        public void Ids_AreUnique_HonourStableId_AndAreReleasedOnDisable()
        {
            var cls = fx.Signal();
            var a = fx.MakeTag(cls, "Red", Far, stableId: 987654);
            Assert.AreEqual(987654, a.Id);
            Assert.IsTrue(SemanticRegistry.TryGet(987654, out var found)); Assert.AreSame(a, found);

            var dup = fx.MakeTag(cls, "Red", Far, stableId: 987654);   // collision -> falls back to an auto ID
            Assert.AreNotEqual(a.Id, dup.Id);
            Assert.Greater(dup.Id, 0);

            a.gameObject.SetActive(false);
            Assert.AreEqual(-1, a.Id);
            Assert.IsFalse(SemanticRegistry.TryGet(987654, out _));
            CollectionAssert.DoesNotContain(SemanticRegistry.Tags, a);
            a.gameObject.SetActive(true);
            Assert.AreEqual(987654, a.Id, "stable ID is reclaimed on re-enable");
        }

        [Test]
        public void StateChange_RaisesEvent_OnlyWhenStateActuallyChanges()
        {
            var cls = fx.Signal();
            var tag = fx.MakeTag(cls, "Red", Far);
            var log = new List<(SemanticTag t, int o, int n)>();
            void H(SemanticTag t, int o, int n) { if (t == tag) log.Add((t, o, n)); }
            SemanticRegistry.StateChanged += H;
            try
            {
                int v0 = SemanticRegistry.Version;
                Assert.IsTrue(tag.SetState("Green"));
                Assert.AreEqual(1, log.Count);
                Assert.AreEqual((tag, 0, 2), log[0]);
                Assert.AreEqual("Green", tag.StateName);
                Assert.Greater(SemanticRegistry.Version, v0);

                Assert.IsFalse(tag.SetState("Green"), "same state: no change");
                Assert.IsFalse(tag.SetState("Purple"), "unknown state: no change");
                Assert.IsFalse(tag.SetState(2), "same index: no change");
                Assert.AreEqual(1, log.Count);

                Assert.IsTrue(tag.SetState(1));
                Assert.AreEqual((tag, 2, 1), log[1]);
            }
            finally { SemanticRegistry.StateChanged -= H; }
        }

        [Test]
        public void DisabledTag_ChangesStateSilently()
        {
            var cls = fx.Signal();
            var tag = fx.MakeTag(cls, "Red", Far);
            tag.gameObject.SetActive(false);
            int n = 0; void H(SemanticTag t, int o, int nn) { if (t == tag) n++; }
            SemanticRegistry.StateChanged += H;
            try { Assert.IsTrue(tag.SetState("Green")); Assert.AreEqual(0, n); }
            finally { SemanticRegistry.StateChanged -= H; }
        }

        [Test]
        public void StateChange_PushesAlbedoToRendererPropertyBlock()
        {
            var cls = fx.Signal();
            var tag = fx.MakeTag(cls, "Red", Far);
            var mpb = new MaterialPropertyBlock();
            tag.TargetRenderer.GetPropertyBlock(mpb);
            AssertColor(SemanticFixture.Hex("#E5322D"), mpb.GetColor("_BaseColor"));
            tag.SetState("Green");
            tag.TargetRenderer.GetPropertyBlock(mpb);
            AssertColor(SemanticFixture.Hex("#3DBB5C"), mpb.GetColor("_BaseColor"));
        }

        static void AssertColor(Color e, Color a)
        {
            Assert.AreEqual(e.r, a.r, 1e-3f); Assert.AreEqual(e.g, a.g, 1e-3f); Assert.AreEqual(e.b, a.b, 1e-3f);
        }

        [Test]
        public void StateMachine_IsDeterministic_AndDrivesTargetsWithPhaseOffsets()
        {
            var cls = fx.Signal();
            var ns = fx.MakeTag(cls, "Red", Far);
            var ew = fx.MakeTag(cls, "Red", Far + Vector3.right);
            var go = new GameObject("fsm"); fx.Created.Add(go);
            var fsm = go.AddComponent<SemanticStateMachine>();
            fsm.autoRun = false;
            fsm.AddStep("Green", 6).AddStep("Amber", 2).AddStep("Red", 8);
            fsm.AddTarget(ns, 0f).AddTarget(ew, 8f);

            Assert.AreEqual(16f, fsm.CycleLength, 1e-5f);
            Assert.AreEqual("Green", fsm.StateAt(0f));
            Assert.AreEqual("Green", fsm.StateAt(5.99f));
            Assert.AreEqual("Amber", fsm.StateAt(6f));
            Assert.AreEqual("Red", fsm.StateAt(8f));
            Assert.AreEqual("Green", fsm.StateAt(16f), "wraps");
            Assert.AreEqual("Red", fsm.StateAt(-0.5f), "negative time wraps too");

            var changes = new List<string>();
            void H(SemanticTag t, int o, int n) { if (t == ns) changes.Add(t.StateName); }
            SemanticRegistry.StateChanged += H;
            try
            {
                fsm.SetTime(0f);
                Assert.AreEqual("Green", ns.StateName); Assert.AreEqual("Red", ew.StateName);
                fsm.Advance(6.5f);
                Assert.AreEqual("Amber", ns.StateName); Assert.AreEqual("Red", ew.StateName);   // EW still red at 6.5 + 8 = 14.5 -> Red
                fsm.Advance(2f);
                Assert.AreEqual("Red", ns.StateName); Assert.AreEqual("Green", ew.StateName);   // at 8.5: NS red, EW at 16.5 -> Green
                CollectionAssert.AreEqual(new[] { "Green", "Amber", "Red" }, changes.Distinct().ToArray());
            }
            finally { SemanticRegistry.StateChanged -= H; }

            // exactly one axis is green at any moment (no conflicting greens)
            for (float t = 0; t < 32f; t += 0.25f)
            {
                bool g1 = fsm.StateAt(t) == "Green", g2 = fsm.StateAt(t + 8f) == "Green";
                Assert.IsFalse(g1 && g2, $"both green at t={t}");
            }
        }
    }
}
