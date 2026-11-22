using System;
using System.Collections.Generic;
using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>An unordered pair of tags, a future "graph edge". Always <c>A.Id &lt; B.Id</c>.</summary>
    public readonly struct TagPair
    {
        public readonly SemanticTag A, B;
        public TagPair(SemanticTag a, SemanticTag b) { A = a; B = b; }
    }

    /// <summary>
    /// Static registry of every enabled <see cref="SemanticTag"/>. Hands out integer IDs (the tag's serialized
    /// stable ID when set and free, otherwise the next free number), enumerates object pairs, and raises events.
    /// Single-threaded (main thread) by design.
    /// </summary>
    public static class SemanticRegistry
    {
        static readonly List<SemanticTag> TagList = new List<SemanticTag>();
        static readonly Dictionary<int, SemanticTag> ById = new Dictionary<int, SemanticTag>();
        static int _nextAuto = 1;

        /// <summary>Bumped on any structural change (register/unregister/class change) or state change.</summary>
        public static int Version { get; private set; }
        /// <summary>Raised after a tag's state changed: (tag, oldIndex, newIndex).</summary>
        public static event Action<SemanticTag, int, int> StateChanged;
        public static event Action<SemanticTag> TagRegistered;
        public static event Action<SemanticTag> TagUnregistered;

        public static IReadOnlyList<SemanticTag> Tags => TagList;
        public static int Count => TagList.Count;
        public static int PairCount => TagList.Count * (TagList.Count - 1) / 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Clear();

        /// <summary>Drop everything (play-mode entry with domain reload disabled, tests).</summary>
        public static void Clear()
        {
            TagList.Clear(); ById.Clear(); _nextAuto = 1; Version++;
        }

        public static bool TryGet(int id, out SemanticTag tag) => ById.TryGetValue(id, out tag);

        internal static void Register(SemanticTag tag)
        {
            if (tag == null || TagList.Contains(tag)) return;
            int id = tag.StableId;
            if (id <= 0 || ById.ContainsKey(id))
            {
                while (ById.ContainsKey(_nextAuto)) _nextAuto++;
                id = _nextAuto++;
            }
            tag.Id = id;
            ById[id] = tag;
            TagList.Add(tag);
            Version++;
            TagRegistered?.Invoke(tag);
        }

        internal static void Unregister(SemanticTag tag)
        {
            if (tag == null || !TagList.Remove(tag)) return;
            if (ById.TryGetValue(tag.Id, out var cur) && cur == tag) ById.Remove(tag.Id);
            tag.Id = -1;
            Version++;
            TagUnregistered?.Invoke(tag);
        }

        internal static void NotifyChanged(SemanticTag tag) { if (tag != null && TagList.Contains(tag)) Version++; }

        internal static void NotifyStateChanged(SemanticTag tag, int oldIndex, int newIndex)
        {
            if (!TagList.Contains(tag)) return; // disabled tags change state silently (still applied to the renderer)
            Version++;
            StateChanged?.Invoke(tag, oldIndex, newIndex);
        }

        /// <summary>All unordered pairs (i &lt; j in registration order, reported with the lower Id first). O(n^2), lazy.</summary>
        public static IEnumerable<TagPair> EnumeratePairs()
        {
            for (int i = 0; i < TagList.Count; i++)
                for (int j = i + 1; j < TagList.Count; j++)
                {
                    var a = TagList[i]; var b = TagList[j];
                    yield return a.Id <= b.Id ? new TagPair(a, b) : new TagPair(b, a);
                }
        }

        /// <summary>Allocation-free pair iteration; <paramref name="visit"/> gets each pair once.</summary>
        public static void ForEachPair(Action<SemanticTag, SemanticTag> visit)
        {
            for (int i = 0; i < TagList.Count; i++)
                for (int j = i + 1; j < TagList.Count; j++)
                {
                    var a = TagList[i]; var b = TagList[j];
                    if (a.Id <= b.Id) visit(a, b); else visit(b, a);
                }
        }
    }
}
