using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerEyedropperTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static object Get(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static void Check(bool ok, string name) { T.True(ok, name); }
    public static class FakeApi
    {
        public static bool IsOpened { get; set; }
        public static int Ends, Draws;
        public static void End() { Ends++; IsOpened = false; }
        public static void DrawPreview(Rect rect) { Draws++; }
    }
    static WhimTexColorPicker Open(Action<Color> change)
        => Scope.OwnWindow((WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
            new object[] { Color.red, false, true, WhimTexColorRange.Switchable, null, change, null, null }));
    static void Intent(WhimTexColorPicker picker, bool native = false, int button = 0)
    {
        var field = (UnityEditor.UIElements.ColorField)Get(picker, "eyedropper");
        var target = field.Q(className: UnityEditor.UIElements.ColorField.eyeDropperUssClassName);
        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = button, mousePosition = target.worldBound.center }))
        {
            e.target = target;
            if (native) target.SendEvent(e); else Call(picker, "OnEyedropperPointerDown", e);
        }
    }
    static bool Hidden(WhimTexColorPicker p, string name) => ((VisualElement)Get(p, name)).ClassListContains("whimtex-picker-hidden");
    private static string BodyRun()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0), "Close the borrowed active picker before this case");
        var focus = EditorWindow.focusedWindow;
        WhimTexColorPicker picker = null;
        checks = 0;
        try
        {
            int edits = 0;
            picker = Open(_ => edits++);
            var native = Get(picker, "eyedropperApi");
            Check(native != null, "Native preview adapter binds on this Editor");
            var type = native.GetType();
            var bind = type.GetMethod("Bind", F);
            Check(bind.Invoke(null, new object[] { null }) == null, "Missing type fallback");
            Check(bind.Invoke(null, new object[] { typeof(string) }) == null, "Missing members fallback");
            var fake = bind.Invoke(null, new object[] { typeof(FakeApi) });
            Check(fake != null, "Exact three-member adapter binds fake");
            Set(picker, "eyedropperApi", fake);
            FakeApi.IsOpened = false; FakeApi.Ends = FakeApi.Draws = 0;
            Check(Hidden(picker, "eyedropperPreview") && !Hidden(picker, "hueRing"), "Initially normal wheel");
            Intent(picker, button: 1);
            Check(!(bool)Get(picker, "eyedropperPending"), "Right click ignored");
            FakeApi.IsOpened = true;
            Intent(picker); Call(picker, "UpdateEyedropper");
            Check(!(bool)Get(picker, "eyedropperOwned"), "Other native session not adopted");
            Call(picker, "StopEyedropper");
            Check(FakeApi.Ends == 0 && FakeApi.IsOpened, "Other session not canceled");
            FakeApi.IsOpened = false;
            Intent(picker); Call(picker, "ClaimEyedropper");
            Check(!(bool)Get(picker, "eyedropperOwned"), "Failed native start ignored");
            Intent(picker); FakeApi.IsOpened = true; Call(picker, "ClaimEyedropper");
            Check((bool)Get(picker, "eyedropperOwned"), "Own native start claimed");
            Check(!Hidden(picker, "eyedropperPreview") && Hidden(picker, "hueRing") && Hidden(picker, "plane"), "Magnifier replaces wheel and square");
            Call(fake, "Draw", new Rect(0, 0, 220, 220));
            Check(FakeApi.Draws == 1, "Draw delegate invoked");
            using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
            { e.target = picker.rootVisualElement; picker.rootVisualElement.SendEvent(e); }
            Check(picker != null && FakeApi.Ends == 1, "Escape cancels own eyedropper without closing picker");
            Check(Hidden(picker, "eyedropperPreview") && !Hidden(picker, "plane"), "Canceled preview restores controls");
            Check(edits == 0, "Hover/cancel do not change selected color");
            Intent(picker); FakeApi.IsOpened = true; Call(picker, "ClaimEyedropper");
            FakeApi.IsOpened = false;
            ((UnityEditor.UIElements.ColorField)Get(picker, "eyedropper")).value = Color.green;
            Call(picker, "UpdateEyedropper");
            Check(edits == 1 && (Color)Get(picker, "color") == Color.green, "Public ColorField delivers selection");
            Check(Hidden(picker, "eyedropperPreview") && !Hidden(picker, "hueRing"), "Selection restores wheel");
            Set(picker, "eyedropperApi", null);
            Intent(picker); Call(picker, "UpdateEyedropper");
            ((UnityEditor.UIElements.ColorField)Get(picker, "eyedropper")).value = Color.blue;
            Check(edits == 2 && Hidden(picker, "eyedropperPreview"), "Unavailable adapter preserves native value path");
            Set(picker, "eyedropperApi", fake);
            Intent(picker); FakeApi.IsOpened = true;
            picker.Close(); picker = null;
            Check(FakeApi.Ends == 2 && !FakeApi.IsOpened, "Close cancels pending owned session");
            return null;
        }
        finally { if (picker != null) Call(picker, "Finish", false); FakeApi.IsOpened = false; if (focus != null) focus.Focus(); }
    }

    sealed class NativeBindings
    {
        internal readonly Func<bool> IsOpened;
        internal readonly Action<Rect> DrawPreview;
        internal readonly Action End;
        internal NativeBindings()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.EyeDropper", true);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            IsOpened = (Func<bool>)type.GetProperty("IsOpened", flags).GetGetMethod().CreateDelegate(typeof(Func<bool>));
            DrawPreview = (Action<Rect>)type.GetMethod("DrawPreview", flags, null, new[] { typeof(Rect) }, null).CreateDelegate(typeof(Action<Rect>));
            End = (Action)type.GetMethod("End", flags, null, Type.EmptyTypes, null).CreateDelegate(typeof(Action));
        }
    }
    static string NativeInspect(WhimTexColorPicker picker)
        => "owned=" + Get(picker, "eyedropperOwned") + "; pending=" + Get(picker, "eyedropperPending")
            + "; native=" + Call(Get(picker, "eyedropperApi"), "IsOpened")
            + "; hidden=" + Hidden(picker, "eyedropperPreview")
            + "; canDraw=" + Get(picker, "eyedropperApi").GetType().GetProperty("CanDraw").GetValue(Get(picker, "eyedropperApi"))
            + "; trace=" + picker.rootVisualElement.userData;
    static void NativeTrace(WhimTexColorPicker picker, string file, string phase)
        => File.AppendAllText(Path.Combine(Scope.Temp, file), phase + ": " + NativeInspect(picker) + Environment.NewLine);
    static void CaptureNativeFrame(WhimTexColorPicker picker, string file)
    {
        Texture2D image = null;
        try
        {
            var target = RenderTexture.active;
            int width = target != null ? target.width : Mathf.RoundToInt(picker.position.width * EditorGUIUtility.pixelsPerPoint);
            int height = target != null ? target.height : Mathf.RoundToInt(picker.position.height * EditorGUIUtility.pixelsPerPoint);
            image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); image.Apply(false);
            File.WriteAllBytes(Path.Combine(Scope.Temp, file), image.EncodeToPNG());
        }
        finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
    }
    static async Task NativeRepaint(WhimTexColorPicker picker, int captureDelay, string stateFile, Action start, Action capture)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string phase = "awaiting first Repaint";
        bool started = false, repaintExited = false;
        long captureAt = 0;
        Exception frameFailure = null;
        var probe = new IMGUIContainer(() =>
        {
            if (repaintExited || Event.current.type != EventType.Repaint) return;
            bool finished = false;
            var previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var previousColor = GUI.color;
            try
            {
                if (!started)
                {
                    started = true;
                    phase = "native start in Repaint";
                    start();
                    captureAt = clock.ElapsedMilliseconds + captureDelay;
                    phase = "awaiting capture Repaint";
                }
                if (clock.ElapsedMilliseconds < captureAt) return;
                phase = "capture in Repaint";
                capture();
                finished = true;
            }
            catch (Exception error) { frameFailure = error; finished = true; }
            finally
            {
                if (finished)
                {
                    try
                    {
                        phase = "stopping owned native session in GUI finally";
                        Call(picker, "StopEyedropper");
                        NativeTrace(picker, stateFile, "GUI finally stopped");
                    }
                    catch (Exception error)
                    {
                        frameFailure = frameFailure == null ? error : new AggregateException("Native frame and stop failed", frameFailure, error);
                    }
                    finally { phase = "awaiting post-Repaint update"; repaintExited = true; }
                }
                RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb; GUI.color = previousColor;
            }
        }) { pickingMode = PickingMode.Ignore };
        probe.style.position = Position.Absolute;
        probe.style.left = 10; probe.style.top = 44; probe.style.width = 220; probe.style.height = 220;
        EditorApplication.CallbackFunction afterRepaint = () =>
        {
            if (repaintExited)
            {
                if (frameFailure == null) completion.TrySetResult(true);
                else completion.TrySetException(frameFailure);
            }
            else if (picker != null) picker.Repaint();
        };
        var repaintSchedule = picker.rootVisualElement.schedule.Execute(() =>
        {
            if (!repaintExited && picker != null) picker.Repaint();
        }).Every(16);
        Exception failure = null;
        using (var deadline = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
        {
            try
            {
                deadline.CancelAfter(10000);
                using (deadline.Token.Register(() => completion.TrySetCanceled()))
                {
                    picker.rootVisualElement.Add(probe);
                    EditorApplication.update += afterRepaint;
                    picker.Repaint();
                    try { await completion.Task; }
                    catch (OperationCanceledException) when (!Cancellation.IsCancellationRequested)
                    { throw new TimeoutException("Native fixture exceeded 10 seconds: " + phase); }
                }
                NativeTrace(picker, stateFile, "post-Repaint completion");
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                WhimTex.Tests.UnityA.UnityAScope.RunCleanup(failure,
                    () => EditorApplication.update -= afterRepaint,
                    () => repaintSchedule.Pause(),
                    () => probe.RemoveFromHierarchy(),
                    () => { if (picker != null) NativeTrace(picker, stateFile, "joined cleanup, phase=" + phase); },
                    () => { if (picker != null) Call(picker, "StopEyedropper"); });
            }
        }
    }
    static async Task BodyNativePreview()
    {
        Check(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length == 0, "Close the borrowed active picker before this case");
        var bindings = new NativeBindings();
        Check(!bindings.IsOpened(), "Do not borrow an already opened native eyedropper");
        var picker = Open(_ => { });
        picker.name = Scope.Tag + "-eyedropper-preview";
        var eye = ((UnityEditor.UIElements.ColorField)Get(picker, "eyedropper")).Q(className: UnityEditor.UIElements.ColorField.eyeDropperUssClassName);
        EventCallback<PointerDownEvent> trace = e => picker.rootVisualElement.userData = "target callback, native="
            + Call(Get(picker, "eyedropperApi"), "IsOpened") + "; pending=" + Get(picker, "eyedropperPending");
        eye.RegisterCallback(trace);
        Exception failure = null;
        try
        {
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(200, Cancellation);
            await NativeRepaint(picker, 1000, "color-picker-eyedropper-state.txt", () =>
            {
                Intent(picker, true);
                picker.rootVisualElement.userData += "; after send native=" + Call(Get(picker, "eyedropperApi"), "IsOpened")
                    + "; pending=" + Get(picker, "eyedropperPending");
                Call(picker, "ClaimEyedropper");
                NativeTrace(picker, "color-picker-eyedropper-state.txt", "after pointer propagation");
                Check((bool)Get(picker, "eyedropperOwned") && bindings.IsOpened(), "Own native eyedropper started");
                Check(!Hidden(picker, "eyedropperPreview") && Hidden(picker, "hueRing") && Hidden(picker, "plane"), "Native magnifier replaces wheel and square");
            }, () =>
            {
                NativeTrace(picker, "color-picker-eyedropper-state.txt", "before preview assertions");
                bool nativeOpen = bindings.IsOpened();
                Check((bool)Get(picker, "eyedropperOwned") == nativeOpen, "Picker ownership follows independently observed native session at delayed capture");
                if (nativeOpen)
                    Check(!Hidden(picker, "eyedropperPreview") && Hidden(picker, "hueRing") && Hidden(picker, "plane"), "Open native session retains magnifier at delayed capture");
                else
                    Check(Hidden(picker, "eyedropperPreview") && !Hidden(picker, "hueRing") && !Hidden(picker, "plane"), "Ended native session restores wheel and square at delayed capture");
                CaptureNativeFrame(picker, "color-picker-eyedropper.png");
            });
            Check(!bindings.IsOpened(), "Owned native session stopped before fixture completion");
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            WhimTex.Tests.UnityA.UnityAScope.RunCleanup(failure,
                () => eye.UnregisterCallback(trace),
                () => { if (picker != null) Call(picker, "StopEyedropper"); },
                () => { if (picker != null) Call(picker, "Finish", false); });
        }
    }
    static async Task BodyNativeRender()
    {
        Check(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length == 0, "Close the borrowed active picker before this case");
        var bindings = new NativeBindings();
        Check(!bindings.IsOpened(), "Do not borrow an already opened native eyedropper");
        var picker = Open(_ => { });
        picker.name = Scope.Tag + "-eyedropper-render";
        Check(Get(picker, "eyedropperApi") != null, "Native preview adapter binds on this Editor");
        Exception failure = null;
        try
        {
            await NativeRepaint(picker, 0, "color-picker-eyedropper-render-state.txt", () =>
            {
                Intent(picker, true);
                Call(picker, "ClaimEyedropper");
                NativeTrace(picker, "color-picker-eyedropper-render-state.txt", "after pointer propagation");
                Check((bool)Get(picker, "eyedropperOwned"), "Own native eyedropper started");
                Check(bindings.IsOpened(), "Independent native binding observes the owned session");
            }, () =>
            {
                var drawEvent = Event.current;
                var savedType = drawEvent.type;
                try
                {
                    drawEvent.type = EventType.Repaint;
                    Check((bool)Call(Get(picker, "eyedropperApi"), "Draw", new Rect(0, 0, 220, 220)), "Native DrawPreview succeeds in Editor GUI context");
                }
                finally { drawEvent.type = savedType; }
                NativeTrace(picker, "color-picker-eyedropper-render-state.txt", "native Draw passed");
                CaptureNativeFrame(picker, "color-picker-eyedropper-render.png");
            });
            Check(!bindings.IsOpened(), "Owned native session stopped before fixture completion");
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            WhimTex.Tests.UnityA.UnityAScope.RunCleanup(failure,
                () => { if (picker != null) Call(picker, "StopEyedropper"); },
                () => { if (picker != null) Call(picker, "Finish", false); });
        }
    }
public static string StartNativePreview(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyNativePreview(); } finally { T = null; Scope = null; } }));
public static string StartNativeRender(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyNativeRender(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
