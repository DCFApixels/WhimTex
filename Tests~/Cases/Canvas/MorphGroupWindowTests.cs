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
            t.True(root.Q("rasterMethod") == null && main.rootVisualElement.Q("rasterMethod") == null,
                "Raster has no solver selector in either window");
            var stationary = root.Q<Toggle>("preserveStationaryDetails");
            Toggle InspectorStationary() => main.rootVisualElement.Q<Toggle>("preserveStationaryDetails");
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
            t.True(stationary.style.display.value == DisplayStyle.Flex && InspectorStationary().style.display.value == DisplayStyle.Flex &&
                !stationary.value && !InspectorStationary().value, "New raster groups expose Liao settings in both windows");
            var tool = root.Q<EnumField>("morphGuideTool");
            var radius = root.Q<FloatField>("morphGuideRadius");
            var boundary = root.Q<Toggle>("morphGuideBoundary");
            void Click(VisualElement pane, Vector2 uv)
            {
                Point(pane, EventType.MouseDown, uv, pick: true);
                Point(pane, EventType.MouseUp, uv, pick: true);
            }
            var otherPoint = new Vector2(.15f, .3f);
            await Tick();
            Click(a, otherPoint);
            Click(b, otherPoint);
            radius.value = .5f;
            Click(b, end);
            t.True(radius.enabledInHierarchy && radius.value == 0 && boundary.style.display.value == DisplayStyle.None,
                "Picking the first numbered point on B exposes its own Auto radius");
            radius.value = .25f;
            radius.value = 2;
            t.True(radius.value == .25f && Pair().anchors[0].radius == .25f && Pair().anchors[1].radius == .5f,
                "Radius edits only the selected point and rejects values above one");
            await Tick();
            foreach (var pane in new[] { a, b })
            {
                var rect = pane.Q<Image>().worldBound;
                var circle = pane.Q("morphPointRadiusPreview").worldBound;
                t.Near(.5f * Mathf.Min(rect.width, rect.height), circle.width, 1, "Point diameter uses the shorter canvas axis");
                t.Near(circle.width, circle.height, 1, "Point radius stays circular on a rectangular image");
                Vector2 uv = pane == a ? moved : end;
                var center = new Vector2(rect.xMin + uv.x * rect.width, rect.yMax - uv.y * rect.height);
                t.True(Vector2.Distance(center, circle.center) <= 1, "Radius follows this endpoint's selected position");
            }
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(Pair().anchors[0].radius == 0 && Pair().anchors[1].radius == .5f, "Undo restores just the first radius");
            Undo.PerformRedo();
            await Tick();
            Click(a, moved);
            t.Equal(.25f, radius.value, "Picking A after Redo reads the restored radius");
            radius.value = 0;
            Click(a, otherPoint);
            Submit(root.Q<Button>("removeMorphPair"));
            t.True(Pair().anchors.Count == 1 && Pair().anchors[0].radius == 0, "Return the remaining point to Auto");
            tool.value = (Enum)Enum.Parse(tool.value.GetType(), "Segments");
            for (int i = 0; i < 4; i++) await Tick();
            var lower = new Vector2(.2f, .2f);
            var upper = new Vector2(.8f, .8f);
            Point(a, EventType.MouseDown, lower);
            Point(a, EventType.MouseDrag, upper);
            Point(a, EventType.MouseUp, upper);
            t.Equal(0, Pair().segments.Count, "A single segment remains a draft");
            Point(b, EventType.MouseDown, upper);
            Point(b, EventType.MouseUp, end);
            t.True(Pair().segments.Count == 1 && Pair().anchors.Count == 1 &&
                Vector2.Distance(Pair().nodes[Pair().segments[0].startNode - 1].to, upper) < .001f, "Matching directed segments retain existing points");
            await Tick();
            t.True(Vector2.Distance(Pair().nodes[Pair().segments[0].startNode - 1].from,lower)<.001f,
                $"A starts at pointer: {Pair().nodes[Pair().segments[0].startNode - 1].from:F4}, expected {lower:F4}");
            Click(a, (lower + upper) * .5f);
            radius.value = .375f;
            boundary.value = true;
            t.True(Pair().segments[0].radius == .375f && Pair().segments[0].boundary &&
                Pair().anchors[0].radius == 0, "Picking the straight guide body edits its radius and boundary together");
            t.True(radius.worldBound.xMax <= boundary.worldBound.xMin + 1 && boundary.worldBound.xMax <= root.worldBound.xMax,
                "The radius and Boundary controls fit side by side at minimum width");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(!Pair().segments[0].boundary && Pair().segments[0].radius == .375f, "Boundary has its own Undo step");
            Point(a, EventType.MouseDown, lower);
            bool capturedHandle = a.HasPointerCapture(PointerId.mousePointerId);
            Point(a, EventType.MouseUp, start);
            t.True(Vector2.Distance(Pair().nodes[Pair().segments[0].startNode - 1].from, start) < .001f, $"Move one segment handle: {Pair().nodes[Pair().segments[0].startNode - 1].from:F4} expected {start:F4}, capture {capturedHandle}");
            Submit(root.Q<Button>("removeMorphPair"));
            t.True(Pair().segments.Count == 0 && Pair().anchors.Count == 1, "Delete affects only the selected segment");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(Pair().segments.Count == 1 && Vector2.Distance(Pair().nodes[Pair().segments[0].endNode - 1].from, upper) < .001f, "Undo restores segment");
            editor.position = new Rect(80,80,960,640);
            for (int i = 0; i < 4; i++) await Tick();
            tool.value = (Enum)Enum.Parse(tool.value.GetType(), "Bezier");
            for (int i = 0; i < 4; i++) await Tick();
            var corner = new Vector2(.18f,.90f);
            Point(a,EventType.MouseDown,corner); Point(a,EventType.MouseUp,upper);
            Point(b,EventType.MouseDown,corner); Point(b,EventType.MouseUp,end);
            var curve = Pair().segments[1];
            t.True(curve.bezier && curve.endNode == Pair().segments[0].endNode && Pair().nodes.Count == 3,
                "Straight edge and cubic share one paired knot");
            await Tick();
            var hinge = new Vector2(.87f,.65f);
            Point(a,EventType.MouseDown,upper); Point(a,EventType.MouseUp,hinge);
            t.True(Vector2.Distance(Pair().nodes[curve.endNode-1].from,hinge) < .001f &&
                Pair().segments[0].endNode == curve.endNode, "Dragging one end moves the common hinge");
            var control = corner + curve.fromOut;
            var bent = control + new Vector2(.01f,-.15f);
            Point(a,EventType.MouseDown,control); Point(a,EventType.MouseUp,bent);
            t.True(Vector2.Distance(corner + curve.fromOut,bent)<.001f,"Cubic handle edits its own side");
            Vector2 OnCurve(bool source, float v)
            {
                float u=1-v;
                var startNode=Pair().nodes[curve.startNode-1]; var endNode=Pair().nodes[curve.endNode-1];
                Vector2 p=source?startNode.from:startNode.to, q=source?endNode.from:endNode.to;
                return u*u*u*p+3*u*u*v*(p+(source?curve.fromOut:curve.toOut))+
                    3*u*v*v*(q+(source?curve.fromIn:curve.toIn))+v*v*v*q;
            }
            Click(b, OnCurve(false, .5f));
            radius.value = .1875f;
            boundary.value = true;
            t.True(curve.radius == .1875f && curve.boundary && !Pair().segments[0].boundary,
                "Picking a cubic on B edits only that guide");
            tool.value=(Enum)Enum.Parse(tool.value.GetType(),"Marks");
            // Switching tools relayouts the endpoint panes in the morph window.
            await AsyncD.Tick(root, token);
            await AsyncD.Tick(root, token);
            Click(a, OnCurve(true,.3f));
            Click(b, OnCurve(false,.6f));
            t.True(curve.marks.Count==1 && Vector2.Distance(curve.marks[0],new Vector2(.3f,.6f))<.01f,
                $"A correspondence mark pairs independent positions along the same curve: {string.Join(", ", curve.marks)}");
            Point(b,EventType.MouseDown,OnCurve(false,.6f));
            Point(b,EventType.MouseUp,OnCurve(false,.7f));
            t.Near(.7,curve.marks[0].y,.01,"Slide mark on B");
            Submit(root.Q<Button>("removeMorphPair"));
            t.Equal(0,curve.marks.Count,"Remove Mark retains the guide");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
            t.Equal(1,Pair().segments[1].marks.Count,"Undo restores the correspondence mark");
            tool.value = (Enum)Enum.Parse(tool.value.GetType(), "Points");
            var classic = root.Q<Toggle>("classicLiao");
            var mask = root.Q<Toggle>("visibilityMask");
            Toggle InspectorMask() => main.rootVisualElement.Q<Toggle>("visibilityMask");
            t.True(mask.value && InspectorMask().value, "Both windows initially enable the visibility mask");
            var blend = root.Q<EnumField>("maskBlend");
            EnumField InspectorBlend() => main.rootVisualElement.Q<EnumField>("maskBlend");
            blend.value = MorphMaskBlend.NormalizeDetail;
            await Tick();
            t.Equal(MorphMaskBlend.NormalizeDetail, (MorphMaskBlend)InspectorBlend().value, "Inspector mirrors tone and contrast mode");
            mask.value = false;
            await Tick();
            t.True(!Model().visibilityMask && !InspectorMask().value, "Edit Morph can disable the mask independently");
            t.True(!blend.enabledInHierarchy && !InspectorBlend().enabledInHierarchy, "Both tone controls require the visibility mask");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
            t.True(mask.value && InspectorMask().value && Model().visibilityMask, "Undo restores the mask in both windows");
            t.True(classic.resolvedStyle.display == DisplayStyle.Flex && !classic.value, "Current Liao is selected initially");
            classic.value = true;
            await Tick();
            t.True(Model().classicLiao && main.rootVisualElement.Q<Toggle>("classicLiao").value, "Both editors show classic mode");
            t.True(stationary.enabledInHierarchy && stationary.style.display.value == DisplayStyle.Flex &&
                InspectorStationary().style.display.value == DisplayStyle.Flex, "Classic Liao exposes the same stationary detail option");
            t.True(!mask.enabledInHierarchy && !InspectorMask().enabledInHierarchy && Model().visibilityMask,
                "Classic Liao disables the mask control and retains the setting");
            t.Near(1, doc.PreviewTime, .00001, "Switching Liao versions preserves time");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(!classic.value && !Model().classicLiao, "Undo restores current Liao");
            var geometry = root.Q<Toggle>("preserveGeometry");
            t.True(geometry.resolvedStyle.display == DisplayStyle.Flex && !geometry.value, "Preserve Geometry is optional");
            geometry.value = true;
            await Tick();
            t.True(Model().preserveGeometry && main.rootVisualElement.Q<Toggle>("preserveGeometry").value, "The group and both editors use the same geometry setting");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(!geometry.value && !Model().preserveGeometry, "Undo restores the previous geometry setting");
            stationary.value = true;
            await Tick();
            t.True(Model().preserveStationaryDetails && InspectorStationary().value, "Edit Morph enables the option in Layer Settings");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(!Model().preserveStationaryDetails && !stationary.value && !InspectorStationary().value,
                "Undo clears stationary detail preservation in both windows");
            Undo.PerformRedo();
            await Tick();
            t.True(Model().preserveStationaryDetails && stationary.value && InspectorStationary().value,
                "Redo restores stationary detail preservation in both windows");
            InspectorStationary().value = false;
            await Tick();
            t.True(!Model().preserveStationaryDetails && !stationary.value, "Edit Morph follows changes made in Layer Settings");
            InspectorStationary().value = true;
            await Tick();
            t.Near(1, doc.PreviewTime, .00001, "Toggling stationary detail preservation keeps the frame");
            var trajectory = root.Q<EnumField>("morphInterpolation");
            trajectory.value = MorphInterpolation.AutomaticCurve;
            await Tick();
            t.Equal(MorphInterpolation.AutomaticCurve, Model().interpolation, "Edit Morph changes trajectory");
            var inspectorTrajectory = main.rootVisualElement.Q<EnumField>("morphInterpolation");
            t.Equal(MorphInterpolation.AutomaticCurve, (MorphInterpolation)inspectorTrajectory.value, "Inspector shows the trajectory");
            inspectorTrajectory.value = MorphInterpolation.Linear;
            await Tick();
            t.Equal(MorphInterpolation.Linear, (MorphInterpolation)trajectory.value, "Editor follows inspector trajectory");
            Point(b, EventType.MouseDown, end, 1);
            Point(b, EventType.MouseUp, end, 1);
            t.Equal(0, Pair().anchors.Count, "Removing B removes the whole pair");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.Equal(1, Pair().anchors.Count, "Undo restores the removed pair");
            main.rootVisualElement.Q<EnumField>("morphMode").value = MorphMode.Vector;
            await Tick();
            t.True(classic.resolvedStyle.display == DisplayStyle.None && result.image == null, "Vector mode releases the raster preview");
            t.True(trajectory.resolvedStyle.display == DisplayStyle.None, "Vector mode hides raster trajectory");
            t.True(stationary.style.display.value == DisplayStyle.None && InspectorStationary().style.display.value == DisplayStyle.None &&
                Model().preserveStationaryDetails, "Vector hides the retained Liao setting in both windows");
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
            t.True(result.image != null && choices.index == 1 && classic.enabledInHierarchy, "Hidden timeline supports comparing the selected raster pair");
            t.Near(pausedTime, doc.PreviewTime, 0, "Local comparison preserves the hidden timeline position");
            var frame = TexturePixels((RenderTexture)result.image);
            t.True(!frame.SequenceEqual(TexturePixels((RenderTexture)a.Q<Image>().image)) &&
                !frame.SequenceEqual(TexturePixels((RenderTexture)b.Q<Image>().image)), "Local midpoint differs from both endpoint images");
            t.True(root.Q<Button>("morphMidpoint").style.display.value == DisplayStyle.None &&
                scrub.style.display.value == DisplayStyle.None, "Timeline actions follow the experimental setting");
            await CheckCorrections(t, root, () => AsyncD.Tick(root, token), () => Model().transitions.Single(pair => pair.fromId == toId));
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

    // Exercise the hidden timeline's local midpoint on the previously unauthored second pair.
    static async Task CheckCorrections(TestContext t, VisualElement root, Func<Task> tick, Func<MorphTransition> pair)
    {
        var a = root.Q("morphPreviewA");
        var b = root.Q("morphPreviewB");
        var result = root.Q("morphPreviewResult");
        void Click(VisualElement pane, Vector2 uv)
        {
            Point(pane, EventType.MouseDown, uv, pick: true);
            Point(pane, EventType.MouseUp, uv, pick: true);
        }
        var start = new Vector2(.25f, .5f);
        var end = new Vector2(.65f, .5f);
        Click(a, start);
        Click(b, end);
        var mode = root.Q<EnumField>("morphCorrectionMode");
        var width = root.Q<FloatField>("morphCorrectionWidth");
        mode.value = MorphCorrectionMode.LocalChoice;
        t.Equal(MorphGuideCorrection.Create(MorphCorrectionMode.LocalChoice), pair().anchors.Single().correction,
            "Enabling a correction initializes the selected point");
        DragNumber(width, 4);
        t.True(pair().anchors[0].correction.width > .1f && pair().anchors[0].correction.width < 1f,
            $"Short label drag commits on release: {width.text} / {pair().anchors[0].correction.width}");
        DragNumber(width, 300);
        t.Equal(1f, pair().anchors[0].correction.width, "Dragging Width past the upper limit commits one");
        DragNumber(width, -300, 4);
        t.True(width.value > 0 && width.value < 1 && width.value == pair().anchors[0].correction.width,
            "Reversing at the lower limit responds immediately");
        float released = width.value;
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();
        await tick();
        t.Equal(1f, pair().anchors[0].correction.width, "One Undo restores Width before the reversed drag");
        Undo.PerformRedo();
        await tick();
        Click(a, start);
        t.Equal(released, width.value, "Redo and reselect retain the released value");
        var radius = root.Q<FloatField>("morphGuideRadius");
        DragNumber(radius, 300);
        t.Equal(1f, pair().anchors[0].radius, "Dragging Radius past one commits the maximum");
        DragNumber(radius, -300);
        t.Equal(0f, pair().anchors[0].radius, "Dragging Radius below zero commits Auto");
        DragNumber(root.Q<FloatField>("morphCorrectionStrength"), -300);
        DragNumber(root.Q<FloatField>("morphCorrectionFeather"), 300);
        t.True(pair().anchors[0].correction.strength == 0 && pair().anchors[0].correction.feather == 1,
            "Strength and Feather commit their respective limits");
        width.value = .2f;
        root.Q<FloatField>("morphCorrectionFeather").value = .4f;
        root.Q<FloatField>("morphCorrectionStrength").value = .7f;
        mode.value = MorphCorrectionMode.MatchTone;
        var before = pair().anchors[0].correction;
        t.True(before.width == .2f && before.feather == .4f && before.strength == .7f && before.mode == MorphCorrectionMode.MatchTone,
            "Changing mode keeps the authored influence");
        var handle = result.Q("morphCorrectionWidthHandle");
        for (int i = 0; i < 4; i++) await tick();
        var rect = result.Q<Image>().worldBound;
        var grip = handle.worldBound.center;
        var uv = new Vector2((grip.x - rect.xMin) / rect.width, (rect.yMax - grip.y) / rect.height);
        float unit = Mathf.Min(rect.width, rect.height) / rect.width;
        t.Near(.45f + before.width * unit, uv.x, 1 / rect.width, "Width handle uses halfway UV and the short axis");
        t.Near(.5f, uv.y, 1 / rect.height, "Handle follows the selected point on Result");
        var target = uv + Vector2.right * (.15f * unit);
        void Drag()
        {
            Point(result, EventType.MouseDown, uv, pick: true);
            Point(result, EventType.MouseDrag, (uv + target) * .5f);
            Point(result, EventType.MouseDrag, target);
            t.Near(.35, width.value, .001, "Drag previews the new width in the field");
            t.Equal(before, pair().anchors[0].correction, "Pointer movement leaves the document untouched");
        }
        Drag();
        using (var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
        {
            escape.target = result;
            result.SendEvent(escape);
        }
        Point(result, EventType.MouseUp, target);
        t.Equal(before.width, width.value, "Escape discards the draft and release does not commit it");
        Drag();
        Point(result, EventType.MouseUp, target);
        t.Near(.35, pair().anchors[0].correction.width, .001, "Release commits the Result handle");
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();
        await tick();
        t.Equal(before, pair().anchors[0].correction, "A single Undo restores the complete correction");

        var tool = root.Q<EnumField>("morphGuideTool");
        tool.value = (Enum)Enum.Parse(tool.value.GetType(), "Segments");
        var left = new Vector2(.2f, .2f);
        var right = new Vector2(.8f, .2f);
        var top = new Vector2(.5f, .85f);
        MorphSegment Edge(Vector2 p, Vector2 q) => new MorphSegment { fromStart = p, toStart = p, fromEnd = q, toEnd = q };
        pair().segments.Add(Edge(left, right));
        await tick();
        Click(a, (left + right) * .5f);
        var foreground = root.Q<Toggle>("morphForegroundContour");
        t.True(!foreground.enabledInHierarchy, "An open edge cannot mark a foreground contour");
        foreground.value = true;
        t.True(!pair().segments[0].foreground && !foreground.value, "An open contour rejects a foreground write");
        pair().segments.Add(Edge(right, top));
        pair().segments.Add(Edge(top, left));
        Click(a, (right + top) * .5f);
        mode.value = MorphCorrectionMode.LocalChoice;
        t.True(pair().segments[1].correction.mode == MorphCorrectionMode.LocalChoice && pair().anchors[0].correction.Equals(before),
            "Edge corrections are independent of point corrections");
        foreground.value = true;
        t.True(pair().segments.All(e => e.foreground), "One toggle marks every member of the paired loop");
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();
        await tick();
        t.True(pair().segments.Count == 3 && pair().segments.All(e => !e.foreground), "One Undo unmarks the complete loop");
        var seconds = root.Q<FloatField>("morphDuration");
        DragNumber(seconds, -300);
        t.Near(.001f, pair().duration, 1e-7, "Duration stops at one millisecond when dragged through zero");
        seconds.value = .0002f;
        DragNumber(seconds, -300);
        t.Equal(.0002f, pair().duration, "Dragging an existing sub-millisecond duration keeps its smaller minimum");
    }

    static void DragNumber(FloatField field, params int[] deltas)
    {
        var label = field.labelElement;
        var position = label.worldBound.center;
        using var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = position });
        down.target = label;
        label.SendEvent(down);
        try
        {
            foreach (int dx in deltas)
            {
                Vector2 delta = Vector2.right * dx;
                position += delta;
                using var move = PointerMoveEvent.GetPooled(new Event {
                    type = EventType.MouseDrag, button = 0, mousePosition = position, delta = delta });
                move.target = label;
                label.SendEvent(move);
            }
        }
        finally
        {
            using var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = position });
            up.target = label;
            label.SendEvent(up);
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

    static void Point(VisualElement pane, EventType type, Vector2 uv, int button = 0, bool pick = false)
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
        var target = pick ? pane.panel.Pick(point) : pane;
        evt.target = target;
        target.SendEvent(evt);
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
