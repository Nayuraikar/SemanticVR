using SemanticVR.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SemanticVR.Confusion
{
    /// <summary>
    /// Second-order spherical-harmonic lighting (9 RGB coefficients) in Unity's storage convention, as a blittable struct
    /// (Burst/Jobs friendly: no managed references).
    /// <para>Unity folds every SH normalisation and the cosine-lobe convolution into the stored coefficients, so a diffuse
    /// irradiance lookup (already divided by pi, i.e. "albedo * result = outgoing radiance") is just
    /// <c>sum_i c_i * B_i(n)</c> with the un-normalised basis B = {1, y, z, x, xy, yz, 3z^2-1, xz, x^2-y^2}.
    /// The ordering and signs were measured against <c>SphericalHarmonicsL2.Evaluate</c> (see LitColorPredictorTests).</para>
    /// </summary>
    public struct ShL2
    {
        public float3 C0, C1, C2, C3, C4, C5, C6, C7, C8;

        public static ShL2 Constant(float3 colour) => new ShL2 { C0 = colour };

        public static ShL2 FromUnity(SphericalHarmonicsL2 sh)
        {
            float3 R(int i) => new float3(sh[0, i], sh[1, i], sh[2, i]);
            return new ShL2 { C0 = R(0), C1 = R(1), C2 = R(2), C3 = R(3), C4 = R(4), C5 = R(5), C6 = R(6), C7 = R(7), C8 = R(8) };
        }

        public static ShL2 Add(in ShL2 a, in ShL2 b) => new ShL2
        {
            C0 = a.C0 + b.C0, C1 = a.C1 + b.C1, C2 = a.C2 + b.C2, C3 = a.C3 + b.C3, C4 = a.C4 + b.C4,
            C5 = a.C5 + b.C5, C6 = a.C6 + b.C6, C7 = a.C7 + b.C7, C8 = a.C8 + b.C8,
        };
    }

    public static class ShMath
    {
        /// <summary>Explicit L2 basis in Unity's convention (index order of SphericalHarmonicsL2[rgb, i]). <paramref name="n"/> must be unit length.</summary>
        public static void Basis(float3 n, out float b0, out float b1, out float b2, out float b3, out float b4, out float b5, out float b6, out float b7, out float b8)
        {
            float x = n.x, y = n.y, z = n.z;
            b0 = 1f;
            b1 = y; b2 = z; b3 = x;
            b4 = x * y; b5 = y * z; b6 = 3f * z * z - 1f; b7 = x * z; b8 = x * x - y * y;
        }

        /// <summary>Diffuse irradiance (linear RGB, /pi convention: multiply by albedo to get outgoing radiance) for unit normal <paramref name="n"/>.</summary>
        public static float3 Evaluate(in ShL2 sh, float3 n)
        {
            Basis(n, out float b0, out float b1, out float b2, out float b3, out float b4, out float b5, out float b6, out float b7, out float b8);
            return sh.C0 * b0 + sh.C1 * b1 + sh.C2 * b2 + sh.C3 * b3 + sh.C4 * b4 + sh.C5 * b5 + sh.C6 * b6 + sh.C7 * b7 + sh.C8 * b8;
        }
    }

    /// <summary>The scene's realtime directional light as the predictor sees it (linear RGB with intensity folded in).</summary>
    public struct MainLight
    {
        public bool Valid;
        public float3 Color;    // linear, premultiplied by intensity
        public float3 ToLight;  // unit vector from the surface towards the light
    }

    /// <summary>Everything that is not per-object: the ambient fallback probe and the main light. Cheap to capture (a few reads).</summary>
    public struct LightingSnapshot
    {
        public ShL2 Ambient;
        public MainLight Main;
        public bool HasLightProbes;

        public static LightingSnapshot Capture() => LitColorPredictor.Capture();
    }

    /// <summary>
    /// C1: predicts the LIT colour of a semantic object (albedo x irradiance + emission) from the lighting model alone, with no
    /// framebuffer readback. Linear colour space throughout.
    /// <para><b>Model.</b> irradiance(n) = SH-L2 probe(n) + L_main * max(0, n.l);  lit = albedo * irradiance + emission;
    /// displayed = saturate(lit). The SH term comes from <c>LightProbes.GetInterpolatedProbe</c> when the scene has baked probes,
    /// else <c>RenderSettings.ambientProbe</c>. The main directional light is added analytically because realtime directional
    /// lights are not in probes.</para>
    /// <para><b>Not modelled</b> (these show up as prediction error, see docs/design/phase2a.md): realtime shadows, specular
    /// highlights and Fresnel/environment reflections, additional point/spot lights, metallic surfaces, textures,
    /// ambient occlusion, bounce light that a probe at the object centre does not see.</para>
    /// </summary>
    public static class LitColorPredictor
    {
        // ---- pure maths (Burst friendly) ----

        /// <summary>Lambert term of one directional light: colour * intensity * max(0, n.l). Same convention as URP's diffuse (no 1/pi).</summary>
        public static float3 Lambert(float3 lightColorLinear, float3 toLight, float3 n) =>
            lightColorLinear * math.max(0f, math.dot(n, toLight));

        public static float3 Irradiance(in ShL2 ambient, in MainLight main, float3 n)
        {
            float3 e = ShMath.Evaluate(ambient, n);
            if (main.Valid) e += Lambert(main.Color, main.ToLight, n);
            return e;
        }

        /// <summary>lit = albedo * irradiance + emission (unclamped, linear).</summary>
        public static float3 Lit(float3 albedoLinear, float3 irradiance, float3 emissionLinear) => albedoLinear * irradiance + emissionLinear;

        /// <summary>What the display shows: the lit colour clamped to [0,1] (no tone mapping, SDR target).</summary>
        public static float3 Displayed(float3 lit) => math.saturate(lit);

        // ---- scene queries ----

        public static LightingSnapshot Capture()
        {
            var s = new LightingSnapshot { HasLightProbes = HasProbes() };
            s.Ambient = ShL2.FromUnity(RenderSettings.ambientProbe);
            s.Main = FindMainLight();
            return s;
        }

        static bool HasProbes() => LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0;

        /// <summary>Linear colour * intensity of a light, honouring colour temperature and the project's intensity space.</summary>
        public static float3 LightColorLinear(Light l)
        {
            Color c = l.color.linear;
            if (l.useColorTemperature) { var t = Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature).linear; c.r *= t.r; c.g *= t.g; c.b *= t.b; }
            float intensity = GraphicsSettings.lightsUseLinearIntensity ? l.intensity : Mathf.GammaToLinearSpace(l.intensity);
            return new float3(c.r, c.g, c.b) * intensity;
        }

        /// <summary>RenderSettings.sun if it is an active directional light, else the brightest active directional light. Baked-only lights are skipped (they live in probes).</summary>
        public static MainLight FindMainLight()
        {
            Light best = null; float bestLum = -1f;
            var sun = RenderSettings.sun;
            if (Usable(sun)) best = sun;
            else
            {
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (!Usable(l)) continue;
                    var c = LightColorLinear(l); float lum = c.x * 0.2126f + c.y * 0.7152f + c.z * 0.0722f;
                    if (lum > bestLum) { bestLum = lum; best = l; }
                }
            }
            if (best == null) return default;
            var d = best.transform.forward;
            return new MainLight { Valid = true, Color = LightColorLinear(best), ToLight = math.normalize(new float3(-d.x, -d.y, -d.z)) };
        }

        static bool Usable(Light l) => l != null && l.type == LightType.Directional && l.isActiveAndEnabled && l.intensity > 0f
                                       && l.lightmapBakeType != LightmapBakeType.Baked;

        /// <summary>SH ambient for an object: interpolated probe if the scene has baked probes, else the ambient probe.</summary>
        public static ShL2 AmbientAt(in LightingSnapshot env, Vector3 position, Renderer renderer = null)
        {
            if (!env.HasLightProbes) return env.Ambient;
            LightProbes.GetInterpolatedProbe(position, renderer, out SphericalHarmonicsL2 p);
            return ShL2.FromUnity(p);
        }

        /// <summary>Linear emission of the tag's current state: what <see cref="SemanticTag.EmissionColor"/> uploads, after the GPU's gamma-to-linear step.</summary>
        public static float3 EmissionLinear(SemanticTag tag)
        {
            var s = tag.CurrentState;
            if (s == null || s.emissionIntensity <= 0f) return float3.zero;
            return SemanticGraph.SrgbToLinear(tag.CurrentAlbedo) * s.emissionIntensity;
        }

        /// <summary>Predicted (unclamped) lit colour of <paramref name="tag"/> at <paramref name="position"/> with surface normal <paramref name="normal"/>.
        /// Unlit materials (screens, sky) show their colour as-is.</summary>
        public static float3 Predict(SemanticTag tag, in LightingSnapshot env, Vector3 position, Vector3 normal)
        {
            float3 albedo = SemanticGraph.SrgbToLinear(tag.CurrentAlbedo);
            if (tag.IsUnlit) return albedo;
            float3 n = math.normalize(new float3(normal.x, normal.y, normal.z));
            var ambient = AmbientAt(env, position, tag.TargetRenderer);
            return Lit(albedo, Irradiance(ambient, env.Main, n), EmissionLinear(tag));
        }

        /// <summary>Prediction at the object's anchor using its dominant face normal (see <see cref="SemanticTag.DominantNormal"/>). This is what the pair graph uses.</summary>
        public static float3 PredictDominant(SemanticTag tag, in LightingSnapshot env, Vector3? viewer)
        {
            Vector3 n = tag.DominantNormal(viewer, env.Main.Valid ? new Vector3(env.Main.ToLight.x, env.Main.ToLight.y, env.Main.ToLight.z) : (Vector3?)null);
            return Predict(tag, env, tag.Position, n);
        }
    }
}
