// run_script entry DocumentProductionSmoke.Run. Unique Assets folder, restored selection, cleanup in finally.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentProductionSmoke
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type T(string name) => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object instance, string name, params object[] args)
    {
        try { return type.GetMethods(Any).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(instance, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); checks++; }
    static void Reject(Action action, string message, bool cancellation = false)
    {
        try { action(); }
        catch (Exception e)
        {
            if (e is TargetInvocationException invocation && invocation.InnerException != null) e = invocation.InnerException;
            Check(cancellation ? e is OperationCanceledException : e is WhimTexDocumentException, message + ": " + e);
            return;
        }
        throw new Exception("FAIL: accepted " + message);
    }
    static IDisposable Operation(Func<string, float, bool> cancel) => (IDisposable)Activator.CreateInstance(
        T("WhimTexDocumentOperation"), Any, null, new object[] { "TIFF regression", cancel }, null);
    static int Bits(byte[] tiff) => BitConverter.ToUInt16(tiff, 8 + 2 + 12 * 12 + 4);
    static void Strips(int size, bool hdr, bool eight)
    {
        var texture = new Texture2D(size, size / 2 + 3, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, true);
        try
        {
            uint seed = 71;
            var raw = texture.GetRawTextureData<byte>();
            if (hdr)
            {
                var half = texture.GetRawTextureData<ushort>();
                for (int i = 0; i < half.Length; i++)
                { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; half[i] = (ushort)(0x3000 + seed % 8192); }
            }
            else for (int i = 0; i < raw.Length; i++) { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; raw[i] = (byte)seed; }
            int sourceBits = hdr ? 16 : 8, targetBits = eight ? 8 : 32;
            byte[] expected = WhimTexTiffImage.WriteRaw(texture.width, texture.height, raw.ToArray(), sourceBits, targetBits, eight && hdr);
            using var stream = new MemoryStream();
            Call(typeof(WhimTexTiffImage), null, "WriteRawTo", stream, texture.width, texture.height, raw, sourceBits, targetBits, eight && hdr, false);
            Check(expected.SequenceEqual(stream.ToArray()), "streamed TIFF byte-exact vs prior writer: " + size + "/" + sourceBits + "/" + targetBits);
            Call(typeof(WhimTexTiffImage), null, "ValidateStream", stream);
            stream.Position = stream.Length - 1; stream.WriteByte((byte)(stream.ToArray()[stream.Length - 1] ^ 1));
            Reject(() => Call(typeof(WhimTexTiffImage), null, "ValidateStream", stream), "damaged TIFF strip rejected");
        }
        finally { Object.DestroyImmediate(texture); }
    }

    public static string Run()
    {
        checks = 0;
        var session = T("WhimTexDocumentSession");
        if ((bool)session.GetProperty("IsLive", Any).GetValue(null)) return "SKIP: user Live Update is active.";
        var selection = Selection.objects;
        string folder = "Assets/WhimTexProduction_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        var owned = new List<Object>();
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); owned.Add(doc);
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = 32; doc.height = 16;
        var fill = new ColorFillLayerBehaviour { color = new Color(.5173f, .1317f, .0171f, .73f) };
        doc.layers.Add(new Layer(fill) { colorRange = LayerColorRange.HDR });
        try
        {
            Strips(33, false, true); Strips(1024, false, true);
            Strips(33, true, true); Strips(1024, true, true); Strips(1024, true, false);
            string path = folder + "/Precision.whimtex.tiff";
            string guid = null;
            foreach (WhimTexOutputPrecision precision in Enum.GetValues(typeof(WhimTexOutputPrecision)))
            {
                doc.outputPrecision = precision;
                WhimTexDocumentFile.Save(doc, path);
                guid ??= AssetDatabase.AssetPathToGUID(path);
                Check(AssetDatabase.AssetPathToGUID(path) == guid, "precision keeps GUID");
                Check(Bits(File.ReadAllBytes(path)) == (precision == WhimTexOutputPrecision.Float32 ? 32 : 8), "requested TIFF precision " + precision);
                var loaded = WhimTexDocumentFile.Load(path); owned.Add(loaded);
                Check(loaded.outputPrecision == precision, "precision persists");
            }
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).sRGBTexture, "float output imports linear");
            var reference = doc.Compose(); owned.Add(reference);
            byte[] actual = File.ReadAllBytes(path);
            byte[] expected = WhimTexTiffImage.WriteRaw(reference.width, reference.height, reference.GetRawTextureData<byte>().ToArray(), 16, 32);
            Check(WhimTexTiffImage.TryReadPixels(actual, out _, out _, out byte[] actualPixels, out _), "float pixels decoded");
            Check(WhimTexTiffImage.TryReadPixels(expected, out _, out _, out byte[] expectedPixels, out _) && expectedPixels.SequenceEqual(actualPixels), "float carrier keeps exact composed values within 0–1");
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            WhimTexDocumentFile.Save(doc, path);
            Check(timestamp == File.GetLastWriteTimeUtc(path), "precision no-op keeps timestamp");

            var floatImporter = (TextureImporter)AssetImporter.GetAtPath(path);
            floatImporter.sRGBTexture = true; AssetDatabase.WriteImportSettingsIfDirty(path);
            byte[] metaBefore = File.ReadAllBytes(path + ".meta");
            using (var operation = Operation((stage, _) => stage == "Finishing file write"))
                Reject(() => WhimTexDocumentFile.Save(doc, path), "cancel identical TIFF before correcting importer", true);
            Check(File.ReadAllBytes(path + ".meta").SequenceEqual(metaBefore), "cancelled identical file keeps importer settings");
            floatImporter.sRGBTexture = false; AssetDatabase.WriteImportSettingsIfDirty(path);

            fill.color = new Color(2, .2f, .3f, .4f);
            doc.outputPrecision = WhimTexOutputPrecision.EightBit;
            WhimTexDocumentFile.Save(doc, path);
            Check(Bits(File.ReadAllBytes(path)) == 8, "explicit 8-bit clamps HDR output");
            doc.outputPrecision = WhimTexOutputPrecision.Auto;
            WhimTexDocumentFile.Save(doc, path);
            Check(Bits(File.ReadAllBytes(path)) == 32, "Auto still chooses HDR when needed");
            byte[] saved = File.ReadAllBytes(path);
            fill.color = Color.green;
            Call(typeof(TextureCompositor), doc, "MarkChanged");
            foreach (string stage in new[] { "Checking document limits", "Rendering composite", "Encoding TIFF strips", "Writing document blocks", "Finishing file write" })
            {
                using var operation = Operation((s, _) => s == stage);
                Reject(() => WhimTexDocumentFile.Save(doc, path), "cancel " + stage, true);
                Check(File.ReadAllBytes(path).SequenceEqual(saved) && fill.color == Color.green, "cancel preserves saved and edited states");
                Check(!Directory.EnumerateFiles(folder, "*.whimtex-tmp").Any(), "cancel cleans only staging");
            }
            using (var operation = Operation((s, _) => s == "Checking document directory"))
                Reject(() => WhimTexDocumentFile.Load(path), "cancel open", true);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = false; importer.SaveAndReimport();
            Check((bool)Call(session, null, "Start", doc, path), "live fixture starts");
            using (var operation = Operation((s, _) => s == "Writing document blocks"))
                Reject(() => WhimTexDocumentFile.Save(doc, path), "cancel live save", true);
            Check((bool)Call(session, null, "IsLiveFor", doc), "live resumes after cancellation");
            Call(session, null, "Stop", "test");
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Read/Write restored after cancelled live save");

            doc.width = doc.height = 16384; doc.outputPrecision = WhimTexOutputPrecision.Float32;
            Reject(() => WhimTexDocumentFile.Save(doc, path), "oversize float canvas rejected before rendering");
            Check(File.ReadAllBytes(path).SequenceEqual(saved), "oversize request leaves saved file intact");
            doc.outputPrecision = WhimTexOutputPrecision.EightBit;
            Reject(() => WhimTexDocumentFile.Save(doc, path), "oversize working composite rejected even for 8-bit output");
            doc.width = 32; doc.height = 16;
            doc.layers.Add(doc.layers[0]);
            Reject(() => WhimTexDocumentFile.Save(doc, path), "duplicate/circular layer hierarchy rejected in preflight");
            doc.layers.RemoveAt(1);
            object[] budget = { 512L * 1024 * 1024, 0L, "8K HDR Drawing" };
            Reject(() => Call(T("WhimTexDocumentLimits"), null, "CheckTexture", budget), "8K HDR Drawing budget");

            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true); owned.Add(texture);
            using (var container = new WhimTexDocumentContainer())
            {
                byte[] model = (byte[])Call(T("WhimTexDocumentSerializer"), null, "Serialize", texture, container);
                Buffer.BlockCopy(BitConverter.GetBytes(16384), 0, model, 5, 4);
                Reject(() => Call(T("WhimTexDocumentSerializer"), null, "Deserialize", model, container, typeof(Texture2D), null, false), "dimension/payload mismatch before GPU allocation");
            }
            string staged = folder + "/Recovery.whimtex-tmp", recovered = folder + "/Recovered.tiff";
            File.WriteAllBytes(staged, saved);
            Call(T("WhimTexDocumentRecovery"), null, "RecoverTo", staged, recovered);
            Check(File.ReadAllBytes(recovered).SequenceEqual(saved) && File.Exists(staged), "recovery copies exact bytes and keeps source");
            Check(AssetDatabase.AssetPathToGUID(recovered) != guid, "recovery creates separate identity");
            Reject(() => Call(T("WhimTexDocumentRecovery"), null, "RecoverTo", staged, path), "recovery cannot overwrite existing document");
            File.WriteAllBytes(staged, saved.Take(saved.Length / 2).ToArray());
            Reject(() => Call(T("WhimTexDocumentRecovery"), null, "RecoverTo", staged, folder + "/Bad.tiff"), "truncated staged file rejected");
            Check(!File.Exists(folder + "/Bad.tiff"), "failed recovery creates no result");
            DrawingOpen(folder, owned);
            return "PASS: " + checks + " TIFF precision, streaming, cancellation, limits and recovery checks.";
        }
        finally
        {
            Call(session, null, "Stop", "test cleanup");
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            AssetDatabase.DeleteAsset(folder);
            Selection.objects = selection;
        }
    }

    static void DrawingOpen(string folder, List<Object> owned)
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); owned.Add(doc);
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 64;
        var expected = new List<byte[]>();
        for (int i = 0; i < 3; i++)
        {
            var pixels = new Texture2D(512, 256, TextureFormat.RGBA32, false, true); owned.Add(pixels);
            var bytes = pixels.GetRawTextureData<byte>();
            for (int j = 0; j < bytes.Length; j++) bytes[j] = (byte)((j * (i + 1)) % 251);
            expected.Add(bytes.ToArray()); pixels.Apply(false, false);
            var drawing = new DrawingLayerBehaviour();
            doc.layers.Add(new Layer(drawing));
            Call(typeof(DrawingLayerBehaviour), drawing, "AdoptStoredTexture", pixels);
        }
        string path = folder + "/Drawing.whimtex.tiff";
        WhimTexDocumentFile.Save(doc, path);
        int count = Resources.FindObjectsOfTypeAll<TextureCompositor>().Length;
        var deferred = WhimTexDocumentFile.Load(path); owned.Add(deferred);
        var deferredPixels = (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any)
            .GetValue(deferred.layers[0].Behaviour);
        Check(deferredPixels != null, "deferred Drawing block materializes on first access");
        Check(Resources.FindObjectsOfTypeAll<TextureCompositor>().Length == count + 1, "deferred open owns one document");
        var restored = WhimTexDocumentFile.Load(path); owned.Add(restored);
        for (int i = 0; i < 3; i++)
        {
            var pixels = (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any).GetValue(restored.layers[i].Behaviour);
            Check(pixels.GetRawTextureData<byte>().ToArray().SequenceEqual(expected[i]), "loaded Drawing pixels exact " + i);
        }

        // Corrupt a stored pixel block, leaving the model/header parseable, to test worker integrity.
        byte[] file = File.ReadAllBytes(path);
        long length = BitConverter.ToInt64(file, file.Length - 16), start = file.Length - 16 - length;
        using (var stream = new MemoryStream(file))
        using (var container = (WhimTexDocumentContainer)Call(typeof(WhimTexDocumentContainer), null, "Open", stream, start, length))
        {
            var entries = (System.Collections.IDictionary)typeof(WhimTexDocumentContainer).GetField("_entries", Any).GetValue(container);
            object entry = entries["texture:0"];
            long offset = Convert.ToInt64(entry.GetType().GetField("dataOffset", Any).GetValue(entry));
            file[start + offset] ^= 1;
        }
        string damaged = folder + "/Damaged.whimtex.tiff";
        File.WriteAllBytes(damaged, file);
        TextureCompositor damagedDocument = null;
        try
        {
            Reject(() =>
            {
                damagedDocument = WhimTexDocumentFile.Load(damaged);
                _ = typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Any).GetValue(damagedDocument.layers[0].Behaviour);
            }, "pixel checksum failure is rejected");
        }
        finally { if (damagedDocument != null) Object.DestroyImmediate(damagedDocument); }
        var previousFocus = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        try
        {
            Call(typeof(TextureCompositorWindow), window, "SetCompositor", restored);
            window.ShowUtility();
            window.CreateGUI();
            var precision = window.rootVisualElement.Q<PopupField<string>>("canvasOutputPrecision");
            Check(precision != null && precision.value == "Auto", "precision control exists on TIFF canvas toolbar");
            Undo.IncrementCurrentGroup();
            precision.value = "Float32";
            Check(restored.outputPrecision == WhimTexOutputPrecision.Float32, "precision control edits document");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(restored.outputPrecision == WhimTexOutputPrecision.Auto, "precision change supports Undo");
        }
        finally
        {
            Undo.ClearUndo(restored); Object.DestroyImmediate(window);
            if (previousFocus != null) previousFocus.Focus();
        }
    }
}
