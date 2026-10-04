using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerEyedropperSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static object Get(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
    public static class FakeApi
    {
        public static bool IsOpened { get; set; }
        public static int Ends, Draws;
        public static void End() { Ends++; IsOpened = false; }
        public static void DrawPreview(Rect rect) { Draws++; }
    }
    static WhimTexColorPicker Open(Action<Color> change)
        => (WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
            new object[] { Color.red, false, true, WhimTexColorRange.Switchable, null, change, null, null });
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
    public static string Main()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0) return "BLOCKED: close the active color picker first.";
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
            return "PASS: " + checks + " eyedropper checks; native binding plus simulated lifecycle, ownership and fallback.";
        }
        finally { if (picker != null) Call(picker, "Finish", false); FakeApi.IsOpened = false; if (focus != null) focus.Focus(); }
    }

    public static string NativeSetup()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0) return "BLOCKED: close the active color picker first.";
        var picker = Open(_ => { });
        picker.name = "Eyedropper preview test";
        var eye = ((UnityEditor.UIElements.ColorField)Get(picker, "eyedropper")).Q(className: UnityEditor.UIElements.ColorField.eyeDropperUssClassName);
        eye.RegisterCallback<PointerDownEvent>(e => picker.rootVisualElement.userData = "target callback, native=" + Call(Get(picker, "eyedropperApi"), "IsOpened") + "; pending=" + Get(picker, "eyedropperPending"));
        picker.rootVisualElement.schedule.Execute(() =>
        {
            if (picker == null) return;
            bool captured = false;
            var capture = new IMGUIContainer(() =>
            {
                if (captured || Event.current.type != EventType.Repaint) return;
                captured = true;
                Texture2D image = null;
                try
                {
                    var target = RenderTexture.active;
                    int width = target != null ? target.width : Mathf.RoundToInt(picker.position.width * EditorGUIUtility.pixelsPerPoint);
                    int height = target != null ? target.height : Mathf.RoundToInt(picker.position.height * EditorGUIUtility.pixelsPerPoint);
                    image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    image.Apply(false);
                    Directory.CreateDirectory("Temp/WhimTex");
                    File.WriteAllBytes("Temp/WhimTex/color-picker-eyedropper.png", image.EncodeToPNG());
                }
                finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
            }) { pickingMode = PickingMode.Ignore };
            capture.style.position = Position.Absolute;
            capture.style.width = 1; capture.style.height = 1;
            picker.rootVisualElement.Add(capture);
            picker.Repaint();
        }).StartingIn(1200);
        picker.rootVisualElement.schedule.Execute(() =>
        {
            if (picker == null) return;
            try { Intent(picker, true); picker.rootVisualElement.userData = picker.rootVisualElement.userData + "; after send native=" + Call(Get(picker, "eyedropperApi"), "IsOpened") + "; pending=" + Get(picker, "eyedropperPending"); }
            catch { Call(picker, "Finish", false); throw; }
        }).StartingIn(200);
        double end = EditorApplication.timeSinceStartup + 15;
        void Cleanup()
        {
            if (picker != null && EditorApplication.timeSinceStartup < end) return;
            EditorApplication.update -= Cleanup;
            if (picker != null) Call(picker, "Finish", false);
        }
        EditorApplication.update += Cleanup;
        return "Temporary native preview test started; automatic cleanup in 15 seconds.";
    }
    public static string NativeInspect()
    {
        foreach (var picker in Resources.FindObjectsOfTypeAll<WhimTexColorPicker>())
            if (picker.name == "Eyedropper preview test")
                return "owned=" + Get(picker, "eyedropperOwned") + "; pending=" + Get(picker, "eyedropperPending") + "; native=" + Call(Get(picker, "eyedropperApi"), "IsOpened") + "; hidden=" + Hidden(picker, "eyedropperPreview") + "; canDraw=" + Get(picker, "eyedropperApi").GetType().GetProperty("CanDraw").GetValue(Get(picker, "eyedropperApi")) + "; trace=" + picker.rootVisualElement.userData;
        return "Temporary preview test closed.";
    }

    public static string NativeRender()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0) return "BLOCKED: close the active color picker first.";
        var focus = EditorWindow.focusedWindow;
        var picker = Open(_ => { });
        picker.name = "Eyedropper render test";
        bool rendered = false;
        var probe = new IMGUIContainer(() =>
        {
            if (rendered || Event.current.type != EventType.Repaint) return;
            rendered = true;
            Texture2D image = null;
            try
            {
                Intent(picker, true);
                Call(picker, "ClaimEyedropper");
                Check((bool)Get(picker, "eyedropperOwned"), "Own native eyedropper started");
                var drawEvent = Event.current;
                var savedType = drawEvent.type;
                try
                {
                    drawEvent.type = EventType.Repaint;
                    Check((bool)Call(Get(picker, "eyedropperApi"), "Draw", new Rect(0, 0, 220, 220)), "Native DrawPreview succeeds in Editor GUI context");
                }
                finally { drawEvent.type = savedType; }
                var target = RenderTexture.active;
                int width = target != null ? target.width : Mathf.RoundToInt(picker.position.width * EditorGUIUtility.pixelsPerPoint);
                int height = target != null ? target.height : Mathf.RoundToInt(picker.position.height * EditorGUIUtility.pixelsPerPoint);
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); image.Apply(false);
                Directory.CreateDirectory("Temp/WhimTex");
                File.WriteAllBytes("Temp/WhimTex/color-picker-eyedropper-render.png", image.EncodeToPNG());
                Debug.Log("WhimTex native eyedropper render: PASS");
            }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                EditorApplication.delayCall += () => { if (picker != null) Call(picker, "Finish", false); if (focus != null) focus.Focus(); };
                Call(picker, "StopEyedropper");
            }
        }) { pickingMode = PickingMode.Ignore };
        probe.style.position = Position.Absolute;
        probe.style.left = 10; probe.style.top = 44; probe.style.width = 220; probe.style.height = 220;
        picker.rootVisualElement.Add(probe);
        picker.Repaint();
        return "Queued one-frame native drawing test, with automatic cleanup.";
    }
    public static string NativeCleanup()
    {
        int closed = 0;
        foreach (var picker in Resources.FindObjectsOfTypeAll<WhimTexColorPicker>())
            if (picker.name == "Eyedropper render test" || picker.name == "Eyedropper preview test") { Call(picker, "Finish", false); closed++; }
        return "Closed " + closed + " temporary eyedropper test windows.";
    }
}
