// Diagnostic only: requires permission for a unique temporary Assets folder and window.
// Mirrors the saved-file window tail to time each step, and compares with the real SaveDocumentTo.
// Restores selection/focus/profiler settings; never saves or edits the source document.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;
#if UNITY_6000_4_OR_NEWER
using ProjectDrawCallback = UnityEditor.EditorApplication.ProjectWindowItemByEntityIdCallback;
#else
using ProjectDrawCallback = UnityEditor.EditorApplication.ProjectWindowItemInstanceCallback;
#endif

public static class DocumentSaveTailProbe
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object owner, string name, params object[] args)
    {
        try { return type.GetMethods(Any).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(owner, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }
    public sealed class Sample { public string name; public double ms; }
    public sealed class Trial
    {
        public string mode;
        public double totalMs, firstUpdateMs, maxLaterUpdateGapMs;
        public bool progressShown;
        public List<Sample> stages = new List<Sample>();
        public List<Frame> frames = new List<Frame>();
    }
    public sealed class Frame
    {
        public int index;
        public double totalMs;
        public List<Sample> samples = new List<Sample>();
    }
    public sealed class Report
    {
        public string source, unity, folder;
        public int width, height, layers;
        public bool cleanup, sourceUnchanged, profiling;
        public List<Trial> trials = new List<Trial>();
    }
    public static string ReadProfile()
    {
        var trial = new Trial { mode = "captured-editor-frames" };
        Frames(trial, ProfilerDriver.firstFrameIndex, ProfilerDriver.lastFrameIndex);
        WriteJson("Temp/WhimTex/TiffValidationResults/save-tail-profile.json", trial);
        return "Captured " + trial.frames.Count + " nontrivial frames; recording is " + ProfilerDriver.enabled;
    }
    static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json").GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
        File.WriteAllText(path, json);
    }
    static void LegacyCallback(ProjectDrawCallback callback, bool attach)
    {
#if UNITY_6000_4_OR_NEWER
        if (attach) EditorApplication.projectWindowItemByEntityIdOnGUI += callback;
        else EditorApplication.projectWindowItemByEntityIdOnGUI -= callback;
#else
        if (attach) EditorApplication.projectWindowItemInstanceOnGUI += callback;
        else EditorApplication.projectWindowItemInstanceOnGUI -= callback;
#endif
    }
    static async Task NextUpdate()
    {
        var completion = new TaskCompletionSource<bool>();
        void Tick() { EditorApplication.update -= Tick; completion.TrySetResult(true); }
        EditorApplication.update += Tick;
        try
        {
            if (await Task.WhenAny(completion.Task, Task.Delay(5000)) != completion.Task)
                throw new TimeoutException("Editor did not update.");
            await completion.Task;
        }
        finally { EditorApplication.update -= Tick; }
    }
    static void Time(Trial trial, string name, Action action)
    {
        var clock = Stopwatch.StartNew();
        Profiler.BeginSample("WhimTexSaveProbe." + name);
        try { action(); }
        finally { Profiler.EndSample(); trial.stages.Add(new Sample { name = name, ms = clock.Elapsed.TotalMilliseconds }); }
    }
    static void Frames(Trial trial, int start, int end)
    {
        for (int frame = Math.Max(start, ProfilerDriver.firstFrameIndex); frame <= end; frame++)
        {
            using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
            if (!data.valid || data.sampleCount == 0) continue;
            var result = new Frame { index = frame, totalMs = data.GetSampleTimeMs(0) };
            for (int i = 0; i < data.sampleCount; i++)
            {
                double ms = data.GetSampleTimeMs(i);
                if (ms >= 2) result.samples.Add(new Sample { name = data.GetSampleName(i), ms = ms });
            }
            result.samples = result.samples.OrderByDescending(s => s.ms).Take(25).ToList();
            if (result.totalMs >= 30 || result.samples.Any(s => s.name.StartsWith("WhimTexSaveProbe."))) trial.frames.Add(result);
        }
    }
    public static async Task<string> Run(string source = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/BASE_Heart.tiff", int repeats = 2, bool profiling = true)
    {
        if (repeats < 1 || repeats > 3) throw new ArgumentOutOfRangeException(nameof(repeats));
        if (!File.Exists(source) || !WhimTexDocumentFile.IsDocument(source)) throw new Exception("Source document missing.");
        if (ProfilerDriver.enabled || ProfilerDriver.deepProfiling) throw new Exception("Stop existing profiling before running this isolated probe.");
        if ((bool)T("WhimTexDocumentSession").GetProperty("IsLive", Any).GetValue(null)) throw new Exception("Stop user Live Update before running probe.");
        string sourceForCopy = source;
        string stagedSourceFolder = null;
        if (source.StartsWith("Packages/", StringComparison.Ordinal))
        {
            stagedSourceFolder = "Assets/WhimTexSaveTailSource_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(stagedSourceFolder));
            sourceForCopy = stagedSourceFolder + "/Source.tiff";
            string sourcePhysical = Path.GetFullPath(source.Replace('/', Path.DirectorySeparatorChar));
            string stagedPhysical = Path.GetFullPath(sourceForCopy.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(sourcePhysical, stagedPhysical, false);
            AssetDatabase.ImportAsset(sourceForCopy, ImportAssetOptions.ForceSynchronousImport);
        }
        var sourceBytes = File.ReadAllBytes(sourceForCopy);
        var sourceMeta = File.Exists(sourceForCopy + ".meta") ? File.ReadAllBytes(sourceForCopy + ".meta") : null;
        var selection = Selection.objects;
        var focus = EditorWindow.focusedWindow;
        bool profileEditor = ProfilerDriver.profileEditor, profileCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
        var report = new Report { source = source, unity = Application.unityVersion, profiling = profiling, folder = "Assets/WhimTexSaveTailProbe_" + Guid.NewGuid().ToString("N") };
        TextureCompositor doc = null;
        TextureCompositorWindow window = null;
        var legacyDraw = (ProjectDrawCallback)T("TextureCompositorProjectPreview")
            .GetMethod("DrawProjectIcon", Any).CreateDelegate(typeof(ProjectDrawCallback));
        bool legacyDetached = false;
        string path = report.folder + "/Probe.tiff";
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(report.folder));
        try
        {
            if (!AssetDatabase.CopyAsset(sourceForCopy, path)) throw new Exception("Copy failed.");
            doc = WhimTexDocumentFile.Load(path);
            report.width = doc.width; report.height = doc.height; report.layers = doc.layers.Count;
            var fill = new ColorFillLayerBehaviour { color = new Color(1, 0, 0, .02f) };
            doc.layers.Insert(0, new Layer(fill));
            window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
            Call(typeof(TextureCompositorWindow), window, "SetCompositor", doc);
            window.ShowUtility();
            window.position = new Rect(100, 100, 850, 650);
            for (int i = 0; i < 8; i++) await NextUpdate();
            // Warm shader/render/import/preview paths outside the measured trials.
            Call(typeof(TextureCompositorWindow), null, "SaveDocumentTo", doc, path);
            for (int i = 0; i < 8; i++) await NextUpdate();
            if (profiling)
            {
                ProfilerDriver.profileEditor = true;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                ProfilerDriver.enabled = true;
            }
            await NextUpdate();
            int iteration = 0;
            for (int repeat = 0; repeat < repeats; repeat++)
            foreach (string mode in new[] { "real-window", "split-full-tail", "split-no-selection-ping-log", "real-window-no-legacy-icons" })
            {
                if (mode == "real-window-no-legacy-icons")
                {
                    LegacyCallback(legacyDraw, false);
                    legacyDetached = true;
                }
                var trial = new Trial { mode = mode };
                report.trials.Add(trial);
                fill.color = new Color((++iteration % 7) / 7f, .5f, .2f, .02f);
                Call(typeof(TextureCompositor), doc, "MarkChanged");
                for (int i = 0; i < 3; i++) await NextUpdate();
                int startFrame = ProfilerDriver.lastFrameIndex + 1;
                var total = Stopwatch.StartNew();
                if (mode.StartsWith("real-window"))
                {
                    bool saved = false;
                    Time(trial, "SaveDocumentTo", () => saved = (bool)Call(typeof(TextureCompositorWindow), null, "SaveDocumentTo", doc, path));
                    if (!saved) throw new Exception("Window save failed.");
                }
                else
                {
                    IDisposable operation = (IDisposable)Activator.CreateInstance(T("WhimTexDocumentOperation"), Any, null,
                        new object[] { "Save tail diagnostic", null }, null);
                    try
                    {
                        Time(trial, "PrepareDocumentSave", () => Call(typeof(TextureCompositorWindow), window, "PrepareDocumentSave"));
                        Time(trial, "File.Save", () => WhimTexDocumentFile.Save(doc, path, deferImport: true));
                        Time(trial, "BindDocumentFile", () => Call(typeof(TextureCompositorWindow), window, "BindDocumentFile", path));
                        typeof(TextureCompositorWindow).GetField("temporaryDocumentDirty", Any).SetValue(window, false);
                        Time(trial, "UpdateUnsavedChangesState", () => Call(typeof(TextureCompositorWindow), window, "UpdateUnsavedChangesState"));
                        if (mode == "split-full-tail")
                        {
                            Texture2D image = null;
                            Time(trial, "LoadImage", () => image = AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                            Time(trial, "Selection", () => Selection.activeObject = image);
                            Time(trial, "Ping", () => EditorGUIUtility.PingObject(image));
                            Time(trial, "Log", () => UnityEngine.Debug.Log("WhimTex: save tail diagnostic " + path));
                        }
                        trial.progressShown = (bool)T("WhimTexDocumentOperation").GetField("shown", Any).GetValue(operation);
                    }
                    finally { Time(trial, "DisposeProgress", operation.Dispose); }
                }
                trial.totalMs = total.Elapsed.TotalMilliseconds;
                var after = Stopwatch.StartNew();
                await NextUpdate();
                trial.firstUpdateMs = after.Elapsed.TotalMilliseconds;
                double last = after.Elapsed.TotalMilliseconds;
                while (after.ElapsedMilliseconds < 800)
                {
                    await NextUpdate();
                    double now = after.Elapsed.TotalMilliseconds;
                    trial.maxLaterUpdateGapMs = Math.Max(trial.maxLaterUpdateGapMs, now - last);
                    last = now;
                }
                if (profiling) Frames(trial, startFrame, ProfilerDriver.lastFrameIndex);
                if (legacyDetached)
                {
                    LegacyCallback(legacyDraw, true);
                    legacyDetached = false;
                }
            }
        }
        finally
        {
            ProfilerDriver.enabled = false;
            ProfilerDriver.profileEditor = profileEditor;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, profileCpu);
            if (legacyDetached) LegacyCallback(legacyDraw, true);
            if (window != null) Object.DestroyImmediate(window);
            if (doc != null) Object.DestroyImmediate(doc);
            Selection.objects = selection;
            if (focus != null) focus.Focus();
            string full = Path.GetFullPath(report.folder), assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("WhimTexSaveTailProbe_", StringComparison.Ordinal))
                throw new IOException("Unsafe cleanup target.");
            report.cleanup = AssetDatabase.DeleteAsset(report.folder) && !Directory.Exists(report.folder);
            report.sourceUnchanged = File.ReadAllBytes(sourceForCopy).SequenceEqual(sourceBytes) &&
                (sourceMeta == null ? !File.Exists(sourceForCopy + ".meta") : File.ReadAllBytes(sourceForCopy + ".meta").SequenceEqual(sourceMeta));
            if (stagedSourceFolder != null) AssetDatabase.DeleteAsset(stagedSourceFolder);
            string output = "Temp/WhimTex/TiffValidationResults/save-tail-probe" + (profiling ? "" : "-no-profiler") + ".json";
            WriteJson(output, report);
        }
        if (!report.cleanup || !report.sourceUnchanged) throw new Exception("Cleanup/source preservation check failed.");
        return "Measured " + report.trials.Count + " saves of " + report.width + "x" + report.height + "; source untouched; test Assets removed. Profiling: " + profiling;
    }
}
