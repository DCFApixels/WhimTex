using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class LayerPreviewPanelTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

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

    
    static string Begin(WhimTex.Tests.UnityC.AsyncFixture job)
    {
        int checks = 0, step = 0;
        void Check(bool ok, string text) { job.Context.True(ok, text); checks++; }
        void CheckFlush(ScrollView scroll, VisualElement preview)
        {
            float gap = preview.worldBound.yMin - scroll.contentViewport.worldBound.yMax;
            Check(Math.Abs(gap) < .5f, "Settings clipping reaches Preview strip; gap=" + gap);
        }
        void CheckChannelDrag(VisualElement group, Func<int> mask)
        {
            var buttons = group.Query<Button>().ToList();
            void Down(int index, int mouseButton = 0)
            {
                using var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = mouseButton, mousePosition = buttons[index].worldBound.center });
                evt.target = buttons[index]; buttons[index].SendEvent(evt);
            }
            void Move(Vector2 position, bool held = true)
            {
                using var evt = PointerMoveEvent.GetPooled(new WhimTex.Tests.UnityC.PublicInput.Pointer { position = position, button = held ? 0 : -1, pressedButtons = held ? 1 : 0 });
                evt.target = group; group.SendEvent(evt);
            }
            void Up(Vector2 position)
            {
                using var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = position });
                evt.target = group; group.SendEvent(evt);
            }
            void Key(int index)
            {
                using var evt = NavigationSubmitEvent.GetPooled();
                evt.target = buttons[index]; buttons[index].SendEvent(evt);
            }
            Check(mask() == 15, "Drag fixture starts with all channels");
            Down(0);
            Check(mask() == 14, "First channel toggles on press, not release");
            Check(group.HasPointerCapture(PointerId.mousePointerId), "Channel group captures drag");
            Move(buttons[3].worldBound.center);
            Check(mask() == 0, "Fast sweep includes skipped intermediate buttons");
            Move(buttons[0].worldBound.center);
            Check(mask() == 0, "Return sweep assigns remembered state without toggling again");
            Up(buttons[0].worldBound.center);
            Check(mask() == 0 && !group.HasPointerCapture(PointerId.mousePointerId), "Release does not double toggle and releases capture");
            Move(buttons[3].worldBound.center, false);
            Check(mask() == 0, "Hover after release does nothing");
            Down(1); Move(buttons[3].worldBound.center); Up(buttons[3].worldBound.center);
            Check(mask() == 14, "New drag remembers on state");
            Key(0); Check(mask() == 15, "Keyboard activation retained");
            Key(1); Key(3); Check(mask() == 5, "Mixed initial mask");
            Down(0); Move(buttons[3].worldBound.center); Up(buttons[3].worldBound.center);
            Check(mask() == 0, "Mixed buttons assigned off instead of individually inverted");
            Down(3); Move(buttons[0].worldBound.center); Up(buttons[0].worldBound.center);
            Check(mask() == 15, "Reverse sweep assigns on");
            buttons[1].SetEnabled(false);
            Down(0); Move(buttons[3].worldBound.center); Up(buttons[3].worldBound.center);
            Check(mask() == 2, "Disabled buttons ignored");
            buttons[1].SetEnabled(true);
            Down(0); Move(buttons[3].worldBound.center); Up(buttons[3].worldBound.center);
            Check(mask() == 15, "Reenabled group usable");
            Down(0, 1);
            Check(mask() == 15 && !group.HasPointerCapture(PointerId.mousePointerId), "Right click does not paint channels");
            Down(0);
            using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
            { evt.target = buttons[0]; buttons[0].SendEvent(evt); }
            Check(!group.HasPointerCapture(PointerId.mousePointerId), "Escape releases capture");
            Move(buttons[3].worldBound.center); Up(buttons[3].worldBound.center);
            Check(mask() == 14, "Escape ends assignment without reverting applied change");
            Key(0);
            Down(0);
            Move(buttons[0].worldBound.center, false);
            Check(!group.HasPointerCapture(PointerId.mousePointerId), "Missing release recovered when mouse is no longer held");
            Move(buttons[3].worldBound.center, false);
            Check(mask() == 14, "Recovered release cannot change other channels");
            Key(0);
            Down(3);
            Vector2 outside = group.worldBound.max + Vector2.one * 100;
            Move(outside); Up(outside);
            Check(mask() == 7 && !group.HasPointerCapture(PointerId.mousePointerId), "Release outside group ends gesture");
            Key(3);
            Check(mask() == 15, "Drag checks leave original state");
        }

        var focus = EditorWindow.focusedWindow;
        var window = job.Scope.Own(ScriptableObject.CreateInstance<TextureCompositorWindow>());
        var doc = (TextureCompositor)Get(window, "compositor");
        var properties = job.Scope.Own(ScriptableObject.CreateInstance<FileLayerEditorWindow>());
        var tex = job.Scope.Own(new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave });
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
        void Cleanup() => job.DisposeOwned();
        job.OwnCleanup(() => {
            job.Scope.CloseWindow(properties); job.Scope.CloseWindow(window);
            if (doc != null) job.Scope.Destroy(doc);
            job.Scope.Destroy(tex);
            if (focus != null) focus.Focus();
        });
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
            finally { job.Scope.Release(expected); }
        }
        void Later() => job.Schedule(window.rootVisualElement, Run, 500);
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
                            Check(p.Query<PopupField<string>>().ToList().Count == 0, "No channel dropdown");
                            var buttons = p.Q("layer-preview-channels").Query<Button>().ToList();
                            Check(buttons.Count == 4 && string.Concat(buttons.Select(x => x.text)) == "RGBA", "Four channel buttons");
                            foreach (var button in buttons)
                            {
                                var h = p.Q("layer-preview-resizer").worldBound;
                                Check(button.worldBound.yMin >= h.yMin && button.worldBound.yMax <= h.yMax && button.worldBound.xMax <= h.xMax, "Compact buttons fit strip");
                                Check(Math.Abs(button.resolvedStyle.height - 16) < .1f && Math.Abs(button.resolvedStyle.width - 20) < .1f, "Compact dimensions");
                                var fill = button.resolvedStyle.backgroundColor;
                                Check(Math.Abs(fill.r - fill.g) < .001f && Math.Abs(fill.g - fill.b) < .001f, "Neutral active channel color");
                            }
                            Check(p.Query<PopupField<int>>().ToList().Count == 0, "No mip dropdown");
                            Check(p.Q("layer-preview-surface").Query<Label>().ToList().Count == 0, "No overlay text");
                        }
                        Compare(standalone, a);
                        Check(Math.Abs(standalone.resolvedStyle.height - 220) < 1, "Landscape retains preferred panel height");
                        Drag(embedded, 1000); Later(); break;
                    case 1:
                        CheckFlush(window.rootVisualElement.Q<ScrollView>("selected-layer-settings"), embedded);
                        // Manual screenshots excluded; UI geometry assertions retained.
                        Check(!(bool)Get(state, "collapsed"), "Drag opens embedded");
                        Check(embedded.resolvedStyle.height <= 256.1f, "Drag capped at 256 independent of texture size");
                        Compare(embedded, a);
                        var source = (RenderTexture)Get(embedded, "source");
                        var before = Read(source);
                        var channelButtons = embedded.Q("layer-preview-channels").Query<Button>().ToList();
                        int footerMask = (int)Get(window, "canvasChannels");
                        CheckChannelDrag(embedded.Q("layer-preview-channels"), () => (int)Get(state, "channelMask"));
                        Check((int)Get(window, "canvasChannels") == footerMask, "Mini drag leaves main mask unchanged");
                        CheckChannelDrag(window.rootVisualElement.Q("canvasFooterColor"), () => (int)Get(window, "canvasChannels"));
                        Check((int)Get(state, "channelMask") == 15 && (int)Get(Get(standalone, "state"), "channelMask") == 15, "Main drag leaves both mini masks unchanged");
                        Check(!embedded.Q("layer-preview-resizer").HasPointerCapture(PointerId.mousePointerId), "Channel gesture does not start divider resize");
                        var savedActive = RenderTexture.active; bool savedSrgb = GL.sRGBWrite;
                        for (int mask = 0; mask < 16; mask++)
                        {
                            for (int bit = 0; bit < 4; bit++)
                                if ((((int)Get(state, "channelMask") ^ mask) & (1 << bit)) != 0)
                                {
                                    using var evt = NavigationSubmitEvent.GetPooled();
                                    evt.target = channelButtons[bit]; channelButtons[bit].SendEvent(evt);
                                }
                            Check((int)Get(state, "channelMask") == mask, "Channel button updates mask");
                            for (int bit = 0; bit < 4; bit++)
                                Check(channelButtons[bit].ClassListContains("whimtex-channel-button--enabled") == ((mask & (1 << bit)) != 0), "Button highlight");
                            Check(RenderTexture.active == savedActive && GL.sRGBWrite == savedSrgb, "Channel render state restored");
                            var after = Read((RenderTexture)embedded.Q<Image>().image);
                            for (int i = 0; i < after.Length; i++) for (int c = 0; c < 4; c++)
                            {
                                int rgb = mask & 7;
                                float expected;
                                if (rgb == 0) expected = c == 3 ? 1 : (mask & 8) != 0 ? before[i].a : 0;
                                else if (c == 3) expected = (mask & 8) != 0 ? before[i].a : 1;
                                else
                                {
                                    int component = rgb == 1 ? 0 : rgb == 2 ? 1 : rgb == 4 ? 2 : c;
                                    expected = (rgb & (1 << component)) != 0 ? Mathf.Clamp01(before[i][component]) : 0;
                                    if (QualitySettings.activeColorSpace == ColorSpace.Gamma) expected = Mathf.LinearToGammaSpace(expected);
                                }
                                Check(Math.Abs(after[i][c] - expected) < .003f, "Channel pixels " + mask);
                            }
                            Check(ReferenceEquals(Get(embedded, "source"), source), "Channel switch does not rerender layer");
                            Check((int)Get(window, "canvasChannels") == footerMask && (int)Get(Get(standalone, "state"), "channelMask") == 15, "Mini masks independent of footer and Properties");
                        }
                        last = source;
                        Call(embedded, "Tick"); Check(ReferenceEquals(Get(embedded, "source"), last), "Clean preview reused");
                        var header = embedded.Q("layer-preview-resizer");
                        foreach (var button in channelButtons)
                        {
                            using var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = button.worldBound.center });
                            e.target = button; Call(Get(embedded, "resize"), "Down", e);
                            Check(!header.HasPointerCapture(PointerId.mousePointerId), "Channel button does not capture resize");
                        }
                        Call(embedded, "ToggleChannel", 2);
                        Call(embedded, "ToggleChannel", 8);
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
                        Check((int)Get(restoredState, "channelMask") == 5, "Channel combination survives layer switching, collapse and rebuild");
                        Compare(embedded, b);
                        var parent = embedded.parent;
                        embedded.RemoveFromHierarchy();
                        Check(Get(embedded, "source") == null && !(bool)Get(embedded, "attached"), "Detach releases rendering resources");
                        Call(embedded, "Tick"); Check(Get(embedded, "source") == null, "Detached preview cannot render");
                        parent.Add(embedded);
                        doc.width = 128; doc.height = 512; Call(doc, "MarkChanged");
                        Call(standalone, "RequestLayerPreview", true); Later(); break;
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
                        job.Pass();
                        Cleanup(); break;
                }
            }
            catch (Exception ex) { job.Fail(ex); Cleanup(); }
        }
        
        try
        {
            window.ShowUtility(); window.position = new Rect(50, 50, 1000, 780);
            typeof(LayerEditorWindowBase).GetMethod("Initialize", F).Invoke(properties, new object[] { a, doc });
            properties.ShowUtility(); properties.position = new Rect(1070, 100, 320, 550);
            Later(); return job.Read();
        }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }

    public static string Poll(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Poll(runId);
    public static string Result(string runId) => Poll(runId);
    public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cancel(runId);
    public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cleanup(runId);

    public static string Start(string runId)
    {
        var job = WhimTex.Tests.UnityC.AsyncFixture.Create(runId);
        try { return Begin(job); }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }
}
