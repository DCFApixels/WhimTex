using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_3_OR_NEWER
using Unity.VectorGraphics;
#endif

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class ShaderFXIconCache
    {
        private sealed class Request
        {
            internal ShaderFXIconMetadata.Descriptor icon;
            internal string source, warning;
            internal int size;
            internal bool dark, ready;
            internal double lastUsed;
            internal Texture2D texture;
            internal readonly List<WeakReference<AdvancedDropdownItem>> items = new List<WeakReference<AdvancedDropdownItem>>();
        }
        private static readonly Dictionary<string, Request> requests = new Dictionary<string, Request>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Queue<Request> pending = new Queue<Request>();
        private static readonly ShaderFXIconMetadata.Descriptor categoryIcon = new ShaderFXIconMetadata.Descriptor
        {
            svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\"><path fill=\"currentColor\" fill-opacity=\"0.22\" d=\"M2 3h4.2c.4 0 .7 .15 1 .45L8.5 5H14a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z\"/></svg>"
        };
        private static double nextFileCheck;
        internal static event Action Changed;
        internal static Texture2D Default => EditorGUIUtility.ObjectContent(null, typeof(Shader)).image as Texture2D;

        static ShaderFXIconCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
            EditorApplication.projectChanged += Invalidate;
            EditorApplication.focusChanged += focused => { if (focused) Invalidate(); };
            EditorApplication.update += Update;
        }

        private static Request GetRequest(ShaderFXIconMetadata.Descriptor icon, string source)
        {
            if (icon == null) return null;
            int size = Mathf.Clamp(Mathf.CeilToInt(16 * EditorGUIUtility.pixelsPerPoint), 16, 64);
            bool dark = EditorGUIUtility.isProSkin;
            string key = size + ":" + dark + ":" + (icon.svg ?? icon.reference ?? icon.error);
            if (icon.reference != null && !icon.reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) && !icon.reference.StartsWith("data:", StringComparison.Ordinal))
                key += ":" + Path.GetDirectoryName(source ?? "");
            if (!requests.TryGetValue(key, out var request))
            {
                request = new Request { icon = icon, source = source, size = size, dark = dark };
                requests.Add(key, request);
                pending.Enqueue(request);
            }
            request.lastUsed = EditorApplication.timeSinceStartup;
            return request;
        }

        internal static Texture2D Get(ShaderFXIconMetadata.Descriptor icon, string source, out string warning)
        {
            var request = GetRequest(icon, source);
            warning = request?.warning;
            return request?.texture != null ? request.texture : Default;
        }

        internal static void Bind(AdvancedDropdownItem item, ShaderFXIconMetadata.Descriptor icon, string source)
        {
            var request = GetRequest(icon, source);
            item.icon = request?.texture != null ? request.texture : Default;
            if (request != null && !request.ready) request.items.Add(new WeakReference<AdvancedDropdownItem>(item));
        }

        internal static void BindCategory(AdvancedDropdownItem item)
        {
            var request = GetRequest(categoryIcon, null);
            item.icon = request.texture;
            if (!request.ready) request.items.Add(new WeakReference<AdvancedDropdownItem>(item));
        }

        internal static void Warm(IReadOnlyList<ShaderFXCatalog.Entry> entries)
        {
            GetRequest(categoryIcon, null);
            foreach (var entry in entries) GetRequest(entry.icon, entry.path);
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (pending.Count == 0) return;
            var request = pending.Dequeue();
            if (request.ready) return;
            try
            {
                byte[] bytes = ShaderFXIconMetadata.ReadImage(request.icon, request.source, out bool svg);
                if (svg) bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim().Replace("\r\n", "\n"));
                using var sha = System.Security.Cryptography.SHA256.Create();
                string key = Convert.ToBase64String(sha.ComputeHash(bytes)) + ":" + request.size + ":" + request.dark;
                if (!textures.TryGetValue(key, out var texture) || texture == null)
                {
                    texture = svg ? RenderSvg(Encoding.UTF8.GetString(bytes), request.size, request.dark) : LoadPng(bytes);
                    texture.name = "WhimTex FX Icon";
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    textures[key] = texture;
                }
                request.texture = texture;
                request.warning = null;
            }
            catch (Exception error)
            {
                request.texture = null;
                request.warning = "FX icon: " + error.Message;
            }
            request.ready = true;
            for (int i = request.items.Count - 1; i >= 0; i--)
                if (request.items[i].TryGetTarget(out var item)) item.icon = request.texture != null ? request.texture : Default;
            request.items.Clear();
            Changed?.Invoke();
            if (pending.Count == 0) Trim();
        }

        private static void Trim()
        {
            if (requests.Count > 256)
            {
                var oldest = new List<KeyValuePair<string, Request>>(requests);
                oldest.Sort((a, b) => a.Value.lastUsed.CompareTo(b.Value.lastUsed));
                for (int i = 0; i < oldest.Count - 256; i++) requests.Remove(oldest[i].Key);
            }
            var retained = new HashSet<Texture2D>();
            foreach (var request in requests.Values) if (request.texture != null) retained.Add(request.texture);
            var expired = new List<string>();
            foreach (var pair in textures)
                if (!retained.Contains(pair.Value)) expired.Add(pair.Key);
            foreach (string key in expired) { UnityEngine.Object.DestroyImmediate(textures[key]); textures.Remove(key); }
        }

        private static Texture2D LoadPng(byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes, true)) return texture;
            UnityEngine.Object.DestroyImmediate(texture);
            throw new FormatException("Cannot decode PNG icon.");
        }

        private static Texture2D RenderSvg(string svg, int size, bool dark)
        {
#if UNITY_6000_3_OR_NEWER
            svg = svg.TrimStart('\uFEFF').Replace("currentColor", dark ? "#dedede" : "#383838");
            var scene = SVGParser.ImportSVG(new StringReader(svg), ViewportOptions.PreserveViewport, 96, 1);
            var geometry = VectorUtils.TessellateScene(scene.Scene, new VectorUtils.TessellationOptions
            {
                StepDistance = 100, MaxCordDeviation = 0.05f, MaxTanAngleDeviation = 0.1f, SamplingStepSize = 0.01f
            }, scene.NodeOpacity);
            foreach (var part in geometry)
                if (part.Fill is GradientFill || part.Fill is TextureFill)
                    throw new FormatException("SVG icon gradients and image fills are unsupported; use PNG for these icons.");
            var shader = Shader.Find("Hidden/WhimTex/FXIcon");
            if (shader == null) throw new InvalidOperationException("FX icon renderer is unavailable.");
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            // Downsample single-sample targets: the first MSAA readback can be transparent
            // in editor rendering, which would persist for every preset sharing this icon.
            var target = RenderTexture.GetTemporary(size * 2, size * 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Bilinear;
            var resolved = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var commands = new CommandBuffer { name = "WhimTex FX Icon" };
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            Texture2D result = null;
            try
            {
                if (!ShaderUtil.IsPassCompiled(material, 0)) ShaderUtil.CompilePass(material, 0, true);
                VectorUtils.FillMesh(mesh, geometry, 1, false);
                if (mesh.vertexCount == 0) throw new FormatException("SVG icon contains no supported vector geometry.");
                Rect viewport = scene.SceneViewport;
                if (viewport.width <= 0 || viewport.height <= 0) throw new FormatException("SVG icon needs a non-empty viewport.");
                float extent = Mathf.Max(viewport.width, viewport.height);
                Vector2 center = viewport.center;
                GL.sRGBWrite = false;
                var projection = Matrix4x4.Ortho(center.x - extent / 2, center.x + extent / 2,
                    center.y + extent / 2, center.y - extent / 2, -1, 1);
                material.SetMatrix("_IconProjection", GL.GetGPUProjectionMatrix(projection, true));
                commands.SetRenderTarget(target);
                commands.SetViewport(new Rect(0, 0, target.width, target.height));
                commands.DisableScissorRect();
                commands.ClearRenderTarget(false, true, Color.clear);
                commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                commands.Blit(target, resolved);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = resolved;
                result = new Texture2D(size, size, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                var pixels = result.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    if (p.a > 0) { p.r = (byte)Math.Min(255, p.r * 255 / p.a); p.g = (byte)Math.Min(255, p.g * 255 / p.a); p.b = (byte)Math.Min(255, p.b * 255 / p.a); }
                    pixels[i] = p;
                }
                result.SetPixels32(pixels);
                result.Apply(false, true);
                return result;
            }
            catch { if (result != null) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(target);
                RenderTexture.ReleaseTemporary(resolved);
                commands.Release();
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
#else
            throw new NotSupportedException("SVG FX icons require Unity 6.3 or newer; PNG icons are supported on earlier versions.");
#endif
        }

        private static void Invalidate()
        {
            if (EditorApplication.timeSinceStartup < nextFileCheck) return;
            nextFileCheck = EditorApplication.timeSinceStartup + 0.25;
            foreach (var request in requests.Values)
            {
                if (!request.ready || request.icon.reference == null || request.icon.reference.StartsWith("data:", StringComparison.Ordinal)) continue;
                request.ready = false;
                pending.Enqueue(request);
            }
        }

        private static void Clear()
        {
            pending.Clear();
            requests.Clear();
            foreach (var texture in textures.Values) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            textures.Clear();
        }
    }
}
