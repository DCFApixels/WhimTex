using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

// Runs actual native/SVM node previews in an owned window. No scene/assets or user graph edits.
public static class ShaderGraphHeightPreviewTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Get(object o, string n) => o.GetType().GetField(n, F)?.GetValue(o) ?? o.GetType().GetProperty(n, F)?.GetValue(o);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static ShaderGraphNode Node(ShaderGraphNodeKind kind, Vector2 p) => (ShaderGraphNode)typeof(ShaderGraph).Assembly
        .GetType("DCFApixels.WhimTex.ShaderGraphNodes").GetMethod("Create", F).Invoke(null, new object[] { kind, p });
    static async Task Until(Func<bool> predicate, CancellationToken token, string message, double seconds = 60)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!predicate())
        {
            token.ThrowIfCancellationRequested();
            if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(message);
            await Task.Delay(35, token);
        }
    }
    static Color[] Read(RenderTexture source)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = source; cpu.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); cpu.Apply(); return cpu.GetPixels(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(cpu); }
    }
    static void Fence(Texture source, RenderTexture one, Texture2D cpu)
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try { GL.sRGBWrite = false; Graphics.Blit(source, one); RenderTexture.active = one; cpu.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }
    static double Median(List<double> samples) { samples.Sort(); return samples[samples.Count / 2]; }
    [Serializable] sealed class Measurements
    {
        public bool svm; public int nodes, previewSize; public long extraBytes;
        public double imageCpuMs, heightCpuMs, imageCompletedMs, heightCompletedMs, cachedCpuMs;
        public double imageP95Ms, heightP95Ms;
    }
    public static string Start(string id, bool svm, int count)
    {
        var job = AsyncFixture.Create(id);
        job.Worker = FixtureContext.RunAsync(job, async token =>
        {
            var t = job.Context; var scope = job.Scope;
            var doc = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = doc.height = 8192;
            var graph = new ShaderGraph();
            var nodes = new List<ShaderGraphNode>();
            for (int i = 0; i < count; i++)
            {
                var node = Node(i == count - 1 ? ShaderGraphNodeKind.CellularNoise : ShaderGraphNodeKind.Add, new Vector2((i % 8) * 260, (i / 8) * 450));
                if (node.kind == ShaderGraphNodeKind.Add)
                {
                    foreach (var input in node.inputs) input.type = ShaderGraphValueType.Float4;
                    node.FindInput("A").value = new Vector4(.25f, 2.5f + i * .01f, -.3f, .6f);
                    node.FindInput("B").value = Vector4.zero;
                }
                nodes.Add(node); graph.nodes.Add(node);
            }
            var output = Node(ShaderGraphNodeKind.Output, new Vector2(0, 0)); graph.nodes.Add(output);
            graph.edges.Add(new ShaderGraphEdge { outputNode = nodes[0].id, inputNode = output.id, inputPort = "RGBA" });
            graph.Validate();
            var behaviour = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
            Layer layer = behaviour; doc.layers.Add(layer); Call(doc, "NormalizeModel");
            var window = scope.Own(ScriptableObject.CreateInstance<ShaderGraphWindow>());
            Set(window, "document", doc); Set(window, "layerId", layer.Id); Set(window, "svmPreviews", svm);
            Set(Get(window, "previewState"), "collapsed", true);
            job.OwnCleanup(() => { scope.CloseWindow(window); Call(layer, "ReleaseTransientResources"); });
            window.titleContent = new GUIContent("WhimTex node Height check");
            window.ShowUtility(); window.position = new Rect(70, 70, 1500, 1000);
            await Until(() => Get(window, "canvas") is VisualElement e && e.panel != null && e.worldBound.width > 300, token, "Graph UI panel");
            var canvas = Get(window, "canvas");
            // Fit actual nodes, then let native layout/visibility and compilation scheduling run.
            Call(canvas, "FrameAll");
            await Task.Delay(700, token);
            Call(canvas, "FrameAll"); await Task.Delay(300, token);
            var previews = ((IDictionary)Get(canvas, "previews")).Values.Cast<object>().ToArray();
            t.Equal(count, previews.Length, "All test nodes support previews");
            try
            {
                await Until(() =>
                {
                    if (previews.Any(p => (bool)Get(Get(p, "source"), "renderError")))
                        throw new InvalidOperationException(string.Join("\n", ((IDictionary)Get(canvas, "messageSources")).Values.Cast<object>()
                            .Cast<IEnumerable>().SelectMany(m => m.Cast<object>()).Select(m => (string)Get(m, "text")).Distinct()));
                    return previews.All(p => (bool)Call(p, "CanDisplay") && (bool)Get(Get(p, "source"), "HasImage") && !(bool)Get(Get(p, "source"), "dirty"));
                },
                    token, "All visible node previews rendered (" + (svm ? "SVM" : "native") + ")", 20);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException(string.Join("\n", previews.Select(p => Get(p, "nodeId") + " visible=" + Call(p, "CanDisplay") +
                    " available=" + Get(Get(p, "source"), "HasImage") + " dirty=" + Get(Get(p, "source"), "dirty") + " error=" + Get(Get(p, "source"), "renderError") +
                    " pending=" + (Get(Get(p, "source"), "pending") != null) + " rect=" + ((VisualElement)p).worldBound)));
            }
            // Freeze automatic window updates only for reproducible measurements of this owned canvas.
            Set(window, "ready", false);
            var state = Get(canvas, "previewState");
            var sources = previews.Select(p => (RenderTexture)Get(Get(p, "source"), "texture")).ToArray();
            var programs = previews.Select(p => Get(Get(p, "source"), svm ? "svm" : "program")).ToArray();
            t.True(programs.All(p => p != null), "Requested preview backend is active; svm toggle=" + Get(window, "svmPreviews") + "; " +
                string.Join("; ", previews.Where((p, i) => programs[i] == null).Select(p => graph.FindNode((string)Get(p, "nodeId")).kind +
                    " attempted=" + Get(Get(p, "source"), "svmAttempted") + " native=" + (Get(Get(p, "source"), "program") != null))));
            string graphBefore = JsonUtility.ToJson(graph); int undo = Undo.GetCurrentGroup(); bool dirty = EditorUtility.IsDirty(doc);
            foreach (var p in previews) { ((VisualElement)p).Q<ToolbarToggle>("node-preview-height").value = true; }
            Call(canvas, "TickPreviews");
            t.True(previews.Count(p => !(bool)Get(p, "presentationDirty")) <= 8, "Height remains in the bounded node update queue");
            foreach (var p in previews) Call(p, "Tick", state);
            long bytes = 0;
            for (int i = 0; i < previews.Length; i++)
            {
                var p = previews[i]; var renderer = Get(p, "heightRenderer");
                var relief = (RenderTexture)Get(renderer, "Output"); var geometry = (RenderTexture)Get(renderer, "Geometry");
                t.True(ReferenceEquals(sources[i], Get(Get(p, "source"), "texture")), "Height reuses the completed node image");
                t.True(ReferenceEquals(programs[i], Get(Get(p, "source"), svm ? "svm" : "program")), "Mode change does not recompile");
                t.Equal(sources[i].width, relief.width, "Height output is preview-sized, not 8K");
                t.True(relief.width <= 128 && geometry.width <= 128, "Both additional buffers are bounded");
                bytes += (long)relief.width * relief.height * 32;
            }
            var first = previews[0]; var second = previews[1]; var settings = Get(first, "viewState");
            var firstRenderer = Get(first, "heightRenderer");
            var pixels = Read((RenderTexture)Get(firstRenderer, "Output"));
            var coords = Read((RenderTexture)Get(firstRenderer, "Geometry"));
            var raw = Read(sources[0]); var expected = raw[raw.Length / 2]; int surface = 0;
            for (int i = 0; i < pixels.Length; i++) if (coords[i].a > .5f)
            {
                surface++;
                t.Near(expected.g * coords[i].b, pixels[i].g, .0003, "HDR/raw color preserved without a second gamma conversion");
                t.Near(expected.b * coords[i].b, pixels[i].b, .0003, "Negative color preserved");
                t.Near(expected.a, pixels[i].a, .0003, "Alpha preserved");
            }
            t.True(surface > pixels.Length / 8 && pixels.Any(c => c.g > 1 && c.b < 0), "Relief contains HDR textured surface");
            // Real settings popup, native fields, and per-node isolation.
            var popupDone = new TaskCompletionSource<bool>();
            var button = ((VisualElement)first).Q<ToolbarButton>("node-preview-height-settings");
            ((VisualElement)Get(first, "viewControls")).AddToClassList("whimtex-node-preview-controls--open");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
            var popup = (PopupWindowContent)Get(first, "heightSettings");
            t.True(popup?.editorWindow != null, "Settings popup opened");
            var popupRoot = popup.editorWindow.rootVisualElement;
            popupRoot.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (popupDone.Task.IsCompleted) return;
                try
                {
                    var scale = popupRoot.Q<FloatField>("height-scale");
                    if (!(scale?.Q(className: BaseField<float>.inputUssClassName)?.worldBound.width > 20)) return;
                    scale.value = .8f; popupRoot.Q<Toggle>("height-texture").value = false;
                    var channel = popupRoot.Q<EnumField>("height-channel"); channel.value = (Enum)Enum.ToObject(channel.value.GetType(), 4);
                    t.Near(.8, (float)Get(settings, "heightScale"), .00001, "Scale updated by native control");
                    t.Near(.25, (float)Get(Get(second, "viewState"), "heightScale"), .00001, "Other node settings unaffected");
                    t.True((bool)Get(Get(second, "viewState"), "heightTexture"), "Other node stays textured");
                    popupDone.TrySetResult(true);
                }
                catch (Exception error) { popupDone.TrySetException(error); }
            });
            await Until(() => popupDone.Task.IsCompleted, token, "Settings popup layout", 10); await popupDone.Task;
            popup.editorWindow.Close();
            // Restore source test setup; changing display settings never touches source image/shader.
            Set(settings, "heightScale", .25f); Set(settings, "heightTexture", true);
            Set(settings, "heightChannel", Enum.ToObject(Get(settings, "heightChannel").GetType(), 0));
            Call(first, "InvalidatePresentation", true); Call(first, "Tick", state);
            var measure = new Measurements { svm = svm, nodes = count, previewSize = sources[0].width, extraBytes = bytes };
            var one = scope.Own(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat)); one.Create();
            var cpu = scope.Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            var costs = new List<double>[4]; for (int i = 0; i < costs.Length; i++) costs[i] = new List<double>();
            // Alternate complete source+presentation updates. Each completion fence reads only 1 pixel.
            for (int round = 0; round < 24; round++)
            {
                for (int variant = 0; variant < 2; variant++)
                {
                    bool height = ((round + variant) & 1) != 0;
                    foreach (var p in previews) { Call(p, "SetPreviewMode", height); Call(p, "Tick", state); }
                    Fence((Texture)Get((Image)Get(previews.Last(), "image"), "image"), one, cpu);
                    foreach (var p in previews) Call(p, "Invalidate", true);
                    var watch = Stopwatch.StartNew();
                    foreach (var p in previews) Call(p, "Tick", state);
                    double submitMs = watch.Elapsed.TotalMilliseconds;
                    Fence(((Image)Get(previews.Last(), "image")).image, one, cpu);
                    if (round >= 4) { costs[height ? 1 : 0].Add(submitMs); costs[height ? 3 : 2].Add(watch.Elapsed.TotalMilliseconds); }
                }
                await Task.Delay(1, token);
            }
            measure.imageCpuMs = Median(costs[0]); measure.heightCpuMs = Median(costs[1]);
            measure.imageCompletedMs = Median(costs[2]); measure.heightCompletedMs = Median(costs[3]);
            measure.imageP95Ms = costs[2][(int)(costs[2].Count * .95)]; measure.heightP95Ms = costs[3][(int)(costs[3].Count * .95)];
            foreach (var p in previews) { Call(p, "SetPreviewMode", true); Call(p, "Tick", state); }
            var idle = Stopwatch.StartNew();
            for (int repeat = 0; repeat < 30; repeat++) foreach (var p in previews) t.True(!(bool)Call(p, "Tick", state), "Unchanged Height is cached");
            measure.cachedCpuMs = idle.Elapsed.TotalMilliseconds / 30;
            t.Equal(graphBefore, JsonUtility.ToJson(graph), "Height controls do not modify the graph");
            t.Equal(undo, Undo.GetCurrentGroup(), "Height controls do not add Undo");
            t.Equal(dirty, EditorUtility.IsDirty(doc), "Height controls do not dirty the document");
            foreach (var p in previews)
            {
                var controls = (VisualElement)Get(p, "viewControls");
                t.True(controls.worldBound.xMin >= ((VisualElement)Get(p, "surface")).worldBound.xMin - .5f &&
                    controls.Children().Where(c => c.resolvedStyle.display != DisplayStyle.None).All(c => c.worldBound.xMax <= controls.worldBound.xMax + .5f), "Overlay including output selector fits the preview");
            }
            var selector = ((VisualElement)previews.Last()).Q<DropdownField>();
            t.True(selector.choices.Count > 1, "Multiple-output node included");
            var oldRelief = Read((RenderTexture)Get(Get(previews.Last(), "heightRenderer"), "Output"));
            selector.value = "CellValue"; Set(window, "ready", true);
            await Until(() => !(bool)Get(Get(previews.Last(), "source"), "dirty") && Get(Get(previews.Last(), "source"), "pending") == null &&
                (string)Get(Get(previews.Last(), "source"), svm ? "svmPort" : "compiledPort") == selector.value, token, "Changed output rendered");
            Set(window, "ready", false);
            var newRelief = Read((RenderTexture)Get(Get(previews.Last(), "heightRenderer"), "Output"));
            t.True(!oldRelief.SequenceEqual(newRelief), "Choosing another port updates relief");
            Call(first, "SetExpanded", false);
            t.True(Get(firstRenderer, "Geometry") == null && Get(firstRenderer, "Output") == null, "Collapsed preview releases relief buffers");
            t.True(!(bool)Call(first, "Tick", state), "Collapsed preview does no work");
            Call(first, "SetExpanded", true); Call(first, "Tick", state);
            var graphView = (GraphView)canvas;
            var pos = graphView.contentViewContainer.resolvedStyle.translate;
            var zoom = graphView.contentViewContainer.resolvedStyle.scale.value;
            graphView.UpdateViewTransform(new Vector3(-100000, -100000), zoom);
            await Task.Delay(100, token);
            foreach (var p in previews) { Call(p, "UpdatePriority"); t.True(Get(Get(p, "heightRenderer"), "Output") == null, "Offscreen relief buffers released"); }
            graphView.UpdateViewTransform(pos, zoom); await Task.Delay(100, token);
            foreach (var p in previews) Call(p, "Tick", state);
            Call(canvas, "Rebuild");
            var rebuilt = ((IDictionary)Get(canvas, "previews"))[nodes[0].id];
            t.True(ReferenceEquals(settings, Get(rebuilt, "viewState")) && (bool)Get(settings, "heightMode"), "Per-node settings survive view rebuild");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Temp/WhimTex/node-height-preview"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, id + ".json"), JsonUtility.ToJson(measure, true));
        });
        return job.Read();
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);
}
