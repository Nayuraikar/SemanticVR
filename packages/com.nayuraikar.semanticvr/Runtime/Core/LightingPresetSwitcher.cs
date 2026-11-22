using System;
using System.Collections.Generic;
using SemanticVR.Confusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace SemanticVR.Core
{
    /// <summary>One named lighting setup for <see cref="LightingPresetSwitcher"/>: a main directional light, a flat ambient colour
    /// (flat so the ambient SH is exactly L0 and the design maths in python/scripts/phase2a_nightcity.py is exact) and a gradient sky.</summary>
    [Serializable]
    public class LightingPreset
    {
        public string name = "Day";
        public Color sunColor = Color.white;
        public float sunIntensity = 1f;
        public Vector3 sunEuler = new Vector3(48f, -32f, 0f);
        [Tooltip("Flat ambient colour (sRGB, as in the Lighting window).")] public Color ambient = Color.gray;
        public Color skyTop = new Color(0.4f, 0.55f, 0.7f), skyHorizon = new Color(0.9f, 0.88f, 0.84f), skyBottom = new Color(0.7f, 0.69f, 0.65f);
        [Tooltip("State to set on the sky tag (if any) when this preset is applied.")] public string skyState = "";
        [Tooltip("Activate the switcher's 'nightProps' (glowing windows, lamp heads) in this preset.")] public bool nightProps;
    }

    /// <summary>Switches between lighting presets at runtime (N key). Used by 05_NightCity.</summary>
    [DisallowMultipleComponent, AddComponentMenu("SemanticVR/Lighting Preset Switcher")]
    public class LightingPresetSwitcher : MonoBehaviour
    {
        public Light sun;
        public SemanticTag skyTag;
        public List<LightingPreset> presets = new List<LightingPreset>();
        public int current;
        [Tooltip("Press N to cycle presets.")] public bool allowHotkey = true;
        public bool applyOnStart = true;
        [Tooltip("Decor shown only in presets with nightProps (lit windows, lamp glow). Never semantic.")] public List<GameObject> nightProps = new List<GameObject>();

        public event Action<LightingPreset> PresetChanged;
        public LightingPreset Current => presets.Count > 0 ? presets[Mathf.Clamp(current, 0, presets.Count - 1)] : null;

        Material _skyInstance;
        static readonly int Top = Shader.PropertyToID("_Top"), Horizon = Shader.PropertyToID("_Horizon"), Bottom = Shader.PropertyToID("_Bottom");

        void Start() { if (applyOnStart) Apply(current); }

        void OnDestroy() { if (_skyInstance != null) Destroy(_skyInstance); }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (allowHotkey && kb != null && kb.nKey.wasPressedThisFrame) Next();
        }

        public void Next() { if (presets.Count > 0) Apply((current + 1) % presets.Count); }

        /// <summary>Write a preset into the scene's lighting state (light, flat ambient, gradient sky). Also used by the scene builder to bake the default preset.</summary>
        public static void ApplyToScene(LightingPreset p, Light sun, Material skyMaterial)
        {
            if (sun != null)
            {
                sun.color = p.sunColor; sun.intensity = p.sunIntensity; sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                RenderSettings.sun = sun;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambient;
            RenderSettings.ambientIntensity = 1f;
            if (skyMaterial != null && skyMaterial.HasProperty(Top))
            { skyMaterial.SetColor(Top, p.skyTop); skyMaterial.SetColor(Horizon, p.skyHorizon); skyMaterial.SetColor(Bottom, p.skyBottom); }
            DynamicGI.UpdateEnvironment(); // refresh ambient probe + skybox reflection
        }

        public bool Apply(string presetName)
        {
            int i = presets.FindIndex(p => string.Equals(p.name, presetName, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            Apply(i); return true;
        }

        public void Apply(int index)
        {
            if (presets.Count == 0) return;
            current = Mathf.Clamp(index, 0, presets.Count - 1);
            var p = presets[current];
            if (sun == null) sun = RenderSettings.sun;
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(Top) && _skyInstance == null) { _skyInstance = new Material(sky) { name = sky.name + " (runtime)" }; RenderSettings.skybox = _skyInstance; }
            ApplyToScene(p, sun, _skyInstance != null ? _skyInstance : sky);
            foreach (var go in nightProps) if (go != null) go.SetActive(p.nightProps);
            if (skyTag != null && !string.IsNullOrEmpty(p.skyState)) skyTag.SetState(p.skyState);
            PresetChanged?.Invoke(p);
        }
    }
}
