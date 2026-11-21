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
    public static string Start(string id) => AsyncD.Start(id, (t, token) => Run(t, token, false));
    public static string StartNavigationBenchmark(string id) => AsyncD.Start(id, (t, token) => Run(t, token, true));
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static async Task Run(TestContext t, CancellationToken token, bool benchmark)
    {
        const string timelineKey = "DCFApixels.WhimTex.Experimental.ShowTimeline";
        bool hadTimeline = EditorPrefs.HasKey(timelineKey), oldTimeline = EditorPrefs.GetBool(timelineKey);
        var type = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.MorphGroupEditorWindow", true);
        var singleton = type.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        var artistsWindow = (EditorWindow)singleton.GetValue(null);
        WhimTexWindow main = null;
        EditorWindow editor = null;
        WhimTexDocument doc = null;
        try
        {
            // The scenario owns its utility window even if the artist is editing another group.
            singleton.SetValue(null, null);
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
            async Task Tick()
            {
                await AsyncD.Tick(main.rootVisualElement, token);
                if (editor != null) await AsyncD.Tick(editor.rootVisualElement, token);
            }
            await Tick();
            var mode = main.rootVisualElement.Q<EnumField>("morphMode");
            t.True(mode != null, "Group properties dispatch to the Morph inspector");
            t.True(singleton.GetValue(null) == null, "Selecting a group while Edit Morph is closed leaves it closed");
            Submit(main.rootVisualElement.Q<Button>("editMorph"));
            editor = Resources.FindObjectsOfTypeAll(type).Cast<EditorWindow>().Single(w =>
                (WhimTexDocument)type.GetField("document", Instance).GetValue(w) == doc);
            editor.position = new Rect(80, 80, 320, 420);
            var root = editor.rootVisualElement;
            for (int i = 0; i < 30 && root.Q("morphPreviewA") == null; i++) await Tick();
            var a = root.Q("morphPreviewA");
            var b = root.Q("morphPreviewB");
            if (benchmark)
            {
                editor.position = new Rect(60, 60, 1180, 760);
                editor.Focus();
                for (int i = 0; i < 8; i++) await Tick();
                await MeasureNavigation(t, editor, a, token);
                return;
            }
            var choices = root.Q<DropdownField>("morphTransition");
            var scrub = root.Q<Slider>("morphScrub");
            t.True(root.Q("rasterMethod") == null && main.rootVisualElement.Q("rasterMethod") == null,
                "Raster has no solver selector in either window");
            var motion = root.Q<Foldout>("morphMotion");
            var experimental = root.Q<Foldout>("morphExperimental");
            var stationary = motion.Q<Toggle>("preserveStationaryDetails");
            bool Displayed(VisualElement element)
            {
                if (element?.panel == null || !(element.layout.height > 0)) return false;
                for (var current = element; current != null; current = current.hierarchy.parent)
                    if (current.resolvedStyle.display == DisplayStyle.None) return false;
                return true;
            }
            async Task WaitForDisplay(VisualElement element, bool visible)
            {
                for (int i = 0; i < 30; i++)
                {
                    await AsyncD.Tick(root, token);
                    if (root.layout.width > 0 && element?.panel != null && Displayed(element) == visible) return;
                }
                throw new TimeoutException($"Morph field {element?.name} did not reach visible={visible}.");
            }
            await WaitForDisplay(stationary, false);
            t.True(!motion.value && !experimental.value && !Displayed(stationary) && !Displayed(experimental.contentContainer),
                "Motion and Experimental start collapsed");
            foreach (string field in new[] { "preserveStationaryDetails", "preserveGeometry", "morphInterpolation",
                "classicLiao", "morphMatching", "visibilityMask", "maskBlend", "morphBlending" })
                t.True(main.rootVisualElement.Q(field) == null, "Layer Settings omits " + field);
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
            for (int i = 0; i < 35 && before.SequenceEqual(CanvasPixels(main)); i++) await Tick();
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
            t.True(!stationary.value && !Model().preserveStationaryDetails, "New raster groups leave stationary detail preservation off");
            t.True(root.Q("morphGuideTool") == null, "Points, curves and marks share the canvas");
            var radius = root.Q<FloatField>("morphGuideRadius");
            var boundary = root.Q<Toggle>("morphGuideBoundary");
            void Click(VisualElement pane, Vector2 uv)
            {
                Point(pane, EventType.MouseDown, uv);
                Point(pane, EventType.MouseUp, uv);
            }
            await CheckViews(t, root, Tick, moved, () => Pair().anchors.Count);
            var otherPoint = new Vector2(.15f, .3f);
            await Tick();
            Click(a, otherPoint);
            Click(b, otherPoint);
            radius.value = .5f;
            await AsyncD.Tick(root, token);
            await AsyncD.Tick(root, token);
            Click(b, end);
            t.True(radius.enabledInHierarchy && radius.value == 0 && boundary.style.display.value == DisplayStyle.None,
                $"Picking the first numbered point on B exposes its own Auto radius: enabled={radius.enabledInHierarchy}, value={radius.value}, boundary={boundary.style.display.value}, points={Pair().anchors.Count}, selected={type.GetField("selected",Instance).GetValue(editor)}, width={b.Q<Image>().worldBound.width}, hint={root.Q<Label>(className:"whimtex-morph-hint").text}");
            radius.value = .25f;
            radius.value = 2;
            t.True(radius.value == .25f && Pair().anchors[0].radius == .25f && Pair().anchors[1].radius == .5f,
                "Radius edits only the selected point and rejects values above one");
            await Tick();
            ZoomAt(a,moved,-2); await Tick(); await Tick();
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
            Point(a,EventType.MouseDown,moved,button:2,clicks:2); Point(a,EventType.MouseUp,moved,button:2,clicks:2);
            await Tick();
            Undo.PerformRedo();
            await Tick();
            Click(a, moved);
            t.Equal(.25f, radius.value, "Picking A after Redo reads the restored radius");
            radius.value = 0;
            await Tick();
            Click(a, otherPoint);
            Submit(root.Q<Button>("removeMorphPair"));
            t.True(Pair().anchors.Count == 1 && Pair().anchors[0].radius == 0, "Return the remaining point to Auto");
            for (int i = 0; i < 4; i++) await Tick();
            editor.position = new Rect(80, 80, 960, 640);
            for (int i = 0; i < 4; i++) await Tick();
            var lower = new Vector2(.12f, .14f);
            var corner = new Vector2(.45f, .36f);
            var upper = new Vector2(.86f, .81f);
            var leftEnd = new Vector2(.12f, .79f);
            var rightEnd = new Vector2(.83f, .79f);
            var tangent = new Vector2(.10f, .08f);
            Click(a, lower);
            Point(a, EventType.MouseDown, corner); Point(a, EventType.MouseUp, corner + tangent);
            Click(a, upper); Click(b, leftEnd); Click(b, rightEnd);
            var finish = root.Q<Button>("finishMorphGuide");
            t.True(finish.enabledInHierarchy && Pair().guides.Count == 0, "Both independent sides stay in the draft until finished");
            Submit(finish); await Tick();
            MorphGuidePath Path() => Pair().guides.Single();
            t.True(Path().from.Count == 3 && Path().to.Count == 2 && Path().from[1].smooth &&
                Pair().anchors.Count == 1 && Pair().segments.Count == 0, "One three-knot curve matches one straight span");
            Vector2 At(bool source, float fraction) => (Vector2)typeof(MorphGuidePath).GetMethod("AtLength", Instance)
                .Invoke(Path(), new object[] { source, fraction, new Vector2(doc.width, doc.height) });
            var oldControl = Path().from[1].position + Path().from[1].outTangent;
            var newControl = oldControl + new Vector2(.025f, .10f);
            Point(a,EventType.MouseDown,oldControl); Point(a,EventType.MouseUp,newControl);
            t.True(Vector2.Distance(Path().from[1].position + Path().from[1].outTangent,newControl) < .001f &&
                Vector2.Dot(Path().from[1].inTangent.normalized,Path().from[1].outTangent.normalized) < -.999f &&
                Path().to[0].outTangent == Vector2.zero, "Dragging a smooth handle rotates the opposite handle on that side only");
            await Tick();
            ContextAction(b, leftEnd, "Make Smooth"); await Tick();
            t.True(Path().to[0].smooth && Path().to[0].outTangent != Vector2.zero &&
                Path().to[0].inTangent == Vector2.zero, "An existing corner gains a handle on its own side");
            ContextAction(b, leftEnd, "Make Corner"); await Tick();
            t.True(!Path().to[0].smooth && Path().to[0].outTangent == Vector2.zero,
                "An endpoint can return to a straight corner");
            Click(b, At(false, .5f));
            radius.value = .375f;
            boundary.value = true;
            t.True(Path().radius == .375f && Path().boundary && Pair().anchors[0].radius == 0,
                "A click anywhere on B selects the whole guide for both controls");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
            t.True(!Path().boundary && Path().radius == .375f, "Boundary has its own Undo step");
            await Tick();
            ZoomAt(a, At(true, .25f), -2);
            await Tick();
            ContextAction(a, At(true, .25f), "Add Match Mark"); await Tick();
            Click(b, At(false, .62f));
            t.True(Path().marks.Count == 1 && Vector2.Distance(Path().marks[0], new Vector2(.25f,.62f)) < .01f,
                "Correspondence marks store a pair of whole-side lengths");
            await Tick();
            Point(a,EventType.MouseDown,At(true,.25f)); Point(a,EventType.MouseUp,At(true,.75f));
            t.Near(.75f, Path().marks[0].x, .01, "A mark can slide across an authored vertex");
            var mark = Path().marks[0];
            var samples = Enumerable.Range(0, 13).Select(i => At(true, i/12f)).ToArray();
            var split = At(true, .19f);
            await Tick();
            Point(a,EventType.MouseDown, split, clicks: 2); Point(a,EventType.MouseUp,split,clicks: 2);
            t.True(Path().from.Count == 4 && Path().to.Count == 2 && Path().marks[0] == mark &&
                samples.Select((uv,i) => Vector2.Distance(uv,At(true,i/12f))).Max() < .001f,
                "Double-click preserves the curve and its marks and inserts a knot on A only");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
            t.True(Path().from.Count == 3 && Path().to.Count == 2, "Undo restores the independent knot lists");
            Undo.PerformRedo(); await Tick();
            Point(a,EventType.MouseDown,At(true,.5f),button:2,clicks:2);
            Point(a,EventType.MouseUp,At(true,.5f),button:2,clicks:2);
            await Tick();
            ContextAction(b, rightEnd, "Continue Guide"); await Tick();
            Click(b, new Vector2(.94f,.43f)); Submit(finish); await Tick();
            t.True(Path().to.Count == 3 && Path().from.Count == 4 && Path().marks[0].x == mark.x &&
                Vector2.Distance(At(false,Path().marks[0].y), Vector2.Lerp(leftEnd,rightEnd,mark.y)) < .002f,
                "Extending only B preserves the existing material position of its mark");
            ContextAction(a, At(true,.5f), "Remove Guide"); await Tick();
            t.True(Pair().guides.Count == 0 && Pair().anchors.Count == 1, "Remove Guide removes the complete paired object");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
            t.True(Path().from.Count == 4 && Path().to.Count == 3 && Path().marks.Count == 1,
                "Undo restores the guide, properties, and marks");
            ContextAction(a, At(true,.5f), "Remove Guide"); await Tick();
            // A pre-path document can still move and subdivide its old paired segments.
            var oldEdge = new MorphSegment { fromStart = lower, fromEnd = upper, toStart = leftEnd, toEnd = rightEnd };
            Pair().segments.Add(oldEdge);
            typeof(MorphTransition).GetMethod("BindNodes", Instance).Invoke(Pair(), null);
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc,null);
            await Tick();
            ZoomAt(a,lower,-2); await Tick();
            Point(a,EventType.MouseDown,lower); Point(a,EventType.MouseUp,corner);
            t.True(Vector2.Distance(Pair().nodes[oldEdge.startNode-1].from,corner)<.001f, "Existing paired vertices remain editable");
            await Tick();
            var middle = Vector2.Lerp(corner, upper, .24f);
            Point(a,EventType.MouseDown,middle,clicks: 2); Point(a,EventType.MouseUp,middle,clicks: 2);
            t.Equal(2, Pair().segments.Count,"A legacy edge can still be split on both sides");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();

            Point(a,EventType.MouseDown,corner,button:2,clicks:2); Point(a,EventType.MouseUp,corner,button:2,clicks:2);
            await Tick();
            var classic = experimental.Q<Toggle>("classicLiao");
            var matching = experimental.Q<PopupField<MorphMatching>>("morphMatching");
            var analyze = experimental.Q<Button>("analyzeMorphPair");
            var blend = motion.Q<PopupField<MorphMaskBlend?>>("morphBlending");
            t.True(blend != null && !Displayed(blend) && !Displayed(matching) && !Displayed(analyze),
                "Collapsed foldouts hide Blending, Matching and Analyze Pair");
            motion.value = true;
            await WaitForDisplay(blend, true);
            t.True(Displayed(blend) && blend.text == "Visibility" && Model().visibilityMask && Model().maskBlend == MorphMaskBlend.Original,
                "Opening Motion exposes the default visibility blend");
            blend.value = MorphMaskBlend.NormalizeDetail;
            await Tick();
            blend.value = null;
            t.True(blend.text == "Crossfade" && !Model().visibilityMask && Model().maskBlend == MorphMaskBlend.NormalizeDetail,
                "Crossfade retains the inactive tone mode");
            await Tick();
            blend.value = MorphMaskBlend.SeparateTone;
            t.True(Model().visibilityMask && Model().maskBlend == MorphMaskBlend.SeparateTone,
                "Separate Tone enables the mask and selects its tone mode together");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(blend.text == "Crossfade" && !Model().visibilityMask && Model().maskBlend == MorphMaskBlend.NormalizeDetail,
                "One Undo restores Crossfade and the previous inactive tone mode");
            blend.value = MorphMaskBlend.Original;
            t.True(Model().visibilityMask && Model().maskBlend == MorphMaskBlend.Original,
                "Visibility restores the original whole-pixel blend");

            experimental.value = true;
            await WaitForDisplay(matching, true);
            matching.value = MorphMatching.ThreeHypotheses;
            experimental.value = false;
            await WaitForDisplay(matching, false);
            t.True(Displayed(experimental) && !Displayed(matching) && experimental.text.Contains("Three Hypotheses") &&
                Model().matching == MorphMatching.ThreeHypotheses,
                "Collapsed Experimental names the active nonstandard matcher");
            experimental.value = true;
            await WaitForDisplay(matching, true);
            matching.value = MorphMatching.MARCO;
            await WaitForDisplay(analyze, true);
            t.True(Displayed(analyze) && experimental.text.Contains("MARCO") && analyze.text == "Analyze Pair",
                "MARCO exposes Analyze Pair for the selected transition");
            classic.value = true;
            experimental.value = false;
            await WaitForDisplay(matching, false);
            t.True(Displayed(experimental) && experimental.text.Contains("Classic Liao") && !experimental.text.Contains("MARCO") &&
                !matching.enabledInHierarchy && !blend.enabledInHierarchy && !Displayed(analyze),
                "The collapsed header shows the active Classic override");
            t.True(Model().classicLiao && blend.text == "Visibility" && Model().visibilityMask && Model().matching == MorphMatching.MARCO,
                "Classic retains the selected blend and matcher");
            t.True(Displayed(stationary) && stationary.enabledInHierarchy, "Both Liao versions support stationary detail preservation");
            t.Near(1, doc.PreviewTime, .00001, "Switching matchers preserves the frame");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(!classic.value && matching.enabledInHierarchy && blend.enabledInHierarchy && experimental.text.Contains("MARCO"),
                "Undoing Classic exposes the saved MARCO mode even while collapsed");
            experimental.value = true;
            await WaitForDisplay(matching, true);
            matching.value = MorphMatching.Liao;
            experimental.value = false;
            t.Equal("Experimental", experimental.text, "Standard Liao clears the active-mode caption");

            // Inspect an unrecognized value already in memory without changing the artist's current view.
            Model().matching = (MorphMatching)99;
            Model().maskBlend = (MorphMaskBlend)98;
            type.GetMethod("RefreshDocument", Instance).Invoke(editor, null);
            t.True(!experimental.value && motion.value && experimental.text.Contains("Unknown (99)") && blend.text == "Unknown (98)" &&
                (int)Model().matching == 99 && (int)Model().maskBlend == 98 && choices.index == 0,
                "Refreshing unknown settings reports their values and preserves the foldouts, pair and model");
            experimental.value = true;
            t.True(matching.text == "Unknown (99)" && matching.index == -1 && blend.index == -1,
                "An unknown value is displayed without selecting a supported mode");
            Model().mode = (MorphMode)97;
            type.GetMethod("RefreshDocument", Instance).Invoke(editor, null);
            var warning = root.Q<HelpBox>("unknownMorphMode");
            t.True(warning.style.display.value == DisplayStyle.Flex && warning.text.Contains("97") &&
                motion.value && experimental.value && (int)Model().mode == 97,
                "An unknown group mode identifies the current value and preserves the artist's foldout choices");
            Model().mode = MorphMode.Raster;
            Model().matching = MorphMatching.Liao;
            Model().maskBlend = MorphMaskBlend.Original;
            type.GetMethod("RefreshDocument", Instance).Invoke(editor, null);
            experimental.value = false;

            var geometry = motion.Q<Toggle>("preserveGeometry");
            await Tick();
            geometry.value = true;
            t.True(Model().preserveGeometry, "Motion enables geometry preservation for the group");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(!geometry.value && !Model().preserveGeometry, "Undo restores the previous geometry setting");
            stationary.value = true;
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.True(!Model().preserveStationaryDetails && !stationary.value, "Undo clears stationary detail preservation");
            Undo.PerformRedo();
            await Tick();
            t.True(Model().preserveStationaryDetails && stationary.value, "Redo restores stationary detail preservation");
            var trajectory = motion.Q<EnumField>("morphInterpolation");
            trajectory.value = MorphInterpolation.AutomaticCurve;
            t.True(trajectory.label == "Trajectory" && Model().interpolation == MorphInterpolation.AutomaticCurve,
                "Trajectory updates the group's interpolation");
            motion.value = false;
            await WaitForDisplay(blend, false);
            t.Near(1, doc.PreviewTime, .00001, "Editing Motion preserves the frame");
            ContextAction(a, moved, "Remove Pair");
            t.Equal(0, Pair().anchors.Count, "Removing B removes the whole pair");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            await Tick();
            t.Equal(1, Pair().anchors.Count, "Undo restores the removed pair");
            // Undo reinstantiates layers. Let Layer Settings bind the restored group before editing it.
            typeof(WhimTexWindow).GetMethod("RefreshToolkitInterface", Instance).Invoke(main, new object[] { true });
            main.rootVisualElement.Q<EnumField>("morphMode").value = MorphMode.Vector;
            t.Equal(MorphMode.Vector, Model().mode, "Layer Settings edits the restored group");
            await WaitForDisplay(motion, false);
            t.True(!Displayed(motion) && !Displayed(experimental) && !Displayed(blend) && result.image == null,
                "Vector hides both raster sections and releases Result");
            t.True(Model().preserveStationaryDetails && Model().interpolation == MorphInterpolation.AutomaticCurve &&
                Model().visibilityMask, "Vector retains the group's raster settings");
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
            void RenderPausedFrame()
            {
                // A utility window can cover the main canvas. Evaluate its current frame explicitly
                // so the fallback indicator can read the result of actual geometry matching.
                using var time = (IDisposable)typeof(WhimTexDocument).GetMethod("EvaluateAtTime", Instance)
                    .Invoke(doc, new object[] { doc.PreviewTime });
                UnityEngine.Object.DestroyImmediate(doc.ComposeCanvas());
            }
            left.fillMode = VectorFillMode.LinearGradient;
            right.fillMode = VectorFillMode.RadialGradient;
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc, null);
            await Tick();
            Submit(root.Q<Button>("morphMidpoint"));
            RenderPausedFrame();
            for (int i = 0; i < 30 && banner.style.display.value != DisplayStyle.Flex; i++) await Tick();
            t.True(!doc.IsPlaying && banner.style.display.value == DisplayStyle.Flex,
                "Paused midpoint reports a rendered paint mismatch between eligible Shape keys");
            left.fillMode = right.fillMode = VectorFillMode.Solid;
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc, null);
            RenderPausedFrame();
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
            var nextGroup = new Layer(new MorphGroupLayerBehaviour()) { layerName = "Second morph" };
            nextGroup.children.AddRange(new[] { Shape(-8,Color.white), Shape(8,Color.black) });
            doc.layers.Add(nextGroup);
            typeof(WhimTexDocument).GetMethod("MarkChanged", Instance).Invoke(doc,null);
            await Tick();
            var select = typeof(WhimTexWindow).GetMethod("SelectOnlyLayer", Instance);
            var groupField = type.GetField("groupId", Instance);
            var current = doc.layers[0];
            var oldSplit = root.Q<WhimTexSplitView>("morphEndpointsSplit");
            Click(a, new Vector2(.02f,.97f));
            t.True(type.GetField("pendingPane",Instance).GetValue(editor) != null, "An unfinished draft exists before selecting the next group");
            select.Invoke(main,new object[] {nextGroup.Id});
            await Tick();
            t.True((string)groupField.GetValue(editor) == nextGroup.Id && choices.choices.Count == 1 &&
                editor.titleContent.text.Contains("Second morph") && root.Q<WhimTexSplitView>("morphEndpointsSplit") == oldSplit &&
                type.GetField("pendingPane",Instance).GetValue(editor) == null, $"Layers retargets the same workspace and clears the old draft: current={groupField.GetValue(editor)}, expected={nextGroup.Id}, choices={choices.choices.Count}, title={editor.titleContent.text}");
            select.Invoke(main,new object[]{nextGroup.children[0].Id});
            t.Equal(nextGroup.Id, (string)groupField.GetValue(editor), "A regular layer keeps the current morph open");
            type.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null,new object[]{doc,current});
            t.True(ReferenceEquals(singleton.GetValue(null),editor) && (string)groupField.GetValue(editor) == current.Id &&
                Resources.FindObjectsOfTypeAll(type).Cast<EditorWindow>().Count(w =>
                    (WhimTexDocument)type.GetField("document",Instance).GetValue(w) == doc) == 1,
                "Explicit Open also switches groups in the same utility window");
            var otherDoc = ScriptableObject.CreateInstance<WhimTexDocument>();
            try
            {
                var otherGroup = new Layer(new MorphGroupLayerBehaviour()) { layerName = "Other document" };
                otherDoc.layers.Add(otherGroup);
                typeof(WhimTexDocument).GetMethod("MarkChanged",Instance).Invoke(otherDoc,null);
                var open = type.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Static);
                open.Invoke(null,new object[]{otherDoc,otherGroup});
                t.True(ReferenceEquals(singleton.GetValue(null),editor) && editor.titleContent.text.Contains("Other document") &&
                    choices.choices.Count == 0 && a.Q<Image>().image == null && result.image == null,
                    "Opening a group from another document reuses the window and releases the previous frames");
                open.Invoke(null,new object[]{doc,current});
            }
            finally { UnityEngine.Object.DestroyImmediate(otherDoc); }
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (editor != null) editor.Close(); },
                () => { if (doc != null) Undo.ClearUndo(doc); },
                () => { if (main != null) { Undo.ClearUndo(main); main.DiscardChanges(); main.Close(); } },
                () => singleton.SetValue(null, artistsWindow),
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
        var p = new Vector2(.16f, .22f);
        var q = new Vector2(.82f, .25f);
        var r = new Vector2(.52f, .83f);
        Click(b, q); Click(b, r); Click(a, p);
        using (var cancel = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
        {
            cancel.target = a;
            a.SendEvent(cancel);
        }
        Click(b, p); Click(b, q); Click(b, r); Click(b, p);
        Click(a, p); Click(a, q); Click(a, r);
        Submit(root.Q<Button>("finishMorphGuide"));
        t.True(pair().guides.Single().closed && pair().guides[0].from.Count == 3 && pair().guides[0].to.Count == 3,
            "Closing the first side closes the paired contour on commit");
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await tick();

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
        ZoomAt(result, new Vector2(.45f,.5f), -3);
        await tick();
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

        var left = new Vector2(.2f, .2f);
        var right = new Vector2(.8f, .2f);
        var top = new Vector2(.5f, .85f);
        var finish = root.Q<Button>("finishMorphGuide");
        Click(b,left); await tick(); Click(b,right); await tick();
        Click(a,left); await tick(); Click(a,right); await tick(); Submit(finish);
        await tick();
        var open = pair().guides.Single();
        t.True(!open.closed && open.from.Count == 2 && Vector2.Distance(open.from[0].position,left) < .001f &&
            Vector2.Distance(open.from[1].position,right) < .001f, $"Open guide endpoints {open.from[0].position}, {open.from[^1].position}");

        var foreground = root.Q<Toggle>("morphForegroundContour");
        t.True(!foreground.enabledInHierarchy, "Only closed guides offer foreground masking");
        foreground.value = true;
        t.True(!pair().guides.Single().foreground && !foreground.value, "An open path rejects foreground masking");
        Submit(root.Q<Button>("removeMorphPair")); await tick();
        t.Equal(0,pair().guides.Count,"Removing the selected open guide keeps the correction point");
        Click(a,left); await tick(); Click(a,right); await tick(); Click(a,top); Click(a,left); await tick();
        Click(b,left); await tick(); Click(b,right); Click(b,top); await tick(); Submit(finish);
        await tick();
        Click(a, (right + top) * .5f);
        mode.value = MorphCorrectionMode.LocalChoice;
        t.True(pair().guides.Single().correction.mode == MorphCorrectionMode.LocalChoice && pair().anchors[0].correction.Equals(before),
            "A contour has one correction, independent of the point");
        foreground.value = true;
        t.True(pair().guides.Single().foreground, "One toggle marks the complete paired loop");
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await tick();
        t.True(pair().guides.Single().closed && !pair().guides[0].foreground, "One Undo unmarks the complete loop");
        pair().guides[0].marks.Add(new Vector2(.2f, 1e-8f));
        pair().guides[0].marks.Add(new Vector2(.6f, 2e-8f));
        ContextAction(b, right, "Reverse Direction");
        t.True(Vector2.Distance(pair().guides[0].to[0].position,left)<.001f &&
            Vector2.Distance(pair().guides[0].to[1].position,top)<.001f &&
            Vector2.Distance(pair().guides[0].from[1].position,right)<.001f, "Reversing B preserves the seam and orientation of A");

        var reversedMarks = pair().guides[0].marks;
        t.True(reversedMarks[0].x == .2f && reversedMarks[1].x == .6f &&
            reversedMarks[0].y > .999999f && reversedMarks[0].y < reversedMarks[1].y && reversedMarks[1].y < 1,
            "Reversed marks near the seam remain distinct, ordered, and inside the contour");
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

    static Vector2 AtUv(Rect rect, Vector2 uv) => new Vector2(rect.xMin + uv.x * rect.width, rect.yMax - uv.y * rect.height);

    static void ZoomAt(VisualElement pane, Vector2 uv, float delta)
    {
        using var wheel = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel,
            mousePosition = AtUv(pane.Q<Image>().worldBound, uv), delta = new Vector2(0,delta) });
        wheel.target = pane;
        pane.SendEvent(wheel);
    }

    static async Task CheckViews(TestContext t, VisualElement root, Func<Task> tick, Vector2 uv, Func<int> pointCount)
    {
        var a = root.Q("morphPreviewA");
        var b = root.Q("morphPreviewB");
        var result = root.Q("morphPreviewResult");
        var panes = new[] { a,b,result };
        Rect Image(VisualElement pane) => pane.Q<Image>().worldBound;
        var before = panes.Select(Image).ToArray();
        var motion = root.Q<Foldout>("morphMotion");
        var advanced = root.Q<Foldout>("morphExperimental");
        motion.value = advanced.value = true;
        await tick(); await tick();
        for (int i=0;i<panes.Length;i++)
            t.True(Vector2.Distance(before[i].position,Image(panes[i]).position) < 1 &&
                Vector2.Distance(before[i].size,Image(panes[i]).size) < 1, "Opening settings keeps the image stationary in " + panes[i].name);
        motion.value = advanced.value = false;
        await tick();
        foreach (var pane in panes)
        {
            var other = pane == b ? a : b;
            var otherRect = Image(other);
            var fitted = Image(pane);
            var texture = pane.Q<Image>().image;
            Vector2 pivot = AtUv(fitted,uv);
            ZoomAt(pane, uv,-4);
            await tick(); await tick();
            var zoomed = Image(pane);
            t.True(zoomed.width > fitted.width*1.3f && Vector2.Distance(pivot,AtUv(zoomed,uv)) < 1.5f,
                "Scrolling keeps the texel under the pointer in " + pane.name);
            Vector2 offset = new Vector2(7,-4);
            Point(pane, EventType.MouseDown,uv,button:2);
            t.True(pane.HasPointerCapture(PointerId.mousePointerId), "Middle drag captures " + pane.name);
            SendPointer(pane, pivot + offset*.5f, EventType.MouseDrag,2,1);
            await tick();
            t.True(Vector2.Distance(AtUv(Image(pane),uv), pivot+offset*.5f) < 1.5f && pane.HasPointerCapture(PointerId.mousePointerId),
                "Middle drag moves the frame before release");
            SendPointer(pane, pivot + offset, EventType.MouseUp,2,1);
            await tick(); await tick();
            var panned = Image(pane);
            t.True(Vector2.Distance(AtUv(panned,uv),pivot + offset) < 1.5f && otherRect == Image(other) &&
                texture == pane.Q<Image>().image && pointCount() == 1, "Pan moves only the retained image and its overlays");
            if (pane == a)
            {
                var marker = pane.Q<Label>(className:"whimtex-morph-marker");
                t.True(Vector2.Distance(marker.worldBound.center, AtUv(panned,uv)) < 1.5f,
                    "Numbered guide follows the scaled and translated image");
            }
            Vector2 panPivot = AtUv(panned,uv);
            ZoomAt(pane,uv,2); await tick(); await tick();
            t.True(Image(pane).width < panned.width && Vector2.Distance(AtUv(Image(pane),uv),panPivot) < 1.5f,
                "Reverse wheel direction zooms out around the same detail");
            Point(pane,EventType.MouseDown,uv,button:2,clicks:2);
            Point(pane,EventType.MouseUp,uv,button:2,clicks:2);
            await tick(); await tick();
            t.True(Vector2.Distance(Image(pane).size,fitted.size) < 1 &&
                Vector2.Distance(Image(pane).center,fitted.center) < 1, "Double middle-click fits " + pane.name);
        }
        var ab = root.Q<WhimTexSplitView>("morphEndpointsSplit");
        var all = root.Q<WhimTexSplitView>("morphResultSplit");
        float left = ab.FixedPaneSize, right = all.FixedPaneSize;
        float aWidth = a.worldBound.width, rWidth = result.worldBound.width;
        DragDivider(ab, 20);
        await tick(); await tick();
        t.True(a.worldBound.width > aWidth+15 && Mathf.Abs(result.worldBound.width-rWidth) < 1,
            "The A/B divider redistributes the source columns");
        DragDivider(all, -16);
        await tick(); await tick();
        t.True(result.worldBound.width > rWidth+12 && Vector2.Distance(a.Q<Label>(className:"whimtex-morph-marker").worldBound.center,
            AtUv(Image(a),uv)) < 1.5f, "The Result divider resizes Result and guides stay aligned");
        var header = root.Q<WhimTexSplitView>("morphHeaderSplit");
        float headerHeight = header.FixedPaneSize, y = a.worldBound.yMin;
        DragDivider(header, 18); await tick(); await tick();
        t.True(a.worldBound.yMin > y + 14, "Only a deliberate header resize moves the preview row");
        header.FixedPaneSize = headerHeight;
        all.FixedPaneSize = right;
        await tick(); ab.FixedPaneSize = left; await tick();
    }

    static async Task MeasureNavigation(TestContext t, EditorWindow editor, VisualElement pane, CancellationToken token)
    {
        var image = pane.Q<Image>();
        var frame = image.parent;
        t.True(image.image != null && pane.worldBound.width > 300, "Visible retained preview at benchmark size");
        var marker = new VisualElement { pickingMode = PickingMode.Ignore };
        marker.style.position = Position.Absolute;
        marker.style.width = marker.style.height = 1;
        pane.Add(marker);
        var originalDraw = frame.generateVisualContent;
        int submitted = 0, painted = 0, frameBuilds = 0;
        double lastPaint = 0, drawMilliseconds = 0;
        bool reference = false;
        var intervals = new System.Collections.Generic.List<double>();
        frame.generateVisualContent = context =>
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            if (reference) DrawReferenceGrid(context, frame.WorldToLocal(pane.worldBound));
            else originalDraw(context);
            drawMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            frameBuilds++;
        };
        marker.generateVisualContent += _ =>
        {
            if (painted == submitted) return;
            double now = EditorApplication.timeSinceStartup;
            if (submitted > 6) intervals.Add((now - lastPaint) * 1000);
            lastPaint = now;
            painted = submitted;
        };
        var report = new System.Collections.Generic.List<string>();
        try
        {
            var uv = new Vector2(.5f,.5f);
            ZoomAt(pane,uv,-20);
            await AsyncD.Tick(pane,token);
            ZoomAt(pane,uv,-4);
            await AsyncD.Tick(pane,token);
            // Interleave both implementations on the same visible panel. The reference isolates
            // the former background work while keeping the new transform/input path for both.
            foreach (string mode in new[] { "idle", "pan reference", "pan current", "zoom current", "zoom reference",
                                            "zoom reference", "zoom current", "pan current", "pan reference" })
            {
                reference = mode.EndsWith("reference",StringComparison.Ordinal);
                bool pan = mode.StartsWith("pan",StringComparison.Ordinal);
                bool zoom = mode.StartsWith("zoom",StringComparison.Ordinal);
                frame.MarkDirtyRepaint();
                await AsyncD.Tick(pane,token);
                var start = pane.worldBound.center;
                if (pan) SendPointer(pane,start,EventType.MouseDown,2,1);
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                intervals.Clear();
                submitted = painted = frameBuilds = 0;
                drawMilliseconds = 0;
                void Pump()
                {
                    try
                    {
                        editor.Repaint();
                        if (painted != submitted) return;
                        if (submitted == 30) { completion.TrySetResult(true); return; }
                        submitted++;
                        if (pan) SendPointer(pane, start + new Vector2(Mathf.Sin(submitted * .15f)*26, 0), EventType.MouseDrag,2,1);
                        if (zoom) ZoomAt(pane,uv, (submitted-1) % 6 < 3 ? -1 : 1);
                        if (reference) frame.MarkDirtyRepaint();
                        marker.MarkDirtyRepaint();
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                }
                EditorApplication.update += Pump;
                using (token.Register(() => completion.TrySetCanceled()))
                    try { await completion.Task; }
                    finally
                    {
                        EditorApplication.update -= Pump;
                        if (pan) SendPointer(pane,start,EventType.MouseUp,2,1);
                    }
                intervals.Sort();
                t.Equal(24,intervals.Count,"One sample per panel repaint after warmup");
                if (pan && !reference) t.Equal(0,frameBuilds,"Panning the enlarged frame reuses its mesh");
                if (zoom || reference) t.True(frameBuilds >= intervals.Count,"Changed geometry was actually drawn");
                report.Add(FormattableString.Invariant($"{mode}: median={intervals[12]:F2} ms, p95={intervals[22]:F2} ms, grid CPU={drawMilliseconds/30:F3} ms/frame, grid builds={frameBuilds}/30, pane={pane.worldBound.size}"));
            }
            string folder = MigrationD.ProjectPath("Temp/WhimTex/test-runs");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(folder,"morph-navigation-timing-" + Guid.NewGuid().ToString("N") + ".txt"), report);
        }
        finally { frame.generateVisualContent = originalDraw; marker.RemoveFromHierarchy(); }
    }

    static void DrawReferenceGrid(MeshGenerationContext context, Rect visible)
    {
        var rect = context.visualElement.contentRect;
        var painter = context.painter2D;
        const int size = 8;
        int x0 = Mathf.FloorToInt(Mathf.Max(0,visible.xMin)/size)*size;
        int y0 = Mathf.FloorToInt(Mathf.Max(0,visible.yMin)/size)*size;
        for (int y = y0; y < Mathf.Min(rect.height,visible.yMax); y += size)
        for (int x = x0; x < Mathf.Min(rect.width,visible.xMax); x += size)
        {
            float shade = ((x/size + y/size) & 1) == 0 ? .28f : .42f;
            float right = Mathf.Min(x+size,rect.width), bottom = Mathf.Min(y+size,rect.height);
            painter.fillColor = new Color(shade,shade,shade,1);
            painter.BeginPath();
            painter.MoveTo(new Vector2(x,y)); painter.LineTo(new Vector2(right,y));
            painter.LineTo(new Vector2(right,bottom)); painter.LineTo(new Vector2(x,bottom));
            painter.ClosePath(); painter.Fill();
        }
    }

    static void DragDivider(WhimTexSplitView split, float amount)
    {
        var divider = split.hierarchy[1];
        var start = divider.worldBound.center;
        var end = start + (split.IsHorizontal ? Vector2.right : Vector2.up) * amount;
        var picked = divider.panel.Pick(start);
        if (picked != divider && !divider.Contains(picked)) throw new Exception("Obstructed divider in " + split.name);
        SendPointer(picked, start, EventType.MouseDown,0,1);
        SendPointer(divider, end, EventType.MouseDrag,0,1);
        SendPointer(divider, end, EventType.MouseUp,0,1);
    }

    static void Submit(Button button)
    {
        using var evt = NavigationSubmitEvent.GetPooled();
        evt.target = button;
        button.SendEvent(evt);
    }

    sealed class MenuCapture : ContextualMenuManager
    {
        public override void DisplayMenuIfEventMatches(EventBase evt, IEventHandler handler) { }
        protected override void DoDisplayMenu(DropdownMenu menu, EventBase triggerEvent) { }
    }

    static void ContextAction(VisualElement pane, Vector2 uv, string name)
    {
        var image = pane.Q<Image>().worldBound;
        var at = new Vector2(image.xMin + uv.x * image.width, image.yMax - uv.y * image.height);
        using var trigger = MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = at, button = 1 });
        var menu = new DropdownMenu();
        using var populate = ContextualMenuPopulateEvent.GetPooled(trigger, menu, pane, new MenuCapture());
        pane.SendEvent(populate);
        var actions = menu.MenuItems().OfType<DropdownMenuAction>().ToArray();
        var chosen = actions.SingleOrDefault(action => action.name == name);
        if (chosen == null) throw new InvalidOperationException($"Missing {name} at {pane.name} {uv}. Available: {string.Join(", ", actions.Select(x => x.name))}");
        chosen.Execute();
    }

    static void Point(VisualElement pane, EventType type, Vector2 uv, int button = 0, bool pick = false, int clicks = 1)
    {
        var image = pane.Q<Image>().worldBound;
        var point = new Vector2(image.xMin + uv.x * image.width, image.yMax - uv.y * image.height);
        var target = pick ? pane.panel.Pick(point) : pane;
        SendPointer(target, point, type, button, clicks);
    }

    static void SendPointer(VisualElement target, Vector2 point, EventType type, int button, int clicks)
    {
        var input = new Event { type = type, button = button, mousePosition = point, clickCount = clicks };
        using EventBase evt = type switch
        {
            EventType.MouseDown => PointerDownEvent.GetPooled(input),
            EventType.MouseUp => PointerUpEvent.GetPooled(input),
            _ => PointerMoveEvent.GetPooled(input)
        };
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
