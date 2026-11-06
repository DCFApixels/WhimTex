using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class SpotLightingTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static ShaderParameter Param(ShaderFX fx, string name) => ((List<ShaderParameter>)fx.GetType().GetProperty("parameters", F).GetValue(fx)).Find(p => p.name == name);
    static Color[] Read(RenderTexture rt)
    {
        var prior = RenderTexture.active;
        var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); cpu.Apply(); return cpu.GetPixels(); }
        finally { RenderTexture.active = prior; Object.DestroyImmediate(cpu); }
    }
    static Color[] Full(WhimTexDocument doc)
    { var cpu = doc.ComposeCanvas(); try { return cpu.GetPixels(); } finally { Object.DestroyImmediate(cpu); } }
    // Independent angle-domain reference; the shader interpolates cosines uploaded by C#.
    static float Attenuation(WhimTexLight light, Vector3 point)
    {
        if (!light.enabled) return 0;
        Vector3 ray = point - light.position;
        float radius = ray.magnitude;
        double theta = Math.Acos(Math.Clamp(Vector3.Dot(ray.normalized, light.direction.normalized), -1, 1));
        double outer = light.angle * Math.PI / 360, inner = outer * (1 - light.softness);
        double edge = theta >= outer ? 0 : theta <= inner ? 1 :
            (Math.Cos(theta) - Math.Cos(outer)) / (Math.Cos(inner) - Math.Cos(outer));
        if (light.softness > 0) edge = edge * edge * (3 - 2 * edge);
        return Mathf.Pow(Mathf.Clamp01(1 - radius / light.range), light.falloff) * (float)edge;
    }
    static void Near(TestContext t, Color expected, Color actual, string label)
    { for (int i = 0; i < 4; i++) t.Near(expected[i], actual[i], .012, label + " channel " + i); }

    public static string Run() => TestContext.Run("Spot cone, shared FX/graph lighting, HDR, persistence and 8K regions", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width = doc.height = 128;
        var effects = new List<ShaderFX>();
        var prior = RenderTexture.active; bool srgb = GL.sRGBWrite;
        using var cache = (IDisposable)Activator.CreateInstance(T("EffectRenderCache"), true);
        ShaderFX FX(string code)
        {
            var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>) }, null)
                .Invoke(null, new object[] { doc, code, new List<ShaderParameter>() });
            effects.Add(fx); Call(fx, "CompileAgentDraft");
            t.Equal("Compiled successfully.", (string)fx.GetType().GetProperty("Diagnostics", F).GetValue(fx), "FX compiles without warnings");
            return fx;
        }
        Color[] Region(RectInt rect)
        {
            object region = Activator.CreateInstance(T("CanvasRenderRegion"), F, null, new object[] { rect, 1 }, null);
            var rt = (RenderTexture)Call(doc, "RenderCanvasViewport", region, cache, false, null);
            try { t.Equal(rect.width, rt.width, "Native region width"); t.Equal(rect.height, rt.height, "Native region height"); return Read(rt); }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }
        try
        {
            using (var defaults = WhimTexDocumentJson.Read("{\"format\":\"" + WhimTexDocumentJson.Format + "\",\"version\":" + WhimTexDocumentJson.Version +
                ",\"document\":{\"lights\":[{\"id\":\"spot-default\",\"type\":\"Spot\"}]},\"layers\":[]}"))
            {
                t.Equal(Color.white, defaults.Document.ambientColor, "Omitted JSON ambient stays neutral");
                t.Equal(Vector3.back, defaults.Document.lights[0].direction, "Omitted JSON Spot direction aims at canvas");
                t.Equal(60f, defaults.Document.lights[0].angle, "Omitted JSON Spot angle");
                t.Equal(.25f, defaults.Document.lights[0].softness, "Omitted JSON Spot softness");
            }
            var spot = doc.AddLight(WhimTexLightType.Spot);
            t.Equal(Vector3.back, spot.direction, "New Spot aims into the canvas");
            spot.position = new Vector3(52, 70, 32); spot.color = new Color(3, 2, .5f); spot.intensity = 2; spot.range = 160;
            var radiance = FX("float4 ApplyFX(PinnedUV uv,float4 c){float3 rgb=0; for(int i=0;i<WTLightCount();i++){WTLightSample l=WTSampleLight(i,float3((float2)uv*_CanvasSize.xy,0));rgb+=l.color*l.attenuation;}return float4(rgb,1);}");
            t.True(radiance.IsPixelLocal, "Spot sampling stays pixel-local");
            var host = new Layer(new ColorFillLayerBehaviour { color = Color.white }) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            doc.layers.Add(host); host.fx.Add(radiance);
            Color Expected(float x, float y)
            {
                var rgb = Color.black;
                foreach (var source in doc.lights) rgb += source.color * (source.intensity * Attenuation(source, new Vector3(x, y, 0)));
                rgb.a = 1; return rgb;
            }
            foreach (var axis in new[] { Vector3.back, new Vector3(.4f, -.2f, -1), Vector3.forward, Vector3.right })
            foreach (float soft in new[] { 0f, .4f, 1f })
            {
                spot.direction = axis; spot.softness = soft; spot.angle = 70;
                var pixels = Full(doc);
                for (int y = 4; y < 128; y += 11) for (int x = 3; x < 128; x += 13)
                    Near(t, Expected(x + .5f, y + .5f), pixels[y * 128 + x], "Cone direction and edge");
            }
            spot.direction = Vector3.back; spot.angle = 60; spot.softness = .25f;
            var full = Full(doc); t.True(full[70 * 128 + 52].r > 1, "HDR radiance survives");
            var regionRect = new RectInt(36, 50, 40, 40); var crop = Region(regionRect);
            for (int y = 0; y < 40; y += 3) for (int x = 0; x < 40; x += 3)
                Near(t, full[(y + 50) * 128 + x + 36], crop[y * 40 + x], "Full and regional parity");
            spot.angle = 28; crop = Region(regionRect);
            Near(t, Expected(64.5f, 70.5f), crop[20 * 40 + 28], "Angle invalidates cached region");
            spot.angle = 90; spot.softness = .9f; crop = Region(regionRect);
            Near(t, Expected(64.5f, 70.5f), crop[20 * 40 + 28], "Softness invalidates cached region");
            var second = doc.AddLight(WhimTexLightType.Spot); second.position = new Vector3(75, 55, 50); second.color = Color.blue; second.intensity = 4;
            Near(t, Expected(64.5f, 64.5f), Full(doc)[64 * 128 + 64], "Multiple spots add");
            second.enabled = false; Near(t, Expected(64.5f, 64.5f), Full(doc)[64 * 128 + 64], "Disabled spot keeps slot but contributes nothing");
            foreach (string name in new[] { "NormalLighting", "BevelEmboss" })
            {
                var fx = FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + name + ".hlsl"));
                Param(fx, "_LightingMode").floatValue = 1; Param(fx, "_BaseColor").colorValue = Color.white;
                Param(fx, "_ShadowColor").colorValue = Color.black; Param(fx, "_Ambient").floatValue = 0;
                host.fx.Clear();
                if (name == "NormalLighting")
                { host.fx.Add(FX("float4 ApplyFX(float4 c){return float4(.5,.5,1,1);}")); Param(fx, "_InputEncoding").floatValue = 0; }
                host.fx.Add(fx);
                var expected = Expected(64.5f, 64.5f); float diffuse = (spot.position - new Vector3(64.5f, 64.5f, 0)).normalized.z;
                expected *= diffuse; expected.a = 1;
                Near(t, expected, Full(doc)[64 * 128 + 64], name + " shares cone attenuation");
                Param(fx, "_Ambient").floatValue = .5f;
                doc.ambientColor = new Color(2, .4f, .1f, 1);
                var tinted = expected + doc.ambientColor * .5f; tinted.a = 1;
                Near(t, tinted, Full(doc)[64 * 128 + 64], name + " uses HDR ambient tint with existing strength");
                doc.ambientColor = Color.white;
            }
            host.fx.Clear(); host.fx.Add(radiance);
            doc.ambientColor = new Color(2, .3f, .1f, 1);
            string json = WhimTexDocumentJson.Write(doc).Json;
            using (var decoded = WhimTexDocumentJson.Read(json))
            {
                t.Equal(spot.angle, decoded.Document.lights[0].angle, "JSON cone angle");
                t.Equal(spot.softness, decoded.Document.lights[0].softness, "JSON softness");
                t.Equal(spot.direction, decoded.Document.lights[0].direction, "JSON direction");
                t.Equal(doc.ambientColor, decoded.Document.ambientColor, "JSON HDR ambient color");
                Near(t, Expected(64.5f, 64.5f), Full(decoded.Document)[64 * 128 + 64], "JSON appearance");
            }
            using (var container = new WhimTexDocumentContainer())
            {
                var serializer = T("WhimTexDocumentSerializer");
                byte[] payload = (byte[])serializer.GetMethod("Serialize", F).Invoke(null, new object[] { doc, container });
                container.Set(WhimTexDocumentContainer.DocumentBlock, payload);
                var composite = doc.ComposeCanvas(); byte[] tiff;
                try { tiff = (byte[])T("WhimTexTiffCarrier").GetMethod("Write", F).Invoke(null, new object[] { container, composite, (bool?)false }); }
                finally { Object.DestroyImmediate(composite); }
                string path = Path.GetFullPath("Temp/WhimTex/spot-" + Guid.NewGuid().ToString("N") + ".tiff");
                File.WriteAllBytes(path, tiff);
                using var reopened = (WhimTexDocumentContainer)T("WhimTexTiffCarrier").GetMethod("OpenContainer", F).Invoke(null, new object[] { path });
                var result = serializer.GetMethod("Deserialize", F).Invoke(null, new object[] { reopened.Get(WhimTexDocumentContainer.DocumentBlock), reopened, typeof(WhimTexDocument), null, false });
                var restored = (WhimTexDocument)result.GetType().GetProperty("Model", F).GetValue(result);
                try
                {
                    t.Equal(JsonUtility.ToJson(spot), JsonUtility.ToJson(restored.lights[0]), "TIFF exact spot settings/HDR");
                    t.Equal(doc.ambientColor, restored.ambientColor, "TIFF HDR ambient color");
                }
                finally { Object.DestroyImmediate(restored); }
            }
            doc.width = doc.height = 8192; spot.position = new Vector3(4096, 4096, 1024); spot.range = 8192;
            regionRect = new RectInt(4072, 4064, 64, 64); crop = Region(regionRect);
            Near(t, Expected(4096.5f, 4096.5f), crop[32 * 64 + 24], "Spot uses native coordinates within 8K region");
            doc.width = doc.height = 128; spot.position = new Vector3(52, 70, 32); spot.range = 160;
            var graph = new ShaderGraph(); var node = new ShaderGraphNode { kind = ShaderGraphNodeKind.NormalLighting, option = 1 };
            node.inputs.Add(new ShaderGraphInput { port = "Ambient", type = ShaderGraphValueType.Float, value = Vector4.zero });
            var output = new ShaderGraphNode { kind = ShaderGraphNodeKind.Output }; graph.nodes.Add(node); graph.nodes.Add(output);
            graph.edges.Add(new ShaderGraphEdge { outputNode = node.id, inputNode = output.id, inputPort = "RGBA" });
            var shader = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
            doc.layers.Clear(); doc.layers.Add(new Layer(shader) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR });
            t.True((bool)Call(shader, "CompileGraph", doc), "Global Lighting graph compiles with Spot");
            var graphExpected = Expected(64.5f, 64.5f) * (spot.position - new Vector3(64.5f, 64.5f, 0)).normalized.z; graphExpected.a = 1;
            Near(t, graphExpected, Full(doc)[64 * 128 + 64], "Graph shares spot result");
            doc.lights.Clear(); node.inputs[0].value = Vector4.one;
            t.True((bool)Call(shader, "CompileGraph", doc), "Ambient-only graph compiles");
            Near(t, doc.ambientColor, Full(doc)[0], "Global graph ambient works with no light sources");
            host.fx.Clear(); host.fx.Add(FX("float4 ApplyFX(float4 c){return float4(WTAmbientColor(),1);}"));
            doc.layers.Clear(); doc.layers.Add(host);
            regionRect = new RectInt(16, 16, 32, 32);
            Near(t, doc.ambientColor, Region(regionRect)[0], "Custom shader reads ambient without explicit lights");
            doc.ambientColor = new Color(.1f, 2, .4f, 1);
            Near(t, doc.ambientColor, Region(regionRect)[0], "Ambient-only edit invalidates cached output");
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc, "Ambient test");
            doc.ambientColor = Color.black; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); t.Equal(new Color(.1f, 2, .4f, 1), doc.ambientColor, "Undo restores ambient HDR");
            Undo.PerformRedo(); t.Equal(Color.black, doc.ambientColor, "Redo restores ambient change");
            t.Equal(prior, RenderTexture.active, "Render target restored"); t.Equal(srgb, GL.sRGBWrite, "sRGB state restored");
        }
        finally
        {
            foreach (var fx in effects) if (fx != null) Object.DestroyImmediate(fx);
            Undo.ClearUndo(doc);
            Object.DestroyImmediate(doc); RenderTexture.active = prior; GL.sRGBWrite = srgb;
        }
    });
}
