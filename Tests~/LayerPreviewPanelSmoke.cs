using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class LayerPreviewPanelSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Key = "WhimTex.LayerPreviewPanelSmoke";
    static FieldInfo Field(object obj, string name)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, F); if (field != null) return field; }
        throw new Exception("Missing field: " + name);
    }
    static object Get(object obj, string name) => Field(obj, name).GetValue(obj);
    static void Set(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static Color[] Read(RenderTexture texture)
    {
        var previous = RenderTexture.active;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = texture; copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); return copy.GetPixels(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }
    static void Drag(VisualElement panel, float rise)
    {
        var header = panel.Q("layer-preview-resizer");
        var start = header.worldBound.position + new Vector2(30, 10);
        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start }))
        { e.target = header; header.SendEvent(e); }
        using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = start - Vector2.up * rise }))
        { e.target = header; header.SendEvent(e); }
        using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = start - Vector2.up * rise }))
        { e.target = header; header.SendEvent(e); }
    }
    public static string Result() => SessionState.GetString(Key, "Not started");
    static void Capture(EditorWindow window, string name)
    {
        var rect = window.position;
        var image = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);
        try
        {
            image.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position, image.width, image.height)); image.Apply();
            string folder = System.IO.Path.GetFullPath("Temp/WhimTex/LayerPreviewPanelSmoke");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    public static string Start()
    {
        int checks = 0, step = 0;
        void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
        void CheckFlush(ScrollView scroll, VisualElement preview)
        {
            float gap = preview.worldBound.yMin - scroll.contentViewport.worldBound.yMax;
            Check(Math.Abs(gap) < .5f, "Settings clipping reaches Preview strip; gap=" + gap);
        }
        AssetDatabase.ImportAsset("Packages/com.dcfapixels.whimtex/src/WhimTexSplitView.uss", ImportAssetOptions.ForceUpdate);
        var focus = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        var doc = (TextureCompositor)Get(window, "compositor");
        var properties = ScriptableObject.CreateInstance<FileLayerEditorWindow>();
        var tex = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
        var pixels = Enumerable.Range(0, 16).Select(i => new Color(.1f + i * .035f, .25f, .6f - i * .025f, .1f + i * .05f)).ToArray();
        tex.SetPixels(pixels); tex.Apply();
        doc.layers.Clear(); doc.width = 512; doc.height = 256;
        Layer a = new FileLayerBehaviour { sourceTexture = tex, colorRange = LayerColorRange.HDR };
        Layer b = new ColorFillLayerBehaviour { color = new Color(.15f, .7f, .4f, .35f) };
        doc.layers.Add(a); doc.layers.Add(b); Call(doc, "NormalizeModel");
        Set(window, "selectedLayerId", a.Id);
        VisualElement embedded = null, standalone = null;
        object state = null;
        RenderTexture last = null;
        float remembered = 0;
        void Cleanup()
        {
            properties.Close(); window.DiscardChanges(); window.Close();
            if (doc != null) UnityEngine.Object.DestroyImmediate(doc);
            UnityEngine.Object.DestroyImmediate(tex);
            if (focus != null) focus.Focus();
        }
        void Compare(VisualElement panel, Layer layer)
        {
            var actual = (RenderTexture)Get(panel, "source");
            Check(actual != null, "Rendered source");
            var expected = (RenderTexture)Call(doc, "RenderLayerPreview", layer, 256);
            try
            {
                Check(actual.width == expected.width && actual.height == expected.height, "Preview dimensions");
                var left = Read(actual); var right = Read(expected);
                for (int i = 0; i < left.Length; i++) for (int c = 0; c < 4; c++)
                    Check(Math.Abs(left[i][c] - right[i][c]) < .002f, "Properties render equivalence");
            }
            finally { RenderTexture.ReleaseTemporary(expected); }
        }
        void Later() => window.rootVisualElement.schedule.Execute(Run).StartingIn(500);
        void Run()
        {
            try
            {
                switch (step++)
                {
                    case 0:
                        embedded = window.rootVisualElement.Q("layer-preview"); standalone = properties.rootVisualElement.Q("layer-preview");
                        Check(embedded != null && standalone != null, "Both owners use panel");
                        state = Get(embedded, "state");
                        Check((bool)Get(state, "collapsed"), "Embedded starts collapsed");
                        Check(Math.Abs(embedded.resolvedStyle.height - 20) < 1, "Collapsed strip height: " + embedded.resolvedStyle.height);
                        Check(Get(embedded, "source") == null, "Collapsed does not render");
                        Check(!(bool)Get(Get(standalone, "state"), "collapsed"), "Properties starts expanded");
                        Check(standalone.parent == properties.rootVisualElement, "Properties footer outside scroll");
                        Check(embedded.parent == window.rootVisualElement.Q<ScrollView>("selected-layer-settings").parent, "Embedded footer outside scroll");
                        CheckFlush(window.rootVisualElement.Q<ScrollView>("selected-layer-settings"), embedded);
                        CheckFlush(properties.rootVisualElement.Q<ScrollView>(), standalone);
                        Check(!properties.rootVisualElement.Query<Button>().ToList().Any(x => x.text == "Close"), "No Close button");
                        foreach (var p in new[] { embedded, standalone })
                        {
                            Check(p.Query<PopupField<string>>().ToList().Count == 1, "Channel dropdown only");
                            Check(p.Query<PopupField<int>>().ToList().Count == 0, "No mip dropdown");
                            Check(p.Q("layer-preview-surface").Query<Label>().ToList().Count == 0, "No overlay text");
                        }
                        Compare(standalone, a);
                        Check(Math.Abs(standalone.resolvedStyle.height - 220) < 1, "Landscape retains preferred panel height");
                        Drag(embedded, 1000); Later(); break;
                    case 1:
                        CheckFlush(window.rootVisualElement.Q<ScrollView>("selected-layer-settings"), embedded);
                        Capture(window, "layer-settings"); Capture(properties, "properties");
                        Check(!(bool)Get(state, "collapsed"), "Drag opens embedded");
                        Check(embedded.resolvedStyle.height <= 256.1f, "Drag capped at 256 independent of texture size");
                        Compare(embedded, a);
                        var source = (RenderTexture)Get(embedded, "source");
                        var before = Read(source);
                        var popup = embedded.Q<PopupField<string>>();
                        var savedActive = RenderTexture.active; bool savedSrgb = GL.sRGBWrite;
                        foreach (string value in new[] { "RGB", "Alpha", "RGBA" })
                        {
                            popup.value = value;
                            Check(RenderTexture.active == savedActive && GL.sRGBWrite == savedSrgb, "Channel render state restored");
                            var after = Read((RenderTexture)embedded.Q<Image>().image);
                            for (int i = 0; i < after.Length; i++) for (int c = 0; c < 4; c++)
                            {
                                float expected = value == "RGBA" ? before[i][c] : c == 3 ? 1 : value == "Alpha" ? before[i].a : before[i][c];
                                Check(Math.Abs(after[i][c] - expected) < .003f, "Channel pixels " + value);
                            }
                            Check(ReferenceEquals(Get(embedded, "source"), source), "Channel switch does not rerender layer");
                        }
                        last = source;
                        Call(embedded, "Tick"); Check(ReferenceEquals(Get(embedded, "source"), last), "Clean preview reused");
                        // Dropdown target must not start a resize, even when its event reaches the header.
                        var header = embedded.Q("layer-preview-resizer");
                        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = popup.worldBound.center }))
                        { e.target = popup; Call(Get(embedded, "resize"), "Down", e); }
                        Check(!header.HasPointerCapture(PointerId.mousePointerId), "Dropdown does not capture resize");
                        Drag(embedded, -1000); Later(); break;
                    case 2:
                        Check((bool)Get(state, "collapsed") && Get(embedded, "source") == null && embedded.Q<Image>().image == null, "Collapse releases images");
                        Set(window, "selectedLayerId", b.Id); Call(window, "RefreshToolkitLayerInspector", true);
                        Call(embedded, "Tick"); Check(Get(embedded, "source") == null, "Selection while collapsed stays unrendered");
                        Drag(embedded, 1000); Later(); break;
                    case 3:
                        Compare(embedded, b); Compare(standalone, a);
                        Check(ReferenceEquals(Get(embedded, "boundLayer"), b), "Selected layer rebound");
                        ((ColorFillLayerBehaviour)b.Behaviour).color = Color.yellow; Call(doc, "MarkChanged");
                        Later(); break;
                    case 4:
                        Compare(embedded, b);
                        remembered = (float)Get(state, "height");
                        window.CreateGUI(); Later(); break;
                    case 5:
                        Check((bool)Get(embedded, "disposed") && Get(embedded, "source") == null, "Rebuild disposes old panel");
                        embedded = window.rootVisualElement.Q("layer-preview");
                        Check((float)Get(Get(embedded, "state"), "height") == remembered, "Height survives UI rebuild");
                        var savedState = Get(embedded, "state");
                        var restoredState = JsonUtility.FromJson(JsonUtility.ToJson(savedState), savedState.GetType());
                        Check((float)Get(restoredState, "height") == remembered && !(bool)Get(restoredState, "collapsed"), "Serializable window state");
                        Compare(embedded, b);
                        var parent = embedded.parent;
                        embedded.RemoveFromHierarchy();
                        Check(Get(embedded, "source") == null && !(bool)Get(embedded, "attached"), "Detach releases rendering resources");
                        Call(embedded, "Tick"); Check(Get(embedded, "source") == null, "Detached preview cannot render");
                        parent.Add(embedded);
                        doc.width = 128; doc.height = 512; Call(doc, "MarkChanged");
                        Call(standalone, "RequestPreview", true); Later(); break;
                    case 6:
                        Drag(standalone, 1000); Later(); break;
                    case 7:
                        Check(Math.Abs(standalone.resolvedStyle.height - 256) < 1, "Portrait max block height");
                        Check(standalone.worldBound.yMax <= properties.rootVisualElement.worldBound.yMax + 1, "Footer inside window");
                        var scroll = properties.rootVisualElement.Q<ScrollView>();
                        Check(scroll.worldBound.yMax <= standalone.worldBound.yMin + 1, "Settings do not overlap preview");
                        doc.width = 2; doc.height = 1; Call(doc, "MarkChanged"); Later(); break;
                    case 8:
                        Check(Math.Abs(standalone.resolvedStyle.height - 256) < 1, "Tiny image scales to fit without changing panel height");
                        doc.width = doc.height = 512; Call(doc, "MarkChanged"); Later(); break;
                    case 9:
                        Check(standalone.resolvedStyle.height > 200, "Desired height restored after small image");
                        doc.layers.Remove(b); Call(doc, "MarkChanged"); Later(); break;
                    case 10:
                        Check(!ReferenceEquals(Get(embedded, "boundLayer"), b), "Deleted layer is no longer bound");
                        var selected = (Layer)Call(window, "GetSelectedLayer");
                        if (selected != null) Compare(embedded, selected);
                        else Check(Get(embedded, "source") == null, "Empty selection releases preview");
                        doc.layers.Remove(a); Call(doc, "MarkChanged"); Later(); break;
                    case 11:
                        Check((bool)Get(standalone, "disposed"), "Deleted Properties layer disposes panel");
                        Check(Get(standalone, "source") == null && Get(standalone, "channelTexture") == null && Get(standalone, "channelMaterial") == null, "Dispose frees GPU resources");
                        Check(!(bool)Get(standalone, "attached"), "Dispose unsubscribes callbacks");
                        Check(!properties.rootVisualElement.Query<Button>().ToList().Any(x => x.text == "Close"), "No Close button for deleted layer");
                        Check(!EditorUtility.IsPersistent(doc), "No saved document touched");
                        SessionState.SetString(Key, "Passed: " + checks + " checks; real pointer drag, docking, channel pixels, layer binding, rendering parity, state, sizing and disposal.");
                        Cleanup(); break;
                }
            }
            catch (Exception ex) { SessionState.SetString(Key, "FAILED step " + (step - 1) + ": " + ex); Cleanup(); }
        }
        SessionState.SetString(Key, "Running");
        try
        {
            window.ShowUtility(); window.position = new Rect(50, 50, 1000, 780);
            typeof(LayerEditorWindowBase).GetMethod("Initialize", F).Invoke(properties, new object[] { a, doc });
            properties.ShowUtility(); properties.position = new Rect(1070, 100, 320, 550);
            Later(); return "Started; call Result.";
        }
        catch { Cleanup(); throw; }
    }
}
