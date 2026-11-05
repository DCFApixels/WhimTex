using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class SearchDropdownTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Popup = typeof(ShaderGraphWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexSearchDropdown", true);
    static readonly Type Item = Popup.GetNestedType("Item", F);
    static object Get(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    static object Call(object o, string n, params object[] args) => o.GetType().GetMethod(n, F).Invoke(o, args);
    static object Make(string label, bool group = false) => Activator.CreateInstance(Item, F, null, new object[] { label, group, null }, null);
    static void Add(object parent, params object[] children) { foreach (var c in children) ((IList)Get(parent, "children")).Add(c); }
    static string Group(EditorWindow w) => (string)Get(Popup.GetProperty("CurrentGroup", F).GetValue(w), "text");
    static bool Path(EditorWindow w, string path) => (bool)Popup.GetMethod("OpenPath", F, null, new[] { typeof(string) }, null).Invoke(w, new object[] { path });
    static VisualElement Page(EditorWindow w) => (VisualElement)Get(Get(w, "page"), "root");
    static void Key(EditorWindow w, KeyCode key)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key });
        e.target = w.rootVisualElement.Q<ToolbarSearchField>(); w.rootVisualElement.SendEvent(e);
    }
    static EditorWindow Open(object root, Func<object, bool> select, string[] path, UnityEngine.Object owner, Func<bool> valid)
    {
        var callback = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(Item, typeof(bool)), select.Target, select.Method);
        // Mount the actual popup view in an owned utility host, so background focus
        // changes cannot dismiss it between asynchronous render assertions.
        var popup = (EditorWindow)ScriptableObject.CreateInstance(Popup);
        Set(popup, "root", root); Set(popup, "select", callback); Set(popup, "openingPath", path);
        Set(popup, "owner", owner); Set(popup, "hasOwner", true); Set(popup, "valid", valid);
        popup.position = new Rect(0, 0, 240, 320); Call(popup, "CreateGUI");
        var host = (EditorWindow)owner; host.rootVisualElement.Clear();
        popup.rootVisualElement.style.flexGrow = 1; host.rootVisualElement.Add(popup.rootVisualElement);
        return popup;
    }
    static EditorWindow OpenNative(object root, Func<object,bool> select, UnityEngine.Object owner, Func<bool> valid)
    {
        var callback = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(Item, typeof(bool)), select.Target, select.Method);
        return (EditorWindow)Popup.GetMethod("Show", F | BindingFlags.DeclaredOnly).Invoke(null, new object[] {new Vector2(560,240),root,callback,new[]{"Procedural","Noise"},Vector2.zero,owner,valid});
    }
    public static string Start(string id) => AsyncD.Start(id, Run);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static async Task Run(TestContext t, CancellationToken token)
    {
        EditorWindow popup = null, native = null;
        var host = ScriptableObject.CreateInstance<SearchDropdownHost>();
        var provider = ScriptableObject.CreateInstance<SearchDropdownNativeProvider>();
        provider.hideFlags = HideFlags.HideAndDontSave;
        string output = PathForOutput(); Directory.CreateDirectory(output);
        int mainThread = Thread.CurrentThread.ManagedThreadId;
        try
        {
            t.Equal(0, Resources.FindObjectsOfTypeAll<SearchWindow>().Length, "No borrowed native search popup");
            t.True(Popup.GetField("current", F).GetValue(null) == null, "No borrowed UITK popup");
            host.titleContent = new GUIContent("Dropdown check"); host.ShowUtility(); host.position = new Rect(440, 225, 240, 320); host.Focus();
            async Task Tick(int count = 2) { for (int i = 0; i < count; i++) await Step(token); }
            var root = Make("Create Node", true); var procedural = Make("Procedural", true); var noise = Make("Noise", true); var math = Make("Math", true);
            var cellular = Make("Cellular Noise"); var simple = Make("Simple Noise"); var disabled = Make("Unavailable");
            Set(cellular, "highlighted", true); Set(disabled, "enabled", false);
            Add(noise, cellular, simple, disabled); Add(procedural, noise); Add(math, Make("Add"), Make("Multiply")); Add(root, math, procedural);
            int calls = 0; string selected = null; bool close = false, valid = true;
            bool Select(object item) { calls++; selected = (string)Get(item, "text"); return close; }
            popup = Open(root, Select, null, host, () => valid);
            t.True(popup != null, "UITK popup opens"); await Tick(3);
            t.Equal(mainThread, Thread.CurrentThread.ManagedThreadId, "Await resumes on Editor main thread");
            await Capture(host, output + "/root.png", token);
            t.Near(240, popup.position.width, .1, "Native width"); t.Near(320, popup.position.height, .1, "Native height");
            t.Equal(0, popup.rootVisualElement.Query<IMGUIContainer>().ToList().Count, "No IMGUI rendering");
            var search = popup.rootVisualElement.Q<ToolbarSearchField>();
            t.Near(20, search.resolvedStyle.height, .1, "Native search height");
            t.Near(8, search.layout.x, .1, "Native search left inset");
            t.Near(232, search.layout.xMax, .1, "Search fits right inset");
            var skin = EditorGUIUtility.Load("Builtin Skins/Generated/Skins/" + (EditorGUIUtility.isProSkin ? "DarkSkin" : "LightSkin") + ".guiskin") as GUISkin;
            t.Equal(skin.FindStyle("AC GroupButton").normal.textColor, Page(popup).Q<Label>(className:"whimtex-search-row-label").resolvedStyle.color, "Row uses active native theme");
            t.Near(30, popup.rootVisualElement.Q("pages").layout.y, .1, "Native page top");
            t.Near(25, Page(popup).Q("categoryHeader").resolvedStyle.height, .1, "Native header height");
            foreach (var row in Page(popup).Query<VisualElement>(className: "whimtex-search-row").ToList())
                t.Near(20, row.resolvedStyle.height, .1, "Native row height");
            t.True(Path(popup, "Procedural/Noise"), "Open nested path"); await Tick(2);
            t.Equal("Noise", Group(popup), "Initial category");
            t.Equal(0, Page(popup).Query<VisualElement>(className: "whimtex-search-selected").ToList().Count, "Highlight distinct from selection");
            var marked = Page(popup).Q<VisualElement>(className: "whimtex-search-highlighted");
            t.True(marked != null, "Current node visible");
            t.Near(.22, marked.Q(className: "whimtex-search-row-background").resolvedStyle.opacity, .001, "Subtle highlight");
            await Capture(host, output + "/noise.png", token);
            t.True(!Path(popup, "Procedural/Missing"), "Invalid path rejected"); t.Equal("Noise", Group(popup), "Invalid path leaves page unchanged");
            Key(popup, KeyCode.LeftArrow); t.Equal("Procedural", Group(popup), "Back retains ancestor"); await Tick(7);
            Key(popup, KeyCode.LeftArrow); await Tick(7); t.Equal("Create Node", Group(popup), "Full root accessible");
            Key(popup, KeyCode.DownArrow); // Previous selection is Procedural; keep it by clamping.
            Key(popup, KeyCode.RightArrow);
            t.Equal("Procedural", Group(popup), "Keyboard enters category");
            ((IVisualElementScheduledItem)Get(popup, "animation"))?.Pause();
            double start = (double)Get(popup, "transitionStart");
            Call(popup, "TickTransition", start + .125);
            t.Near(50, Page(popup).style.translate.value.x.value, .001, "250 ms smoothstep midpoint");
            var oldPage = (VisualElement)Get(Get(popup, "outgoing"), "root");
            t.Near(-50, oldPage.style.translate.value.x.value, .001, "Outgoing slides left");
            await Capture(host, output + "/animation.png", token);
            Call(popup, "TickTransition", start + .25); t.True(!(bool)Popup.GetProperty("IsAnimating", F).GetValue(popup), "Animation finishes and removes old page");
            t.Equal(1, popup.rootVisualElement.Q("pages").childCount, "No leaked animated pages");
            Key(popup, KeyCode.DownArrow); Key(popup, KeyCode.RightArrow); Key(popup, KeyCode.LeftArrow); await Tick(7);
            t.Equal("Procedural", Group(popup), "Back reverses active navigation");
            Path(popup, "Procedural/Noise"); search.value = "NOI cell"; await Tick();
            var rows = Page(popup).Query<VisualElement>(className: "whimtex-search-row").ToList();
            t.Equal(1, rows.Count, "Case-insensitive multiword search across whole catalog");
            t.Equal("Cellular Noise", rows[0].Q<Label>().text, "Search result matches all words");
            t.True(rows[0].ClassListContains("whimtex-search-highlighted"), "Highlight remains in search");
            await Capture(host, output + "/search.png", token);
            search.value = "Noise"; await Tick(); t.Equal(2, Page(popup).Query<VisualElement>(className:"whimtex-search-row").ToList().Count, "Search all leaf names");
            search.value = "   "; await Tick(); t.Equal(5, Page(popup).Query<VisualElement>(className:"whimtex-search-row").ToList().Count, "Whitespace query is safe");
            search.value = "no-such-node"; await Tick(); Key(popup, KeyCode.Return); t.Equal(0, calls, "Empty result cannot select");
            Key(popup, KeyCode.Escape); await Tick(); t.Equal("Noise", Group(popup), "Escape clears search and returns to category");
            Key(popup, KeyCode.DownArrow); Key(popup, KeyCode.Return); t.Equal(1, calls, "Enter invokes once");
            t.Equal("Cellular Noise", selected, "Correct selection payload"); t.True(popup != null, "False result keeps popup open");
            Key(popup, KeyCode.DownArrow); Key(popup, KeyCode.DownArrow); Key(popup, KeyCode.Return); t.Equal(1, calls, "Disabled choice cannot invoke");
            UnityEngine.Object.DestroyImmediate(popup); popup = null; await Tick();
            var longRoot = Make("Long catalog", true); var longGroup = Make("Items", true); Add(longRoot, longGroup);
            for (int i = 0; i < 60; i++) Add(longGroup, Make("Item " + i.ToString("00")));
            popup = Open(longRoot, Select, new[] { "Items" }, host, () => valid); await Tick();
            for (int i = 0; i < 60; i++) Key(popup, KeyCode.DownArrow);
            await Tick(); var scroll = (ScrollView)Get(Get(popup,"page"),"scroll");
            t.True(scroll.scrollOffset.y > 0, "Keyboard reveals items below viewport");
            float savedScroll = scroll.scrollOffset.y;
            await Capture(host, output + "/scroll.png", token);
            Key(popup, KeyCode.LeftArrow); await Tick(7); Key(popup, KeyCode.RightArrow); await Tick(7);
            scroll = (ScrollView)Get(Get(popup,"page"),"scroll");
            t.Near(savedScroll, scroll.scrollOffset.y, 1, "Returning to category preserves scroll");
            t.Equal(59, (int)Get(Get(Get(popup,"page"),"state"),"selected"), "Returning preserves selection");
            UnityEngine.Object.DestroyImmediate(popup); popup = null; await Tick();
            popup = OpenNative(root, Select, host, () => valid); close = true;
            Call(popup, "Select", 1, false); Call(popup, "Activate", true); t.Equal(2, calls, "Second enabled choice invokes");
            t.True(popup == null, "True result closes popup"); await Tick();
            popup = OpenNative(root, Select, host, () => valid);
            t.Equal("Noise", Group(popup), "Show accepts initial path");
            valid = false; Call(popup,"Update"); await Tick(3); t.True(popup == null, "Changed owner context closes stale popup"); valid = true;
            await Tick(); popup = OpenNative(root, Select, host, () => valid);
            Call(popup,"OnLostFocus"); await Tick(); t.True(popup == null, "Loss of focus closes owned popup");
            var names = new[] { Make("X Noise"), Make("Noise B"), Make("Noise A") };
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Item)); foreach (var n in names) list.Add(n);
            var found = (IList)Popup.GetMethod("FindMatches", F).Invoke(null, new object[] { list, "noise" });
            t.Equal("Noise A,Noise B,X Noise", string.Join(",", found.Cast<object>().Select(n => Get(n,"text"))), "Prefix matches precede substrings; alphabetic within groups");
            t.True(SearchWindow.Open(new SearchWindowContext(new Vector2(560, 240)), provider), "Native reference opens through public API");
            native = Resources.FindObjectsOfTypeAll<SearchWindow>().Single();
            await Tick(3);
            t.Near(240, native.position.width, .1, "Native reference width");
            t.Near(320, native.position.height, .1, "Native reference height");
        }
        finally
        {
            AsyncD.CleanupOwned(() => { if (popup != null) UnityEngine.Object.DestroyImmediate(popup); }, () => { if (native != null) native.Close(); },
                () => { if (host != null) host.Close(); }, () => UnityEngine.Object.DestroyImmediate(provider));
        }
    }
    public static string RenderStart(string id) => AsyncD.Start(id, RenderPopup);
    static async Task RenderPopup(TestContext t, CancellationToken token)
    {
        EditorWindow popup = null;
        try
        {
            var root = Make("Create Node",true); var category = Make("Procedural",true); var noise = Make("Noise",true);
            var cellular = Make("Cellular Noise"); Set(cellular,"highlighted",true); Add(root,category); Add(category,noise); Add(noise,cellular,Make("Simple Noise"));
            popup = OpenNative(root, _=>false, null, ()=>true);
            t.True(popup != null,"Real popup opened");
            await Capture(popup, PathForOutput()+"/actual-popup.png", token, () =>
            {
                t.True(popup.rootVisualElement.panel != null,"Popup attached to its own native window during render");
                t.Near(240,popup.rootVisualElement.resolvedStyle.width,.1,"Own popup width");
                t.Equal("Noise",Group(popup),"Actual popup starts at nested path");
                t.True(popup.rootVisualElement.Q(className:"whimtex-search-back").resolvedStyle.backgroundImage.texture != null,"Back arrow has a themed texture");
            });
        }
        finally { if (popup != null) popup.Close(); }
    }
    static string PathForOutput() => System.IO.Path.GetFullPath("Temp/WhimTex/dropdown-check");
    static async Task Capture(EditorWindow owner, string path, CancellationToken token, Action check = null)
    {
        var capture = new RenderCapture { owner = owner, path = path, check = check, pickingMode = PickingMode.Ignore, cullingEnabled = false };
        capture.style.position = Position.Absolute; capture.style.width = capture.style.height = 1;
        capture.style.top = capture.style.left = 0;
        owner.rootVisualElement.Add(capture);
        try
        {
            for (int i = 0; i < 100 && !capture.done.Task.IsCompleted; i++) { owner.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await Step(token); }
            if (!capture.done.Task.IsCompleted) throw new TimeoutException("Owned popup did not repaint: window=" + owner.position + ", panel=" + (owner.rootVisualElement.panel != null) + ", root=" + owner.rootVisualElement.worldBound + ", capture=" + capture.worldBound + ", focused=" + owner.hasFocus);
            await capture.done.Task;
        }
        finally { capture.RemoveFromHierarchy(); }
    }
    static async Task Step(CancellationToken token)
    {
        var done = new TaskCompletionSource<bool>(); double until = EditorApplication.timeSinceStartup + .05;
        int editorThread = -1;
        void Tick() { if (EditorApplication.timeSinceStartup >= until) { editorThread = Thread.CurrentThread.ManagedThreadId; done.TrySetResult(true); } }
        EditorApplication.update += Tick;
        using (token.Register(() => done.TrySetCanceled()))
            try { await done.Task; } finally { EditorApplication.update -= Tick; }
        if (editorThread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("Continuation left Editor thread: " + editorThread + " -> " + Thread.CurrentThread.ManagedThreadId);
    }
    sealed class RenderCapture : ImmediateModeElement
    {
        internal EditorWindow owner; internal string path;
        internal Action check;
        internal readonly TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
        protected override void ImmediateRepaint()
        {
            if (done.Task.IsCompleted) return;
            Texture2D texture = null;
            try
            {
                var rt = RenderTexture.active;
                int w = rt != null ? rt.width : Mathf.RoundToInt(owner.position.width * EditorGUIUtility.pixelsPerPoint);
                int h = rt != null ? rt.height : Mathf.RoundToInt(owner.position.height * EditorGUIUtility.pixelsPerPoint);
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, w, h), 0, 0, false); texture.Apply(false);
                File.WriteAllBytes(path, texture.EncodeToPNG()); check?.Invoke(); done.TrySetResult(true);
            }
            catch (Exception e) { done.TrySetException(e); }
            finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
public sealed class SearchDropdownHost : EditorWindow { public void CreateGUI() => rootVisualElement.Add(new Label("Dropdown check")); }
public sealed class SearchDropdownNativeProvider : ScriptableObject, ISearchWindowProvider
{
    public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext c) => new List<SearchTreeEntry> {
        new SearchTreeGroupEntry(new GUIContent("Create Node"),0),
        new SearchTreeGroupEntry(new GUIContent("Math"),1), new SearchTreeEntry(new GUIContent("Add")){level=2},
        new SearchTreeEntry(new GUIContent("Multiply")){level=2}, new SearchTreeGroupEntry(new GUIContent("Procedural"),1),
        new SearchTreeGroupEntry(new GUIContent("Noise"),2),new SearchTreeEntry(new GUIContent("Cellular Noise")){level=3},
        new SearchTreeEntry(new GUIContent("Simple Noise")){level=3},new SearchTreeEntry(new GUIContent("Unavailable")){level=3} };
    public bool OnSelectEntry(SearchTreeEntry e, SearchWindowContext c) => false;
}
