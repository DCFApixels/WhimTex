using System;
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

public static class ExternalFileLayerTests
{
    [Serializable] public sealed class SourceSettings { public string source; }
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object target, string name, params object[] args) =>
        (target is Type type ? type : target.GetType()).GetMethod(name, F).Invoke(target is Type ? null : target, args);
    static void Assign(FileLayerBehaviour layer, WhimTexDocument doc, string path) => Call(layer, "AssignSourcePath", path, doc, true);
    static Texture2D Resolve(FileLayerBehaviour layer, WhimTexDocument doc) => (Texture2D)Call(layer, "ResolveSourceTexture", doc);
    static void Normalize(WhimTexDocument doc) => Call(doc, "NormalizeModel");
    static FileLayerBehaviour FileLayer(WhimTexDocument doc) => (FileLayerBehaviour)doc.layers[0].Behaviour;

    public static string Run() => TestContext.Run("External File sources: parsers, rendering, HDR, cache, persistence and Undo", t =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "WhimTexExternalTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var owned = new List<Object>();
        var files = new List<string>();
        Texture2D Create(bool hdr)
        {
            var texture = new Texture2D(4, 2, hdr ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, hdr);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(texture);
            var pixels = new Color[8];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = hdr
                ? new Color(i + .25f, -i * .125f, .5f, i / 7f)
                : new Color(i / 7f, (7 - i) / 7f, .25f, i / 7f);
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
        WhimTexDocument Document()
        {
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave; owned.Add(doc);
            doc.layers.Clear(); doc.layers.Add(new FileLayerBehaviour()); Normalize(doc); return doc;
        }
        string Save(string name, byte[] bytes)
        {
            string path = Path.Combine(directory, name); File.WriteAllBytes(path, bytes); files.Add(path); return path;
        }
        void Compare(Color[] expected, Color[] actual, double tolerance, string label)
        {
            t.Equal(expected.Length, actual.Length, label + " length");
            for (int i = 0; i < expected.Length; i++) for (int c = 0; c < 4; c++)
                t.Near(expected[i][c], actual[i][c], tolerance, label + " pixel " + i + " channel " + c);
        }
        try
        {
            var ldr = Create(false); var hdr = Create(true);
            string png = Save("цвет с пробелом.png", ldr.EncodeToPNG());
            string jpg = Save("photo.jpg", ldr.EncodeToJPG(100));
            string tga = Save("image.tga", ldr.EncodeToTGA());
            string exr = Save("hdr.exr", hdr.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            string tiff = Save("image.tiff", WhimTexTiffImage.Write(4, 2, ldr.GetPixels32()));
            string tif = Save("hdr.tif", WhimTexTiffImage.Write(4, 2, hdr.GetPixels()));
            var linear = new Texture2D(4, 2, TextureFormat.RGBA32, false, true); owned.Add(linear);
            linear.SetPixels32(ldr.GetPixels32()); linear.Apply();
            string linearTiff;
            using (var carrier = new WhimTexDocumentContainer())
                linearTiff = Save("linear.tiff", (byte[])Call(T("WhimTexTiffCarrier"), "Write", carrier, linear, false));
            // Independent 24-bit bottom-up BMP fixture, padded rows.
            byte[] bmp = new byte[54 + 24];
            void Int(int offset, int value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bmp, offset, 4);
            bmp[0] = 66; bmp[1] = 77; Int(2, bmp.Length); Int(10, 54); Int(14, 40); Int(18, 4); Int(22, 2);
            bmp[26] = 1; bmp[28] = 24; Int(34, 24);
            var bytes = ldr.GetPixels32();
            for (int i = 0; i < 8; i++) { bmp[54 + i * 3] = bytes[i].b; bmp[55 + i * 3] = bytes[i].g; bmp[56 + i * 3] = bytes[i].r; }
            string bitmap = Save("image.bmp", bmp);
            var document = Document(); var layer = FileLayer(document);
            foreach (string path in new[] { png, tga, tiff, jpg, bitmap, exr, linearTiff, tif })
            {
                Assign(layer, document, path); Normalize(document);
                var decoded = Resolve(layer, document);
                t.True(decoded != null && decoded.width == 4 && decoded.height == 2, "Decoder dimensions " + path);
                t.True(!AssetDatabase.Contains(decoded), "External input is not imported");
                t.True(layer.sourceTexture == null && layer.externalSourcePath == path, "Only the external link is stored");
                t.True(ReferenceEquals(decoded, Resolve(layer, document)), "Repeated resolves reuse the texture");
                t.True(ReferenceEquals(decoded, layer.GetPreviewTexture(16)), "Layer thumbnail resolves external input");
                if (path == linearTiff)
                {
                    t.True(!decoded.isDataSRGB, "Native TIFF carrier retains linear encoding");
                    Compare(linear.GetPixels(), decoded.GetPixels(), 1e-6, "Native linear TIFF");
                }
                if (path == png || path == tga || path == tiff) Compare(ldr.GetPixels(), decoded.GetPixels(), 1e-6, "LDR orientation/alpha");
                if (path == exr || path == tif)
                {
                    Compare(hdr.GetPixels(), decoded.GetPixels(), 1e-6, "Signed HDR/hidden RGB");
                    t.Equal(LayerColorRange.HDR, layer.colorRange, "HDR range promotion");
                }
                if (path == bitmap)
                {
                    Color[] expected = ldr.GetPixels(); for (int i = 0; i < expected.Length; i++) expected[i].a = 1;
                    Compare(expected, decoded.GetPixels(), 1e-6, "BMP orientation");
                }
            }
            layer.colorRange = LayerColorRange.Standard; layer.blendRange = LayerBlendRange.Standard;
            Assign(layer, document, tif);
            t.Equal(LayerColorRange.Standard, layer.colorRange, "Same HDR path preserves manual range");
            Assign(layer, document, png); Normalize(document);
            var control = Document();
            Call(FileLayer(control), "AssignSourceTexture", ldr, control, true); Normalize(control);
            // The independent control owns the same source pixels and survives external cache eviction.
            FileLayer(control).colorRange = layer.colorRange; FileLayer(control).blendRange = layer.blendRange;
            var actualRender = document.ComposeCanvas(); var expectedRender = control.ComposeCanvas();
            owned.Add(actualRender); owned.Add(expectedRender);
            Compare(expectedRender.GetPixels(), actualRender.GetPixels(), 1e-6, "Composition parity");
            var ui = new VisualElement();
            var bindings = Activator.CreateInstance(T("WhimTexUI+ValueBindings"), true);
            Call(typeof(FileLayerEditorWindow), "BuildFields", ui, layer, document,
                new Action<string, Action>((_, action) => action()), bindings);
            var sourceField = ui.Q<ObjectField>();
            t.True(sourceField != null && sourceField.enabledInHierarchy && sourceField.value == Resolve(layer, document), "Native source field displays decoded external image");
            t.True(ui.Q<Button>()?.text == "Browse…", "External file picker is available");
            var jsonObject = Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true);
            var settings = jsonObject.GetMethod("Parse", new[] { typeof(string) }).Invoke(null,
                new object[] { JsonUtility.ToJson(new SourceSettings { source = tif }) });
            Call(typeof(WhimTexApi), "SetLayer", document, document.layers[0], settings);
            Normalize(document);
            var snapshot = Call(typeof(WhimTexApi), "Snapshot", document, "");
            string Value(string path) => snapshot.GetType().GetMethod("SelectToken", new[] { typeof(string) }).Invoke(snapshot, new object[] { path }).ToString();
            t.Equal(tif, Value("layers[0].settings.source"), "Agent API keeps absolute source path");
            t.Equal("4", Value("layers[0].sourceSize[0]"), "Agent API resolves external source dimensions");
            Assign(layer, document, png); Normalize(document);
            var externalRoot = document.layers[0]; var controlRoot = control.layers[0];
            void RenderParity(string label)
            {
                controlRoot.colorRange = externalRoot.colorRange; controlRoot.blendRange = externalRoot.blendRange;
                Normalize(document); Normalize(control);
                var actual = document.ComposeCanvas(); var expected = control.ComposeCanvas();
                try { Compare(expected.GetPixels(), actual.GetPixels(), 1e-6, label); }
                finally { Object.DestroyImmediate(actual); Object.DestroyImmediate(expected); }
            }
            foreach (GroupCompositing mode in new[] { GroupCompositing.PassThrough, GroupCompositing.Isolated })
            {
                var a = new GroupLayerBehaviour { compositing = mode }; a.layers.Add(externalRoot);
                var b = new GroupLayerBehaviour { compositing = mode }; b.layers.Add(controlRoot);
                document.layers.Clear(); document.layers.Add(a); control.layers.Clear(); control.layers.Add(b);
                RenderParity("Group " + mode);
            }
            document.layers.Clear(); document.layers.Add(externalRoot); control.layers.Clear(); control.layers.Add(controlRoot);
            externalRoot.clippingMask = controlRoot.clippingMask = true;
            document.layers.Add(new ColorFillLayerBehaviour { color = new Color(.2f, .5f, .8f, .4f) });
            control.layers.Add(new ColorFillLayerBehaviour { color = new Color(.2f, .5f, .8f, .4f) });
            RenderParity("Clipping");
            externalRoot.clippingMask = controlRoot.clippingMask = false;
            document.layers.RemoveAt(1); control.layers.RemoveAt(1);
            document.layers.Insert(0, new OutlineLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = externalRoot.Id, outlineWidth = 1 });
            control.layers.Insert(0, new OutlineLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = controlRoot.Id, outlineWidth = 1 });
            RenderParity("Target input");
            document.layers.RemoveAt(0); control.layers.RemoveAt(0); Normalize(document); Normalize(control);
            foreach (var mode in new[] { WhimTexJsonWriteMode.Full, WhimTexJsonWriteMode.Compact })
            {
                string json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var restored = WhimTexDocumentJson.Read(json, false);
                Normalize(restored.Document);
                t.Equal(png, FileLayer(restored.Document).externalSourcePath, "JSON keeps external path");
                t.True(FileLayer(restored.Document).sourceTexture == null, "JSON does not embed decoded pixels");
                Compare(ldr.GetPixels(), Resolve(FileLayer(restored.Document), restored.Document).GetPixels(), 1e-6, "JSON reload");
            }
            using (var container = new WhimTexDocumentContainer())
            {
                byte[] model = (byte[])Call(T("WhimTexDocumentSerializer"), "Serialize", document, container);
                t.True((bool)container.GetType().GetProperty("HasExternalInputs", F).GetValue(container), "External inputs disable unsafe unchanged-model save reuse");
                object result = Call(T("WhimTexDocumentSerializer"), "Deserialize", model, container, typeof(WhimTexDocument), null, false);
                var restored = (WhimTexDocument)result.GetType().GetProperty("Model", F).GetValue(result);
                owned.Add(restored); Normalize(restored);
                t.Equal(png, FileLayer(restored).externalSourcePath, "TIFF model keeps path");
                Compare(ldr.GetPixels(), Resolve(FileLayer(restored), restored).GetPixels(), 1e-6, "TIFF model reload");
            }
            string invalid = Save("unsupported.gif", new byte[] { 1, 2, 3 });
            bool rejected = false;
            try { Assign(layer, document, invalid); } catch (TargetInvocationException) { rejected = true; }
            t.True(rejected && layer.externalSourcePath == png, "Unsupported source leaves old link intact");
            string damaged = Save("damaged.png", new byte[] { 1, 2, 3 });
            rejected = false;
            try { Assign(layer, document, damaged); } catch (TargetInvocationException) { rejected = true; }
            t.True(rejected && layer.externalSourcePath == png, "Damaged image leaves old link intact");
            var previous = Resolve(layer, document);
            File.WriteAllBytes(png, Texture2D.whiteTexture.EncodeToPNG());
            File.SetLastWriteTimeUtc(png, DateTime.UtcNow.AddSeconds(3));
            Call(document, "RefreshExternalFiles");
            t.True(previous == null && Resolve(layer, document).width == Texture2D.whiteTexture.width, "Changed source replaces and releases decoded texture");
            File.Delete(png); Call(document, "RefreshExternalFiles");
            t.True(Resolve(layer, document) == null && !string.IsNullOrEmpty((string)Call(document, "ExternalFileError", png)), "Missing file reports error without losing its path");
            File.WriteAllBytes(png, ldr.EncodeToPNG()); Call(document, "RefreshExternalFiles");
            Compare(ldr.GetPixels(), Resolve(layer, document).GetPixels(), 1e-6, "Repaired source reloads");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(document, "Test external link");
            Assign(layer, document, tif); Normalize(document);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); Normalize(document);
            t.Equal(png, FileLayer(document).externalSourcePath, "Undo restores source link");
            Compare(ldr.GetPixels(), Resolve(FileLayer(document), document).GetPixels(), 1e-6, "Undo reloads disposed source");
            Undo.PerformRedo(); Normalize(document);
            t.Equal(tif, FileLayer(document).externalSourcePath, "Redo restores HDR link");
            Compare(hdr.GetPixels(), Resolve(FileLayer(document), document).GetPixels(), 1e-6, "Redo exact HDR");
            var released = Resolve(FileLayer(document), document);
            document.layers.Clear(); Normalize(document);
            t.True(released == null, "Removing last reference releases texture");
        }
        finally
        {
            Undo.FlushUndoRecordObjects();
            foreach (var item in owned) if (item != null) Undo.ClearUndo(item);
            // Destroy later-owned resources before their sources.
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            Undo.IncrementCurrentGroup();
            foreach (string path in files) if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory, false);
        }
    });
}
