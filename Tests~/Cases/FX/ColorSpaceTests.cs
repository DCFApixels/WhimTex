using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class ColorSpaceTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    const string Path = "Packages/com.dcfapixels.whimtex/src/FXPresets/ColorSpace.hlsl";

    static float Transfer(float x, bool decode)
    {
        double v = Math.Abs((double)x);
        double result = decode ? (v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4))
            : (v <= .0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - .055);
        return (float)(Math.Sign(x) * result);
    }

    static Color Reference(Color c, int from, int to)
    {
        if (from == to) return c;
        float alpha = c.a;
        if (from == 1) c = new Color(Transfer(c.r, false), Transfer(c.g, false), Transfer(c.b, false));
        if (from == 2) c = Color.HSVToRGB(Mathf.Repeat(c.r, 1), Mathf.Clamp01(c.g), Mathf.Max(0, c.b), true);
        if (to == 1) c = new Color(Transfer(c.r, true), Transfer(c.g, true), Transfer(c.b, true));
        if (to == 2)
        {
            Color.RGBToHSV(new Color(Mathf.Max(0, c.r), Mathf.Max(0, c.g), Mathf.Max(0, c.b)), out float h, out float s, out float v);
            c = new Color(h, s, v);
        }
        c.a = alpha;
        return c;
    }

    public static string Run() => TestContext.Run("Color Space GPU conversions, HDR, alpha, identity and roundtrips", t =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.width = 16; document.height = 1;
        ShaderFX fx = null, sourceFx = null;
        var input = new Texture2D(16, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
        var read = new Texture2D(16, 1, TextureFormat.RGBAFloat, false, true);
        var output = RenderTexture.GetTemporary(16, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            // Import only the new requested preset so its metadata and catalog entry are available.
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>), typeof(string) }, null);
            fx = (ShaderFX)create.Invoke(null, new object[] { document, File.ReadAllText(Path), new List<ShaderFXParameter>(), Path });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            t.Equal(2, parameters.Count, "Only input and output controls");
            var from = parameters.Find(p => p.name == "_InputSpace");
            var to = parameters.Find(p => p.name == "_OutputSpace");
            t.Equal(1f, from.floatValue, "Default input Gamma");
            t.Equal(0f, to.floatValue, "Default output Linear");
            t.True(fx.IsPixelLocal, "Color-only conversion remains pixel local");
            var context = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),
                document, null, 16, 1, 1f, true, true, null);
            Material material = null;
            Color[] Render(Color[] source, int inputSpace, int outputSpace)
            {
                from.floatValue = inputSpace; to.floatValue = outputSpace;
                input.SetPixels(source); input.Apply();
                material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", F).Invoke(fx, new[] { context });
                t.True(material != null && !ShaderUtil.ShaderHasError(material.shader), "Compiled material");
                GL.sRGBWrite = false; Graphics.Blit(input, output, material);
                RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, 16, 1), 0, 0); read.Apply();
                return read.GetPixels();
            }
            void Near(Color expected, Color actual, string label, bool exact = false)
            {
                for (int channel = 0; channel < 4; channel++)
                    t.Near(expected[channel], actual[channel], exact || channel == 3 ? 0 : Math.Max(.00001, Math.Abs(expected[channel]) * .00003), label + " channel " + channel);
            }
            var rgb = new[] {
                new Color(0,0,0,0), new Color(1,1,1,1), new Color(.5f,.5f,.5f,.37f),
                Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta,
                new Color(.25f,.5f,.75f,0), new Color(.0031308f,.04045f,.00001f,.5f),
                new Color(8,4,2,.73f), new Color(-2,.5f,4,.1f), new Color(-1,-2,-3,1),
                new Color(0,2,4,1), new Color(16,.125f,.75f,1) };
            var hsv = new[] {
                new Color(0,0,0,0), new Color(0,0,1,1), new Color(0,0,.5f,.37f),
                new Color(0,1,1,1), new Color(1f/3,1,1,1), new Color(2f/3,1,1,1),
                new Color(1f/6,1,1,1), new Color(.5f,1,1,1), new Color(5f/6,1,1,1),
                new Color(.6f,.7f,.75f,0), new Color(.21f,.5f,.00001f,.5f), new Color(.75f,.8f,8,.73f),
                new Color(-.25f,.5f,4,.1f), new Color(1.25f,2,2,1), new Color(.3f,-1,2,1), new Color(.5f,.5f,-1,1) };
            for (int i = 0; i < 3; i++) for (int o = 0; o < 3; o++)
            {
                Color[] source = i == 2 ? hsv : rgb;
                Color[] actual = Render(source, i, o);
                for (int p = 0; p < source.Length; p++) Near(Reference(source[p], i, o), actual[p], $"{i}->{o} pixel {p}", i == o);
            }
            foreach (var route in new[] { (0, 1), (1, 0), (0, 2), (1, 2) })
            {
                var restored = Render(Render(rgb, route.Item1, route.Item2), route.Item2, route.Item1);
                for (int p = 0; p < rgb.Length; p++)
                    if (route.Item2 != 2 || rgb[p].r >= 0 && rgb[p].g >= 0 && rgb[p].b >= 0)
                        Near(rgb[p], restored[p], $"Roundtrip {route} pixel {p}");
            }
            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning &&
                    message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
            bool found = false;
            foreach (var entry in (System.Collections.IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
                found |= (string)entry.GetType().GetField("menuPath", F).GetValue(entry) == "Color/Color Space";
            t.True(found, "New preset is available in Add FX catalog");

            // Exercise the preset through the ordinary group, preview, clipping and export paths.
            sourceFx = (ShaderFX)create.Invoke(null, new object[] { document,
                "float4 ApplyFX(float4 color) { return float4(.25,.5,.75,.37); }", new List<ShaderFXParameter>(), null });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(sourceFx, null);
            from.floatValue = 1; to.floatValue = 0;
            Layer child = new ColorFillLayerBehaviour(); child.fx.Add(sourceFx);
            Layer group = new GroupLayerBehaviour(); group.children.Add(child); group.fx.Add(fx); document.layers.Add(group);
            Color expected = Reference(new Color(.25f,.5f,.75f,.37f), 1, 0);
            void PipelineNear(Color actual, Color target, string label)
            {
                for (int c = 0; c < 4; c++) t.Near(target[c], actual[c], .003, label + " channel " + c);
            }
            var composite = document.ComposeCanvas();
            try { PipelineNear(composite.GetPixel(8,0), expected, "Group composite"); }
            finally { UnityEngine.Object.DestroyImmediate(composite); }
            var thumbnail = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderAgentLayerPreview", F).Invoke(document, new object[] { group, 16 });
            try
            {
                RenderTexture.active = thumbnail; read.ReadPixels(new Rect(0,0,16,1),0,0); read.Apply();
                PipelineNear(read.GetPixel(8,0), expected, "Layer thumbnail");
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
            var exported = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdGroupContent", F).Invoke(document, new object[] { group });
            try { PipelineNear(exported.GetPixel(8,0), expected.gamma, "Layered export"); }
            finally { UnityEngine.Object.DestroyImmediate(exported); }
            group.clippingMask = true;
            document.layers.Add(new ColorFillLayerBehaviour { color = Color.black });
            composite = document.ComposeCanvas();
            Color display = expected.gamma;
            try { PipelineNear(composite.GetPixel(8,0), new Color(display.r*.37f,display.g*.37f,display.b*.37f,1).linear, "Clipped group"); }
            finally { UnityEngine.Object.DestroyImmediate(composite); }

            group.fx.Clear();
            document.layers.Clear(); document.width = 32; document.height = 16;
            Color[] Canvas()
            {
                var image = document.ComposeCanvas();
                try { return image.GetPixels(); }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            void SameImage(Color[] a, Color[] b, string label)
            {
                t.Equal(a.Length, b.Length, label + " dimensions");
                double maximum = 0;
                for (int p = 0; p < a.Length; p++) for (int c = 0; c < 4; c++)
                    maximum = Math.Max(maximum, Math.Abs(a[p][c] - b[p][c]));
                // One path has an extra Half working-buffer roundtrip before the FX.
                t.Near(0, maximum, .002, label);
            }
            void CheckGenerator(Layer layer, Action<bool> setGamma, string label)
            {
                layer.fx.Clear(); setGamma(false); var linear = Canvas();
                setGamma(true); var gamma = Canvas();
                double difference = 0;
                for (int p = 0; p < linear.Length; p++) difference = Math.Max(difference, Math.Abs(linear[p].r - gamma[p].r));
                t.True(difference > .02, label + " fixture exercises nontrivial encoding");
                layer.fx.Add(fx); from.floatValue = 1; to.floatValue = 0;
                SameImage(linear, Canvas(), label + " Gamma + Gamma-to-Linear equals native Linear");
                setGamma(false); from.floatValue = 0; to.floatValue = 1;
                SameImage(gamma, Canvas(), label + " Linear + Linear-to-Gamma equals native Gamma");
                layer.fx.Clear();
            }
            var noise = new NoiseLayerBehaviour(); Layer noiseLayer = noise;
            document.layers.Add(noiseLayer);
            foreach (var type in new[] { NoiseLayerBehaviour.NoiseType.OpenSimplex2, NoiseLayerBehaviour.NoiseType.WhiteNoise, NoiseLayerBehaviour.NoiseType.BlueNoise })
            {
                noise.noiseType = type; noise.grainColor = NoiseLayerBehaviour.GrainColor.Color;
                CheckGenerator(noiseLayer, gamma => noise.encoding = gamma ? NoiseLayerBehaviour.OutputEncoding.Gamma : NoiseLayerBehaviour.OutputEncoding.Linear,
                    "Noise " + type);
            }
            noise.noiseType = NoiseLayerBehaviour.NoiseType.OpenSimplex2; noise.encoding = NoiseLayerBehaviour.OutputEncoding.Linear;
            var normal = new NormalMapLayerBehaviour { TargetLayerId = noiseLayer.Id,
                inputSpace = NormalMapLayerBehaviour.InputSpace.Linear, smoothing = 0, strength = 3 };
            Layer normalLayer = normal; document.layers.Insert(0, normalLayer);
            CheckGenerator(normalLayer, gamma => normal.encoding = gamma ? NormalMapLayerBehaviour.OutputEncoding.Gamma : NormalMapLayerBehaviour.OutputEncoding.Linear,
                "Normal Map");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            RenderTexture.ReleaseTemporary(output);
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            if (sourceFx != null) UnityEngine.Object.DestroyImmediate(sourceFx);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(read);
            UnityEngine.Object.DestroyImmediate(document);
        }
    });
}
