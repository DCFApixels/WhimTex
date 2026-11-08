using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

// Test-only instrumentation of WhimTex types; no non-public Unity API bindings.
public static partial class ShaderGraphEditingBench
{
    const string FixturePath = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/ShaderGraphEditing/";
    const string GraphHash = "39be9f3a09c8eb04cf182d8bcce67a9662ff5f3beebbb1f9b7725a87a4b4b05a";
    const string LightsHash = "2a7e6d960258e8ef98f6608bec8f698c081a64dd0ab101b8ac4d9d5db583e97f";
    const string OutputId = "4cc9aef9de8b4f1abb50d0c25e379eef";
    const string AddedId = "ed170000000000000000000000000001";
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    static Type TypeOf(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object owner, string name)
    {
        if (owner == null) return null;
        for (Type type = owner as Type ?? owner.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(owner is Type ? null : owner);
            var property = type.GetProperty(name, Flags);
            if (property != null) return property.GetValue(owner is Type ? null : owner);
        }
        throw new MissingMemberException(owner.ToString(), name);
    }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static object Call(object owner, string name, params object[] args)
    {
        for (Type type = owner as Type ?? owner.GetType(); type != null; type = type.BaseType)
        {
            var method = type.GetMethod(name, Flags);
            if (method != null) return method.Invoke(owner is Type ? null : owner, args);
        }
        throw new MissingMethodException(owner.ToString(), name);
    }
    static object Static(string type, string method, params object[] args) => Call(TypeOf(type), method, args);
    static string Json(object value) => (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    static T FromJson<T>(string json) => (T)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { json, typeof(T) });
    static string Read(string file, string hash)
    {
        byte[] bytes = File.ReadAllBytes(MigrationD.ProjectPath(FixturePath + file));
        using (var sha = SHA256.Create())
            if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != hash)
                throw new InvalidDataException("Frozen fixture changed: " + file);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
    static ShaderGraph Load()
    {
        return ParseGraph(Read("graph.json", GraphHash));
    }
    static ShaderGraph ParseGraph(string text)
    {
        object json = Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true)
            .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new[] { text });
        return (ShaderGraph)Static("WhimTexDocumentJson", "ReadShaderGraph", json);
    }
    static string Snapshot(ShaderGraph graph) => Json(Static("WhimTexDocumentJson", "ShaderGraphSnapshot", graph, null));

    [Serializable] public sealed class Step
    {
        public string action, node, port, source, sourcePort, value;
        public float number;
    }
    [Serializable] public sealed class Plan
    {
        public int version = 1, seed;
        public string fixtureHash = GraphHash;
        public List<Step> steps = new List<Step>();
    }
    struct RandomSequence
    {
        uint state;
        internal RandomSequence(int seed) { state = (uint)seed; if (state == 0) state = 1; }
        internal int Next(int count) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (int)(state % (uint)count); }
    }
    static Plan MakePlan(int seed, int count)
    {
        if (count < 1 || count > 24) throw new ArgumentOutOfRangeException(nameof(count));
        var random = new RandomSequence(seed); var graph = Load(); var plan = new Plan { seed = seed };
        var noises = graph.nodes.Where(n => n.kind == ShaderGraphNodeKind.ShardNoise || n.kind == ShaderGraphNodeKind.ValueCubicNoise).ToArray();
        var fractals = graph.nodes.Where(n => n.kind == ShaderGraphNodeKind.Fractal).ToArray();
        var lighting = graph.nodes.Where(n => n.kind == ShaderGraphNodeKind.NormalLighting).ToArray();
        ShaderGraphEdge disconnected = null, inserted = null;
        // Stratified random edits: every cycle covers topology and value changes. No invalid edits are silently retried.
        for (int i = 0; i < count; i++)
        {
            var noise = noises[random.Next(noises.Length)]; var fractal = fractals[random.Next(fractals.Length)];
            var step = new Step { node = noise.id };
            switch (i % 12)
            {
                case 0: step.action = "value"; step.port = "Seed"; step.number = 100 + random.Next(10000); break;
                case 1: step.action = "dimensions"; step.value = noise.noiseDimensions == ShaderGraphNoiseDimensions.ThreeD ? "TwoD" : "ThreeD"; break;
                case 2: step.action = "algorithm"; step.value = noise.kind == ShaderGraphNodeKind.PerlinNoise ? "ValueNoise" : "PerlinNoise"; break;
                case 3: step.action = "fractal"; step.node = fractal.id; step.value = fractal.fractal == ShaderGraphFractal.FBm ? "Ridged" : "FBm"; break;
                case 4: step.action = "lighting"; step.node = lighting[random.Next(lighting.Length)].id; step.number = 1 - graph.FindNode(step.node).option; break;
                case 5:
                    step.action = "disconnect";
                    var candidates = graph.edges.Where(e => e.inputPort == "Height" && graph.FindNode(e.inputNode).kind == ShaderGraphNodeKind.HeightToNormal).ToArray();
                    disconnected = candidates[random.Next(candidates.Length)]; CopyEdge(step, disconnected); break;
                case 6: step.action = "connect"; CopyEdge(step, disconnected); break;
                case 7:
                    step.action = "output";
                    var outputCandidates = noises.Where(n => ((string[])Static("ShaderGraphNodes", "StoredOutputs", n)).Contains("Gradient")).ToArray();
                    string outputNode = outputCandidates[random.Next(outputCandidates.Length)].id;
                    var edge = graph.edges.First(e => e.outputNode == outputNode);
                    CopyEdge(step, edge); step.sourcePort = edge.outputPort == "Value" ? "Gradient" : "Value"; break;
                case 8: step.action = "insert"; inserted = graph.edges.Single(e => e.inputNode == OutputId && e.inputPort == "RGBA"); CopyEdge(step, inserted); break;
                case 9: step.action = "remove"; CopyEdge(step, inserted); break;
                case 10: step.action = "layout"; step.number = 20 + random.Next(80); break;
                default: step.action = "filter"; step.number = noise.noiseFiltering ? 0 : 1; break;
            }
            try { Apply(graph, step); graph.Validate(); }
            catch (Exception error) { throw new InvalidOperationException("Invalid plan step " + i + ": " + Json(step), error); }
            plan.steps.Add(step);
        }
        return plan;
    }
    static void CopyEdge(Step step, ShaderGraphEdge edge)
    { step.node = edge.inputNode; step.port = edge.inputPort; step.source = edge.outputNode; step.sourcePort = edge.outputPort; }
    static void Connect(ShaderGraph graph, string from, string output, string to, string input)
    { graph.edges.Add(new ShaderGraphEdge { outputNode = from, outputPort = output, inputNode = to, inputPort = input }); }
    static void Apply(ShaderGraph graph, Step step)
    {
        var node = graph.FindNode(step.node) ?? throw new InvalidDataException("Missing node " + step.node);
        switch (step.action)
        {
            case "value": node.FindInput(step.port).value = Vector4.one * step.number; break;
            case "dimensions": Static("ShaderGraphNodes", "ChangeNoiseDimensions", graph, node, Enum.Parse(typeof(ShaderGraphNoiseDimensions), step.value)); break;
            case "algorithm":
                node.kind = (ShaderGraphNodeKind)Enum.Parse(typeof(ShaderGraphNodeKind), step.value);
                var ports = new HashSet<string>(((IEnumerable)Static("ShaderGraphNodes", "StoredInputs", node)).Cast<object>().Select(p => (string)Get(p, "name")));
                node.inputs.RemoveAll(input => !ports.Contains(input.port)); break;
            case "fractal": node.fractal = (ShaderGraphFractal)Enum.Parse(typeof(ShaderGraphFractal), step.value); break;
            case "lighting": node.option = (int)step.number; break;
            case "filter": node.noiseFiltering = step.number != 0; break;
            case "layout": node.position += new Vector2(step.number, 0); break;
            case "disconnect": graph.edges.RemoveAll(e => e.inputNode == step.node && e.inputPort == step.port); break;
            case "connect": Connect(graph, step.source, step.sourcePort, step.node, step.port); break;
            case "output": graph.edges.Single(e => e.inputNode == step.node && e.inputPort == step.port).outputPort = step.sourcePort; break;
            case "insert":
                var added = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", ShaderGraphNodeKind.Multiply, node.position + Vector2.left * 250);
                added.id = AddedId; added.FindInput("B").value = Vector4.one * .93f; graph.nodes.Add(added);
                graph.edges.RemoveAll(e => e.inputNode == step.node && e.inputPort == step.port);
                Connect(graph, step.source, step.sourcePort, AddedId, "A"); Connect(graph, AddedId, "Out", step.node, step.port); break;
            case "remove":
                graph.edges.RemoveAll(e => e.inputNode == AddedId || e.outputNode == AddedId); graph.nodes.RemoveAll(n => n.id == AddedId);
                Connect(graph, step.source, step.sourcePort, step.node, step.port); break;
            default: throw new InvalidDataException("Unknown edit " + step.action);
        }
    }

    public static string Model() => TestContext.Run("Frozen graph and reproducible edit plans", t =>
    {
        Read("lights.json", LightsHash);
        var graph = Load(); t.Equal(37, graph.nodes.Count, "Fixture nodes"); t.Equal(42, graph.edges.Count, "Fixture edges");
        var plan = MakePlan(1138, 24); t.Equal(Json(plan), Json(MakePlan(1138, 24)), "Same seed produces identical plan");
        t.True(Json(plan) != Json(MakePlan(1139, 24)), "Seed changes selected nodes/values");
        t.Equal(12, plan.steps.Select(s => s.action).Distinct().Count(), "All edit categories covered");
        var replay = FromJson<Plan>(Json(plan)); var other = Load();
        t.Equal(plan.steps.Count, replay.steps.Count, "Serialized plan retains every step");
        foreach (var step in plan.steps)
        {
            Apply(graph, step); graph.Validate(); Static("ShaderGraphRevision", "Invalidate", graph);
            // Compile HLSL text only: verifies the selected outputs/types, no native shaders or GPU work.
            var compilation = Static("ShaderGraphCompiler", "Compile", graph, null, "Out", false);
            t.True(!string.IsNullOrEmpty((string)Get(compilation, "source")), "Valid HLSL generation: " + step.action);
        }
        foreach (var step in replay.steps) Apply(other, step);
        t.True(Snapshot(graph) == Snapshot(other), "Serialized plan replays exactly");
        t.Equal(37, graph.nodes.Count, "Inserted node removed"); t.Equal(42, graph.edges.Count, "Disconnected edges restored");
    });

    #pragma warning disable CS0649 // Populated from the frozen light fixture.
    [Serializable] sealed class LightData
    {
        public string id, name, type, areaShape;
        public bool enabled;
        public float intensity, range, falloff, angle, softness, rotation;
        public float[] color, position, direction, size;
        public object cookie;
    }
    [Serializable] sealed class LightFile { public LightData[] lights; }
    #pragma warning restore CS0649
    sealed class Fixture : IDisposable
    {
        internal readonly WhimTexDocument document = ScriptableObject.CreateInstance<WhimTexDocument>();
        internal readonly ShaderLayerBehaviour behaviour = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph };
        internal readonly ShaderGraphWindow window = ScriptableObject.CreateInstance<ShaderGraphWindow>();
        internal readonly Dictionary<string, string> symbolMap = new Dictionary<string, string>();
        bool shown;
        internal Fixture(bool freshSymbols = false)
        {
            try { Initialize(freshSymbols); }
            catch { Dispose(); throw; }
        }
        void Initialize(bool freshSymbols)
        {
            document.hideFlags = window.hideFlags = HideFlags.HideAndDontSave;
            document.name = "Shader editing benchmark"; document.width = document.height = 512;
            behaviour.draftGraph = Load(); Layer layer = behaviour; Call(layer, "AssignNewId"); document.layers.Add(layer);
            if (freshSymbols)
            {
                string json = Snapshot(behaviour.draftGraph);
                foreach (var node in behaviour.draftGraph.nodes)
                {
                    string id = Guid.NewGuid().ToString("N"); symbolMap.Add(node.id, id);
                    json = json.Replace("\"" + node.id + "\"", "\"" + id + "\"");
                }
                behaviour.draftGraph = ParseGraph(json);
            }
            foreach (var l in FromJson<LightFile>(Read("lights.json", LightsHash)).lights)
                document.lights.Add(new WhimTexLight { id = l.id, name = l.name, enabled = l.enabled,
                    type = (WhimTexLightType)Enum.Parse(typeof(WhimTexLightType), l.type),
                    areaShape = (WhimTexAreaLightShape)Enum.Parse(typeof(WhimTexAreaLightShape), l.areaShape),
                    color = new Color(l.color[0], l.color[1], l.color[2], l.color[3]),
                    position = new Vector3(l.position[0], l.position[1], l.position[2]),
                    direction = new Vector3(l.direction[0], l.direction[1], l.direction[2]),
                    size = new Vector2(l.size[0], l.size[1]), intensity = l.intensity, range = l.range,
                    falloff = l.falloff, angle = l.angle, softness = l.softness, rotation = l.rotation,
                    cookie = l.cookie == null ? null : (Texture2D)Static("WhimTexDocumentJson", "ReadLightCookie", l.cookie) });
            Set(window, "document", document); Set(window, "layerId", layer.Id); Set(window, "svmPreviews", false);
        }
        internal void Show()
        { window.position = new Rect(60, 60, 1100, 760); window.ShowUtility(); shown = true; window.Focus(); }
        internal void Edit(Step step)
        {
            if (symbolMap.Count != 0)
            {
                string Map(string id) => id != null && symbolMap.TryGetValue(id, out var mapped) ? mapped : id;
                step = new Step { action = step.action, node = Map(step.node), source = Map(step.source), port = step.port,
                    sourcePort = step.sourcePort, value = step.value, number = step.number };
            }
            if (step.action == "layout")
                Call(window, "Edit", "Benchmark layout", (Action<ShaderGraph>)(g => Apply(g, step)), false, false, true);
            else if (step.action == "value")
                Call(window, "EditNode", step.node, "Benchmark value", (Action<ShaderGraphNode>)(n => n.FindInput(step.port).value = Vector4.one * step.number), false, false);
            else Call(window, "Edit", "Benchmark " + step.action, (Action<ShaderGraph>)(g => Apply(g, step)), true, true, false);
        }
        public void Dispose() => AsyncD.CleanupOwned(
            () => { if (window != null) { if (shown) window.Close(); else UnityEngine.Object.DestroyImmediate(window); } },
            () => Call((Layer)behaviour, "ReleaseTransientResources"),
            () => Undo.ClearUndo(document),
            () => UnityEngine.Object.DestroyImmediate(document));
    }

    [Serializable] public sealed class Sample
    {
        public int pass, index, visiblePreviews, maxActiveCompilers, requiredBufferPrograms;
        public string action, status = "running", error, shaderSourceHash, finalReadinessSignal;
        public double editCallMs, finalCompileReadyMs = -1, allVisibleCompileReadyMs = -1, maxObservationGapMs;
        public double requestObservedMs = -1, preparationStartedObservedMs = -1, preparationDoneObservedMs = -1, nativeStartedObservedMs = -1;
        public long nativeStartsToFinal, nativeStartsTotal, passRequestsTotal, cacheHitsTotal;
        public bool within3Seconds;
        public bool retainedFinalProgram;
    }
    [Serializable] public sealed class Report
    {
        public string runId, unity, graphics, device, startedUtc, fixtureHash = GraphHash, status = "running", error;
        public string cachePolicy = "Existing application/driver caches retained. Pass 0 is first traversal, NOT guaranteed cold. Pass 1 replays the same plan.";
        public string timing = "Edit entry to accepted current final HLSL runtime, including required Buffer programs. Resource notification for new programs; Editor-update polling for retained programs. Render/paint excluded. Stage observations are not CPU or GPU durations. Global counters include visible node previews.";
        public int seed, targetMs = 3000, idealMs = 2000;
        public double p50Ms, p95Ms, maxMs;
        public int overTarget;
        public List<Sample> samples = new List<Sample>();
    }
    static long Counter(string name) => Convert.ToInt64(Get(TypeOf("ShaderExecutableCache"), name));
    static ulong Stamp(ShaderGraph graph) => (ulong)Static("ShaderGraphRevision", "Fingerprint", graph);
    static bool FinalReady(Fixture fixture, ulong expected)
    {
        var state = Get(fixture.behaviour, "CurrentState");
        if ((bool)Get(state, "HasError")) throw new InvalidOperationException((string)Get(state, "Diagnostics"));
        var runtime = Get(fixture.behaviour, "Runtime");
        if ((bool)Get(fixture.window, "compileRequested") || runtime == null ||
            (bool)Get(state, "IsPending") || (bool)Call(state, "NeedsCompile", expected, true)) return false;
        var material = (Material)Get(runtime, "Material");
        if ((bool)Get(runtime, "UsesLighting")) Call(fixture.document, "BindLighting", material);
        return ShaderUtil.IsPassCompiled(material, 0);
    }
    static bool PreviewsReady(Fixture fixture, out int count)
    {
        count = 0; var canvas = Get(fixture.window, "canvas");
        if (canvas == null || !(bool)Get(canvas, "PreviewStateIsCurrent")) return false;
        bool ready = true; object state = Get(canvas, "previewState");
        foreach (DictionaryEntry pair in (IDictionary)Get(canvas, "previews"))
        {
            object preview = pair.Value; if (!(bool)Call(preview, "CanDisplay")) continue; count++;
            if ((bool)Get(preview, "renderError")) throw new InvalidOperationException("Preview failed: " + pair.Key);
            string port = (string)Get(Get(preview, "output"), "value");
            ready &= Get(preview, "pending") == null && Get(preview, "program") != null &&
                (ulong)Get(preview, "programFingerprint") == (ulong)Call(preview, "Fingerprint", state) &&
                (string)Get(preview, "programPort") == port;
        }
        return ready;
    }
    static Task NextUpdate(CancellationToken token)
    {
        var result = new TaskCompletionSource<bool>(); EditorApplication.CallbackFunction callback = null;
        callback = () => { EditorApplication.update -= callback; result.TrySetResult(true); };
        EditorApplication.update += callback;
        return AwaitUpdate(result, callback, token);
    }
    static async Task AwaitUpdate(TaskCompletionSource<bool> result, EditorApplication.CallbackFunction callback, CancellationToken token)
    {
        using (token.Register(() => result.TrySetCanceled()))
            try { await result.Task; } finally { EditorApplication.update -= callback; }
    }
    static void Observe(Fixture fixture, Sample sample, double ms)
    {
        sample.maxActiveCompilers = Math.Max(sample.maxActiveCompilers, (int)Counter("ActiveCount"));
        var request = Get(Get(Get(fixture.behaviour, "CurrentState"), "pending"), "request");
        if (request == null) return;
        if (sample.requestObservedMs < 0) sample.requestObservedMs = ms;
        var preparation = Get(request, "preparation");
        if (preparation != null)
        {
            if ((bool)Get(preparation, "started") && sample.preparationStartedObservedMs < 0) sample.preparationStartedObservedMs = ms;
            if ((bool)Get(preparation, "complete") && sample.preparationDoneObservedMs < 0) sample.preparationDoneObservedMs = ms;
        }
        var executable = Get(request, "executable");
        if (executable != null && (bool)Get(executable, "started") && sample.nativeStartedObservedMs < 0) sample.nativeStartedObservedMs = ms;
    }
    static async Task Measure(Fixture fixture, Step step, Sample sample, CancellationToken token)
    {
        long starts = Counter("NativeCompilations"), passes = Counter("PassCompilationRequests"), hits = Counter("CacheHits");
        object previousRuntime = Get(fixture.behaviour, "Runtime");
        var clock = new Stopwatch(); double previous = 0; ulong expected = 0; bool armed = false;
        Exception notificationError = null;
        void CaptureFinal(string signal)
        {
            if (sample.finalCompileReadyMs >= 0 || !armed || !FinalReady(fixture, expected)) return;
            sample.finalCompileReadyMs = clock.Elapsed.TotalMilliseconds; sample.finalReadinessSignal = signal;
            sample.nativeStartsToFinal = Counter("NativeCompilations") - starts;
            sample.retainedFinalProgram = ReferenceEquals(previousRuntime, Get(fixture.behaviour, "Runtime"));
        }
        Action<WhimTexDocument> completed = doc =>
        {
            if (doc != fixture.document) return;
            // Observation failures belong to the test, never to the production notification loop.
            try { CaptureFinal("resource-notification"); } catch (Exception error) { notificationError = error; }
        };
        var notification = typeof(WhimTexDocument).GetEvent("RenderResourcesChanged", Flags);
        notification.GetAddMethod(true).Invoke(null, new object[] { completed });
        try
        {
            clock.Start();
            if (step != null) fixture.Edit(step);
            sample.editCallMs = clock.Elapsed.TotalMilliseconds; expected = Stamp(fixture.behaviour.draftGraph); armed = true;
            do
            {
                await NextUpdate(token); token.ThrowIfCancellationRequested();
                if (notificationError != null) throw notificationError;
                double now = clock.Elapsed.TotalMilliseconds;
                sample.maxObservationGapMs = Math.Max(sample.maxObservationGapMs, now - previous); previous = now;
                Observe(fixture, sample, now);
                CaptureFinal("editor-update");
                if (sample.finalCompileReadyMs >= 0 && sample.shaderSourceHash == null)
                {
                    var runtime = Get(fixture.behaviour, "Runtime");
                    sample.requiredBufferPrograms = ((Array)Get(runtime, "bufferExecutables"))?.Length ?? 0;
                    using (var sha = SHA256.Create()) sample.shaderSourceHash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes((string)Get(runtime, "Source")))).Replace("-", "").ToLowerInvariant();
                }
                bool previews = PreviewsReady(fixture, out sample.visiblePreviews);
                if (sample.finalCompileReadyMs >= 0 && previews)
                { sample.allVisibleCompileReadyMs = clock.Elapsed.TotalMilliseconds; sample.status = "complete"; break; }
                if (now > 90000)
                {
                    var canvas = Get(fixture.window, "canvas"); var details = new List<string>();
                    foreach (DictionaryEntry pair in (IDictionary)Get(canvas, "previews"))
                    {
                        object p = pair.Value; if (!(bool)Call(p, "CanDisplay")) continue;
                        object pending = Get(p, "pending"), prep = Get(pending, "preparation"), executable = Get(pending, "executable");
                        details.Add(pair.Key + ": pending=" + (Get(p, "pending") != null) + ", program=" + (Get(p, "program") != null) +
                            ", fingerprint=" + Get(p, "programFingerprint") + "/" + Call(Get(canvas, "previewState"), "Fingerprint", (string)pair.Key) +
                            ", port=" + Get(p, "programPort") + "/" + Get(Get(p, "output"), "value") + ", wanted=" + Call(p, "WantsCompilation") +
                            ", preparation=" + (prep == null ? "none" : Get(prep, "started") + "/" + Get(prep, "complete") + "/" + Get(prep, "cancelled")) +
                            ", native=" + (executable == null ? "none" : Get(executable, "started") + "/" + Get(executable, "ready") + "/" + Get(executable, "Rank")));
                    }
                    throw new TimeoutException("Compilation readiness deadline exceeded. " + string.Join("; ", details));
                }
            } while (true);
            sample.within3Seconds = sample.finalCompileReadyMs <= 3000;
        }
        catch (Exception error) { sample.status = token.IsCancellationRequested ? "cancelled" : "failed"; sample.error = error.ToString(); throw; }
        finally
        {
            notification.GetRemoveMethod(true).Invoke(null, new object[] { completed });
            sample.nativeStartsTotal = Counter("NativeCompilations") - starts;
            sample.passRequestsTotal = Counter("PassCompilationRequests") - passes;
            sample.cacheHitsTotal = Counter("CacheHits") - hits;
        }
    }
    public static string Start(string runId, int seed, int steps, int repeats) => Run(runId, MakePlan(seed, steps), repeats);
    public static string ColdEdits(string runId) => Run(runId, MakePlan(1138, 12), 1, true);
    public static string Replay(string runId, string planPath, int repeats) => Run(runId, FromJson<Plan>(File.ReadAllText(planPath)), repeats);
    static string Run(string runId, Plan plan, int repeats, bool freshSymbols = false) => AsyncD.Start(runId, async (t, token) =>
    {
        if (plan.version != 1 || plan.fixtureHash != GraphHash) throw new InvalidDataException("Incompatible edit plan or fixture.");
        if (plan.steps.Count < 1 || plan.steps.Count > 24 || repeats < 1 || repeats > 2) throw new ArgumentOutOfRangeException("Bounded diagnostic: 1..24 edits, 1..2 traversals.");
        var report = new Report { runId = runId, seed = plan.seed, unity = Application.unityVersion,
            graphics = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName, startedUtc = DateTime.UtcNow.ToString("O") };
        if (freshSymbols) report.cachePolicy = "Fresh node identifiers per traversal generate previously unseen HLSL symbols. Application/driver caches are not flushed; edits within the traversal retain normal dependency reuse. Original identifiers and source hashes are recorded.";
        string directory = MigrationD.ProjectPath("Temp/WhimTex/shader-editing/" + runId); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plan.json"), Json(plan));
        void Save() => File.WriteAllText(Path.Combine(directory, "measurements.json"), Json(report));
        try
        {
            for (int pass = 0; pass < repeats; pass++)
                using (var fixture = new Fixture(freshSymbols))
                {
                    File.WriteAllText(Path.Combine(directory, "symbols-" + pass + ".json"), Json(fixture.symbolMap));
                    fixture.Show(); await NextUpdate(token); await NextUpdate(token);
                    ((GraphView)Get(fixture.window, "canvas")).FrameAll();
                    // Initial open is separate from every edit sample; no artificial cache flush or HLSL nonce.
                    var opening = new Sample { pass = pass, index = -1, action = "open-settle" }; report.samples.Add(opening);
                    await Measure(fixture, null, opening, token); Save();
                    t.True(opening.visiblePreviews > 0, "Actual visible node preview demand exists");
                    for (int index = 0; index < plan.steps.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        var sample = new Sample { pass = pass, index = index, action = plan.steps[index].action }; report.samples.Add(sample);
                        await Measure(fixture, plan.steps[index], sample, token); Save();
                        t.True(sample.finalCompileReadyMs >= sample.editCallMs, "Current final program accepted after edit");
                        t.True(sample.allVisibleCompileReadyMs >= sample.finalCompileReadyMs, "Secondary completion follows primary boundary");
                        if (sample.action == "layout" || sample.action == "value")
                            t.Equal(0L, sample.nativeStartsTotal, "Layout/existing uniform edit starts no native shader compilation");
                    }
                }
            var times = report.samples.Where(s => s.index >= 0).Select(s => s.finalCompileReadyMs).OrderBy(x => x).ToArray();
            report.p50Ms = times[(int)Math.Ceiling(times.Length * .5) - 1]; report.p95Ms = times[(int)Math.Ceiling(times.Length * .95) - 1];
            report.maxMs = times.Last(); report.overTarget = times.Count(x => x > report.targetMs); report.status = "complete";
        }
        catch (Exception error) { report.status = token.IsCancellationRequested ? "cancelled" : "failed"; report.error = error.ToString(); throw; }
        finally { Save(); }
    });
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    public static string RenderPair(string runId) => AsyncD.Start(runId, async (t, token) =>
    {
        string baseline = File.ReadAllText(MigrationD.ProjectPath("Temp/WhimTex/shader-cold/baseline.txt")).Trim();
        string dir = MigrationD.ProjectPath("Temp/WhimTex/shader-render-pair/" + runId); Directory.CreateDirectory(dir);
        using var fixture = new Fixture(); var graph = fixture.behaviour.draftGraph;
        object[] programs = new object[2]; object request = null;
        RenderTexture target = null; Texture2D readback = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            for (int variant = 0; variant < 2; variant++)
            {
                var compilation = Static("ShaderGraphCompiler", "Compile", graph, null, "Out", false);
                object[] args = { compilation, true, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, null };
                string source = (string)Static("ShaderExecutableCache", "Prepare", args);
                if (variant == 0)
                {
                    source = File.ReadAllText(Path.Combine(baseline, "final-original.shader.txt"));
                    int index = 0;
                    foreach (object pass in (IEnumerable)Get(compilation, "buffers"))
                        Set(pass, "preparedSource", File.ReadAllText(Path.Combine(baseline, "final-original-buffer" + index++ + ".shader.txt")));
                }
                request = Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { graph, compilation, Stamp(graph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, false, true, source, args[4] }, null);
                Call(request, "WithLighting", fixture.document);
                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    object[] result = { null };
                    if ((bool)Call(request, "TryComplete", result)) { programs[variant] = result[0]; break; }
                    if (deadline.Elapsed.TotalSeconds > 60) throw new TimeoutException("Paired render compile");
                    await NextUpdate(token); token.ThrowIfCancellationRequested();
                }
                ((IDisposable)request).Dispose(); request = null;
            }
            target = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
            readback = new Texture2D(512, 512, TextureFormat.RGBAFloat, false, true);
            var state = Activator.CreateInstance(TypeOf("ShaderGraphBindingState"), true); Call(state, "Prepare", graph);
            var context = Activator.CreateInstance(TypeOf("LayerRenderContext"), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new object[] { fixture.document, null, 512, 512, 1f, false, false, false }, null);
            context = Call(context, "WithShaderGraphPreviewTime", (float?)2.5f);
            foreach (var program in programs)
            {
                Call(program, "Bind", graph, context, state, true, false);
                Call(program, "PrepareCoordinates", (Layer)fixture.behaviour, context);
                t.True(ShaderUtil.IsPassCompiled((Material)Get(program, "Material"), 0), "Active lighting variant ready");
            }
            double Draw(int variant)
            {
                var clock = Stopwatch.StartNew(); GL.sRGBWrite = false;
                Call(programs[variant], "Render", Texture2D.blackTexture, target, context);
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); readback.Apply();
                RenderTexture.active = previous; GL.sRGBWrite = srgb; return clock.Elapsed.TotalMilliseconds;
            }
            for (int i = 0; i < 6; i++) { Draw(0); Draw(1); }
            Draw(0); byte[] expected = readback.GetRawTextureData<byte>().ToArray();
            Draw(1); t.True(expected.SequenceEqual(readback.GetRawTextureData<byte>().ToArray()), "Paired original frame is bit exact");
            double[][] times = { new double[31], new double[31] };
            for (int i = 0; i < 31; i++)
            { int first = i & 1; times[first][i] = Draw(first); times[1-first][i] = Draw(1-first); }
            var medians = times.Select(a => a.OrderBy(x => x).ElementAt(15)).ToArray();
            File.WriteAllText(Path.Combine(dir, "measurements.json"), Json(new { baseline, times, beforeMs = medians[0], afterMs = medians[1],
                protocol = "31 alternating pairs, six warmups per shader, actual Cookie variant, cached unchanged buffers; CPU submission + synchronized readback proxy, not GPU timestamps." }));
        }
        finally
        {
            (request as IDisposable)?.Dispose(); foreach (var program in programs) (program as IDisposable)?.Dispose();
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
        }
    });

    public static string RenderParity(string runId, string label, string reference) => AsyncD.Start(runId, async (t, token) =>
    {
        string baselineFile = MigrationD.ProjectPath("Temp/WhimTex/shader-parity/baseline.txt");
        if (reference == "baseline") reference = File.ReadAllText(baselineFile).Trim();
        string dir = MigrationD.ProjectPath("Temp/WhimTex/shader-parity/" + label + "-" + runId); Directory.CreateDirectory(dir);
        var rows = new List<object>(); int differingFrames = 0;
        using var fixture = new Fixture();
        RenderTexture target = null; Texture2D readback = null; object program = null, request = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            target = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
            readback = new Texture2D(512, 512, TextureFormat.RGBAFloat, false, true);
            var graph = fixture.behaviour.draftGraph; var plan = MakePlan(1138, 12);
            for (int index = -1; index < plan.steps.Count; index++)
            {
                if (index >= 0) { Apply(graph, plan.steps[index]); Static("ShaderGraphRevision", "Invalidate", graph); }
                request = Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { graph, null, "Out", false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", true }, null);
                bool variantAware = request.GetType().GetMethod("WithLighting", Flags) != null;
                if (variantAware) Call(request, "WithLighting", fixture.document);
                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    object[] result = { null };
                    if ((bool)Call(request, "TryComplete", result)) { program = result[0]; break; }
                    if (deadline.Elapsed.TotalSeconds > 60) throw new TimeoutException("Parity compile " + index);
                    await NextUpdate(token);
                }
                ((IDisposable)request).Dispose(); request = null;
                File.WriteAllText(Path.Combine(dir, index.ToString("D2") + ".shader.txt"), (string)Get(program, "Source"));
                var state = Activator.CreateInstance(TypeOf("ShaderGraphBindingState"), true); Call(state, "Prepare", graph);
                var context = Activator.CreateInstance(TypeOf("LayerRenderContext"), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new object[] { fixture.document, null, 512, 512, 1f, false, false, false }, null);
                context = Call(context, "WithShaderGraphPreviewTime", (float?)2.5f);
                Call(program, "Bind", graph, context, state, true, false); Call(program, "PrepareCoordinates", (Layer)fixture.behaviour, context);
                var material = (Material)Get(program, "Material");
                if (variantAware) t.True(ShaderUtil.IsPassCompiled(material, 0), "Actual lighting variant is ready before rendering");
                bool async = ShaderUtil.allowAsyncCompilation;
                try { ShaderUtil.allowAsyncCompilation = true; ShaderUtil.CompilePass(material, 0, false); }
                finally { ShaderUtil.allowAsyncCompilation = async; }
                while (!ShaderUtil.IsPassCompiled(material, 0))
                { if (deadline.Elapsed.TotalSeconds > 90) throw new TimeoutException("Lighting variant " + index); await NextUpdate(token); }
                t.True(!ShaderUtil.ShaderHasError(material.shader), "Active lighting variant compiles");
                double Draw()
                {
                    var timer = Stopwatch.StartNew(); GL.sRGBWrite = false;
                    Call(program, "Render", Texture2D.blackTexture, target, context);
                    RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); readback.Apply();
                    RenderTexture.active = previous; GL.sRGBWrite = srgb; return timer.Elapsed.TotalMilliseconds;
                }
                Draw(); var timing = new double[5]; for (int j = 0; j < timing.Length; j++) timing[j] = Draw(); Array.Sort(timing);
                var bytes = readback.GetRawTextureData<byte>().ToArray(); string file = index.ToString("D2") + ".rgba.gz";
                using (var stream = File.Create(Path.Combine(dir, file)))
                using (var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress)) zip.Write(bytes, 0, bytes.Length);
                double maxError = 0; int bad = 0;
                if (!string.IsNullOrEmpty(reference))
                {
                    using var stream = File.OpenRead(Path.Combine(reference, file));
                    using var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
                    using var memory = new MemoryStream(); zip.CopyTo(memory); byte[] expected = memory.ToArray(); t.Equal(bytes.Length, expected.Length, "Capture size");
                    for (int j = 0; j < bytes.Length; j += 4)
                    {
                        float a = BitConverter.ToSingle(expected, j), b = BitConverter.ToSingle(bytes, j);
                        if (!float.IsFinite(a) || !float.IsFinite(b)) { if (BitConverter.ToInt32(expected,j) != BitConverter.ToInt32(bytes,j)) bad++; continue; }
                        double error = Math.Abs((double)a-b); maxError = Math.Max(maxError,error);
                        if (error > .0002 * (1 + Math.Abs(a))) bad++;
                    }
                }
                rows.Add(new { index, action = index < 0 ? "original" : plan.steps[index].action, medianSubmissionReadbackMs = timing[2], maxError, bad,
                    keywords = material.shaderKeywords });
                File.WriteAllText(Path.Combine(dir, "results.json"), Json(new { label, reference, rows }));
                if (bad != 0) differingFrames++;
                ((IDisposable)program).Dispose(); program = null;
            }
            if (label == "before") File.WriteAllText(baselineFile, dir);
            t.Equal(0, differingFrames, "Every full 512x512 float frame matches the reference");
        }
        finally
        {
            (request as IDisposable)?.Dispose(); (program as IDisposable)?.Dispose(); RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
        }
    });

    public static string ColdStudy(string runId, string label) => AsyncD.Start(runId, async (t, token) =>
    {
        if (label != "before" && label != "after") throw new ArgumentException("Unknown study label");
        t.True(!ShaderUtil.disableShaderOptimization, "Normal shader optimization is enabled");
        string dir = MigrationD.ProjectPath("Temp/WhimTex/shader-cold/" + label + "-" + runId);
        Directory.CreateDirectory(dir);
        var rows = new List<object>();
        var idleClock = Stopwatch.StartNew(); int idleUpdates = 0;
        while (idleUpdates < 4)
        {
            await NextUpdate(token);
            idleUpdates = ShaderUtil.anythingCompiling || Counter("ActiveCount") != 0 ? 0 : idleUpdates + 1;
            if (idleClock.Elapsed.TotalSeconds > 60) throw new TimeoutException("Other shader work did not settle before the native study.");
        }
        void Save() => File.WriteAllText(Path.Combine(dir, "measurements.json"), Json(new { label, unity = Application.unityVersion,
            graphics = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName, fixtureHash = GraphHash,
            protocol = "Unique fragment entry identifier per submission; math and compiler flags unchanged. Isolated native compilation, not edit-to-ready. Driver cache is not cleared.", rows }));
        async Task CompileSource(string source, string name, double prepareMs)
        {
            File.WriteAllText(Path.Combine(dir, name + ".shader.txt"), source);
            string unique = "WTCold_" + Guid.NewGuid().ToString("N");
            string salted = source.Replace("SpriteFXFragment", unique);
            Shader shader = null; Material material = null;
            double createMs = 0, submitMs = 0; var clock = Stopwatch.StartNew();
            try
            {
                bool async = ShaderUtil.allowAsyncCompilation;
                try
                {
                    ShaderUtil.allowAsyncCompilation = true;
                    shader = ShaderUtil.CreateShaderAsset(salted, false);
                    if (shader == null) throw new InvalidOperationException("Cannot create study shader: " + name);
                    shader.hideFlags = HideFlags.HideAndDontSave;
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    createMs = clock.Elapsed.TotalMilliseconds;
                    ShaderUtil.CompilePass(material, 0, false);
                    submitMs = clock.Elapsed.TotalMilliseconds - createMs;
                }
                finally { ShaderUtil.allowAsyncCompilation = async; }
                while (!ShaderUtil.IsPassCompiled(material, 0))
                {
                    if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException(string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
                    if (clock.Elapsed.TotalSeconds > 60) throw new TimeoutException(name);
                    await NextUpdate(token); token.ThrowIfCancellationRequested();
                }
                t.True(!ShaderUtil.ShaderHasError(shader), name + ": " + string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
                rows.Add(new { name, sourceChars = source.Length, prepareMs, createMs, submitMs, nativeReadyMs = clock.Elapsed.TotalMilliseconds, unique }); Save();
            }
            finally
            { if (material != null) UnityEngine.Object.DestroyImmediate(material); if (shader != null) UnityEngine.Object.DestroyImmediate(shader); }
        }
        async Task MeasureGraph(ShaderGraph graph, string name, string node = null, string port = "Out")
        {
            var clock = Stopwatch.StartNew();
            var compilation = Static("ShaderGraphCompiler", "Compile", graph, node, port, false);
            object[] args = { compilation, true, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, null };
            string source = (string)Static("ShaderExecutableCache", "Prepare", args);
            double prepareMs = clock.Elapsed.TotalMilliseconds;
            await CompileSource(source, name, prepareMs);
            int index = 0;
            foreach (object buffer in (IEnumerable)Get(compilation, "buffers"))
                await CompileSource((string)Get(buffer, "preparedSource"), name + "-buffer" + index++, 0);
        }
        try
        {
            var graph = Load();
            await MeasureGraph(graph, "final-original");
            foreach (var node in graph.nodes.Where(n => n.kind == ShaderGraphNodeKind.ShardNoise || n.kind == ShaderGraphNodeKind.ValueCubicNoise || n.kind == ShaderGraphNodeKind.Fractal || n.kind == ShaderGraphNodeKind.HeightToNormal || n.kind == ShaderGraphNodeKind.NormalLighting))
            {
                string port = ((string[])Static("ShaderGraphNodes", "StoredOutputs", node))[0];
                await MeasureGraph(graph, "branch-" + node.kind + "-" + node.id.Substring(0, 5), node.id, port);
            }
            var plan = MakePlan(1138, 12);
            for (int i = 0; i < plan.steps.Count; i++)
            {
                Apply(graph, plan.steps[i]); Static("ShaderGraphRevision", "Invalidate", graph);
                if (i == 7 || i == 8 || i == 11) await MeasureGraph(graph, "edit-" + plan.steps[i].action);
            }
            if (label == "before") File.WriteAllText(MigrationD.ProjectPath("Temp/WhimTex/shader-cold/baseline.txt"), dir);
        }
        catch (Exception error) { rows.Add(new { error = error.ToString() }); throw; }
        finally { Save(); }
    });
}
