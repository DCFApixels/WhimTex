using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class HeightToDistortionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const int W = 40, H = 32;
    public static string Run() => TestContext.Run("Height to Distortion GPU and document integration", t =>
    {
        var owned = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        RenderTexture output = null;
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = W; doc.height = H;
            var input = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            var map = Own(new Texture2D(17, 13, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            var read = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));
            output = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var assembly = typeof(ShaderFX).Assembly;
            var draft = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            var getMaterial = typeof(ShaderFX).GetMethod("GetMaterial", F);
            object Context(int width = W, int height = H, float scale = 1) => Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true), doc, null, width, height, scale, true, true, null);
            ShaderFX Create(string code)
            {
                var fx = Own((ShaderFX)draft.Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() }));
                typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null); return fx;
            }
            ShaderFX Catalog(string name)
            {
                var catalog = assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
                foreach (object entry in (IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
                    if ((string)entry.GetType().GetField("menuPath", F).GetValue(entry) == name)
                        return Own((ShaderFX)typeof(ShaderFX).GetMethod("FromCatalog", F).Invoke(null, new object[] { doc, entry }));
                throw new InvalidOperationException("Missing imported catalog preset: " + name);
            }
            ShaderFXParameter P(ShaderFX fx, string name) => ((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx)).Find(p => p.name == name);
            void Set(ShaderFX fx, string name, float value)
            {
                P(fx, name).floatValue = value;
                typeof(ShaderFX).GetMethod("NotifyValuesChanged", F).Invoke(fx, null);
            }
            Color[] Read(RenderTexture texture)
            {
                t.Equal(W, texture.width, "Readback width"); t.Equal(H, texture.height, "Readback height");
                RenderTexture.active = texture; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply(); return read.GetPixels();
            }
            Color[] Render(ShaderFX fx, Texture height)
            {
                var material = (Material)getMaterial.Invoke(fx, new[] { Context() });
                t.True(material != null && !ShaderUtil.ShaderHasError(material.shader), "FX compiled through production builder");
                material.SetTexture("_HeightMap", height);
                GL.sRGBWrite = false; Graphics.Blit(input, output, material); return Read(output);
            }
            Color[] Composite(WhimTexDocument target = null)
            {
                var active = RenderTexture.active; bool write = GL.sRGBWrite;
                var image = (target ?? doc).ComposeCanvas();
                try
                {
                    t.True(active == RenderTexture.active && write == GL.sRGBWrite, "Composition restores caller GPU state");
                    return image.GetPixels();
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            void Same(Color[] expected, Color[] actual, string label, double tolerance = .004)
            {
                t.Equal(expected.Length, actual.Length, label + " dimensions");
                double worst = 0; bool finite = true; int worstPixel = 0, worstChannel = 0;
                for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                {
                    finite &= !float.IsNaN(actual[i][c]) && !float.IsInfinity(actual[i][c]);
                    double error = Math.Abs(expected[i][c] - actual[i][c]);
                    if (error > worst) { worst = error; worstPixel = i; worstChannel = c; }
                }
                t.True(finite, label + " finite RGBA"); t.Near(0, worst, tolerance, label + " at " + (worstPixel % W) + "," + (worstPixel / W) +
                    " channel " + worstChannel + ": expected " + expected[worstPixel] + ", actual " + actual[worstPixel]);
            }
            var generator = Catalog("Distortion/Height to Distortion");
            var displacement = Catalog("Distortion/Displacement Map");
            t.Equal(0f, P(generator, "_Output").floatValue, "Packed RG defaults to existing displacement encoding");
            t.Equal(ShaderFXTextureSource.Self, P(generator, "_HeightMap").textureSource, "Height source defaults to Self");
            t.Equal(0f, P(generator, "_Filter").floatValue, "Scharr default");
            t.True(!(bool)typeof(ShaderFX).GetProperty("IsPixelLocal", F).GetValue(generator), "Neighbour sampling is not pixel-local");
            t.Equal("spatial", (string)typeof(ShaderFX).GetProperty("AppliedLocalityStatus", F).GetValue(generator),
                "Explicit LOD reads use the verified spatial sampling API");
            t.Equal("Applied successfully.", (string)typeof(ShaderFX).GetProperty("Diagnostics", F).GetValue(generator),
                "Height to Distortion compiles without locality warnings");
            string source = (string)typeof(ShaderFX).GetProperty("Code", F).GetValue(generator);
            t.Equal("// @control(_Strength)", source.Split('\n')[2].TrimEnd('\r'), "Header controls gradient strength after preset identity");

            var pixels = new Color[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) pixels[y * W + x] = new Color((x + .5f) / W, (y + .5f) / H, .2f, 1);
            input.SetPixels(pixels); input.Apply();
            var heights = new Color[map.width * map.height];
            for (int y = 0; y < map.height; y++) for (int x = 0; x < map.width; x++)
                heights[y * map.width + x] = new Color(.03f * x - .04f * y, .1f * Mathf.Sin(x) + .02f * y, 1.4f - .02f * x, .1f + .03f * y);
            map.SetPixels(heights); map.Apply();
            int Address(int value, int size, int edge)
            {
                if (edge == 0) return Mathf.Clamp(value, 0, size - 1);
                int period = edge == 1 ? size : size * 2;
                int index = (value % period + period) % period;
                return edge == 1 || index < size ? index : period - 1 - index;
            }
            float Height(Vector2 uv, int edge, int channel)
            {
                float x = uv.x * map.width - .5f, y = uv.y * map.height - .5f;
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                Color At(int px, int py) => heights[Address(py, map.height, edge) * map.width + Address(px, map.width, edge)];
                Color c = Color.LerpUnclamped(Color.LerpUnclamped(At(ix, iy), At(ix + 1, iy), x - ix),
                    Color.LerpUnclamped(At(ix, iy + 1), At(ix + 1, iy + 1), x - ix), y - iy);
                return channel == 0 ? c.r * .2126f + c.g * .7152f + c.b * .0722f : c[channel == 4 ? 3 : channel - 1];
            }
            Color[] Oracle(int filter, int edge, int channel, float radius, float strength)
            {
                var expected = new Color[W * H];
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    var uv = new Vector2((x + .5f) / W, (y + .5f) / H);
                    float At(float dx, float dy) => Height(uv + new Vector2(dx * radius / W, dy * radius / H), edge, channel);
                    float side = filter == 0 ? 3 : filter == 1 ? 1 : 0, center = filter == 0 ? 10 : filter == 1 ? 2 : 1;
                    float gx = side * (At(1, -1) + At(1, 1) - At(-1, -1) - At(-1, 1)) + center * (At(1, 0) - At(-1, 0));
                    float gy = side * (At(-1, 1) + At(1, 1) - At(-1, -1) - At(1, -1)) + center * (At(0, 1) - At(0, -1));
                    expected[y * W + x] = new Color(gx * strength / ((4 * side + 2 * center) * radius), gy * strength / ((4 * side + 2 * center) * radius), 0, 1);
                }
                return expected;
            }
            Set(generator, "_Output", 1); Set(generator, "_Strength", 8);
            foreach (int filter in new[] { 0, 1, 2 }) foreach (int edge in new[] { 0, 1, 2 })
            foreach (int channel in new[] { 0, 1, 2, 3, 4 }) foreach (float radius in new[] { 1f, 2.25f })
            {
                Set(generator, "_Filter", filter); Set(generator, "_Edges", edge); Set(generator, "_Channel", channel); Set(generator, "_Radius", radius);
                Same(Oracle(filter, edge, channel, radius, 8), Render(generator, map), $"Independent CPU derivative: filter {filter}, edge {edge}, channel {channel}, radius {radius}");
            }
            var constant = Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            constant.SetPixel(0, 0, new Color(-2, 4, .7f, .3f)); constant.Apply();
            foreach (int encoding in new[] { 0, 1 })
            {
                Set(generator, "_Output", encoding);
                Color neutral = new Color(encoding == 0 ? .5f : 0, encoding == 0 ? .5f : 0, 0, 1);
                var expected = new Color[W * H]; for (int i = 0; i < expected.Length; i++) expected[i] = neutral;
                Same(expected, Render(generator, constant), "Constant height gives exact neutral encoding", .00001);
                Set(generator, "_Strength", 0); Same(expected, Render(generator, map), "Zero header strength outputs neutral, not the source", .00001);
                Set(generator, "_Strength", 8);
            }
            var ramp = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            var rampPixels = new Color[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) rampPixels[y * W + x] = new Color(.08f * x - .04f * y, 0, 0, 1);
            ramp.SetPixels(rampPixels); ramp.Apply();
            Set(generator, "_Channel", 1); Set(generator, "_Edges", 0); Set(generator, "_Filter", 0); Set(generator, "_Radius", 1); Set(generator, "_Output", 1);
            Set(generator, "_Strength", 32);
            Color strong = Render(generator, ramp)[H / 2 * W + W / 2];
            t.Near(2.56, strong.r, .0001, "Large X slope is not normalized or clamped"); t.Near(-1.28, strong.g, .0001, "Negative Y slope retained");
            Set(generator, "_Output", 0); Color packed = Render(generator, ramp)[H / 2 * W + W / 2];
            t.Near(strong.r * .5 + .5, packed.r, .00001, "Packed affine encoding preserves values above one");
            t.Near(strong.g * .5 + .5, packed.g, .00001, "Packed affine encoding preserves values below zero");
            Set(generator, "_Output", 1); Set(generator, "_FlipX", 1); Color flipped = Render(generator, ramp)[H / 2 * W + W / 2];
            t.Near(-strong.r, flipped.r, .00001, "Flip X"); t.Near(strong.g, flipped.g, .00001, "Flip X leaves Y");
            Set(generator, "_FlipX", 0); Set(generator, "_FlipY", 1); Set(generator, "_Strength", -32);
            flipped = Render(generator, ramp)[H / 2 * W + W / 2];
            t.Near(-strong.r, flipped.r, .00001, "Negative strength reverses X"); t.Near(strong.g, flipped.g, .00001, "Negative strength and Flip Y cancel");

            Set(generator, "_Output", 0); Set(generator, "_Strength", 8); Set(generator, "_FlipY", 0);
            var heightFX = Create("float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; float h = 2.0 * uv.x - uv.y + 0.3; return float4(h, h, h, 1); }");
            Layer child = new ColorFillLayerBehaviour(); child.colorRange = LayerColorRange.HDR; child.fx.Add(heightFX); child.fx.Add(generator);
            doc.layers.Add(child); var baseline = Composite();
            // Ordinary document finishing uses Half intermediates; direct float tests above
            // retain the stricter arithmetic checks before that existing storage rounding.
            t.Near(.7, baseline[H / 2 * W + W / 2].r, .002, "Self height input through ordinary FX pipeline");
            t.Near(.375, baseline[H / 2 * W + W / 2].g, .002, "Document Y slope and sign");
            child.fx.Remove(generator);
            Layer group = new GroupLayerBehaviour(); group.colorRange = LayerColorRange.HDR; group.children.Add(child); group.fx.Add(generator);
            doc.layers.Clear(); doc.layers.Add(group); Same(baseline, Composite(), "Group Self input");
            var thumbnail = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderAgentLayerPreview", F).Invoke(doc, new object[] { group, W });
            try { Same(baseline, Read(thumbnail), "Thumbnail geometry and data"); }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
            var export = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdGroupContent", F).Invoke(doc, new object[] { group });
            try { var encoded = (Color[])baseline.Clone(); for (int i = 0; i < encoded.Length; i++) encoded[i] = encoded[i].gamma; Same(encoded, export.GetPixels(), "Group export"); }
            finally { UnityEngine.Object.DestroyImmediate(export); }
            group.clippingMask = true; doc.layers.Add(new ColorFillLayerBehaviour { color = Color.white });
            Same(baseline, Composite(), "Clipped group"); group.clippingMask = false; doc.layers.RemoveAt(1);
            group.enabled = false;
            Layer target = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = group.Id, radius = 0 };
            doc.layers.Insert(0, target); Same(baseline, Composite(), "Target input"); doc.layers.Clear(); group.enabled = true; doc.layers.Add(group);
            var json = WhimTexDocumentJson.Write(doc); t.Equal(0, json.Warnings.Count, "Preset JSON warnings");
            using (var restored = WhimTexDocumentJson.Read(json.Json))
            { t.Equal(0, restored.Warnings.Count, "Restored preset warnings: " + string.Join("; ", restored.Warnings)); Same(baseline, Composite(restored.Document), "JSON render parity"); }

            group.fx.Clear(); doc.layers.Clear(); child.fx.Add(generator); child.enabled = false;
            var imageFX = Create("float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return float4(uv, .2, 1); }");
            Layer imageLayer = new ColorFillLayerBehaviour(); imageLayer.fx.Add(imageFX); imageLayer.fx.Add(displacement);
            doc.layers.Add(imageLayer); doc.layers.Add(child);
            P(displacement, "_DisplacementMap").textureSource = ShaderFXTextureSource.Layer;
            P(displacement, "_DisplacementMap").textureLayerId = child.Id;
            Set(displacement, "_StrengthX", 4); Set(displacement, "_StrengthY", 4);
            var warpedPacked = Composite();
            int middle = H / 2 * W + W / 2;
            t.Near(pixels[middle].r + .4f * 4 / W, warpedPacked[middle].r, .0004, "Generated packed map drives expected X sample offset");
            t.Near(pixels[middle].g - .25f * 4 / H, warpedPacked[middle].g, .0004, "Generated packed map drives expected Y sample offset");
            Set(generator, "_Output", 1); Set(displacement, "_Neutral", 0); Set(displacement, "_StrengthX", 2); Set(displacement, "_StrengthY", 2);
            Same(warpedPacked, Composite(), "Signed map with zero neutral and half strengths matches packed map");
            P(generator, "_HeightMap").textureSource = ShaderFXTextureSource.Layer;
            Layer externalHeight = new ColorFillLayerBehaviour(); externalHeight.colorRange = LayerColorRange.HDR; externalHeight.fx.Add(heightFX); externalHeight.enabled = false;
            doc.layers.Add(externalHeight);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
            t.True(!string.IsNullOrEmpty(externalHeight.Id), "External height reference has a real model ID");
            P(generator, "_HeightMap").textureLayerId = externalHeight.Id;
            child.fx.Remove(heightFX);
            Same(warpedPacked, Composite(), "External disabled height layer input");

            string folder = Path.Combine("Temp/WhimTex/height-to-distortion", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var preview = Own(new Texture2D(W * 3, H, TextureFormat.RGBA32, false, true));
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                preview.SetPixel(x, y, pixels[y * W + x]); preview.SetPixel(W + x, y, baseline[y * W + x]); preview.SetPixel(2 * W + x, y, warpedPacked[y * W + x]);
            }
            preview.Apply(); File.WriteAllBytes(Path.Combine(folder, "source-map-warped.png"), preview.EncodeToPNG());
            foreach (var message in ShaderUtil.GetShaderMessages((Shader)typeof(ShaderFX).GetField("compiledShader", F).GetValue(generator)))
                t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error && message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    });
}
