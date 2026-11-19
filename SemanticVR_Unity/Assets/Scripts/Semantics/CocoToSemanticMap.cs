using System;
using System.Collections.Generic;
using SemanticVR.Environment;
using SemanticVR.Perception;
using UnityEngine;

namespace SemanticVR.Semantics
{
    /// <summary>
    /// Editable mapping from COCO-91 class ids to the project's <see cref="SemanticCategory"/>. COCO classes without
    /// an entry are intentionally unmapped (they are never forced into an unrelated category).
    /// Entries store the expected class name and are verified against the real class-name table.
    /// </summary>
    [CreateAssetMenu(menuName = "SemanticVR/COCO To Semantic Map", fileName = "CocoToSemanticMap")]
    public sealed class CocoToSemanticMap : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public int cocoId;
            public string expectedName;
            public SemanticCategory category;
        }

        [SerializeField] List<Entry> entries = DefaultEntries();
        Dictionary<int, SemanticCategory> lookup;

        /// <summary>Default mapping for the eight relevant COCO classes.</summary>
        public static List<Entry> DefaultEntries() => new List<Entry>
        {
            new Entry { cocoId = 1,  expectedName = "person",        category = SemanticCategory.Pedestrian },
            new Entry { cocoId = 2,  expectedName = "bicycle",       category = SemanticCategory.Vehicle },
            new Entry { cocoId = 3,  expectedName = "car",           category = SemanticCategory.Vehicle },
            new Entry { cocoId = 4,  expectedName = "motorcycle",    category = SemanticCategory.Vehicle },
            new Entry { cocoId = 6,  expectedName = "bus",           category = SemanticCategory.Vehicle },
            new Entry { cocoId = 8,  expectedName = "truck",         category = SemanticCategory.Vehicle },
            new Entry { cocoId = 10, expectedName = "traffic light", category = SemanticCategory.TrafficLight },
            new Entry { cocoId = 13, expectedName = "stop sign",     category = SemanticCategory.TrafficSign },
        };

        void OnEnable() { lookup = null; }
        void OnValidate() { lookup = null; }

        /// <summary>True and the category if the COCO class is mapped.</summary>
        public bool TryGetCategory(int cocoId, out SemanticCategory category)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<int, SemanticCategory>();
                foreach (var e in entries) lookup[e.cocoId] = e.category;
            }
            return lookup.TryGetValue(cocoId, out category);
        }

        /// <summary>Checks every entry's expected name against <see cref="CocoClasses"/>. Returns null if all match.</summary>
        public string Validate()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in entries)
                if (CocoClasses.NameOf(e.cocoId) != e.expectedName)
                    sb.AppendLine($"COCO id {e.cocoId}: expected '{e.expectedName}' but class table says '{CocoClasses.NameOf(e.cocoId)}'");
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>
        /// COCO classes that the pretrained model cannot recognise but the project needs (not mappable):
        /// road, road markings/crosswalks, sidewalk, traffic signs other than stop, building, vegetation, ground.
        /// These stay on the manual SemanticTag path until a custom dataset exists.
        /// </summary>
        public static readonly SemanticCategory[] UnsupportedByPretrainedModel =
        {
            SemanticCategory.Ground, SemanticCategory.Road, SemanticCategory.RoadMarking, SemanticCategory.Sidewalk,
            SemanticCategory.Building, SemanticCategory.Vegetation
        };
    }
}
