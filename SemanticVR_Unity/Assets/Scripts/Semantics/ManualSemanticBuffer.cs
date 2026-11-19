using System;
using System.Collections.Generic;
using SemanticVR.Environment;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SemanticVR.Semantics
{
    /// <summary>
    /// Renders the existing manual <see cref="SemanticTag"/> labels into a flat category-ID buffer (R = category + 1,
    /// 0 = untagged/sky) with the same stretched framing as the model input, and reads it back asynchronously.
    /// Used as the manual fallback and to expose AI-vs-manual disagreement. It never modifies any SemanticTag.
    /// Colliders are not needed (Phase 1 removed them), the buffer is rendered from mesh renderers.
    /// </summary>
    public sealed class ManualSemanticBuffer : IDisposable
    {
        static readonly int CategoryCount = Enum.GetValues(typeof(SemanticCategory)).Length;
        readonly List<KeyValuePair<Renderer, int>> items = new List<KeyValuePair<Renderer, int>>(256);
        readonly Material[] materials = new Material[CategoryCount];
        readonly RenderTexture rt;
        readonly CommandBuffer cb = new CommandBuffer { name = "SemanticVR Manual Category Buffer" };
        readonly byte[] data;
        bool requestPending;
        /// <summary>Depth clear value. Must be 0 on reversed-Z platforms (Metal, D3D, Vulkan): verified, a clear of 1 draws nothing/garbage.</summary>
        public float ClearDepth = SystemInfo.usesReversedZBuffer ? 0f : 1f;

        /// <summary>The category-ID render target (R8, value = (category + 1) / 255). Valid after the first <see cref="Render"/>.</summary>
        public RenderTexture Texture => rt;
        /// <summary>Edge length in pixels (square, stretched like the model input).</summary>
        public int Size { get; }
        /// <summary>True once at least one readback has arrived.</summary>
        public bool HasData { get; private set; }
        /// <summary>Number of tagged renderers drawn.</summary>
        public int RendererCount => items.Count;
        /// <summary>False if the shader was not found.</summary>
        public bool IsValid { get; }

        public ManualSemanticBuffer(int size)
        {
            Size = size; data = new byte[size * size];
            var shader = Resources.Load<Shader>("SemanticVR/CategoryFlat");
            if (shader == null) { Debug.LogError("[SemanticVR] CategoryFlat shader missing."); return; }
            for (int i = 0; i < CategoryCount; i++)
            {
                materials[i] = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                materials[i].SetFloat("_Id", i + 1);
                materials[i].SetFloat("_ZTestMode", (float)(SystemInfo.usesReversedZBuffer ? CompareFunction.GreaterEqual : CompareFunction.LessEqual));
            }
            var desc = new RenderTextureDescriptor(size, size, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm, UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat) { sRGB = false, msaaSamples = 1 };
            rt = new RenderTexture(desc) { name = "ManualCategoryIds", filterMode = FilterMode.Point };
            rt.Create();
            IsValid = true;
            Rebuild();
        }

        /// <summary>Re-scans the scene for tagged renderers (call after spawning/destroying tagged objects).</summary>
        public void Rebuild()
        {
            items.Clear();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var tag = r.GetComponentInParent<SemanticTag>();
                if (tag != null) items.Add(new KeyValuePair<Renderer, int>(r, (int)tag.Category));
            }
        }

        /// <summary>Renders the buffer with <paramref name="cam"/>'s matrices and starts an async readback.</summary>
        public void Render(Camera cam, bool readback = true)
        {
            if (!IsValid || (readback && requestPending)) return;
            cb.Clear();
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.black, ClearDepth);
            cb.SetViewProjectionMatrices(cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(cam.projectionMatrix, true));
            foreach (var kv in items)
            {
                var r = kv.Key;
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                int sub = Mathf.Max(1, r.sharedMaterials.Length);
                for (int s = 0; s < sub; s++) cb.DrawRenderer(r, materials[kv.Value], s);
            }
            Graphics.ExecuteCommandBuffer(cb);
            if (!readback) return;   // GPU-only use (live priority weights): no CPU copy needed
            requestPending = true;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.R8, OnReadback);
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            requestPending = false;
            if (req.hasError) { Debug.LogWarning("[SemanticVR] Manual semantic readback failed."); return; }
            req.GetData<byte>().CopyTo(data);
            HasData = true;
        }

        /// <summary>Debug: raw readback bytes (row 0 as delivered by the GPU readback).</summary>
        public byte[] DebugData => data;

        /// <summary>Number of collected renderers per category (diagnostics).</summary>
        public int[] CountsPerCategory()
        {
            var c = new int[CategoryCount];
            foreach (var kv in items) c[kv.Value]++;
            return c;
        }

        /// <summary>True while a readback is outstanding.</summary>
        public bool IsPending => requestPending;

        /// <summary>
        /// Value at normalised (u, v), origin TOP-left: 0 = untagged, otherwise category + 1.
        /// Verified empirically: the GPU readback of this buffer is delivered top row first, so no flip is applied.
        /// </summary>
        public int ValueAt(float u, float vTop)
        {
            int x = Mathf.Clamp((int)(u * Size), 0, Size - 1);
            int y = Mathf.Clamp((int)(vTop * Size), 0, Size - 1);
            return data[y * Size + x];
        }

        public void Dispose()
        {
            cb.Release();
            foreach (var m in materials) if (m != null) UnityEngine.Object.Destroy(m);
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        }
    }
}
