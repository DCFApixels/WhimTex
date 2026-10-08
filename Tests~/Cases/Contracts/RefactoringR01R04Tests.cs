// run_script entry RefactoringR01R04Tests.Run. Owned in-memory objects only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class RefactoringR01R04Tests
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object value, string name) => value.GetType().GetProperty(name, Any).GetValue(value);
    static object Call(Type type, string name, params object[] args) => type.GetMethod(name, Any).Invoke(null, args);
    static int checks;
    static void Check(bool value, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
    static IReadOnlyList<string> Diagnostics(object result, string name) => (IReadOnlyList<string>)Get(result, name);
    static object Read(byte[] bytes, WhimTexDocumentContainer container, Type type) =>
        Call(T("WhimTexDocumentSerializer"), "Deserialize", bytes, container, type, null, false);

    static void Channels()
    {
        Type panelType = T("LayerPreviewPanel");
        Type stateType = panelType.GetNestedType("ViewState", Any);
        var maskField = stateType.GetField("channelMask", Any);
        Check(stateType.GetField("channel", Any) == null, "Removed old channel state");
        Check((int)maskField.GetValue(Activator.CreateInstance(stateType, true)) == 15, "Default RGBA");
        foreach (int input in Enumerable.Range(0, 16).Concat(new[] { -1, 16, 25, int.MinValue, int.MaxValue }))
        {
            object state = Activator.CreateInstance(stateType, true);
            maskField.SetValue(state, input);
            var panel = (VisualElement)Activator.CreateInstance(panelType, Any, null, new[] { state }, null);
            try
            {
                int mask = input & 15;
                Check((int)maskField.GetValue(state) == mask, "Mask sanitization " + input);
                foreach (string channel in new[] { "r", "g", "b", "a" })
                {
                    int bit = 1 << Array.IndexOf(new[] { "r", "g", "b", "a" }, channel);
                    Check(panel.Q<Button>("layer-preview-channel-" + channel).ClassListContains("whimtex-channel-button--enabled") ==
                        ((mask & bit) != 0), "Button state " + input + "/" + channel);
                }
                string json = JsonUtility.ToJson(state);
                Check(!json.Contains("\"channel\""), "Only canonical channel field");
                object copy = JsonUtility.FromJson(json, stateType);
                Check((int)maskField.GetValue(copy) == mask, "View state roundtrip " + input);
            }
            finally { ((IDisposable)panel).Dispose(); }
        }
    }

    static void Kernel()
    {
        var material = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(Shader.Find("Hidden/WhimTex/GaussianBlur")));
        try
        {
            var shortBuffer = new Vector4[5];
            bool rejected = false;
            try { Call(T("GaussianKernel"), "Upload", material, .2f, 5, shortBuffer); }
            catch (TargetInvocationException error) { rejected = error.InnerException is ArgumentException; }
            Check(rejected, "Reject short first upload");
            Call(T("GaussianKernel"), "Upload", material, .2f, 5, new Vector4[128]);
            foreach (float support in new[] { 0f, 1f, 2f, 3f, 31f, 255f, 256f, 999f })
            foreach (float sigma in new[] { 0f, .1f, 1f, 10f, 100f })
            {
                Call(T("GaussianKernel"), "Set", material, sigma, support);
                int extent = Mathf.Clamp(Mathf.CeilToInt(support), 1, 256);
                double divisor = 2d * Math.Max(.0001d, sigma * sigma), total = 1;
                var expected = new List<Vector4>();
                for (int i = 1; i <= extent; i += 2)
                {
                    double a = Math.Exp(-(double)i*i/divisor), b = i+1 <= extent ? Math.Exp(-(double)(i+1)*(i+1)/divisor) : 0;
                    double weight = a+b;
                    expected.Add(new Vector4((float)(i+(weight>0 ? b/weight : 0)), (float)weight, 0, 0));
                    total += 2*weight;
                }
                Vector4[] actual = material.GetVectorArray("_Kernel");
                Check(actual.Length == 128, "Full material array");
                Check(material.GetInt("_PairCount") == expected.Count, "Active pair count");
                Check(material.GetFloat("_CenterWeight") == (float)(1/total), "Exact center weight");
                for (int i = 0; i < expected.Count; i++)
                {
                    Vector4 pair = expected[i]; pair.y /= (float)total;
                    Check(actual[i].Equals(pair), "Exact original double-precision formula " + i);
                }
            }
        }
        finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material); }
    }

    static void Encoding()
    {
        Type format = T("RasterImageFormat");
        Type windowFormat = typeof(WhimTexWindow).GetNestedType("TextureExportFormat", Any);
        var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(8, 4, TextureFormat.RGBAHalf, false, true));
        try
        {
            Color[] pixels = Enumerable.Range(0, 32).Select(i => new Color(i/16f, -.1f, .2f, i/31f)).ToArray();
            texture.SetPixels(pixels); texture.Apply();
            Color[] original = texture.GetPixels();
            foreach (string name in new[] { "Png", "Jpeg", "Tga", "Exr" })
            foreach (int quality in new[] { 25, 95 })
            foreach (Texture2D.EXRFlags flags in new[] { Texture2D.EXRFlags.CompressZIP, Texture2D.EXRFlags.OutputAsFloat })
            {
                int ownedBefore = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
                byte[] encoded = (byte[])Call(T("WhimTexRasterEncoder"), "Encode", texture, Enum.Parse(format, name), quality, flags);
                Check(Resources.FindObjectsOfTypeAll<Texture2D>().Length == ownedBefore, "Intermediate texture released");
                Check(texture != null && texture.GetPixels().SequenceEqual(original), "Input remains borrowed and unchanged");
                byte[] fromWindow = (byte[])Call(typeof(WhimTexWindow), "EncodeExportTextureWithOptions",
                    texture, Enum.Parse(windowFormat, name), quality, flags);
                Check(encoded.SequenceEqual(fromWindow), "Window encoder parity " + name);
                Texture2D ldr = null;
                try
                {
                    if (name != "Exr") ldr = (Texture2D)Call(T("HdrUtility"), "ToLdr", texture, name == "Jpeg");
                    byte[] expected = name == "Exr" ? texture.EncodeToEXR(flags) : name == "Png" ? ldr.EncodeToPNG() :
                        name == "Tga" ? ldr.EncodeToTGA() : ldr.EncodeToJPG(quality);
                    Check(encoded.SequenceEqual(expected), "Original encoder byte parity " + name);
                }
                finally { if (ldr != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(ldr); }
            }
            int beforeReject = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            bool rejected = false;
            try { Call(T("WhimTexRasterEncoder"), "Encode", texture, Enum.ToObject(format, 99), 95, Texture2D.EXRFlags.CompressZIP); }
            catch (TargetInvocationException error) { rejected = error.InnerException is ArgumentOutOfRangeException; }
            Check(rejected && Resources.FindObjectsOfTypeAll<Texture2D>().Length == beforeReject, "Invalid raster format does not allocate");
        }
        finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(texture); }
    }

    static byte[] Payload(bool warnings)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(2); writer.Write((byte)29); writer.Write(typeof(WhimTexDocument).FullName);
        writer.Write(warnings ? 5 : 1);
        writer.Write("width"); writer.Write((byte)6); writer.Write(8);
        if (warnings)
        {
            for (int i = 0; i < 2; i++) { writer.Write("unknownField"); writer.Write((byte)6); writer.Write(i); }
            writer.Write("layers"); writer.Write((byte)30); writer.Write(2);
            for (int i = 0; i < 2; i++)
            {
                writer.Write((byte)29); writer.Write(typeof(Layer).FullName); writer.Write(1);
                writer.Write("behaviour"); writer.Write((byte)29); writer.Write("Unavailable.RefactoringBehaviour"); writer.Write(0);
            }
            writer.Write("unknownReference"); writer.Write((byte)27); writer.Write(new string('f', 32)); writer.Write(987L);
        }
        writer.Flush(); return stream.ToArray();
    }

    [Serializable]
    public sealed class NestedRead : IWhimTexDocumentSerializable
    {
        public int tail;
        public static object Inner;
        public void WriteDocument(IWhimTexDocumentWriter writer) { writer.Begin(1); writer.Write("tail", tail); }
        public void ReadDocument(IWhimTexDocumentReader reader)
        {
            int count = reader.Count;
            for (int i = 0; i < count; i++)
            {
                if (reader.NextName() == "tail")
                {
                    using var container = new WhimTexDocumentContainer();
                    Inner = Read(new byte[] { 1, 0, 0, 0, 6, 42, 0, 0, 0 }, container, typeof(int));
                    tail = reader.ReadInt();
                }
                else reader.Skip();
            }
        }
    }

    static void ReaderDiagnostics()
    {
        using var container = new WhimTexDocumentContainer();
        object first = Read(Payload(true), container, typeof(WhimTexDocument));
        object second = null;
        try
        {
            second = Read(Payload(false), container, typeof(WhimTexDocument));
            Check(((WhimTexDocument)Get(second, "Model")).width == 8, "Clean model");
            foreach (string name in new[] { "SkippedFields", "MissingTypes", "UnresolvedReferences" })
            {
                Check(Diagnostics(second, name).Count == 0, "Clean diagnostics " + name);
                Check(Diagnostics(first, name).Count > 0, "Earlier diagnostics retained " + name);
                var list = (IList)Diagnostics(first, name);
                Check(list.IsReadOnly, "Immutable diagnostics " + name);
                bool rejected = false;
                try { list.Add("changed"); } catch (NotSupportedException) { rejected = true; }
                Check(rejected, "Cannot mutate diagnostic snapshot " + name);
            }
            Check(Diagnostics(first, "SkippedFields").Count == 2, "Unknown fields deduplicated");
            Check(Diagnostics(first, "MissingTypes").Count == 1, "Missing types deduplicated");
            Check(Diagnostics(first, "UnresolvedReferences")[0] == new string('f', 32)+":987", "Reference diagnostic preserved");
            bool failed = false;
            try { Read(Payload(true).Take(20).ToArray(), container, typeof(WhimTexDocument)); }
            catch (TargetInvocationException error) { failed = error.InnerException is WhimTexDocumentException; }
            Check(failed && Diagnostics(first, "MissingTypes").Count == 1, "Failed read cannot replace earlier diagnostics");
            int modelsBefore = Resources.FindObjectsOfTypeAll<WhimTexDocument>().Length;
            failed = false;
            try { Read(Payload(true).Concat(new byte[] { 255 }).ToArray(), container, typeof(WhimTexDocument)); }
            catch (TargetInvocationException error) { failed = error.InnerException is WhimTexDocumentException; }
            Check(failed && Resources.FindObjectsOfTypeAll<WhimTexDocument>().Length == modelsBefore, "Read failure releases constructed Unity objects");
            Check(Diagnostics(first, "SkippedFields").Count == 2, "Failure after construction leaves earlier snapshots intact");

            var known = (Dictionary<string, Type>)T("WhimTexDocumentSerializer").GetField("KnownTypes", Any).GetValue(null);
            known.TryGetValue(typeof(NestedRead).FullName, out Type previous);
            known[typeof(NestedRead).FullName] = typeof(NestedRead);
            try
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                writer.Write(2); writer.Write((byte)29); writer.Write(typeof(NestedRead).FullName); writer.Write(2);
                writer.Write("unknown"); writer.Write((byte)6); writer.Write(1);
                writer.Write("tail"); writer.Write((byte)6); writer.Write(123);
                writer.Flush();
                object outer = Read(stream.ToArray(), container, typeof(NestedRead));
                Check(((NestedRead)Get(outer, "Model")).tail == 123 && (int)Get(NestedRead.Inner, "Model") == 42, "Nested read stream alignment");
                Check(Diagnostics(outer, "SkippedFields").Single() == "NestedRead.unknown", "Nested read cannot clear outer diagnostics");
                Check(Diagnostics(NestedRead.Inner, "SkippedFields").Count == 0, "Nested read has its own diagnostics");
            }
            finally
            {
                if (previous == null) known.Remove(typeof(NestedRead).FullName); else known[typeof(NestedRead).FullName] = previous;
                NestedRead.Inner = null;
            }
        }
        finally
        {
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy((WhimTexDocument)Get(first, "Model"));
            if (second != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy((WhimTexDocument)Get(second, "Model"));
        }
    }

    static string ExecuteEditableCopy()
    {
        checks = 0;
        var source = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        WhimTexDocument copy = null;
        var pixels = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(8, 4, TextureFormat.RGBAHalf, false, true));
        try
        {
            source.width = 8; source.height = 4; source.name = "Independent copy";
            var drawing = new DrawingLayerBehaviour { brushSize = 17, colorRange = LayerColorRange.HDR };
            source.layers.Add(drawing);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Any).Invoke(source, null);
            Color[] values = Enumerable.Range(0, 32).Select(i => new Color(i/8f, .2f, -.3f, i/31f)).ToArray();
            pixels.SetPixels(values); pixels.Apply(); pixels.filterMode = FilterMode.Point; pixels.wrapModeU = TextureWrapMode.Repeat;
            typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", Any).Invoke(drawing, new object[] { pixels });
            copy = (WhimTexDocument)Call(typeof(WhimTexDocumentFile), "CreateEditableCopy", source);
            Check(copy != null && copy != source && !AssetDatabase.Contains(copy), "Independent in-memory model");
            Check(copy.name == source.name && copy.width == 8 && copy.height == 4, "Copy name and dimensions");
            Check(copy.layers[0].Id == source.layers[0].Id && !ReferenceEquals(copy.layers[0], source.layers[0]), "Layer identity and independent wrapper");
            var copiedDrawing = (DrawingLayerBehaviour)copy.layers[0].Behaviour;
            var copiedPixels = (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any).GetValue(copiedDrawing);
            Check(copiedPixels != pixels && copiedPixels.GetRawTextureData<byte>().ToArray().SequenceEqual(pixels.GetRawTextureData<byte>().ToArray()), "Independent exact HDR pixels");
            Check(copiedPixels.filterMode == FilterMode.Point && copiedPixels.wrapModeU == TextureWrapMode.Repeat && copiedDrawing.brushSize == 17, "Sampling and authoring values");
            copiedPixels.SetPixel(0, 0, Color.white); copiedPixels.Apply();
            Check(pixels.GetPixel(0, 0) != Color.white, "Editing copy cannot mutate source pixels");
            var warnings = typeof(WhimTexDocument).GetField("documentLoadWarning", Any);
            Check(string.IsNullOrEmpty((string)warnings.GetValue(copy)), "Complete copy has no load warning");
            warnings.SetValue(source, "unknown");
            bool rejected = false;
            try { Call(typeof(WhimTexDocumentFile), "CreateEditableCopy", source); }
            catch (TargetInvocationException error) { rejected = error.InnerException is WhimTexDocumentException; }
            Check(rejected, "Incomplete source still blocks lossy copy");
            return "PASS: " + checks + " editable-copy ownership/HDR/sampling/unknown-data guard checks.";
        }
        finally
        {
            if (copy != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(copy);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(source);
            if (pixels != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(pixels);
        }
    }

    static string ExecuteSharpenMaterialOrder()
    {
        checks = 0;
        Type materials = T("WhimTexMaterials");
        FieldInfo cache = materials.GetField("gaussianBlurMaterial", Any);
        object originalMaterial = cache.GetValue(null);
        var fresh = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(Shader.Find("Hidden/WhimTex/GaussianBlur")));
        var afterBrush = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(fresh.shader));
        var document = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var brushDoc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(33, 25, TextureFormat.RGBAFloat, false, true));
        RenderTexture active = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            Color[] pixels = Enumerable.Range(0, 33*25).Select(i => new Color(1.5f+(i%7)*.1f, (i%13)*.07f, .2f, (i%19)/18f)).ToArray();
            texture.SetPixels(pixels); texture.Apply();
            document.width = 33; document.height = 25;
            var sharpen = new SharpenLayerBehaviour { radius = 24, colorRange = LayerColorRange.HDR };
            document.layers.Add(sharpen);
            document.layers.Add(new FileLayerBehaviour { sourceTexture = texture, colorRange = LayerColorRange.HDR });
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Any).Invoke(document, null);
            Color[] Render()
            {
                var rt = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreview", Any).Invoke(document, new object[] { sharpen.Owner, 33 });
                var cpu = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(33, 25, TextureFormat.RGBAFloat, false, true));
                try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, 33, 25), 0, 0); return cpu.GetPixels(); }
                finally { RenderTexture.active = active; WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(cpu); }
            }
            cache.SetValue(null, fresh);
            Color[] expected = Render();
            cache.SetValue(null, afterBrush);
            brushDoc.width = brushDoc.height = 33;
            var drawing = new DrawingLayerBehaviour(); brushDoc.layers.Add(new Layer(drawing));
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Any).Invoke(brushDoc, null);
            typeof(DrawingLayerBehaviour).GetMethod("InitializeCanvas", Any).Invoke(drawing, new object[] { 33, 33 });
            typeof(DrawingLayerBehaviour).GetMethod("BlurSegment", Any).Invoke(drawing,
                new object[] { new Vector2(.5f,.5f), new Vector2(.5f,.5f), 33, 33, 16f, 1f, 1f, null, false });
            Check(afterBrush.GetVectorArray("_Kernel").Length == 128 && afterBrush.GetInt("_PairCount") == 5, "Cold brush capacity/pairs");
            Check(afterBrush.GetFloat("_CenterWeight") == .2f, "Brush center weight unchanged");
            Vector4[] brushKernel = afterBrush.GetVectorArray("_Kernel");
            float[] weights = { .12f, .10f, .08f, .06f, .04f };
            for (int i = 0; i < 5; i++) Check(brushKernel[i].y == weights[i], "Brush pair weight " + i);
            Color[] actual = Render();
            Check(afterBrush.GetVectorArray("_Kernel").Length == 128 && afterBrush.GetInt("_PairCount") == 12, "Sharpen uses full Gaussian capacity");
            for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                Check(float.IsFinite(actual[i][c]) && Math.Abs(actual[i][c]-expected[i][c]) < .00001f, "HDR Sharpen independent of first material user");
            Check(RenderTexture.active == active && GL.sRGBWrite == srgb, "Caller render state");
            return "PASS: " + checks + " cold Brush -> Sharpen HDR parity, brush weights, full capacity and render-state checks.";
        }
        finally
        {
            cache.SetValue(null, originalMaterial);
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            foreach (Object value in new Object[] { document, brushDoc, texture, fresh, afterBrush }) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(value);
        }
    }

    static string ExecuteExportApiParity()
    {
        checks = 0;
        string token = Guid.NewGuid().ToString("N");
        string path = WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder() + "/RefactoringExport-" + token + ".tiff";
        string prefix = WhimTex.Tests.UnityC.FixtureContext.Scope.RelativeTemp + "/export-" + token;
        var outputs = new List<string>();
        var document = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var jsonType = typeof(WhimTexApi).GetMethod("SetBrush", Any).GetParameters().Single(p => p.ParameterType.FullName == "Newtonsoft.Json.Linq.JObject").ParameterType;
        object Parse(string json) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
        object At(object json, string key) => json.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(json, new object[] { key });
        try
        {
            document.width = 8; document.height = 4;
            document.layers.Add(new ColorFillLayerBehaviour { color = new Color(2f, .3f, .7f, .4f), colorRange = LayerColorRange.HDR });
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Any).Invoke(document, null);
            WhimTexDocumentFile.Save(document, path);
            Type format = T("RasterImageFormat");
            foreach (int maxSize in new[] { 0, 4 })
            foreach (string extension in new[] { "png", "jpg", "jpeg", "tga", "exr" })
            {
                string output = prefix + "-" + maxSize + "." + extension;
                outputs.Add(output);
                object reply = Parse(WhimTexApi.Export(path, output, maxSize));
                Check(bool.Parse(At(reply, "success").ToString()), "API export success: " + reply);
                Check(int.Parse(At(reply, "width").ToString()) == (maxSize == 0 ? 8 : 4) &&
                    int.Parse(At(reply, "height").ToString()) == (maxSize == 0 ? 4 : 2), "API scaled dimensions");
                string name = extension == "png" ? "Png" : extension == "tga" ? "Tga" : extension == "exr" ? "Exr" : "Jpeg";
                using var loaded = new ModelOwner(WhimTexDocumentFile.Load(path));
                Texture2D image = maxSize == 0 ? loaded.Document.ComposeCanvas() :
                    (Texture2D)typeof(WhimTexDocument).GetMethod("ComposeCanvas", Any, null, new[] { typeof(int) }, null).Invoke(loaded.Document, new object[] { maxSize });
                try
                {
                    byte[] expected = (byte[])Call(T("WhimTexRasterEncoder"), "Encode", image, Enum.Parse(format, name), 95, Texture2D.EXRFlags.CompressZIP);
                    byte[] bytes = File.ReadAllBytes(output);
                    Check(bytes.SequenceEqual(expected), "API and shared encoder byte parity " + extension);
                    reply = Parse(WhimTexApi.Export(path, output, maxSize));
                    Check(!bool.Parse(At(reply, "success").ToString()) && At(reply, "errorCode").ToString() == "already_exists", "API overwrite guard");
                    Check(File.ReadAllBytes(output).SequenceEqual(bytes), "Rejected overwrite preserves bytes");
                }
                finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image); }
            }
            object invalid = Parse(WhimTexApi.Export(path, WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder() + "/InvalidRefactoringExport.png"));
            Check(!bool.Parse(At(invalid, "success").ToString()) && At(invalid, "errorCode").ToString() == "invalid_path", "API path boundary unchanged");
            return "PASS: " + checks + " API raster parity/dimensions/path/overwrite checks for PNG, JPG, JPEG, TGA and EXR.";
        }
        finally
        {
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(document);
            WhimTex.Tests.UnityC.FixtureContext.Scope.DeleteAsset(path);
            foreach (string output in outputs) if (File.Exists(output)) File.Delete(output);
        }
    }

    sealed class ModelOwner : IDisposable
    {
        internal WhimTexDocument Document { get; }
        internal ModelOwner(WhimTexDocument document) { Document = document; }
        public void Dispose() => WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(Document);
    }

    static string ExecuteRun()
    {
        checks = 0;
        Channels(); Kernel(); Encoding(); ReaderDiagnostics();
        return "PASS: " + checks + " R01-R04 checks; 16 masks, kernel formula/capacity, encoder parity/ownership, immutable and nested read diagnostics.";
    }

    public static string EditableCopy() => WhimTex.Tests.UnityC.FixtureContext.Run("RefactoringR01R04Tests.EditableCopy", () => { ExecuteEditableCopy(); });

    public static string SharpenMaterialOrder() => WhimTex.Tests.UnityC.FixtureContext.Run("RefactoringR01R04Tests.SharpenMaterialOrder", () => { ExecuteSharpenMaterialOrder(); });

    public static string ExportApiParity() => WhimTex.Tests.UnityC.FixtureContext.Run("RefactoringR01R04Tests.ExportApiParity", () => { ExecuteExportApiParity(); });

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("RefactoringR01R04Tests.Run", () => { ExecuteRun(); });
}
