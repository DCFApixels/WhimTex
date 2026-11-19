using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class MorphGroupWindowTests
{
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    public static string Start(string id) => AsyncD.Start(id, Run);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static async Task Run(TestContext t, CancellationToken token)
    {
        const string timelineKey = "DCFApixels.WhimTex.Experimental.ShowTimeline";
        bool hadTimeline = EditorPrefs.HasKey(timelineKey), oldTimeline = EditorPrefs.GetBool(timelineKey);
        WhimTexWindow main = null;
        EditorWindow editor = null;
        WhimTexDocument doc = null;
        try
        {
            EditorPrefs.SetBool(timelineKey, true);
            main = ScriptableObject.CreateInstance<WhimTexWindow>();
            doc = (WhimTexDocument)typeof(WhimTexWindow).GetField("activeDocument", Instance).GetValue(main);
            doc.width = 64;
            doc.height = 32;
            var group = new Layer(new MorphGroupLayerBehaviour());
            var first = Shape(-16, Color.red);
            var second = Shape(16, Color.blue);
            group.children.AddRange(new[] { first, new Layer(new ColorFillLayerBehaviour()) { enabled = false },
                second, new Layer(new ColorFillLayerBehaviour()) });
            doc.layers.Add(group);
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc, null);
            string fromId = first.Id, toId = second.Id;
            main.ShowUtility();
            main.position = new Rect(30, 30, 940, 620);
            main.CreateGUI();
            typeof(WhimTexWindow).GetMethod("SelectOnlyLayer", Instance).Invoke(main, new object[] { group.Id });
            typeof(WhimTexWindow).GetMethod("RefreshToolkitInterface", Instance).Invoke(main, new object[] { true });
            Task Tick() => AsyncD.Tick(main.rootVisualElement, token);
            await Tick();
            var mode = main.rootVisualElement.Q<EnumField>("morphMode");
            t.True(mode != null, "Group properties dispatch to the Morph inspector");
            Submit(main.rootVisualElement.Q<Button>("editMorph"));
            var type = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.MorphGroupEditorWindow", true);
            editor = Resources.FindObjectsOfTypeAll(type).Cast<EditorWindow>().Single(w =>
                (WhimTexDocument)type.GetField("document", Instance).GetValue(w) == doc);
            editor.position = new Rect(80, 80, 320, 420);
            var root = editor.rootVisualElement;
            for (int i = 0; i < 30 && root.Q("morphPreviewA") == null; i++) await Tick();
            var a = root.Q("morphPreviewA");
            var b = root.Q("morphPreviewB");
            var choices = root.Q<DropdownField>("morphTransition");
            var scrub = root.Q<Slider>("morphScrub");
            var rasterMethod = root.Q<EnumField>("rasterMethod");
            var result = root.Q("morphPreviewResult").Q<Image>();
            for (int i = 0; i < 30 && (a.Q<Image>().image == null || b.Q<Image>().worldBound.width < 1); i++) await Tick();
            var fa = a.Q<Image>().worldBound;
            var fb = b.Q<Image>().worldBound;
            t.True(a.Q<Image>().image != null && b.Q<Image>().image != null && fa.width > 32 && fb.xMin >= fa.xMax,
                "Two owned endpoint previews fit side by side at minimum width");
            t.True(result.image != null && result.worldBound.width > 32 && result.worldBound.xMin >= fb.xMax &&
                result.worldBound.xMax <= root.worldBound.xMax, "Result fits beside the sources at minimum width");
            t.Near(2, fa.width / fa.height, .04, "Non-square endpoint proportions");
            t.Equal(2, choices.choices.Count, "Disabled child skipped, repeated display names supported");
            MorphGroupLayerBehaviour Model() => (MorphGroupLayerBehaviour)doc.layers[0].Behaviour;
            MorphTransition Pair() => Model().Find(fromId, toId);
            var start = new Vector2(.25f, .55f);
            var end = new Vector2(.75f, .55f);
            Point(a, EventType.MouseDown, start);
            Point(a, EventType.MouseUp, start);
            t.True(Pair() == null, "First point is only a draft");
            Point(b, EventType.MouseDown, end);
            Point(b, EventType.MouseUp, end);
            t.True(Vector2.Distance(start, Pair().anchors.Single().from) < .001f &&
                Vector2.Distance(end, Pair().anchors[0].to) < .001f, $"Commit both bottom-left UVs by layer ID: {Pair().anchors[0].from:F4} to {Pair().anchors[0].to:F4}");
            t.Near(1, Pair().duration, 0, "Default transition duration");
            root.Q<FloatField>("morphDuration").value = 2;
            Submit(root.Q<Button>("morphMidpoint"));
            t.Near(1, doc.PreviewTime, .00001, "Preview midpoint uses the canvas clock");
            t.Near(1d / 3, scrub.value, .0001, "Authoring scrub reflects the sequence duration");
            for (int i = 0; i < 8; i++) await Tick();
            Color32[] before = CanvasPixels(main);
            t.True(before.Any(pixel => pixel.a > 128), $"Visible midpoint on the main canvas (highest alpha {before.Max(pixel => pixel.a)})");
            var moved = new Vector2(.65f, .6f);
            Point(a, EventType.MouseDown, start);
            Point(a, EventType.MouseDrag, new Vector2(.45f, .4f));
            Point(a, EventType.MouseDrag, moved);
            for (int i = 0; i < 20 && before.SequenceEqual(CanvasPixels(main)); i++) await Tick();
            t.True(a.HasPointerCapture(PointerId.mousePointerId) && !before.SequenceEqual(CanvasPixels(main)),
                "Main canvas recalculates before pointer release");
            Point(a, EventType.MouseUp, moved);
            t.True(Vector2.Distance(moved, Pair().anchors[0].from) < .001f &&
                Vector2.Distance(end, Pair().anchors[0].to) < .001f, "Only A moved");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            for (int i = 0; i < 20 && !before.SequenceEqual(CanvasPixels(main)); i++) await Tick();
            t.True(Vector2.Distance(start, Pair().anchors.Single().from) < .001f && before.SequenceEqual(CanvasPixels(main)),
                "One Undo restores the whole drag and midpoint pixels");
            Undo.PerformRedo();
            await Tick();
            t.True(Vector2.Distance(moved, Pair().anchors[0].from) < .001f, "Redo restores the last dragged position");
            var oldResult = TexturePixels((RenderTexture)result.image);
            rasterMethod.value = MorphRasterMethod.Liao;
            for (int i = 0; i < 30 && oldResult.SequenceEqual(TexturePixels((RenderTexture)result.image)); i++) await Tick();
            t.Equal(MorphRasterMethod.Liao, Model().rasterMethod, "Raster method applies to the group");
            t.Equal(MorphRasterMethod.Liao, (MorphRasterMethod)main.rootVisualElement.Q<EnumField>("rasterMethod").value,
                "Layer Settings reflects the choice in Edit Morph");
            t.True(!oldResult.SequenceEqual(TexturePixels((RenderTexture)result.image)), "Switching solver renders different midpoint pixels");
            t.Near(1, doc.PreviewTime, .00001, "Switching solver preserves frame");
            t.True(choices.index == 0 && Vector2.Distance(moved, Pair().anchors.Single().from) < .001f, "Switching solver preserves pair and guide");
            main.rootVisualElement.Q<EnumField>("rasterMethod").value = MorphRasterMethod.LocalContrast;
            for (int i = 0; i < 30 && !oldResult.SequenceEqual(TexturePixels((RenderTexture)result.image)); i++) await Tick();
            t.Equal(MorphRasterMethod.LocalContrast, (MorphRasterMethod)rasterMethod.value, "Edit Morph follows external method changes");
            t.True(oldResult.SequenceEqual(TexturePixels((RenderTexture)result.image)), "Switching back restores the previous image");
            Point(b, EventType.MouseDown, end, 1);
            Point(b, EventType.MouseUp, end, 1);
            t.Equal(0, Pair().anchors.Count, "Removing B removes the whole pair");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.Equal(1, Pair().anchors.Count, "Undo restores the removed pair");
            main.rootVisualElement.Q<EnumField>("morphMode").value = MorphMode.Vector;
            await Tick();
            t.True(rasterMethod.resolvedStyle.display == DisplayStyle.None && result.image == null, "Vector mode releases the raster preview");
            scrub.value = 1;
            t.Near(3, doc.PreviewTime, .0001, "Scrubbing reaches final hold");
            t.Equal(1, choices.index, "Final hold exposes the last ordered pair");
            t.True(root.Q<HelpBox>("morphRasterFallback").style.display.value == DisplayStyle.Flex,
                "Unsupported second pair shows Raster fallback in Vector mode");
            t.Equal(1, Model().transitions.Count, "Navigating to an unauthored pair preserves sparse data");
            doc.SetPreviewTime(.5);
            await Tick();
            t.Equal(0, choices.index, "External timeline seek selects the first pair");
            var banner = root.Q<HelpBox>("morphRasterFallback");
            var left = (ShapeLayerBehaviour)doc.layers[0].children[0].Behaviour;
            var right = (ShapeLayerBehaviour)doc.layers[0].children[2].Behaviour;
            Pair().anchors.Clear();
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc, null);
            for (int i = 0; i < 30 && banner.style.display.value != DisplayStyle.None; i++) await Tick();
            t.True(banner.style.display.value == DisplayStyle.None, "Compatible solid Shape pair has no fallback notice");
            left.fillMode = VectorFillMode.LinearGradient;
            right.fillMode = VectorFillMode.RadialGradient;
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc, null);
            await Tick();
            Submit(root.Q<Button>("morphMidpoint"));
            for (int i = 0; i < 30 && banner.style.display.value != DisplayStyle.Flex; i++) await Tick();
            t.True(!doc.IsPlaying && banner.style.display.value == DisplayStyle.Flex,
                "Paused midpoint reports a rendered paint mismatch between eligible Shape keys");
            left.fillMode = right.fillMode = VectorFillMode.Solid;
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc, null);
            for (int i = 0; i < 30 && banner.style.display.value != DisplayStyle.None; i++) await Tick();
            t.True(banner.style.display.value == DisplayStyle.None, "Fallback clears after a compatible midpoint render");

            EditorPrefs.SetBool(timelineKey, false);
            type.GetMethod("RefreshDocument", Instance).Invoke(editor, null);
            double pausedTime = doc.PreviewTime;
            choices.index = 1;
            t.Equal(1, choices.index, "Pair editing works with timeline hidden");
            t.Near(pausedTime, doc.PreviewTime, 0, "Choosing another pair leaves the hidden clock at rest");
            main.rootVisualElement.Q<EnumField>("morphMode").value = MorphMode.Raster;
            for (int i = 0; i < 30 && result.image == null; i++) await Tick();
            t.True(result.image != null && choices.index == 1 && rasterMethod.enabledInHierarchy, "Hidden timeline supports comparing the selected raster pair");
            t.Near(pausedTime, doc.PreviewTime, 0, "Local comparison preserves the hidden timeline position");
            var frame = TexturePixels((RenderTexture)result.image);
            t.True(!frame.SequenceEqual(TexturePixels((RenderTexture)a.Q<Image>().image)) &&
                !frame.SequenceEqual(TexturePixels((RenderTexture)b.Q<Image>().image)), "Local midpoint differs from both endpoint images");
            t.True(root.Q<Button>("morphMidpoint").style.display.value == DisplayStyle.None &&
                scrub.style.display.value == DisplayStyle.None, "Timeline actions follow the experimental setting");
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (editor != null) editor.Close(); },
                () => { if (doc != null) Undo.ClearUndo(doc); },
                () => { if (main != null) { Undo.ClearUndo(main); main.DiscardChanges(); main.Close(); } },
                () => { if (hadTimeline) EditorPrefs.SetBool(timelineKey, oldTimeline); else EditorPrefs.DeleteKey(timelineKey); });
        }
    }

    static Layer Shape(float x, Color color)
    {
        var layer = new Layer(new ShapeLayerBehaviour { fillColor = color }) { layerName = "Key" };
        layer.transform.positionF = new Vector2(x, 0);
        layer.transform.scaleF = new Vector2(.25f, .375f);
        layer.transform.tiling = TransformTilingMode.Unbounded;
        return layer;
    }

    static void Submit(Button button)
    {
        using var evt = NavigationSubmitEvent.GetPooled();
        evt.target = button;
        button.SendEvent(evt);
    }

    static void Point(VisualElement pane, EventType type, Vector2 uv, int button = 0)
    {
        var image = pane.Q<Image>().worldBound;
        var point = new Vector2(image.xMin + uv.x * image.width, image.yMax - uv.y * image.height);
        var input = new Event { type = type, button = button, mousePosition = point };
        using EventBase evt = type switch
        {
            EventType.MouseDown => PointerDownEvent.GetPooled(input),
            EventType.MouseUp => PointerUpEvent.GetPooled(input),
            _ => PointerMoveEvent.GetPooled(input)
        };
        evt.target = pane;
        pane.SendEvent(evt);
    }

    static Color32[] CanvasPixels(WhimTexWindow main)
    {
        var rt = (RenderTexture)typeof(WhimTexWindow).GetField("canvasTexture", Instance).GetValue(main);
        return TexturePixels(rt);
    }

    static Color32[] TexturePixels(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var copy = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            return copy.GetPixels32();
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }
}
