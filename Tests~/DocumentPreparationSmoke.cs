// Unity run_script, entry DocumentPreparationSmoke.Run. Only unique owned Assets;
// no user window/scene edits. Requires an idle Live Update service.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentPreparationSmoke
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Package = typeof(TextureCompositor).Assembly;
    static Type Type(string name) => Package.GetType("DCFApixels.WhimTex." + name);
    static object Call(Type type, object target, string name, params object[] args) => type.GetMethods(Any)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetProperty(name, Any).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Any).SetValue(target, value);
    static readonly List<Object> Owned = new();
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); checks++; }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception e) {
            Check(e is WhimTexDocumentException || e.InnerException is WhimTexDocumentException, message + " exception: " + e);
            return;
        }
        Check(false, message);
    }
    static TextureCompositor Document()
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); Owned.Add(doc);
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = 64; doc.height = 32;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.red }));
        return doc;
    }
    static TextureCompositor Load(string path)
    { var doc = WhimTexDocumentFile.Load(path); Owned.Add(doc); return doc; }
    static ShaderFX Effect(TextureCompositor doc)
    {
        var fx = (ShaderFX)Call(typeof(TextureCompositor), doc, "AddEmbeddedShaderFX", doc.layers[0]);
        Call(typeof(ShaderFX), fx, "ApplyAgentDraft"); return fx;
    }

    public static string Run()
    {
        checks = 0;
        var session = Type("WhimTexDocumentSession");
        if ((bool)session.GetProperty("IsLive", Any).GetValue(null)) return "SKIP: user Live Update is active.";
        int windows = Resources.FindObjectsOfTypeAll<TextureCompositorWindow>().Length;
        string folder = "Assets/WhimTexPreparation_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        var errors = new List<string>();
        Application.LogCallback onLog = (message, stack, type) => {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) errors.Add(message);
        };
        Application.logMessageReceived += onLog;
        try
        {
            // Both memory-backed and streamed readers verify raw/compressed blocks.
            using (var container = new WhimTexDocumentContainer())
            {
                var data = new byte[4096]; new System.Random(19).NextBytes(data);
                container.Set("document", data, System.IO.Compression.CompressionLevel.NoCompression);
                byte[] bytes = container.Serialize();
                using var parsed = WhimTexDocumentContainer.Parse(bytes);
                Check(parsed.Get("document").SequenceEqual(data), "checksummed memory roundtrip");
                using (var editable = WhimTexDocumentContainer.Parse(bytes))
                {
                    editable.Set("new", new byte[] { 17 });
                    using var rewritten = WhimTexDocumentContainer.Parse(editable.Serialize());
                    Check(rewritten.Get("new")[0] == 17 && rewritten.Get("document").SequenceEqual(data), "editing a parsed container rebuilds integrity manifest");
                }
                string rawPath = folder + "/payload.bin";
                File.WriteAllBytes(rawPath, bytes);
                using var input = new FileStream(rawPath, FileMode.Open, FileAccess.Read);
                using var streamed = (WhimTexDocumentContainer)Call(typeof(WhimTexDocumentContainer), null, "Open", input, 0L, input.Length);
                Check(streamed.Get("document").SequenceEqual(data), "checksummed streamed roundtrip");
                using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
                reader.BaseStream.Position = 12; int count = reader.ReadInt32();
                for (int i = 0; i < count; i++) { int length = reader.ReadInt32(); reader.BaseStream.Position += length + 20; }
                bytes[(int)reader.BaseStream.Position + 31] ^= 0x23;
                using var damaged = WhimTexDocumentContainer.Parse(bytes);
                Reject(() => damaged.Get("document"), "raw block corruption rejected by checksum");
            }
            using (var legacyBytes = new MemoryStream())
            {
                using var writer = new BinaryWriter(legacyBytes, Encoding.UTF8, true);
                writer.Write(Encoding.ASCII.GetBytes("WHIMTEXD")); writer.Write(1); writer.Write(1);
                writer.Write(8); writer.Write(Encoding.ASCII.GetBytes("document")); writer.Write(0); writer.Write(1L); writer.Write(1L); writer.Write((byte)77);
                using var old = WhimTexDocumentContainer.Parse(legacyBytes.ToArray());
                Check(old.Get("document")[0] == 77, "old container without checksum still readable");
            }

            var doc = Document();
            string path = WhimTexDocumentFile.Save(doc, folder + "/Current.tiff");
            var stale = Load(path);
            ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.green;
            WhimTexDocumentFile.Save(doc, path);
            byte[] latest = File.ReadAllBytes(path);
            Reject(() => WhimTexDocumentFile.Save(stale, path), "stale window-independent writer rejected");
            Check(File.ReadAllBytes(path).SequenceEqual(latest), "external save not overwritten");
            string copyPath = WhimTexDocumentFile.Save(stale, folder + "/Copy.tiff");
            Check(File.Exists(copyPath), "stale model can still Save As");
            Reject(() => WhimTexDocumentFile.Save(doc, "Assets/../escape.tiff"), "path traversal rejected");
            Reject(() => WhimTexDocumentFile.Save(doc, "Assets/StreamingAssets/escape.tiff"), "document payload kept outside StreamingAssets");

            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(doc, "TIFF revision test");
            ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.blue;
            WhimTexDocumentFile.Save(doc, path);
            Undo.PerformUndo();
            WhimTexDocumentFile.Save(doc, path);
            Check(((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color == Color.green, "Undo changes model without rolling back the disk revision");
            Undo.ClearUndo(doc);

            Effect(doc);
            WhimTexDocumentFile.Save(doc, path);
            var fx = (ShaderFX)doc.layers[0].modifiers[0];
            string applied = (string)typeof(ShaderFX).GetField("code", Any).GetValue(fx);
            Set(fx, "code", applied + "\n// pending");
            latest = File.ReadAllBytes(path);
            Reject(() => WhimTexDocumentFile.Save(doc, path), "pending HLSL does not silently change rendered output");
            Check(File.ReadAllBytes(path).SequenceEqual(latest), "pending HLSL leaves TIFF intact");
            Set(fx, "code", applied);

            // Headless working session: opening does not compile; Render prepares embedded effects.
            var buildType = Type("WhimTexDocumentBuild");
            using (var build = (IDisposable)Call(buildType, null, "Open", path))
            {
                var opened = (TextureCompositor)Get(build, "Document");
                var restored = (ShaderFX)opened.layers[0].modifiers[0];
                Check(!(bool)Get(restored, "HasAppliedShader"), "headless Open is shader-lazy");
                var preview = (Texture2D)Call(buildType, build, "Render"); Owned.Add(preview);
                Check((bool)Get(restored, "HasAppliedShader") && preview.width == 64, "headless Render prepares effects");
                Call(buildType, build, "Save", folder + "/Headless.tiff");
            }
            int texturesBefore = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            Call(typeof(WhimTexDocumentFile), null, "InspectStorage", path);
            Check(Resources.FindObjectsOfTypeAll<Texture2D>().Length == texturesBefore, "storage inspection does not create textures");
            Check(Resources.FindObjectsOfTypeAll<TextureCompositorWindow>().Length == windows, "headless path never creates an editor window");

            // External FX must retain their persistent shader, source, values and dirty state on open.
            var external = ScriptableObject.CreateInstance<ShaderFX>();
            string externalPath = folder + "/External.asset";
            AssetDatabase.CreateAsset(external, externalPath);
            Check((bool)Call(typeof(ShaderFX), external, "Apply"), "external FX fixture applied");
            AssetDatabase.SaveAssetIfDirty(external);
            byte[] externalBytes = File.ReadAllBytes(externalPath);
            int externalDirty = EditorUtility.GetDirtyCount(external);
            var externalDoc = Document(); externalDoc.layers[0].modifiers.Add(external);
            var externalFile = WhimTexDocumentFile.Save(externalDoc, folder + "/ExternalDoc.tiff");
            var externalLoaded = Load(externalFile);
            Check(externalLoaded.layers[0].modifiers[0] == external, "external FX identity preserved");
            Check(EditorUtility.GetDirtyCount(external) == externalDirty && File.ReadAllBytes(externalPath).SequenceEqual(externalBytes), "loading TIFF does not mutate external FX");

            // Legacy root is Texture2D, not the document. Drawing belongs to the model nonetheless.
            var legacy = Document(); legacy.layers.Clear();
            var drawing = new DrawingLayerBehaviour(); legacy.layers.Add(new Layer(drawing));
            Call(typeof(TextureCompositor), legacy, "NormalizeModel");
            Call(typeof(DrawingLayerBehaviour), drawing, "PaintPoint", new Vector2(.5f, .5f), 64, 32,
                Call(typeof(DrawingLayerBehaviour), drawing, "GetStrokeParameters", false));
            string legacyPath = folder + "/Legacy.asset";
            legacy.hideFlags = HideFlags.None;
            Call(typeof(TextureCompositor), legacy, "SaveLegacyAssetForCompatibility", legacyPath);
            Check(AssetDatabase.LoadMainAssetAtPath(legacyPath) is Texture2D, "legacy main object is the output texture");
            Texture2D oldOutput = legacy.OutputTexture;
            byte[] oldBytes = File.ReadAllBytes(legacyPath);
            var migrated = WhimTexDocumentFile.Save(legacy, folder + "/Migrated.tiff");
            Check(legacy.OutputTexture == oldOutput && File.ReadAllBytes(legacyPath).SequenceEqual(oldBytes), "migration leaves legacy asset and output intact");
            var migratedDoc = Load(migrated);
            var copiedPixels = (Texture2D)typeof(DrawingLayerBehaviour).GetField("pixels", Any).GetValue(migratedDoc.layers[0].Behaviour);
            Check(copiedPixels != null && !AssetDatabase.Contains(copiedPixels), "legacy Drawing embedded rather than GUID reference");
            using (var cached = new WhimTexDocumentContainer())
            {
                Call(Type("WhimTexDocumentSerializer"), null, "Serialize", migratedDoc, cached);
                Check(((System.Collections.IDictionary)typeof(WhimTexDocumentContainer).GetField("_native", Any).GetValue(cached)).Count == 0,
                    "unchanged Drawing reuses compressed cache without native pixel snapshot");
            }
            AssetDatabase.DeleteAsset(legacyPath);
            var independent = Load(migrated);
            var independentPixels = (Texture2D)typeof(DrawingLayerBehaviour).GetField("pixels", Any).GetValue(independent.layers[0].Behaviour);
            Check(independentPixels.GetPixels32().Any(c => c.a > 200), "Drawing survives removal of original legacy asset");

            // Merely previewing a Drawing layer must not rewrite/quantize its source pixels on Save.
            var untouched = Document(); untouched.layers.Clear();
            var sourcePixels = new Texture2D(32, 32, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var halfData = sourcePixels.GetRawTextureData<ushort>();
            for (int i = 0; i < halfData.Length; i++) halfData[i] = (ushort)(0x3400 + i % 2048);
            sourcePixels.Apply(false, false);
            byte[] originalPixels = sourcePixels.GetRawTextureData<byte>().ToArray();
            var untouchedDrawing = new DrawingLayerBehaviour(); Set(untouchedDrawing, "pixels", sourcePixels);
            untouched.layers.Add(new Layer(untouchedDrawing) { colorRange = LayerColorRange.HDR });
            string untouchedPath = WhimTexDocumentFile.Save(untouched, folder + "/UntouchedDrawing.tiff");
            DateTime unchangedTime = File.GetLastWriteTimeUtc(untouchedPath);
            WhimTexDocumentFile.Save(untouched, untouchedPath);
            Check(sourcePixels.GetRawTextureData<byte>().ToArray().SequenceEqual(originalPixels), "Save preserves unpainted Drawing bytes after preview");
            Check(File.GetLastWriteTimeUtc(untouchedPath) == unchangedTime, "unchanged Drawing skips file rewrite");
            var untouchedLoaded = Load(untouchedPath);
            var restoredPixels = (Texture2D)typeof(DrawingLayerBehaviour).GetField("pixels", Any).GetValue(untouchedLoaded.layers[0].Behaviour);
            Check(restoredPixels.GetRawTextureData<byte>().ToArray().SequenceEqual(originalPixels), "unpainted Drawing roundtrip remains bit exact");

            // Save As resolves includes without method expansion or edits to source code.
            File.WriteAllText(folder + "/Shared.hlsl", "float4 Tint(float4 c) { return c; }\n");
            var includeDoc = Document(); var includeFx = Effect(includeDoc);
            Set(includeFx, "code", "#include \"./Shared.hlsl\"\nfloat4 ApplyFX(float2 uv, float4 color) { return Tint(color); }");
            Call(Type("WhimTexDocumentService"), null, "Bind", includeDoc, folder + "/Source.tiff");
            Call(typeof(ShaderFX), includeFx, "ApplyAgentDraft");
            string originalCode = (string)typeof(ShaderFX).GetField("code", Any).GetValue(includeFx);
            var includedPath = WhimTexDocumentFile.Save(includeDoc, folder + "/Nested/Includes.tiff");
            var included = Load(includedPath);
            var includedFx = (ShaderFX)included.layers[0].modifiers[0];
            Check((bool)Get(includedFx, "HasAppliedShader"), "relative include compiles after Save As to another folder");
            string includedCode = (string)typeof(ShaderFX).GetField("code", Any).GetValue(includedFx);
            Check(includedCode.Contains(folder + "/Shared.hlsl") && !includedCode.Contains("return c;"), "include rebased, not expanded");
            Check((string)typeof(ShaderFX).GetField("code", Any).GetValue(includeFx) == originalCode, "saving does not edit source HLSL");
            WhimTexDocumentFile.Save(includeDoc, includedPath);
            var twiceIncluded = Load(includedPath);
            Check((bool)Get((ShaderFX)twiceIncluded.layers[0].modifiers[0], "HasAppliedShader"), "relative include still works on a second save after Save As");

            // Saving an active session keeps temporary Read/Write instead of stopping/restarting it.
            var liveDoc = Document(); string livePath = WhimTexDocumentFile.Save(liveDoc, folder + "/Live.tiff");
            var importer = (TextureImporter)AssetImporter.GetAtPath(livePath);
            importer.isReadable = false; importer.SaveAndReimport();
            string guid = AssetDatabase.AssetPathToGUID(livePath);
            Check((bool)Call(session, null, "Start", liveDoc, livePath), "live session starts");
            ((ColorFillLayerBehaviour)liveDoc.layers[0].Behaviour).color = Color.blue;
            WhimTexDocumentFile.Save(liveDoc, livePath);
            Check((bool)Call(session, null, "IsLiveFor", liveDoc), "live session resumes after save");
            Check(((TextureImporter)AssetImporter.GetAtPath(livePath)).isReadable, "temporary Read/Write retained across save");
            Call(session, null, "Stop", "test");
            Check(!((TextureImporter)AssetImporter.GetAtPath(livePath)).isReadable && AssetDatabase.AssetPathToGUID(livePath) == guid, "Stop restores Read/Write and preserves GUID");
            Check(!Directory.EnumerateFiles(folder, "*.whimtex-tmp", SearchOption.AllDirectories).Any(), "no staged files leaked");
            Check(errors.Count == 0, "no Unity errors/asserts: " + string.Join("; ", errors));
            return "PASS: " + checks + " TIFF preparation checks.";
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            Call(session, null, "Stop", "test cleanup");
            for (int i = Owned.Count - 1; i >= 0; i--)
                if (Owned[i] != null && !AssetDatabase.Contains(Owned[i])) Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
