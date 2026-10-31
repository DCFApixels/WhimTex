using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class HlslOnlyFXTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly A = typeof(ShaderFX).Assembly;
    static Type Type(string name) => A.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, F).Invoke(instance, args);
    static object Static(string type, string name, params object[] args) => Type(type).GetMethod(name, F).Invoke(null, args);

    public static string Run() => TestContext.Run("HLSL-only FX model, formats, UI and render consumers", test =>
    {
        var owned = new List<Object>();
        var active = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            test.Equal(typeof(List<ShaderFX>), typeof(Layer).GetField("fx").FieldType, "FX model cannot accept arbitrary Unity objects");
            test.True(typeof(ShaderFX).GetCustomAttribute<CreateAssetMenuAttribute>() == null, "No standalone ShaderFX asset creation menu");
            test.True(typeof(WhimTexDocument).GetMethod("EmbedShaderFX", F) == null, "External-reference embedding path removed");
            test.Equal(typeof(IReadOnlyList<ShaderFX>), Type("ShaderFXClipboard").GetProperty("Current", F).PropertyType, "FX clipboard is an HLSL-only list");
            test.Equal(typeof(ShaderFX), Type("ShaderFXClipboard").GetMethod("Copy", F).GetParameters()[1].ParameterType, "Clipboard cannot accept a Material");
            object preset = null;
            foreach (var entry in (IEnumerable)Static("ShaderFXCatalog", "GetEntries"))
            {
                string path = (string)entry.GetType().GetField("path", F).GetValue(entry);
                test.True(path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase), "Catalog only discovers HLSL presets");
                if (path.EndsWith("/HueSaturation.hlsl", StringComparison.OrdinalIgnoreCase)) preset = entry;
            }
            test.True(preset != null, "Bundled HLSL preset is discoverable");

            var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(doc);
            doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 16;
            doc.outputPrecision = WhimTexOutputPrecision.Float32;
            var group = new Layer(new GroupLayerBehaviour()) { blendMode = BlendMode.Normal };
            var bottom = new Layer(new ColorFillLayerBehaviour { color = Color.blue });
            var clipped = new Layer(new ColorFillLayerBehaviour { color = Color.red }) { clippingMask = true };
            group.children.Add(clipped); group.children.Add(bottom); doc.layers.Add(group);
            ShaderFX Fx(string code)
            {
                var parameters = (List<ShaderFXParameter>)Static("ShaderFXMetadata", "Parse", code, false, null);
                var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                    new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                    .Invoke(null, new object[] { doc, code, parameters });
                owned.Add(fx); Call(fx, "ApplyAgentDraft"); Call(doc, "AdoptAgentShaderFX", fx, null);
                return fx;
            }
            var gain = Fx("// @param float _Gain = 1 [0 .. 2]\nfloat4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return float4(c.b*_Gain,0,c.r,c.a);}");
            bottom.fx.Add(gain);
            clipped.fx.Add(Fx("float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return float4(0,0,1,c.a);}"));
            group.fx.Add(Fx("float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return float4(1-c.rgb,c.a);}"));
            Call(doc, "NormalizeModel");

            var view = (VisualElement)Activator.CreateInstance(Type("LayerShaderFXView"), F, null,
                new object[] { bottom, doc, new Action<string, Action>((label, action) => action()) }, null);
            test.True(view.Query<Button>().ToList().Exists(button => button.text == "+ Shader FX"), "Inline HLSL creation remains available");
            test.True(!view.Query<Button>().ToList().Exists(button => button.text.Contains("Reference")), "No Reference creation button");
            test.Equal(0, view.Query<ObjectField>().ToList().Count, "FX card does not offer an external object picker");

            Color Render(WhimTexDocument document)
            {
                var image = document.ComposeCanvas();
                try { return image.GetPixel(8, 8); } finally { Object.DestroyImmediate(image); }
            }
            void Same(Color expected, Color actual, string label)
            { for (int channel = 0; channel < 4; channel++) test.Near(expected[channel], actual[channel], .002, label + " channel " + channel); }
            // Primary endpoints avoid comparing linear floats to display-encoded 8-bit export values.
            clipped.enabled = false;
            var unmasked = Render(doc);
            test.True(unmasked.a > .99f && unmasked.r < .01f && unmasked.g > .99f && unmasked.b > .99f, "Layer and group HLSL execute in order");
            clipped.enabled = true;
            var baseline = Render(doc);
            test.True(baseline.a > .99f && baseline.g > .99f && baseline.r > .99f && baseline.b < .01f, "HLSL on the clipping child runs before composition");
            var exported = (Texture2D)Call(doc, "RenderPsdGroupContent", group);
            try { Same(baseline, exported.GetPixel(8, 8), "Group export uses the same HLSL stack"); }
            finally { Object.DestroyImmediate(exported); }
            var preview = (RenderTexture)Call(doc, "RenderLayerPreviewFallback", group, 16);
            try
            {
                var read = new Texture2D(preview.width, preview.height, TextureFormat.RGBAFloat, false, true);
                try
                {
                    RenderTexture.active = preview; read.ReadPixels(new Rect(0, 0, preview.width, preview.height), 0, 0); read.Apply();
                    Same(baseline, read.GetPixel(preview.width / 2, preview.height / 2), "Group thumbnail/preview retains HLSL");
                }
                finally { Object.DestroyImmediate(read); }
            }
            finally { RenderTexture.active = active; RenderTexture.ReleaseTemporary(preview); }
            var target = new Layer(new BlurLayerBehaviour { radius = 0, strength = 0, inputMode = EffectInputMode.Specific, TargetLayerId = group.Id });
            doc.layers.Insert(0, target); Call(doc, "NormalizeModel");
            Same(baseline, Render(doc), "Specific Target input retains the group's HLSL result");

            var presetLayer = new Layer(new ColorFillLayerBehaviour()) { enabled = false };
            var first = (ShaderFX)Static("ShaderFX", "FromCatalog", doc, preset);
            var second = (ShaderFX)Static("ShaderFX", "FromCatalog", doc, preset);
            owned.Add(first); owned.Add(second);
            Call(doc, "AdoptAgentShaderFX", first, null); Call(doc, "AdoptAgentShaderFX", second, null);
            presetLayer.fx.Add(first); presetLayer.fx.Add(second); doc.layers.Add(presetLayer); Call(doc, "NormalizeModel");
            test.True((bool)typeof(ShaderFX).GetProperty("IsCatalogLinked", F).GetValue(first), "Project HLSL source link remains available");
            var a = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(first);
            var b = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(second);
            float saved = b[1].floatValue; a[1].floatValue += 15;
            test.Near(saved, b[1].floatValue, 0, "Two uses of one HLSL preset do not share parameter values");
            test.True(!ReferenceEquals(a[1], b[1]), "HLSL source sharing does not share mutable parameters");

            string json = WhimTexDocumentJson.Write(doc).Json;
            using (var loaded = WhimTexDocumentJson.Read(json))
            {
                Same(baseline, Render(loaded.Document), "JSON restores inline HLSL pixels");
                var loadedFx = loaded.Document.layers[1].children[1].fx[0];
                test.True(!ReferenceEquals(gain, loadedFx), "JSON creates document-owned FX, not shared external objects");
                test.Equal(typeof(List<ShaderFX>), loaded.Document.layers[1].fx.GetType(), "JSON keeps a typed HLSL stack");
            }
            using (var container = new WhimTexDocumentContainer())
            {
                var bytes = (byte[])Static("WhimTexDocumentSerializer", "Serialize", doc, container);
                var info = Static("WhimTexDocumentSerializer", "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
                var copy = (WhimTexDocument)info.GetType().GetProperty("Model", F).GetValue(info); owned.Add(copy);
                void Restore(List<Layer> layers)
                {
                    if (layers == null) return;
                    foreach (var layer in layers)
                    {
                        foreach (var fx in layer.fx)
                        { Call(fx, "RestoreDocumentOwner", copy); Call(fx, "ApplyAgentDraft"); }
                        Restore(layer.children);
                    }
                }
                Restore(copy.layers); Call(copy, "NormalizeModel");
                Same(baseline, Render(copy), "Native document payload restores inline HLSL pixels");
            }

            foreach (string oldType in new[] { "UnityEngine.Material", "UnityEngine.Shader", typeof(ShaderFX).FullName })
            {
                string invalid = "{\"format\":\"whimtex.document\",\"version\":3,\"layers\":[{\"id\":\"legacy\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$asset\":{\"guid\":\"00000000000000000000000000000000\",\"localId\":\"0\",\"path\":\"Assets/RemovedFX.asset\",\"type\":\"" + oldType + "\"}}]}]}";
                bool rejected = false;
                try { using var unexpected = WhimTexDocumentJson.Read(invalid, false); }
                catch (WhimTexDocumentException error) { rejected = error.Message.Contains("External FX references are no longer supported"); }
                test.True(rejected, "JSON explicitly rejects external FX: " + oldType);
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var container = new WhimTexDocumentContainer())
            {
                // Old binary list with one external object reference, without creating any asset.
                writer.Write(2); writer.Write((byte)30); writer.Write(1); writer.Write((byte)27);
                writer.Write("00000000000000000000000000000000"); writer.Write(0L); writer.Flush();
                bool rejected = false;
                try { Static("WhimTexDocumentSerializer", "Deserialize", stream.ToArray(), container, typeof(List<ShaderFX>), null, false); }
                catch (TargetInvocationException error) { rejected = error.InnerException.Message.Contains("External FX references are no longer supported"); }
                test.True(rejected, "Native external FX references fail clearly before asset resolution");
            }
            test.Equal(active, RenderTexture.active, "Render consumers preserve active RT");
            test.Equal(srgb, GL.sRGBWrite, "Render consumers preserve color-space write state");
        }
        finally
        {
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }
    });
}
