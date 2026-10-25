using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class ColorEncodingTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Run() => TestContext.Run("Linear/Gamma encoding contract and rendering", t =>
    {
        var owned = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = doc.height = 32;
            var noise = new NoiseLayerBehaviour(); doc.layers.Add(noise);
            void Normalize() => typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
            Color[] Render(Layer layer)
            {
                Normalize();
                var rt = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreview", F).Invoke(doc, new object[] { layer, 32 });
                var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
                try { RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); return read.GetPixels(); }
                finally { RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(read); RenderTexture.ReleaseTemporary(rt); }
            }
            t.Equal(NoiseLayerBehaviour.OutputEncoding.Linear, noise.encoding, "Noise default remains linear");
            Normalize();
            var linear = Render(noise.Owner); noise.encoding = NoiseLayerBehaviour.OutputEncoding.Gamma;
            var gamma = Render(noise.Owner);
            for (int i = 0; i < linear.Length; i++) t.Near(Mathf.GammaToLinearSpace(linear[i].r), gamma[i].r, .003, "Noise Gamma decodes the same raw sample");

            var normal = new NormalMapLayerBehaviour { smoothing = 0, inputSpace = NormalMapLayerBehaviour.InputSpace.Linear,
                encoding = NormalMapLayerBehaviour.OutputEncoding.Linear, output = NormalMapLayerBehaviour.OutputMode.Height };
            var source = Own(new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true));
            source.SetPixels(Enumerable.Repeat(new Color(.25f, .25f, .25f, .4f), 1024).ToArray()); source.Apply();
            doc.layers.Clear(); doc.layers.Add(normal); doc.layers.Add(new FileLayerBehaviour { sourceTexture = source }); Normalize();
            t.Equal(NormalMapLayerBehaviour.InputSpace.Gamma, new NormalMapLayerBehaviour().inputSpace, "Normal input default remains Gamma");
            t.Equal(NormalMapLayerBehaviour.OutputEncoding.Gamma, new NormalMapLayerBehaviour().encoding, "Normal output default remains Gamma");
            t.Near(.25, Render(normal.Owner)[528].r, .003, "Linear height input/output");
            normal.inputSpace = NormalMapLayerBehaviour.InputSpace.Gamma;
            t.Near(Mathf.LinearToGammaSpace(.25f), Render(normal.Owner)[528].r, .003, "Gamma height input");
            normal.encoding = NormalMapLayerBehaviour.OutputEncoding.Gamma;
            t.Near(.25, Render(normal.Owner)[528].r, .003, "Gamma height output decodes once");
            normal.output = NormalMapLayerBehaviour.OutputMode.Normal;

            ShaderFX FX(string filename)
            {
                var method = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
                var fx = Own((ShaderFX)method.Invoke(null, new object[] { doc, File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + filename), new List<ShaderFXParameter>() }));
                typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
                return fx;
            }
            List<ShaderFXParameter> Parameters(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            ShaderFXParameter P(ShaderFX fx, string name) => Parameters(fx).Single(p => p.name == name);
            void Changed(ShaderFX fx) => typeof(ShaderFX).GetMethod("NotifyValuesChanged", F).Invoke(fx, null);
            var lighting = FX("NormalLighting.hlsl"); var normalize = FX("Normalize.hlsl");
            foreach (var pair in new[] { (lighting, "_InputEncoding"), (normalize, "_Encoding") })
            {
                var p = P(pair.Item1, pair.Item2);
                t.True(p.controls.Any(c => c.type == ShaderFXParameterType.Enum), "Encoding is a dropdown control");
                t.Near(1, p.floatValue, 0, "FX default remains Gamma");
                t.True(p.controls.Any(c => c.optionNames.SequenceEqual(new[] { "Linear", "Gamma" }) && c.optionValues.SequenceEqual(new[] { 0f, 1f })), "Named encoding choices");
                t.True(Parameters(pair.Item1).All(p => p.name != "_PackedColor"), "No old parameter alias");
            }
            P(lighting, "_BaseColor").colorValue = Color.white;
            P(lighting, "_LightDirection").vectorValue = new Vector4(0, 0, 1, 0); P(lighting, "_Ambient").floatValue = 0;
            foreach (bool encoded in new[] { false, true })
            {
                normal.encoding = encoded ? NormalMapLayerBehaviour.OutputEncoding.Gamma : NormalMapLayerBehaviour.OutputEncoding.Linear;
                P(normalize, "_Encoding").floatValue = encoded ? 1 : 0; Changed(normalize);
                normal.Owner.fx.Clear(); var before = Render(normal.Owner)[528]; normal.Owner.fx.Add(normalize);
                var after = Render(normal.Owner)[528];
                t.Near(before.r, after.r, .003, "Normalize keeps a flat normal in " + normal.encoding);
                t.Near(before.g, after.g, .003, "Normalize green"); t.Near(before.b, after.b, .003, "Normalize blue");
                normal.Owner.fx.Clear(); normal.Owner.fx.Add(lighting);
                P(lighting, "_InputEncoding").floatValue = encoded ? 1 : 0; Changed(lighting);
                var lit = Render(normal.Owner)[528];
                t.Near(1, lit.r, .003, "Lighting reads flat normal in " + normal.encoding); t.Near(1, lit.a, .003, "Lighting alpha");
            }
            normal.Owner.fx.Clear(); doc.layers.Clear(); doc.layers.Add(noise); doc.layers.Add(normal); Normalize();
            foreach (var mode in new[] { WhimTexJsonWriteMode.Full, WhimTexJsonWriteMode.FullOptimized, WhimTexJsonWriteMode.Compact })
            {
                string json = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var copy = WhimTexDocumentJson.Read(json, false);
                t.Equal(noise.encoding, ((NoiseLayerBehaviour)copy.Document.layers[0].Behaviour).encoding, "Noise JSON " + mode);
                t.Equal(normal.encoding, ((NormalMapLayerBehaviour)copy.Document.layers[1].Behaviour).encoding, "Normal JSON " + mode);
                t.Equal(normal.inputSpace, ((NormalMapLayerBehaviour)copy.Document.layers[1].Behaviour).inputSpace, "Input JSON " + mode);
            }
            string full = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
            foreach (string old in new[] { "ColorValues", "PackedColor", "LinearData" })
            {
                bool rejected = false;
                try { using var copy = WhimTexDocumentJson.Read(full.Replace("\"Gamma\"", "\"" + old + "\""), false); }
                catch (WhimTexDocumentException) { rejected = true; }
                t.True(rejected, "Old JSON encoding rejected: " + old);
            }
            var serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
            using var container = new WhimTexDocumentContainer();
            object Native(object model)
            {
                var bytes = (byte[])serializer.GetMethod("Serialize", F).Invoke(null, new[] { model, container });
                var result = serializer.GetMethod("Deserialize", F).Invoke(null, new object[] { bytes, container, model.GetType(), null, false });
                return result.GetType().GetProperty("Model", F).GetValue(result);
            }
            var restored = Own((WhimTexDocument)Native(doc));
            t.Equal(noise.encoding, ((NoiseLayerBehaviour)restored.layers[0].Behaviour).encoding, "Native Noise encoding");
            t.Equal(normal.inputSpace, ((NormalMapLayerBehaviour)restored.layers[1].Behaviour).inputSpace, "Native input encoding");
            foreach (object invalid in new object[] { new NoiseLayerBehaviour { encoding = (NoiseLayerBehaviour.OutputEncoding)123 },
                new NormalMapLayerBehaviour { inputSpace = (NormalMapLayerBehaviour.InputSpace)123 },
                new NormalMapLayerBehaviour { encoding = (NormalMapLayerBehaviour.OutputEncoding)123 } })
            {
                bool rejected = false;
                try { Native(invalid); } catch (TargetInvocationException e) { rejected = e.InnerException is WhimTexDocumentException; }
                t.True(rejected, "Native encoding has no numeric fallback");
            }
        }
        finally
        {
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    });
}
