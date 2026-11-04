using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderProgramRuntime
    {
        sealed class BufferImage
        {
            internal ShaderGraphBufferPass pass;
            internal int property;
            internal RenderTexture texture;
        }
        sealed class BufferProperty
        {
            internal int id;
            internal ShaderPropertyType type;
            internal RenderFingerprint.ObjectStamp identity;
        }
        BufferImage[] bufferImages;
        BufferProperty[] bufferProperties;
        int[] bufferVectors;
        ulong bufferStamp;
        bool bufferValid;
        internal int BufferDrawCount { get; private set; }
        static readonly int BufferPassId = Shader.PropertyToID("_GraphBufferPass");
        static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        static readonly int ViewportId = Shader.PropertyToID("_WhimTex_Viewport");

        void InitializeBuffers()
        {
            bufferImages = new BufferImage[compilation.buffers.Count];
            var excluded = new HashSet<int>();
            for (int i = 0; i < bufferImages.Length; i++)
            {
                var pass = compilation.buffers[i];
                int id = Shader.PropertyToID(pass.textureName);
                bufferImages[i] = new BufferImage { pass = pass, property = id };
                excluded.Add(id);
            }
            var properties = new List<BufferProperty>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                int id = shader.GetPropertyNameId(i);
                if (!excluded.Contains(id)) properties.Add(new BufferProperty { id = id, type = shader.GetPropertyType(i) });
            }
            bufferProperties = properties.ToArray();
            var vectors = new List<int>();
            foreach (string prefix in new[] { "_UvRow", "_WhimTex_LayerToLocalRow", "_GraphCanvasRow" })
                for (int i = 0; i < 3; i++) vectors.Add(Shader.PropertyToID(prefix + i));
            foreach (string name in new[] { "_CanvasSize", "_InputSize" }) vectors.Add(Shader.PropertyToID(name));
            foreach (var parameter in compilation.parameters)
                if (parameter.type == ShaderParameterType.Transform2D)
                    foreach (string direction in new[] { "ToLocal", "ToInput" })
                        for (int i = 0; i < 3; i++) vectors.Add(Shader.PropertyToID(parameter.InternalPrefix + direction + "Row" + i));
            bufferVectors = vectors.ToArray();
        }

        internal void Render(Texture input, RenderTexture destination, in LayerRenderContext context)
        {
            if (compilation.buffers.Count == 0) { Graphics.Blit(input, destination, Material, 0); return; }
            if (bufferImages == null) InitializeBuffers();
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            Texture previousInput = Material.GetTexture(MainTextureId);
            Vector4 viewport = Material.GetVector(ViewportId);
            try
            {
                Material.SetTexture(MainTextureId, input);
                ulong stamp = BufferFingerprint(context, out bool volatileInput);
                bool dirty = !bufferValid || volatileInput || stamp != bufferStamp;
                for (int i = 0; i < bufferImages.Length; i++)
                {
                    var image = bufferImages[i];
                    int width = image.pass.resolution == ShaderGraphBufferResolution.Canvas ? destination.width : image.pass.size.x;
                    int height = image.pass.resolution == ShaderGraphBufferResolution.Canvas ? destination.height : image.pass.size.y;
                    if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                        throw new ShaderGraphException(image.pass.nodeId, "Buffer resolution exceeds this device's maximum texture size.");
                    if (image.texture != null && image.texture.IsCreated() && image.texture.width == width && image.texture.height == height) continue;
                    ReleaseBuffer(image);
                    image.texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                    {
                        hideFlags = HideFlags.HideAndDontSave, name = "Shader Graph Buffer",
                        filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false
                    };
                    if (!image.texture.Create()) throw new InvalidOperationException("Cannot allocate Shader Graph Buffer.");
                    WhimTexProfiler.TrackTexture(image.texture, "shader graph buffer");
                    dirty = true;
                }
                if (dirty)
                {
                    bufferValid = false;
                    GL.sRGBWrite = false;
                    Material.SetVector(ViewportId, new Vector4(0, 0, 1, 1));
                    // Never bind an image as a reader while writing it. Dependencies
                    // are topologically ordered and become available after their pass.
                    foreach (var image in bufferImages) Material.SetTexture(image.property, null);
                    for (int i = 0; i < bufferImages.Length; i++)
                    {
                        var image = bufferImages[i];
                        Material.SetInt(BufferPassId, i + 1);
                        Graphics.Blit(input, image.texture, Material, 0);
                        image.texture.IncrementUpdateCount();
                        BufferDrawCount++;
                        Material.SetTexture(image.property, image.texture);
                    }
                    bufferStamp = stamp; bufferValid = true;
                }
                else foreach (var image in bufferImages) Material.SetTexture(image.property, image.texture);
                Material.SetInt(BufferPassId, 0);
                Material.SetVector(ViewportId, viewport);
                GL.sRGBWrite = srgb;
                Graphics.Blit(input, destination, Material, 0);
            }
            finally
            {
                Material.SetInt(BufferPassId, 0);
                Material.SetVector(ViewportId, viewport);
                Material.SetTexture(MainTextureId, previousInput);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
            }
        }

        ulong BufferFingerprint(in LayerRenderContext context, out bool volatileInput)
        {
            ulong hash = fingerprint;
            bool volatileTexture = compilation.bufferExternalCode;
            void Vector(Vector4 value)
            { for (int i = 0; i < 4; i++) hash = RenderFingerprint.Float(hash, value[i]); }
            foreach (var property in bufferProperties)
            {
                switch (property.type)
                {
                    case ShaderPropertyType.Texture:
                        var texture = Material.GetTexture(property.id);
                        hash = RenderFingerprint.Mix(hash, property.identity.Get(texture));
                        if (texture != null)
                        {
                            hash = RenderFingerprint.Mix(hash, texture.updateCount);
                            // A GPU writer need not increment Texture.updateCount. Reuse
                            // its result only within this render, never assume it is static.
                            volatileTexture |= texture is RenderTexture;
                            hash = RenderFingerprint.Mix(hash, (ulong)texture.filterMode);
                            hash = RenderFingerprint.Mix(hash, (ulong)texture.wrapMode);
                        }
                        break;
                    case ShaderPropertyType.Color: case ShaderPropertyType.Vector: Vector(Material.GetVector(property.id)); break;
                    case ShaderPropertyType.Int: hash = RenderFingerprint.Mix(hash, unchecked((uint)Material.GetInteger(property.id))); break;
                    default: hash = RenderFingerprint.Float(hash, Material.GetFloat(property.id)); break;
                }
            }
            foreach (var id in bufferVectors) Vector(Material.GetVector(id));
            hash = RenderFingerprint.Float(hash, context.scaleMultiplier);
            hash = RenderFingerprint.Float(hash, context.pixelScale);
            if (UsesTime) hash = RenderFingerprint.Float(hash, context.shaderContext.Time);
            if (UsesLighting) hash = RenderFingerprint.Mix(hash, context.activeDocument.LightingFingerprint);
            if (UsesInstanceIndex) hash = RenderFingerprint.Mix(hash, context.shaderContext.InstanceIndex);
            hash = RenderFingerprint.Mix(hash, unchecked((uint)Material.GetInt("_UnboundedUv")));
            if (compilation.usesCanvasPosition)
            {
                hash = RenderFingerprint.Mix(hash, unchecked((uint)Material.GetInt("_GraphCanvasCoordinates")));
                hash = RenderFingerprint.Mix(hash, unchecked((uint)Material.GetInt("_GraphTiling")));
            }
            volatileInput = volatileTexture;
            return hash;
        }

        static void ReleaseBuffer(BufferImage image)
        {
            if (image.texture == null) return;
            WhimTexProfiler.ReleaseTexture(image.texture);
            UnityEngine.Object.DestroyImmediate(image.texture);
            image.texture = null;
        }
        void ReleaseBuffers()
        {
            if (bufferImages != null) foreach (var image in bufferImages) ReleaseBuffer(image);
            bufferImages = null; bufferProperties = null; bufferVectors = null; bufferValid = false;
        }
    }
}
