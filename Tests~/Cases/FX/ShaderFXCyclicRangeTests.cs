using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXCyclicRangeTestWindow : EditorWindow { }

public static class ShaderFXCyclicRangeTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Code = @"
// @param preview(P) float2 _R = (330, 30) [0 % 360]
// @param preview(S) float _Angle = 20 [0 % 360]
// @param preview(P) float2 _Ordinary = (0.2, 0.8) [0 .. 1]
float4 P(float2 uv, float2 value) { return float4(value / 360, uv.x, 1); }
float4 S(float2 uv, float value) { return float4(uv.x, value / 360, 0, 1); }
float4 ApplyFX(float2 uv, float4 color) { return color; }
";
    static Type T(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)T("ShaderFXMetadata").GetMethod("Parse", F).Invoke(null, new object[] { source, false, null });
    static IReadOnlyList<ShaderFXParameter> Values(object effect) => (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(effect);
    static ShaderFX Create(string source) => (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
        new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null).Invoke(null, new object[] { null, source, Parse(source) });
    static string Key(string id) => "WhimTex.Tests.CyclicRange." + Guid.Parse(id).ToString("D");

    static void Metadata(TestContext t)
    {
        var p = Parse(Code)[0];
        t.True(p.cyclicRange && p.controls[0].cyclicRange, "Cyclic flag belongs to parameter and control");
        t.Equal(new Vector4(330, 30, 0, 0), p.vectorValue, "Wrapped order is retained");
        t.Equal(new Vector4(0, 360, 0, 0), Parse("// @param float2 _R [0 % 360]")[0].vectorValue, "Defaultless range is full, not empty");
        var copy = (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", F).Invoke(p, null);
        t.True(copy.cyclicRange && copy.controls[0].cyclicRange, "Copy retains cyclic semantics");
        var next = Parse(Code);
        T("ShaderFXMetadata").GetMethod("PreserveValues", F).Invoke(null, new object[] { next, new[] { p } });
        t.Equal(p.vectorValue, next[0].vectorValue, "Reapply does not sort wrapped endpoints");
        var scalar = Parse(Code)[1];
        foreach (var item in new[] { new Vector2(370, 10), new Vector2(-10, 350), new Vector2(1081, 1), new Vector2(360, 360), new Vector2(0, 0) })
            t.Near(item.y, (float)typeof(ShaderFXParameter).GetMethod("Clamp", F).Invoke(scalar, new object[] { item.x }), .0001, "Float cyclic numeric normalization " + item.x);
        var api = typeof(WhimTexApi).GetMethod("ReadFxParameterValue", F);
        var json = api.GetParameters()[1].ParameterType.GetMethod("Parse", new[] { typeof(string) });
        foreach (string text in new[] { "[330,30]", "[0,360]", "[0,0]", "[360,0]", "[-330,30]", "[330,690]" })
        {
            var result = (ShaderFXParameter)api.Invoke(null, new object[] { p, json.Invoke(null, new object[] { text }), null });
            t.True(result.cyclicRange, "API accepts cyclic endpoint order: " + text);
        }
        ShaderFX effect = null;
        try
        {
            effect = Create(Code);
            string exported = (string)T("ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { effect, "Tests/Cyclic Range" });
            var restored = Parse(exported);
            t.True(restored[0].cyclicRange && restored[1].cyclicRange, "Preset preserves percent syntax for float2 and float");
            t.Equal(p.vectorValue, restored[0].vectorValue, "Preset preserves wrapped pair");
            string native = EditorJsonUtility.ToJson(effect);
            EditorJsonUtility.FromJsonOverwrite(native, effect);
            t.True(Values(effect)[0].cyclicRange && Values(effect)[0].controls[0].cyclicRange, "Native roundtrip retains cyclic flags");
            foreach (var pair in new[] { new Vector2(-330, 30), new Vector2(330, 690), new Vector2(30.1f - 360, 30.1f) })
            {
                var full = Values(effect)[0]; full.vectorValue = pair;
                string fullSource = (string)T("ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { effect, "Tests/Cyclic Range" });
                t.Equal(full.vectorValue, Parse(fullSource)[0].vectorValue, "Preset retains anchored full-cycle coordinates");
                var retained = Parse(Code);
                T("ShaderFXMetadata").GetMethod("PreserveValues", F).Invoke(null, new object[] { retained, new[] { full } });
                t.Equal(full.vectorValue, retained[0].vectorValue, "Reapply retains anchored full cycle without clamping or wrapping it to empty");
                var stored = full.vectorValue;
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(effect), effect);
                t.Equal(stored, Values(effect)[0].vectorValue, "Native roundtrip retains full-cycle anchor");
            }
        }
        finally { if (effect != null) UnityEngine.Object.DestroyImmediate(effect); }
    }

    public static string Start(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null) return TestContext.Result("failed", 0, "Duplicate cyclic range test").ToJson();
        var state = new object[9]; state[8] = EditorApplication.timeSinceStartup + 20; AppDomain.CurrentDomain.SetData(Key(id), state);
        var metadata = JsonUtility.FromJson<TestResult>(TestContext.Run("Cyclic range metadata", Metadata));
        state[6] = metadata.checks;
        if (metadata.status != "passed") { state[3] = metadata.ToJson(); return (string)state[3]; }
        try
        {
            state[2] = EditorWindow.focusedWindow;
            state[1] = Create(Code);
            var window = ScriptableObject.CreateInstance<ShaderFXCyclicRangeTestWindow>(); state[0] = window;
            var view = (VisualElement)Activator.CreateInstance(T("ShaderFXParameterView"), F, null, new[] { state[1] }, null);
            window.rootVisualElement.Add(view); state[5] = view;
            window.rootVisualElement.Add(new Slider("Native reference", 0, 1) { name = "nativeSliderReference", showInputField = true });
            string source = System.IO.File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/HueSaturation.hlsl");
            var selective = Create(source); state[7] = selective;
            foreach (var p in Values(selective)) if (p.name == "_Range1Enabled") p.floatValue = 1;
            window.rootVisualElement.Add((VisualElement)Activator.CreateInstance(T("ShaderFXParameterView"), F, null, new object[] { selective }, null));
            window.ShowUtility(); window.position = new Rect(120, 120, 550, 650);
            state[4] = window.rootVisualElement.schedule.Execute(() => Check(state, false)).StartingIn(900);
        }
        catch (Exception e) { state[3] = TestContext.Result("failed", 0, "Cyclic UI setup", e.ToString()).ToJson(); Release(state); }
        return Poll(id);
    }

    static void Check(object[] state, bool narrow)
    {
        var current = ((VisualElement)state[5]).Q(className: "whimtex-cyclic-range-track");
        if (current != null && !ProjectiveReady(current) && EditorApplication.timeSinceStartup < (double)state[8])
        {
            state[4] = ((EditorWindow)state[0]).rootVisualElement.schedule.Execute(() => Check(state, narrow)).StartingIn(100);
            return;
        }
        var result = JsonUtility.FromJson<TestResult>(TestContext.Run("Cyclic range UI", t =>
        {
            var view = (VisualElement)state[5];
            var field = (BaseField<Vector2>)view.Q(className: "whimtex-cyclic-range");
            t.True(field != null, "Cyclic range uses an independent track without native ordering clamps");
            var track = field.Q(className: "whimtex-cyclic-range-track");
            var start = field.Q("cyclic-range-start"); var end = field.Q("cyclic-range-end");
            var scalarTrack = view.Q<Slider>().Q(className: Slider.trackerUssClassName);
            var ordinaryTrack = view.Q<MinMaxSlider>().Q(className: MinMaxSlider.trackerUssClassName);
            foreach (var other in new[] { scalarTrack, ordinaryTrack })
            {
                t.Near(other.worldBound.xMin, track.worldBound.xMin, .2, "All preview track starts align: scalar=" + scalarTrack.worldBound + " ordinary=" + ordinaryTrack.worldBound + " cyclic=" + track.worldBound);
            }
            t.Near(ordinaryTrack.worldBound.xMax, track.worldBound.xMax, .2, "Ordered and cyclic range track ends align");
            t.Near(10, track.layout.height, .1, "Fixed track height");
            foreach (var handle in new[] { start, end })
            {
                var marker = handle.Q(className: "whimtex-fx-preview-thumb--half");
                t.Near(8, marker.layout.width, .1, "Half triangle width");
                t.Near(12, marker.layout.height, .1, "Half triangle height");
                t.Near(track.worldBound.yMax, marker.worldBound.center.y, .6, "Marker straddles track edge");
            }
            t.True(track.Q<Image>()?.image is RenderTexture, "Preview is embedded in cyclic track");
            var numbers = field.Query<FloatField>().ToList();
            t.Equal(2, numbers.Count, "Two synchronized endpoint fields");
            var nativeNumber = ((EditorWindow)state[0]).rootVisualElement.Q<Slider>("nativeSliderReference").Q<TextField>();
            foreach (var input in numbers)
            {
                t.Near(nativeNumber.layout.width, input.layout.width, .1, "Cyclic numeric width matches native Slider");
                t.Near(nativeNumber.layout.height, input.layout.height, .1, "Cyclic numeric height matches native Slider");
            }
            if (narrow)
            {
                foreach (var handle in new[] { start, end })
                {
                    var marker = handle.Q(className: "whimtex-fx-preview-thumb--half");
                    var visible = ((EditorWindow)state[0]).rootVisualElement.worldBound;
                    t.True(marker.worldBound.xMin >= visible.xMin && marker.worldBound.xMax <= visible.xMax,
                        "Full-range edge marker remains inside the visible window");
                    var hit = new Vector2(handle == start ? handle.worldBound.xMax - 2 : handle.worldBound.xMin + 2, handle.worldBound.center.y);
                    var picked = handle.panel.Pick(hit);
                    t.True(picked == handle || picked != null && handle.Contains(picked), "Both full-range handles remain directly pickable: " + handle.name + " picked=" + picked?.name + " at=" + hit);
                }
                Drag(start, start.worldBound.center, start.worldBound.center + new Vector2(track.worldBound.width * 120 / 360, 0));
                t.Near(120, field.value.x, .02, "Minimum can leave a full cycle immediately");
                t.Near(360, field.value.y, 0, "Dragging the minimum out of a full cycle keeps the maximum fixed");
                return;
            }
            RepeatDrag(t, field, track, start, 0);
            RepeatDrag(t, field, track, end, 1);
            numbers[0].value = 370; numbers[1].value = -10;
            t.Equal(new Vector2(10, 350), field.value, "Endpoint numeric input wraps in both directions");
            field.value = new Vector2(330, 30);
            var a = start.worldBound.center;
            string dragInfo = Drag(start, a, new Vector2(a.x + track.worldBound.width * 40 / 360, a.y));
            t.Near(10, field.value.x, .02, "Start handle crosses right edge without changing identity " + dragInfo);
            t.Near(30, field.value.y, .02, "End remains fixed while start wraps");
            field.value = new Vector2(330, 30);
            var b = end.worldBound.center;
            Drag(end, b, new Vector2(b.x - track.worldBound.width * 40 / 360, b.y));
            t.Near(330, field.value.x, .02, "Start remains fixed while end wraps");
            t.Near(350, field.value.y, .02, "End handle crosses left edge");
            field.value = new Vector2(330, 30);
            var pnt = new Vector2(track.worldBound.xMax - track.worldBound.width * 10 / 360, track.worldBound.center.y);
            Drag(track, pnt, new Vector2(pnt.x + track.worldBound.width * 40 / 360, pnt.y));
            t.Near(10, field.value.x, .02, "Dragging wrapped selection moves start");
            t.Near(70, field.value.y, .02, "Dragging selection preserves width");
            field.value = new Vector2(0, 360);
            Drag(track, track.worldBound.center, track.worldBound.center + new Vector2(20, 0));
            t.Near(360, field.value.y - field.value.x, .0001, "Full circle remains full when moved");
            t.True(field.value.x > 0, "Moving a full cycle moves its anchor instead of resetting it to the limits");
            field.value = new Vector2(30, 390);
            using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.LeftArrow }))
                start.SendEvent(key);
            t.Equal(new Vector2(30, 390), field.value, "Blocked start movement does not rewrite the full-cycle coordinates");
            field.value = new Vector2(-330, 30);
            using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.RightArrow }))
                end.SendEvent(key);
            t.Equal(new Vector2(-330, 30), field.value, "Blocked end movement does not rewrite the full-cycle coordinates");
            field.value = new Vector2(0, 0);
            using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.RightArrow, modifiers = EventModifiers.Control }))
                end.SendEvent(key);
            t.Near(field.value.x, field.value.y, .0001, "Moving an empty interval remains empty");
            var scalar = view.Q<Slider>(); var scalarField = scalar.GetFirstAncestorOfType<BaseField<float>>();
            var number = scalarField.Q<FloatField>();
            number.value = 370; t.Near(10, Values(state[1])[1].floatValue, .0001, "Scalar manual overflow repeats");
            number.value = -10; t.Near(350, Values(state[1])[1].floatValue, .0001, "Scalar manual underflow repeats");
            scalar.value = 370; t.Near(360, Values(state[1])[1].floatValue, .0001, "Scalar slider itself remains clamped");
            var hue = ((EditorWindow)state[0]).rootVisualElement.Query<Image>().ToList();
            Image hueTrack = null;
            foreach (var image in hue) if (image.parent.ClassListContains("whimtex-cyclic-range-track") && !view.Contains(image)) { hueTrack = image; break; }
            t.True(hueTrack != null && hueTrack.image is RenderTexture, "Selective HSV hue preview rendered");
            var quarter = Pixel(hueTrack.image, .25f, .8f); var threeQuarter = Pixel(hueTrack.image, .75f, .8f);
            t.True(quarter.g > quarter.r && quarter.g > quarter.b, "First quarter is yellow-green, not repeated red");
            t.True(threeQuarter.b > threeQuarter.r && threeQuarter.b > threeQuarter.g, "Third quarter is violet-blue, one hue turn");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            field.value = new Vector2(30, 390); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            var beforeUndo = Values(state[1])[0].vectorValue;
            field.value = new Vector2(100, 200); Undo.FlushUndoRecordObjects();
            Undo.PerformUndo(); t.Equal(beforeUndo, Values(state[1])[0].vectorValue, "Undo restores both cyclic endpoints");
            Undo.PerformRedo(); t.Equal(new Vector4(100, 200, 0, 0), Values(state[1])[0].vectorValue, "Redo restores both cyclic endpoints");
            view.GetType().GetMethod("Refresh", F).Invoke(view, null);
            ((BaseField<Vector2>)view.Q(className: "whimtex-cyclic-range")).value = new Vector2(0, 360);
        }));
        result.checks += (int)state[6];
        if (result.status != "passed" || narrow) { state[3] = result.ToJson(); Release(state); return; }
        state[6] = result.checks;
        ((EditorWindow)state[0]).position = new Rect(120, 120, 360, 650);
        state[4] = ((EditorWindow)state[0]).rootVisualElement.schedule.Execute(() => Check(state, true)).StartingIn(500);
    }
    static bool ProjectiveReady(VisualElement element) => !float.IsNaN(element.worldBound.width) && element.worldBound.width > 0;
    static void RepeatDrag(TestContext t, BaseField<Vector2> field, VisualElement track, VisualElement handle, int endpoint)
    {
        field.value = new Vector2(330, 30);
        var origin = handle.worldBound.center;
        var initial = field.value;
        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = origin, button = 0 })) handle.SendEvent(down);
        try
        {
            float growthDirection = endpoint == 0 ? -1 : 1;
            var full = endpoint == 0 ? new Vector2(-330, 30) : new Vector2(330, 690);
            var numbers = field.Query<FloatField>().ToList();
            void FullStop()
            {
                t.Equal(full, field.value, "Full stop retains the original opposite endpoint, not global min/max");
                float anchor = endpoint == 0 ? 30 : 330;
                t.Near(anchor, numbers[0].value, .0001, "Full-cycle start input stays at the meeting point");
                t.Near(anchor, numbers[1].value, .0001, "Full-cycle end input stays at the meeting point");
                t.Near(anchor / 360 * 100, field.Q("cyclic-range-start").style.left.value.value, .001, "Start marker stays at the meeting point");
                t.Near(anchor / 360 * 100, field.Q("cyclic-range-end").style.left.value.value, .001, "End marker stays at the meeting point");
                field.SetValueWithoutNotify(field.value);
                t.Equal(full, field.value, "Refreshing the field preserves full-cycle identity");
            }
            float total = 0;
            void Move(float degrees)
            {
                var point = origin + new Vector2(track.worldBound.width * degrees / 360, 0);
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, mousePosition = point, button = 0 })) handle.SendEvent(move);
            }
            total = growthDirection * 1080; Move(total);
            FullStop();
            total += growthDirection * 1080; Move(total);
            FullStop();
            total -= growthDirection * 15; Move(total);
            t.Near(endpoint == 0 ? 45 : 330, field.value.x, .03, "Immediate reversal away from the full-circle stop: X");
            t.Near(endpoint == 0 ? 30 : 315, field.value.y, .03, "Immediate reversal away from the full-circle stop: Y");
            float opposite = field.value[1 - endpoint];
            total -= growthDirection * 1080; Move(total);
            t.Equal(new Vector2(opposite, opposite), field.value, "Narrowing stops exactly at the other endpoint");
            total -= growthDirection * 1080; Move(total);
            t.Equal(new Vector2(opposite, opposite), field.value, "Continuing to narrow does not cross the stop");
            total += growthDirection * 15; Move(total);
            t.Near(Mathf.Repeat(opposite + growthDirection * 15, 360), Mathf.Repeat(field.value[endpoint], 360), .03,
                "Reversing away from the empty stop responds immediately without unwinding the overshoot");
            t.Near(opposite, field.value[1 - endpoint], 0, "Opposite endpoint remains fixed after reversal");
            for (int step = 1; step <= 3; step++)
            {
                total += growthDirection * 1080; Move(total);
                FullStop();
            }
            using (var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape })) handle.SendEvent(escape);
            t.Equal(initial, field.value, "Escape cancels a multi-turn drag including a narrowing stop");
            t.True(!field.Q("cyclic-range-slider").HasPointerCapture(0), "Escape releases pointer capture");
        }
        finally
        {
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = origin, button = 0 })) handle.SendEvent(up);
        }
    }
    static string Drag(VisualElement element, Vector2 from, Vector2 to)
    {
        var area = element.GetFirstAncestorOfType<BaseField<Vector2>>().Q("cyclic-range-slider");
        string trace = "attached=" + (element.panel != null) + " from=" + from + " to=" + to;
        EventCallback<PointerDownEvent> onDown = e => trace += " down=" + e.pointerId + "/" + e.button + "/" + e.position;
        EventCallback<PointerMoveEvent> onMove = e => trace += " move=" + e.pointerId + "/" + e.position;
        element.RegisterCallback(onDown, TrickleDown.TrickleDown); element.RegisterCallback(onMove, TrickleDown.TrickleDown);
        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = from, button = 0 })) element.SendEvent(down);
        trace += " capture=" + area.HasPointerCapture(0);
        using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, mousePosition = to, button = 0 })) element.SendEvent(move);
        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = to, button = 0 })) element.SendEvent(up);
        element.UnregisterCallback(onDown, TrickleDown.TrickleDown); element.UnregisterCallback(onMove, TrickleDown.TrickleDown);
        return trace;
    }
    static Color Pixel(Texture source, float u, float v)
    {
        var active = RenderTexture.active;
        var readback = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = (RenderTexture)source;
            readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); readback.Apply();
            return readback.GetPixel(Mathf.Min(source.width - 1, (int)(u * source.width)), Mathf.Min(source.height - 1, (int)(v * source.height)));
        }
        finally { RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(readback); }
    }
    public static string Poll(string id) => AppDomain.CurrentDomain.GetData(Key(id)) is object[] state
        ? (string)state[3] ?? TestContext.Result("running", 0, "Waiting for cyclic range UI").ToJson() : TestContext.Result("failed", 0, "Missing cyclic fixture").ToJson();
    public static string Cleanup(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) is not object[] state) return TestContext.Result("failed", 0, "Missing cyclic fixture").ToJson();
        Release(state); AppDomain.CurrentDomain.SetData(Key(id), null); return TestContext.Result("passed", 1, "Cyclic fixture released").ToJson();
    }
    static void Release(object[] state)
    {
        var errors = new System.Collections.Generic.List<Exception>();
        void ReleaseOwned(Action action)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }
        ReleaseOwned(() =>
        {
            (state[4] as IVisualElementScheduledItem)?.Pause(); state[4] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[0] is EditorWindow window && window != null)
            {
                try { if (window.rootVisualElement.panel != null) window.Close(); }
                finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
            }
            state[0] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[1] is ShaderFX effect && effect != null)
            {
                try { Undo.ClearUndo(effect); }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
            state[1] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[7] is ShaderFX effect && effect != null)
            {
                try { Undo.ClearUndo(effect); }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
            state[7] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[2] is EditorWindow focus && focus != null) focus.Focus(); state[2] = null;
        });
        if (errors.Count != 0)
        {
            var failure = new AggregateException("Owned FX fixture cleanup unfinished; retry cleanup before another run", errors);
            state[3] = TestContext.Result("failed", 0, "Cleanup requires recovery; prior result: " + state[3], failure.ToString()).ToJson();
            throw failure;
        }
    }
}
