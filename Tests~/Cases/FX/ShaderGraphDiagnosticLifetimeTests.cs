using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

public static class ShaderGraphDiagnosticLifetimeTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type WT(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object v) => o.GetType().GetField(name, F).SetValue(o, v);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static object Static(string type, string name, params object[] args) => WT(type).GetMethod(name, F).Invoke(null, args);
    static object New(Type type, params object[] args) => Activator.CreateInstance(type, F, null, args, null);
    static int Count(object o) => ((IEnumerable)o).Cast<object>().Count();
    static ulong Stamp(ShaderGraph graph) => (ulong)Static("ShaderGraphRevision", "Fingerprint", graph);
    public static string Start(string id)
    {
        var job = AsyncFixture.Create(id);
        job.Worker = FixtureContext.RunAsync(job, async token =>
        {
            var t = job.Context;
            var doc = job.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            doc.width = doc.height = 32;
            var graph = new ShaderGraph();
            ShaderGraphNode Node(ShaderGraphNodeKind kind)
            {
                var n = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
                graph.nodes.Add(n); return n;
            }
            var bad = Node(ShaderGraphNodeKind.Expression); bad.expression = "1 +";
            var add = Node(ShaderGraphNodeKind.Add);
            var output = Node(ShaderGraphNodeKind.Output);
            var input = new ShaderGraphEdge { outputNode = bad.id, outputPort = "Out", inputNode = add.id, inputPort = "A" };
            graph.edges.Add(input);
            graph.edges.Add(new ShaderGraphEdge { outputNode = add.id, outputPort = "Out", inputNode = output.id, inputPort = "RGBA" });
            var behaviour = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
            Layer layer = behaviour; doc.layers.Add(layer); Call(doc, "NormalizeModel");
            var window = job.Scope.Own(ScriptableObject.CreateInstance<ShaderGraphWindow>());
            Set(window, "document", doc); Set(window, "layerId", layer.Id); Set(window, "svmPreviews", false);
            var canvas = New(typeof(ShaderGraphWindow).GetNestedType("Canvas", F), window);
            Set(window, "canvas", canvas);
            var preview = New(typeof(ShaderGraphWindow).GetNestedType("NodePreview", F), window, canvas, add.id);
            ((IDisposable)Get(preview, "source")).Dispose();
            // A detached owned view keeps the user's graph/focus unchanged. Explicit demand drives its real source.
            var source = New(typeof(ShaderGraphWindow).GetNestedType("NodePreviewSource", F), window, canvas, add.id, (Func<bool>)(() => true));
            Set(preview, "source", source);
            var state = Get(canvas, "previewState");
            job.OwnCleanup(() => { ((IDisposable)preview).Dispose(); ((IDisposable)canvas).Dispose(); Call(layer, "ReleaseTransientResources"); });
            void Changed()
            {
                Static("ShaderGraphRevision", "Invalidate", graph);
                Call(canvas, "InvalidatePreparation"); Call(state, "Prepare", graph);
                Call(preview, "InvalidateCode", Call(state, "PreviewFingerprint", add.id, false));
            }
            async Task Render(bool error)
            {
                double end = EditorApplication.timeSinceStartup + 30;
                do
                {
                    token.ThrowIfCancellationRequested();
                    Call(source, "Tick", state, "Out", 16, true); Call(preview, "SyncSource", false);
                    if (error ? Count(Get(source, "Messages")) > 0 : (bool)Get(source, "HasImage") && !(bool)Get(source, "IsUpdating")) return;
                    await Task.Delay(25, token);
                } while (EditorApplication.timeSinceStartup < end);
                throw new TimeoutException("Preview did not reach " + (error ? "diagnostic" : "rendered") + " state.");
            }
            Changed(); await Render(true);
            t.True(Count(Get(source, "Messages")) > 0, "Reachable invalid expression produces a real diagnostic");
            var producers = (IDictionary)Get(canvas, "messageSources");
            t.True(producers.Contains("preview:" + add.id), "View publishes the source error");
            Call(preview, "SetExpanded", false);
            graph.edges.Remove(input); Changed();
            t.Equal(0, Count(Get(source, "Messages")), "Rewire invalidates diagnostics before rendering, including collapsed previews");
            t.True(!producers.Contains("preview:" + add.id), "Collapsed view removes its upstream badge producer immediately");
            Call(source, "Tick", state, "Out", 0, false);
            t.Equal(0, Count(Get(source, "Messages")), "Hidden preview cannot republish obsolete errors");
            await Render(false);
            t.Equal(0, Count(Get(source, "Messages")), "Disconnected invalid expression does not poison the valid branch");
            var retained = Get(source, "Texture");
            graph.edges.Add(input); Changed();
            t.True(ReferenceEquals(retained, Get(source, "Texture")), "Invalidation retains the last rendered texture");
            await Render(true);
            t.True(producers.Contains("preview:" + add.id), "Reconnecting the broken branch reports its error again");
            Call(source, "Suspend"); Call(preview, "SyncSource", false);
            t.Equal(0, Count(Get(source, "Messages")), "Suspension releases diagnostics together with the old program");
            t.True(!producers.Contains("preview:" + add.id), "Suspension removes the old badge producer");
            graph.edges.Remove(input); Changed(); Set(window, "svmPreviews", true); await Render(false);
            t.True((bool)Get(source, "DisplayedSVM"), "Corrected branch can resume through SVM");
            t.Equal(0, Count(Get(source, "Messages")), "SVM does not inherit native errors");
            // Supersede an actual preparation request before its failure can be delivered.
            Set(window, "svmPreviews", false); graph.edges.Add(input); Changed();
            Call(source, "Tick", state, "Out", 16, true);
            graph.edges.Remove(input); Changed(); await Render(false);
            t.Equal(0, Count(Get(source, "Messages")), "Superseded preparation cannot restore an old connection's error");
            Call(canvas, "ShowError", add.id, "Previous function operation failed");
            add.kind = ShaderGraphNodeKind.Multiply; Changed(); Call(canvas, "RefreshDiagnosticSources");
            t.True(!producers.Contains("function:" + add.id), "Operation diagnostics also expire when their branch changes");
            add.kind = ShaderGraphNodeKind.Add; Changed();

            // Main layer/FX state: restoring the old executable must not mark a newer draft as compiled.
            var program = new ShaderProgram { mode = ShaderProgramMode.Graph, draftGraph = graph };
            using var runtime = (IDisposable)Static("ShaderProgramRuntime", "CompileDraft", program, ShaderProgramMode.Graph,
                "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, null);
            ulong good = Stamp(graph);
            var compilation = New(WT("ShaderCompilationState"));
            Call(compilation, "Succeed", good, runtime, false);
            graph.edges.Add(input); Static("ShaderGraphRevision", "Invalidate", graph); ulong broken = Stamp(graph);
            Call(compilation, "RecordAttempt", broken);
            Call(compilation, "Fail", new InvalidOperationException("Failed draft"), true, null, null);
            Call(compilation, "Invalidate"); // releasing/reloading native resources
            Call(compilation, "Succeed", good, runtime, true);
            t.True((bool)Get(compilation, "HasError"), "Restoring working resources preserves current draft failure");
            t.True(!(bool)Call(compilation, "NeedsCompile", broken, true), "Restoration does not retry a known failed draft every frame");
            graph.edges.Remove(input); Static("ShaderGraphRevision", "Invalidate", graph);
            Call(compilation, "ObserveGraph", Stamp(graph));
            t.True(!(bool)Get(compilation, "HasError"), "Changed draft discards obsolete failure state");
            t.Equal(0, Count(Get(compilation, "Messages")), "Changed draft discards obsolete node messages");
            t.True((bool)Call(compilation, "NeedsCompile", good, true), "Changed draft still requires validation");
            string saved = JsonUtility.ToJson(compilation);
            Call(compilation, "RecordAttempt", broken); Call(compilation, "Fail", new Exception("Later failure"), true, null, null);
            JsonUtility.FromJsonOverwrite(saved, compilation);
            t.Equal(0, Count(Get(compilation, "Messages")), "Undo overwrite cannot retain the later draft's transient node messages");

            // Exercise both actual owners, not only the shared state object.
            foreach (bool fx in new[] { false, true })
            {
                var effect = fx ? job.Scope.Own(ScriptableObject.CreateInstance<ShaderFX>()) : null;
                if (fx) { Call(effect, "ReplaceProgram", program); Call(effect, "AcceptProgram", runtime, false, false); }
                else Call(behaviour, "AcceptCompiled", runtime, ShaderProgramMode.Graph, false, false);
                var owner = fx ? (object)effect : behaviour;
                graph.edges.Add(input); Static("ShaderGraphRevision", "Invalidate", graph);
                if (fx) { Set(effect, "runtime", null); Call(effect, "AcceptProgram", runtime, true, false); }
                else { Set(Get(behaviour, "CurrentState"), "runtime", null); Call(behaviour, "AcceptCompiled", runtime, ShaderProgramMode.Graph, true, false); }
                t.True((bool)Call(Get(owner, "CompilationStatus"), "NeedsCompile", Stamp(graph), true),
                    "Restoring working shader does not mark a different draft as checked: " + (fx ? "FX" : "layer"));
                t.True(!(bool)Call(owner, "TryRetainUnchangedGraph"), "A restored different working shader cannot bypass draft validation: " + (fx ? "FX" : "layer"));
                var status = Get(owner, "CompilationStatus");
                Call(status, "RecordAttempt", Stamp(graph)); Call(status, "Fail", new Exception("Old connection"), true, null, null);
                var unrelated = Node(ShaderGraphNodeKind.UV); Call(owner, "InvalidateGraph");
                t.True(!(bool)Call(owner, "TryRetainUnchangedGraph"), "Unrelated edit cannot bless an unvalidated failed branch: " + (fx ? "FX" : "layer"));
                graph.nodes.Remove(unrelated);
                graph.edges.Remove(input); Call(owner, "InvalidateGraph");
                t.True(!(bool)Get(Get(owner, "CompilationStatus"), "HasError"), "Owner clears old errors on rewire: " + (fx ? "FX" : "layer"));
                // Detach borrowed runtime: the using declaration owns it.
                if (fx) Set(effect, "runtime", null); else Set(Get(behaviour, "CurrentState"), "runtime", null);
            }
        });
        return job.Read();
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);
    public static string Caustics() => FixtureContext.Run("Caustics compiler diagnostics", () =>
    {
        for (int variant = 0; variant < 8; variant++)
        {
            var graph = new ShaderGraph();
            ShaderGraphNode Node(ShaderGraphNodeKind kind)
            {
                var node = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
                graph.nodes.Add(node); return node;
            }
            var noise = Node(ShaderGraphNodeKind.CausticsNoise);
            noise.option = variant % 2; noise.noisePeriodic = true; noise.noiseFiltering = (variant & 2) != 0;
            var output = Node(ShaderGraphNodeKind.Output);
            var source = noise; string port = "Value";
            if ((variant & 4) != 0)
            {
                source = Node(ShaderGraphNodeKind.Fractal);
                graph.edges.Add(new ShaderGraphEdge { outputNode = noise.id, outputPort = port, inputNode = source.id, inputPort = "In" });
                port = "Out";
            }
            graph.edges.Add(new ShaderGraphEdge { outputNode = source.id, outputPort = port, inputNode = output.id, inputPort = "RGBA" });
            using var runtime = (IDisposable)Static("ShaderProgramRuntime", "Create", graph, null, "Out", false);
            var messages = ((IEnumerable)Get(runtime, "DiagnosticMessages")).Cast<object>()
                .Where(d => Get(d, "Severity").ToString() != "Info").Select(d => Get(d, "Message").ToString()).ToArray();
            FixtureContext.Context.Equal(0, messages.Length, "Caustics variant " + variant + ": " + string.Join("; ", messages));
        }
    });
}
