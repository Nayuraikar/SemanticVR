using System.Collections.Generic;
using SemanticVR.Core;
using UnityEditor;
using UnityEngine;

namespace SemanticVR.Editor
{
    /// <summary>Defines (and creates as assets) the default semantic classes shared by the sample scenes.
    /// Assets live inside the package (Runtime/Classes) so every sample references the same GUIDs.</summary>
    public static class DefaultClassLibrary
    {
        public const string Dir = "Packages/com.nayuraikar.semanticvr/Runtime/Classes";

        // Stable class IDs. Never renumber: scenes and (later) trained solvers index by these.
        public const int TrafficSignal = 1, HazardLight = 2, RoadSign = 3, Vehicle = 4, Grass = 5, Sky = 6,
                         Foliage = 7, Berry = 8, Fruit = 9, RailSignal = 10, StatusPanel = 11, Skin = 12;

        // Phase 1b: cockpit (13-18) and medical (19-26). Palettes chosen/checked with python/scripts/phase1b_palette.py (see docs/design/phase1b.md).
        public const int Warning = 13, Caution = 14, Advisory = 15, NormalStatus = 16, DisplayText = 17, RunwayLight = 18,
                         AlarmHigh = 19, AlarmMedium = 20, AlarmLow = 21, VitalTrace = 22, GasCylinder = 23, DrugLabel = 24, StatusLight = 25, Tissue = 26;

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        static SemanticState S(string n, string hex, float emission = 0f) => SemanticClass.State(n, C(hex), emission);

        static void Fill(SemanticClass c, int id)
        {
            switch (id)
            {
                case TrafficSignal: c.Configure(id, "Traffic Signal", 0.95f, 4f, false, S("Red", "#E5322D", 2.5f), S("Amber", "#F2A516", 2.5f), S("Green", "#3DBB5C", 2.5f), S("Off", "#2A2C2F")); break;
                case HazardLight: c.Configure(id, "Hazard Light", 0.90f, 5f, false, S("Off", "#3A2E22"), S("Amber", "#FFB020", 3f), S("Red", "#E5322D", 3f)); break;
                case RoadSign: c.Configure(id, "Road Sign", 0.70f, 6f, false, S("Prohibit", "#C62828"), S("Warning", "#F2C230"), S("Mandatory", "#1E5AA8"), S("Info", "#2E7D4F")); break;
                case Vehicle: c.Configure(id, "Vehicle", 0.35f, 14f, false, S("Red", "#B3282D"), S("Green", "#3F7F45"), S("Blue", "#2F5FA8"), S("Silver", "#B8BDC2"), S("Yellow", "#E3B72B")); break;
                case Grass: c.Configure(id, "Grass", 0.10f, 3f, true, S("Green", "#5A8F3C"), S("Dry", "#A59A55")); break;
                case Sky: c.Configure(id, "Sky", 0.05f, 2f, true, S("Day", "#8FB4E0")); break;
                case Foliage: c.Configure(id, "Foliage", 0.15f, 3f, true, S("Leaf", "#4F7F34"), S("Dark", "#3A6330")); break;
                case Berry: c.Configure(id, "Berry", 0.80f, 10f, false, S("Unripe", "#8FB04A"), S("Ripe", "#C8312B")); break;
                case Fruit: c.Configure(id, "Fruit", 0.80f, 10f, false, S("Unripe", "#9BBF3A"), S("Ripening", "#E0A32A"), S("Ripe", "#B52A2A")); break;
                case RailSignal: c.Configure(id, "Rail Signal", 0.95f, 4f, false, S("Red", "#E0302A", 2.5f), S("Yellow", "#F2C230", 2.5f), S("Green", "#46B04A", 2.5f), S("Off", "#2A2C2F")); break;
                case StatusPanel: c.Configure(id, "Status Panel", 0.85f, 6f, false, S("OnTime", "#2FA25A", 1.2f), S("Delayed", "#E8A91E", 1.2f), S("Cancelled", "#D23B2E", 1.2f)); break;
                case Skin: c.Configure(id, "Skin", 0.30f, 2f, true, S("Light", "#E0B49A")); break;
                // Cockpit: Warning is the highest priority with the tightest naturalness bound (its hue is the message).
                case Warning: c.Configure(id, "Warning", 1.00f, 2f, false, S("Off", "#3A1B1A"), S("Red", "#E5322D", 3f)); break;
                case Caution: c.Configure(id, "Caution", 0.85f, 4f, false, S("Off", "#3A2D15"), S("Amber", "#F2A516", 3f)); break;
                case Advisory: c.Configure(id, "Advisory", 0.50f, 8f, false, S("Off", "#26292E"), S("White", "#F1EFE9", 2f), S("Blue", "#2F7FD8", 2.5f)); break;
                case NormalStatus: c.Configure(id, "Normal Status", 0.35f, 10f, false, S("Off", "#1F2A22"), S("Green", "#3DBB5C", 2f)); break;
                case DisplayText: c.Configure(id, "Display Text", 0.75f, 6f, false, S("Green", "#3DDC6A", 1.5f), S("Magenta", "#E040C8", 1.5f), S("Cyan", "#38D0E0", 1.5f),
                                              S("Amber", "#F2A516", 1.5f), S("Red", "#E5322D", 1.5f), S("White", "#F1EFE9", 1.5f)); break;
                case RunwayLight: c.Configure(id, "Runway Light", 0.80f, 5f, false, S("Red", "#E5322D", 3f), S("White", "#F1EFE9", 3f), S("Off", "#2A2C2F")); break;
                // Medical: alarm priorities follow the red / yellow / cyan idea of IEC 60601-1-8 (inspired by, not a copy).
                case AlarmHigh: c.Configure(id, "Alarm High", 1.00f, 2f, false, S("Off", "#3A1B1A"), S("Red", "#E5322D", 2.5f)); break;
                case AlarmMedium: c.Configure(id, "Alarm Medium", 0.85f, 4f, false, S("Off", "#3A3515"), S("Yellow", "#F2D21E", 2.5f)); break;
                case AlarmLow: c.Configure(id, "Alarm Low", 0.55f, 8f, false, S("Off", "#15353A"), S("Cyan", "#38C8D8", 2.5f)); break;
                case VitalTrace: c.Configure(id, "Vital Trace", 0.70f, 6f, false, S("Ecg", "#3DDC6A", 1.5f), S("Spo2", "#38C8D8", 1.5f), S("Bp", "#E5322D", 1.5f), S("Temp", "#F2D21E", 1.5f)); break;
                case GasCylinder: c.Configure(id, "Gas Cylinder", 0.90f, 4f, false, S("Oxygen", "#2E9E4F"), S("Helium", "#8A5A32"), S("Air", "#E8C21E"), S("NitrousOxide", "#2F5FA8"), S("CO2", "#8C9096"), S("Vacuum", "#F1EFE9")); break;
                case DrugLabel: c.Configure(id, "Drug Label", 0.90f, 4f, false, S("Yellow", "#E8C21E"), S("Blue", "#2F5FA8"), S("Red", "#C62828"), S("Orange", "#E8761E"), S("Violet", "#7B4BA8"), S("Green", "#2E9E4F")); break;
                case StatusLight: c.Configure(id, "Status Light", 0.90f, 4f, false, S("Off", "#2A2C2F"), S("Green", "#3DBB5C", 2.5f), S("Amber", "#F2A516", 2.5f), S("Red", "#E5322D", 2.5f)); break;
                case Tissue: c.Configure(id, "Tissue Tint", 0.85f, 5f, false, S("Pale", "#E0B49A"), S("Cyanosis", "#9C9FB8"), S("Wound", "#B84A4A")); break;
            }
        }

        static readonly (int id, string file)[] All =
        {
            (TrafficSignal, "TrafficSignal"), (HazardLight, "HazardLight"), (RoadSign, "RoadSign"), (Vehicle, "Vehicle"),
            (Grass, "Grass"), (Sky, "Sky"), (Foliage, "Foliage"), (Berry, "Berry"), (Fruit, "Fruit"),
            (RailSignal, "RailSignal"), (StatusPanel, "StatusPanel"), (Skin, "Skin"),
            (Warning, "Warning"), (Caution, "Caution"), (Advisory, "Advisory"), (NormalStatus, "NormalStatus"), (DisplayText, "DisplayText"), (RunwayLight, "RunwayLight"),
            (AlarmHigh, "AlarmHigh"), (AlarmMedium, "AlarmMedium"), (AlarmLow, "AlarmLow"), (VitalTrace, "VitalTrace"), (GasCylinder, "GasCylinder"),
            (DrugLabel, "DrugLabel"), (StatusLight, "StatusLight"), (Tissue, "Tissue"),
        };

        static string PathOf(string file) => $"{Dir}/{file}.asset";

        /// <summary>Create missing class assets (never overwrites existing ones, so hand edits survive).</summary>
        public static void EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Packages/com.nayuraikar.semanticvr/Runtime", "Classes"); // works for local (file:) packages
            bool created = false;
            foreach (var (id, file) in All)
            {
                if (AssetDatabase.LoadAssetAtPath<SemanticClass>(PathOf(file)) != null) continue;
                var c = ScriptableObject.CreateInstance<SemanticClass>();
                Fill(c, id);
                AssetDatabase.CreateAsset(c, PathOf(file));
                created = true;
            }
            if (created) AssetDatabase.SaveAssets();
        }

        /// <summary>Load a default class by its ID constant (creating the assets first if necessary).</summary>
        public static SemanticClass Load(int id)
        {
            EnsureAssets();
            foreach (var (cid, file) in All)
                if (cid == id) return AssetDatabase.LoadAssetAtPath<SemanticClass>(PathOf(file));
            return null;
        }

        /// <summary>Every SemanticClass asset in the project (defaults included), ordered by ID.</summary>
        public static List<SemanticClass> LoadAllProjectClasses()
        {
            EnsureAssets();
            var list = new List<SemanticClass>();
            foreach (var g in AssetDatabase.FindAssets("t:SemanticClass"))
            {
                var c = AssetDatabase.LoadAssetAtPath<SemanticClass>(AssetDatabase.GUIDToAssetPath(g));
                if (c != null) list.Add(c);
            }
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return list;
        }

        [MenuItem("SemanticVR/Create Default Classes")]
        static void Menu() { EnsureAssets(); Debug.Log("[SemanticVR] Default semantic classes are in " + Dir); }
    }
}
