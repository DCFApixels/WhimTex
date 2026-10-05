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
    // Opt-in manual ability, independent of the three existing modes. Diagnostic guards
    // below prove fixture/inspection/capture/cleanup only, never an original image oracle.
    static string InspectionKey(string runId)
    {
        if (!Guid.TryParseExact(runId, "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("Expected nonempty per-run D-format GUID", nameof(runId));
        return "WhimTex.Tests.UnityA.NativeInspection." + id.ToString("N");
    }
    static class InspectionJson
    {
        // Select the public token library explicitly: no compile-time ambiguous types,
        // no Unity internals, and no ephemeral DTO serialization.
        static readonly Assembly Library = Assembly.Load("Newtonsoft.Json");
        static readonly Type ObjectType = Library.GetType("Newtonsoft.Json.Linq.JObject", true);
        static readonly Type ArrayType = Library.GetType("Newtonsoft.Json.Linq.JArray", true);
        static readonly Type ValueType = Library.GetType("Newtonsoft.Json.Linq.JValue", true);
        static readonly PropertyInfo Item = ObjectType.GetProperty("Item", new[] { typeof(string) });
        static readonly PropertyInfo Scalar = ValueType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        static readonly ConstructorInfo ScalarConstructor = ValueType.GetConstructor(new[] { typeof(object) });
        public static string Object(params object[] pairs)
        {
            object result = Activator.CreateInstance(ObjectType);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                object value;
                if (pairs[i + 1] is string[] texts)
                {
                    value = Activator.CreateInstance(ArrayType);
                    var add = ArrayType.GetMethod("Add", new[] { typeof(object) });
                    foreach (string text in texts)
                        add.Invoke(value, new[] { ScalarConstructor.Invoke(new object[] { text }) });
                }
                else value = ScalarConstructor.Invoke(new[] { pairs[i + 1] });
                Item.SetValue(result, value, new[] { pairs[i] });
            }
            return result.ToString();
        }
        public static object Parse(string json) => ObjectType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
        static object Value(object document, string key)
        {
            object token = Item.GetValue(document, new object[] { key });
            if (token == null) throw new InvalidOperationException("Inspection JSON field missing: " + key);
            return Scalar.GetValue(token);
        }
        public static string GetString(object document, string key) => (string)Value(document, key);
        public static bool GetBool(object document, string key) => Convert.ToBoolean(Value(document, key), System.Globalization.CultureInfo.InvariantCulture);
        public static int GetInt(object document, string key) => Convert.ToInt32(Value(document, key), System.Globalization.CultureInfo.InvariantCulture);
        public static string WithObservations(string result, string first, string second, string callerAssembly)
        {
            object document = Parse(result);
            object facts = Parse(Object("callerAssembly", callerAssembly,
                "proof", "Actual repeated getter observations from this Poll assembly; no behavior/visual equivalence verdict."));
            Item.SetValue(facts, Parse(first), new object[] { "inspectionFirst" });
            Item.SetValue(facts, Parse(second), new object[] { "inspectionSecond" });
            Item.SetValue(document, facts, new object[] { "diagnosticFacts" });
            return document.ToString();
        }
    }
    sealed class NativeInspectionSession
    {
        readonly string runId;
        readonly WhimTex.Tests.TestContext context;
        readonly WhimTex.Tests.UnityA.UnityAScope scope;
        readonly NativeBindings bindings;
        readonly System.Threading.CancellationToken cancellation;
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        readonly TaskCompletionSource<bool> finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        WhimTexColorPicker picker;
        VisualElement eye;
        IMGUIContainer probe;
        IVisualElementScheduledItem repaint;
        EventCallback<PointerDownEvent> trace;
        EditorApplication.CallbackFunction update;
        bool pointerAttached, updateAttached, probeAttached, scheduleAttached, disposed, ending, sent, captured, retained;
        int inspections, callbackInvocations;
        long pointerAtMs = -1, captureAtMs = -1;
        string phase = "setup", captureState, capturedHash;
        string workingPath, artifactPath;
        public NativeInspectionSession(string id, WhimTex.Tests.TestContext test,
            WhimTex.Tests.UnityA.UnityAScope owner, NativeBindings native, System.Threading.CancellationToken token)
        { runId = id; context = test; scope = owner; bindings = native; cancellation = token; }
        int ActiveCallbacks => (pointerAttached ? 1 : 0) + (updateAttached ? 1 : 0)
            + (probeAttached ? 1 : 0) + (scheduleAttached ? 1 : 0);
        public string Inspect()
        {
            inspections++;
            return InspectionJson.Object(
                "status", disposed ? "closed" : "running", "runId", runId, "scopeGuid", scope.Tag,
                "phase", phase, "elapsedMs", clock.ElapsedMilliseconds, "pointerAtMs", pointerAtMs, "captureAtMs", captureAtMs,
                "inspections", inspections, "activeCallbacks", ActiveCallbacks, "callbackInvocations", callbackInvocations,
                "windowAlive", picker != null, "state", picker != null ? NativeInspect(picker) : "Temporary preview test closed.",
                "captured", captured, "retained", retained, "artifactPath", artifactPath, "captureState", captureState,
                "message", "Diagnostic state only; no original image/lifetime oracle.");
        }
        void VerifyRepeatedInspection()
        {
            var first = InspectionJson.Parse(InspectNativeSession(runId));
            var second = InspectionJson.Parse(InspectNativeSession(runId));
            context.True(InspectionJson.GetString(first, "runId") == runId && InspectionJson.GetString(second, "runId") == runId && InspectionJson.GetString(second, "scopeGuid") == scope.Tag,
                "Public repeated inspection resolves this run's BCL delegate and owned scope");
            context.True(InspectionJson.GetBool(first, "windowAlive") && InspectionJson.GetBool(second, "windowAlive") && InspectionJson.GetInt(second, "inspections") == InspectionJson.GetInt(first, "inspections") + 1,
                "Public inspection is repeatable while the owned fixture is alive");
            context.True(InspectionJson.GetString(first, "state") == InspectionJson.GetString(second, "state") && InspectionJson.GetInt(first, "activeCallbacks") == InspectionJson.GetInt(second, "activeCallbacks"),
                "Repeated inspection does not stop/restart the native session or change subscriptions");
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        void Capture()
        {
            captureState = NativeInspect(picker);
            Texture2D image = null;
            try
            {
                var target = RenderTexture.active;
                int width = target != null ? target.width : Mathf.RoundToInt(picker.position.width * EditorGUIUtility.pixelsPerPoint);
                int height = target != null ? target.height : Mathf.RoundToInt(picker.position.height * EditorGUIUtility.pixelsPerPoint);
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); image.Apply(false);
                byte[] bytes = image.EncodeToPNG();
                File.WriteAllBytes(workingPath, bytes);
                capturedHash = Hash(bytes);
                context.True(bytes.Length > 8 && Hash(File.ReadAllBytes(workingPath)) == capturedHash,
                    "1200ms full-frame PNG was actually written, not an availability-only capture");
                captured = true; captureAtMs = clock.ElapsedMilliseconds;
                phase = "live inspection after capture; waiting for 15s automatic cleanup";
                Log("capture; " + captureState);
                VerifyRepeatedInspection();
            }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
        void Log(string message) => File.AppendAllText(Path.Combine(scope.Temp, "color-picker-eyedropper-inspection-state.txt"),
            clock.ElapsedMilliseconds + "ms " + message + Environment.NewLine);
        void EndFromPump(Exception failure = null)
        {
            if (ending) return;
            ending = true;
            phase = cancellation.IsCancellationRequested ? "cancel requested" : failure != null ? "fixture failure" : "15s automatic cleanup";
            // Only the package-owned pending/claimed session is stopped. Never End a foreign session.
            try { if (picker != null) Call(picker, "StopEyedropper"); }
            catch (Exception cleanup) { failure = failure == null ? cleanup : new AggregateException(failure, cleanup); }
            if (failure != null) finished.TrySetException(failure);
            else if (cancellation.IsCancellationRequested) finished.TrySetCanceled();
            else finished.TrySetResult(true);
        }
        bool EndIfDue()
        {
            if (disposed || ending) return true;
            if (!cancellation.IsCancellationRequested && clock.ElapsedMilliseconds < 15000) return false;
            EndFromPump(); return true;
        }
        void OnFrame()
        {
            callbackInvocations++;
            if (Event.current.type != EventType.Repaint || EndIfDue()) return;
            var previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var previousColor = GUI.color;
            try
            {
                if (!sent && clock.ElapsedMilliseconds >= 200)
                {
                    sent = true; pointerAtMs = clock.ElapsedMilliseconds;
                    Intent(picker, true);
                    picker.rootVisualElement.userData += "; after send native=" + bindings.IsOpened()
                        + "; pending=" + Get(picker, "eyedropperPending");
                    Call(picker, "ClaimEyedropper");
                    phase = "live native inspection before capture";
                    Log("after pointer propagation; " + NativeInspect(picker));
                    context.True((bool)Get(picker, "eyedropperOwned") && bindings.IsOpened(),
                        "Manual fixture starts a real owned native session (supplemental diagnostic guard)");
                }
                if (!captured && sent && clock.ElapsedMilliseconds >= 1200) Capture();
            }
            catch (Exception error) { EndFromPump(error); }
            finally { RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb; GUI.color = previousColor; }
        }
        public async Task Run()
        {
            Exception failure = null;
            try
            {
                picker = scope.OwnWindow((WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
                    new object[] { Color.red, false, true, WhimTexColorRange.Switchable, null, (Action<Color>)(_ => { }), null, null }));
                picker.name = scope.Tag + "-eyedropper-inspection-" + runId;
                workingPath = Path.Combine(scope.Temp, "color-picker-eyedropper-inspection.png");
                artifactPath = Path.GetFullPath("Temp/WhimTex/tests-unity-a-artifacts/" + scope.Tag + "/color-picker-eyedropper-inspection.png");
                eye = ((UnityEditor.UIElements.ColorField)Get(picker, "eyedropper")).Q(className: UnityEditor.UIElements.ColorField.eyeDropperUssClassName);
                clock.Start();
                trace = e => { callbackInvocations++; if (!disposed) picker.rootVisualElement.userData =
                    "target callback, native=" + bindings.IsOpened() + "; pending=" + Get(picker, "eyedropperPending"); };
                pointerAttached = true; eye.RegisterCallback(trace);
                probe = new IMGUIContainer(OnFrame) { pickingMode = PickingMode.Ignore };
                probe.style.position = Position.Absolute; probe.style.width = 1; probe.style.height = 1;
                probeAttached = true; picker.rootVisualElement.Add(probe);
                update = () =>
                {
                    callbackInvocations++;
                    try { if (!EndIfDue() && picker != null) picker.Repaint(); }
                    catch (Exception error) { EndFromPump(error); }
                };
                updateAttached = true; EditorApplication.update += update;
                repaint = picker.rootVisualElement.schedule.Execute(() =>
                {
                    callbackInvocations++;
                    try { if (!EndIfDue() && picker != null) picker.Repaint(); }
                    catch (Exception error) { EndFromPump(error); }
                });
                scheduleAttached = true; repaint.Every(16);
                VerifyRepeatedInspection();
                using (var deadline = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    deadline.CancelAfter(20000);
                    using (deadline.Token.Register(() =>
                    {
                        // Timer thread performs no Unity access; GUI pump/finally owns all teardown.
                        if (cancellation.IsCancellationRequested) finished.TrySetCanceled();
                        else finished.TrySetException(new TimeoutException("15s manual inspection did not join within 20s"));
                    }))
                    {
                        picker.Repaint(); await finished.Task;
                    }
                }
                context.True(clock.ElapsedMilliseconds >= 15000 && picker != null,
                    "Normal completion keeps the manual fixture alive for the full 15 seconds");
                context.True(sent && captured, "Original 200ms pointer and 1200ms actual capture both executed");
                VerifyRepeatedInspection();
            }
            catch (Exception error) { failure = error; throw; }
            finally { WhimTex.Tests.UnityA.UnityAScope.RunCleanup(failure, Dispose); }
        }
        public void Dispose()
        {
            if (disposed && ActiveCallbacks == 0 && picker == null) return;
            disposed = true;
            WhimTex.Tests.UnityA.UnityAScope.RunCleanup(null,
                () => { if (updateAttached) { EditorApplication.update -= update; updateAttached = false; } },
                () => { if (scheduleAttached) { repaint.Pause(); scheduleAttached = false; }
                    context.True(repaint == null || !repaint.isActive, "Owned repaint scheduler paused"); },
                () => { if (probeAttached) { probe.RemoveFromHierarchy(); probeAttached = false; }
                    context.True(probe == null || probe.parent == null, "Owned IMGUI capture probe detached"); },
                () => { if (pointerAttached) { eye.UnregisterCallback(trace); pointerAttached = false; } },
                () => { if (picker != null) Call(picker, "StopEyedropper"); },
                () => { if (picker != null) Call(picker, "Finish", false); },
                () => { context.True(ActiveCallbacks == 0, "Every owned callback registration detached"); },
                () => { phase = "closed; diagnostic artifacts preserved by scope cleanup"; });
        }
        public async Task VerifyDetachedCallbacks()
        {
            int detachedCount = callbackInvocations;
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(50, System.Threading.CancellationToken.None);
            context.True(disposed && picker == null && ActiveCallbacks == 0 && callbackInvocations == detachedCount,
                "Owned window and all callback registrations are gone; no callbacks ran after joined scope cleanup");
        }
        public void VerifyRetainedCapture()
        {
            if (!captured) return; // Cancellation before capture is not fabricated capture success.
            context.True(File.Exists(artifactPath) && Hash(File.ReadAllBytes(artifactPath)) == capturedHash,
                "Scope retained the actual captured PNG byte-for-byte after working-folder cleanup");
            retained = true;
        }
    }
    public static string InspectNativeSession(string runId)
    {
        var bridge = AppDomain.CurrentDomain.GetData(InspectionKey(runId)) as object[];
        return bridge != null && bridge[0] is Func<string> inspect ? inspect() :
            InspectionJson.Object("status", "missing", "runId", runId,
                "windowAlive", false, "phase", "No owned native inspection session; no native state borrowed.");
    }
    public static string StartNativeInspection(string runId)
    {
        string key = InspectionKey(runId);
        if (AppDomain.CurrentDomain.GetData(key) != null)
            return WhimTex.Tests.TestContext.Result("failed", 0, "Duplicate manual inspection GUID").ToJson();
        // BCL-only getter and joined operation tasks survive fresh Pipeline assemblies.
        var bridge = new object[] { null, null, null };
        AppDomain.CurrentDomain.SetData(key, bridge);
        bool entered = false;
        try
        {
            string result = WhimTex.Tests.UnityA.UnityAAsync.Start(runId, async (context, cancellation) =>
            {
                entered = true;
                NativeInspectionSession session = null;
                Exception failure = null;
                try
                {
                    await WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope =>
                    {
                        context.True(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length == 0, "Close borrowed picker before opt-in inspection");
                        var bindings = new NativeBindings();
                        context.True(!bindings.IsOpened(), "Do not borrow an existing native eyedropper session");
                        session = new NativeInspectionSession(runId, context, scope, bindings, cancellation);
                        scope.Finally(session.Dispose);
                        bridge[0] = (Func<string>)session.Inspect;
                        await session.Run();
                    });
                }
                catch (Exception error) { failure = error; }
                Exception callbackFailure = null;
                try { if (session != null) await session.VerifyDetachedCallbacks(); }
                catch (Exception error) { callbackFailure = error; }
                WhimTex.Tests.UnityA.UnityAScope.RunCleanup(failure,
                    () => { if (callbackFailure != null) throw callbackFailure; },
                    () => { if (session != null) session.VerifyRetainedCapture(); });
                if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            });
            if (!entered) AppDomain.CurrentDomain.SetData(key, null);
            var start = JsonUtility.FromJson<WhimTex.Tests.TestResult>(result);
            start.message = "Opt-in native inspection diagnostic: " + start.message + "; not original behavior or visual equivalence.";
            return start.ToJson();
        }
        catch { if (!entered) AppDomain.CurrentDomain.SetData(key, null); throw; }
    }
    public static string PollNativeInspection(string runId)
    {
        if (!(AppDomain.CurrentDomain.GetData(InspectionKey(runId)) is object[]))
            return WhimTex.Tests.TestContext.Result("failed", 0, "No owned native inspection GUID; no other mode polled").ToJson();
        var result = JsonUtility.FromJson<WhimTex.Tests.TestResult>(WhimTex.Tests.UnityA.UnityAAsync.Poll(runId));
        result.message = "Opt-in native inspection diagnostic: " + result.message
            + "; fixture guards/completion are not original behavior or visual equivalence.";
        string first = InspectNativeSession(runId);
        string second = InspectNativeSession(runId);
        return InspectionJson.WithObservations(result.ToJson(), first, second, typeof(ColorPickerEyedropperTests).Assembly.FullName);
    }
    static string InspectionRecovery(string message, string failure) => InspectionJson.Object(
        "status", "failed", "checks", 0, "message", message,
        "failures", new[] { failure }, "recoveryRequired", true);
    static async Task<string> JoinInspection(Task<string> operation, string phase)
    {
        using (var waitBudget = new System.Threading.CancellationTokenSource())
        {
            try
            {
                if (await Task.WhenAny(operation, Task.Delay(10000, waitBudget.Token)) == operation) return await operation;
                return InspectionRecovery(phase + " did not join within 10s; retain exact GUID/carrier and inspect Editor work",
                    "Owned cleanup completion is unproven; the outstanding operation was not abandoned or replayed.");
            }
            finally { waitBudget.Cancel(); }
        }
    }
    public static Task<string> CancelNativeInspection(string runId)
    {
        var bridge = AppDomain.CurrentDomain.GetData(InspectionKey(runId)) as object[];
        if (bridge == null)
            return Task.FromResult(WhimTex.Tests.TestContext.Result("failed", 0, "No owned native inspection GUID; no other mode cancelled").ToJson());
        if (bridge[2] is Task<string>)
            return Task.FromResult(InspectionRecovery("Cleanup already requested; join CleanupNativeInspection, do not replay cancellation",
                "Exact owned cleanup result must be joined before any new operation."));
        var pending = bridge[1] as Task<string>;
        if (pending == null) bridge[1] = pending = WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
        return JoinInspection(pending, "Native inspection cancellation");
    }
    static async Task<string> CleanupInspectionCarrier(string runId, object[] bridge)
    {
        if (bridge[1] is Task<string> cancel) await cancel;
        return await WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
    }
    public static async Task<string> CleanupNativeInspection(string runId)
    {
        string key = InspectionKey(runId);
        var bridge = AppDomain.CurrentDomain.GetData(key) as object[];
        if (bridge == null)
            return WhimTex.Tests.TestContext.Result("failed", 0, "No owned native inspection GUID; no other mode cleaned").ToJson();
        var pending = bridge[2] as Task<string>;
        if (pending == null) bridge[2] = pending = CleanupInspectionCarrier(runId, bridge);
        string result = await JoinInspection(pending, "Native inspection cleanup");
        if (JsonUtility.FromJson<WhimTex.Tests.TestResult>(result).status == "passed"
            && ReferenceEquals(AppDomain.CurrentDomain.GetData(key), bridge)) AppDomain.CurrentDomain.SetData(key, null);
        return result;
    }
public static string StartNativePreview(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyNativePreview(); } finally { T = null; Scope = null; } }));
public static string StartNativeRender(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyNativeRender(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
