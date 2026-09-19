// run_script: Install -> recompile -> Faults / PreparePlayer -> build -> InspectBuild -> Cleanup.
// Install owns a unique Assets directory; never changes user scenes/build settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class DocumentReleaseValidation
{
    const string Key = "WhimTex.ReleaseValidation";
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type Session => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSession");
    static object Call(Type type, object instance, string name, params object[] args) => type.GetMethods(Any)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(instance, args);
    static int checks;
    static object Record(string name, object result)
    {
        string output = "Temp/WhimTex/TiffValidationResults"; Directory.CreateDirectory(output);
        string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json")
            .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { result });
        File.WriteAllText(output + "/" + name + ".json", json);
        return result;
    }
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); checks++; }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception e) { if (e is TargetInvocationException) e = e.InnerException;
            Check(e is IOException || e is WhimTexDocumentException || e is UnityEditor.Build.BuildFailedException || e is InvalidOperationException, message + ": " + e); return; }
        throw new Exception("Expected failure: " + message);
    }
    static string Folder()
    {
        string path = SessionState.GetString(Key, "");
        Check(path.StartsWith("Assets/WhimTexValidation_") && Path.GetFileName(path).Length == "WhimTexValidation_".Length + 32 && AssetDatabase.IsValidFolder(path), "owned fixture folder");
        return path;
    }
    static TextureCompositor Document(Color color, int size = 256)
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = size; doc.height = size; doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = color })); return doc;
    }
    static void Idle() => Check(!(bool)Session.GetProperty("IsLive", Any).GetValue(null), "user Live Update must be idle");
    public static object Install()
    {
        Idle(); Check(string.IsNullOrEmpty(SessionState.GetString(Key, "")), "no prior fixture");
        string folder = "Assets/WhimTexValidation_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder)); AssetDatabase.CreateFolder(folder, "Editor");
        SessionState.SetString(Key, folder);
        File.Copy("Packages/com.dcfapixels.whimtex/Tests~/Fixtures/WhimTexPlayerProbe.cs", folder + "/WhimTexPlayerProbe.cs");
        File.Copy("Packages/com.dcfapixels.whimtex/Tests~/Fixtures/WhimTexImportFailureProbe.cs", folder + "/Editor/WhimTexImportFailureProbe.cs");
        AssetDatabase.Refresh(); return new { folder, next = "recompile, then Faults and PreparePlayer" };
    }
    public static object Faults()
    {
        checks = 0; Idle(); string folder = Folder(), raw = folder + "/transaction.bin";
        string recovery = (string)Session.GetField("RecoveryKey", Any).GetValue(null);
        Check(!EditorPrefs.HasKey(recovery), "no user's pending recovery journal");
        var doc = Document(Color.green, 32); TextureCompositor loaded = null;
        string path = folder + "/Fault.whimtex.tiff";
        try
        {
            File.Delete(raw + ".00000000000000000000000000000000.whimtex-tmp");
            byte[] original = { 10, 20, 30, 40 };
            File.WriteAllBytes(raw, original);
            Reject(() => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw,
                (Action<Stream>)(s => { s.WriteByte(99); throw new IOException("Injected disk-full/write interruption"); })), "interrupted existing write");
            Check(File.ReadAllBytes(raw).SequenceEqual(original), "interrupted write preserves old file");
            string fresh = folder + "/new.bin";
            Reject(() => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", fresh,
                (Action<Stream>)(s => { s.WriteByte(99); throw new OperationCanceledException(); })), "cancel first save");
            Check(!File.Exists(fresh), "cancel leaves no partial destination");
            Reject(() => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw,
                (Action<Stream>)(s => { s.WriteByte(98); File.WriteAllBytes(raw, new byte[] { 7, 8, 9 }); })), "external write during staging");
            Check(File.ReadAllBytes(raw).SequenceEqual(new byte[] { 7, 8, 9 }), "external version retained");
            using (var locked = new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw, (Action<Stream>)(s => s.WriteByte(12))), "locked destination");
            Check(!Directory.GetFiles(folder, "*.whimtex-tmp").Any(), "staging failures clean temporary files");
            // An orphan from a hard process exit is inert and must never replace a saved document.
            string orphan = raw + ".00000000000000000000000000000000.whimtex-tmp";
            File.WriteAllBytes(orphan, new byte[] { 255 });
            Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw, (Action<Stream>)(s => s.Write(original, 0, original.Length)));
            Check(File.ReadAllBytes(raw).SequenceEqual(original) && File.Exists(orphan), "orphan is not promoted/deleted implicitly");

            WhimTexDocumentFile.Save(doc, path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = false; importer.SaveAndReimport();
            string meta = File.ReadAllText(path + ".meta"); byte[] saved = File.ReadAllBytes(path);
            ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.red;
            Check((bool)Call(Session, null, "Start", doc, path), "live before build");
            Call(Session, null, "PrepareForBuild");
            Check(!(bool)Session.GetProperty("IsLive", Any).GetValue(null), "build ends live");
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable && File.ReadAllText(path + ".meta") == meta, "build restores exact meta");
            Check(File.ReadAllBytes(path).SequenceEqual(saved) && ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color == Color.red, "build neither saves nor discards unsaved edits");
            Call(Session, null, "PrepareForBuild"); Check(!EditorPrefs.HasKey(recovery), "build guard is idempotent");

            importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = true; importer.SaveAndReimport();
            Check((bool)Call(Session, null, "Start", doc, path), "live with originally enabled Read/Write");
            using (var pause = (IDisposable)Call(Session, null, "SuspendForSave", doc))
                Reject(() => Call(Session, null, "PrepareForBuild"), "build during in-progress save rejected");
            Call(Session, null, "PrepareForBuild");
            Check(((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "build preserves originally enabled Read/Write");

            // Recreate durable state left after a crash, with no live in-memory owner.
            importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = true; importer.SaveAndReimport();
            EditorPrefs.SetString(recovery, guid); Call(Session, null, "RecoverReadable");
            Check(!EditorPrefs.HasKey(recovery) && !((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "crash journal restores Read/Write");
            EditorPrefs.SetString(recovery, Guid.NewGuid().ToString("N")); Call(Session, null, "RecoverReadable");
            Check(EditorPrefs.HasKey(recovery), "temporarily missing asset retains recovery journal");
            Reject(() => Call(Session, null, "PrepareForBuild"), "unresolved recovery blocks build");
            string missingGuid = EditorPrefs.GetString(recovery);
            Check(!(bool)Call(Session, null, "Start", doc, path) && EditorPrefs.GetString(recovery) == missingGuid,
                "another live session cannot overwrite an unresolved recovery journal");
            EditorPrefs.DeleteKey(recovery);

            string marker = path + ".failimport"; File.WriteAllText(marker, "expected fault");
            Reject(() => WhimTexDocumentFile.Save(doc, path), "import errors are reported after successful disk write");
            Check(AssetDatabase.AssetPathToGUID(path) == guid && File.Exists(path), "failed import retains saved file and GUID");
            File.Delete(marker);
            WhimTexDocumentFile.Save(doc, path);
            Check(!(bool)Call(typeof(WhimTexDocumentFile), null, "ImportHasErrors", path), "identical-byte retry repairs failed import");
            loaded = WhimTexDocumentFile.Load(path);
            Check(((ColorFillLayerBehaviour)loaded.layers[0].Behaviour).color == Color.red, "retry retains newest contents");

            importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = true; importer.SaveAndReimport();
            EditorPrefs.SetString(recovery, guid); File.WriteAllText(marker, "expected recovery fault");
            Reject(() => Call(Session, null, "RecoverReadable"), "failed recovery import reported");
            Check(EditorPrefs.HasKey(recovery), "failed recovery import retains durable journal");
            Reject(() => Call(Session, null, "PrepareForBuild"), "failed recovery import blocks build");
            File.Delete(marker); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Call(Session, null, "RecoverReadable");
            Check(!EditorPrefs.HasKey(recovery), "recovery retry succeeds after import error resolved");

            // Replace the source externally, without asking our writer to acknowledge it.
            var external = Document(Color.blue, 32);
            try { WhimTexDocumentFile.Save(external, folder + "/External.whimtex.tiff"); }
            finally { Object.DestroyImmediate(external); }
            File.Copy(folder + "/External.whimtex.tiff", path, true);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(3));
            Reject(() => WhimTexDocumentFile.Save(doc, path), "external replacement blocks stale save");
            return Record("faults", new { success = true, checks });
        }
        finally
        {
            File.Delete(path + ".failimport"); EditorPrefs.DeleteKey(recovery);
            File.Delete(raw + ".00000000000000000000000000000000.whimtex-tmp");
            Call(Session, null, "Stop", "validation cleanup");
            if (loaded != null) Object.DestroyImmediate(loaded); Object.DestroyImmediate(doc);
        }
    }

    public static string PrepareDeferredFailure()
    {
        Idle(); string folder = Folder();
        var doc = Document(Color.green, 32); doc.name = Path.GetFileName(folder) + ".deferred";
        string path = WhimTexDocumentFile.Save(doc, folder + "/Deferred.tiff");
        ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.red;
        File.WriteAllText(path + ".failimport", "expected deferred import fault");
        WhimTexDocumentFile.Save(doc, path, true);
        return "Wait one editor update; then VerifyDeferredFailure.";
    }
    public static string VerifyDeferredFailure()
    {
        string folder = Folder(), path = folder + "/Deferred.tiff";
        var doc = Resources.FindObjectsOfTypeAll<TextureCompositor>().Single(d => d.name == Path.GetFileName(folder) + ".deferred");
        try
        {
            var binding = typeof(TextureCompositor).GetField("documentBinding", Any).GetValue(doc);
            Check((bool)binding.GetType().GetField("dirty", Any).GetValue(binding), "deferred failure marks document retryable");
            File.Delete(path + ".failimport"); WhimTexDocumentFile.Save(doc, path);
            Check(!(bool)Call(typeof(WhimTexDocumentFile), null, "ImportHasErrors", path), "retry repairs deferred failed import");
            return "PASS: deferred failure is reported and retry succeeds.";
        }
        finally { File.Delete(path + ".failimport"); Object.DestroyImmediate(doc); }
    }
    public static object PreparePlayer()
    {
        Idle(); string folder = Folder(), prefix = Path.GetFileName(folder);
        string resources = folder + "/Resources", images = resources + "/" + prefix;
        AssetDatabase.CreateFolder(folder, "Resources"); AssetDatabase.CreateFolder(resources, prefix);
        var doc = Document(Color.green);
        for (int i = 0; i < 2; i++)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            byte[] noise = new byte[1024 * 1024 * 4]; new System.Random(17 + i).NextBytes(noise); texture.LoadRawTextureData(noise); texture.Apply();
            var drawing = new DrawingLayerBehaviour(); typeof(DrawingLayerBehaviour).GetField("pixels", Any).SetValue(drawing, texture);
            doc.layers.Add(new Layer(drawing) { enabled = false });
        }
        string path = WhimTexDocumentFile.Save(doc, images + "/Document.tiff");
        byte[] file = File.ReadAllBytes(path); long containerLength = BitConverter.ToInt64(file, file.Length - 16);
        File.WriteAllBytes(images + "/Plain.tiff", file.Take((int)(file.Length - 16 - containerLength)).ToArray());
        File.Copy(path, images + "/Compressed.tiff");
        foreach (string name in new[] { "Document", "Plain", "Compressed" })
        {
            string asset = images + "/" + name + ".tiff";
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(asset);
            importer.sRGBTexture = true; importer.isReadable = false; importer.mipmapEnabled = name == "Compressed";
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.npotScale = TextureImporterNPOTScale.None;
            if (name == "Compressed") importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Standalone", overridden = true, maxTextureSize = 128, format = TextureImporterFormat.DXT5, textureCompression = TextureImporterCompression.Compressed });
            importer.SaveAndReimport();
        }
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        string scenePath = folder + "/Probe.unity";
        try
        {
            var go = new GameObject("WhimTex Player probe"); SceneManager.MoveGameObjectToScene(go, scene);
            Type probeType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("WhimTexPlayerProbe")).First(t => t != null);
            var probe = go.AddComponent(probeType); probeType.GetField("resourcePrefix").SetValue(probe, prefix);
            EditorSceneManager.SaveScene(scene, scenePath);
        }
        finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
        ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.red;
        Check((bool)Call(Session, null, "Start", doc, path), "live red before player build of saved green");
        // Held by the live session until the callback; cleanup finds this explicit marker.
        doc.name = prefix;
        string output = Path.GetFullPath("Temp/WhimTex/" + prefix); Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        return new { scenePath, output, sourceBytes = file.Length, imageBytes = file.Length - 16 - containerLength };
    }
    public static bool RestartPlayerLive()
    {
        Idle(); string folder = Folder(), prefix = Path.GetFileName(folder);
        var doc = Resources.FindObjectsOfTypeAll<TextureCompositor>().FirstOrDefault(d => d.name == prefix);
        string path = folder + "/Resources/" + prefix + "/Document.tiff";
        if (doc == null) { doc = WhimTexDocumentFile.Load(path); doc.name = prefix; }
        ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.red;
        bool started = (bool)Call(Session, null, "Start", doc, path);
        Check(started, "live red before retry of player build");
        Call(Session, null, "Publish");
        return started;
    }
    public static object InspectBuild()
    {
        string folder = Folder(); var report = BuildReport.GetLatestReport();
        Check(report != null && report.summary.result == BuildResult.Succeeded, "Player build succeeds");
        var packed = report.packedAssets.SelectMany(p => p.contents).Where(p => p.sourceAssetPath.StartsWith(folder + "/Resources/"))
            .Select(p => new { path = p.sourceAssetPath, type = p.type.Name, bytes = p.packedSize }).ToArray();
        Check(packed.Length >= 3 && packed.All(p => p.type == "Texture2D"), "only textures from TIFF enter Player");
        ulong doc = packed.Where(p => p.path.EndsWith("/Document.tiff")).Aggregate(0UL, (sum, p) => sum + p.bytes);
        ulong plain = packed.Where(p => p.path.EndsWith("/Plain.tiff")).Aggregate(0UL, (sum, p) => sum + p.bytes);
        Check(doc > 0 && Math.Abs((double)doc - plain) < 1024, "layered TIFF packs like identical plain TIFF");
        Check(!report.GetFiles().Any(f => f.path.Contains("DCFApixels.WhimTex")), "no WhimTex assembly shipped");
        return Record("player-build", new { success = true, totalBytes = report.summary.totalSize, buildSeconds = report.summary.totalTime.TotalSeconds, packed });
    }
    public static string Cleanup()
    {
        string folder = Folder(); string prefix = Path.GetFileName(folder);
        string livePath = (string)Session.GetProperty("LivePath", Any).GetValue(null);
        if (livePath != null && livePath.StartsWith(folder + "/")) Call(Session, null, "Stop", "test cleanup");
        foreach (var doc in Resources.FindObjectsOfTypeAll<TextureCompositor>()) if (doc.name == prefix || doc.name == prefix + ".deferred") Object.DestroyImmediate(doc);
        Check(AssetDatabase.DeleteAsset(folder), "delete owned fixture");
        SessionState.EraseString(Key); SessionState.EraseString(Key + ".output");
        return "Deleted only the temporary validation Assets folder; build/report kept in Temp/WhimTex.";
    }
}
