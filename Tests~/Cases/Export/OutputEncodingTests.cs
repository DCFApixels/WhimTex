// Unity Pipeline run_script, entry OutputEncodingTests.Run. Own temporary assets only.
// Reflection is limited to WhimTex's test seams, never Unity internals.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class OutputEncodingTests
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type TypeOf(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object owner, string name, params object[] args) => type.GetMethod(name, Any).Invoke(owner, args);
    static object Field(object owner, string name) => owner.GetType().GetField(name, Any).GetValue(owner);
    static int checks;
    static void Check(bool yes, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(yes, message); checks++; }
    static TextureImporter Importer(string path) => (TextureImporter)AssetImporter.GetAtPath(path);
    static void Reencode(string path, bool srgb, WhimTexDocument owner)
    {
        try { Call(TypeOf("WhimTexOutputEncoding"), null, "Change", path, srgb, owner); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static Dictionary<string, byte[]> Blocks(string path)
    {
        using var c = (WhimTexDocumentContainer)Call(TypeOf("WhimTexTiffCarrier"), null, "OpenContainer", path);
        return c.Names.Where(n => n != "carrier" && n != "integrity:sha256").ToDictionary(n => n, n => c.Get(n));
    }
    static byte[] Pixels(string path)
    {
        Check(WhimTexTiffImage.TryReadPixels(File.ReadAllBytes(path), out _, out _, out byte[] bytes, out string error), error);
        return bytes;
    }
    static bool Stored(string path)
    {
        using var c = (WhimTexDocumentContainer)Call(TypeOf("WhimTexTiffCarrier"), null, "OpenContainer", path);
        return (c.Get("carrier")[0] & 1) != 0;
    }
    static Color[] GpuPixels(string path)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var rt = WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        var cpu = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true));
        var active = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            cpu.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            cpu.Apply(false, false);
            return cpu.GetPixels();
        }
        finally { RenderTexture.active = active; GL.sRGBWrite = srgb; WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(cpu); WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt); }
    }
    static async Task SetFromInspector(string path, bool value, System.Threading.CancellationToken token)
    {
        var importer = Importer(path);
        importer.sRGBTexture = value;
        importer.SaveAndReimport();
        for (int i = 0; i < 125 && Stored(path) != value; i++) await Task.Delay(40, token);
        Check(Stored(path) == value && Importer(path).sRGBTexture == value, "Inspector change re-encodes saved output");
    }
    static async Task Execute(System.Threading.CancellationToken token)
    {
        checks = 0;
        string dir = WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder();
        // WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder() created this unique owned folder.
        var owned = new List<Object>();
        var previousFocus = EditorWindow.focusedWindow;
        WhimTexWindow window = null;
        try
        {
            var doc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); owned.Add(doc);
            doc.width = doc.height = 16;
            var fill = new ColorFillLayerBehaviour { color = new Color(.75f, .43f, .17f, .61f) };
            doc.layers.Add(new Layer(fill));
            var drawing = new DrawingLayerBehaviour { brushColor = new Color(.2f, .4f, .8f, .3f), brushSize = 6, brushHardness = 1 };
            Call(drawing.GetType(), drawing, "PaintPoint", new Vector2(.5f, .5f), 16, 16,
                Call(drawing.GetType(), drawing, "GetStrokeParameters", false));
            Call(drawing.GetType(), drawing, "SyncSurfaceToTexture");
            doc.layers.Add(drawing);
            string path = WhimTexDocumentFile.Save(doc, dir + "/Color.tiff");
            Check(Importer(path).sRGBTexture && WhimTexDocumentFile.GetOutputSrgb(doc), "new TIFF defaults sRGB");
            string guid = AssetDatabase.AssetPathToGUID(path);
            var importer = Importer(path);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            var blocks = Blocks(path);
            byte[] original = Pixels(path);
            Color[] originalGpu = GpuPixels(path);

            // Register as an open document without touching any user's window.
            Call(TypeOf("WhimTexDocumentService"), null, "Attach", doc, doc);
            var binding = Field(doc, "documentBinding");
            binding.GetType().GetField("dirty").SetValue(binding, true);
            fill.color = Color.magenta;
            EditorUtility.SetDirty(doc);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Reencode(path, false, doc);
                Check(!Importer(path).sRGBTexture && !WhimTexDocumentFile.GetOutputSrgb(doc), "Linear UI/importer agree");
                byte[] linear = Pixels(path);
                Color[] linearGpu = GpuPixels(path);
                Check(originalGpu.Zip(linearGpu, (a, b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a))).Max() < .006f,
                    "Unity GPU sampling preserves decoded output");
                float maxError = 0;
                for (int i = 0; i < original.Length; i++)
                    if (i % 4 == 3) Check(original[i] == linear[i], "alpha is not gamma-converted");
                    else maxError = Mathf.Max(maxError, Mathf.Abs(Mathf.GammaToLinearSpace(original[i] / 255f) - linear[i] / 255f));
                Check(maxError < .006f, "sRGB/Linear preserve decoded appearance: " + maxError);
                Reencode(path, true, doc);
                Check(Pixels(path).SequenceEqual(original), "repeated toggling has no cumulative quantization");
            }
            foreach (var pair in Blocks(path)) Check(blocks[pair.Key].SequenceEqual(pair.Value), "source block unchanged: " + pair.Key);
            Check(fill.color == Color.magenta && EditorUtility.IsDirty(doc) && (bool)binding.GetType().GetField("dirty").GetValue(binding), "pending edits/dirty state intact");
            Check(AssetDatabase.AssetPathToGUID(path) == guid && !Importer(path).mipmapEnabled && Importer(path).wrapMode == TextureWrapMode.Repeat, "GUID/import settings intact");
            await SetFromInspector(path, false, token);
            await SetFromInspector(path, true, token);
            Check(Pixels(path).SequenceEqual(original), "Inspector uses saved model, not unsaved edits");
            long ticks = (long)binding.GetType().GetField("writeTicks").GetValue(binding);
            binding.GetType().GetField("writeTicks").SetValue(binding, ticks - 1);
            importer = Importer(path);
            importer.sRGBTexture = false;
            AssetDatabase.WriteImportSettingsIfDirty(path);
            bool rejected = false;
            try { Reencode(path, false, doc); }
            catch (WhimTexDocumentException) { rejected = true; }
            finally { binding.GetType().GetField("writeTicks").SetValue(binding, ticks); }
            Check(rejected && Importer(path).sRGBTexture && Pixels(path).SequenceEqual(original), "stale session failure restores importer without writing image");
            // Keep midtones in the subsequent UI Save fixture: 0 and 1 alone cannot detect gamma errors.
            fill.color = new Color(.63f, .31f, .14f, .61f);
            WhimTexDocumentFile.Save(doc, path);
            Check(!Pixels(path).SequenceEqual(original), "next normal Save commits edits without stale revision conflict");
            Call(TypeOf("WhimTexDocumentService"), null, "Detach", doc);

            var fresh = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); owned.Add(fresh);
            fresh.width = fresh.height = 4;
            fresh.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.gray }));
            WhimTexDocumentFile.SetOutputSrgb(fresh, false);
            string dataPath = WhimTexDocumentFile.Save(fresh, dir + "/Data.tiff");
            Check(!Importer(dataPath).sRGBTexture, "unsaved Linear preference honored on first save");
            fresh.outputPrecision = WhimTexOutputPrecision.Float32;
            WhimTexDocumentFile.Save(fresh, dataPath);
            byte[] floatBytes = Pixels(dataPath);
            WhimTexDocumentFile.SetOutputSrgb(fresh, true);
            Check(!Importer(dataPath).sRGBTexture && !WhimTexDocumentFile.GetOutputSrgb(fresh) && Pixels(dataPath).SequenceEqual(floatBytes), "Float32 always linear and pixels unchanged");
            var hdrImporter = Importer(dataPath);
            hdrImporter.sRGBTexture = true;
            hdrImporter.SaveAndReimport();
            for (int i = 0; i < 125 && Importer(dataPath).sRGBTexture; i++) await Task.Delay(40, token);
            Check(!Importer(dataPath).sRGBTexture && Pixels(dataPath).SequenceEqual(floatBytes), "Inspector cannot enable sRGB on Float32");
            DateTime floatTimestamp = File.GetLastWriteTimeUtc(dataPath);
            await Task.Delay(180, token);
            Check(File.GetLastWriteTimeUtc(dataPath) == floatTimestamp, "no recursive HDR encoding imports");

            string plainPath = dir + "/Plain.tiff";
            byte[] plain = WhimTexTiffImage.Write(2, 2, Enumerable.Repeat(new Color32(70, 100, 150, 255), 4).ToArray());
            File.WriteAllBytes(plainPath, plain);
            AssetDatabase.ImportAsset(plainPath, ImportAssetOptions.ForceSynchronousImport);
            var plainImporter = Importer(plainPath);
            plainImporter.sRGBTexture = false;
            plainImporter.SaveAndReimport();
            await Task.Delay(120, token);
            Check(!Importer(plainPath).sRGBTexture && File.ReadAllBytes(plainPath).SequenceEqual(plain), "ordinary source TIFF unaffected");

            // A real temporary UITK window, using only public Unity APIs.
            var restored = WhimTexDocumentFile.Load(path); owned.Add(restored);
            var lazyDrawing = (DrawingLayerBehaviour)restored.layers[1].Behaviour;
            Check((bool)typeof(DrawingLayerBehaviour).GetProperty("HasDeferredTexture", Any).GetValue(lazyDrawing), "Drawing initially lazy-loaded");
            Reencode(path, false, restored);
            Check((bool)typeof(DrawingLayerBehaviour).GetProperty("HasDeferredTexture", Any).GetValue(lazyDrawing), "output switch does not allocate hidden Drawing pixels");
            var lazyPixels = (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any).GetValue(lazyDrawing);
            var sourcePixels = (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any).GetValue(drawing);
            Check(lazyPixels.GetRawTextureData<byte>().ToArray().SequenceEqual(sourcePixels.GetRawTextureData<byte>().ToArray()), "deferred Drawing revision survives output rewrite");
            Reencode(path, true, restored);
            window = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
            window.Show();
            Call(typeof(WhimTexWindow), window, "SetDocument", restored);
            await Task.Delay(120, token);
            var toggle = window.rootVisualElement.Q<Toggle>("canvasOutputSrgb");
            Check(toggle != null && toggle.value, "sRGB toggle visible beside Precision");
            WhimTexDocumentFile.Save(restored, path); // Warm the no-op save cache.
            byte[] beforeToggle = File.ReadAllBytes(path);
            byte[] beforeTogglePixels = Pixels(path);
            Color[] beforeToggleGpu = GpuPixels(path);
            byte[] metaBeforeToggle = File.ReadAllBytes(path + ".meta");
            Undo.IncrementCurrentGroup();
            toggle.value = false;
            Check(Importer(path).sRGBTexture && !toggle.value && !WhimTexDocumentFile.GetOutputSrgb(restored), "UI edits pending encoding only");
            Check(window.hasUnsavedChanges && ((Button)Field(window, "toolkitSaveButton")).enabledSelf, "encoding edit enables Save and dirty indicator");
            Check(File.ReadAllBytes(path).SequenceEqual(beforeToggle) && File.ReadAllBytes(path + ".meta").SequenceEqual(metaBeforeToggle), "toggle leaves TIFF and meta untouched");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Check(WhimTexDocumentFile.GetOutputSrgb(restored) && File.ReadAllBytes(path).SequenceEqual(beforeToggle), "Undo restores pending setting without writing");
            Undo.PerformRedo();
            Check(!WhimTexDocumentFile.GetOutputSrgb(restored), "Redo restores pending Linear setting");
            window.SaveChanges();
            Check(!Importer(path).sRGBTexture && !Stored(path) && !window.hasUnsavedChanges, "Save applies encoding and clears dirty state");
            byte[] afterTogglePixels = Pixels(path);
            float saveEncodingError = 0;
            for (int i = 0; i < beforeTogglePixels.Length; i++)
            {
                float expected = i % 4 == 3 ? beforeTogglePixels[i] / 255f : Mathf.GammaToLinearSpace(beforeTogglePixels[i] / 255f);
                saveEncodingError = Mathf.Max(saveEncodingError, Mathf.Abs(expected - afterTogglePixels[i] / 255f));
            }
            Check(!beforeTogglePixels.SequenceEqual(afterTogglePixels) && saveEncodingError < .006f,
                "UI Save converts actual RGB samples, not just flags: " + saveEncodingError);
            Check(beforeToggleGpu.Zip(GpuPixels(path), (a, b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a))).Max() < .006f,
                "UI Save preserves decoded GPU values");
            Check(!((Button)Field(window, "toolkitSaveButton")).enabledSelf, "Save disabled again after successful save");
            var reopened = WhimTexDocumentFile.Load(path); owned.Add(reopened);
            Check(!WhimTexDocumentFile.GetOutputSrgb(reopened), "saved encoding survives reopen");
            DateTime noOpTimestamp = File.GetLastWriteTimeUtc(path);
            WhimTexDocumentFile.Save(restored, path);
            Check(File.GetLastWriteTimeUtc(path) == noOpTimestamp, "unchanged save still skips rewrite");
            toggle = window.rootVisualElement.Q<Toggle>("canvasOutputSrgb");
            toggle.value = true;
            window.DiscardChanges();
            Check(!Stored(path) && !Importer(path).sRGBTexture, "discard does not apply pending encoding");
            Undo.ClearUndo(restored);
            restored.outputPrecision = WhimTexOutputPrecision.Float32;
            Call(typeof(WhimTexWindow), window, "RefreshToolkitInterface", true);
            toggle = window.rootVisualElement.Q<Toggle>("canvasOutputSrgb");
            await Task.Delay(150, token);
            Check(toggle != null && !toggle.value && !toggle.enabledSelf, "Float32 disables sRGB control");
            return;
        }
        finally
        {
            if (window != null) { global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window); }
            foreach (var item in owned)
            {
                if (item is WhimTexDocument activeDocument) Call(TypeOf("WhimTexDocumentService"), null, "Detach", activeDocument);
                if (item != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(item);
            }
            WhimTex.Tests.UnityC.FixtureContext.Scope.DeleteAsset(dir);
            if (previousFocus != null) previousFocus.Focus();
        }
    }

    public static string Start(string runId)
    {
        var job = WhimTex.Tests.UnityC.AsyncFixture.Create(runId);
        job.Worker = WhimTex.Tests.UnityC.FixtureContext.RunAsync(job, Execute);
        return job.Read();
    }
    public static string Run(string runId) => Start(runId);
    public static string Poll(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Poll(runId);
    public static Task<string> Cancel(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cancel(runId);
    public static Task<string> Cleanup(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cleanup(runId);
}
