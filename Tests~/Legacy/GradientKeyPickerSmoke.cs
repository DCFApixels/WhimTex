using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class GradientKeyPickerSmoke
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string WindowName = "Gradient key picker smoke";
    static WhimTexColorPicker ActivePicker => Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().FirstOrDefault();
    static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static void Refresh(WhimTexGradientWindow window) => typeof(WhimTexGradientWindow).GetMethod("Refresh", Flags).Invoke(window, null);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Pointer(VisualElement strip, Vector2 local, EventType type, int clicks, int button = 0)
    {
        var source = new Event { type = type, mousePosition = strip.LocalToWorld(local), button = button, clickCount = clicks };
        if (type == EventType.MouseDown)
        {
            using (var e = PointerDownEvent.GetPooled(source)) { e.target = strip; strip.SendEvent(e); }
        }
        else
        {
            using (var e = PointerUpEvent.GetPooled(source)) { e.target = strip; strip.SendEvent(e); }
        }
    }
    public static string Setup()
    {
        Check(ActivePicker == null, "Close the existing color picker before running this test.");
        var window = ScriptableObject.CreateInstance<WhimTexGradientWindow>();
        window.name = WindowName;
        window.titleContent = new GUIContent(WindowName);
        window.position = new Rect(100, 100, 560, 420);
        window.ShowUtility();
        return "Temporary test window opened. Run Verify after layout.";
    }
    public static string FocusLifecycle()
    {
        if (ActivePicker != null || Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length != 0)
            return "BLOCKED: close user gradient/color picker windows before running this test.";
        var previousFocus = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<WhimTexGradientWindow>();
        WhimTexColorPicker picker = null;
        void Call(string method) => typeof(WhimTexGradientWindow).GetMethod(method, Flags).Invoke(window, null);
        void PendingFocusCheck() { Set(window, "checkFocus", true); Set(window, "focusCheckAfter", 0d); }
        try
        {
            window.name = "Gradient picker focus test"; window.ShowUtility();
            foreach (bool cancel in new[] { false, true, false })
            {
                Call("OpenKeyColor"); picker = ActivePicker;
                Check(picker != null && ReferenceEquals(Get(window, "keyColorPicker"), picker), "Gradient owns its key picker");
                PendingFocusCheck(); Call("CheckFocus");
                Check(window != null, "Parent survives expired focus check while child is open");
                if (cancel)
                {
                    using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
                    { e.target = picker.rootVisualElement; picker.rootVisualElement.SendEvent(e); }
                }
                else picker.Close();
                Check(picker == null, "Child closed"); picker = null;
                PendingFocusCheck(); Call("CheckFocus");
                Check(window != null && !(bool)Get(window, "checkFocus") && !(bool)Get(window, "waitingForColorPicker"),
                    "Closing/canceling child clears pending parent dismissal");
                Check(EditorWindow.focusedWindow == window, "Parent regains focus");
                Call("CheckFocus"); Check(window != null, "Parent survives subsequent update");
            }
            picker = (WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", Flags).Invoke(null,
                new object[] { Color.red, false, true, WhimTexColorRange.Switchable, null, (Action<Color>)(_ => { }), null, null });
            picker.Focus(); PendingFocusCheck(); Call("CheckFocus");
            Check(window == null, "Unrelated picker does not suppress ordinary parent dismissal");
            return "PASS: close/Escape/reopen, expired focus checks, parent focus restoration and unrelated-picker isolation.";
        }
        finally
        {
            if (picker != null) picker.Close();
            if (window != null) { Undo.ClearUndo(window); window.Close(); }
            if (previousFocus != null) previousFocus.Focus();
        }
    }
    public static string Verify()
    {
        var window = Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Single(w => w.name == WindowName);
        try
        {
            var strip = window.rootVisualElement.Q("gradientStrip");
            Check(strip.contentRect.width > 100 && strip.contentRect.height > 44, "Window layout is ready");
            var bottom = new Vector2(10, strip.contentRect.height - 10);
            Pointer(strip, bottom, EventType.MouseDown, 1);
            Check(ActivePicker == null, "Single click must only select");
            Pointer(strip, bottom, EventType.MouseUp, 1);
            foreach (var space in new[] { ColorSpace.Gamma, ColorSpace.Linear })
            foreach (bool hdr in new[] { false, true })
            {
                var initial = new Color(.6f, .3f, .1f, .7f) * (hdr ? 3 : 1);
                var gradient = new WhimTexGradient { ColorSpace = space };
                gradient.SetKeys(new[] { new GradientColorKey(space == ColorSpace.Linear ? initial.linear : initial, 0),
                    new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                Set(window, "gradient", gradient); Set(window, "alphaTrack", false); Set(window, "selected", 1);
                ((System.Collections.IDictionary)Get(window, "hdrPreferences")).Clear();
                ((System.Collections.IDictionary)Get(window, "intensityPreferences")).Clear();
                Refresh(window);
                string before = JsonUtility.ToJson(gradient);
                Pointer(strip, bottom, EventType.MouseDown, 2);
                var picker = ActivePicker;
                Check(picker != null, "Double click must open WhimTex picker");
                Check((int)Get(window, "selected") == 0, "Clicked key selected before opening");
                Check(before == JsonUtility.ToJson(gradient), "Opening must not modify gradient");
                Check(!strip.HasPointerCapture(PointerId.mousePointerId), "Double click must not start dragging");
                Check((bool)Get(picker, "hdr") == hdr, "Picker respects HDR mode");
                void Change(Color c) => typeof(WhimTexColorPicker).GetMethod("SetColor", Flags).Invoke(picker, new object[] { c, true });
                var value = new Color(.2f, .4f, .6f, 1) * (hdr ? 4 : 1);
                Change(value);
                var expected = space == ColorSpace.Linear ? value.linear : value;
                var actual = gradient.ColorKeys[0].color;
                Check(Mathf.Abs(actual.r - expected.r) < .0001f && Mathf.Abs(actual.g - expected.g) < .0001f &&
                    Mathf.Abs(actual.b - expected.b) < .0001f, "Picker updates correct color space");
                Check(actual.a == (space == ColorSpace.Linear ? initial.linear : initial).a, "Color picker preserves key alpha");
                Set(window, "selected", 1); Refresh(window);
                string unchanged = JsonUtility.ToJson(gradient);
                Change(Color.magenta);
                Check(unchanged == JsonUtility.ToJson(gradient), "Stale callback cannot change another key");
                picker.Close(); window.Focus();
            }
            Pointer(strip, new Vector2(10, 10), EventType.MouseDown, 2);
            Check(ActivePicker == null, "Alpha marker must not open color picker");
            Pointer(strip, new Vector2(10, 10), EventType.MouseUp, 2);
            var midpoint = new Vector2(strip.contentRect.width / 2, strip.contentRect.height - 10);
            Pointer(strip, midpoint, EventType.MouseDown, 2);
            Check(ActivePicker == null && (bool)Get(window, "midpointSelected"), "Midpoint must not open color picker");
            Pointer(strip, midpoint, EventType.MouseUp, 2);
            return "PASS: WhimTex picker, selection, no mutation/capture on opening, Gamma/Linear, SDR/HDR, alpha preservation, stale callbacks, alpha and midpoint exclusions.";
        }
        finally
        {
            ActivePicker?.Close();
            Undo.ClearUndo(window);
            window.Close();
        }
    }
}
