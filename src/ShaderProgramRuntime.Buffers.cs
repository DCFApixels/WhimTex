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
            internal RenderTexture[] lightingTargets;
            internal RenderBuffer[] colors;
            internal BufferImage lightingSource;
            internal Material material;
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
        static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        static readonly int ViewportId = Shader.PropertyToID("_WhimTex_Viewport");
        static readonly int BakeSizeId = Shader.PropertyToID("_GraphBakeSize");

        void InitializeBuffers()
        {
            bufferImages = new BufferImage[compilation.buffers.Count];
            var excluded = new HashSet<int>();
            for (int i = 0; i < bufferImages.Length; i++)
            {
                var pass = compilation.buffers[i];
                int id = Shader.PropertyToID(pass.textureName);
                bufferImages[i] = new BufferImage { pass = pass, property = id, material = bufferMaterials[i] };
                excluded.Add(id);
            }
            foreach (var image in bufferImages)
                if (image.pass.lightingSource != null)
                    foreach (var other in bufferImages) if (ReferenceEquals(other.pass, image.pass.lightingSource)) image.lightingSource = other;
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
            => RenderPrepared(input, destination, context, true);

        internal void RenderPrepared(Texture input, RenderTexture destination, in LayerRenderContext context, bool drawFinal)
        {
            if (compilation.buffers.Count == 0) { if (drawFinal) Graphics.Blit(input, destination, Material, 0); return; }
            if (bufferImages == null) InitializeBuffers();
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            Texture previousInput = Material.GetTexture(MainTextureId);
            Vector4 viewport = BufferVector(ViewportId);
            Vector4 bakeSize = BufferVector(BakeSizeId);
            Material.SetVector("_GraphLightingViewport", viewport);
            try
            {
                Material.SetTexture(MainTextureId, input);
                ulong stamp = BufferFingerprint(context, out bool volatileInput);
                bool dirty = !bufferValid || volatileInput || stamp != bufferStamp;
                for (int i = 0; i < bufferImages.Length; i++)
                {
                    var image = bufferImages[i];
                    if (image.pass.lightingInputs) continue;
                    int width = image.pass.resolution == ShaderGraphBufferResolution.Canvas ? destination.width : image.pass.size.x;
                    int height = image.pass.resolution == ShaderGraphBufferResolution.Canvas ? destination.height : image.pass.size.y;
                    if (image.pass.causticsHeight) { width = Mathf.Clamp(width, 16, 512); height = Mathf.Clamp(height, 16, 512); }
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
                        var producer = bufferMaterials[i];
                        producer.CopyPropertiesFromMaterial(Material);
                        if (image.pass.program.usesLighting) context.activeDocument.BindLighting(producer);
                        if (image.pass.screenSpace) producer.SetVector(ViewportId, viewport);
                        if (image.pass.lightingInputs) continue;
                        if (image.pass.lightingSource != null) RenderLighting(input, image, viewport, context);
                        else if (image.pass.caustics) RenderCaustics(input, image.texture, image.pass.wrap, producer);
                        else Graphics.Blit(input, image.texture, producer, 0);
                        image.texture.IncrementUpdateCount();
                        BufferDrawCount++;
                        Material.SetTexture(image.property, image.texture);
                    }
                    bufferStamp = stamp; bufferValid = true;
                }
                else foreach (var image in bufferImages) Material.SetTexture(image.property, image.texture);
                Material.SetVector(ViewportId, viewport);
                GL.sRGBWrite = srgb;
                if (drawFinal) Graphics.Blit(input, destination, Material, 0);
            }
            finally
            {
                foreach (var image in bufferImages) if (image.pass.lightingInputs) ReleaseBuffer(image);
                Material.SetVector(ViewportId, viewport);
                Material.SetTexture(MainTextureId, previousInput);
                Material.SetVector(BakeSizeId, bakeSize);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
            }
        }

        internal RenderTexture BufferTexture(string name)
        {
            if (bufferImages != null) foreach (var image in bufferImages)
                if (image.pass.textureName == name) return image.texture;
            throw new InvalidOperationException("Missing compiled image boundary: " + name);
        }

        // Coordinate uniforms can be absent until their host path binds them. Check at
        // read time: a later render may enable transforms on the same runtime material.
        Vector4 BufferVector(int id) => Material.HasVector(id) ? Material.GetVector(id) : Vector4.zero;
        int BufferInt(string name) => Material.HasProperty(name) ? Material.GetInt(name) : 0;

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
            foreach (var id in bufferVectors) Vector(BufferVector(id));
            if (compilation.buffers.Exists(p => p.screenSpace)) Vector(BufferVector(ViewportId));
            hash = RenderFingerprint.Float(hash, context.scaleMultiplier);
            hash = RenderFingerprint.Float(hash, context.pixelScale);
            if (UsesTime) hash = RenderFingerprint.Float(hash, context.shaderContext.Time);
            if (UsesLighting) hash = RenderFingerprint.Mix(hash, context.activeDocument.LightingFingerprint);
            if (UsesInstanceIndex) hash = RenderFingerprint.Mix(hash, context.shaderContext.InstanceIndex);
            hash = RenderFingerprint.Mix(hash, unchecked((uint)BufferInt("_UnboundedUv")));
            if (compilation.usesCanvasPosition)
            {
                hash = RenderFingerprint.Mix(hash, unchecked((uint)BufferInt("_GraphCanvasCoordinates")));
                hash = RenderFingerprint.Mix(hash, unchecked((uint)BufferInt("_GraphTiling")));
            }
            volatileInput = volatileTexture;
            return hash;
        }

        // Only the result is full-sized. Surface inputs are evaluated in aligned tiles,
        // so four float32 MRTs do not add gigabytes of scratch storage to an 8K render.
        void RenderLighting(Texture input, BufferImage image, Vector4 viewport, in LayerRenderContext context)
        {
            const int tileSize = 512;
            var inputs = image.lightingSource;
            var surface = inputs.material; var lighting = image.material;
            surface.CopyPropertiesFromMaterial(Material);
            if (inputs.pass.program.usesLighting) context.activeDocument.BindLighting(surface);
            lighting.SetVector(ViewportId, new Vector4(0, 0, 1, 1));
            int width = image.texture.width, height = image.texture.height;
            try
            {
                for (int y = 0; y < height; y += tileSize)
                for (int x = 0; x < width; x += tileSize)
                {
                    int w = Mathf.Min(tileSize, width - x), h = Mathf.Min(tileSize, height - y);
                    AllocateLightingInputs(inputs, w, h);
                    surface.SetVector(ViewportId, new Vector4(viewport.x + viewport.z * x / width,
                        viewport.y + viewport.w * y / height, viewport.z * w / width, viewport.w * h / height));
                    RenderTexture tile = null;
                    try
                    {
                        RenderLightingInputs(input, inputs, surface);
                        lighting.SetVector("_GraphLightSize", new Vector4(w, h, 0, 0));
                        for (int slot = 0; slot < 4; slot++) lighting.SetTexture("_GraphLightData" + slot, inputs.lightingTargets[slot]);
                        if (w == width && h == height) Graphics.Blit(input, image.texture, lighting, 0);
                        else
                        {
                            tile = ProfiledRenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                            Graphics.Blit(input, tile, lighting, 0);
                            Graphics.CopyTexture(tile, 0, 0, 0, 0, w, h, image.texture, 0, 0, x, y);
                        }
                    }
                    finally
                    {
                        for (int slot = 0; slot < 4; slot++) lighting.SetTexture("_GraphLightData" + slot, null);
                        if (tile != null) ProfiledRenderTexture.ReleaseTemporary(tile);
                        ReleaseBuffer(inputs);
                    }
                    BufferDrawCount++;
                }
            }
            finally { ReleaseBuffer(inputs); }
        }

        static void RenderLightingInputs(Texture input, BufferImage image, Material material)
        {
            material.SetTexture(MainTextureId, input);
            Graphics.SetRenderTarget(image.colors, image.texture.depthBuffer);
            GL.Clear(false, true, Color.clear);
            // This overload preserves the active MRT binding and uses the same
            // fullscreen geometry/UV interpolation as the ordinary graph path.
            Graphics.Blit(input, material, 0);
        }

        static void AllocateLightingInputs(BufferImage image, int width, int height)
        {
            image.lightingTargets ??= new RenderTexture[4]; image.colors ??= new RenderBuffer[4];
            for (int i = 0; i < 4; i++)
            {
                var texture = ProfiledRenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
                image.lightingTargets[i] = texture; image.colors[i] = texture.colorBuffer;
            }
            image.texture = image.lightingTargets[0];
        }
        static void ReleaseBuffer(BufferImage image)
        {
            if (image.pass.lightingInputs)
            {
                if (image.lightingTargets != null) for (int i = 0; i < image.lightingTargets.Length; i++)
                {
                    if (image.lightingTargets[i] != null) ProfiledRenderTexture.ReleaseTemporary(image.lightingTargets[i]);
                    image.lightingTargets[i] = null;
                }
                image.texture = null; return;
            }
            if (image.texture == null) return;
            WhimTexProfiler.ReleaseTexture(image.texture);
            UnityEngine.Object.DestroyImmediate(image.texture);
            image.texture = null;
        }
        void ReleaseBuffers()
        {
            if (causticsMaterial != null) UnityEngine.Object.DestroyImmediate(causticsMaterial);
            causticsMaterial = null;
            if (bufferImages != null) foreach (var image in bufferImages) ReleaseBuffer(image);
            bufferImages = null; bufferProperties = null; bufferVectors = null; bufferValid = false;
        }
    }
}
