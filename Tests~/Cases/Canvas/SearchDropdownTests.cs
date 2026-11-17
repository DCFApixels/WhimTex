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
        return (EditorWindow)Popup.GetMethod("Show", F | BindingFlags.DeclaredOnly).Invoke(null, new object[] {new Vector2(560,240),root,callback,new[]{"Procedural","Noise"},Vector2.zero,owner,valid,null,null});
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
            Reopening(t, root, host, provider);
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
            await Tick(); var scroll = ((ListView)Get(Get(popup,"page"),"list")).Q<ScrollView>();
            t.True(scroll.scrollOffset.y > 0, "Keyboard reveals items below viewport");
            t.True(Page(popup).Query<VisualElement>(className: "whimtex-search-row").ToList().Count < 25, "Only visible rows are created");
            float savedScroll = scroll.scrollOffset.y;
            await Capture(host, output + "/scroll.png", token);
            Key(popup, KeyCode.LeftArrow); await Tick(7); Key(popup, KeyCode.RightArrow); await Tick(7);
            scroll = ((ListView)Get(Get(popup,"page"),"list")).Q<ScrollView>();
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
            await Thumbnails(t, host, token);
            await PaletteModes(t, host, token);
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
    static void Reopening(TestContext t, object root, UnityEngine.Object firstOwner, UnityEngine.Object secondOwner)
    {
        var owned = new List<EditorWindow>();
        var current = Popup.GetField("current", F);
        bool valid = true;
        int retiredCalls = 0, activeCalls = 0;
        EditorWindow Open(Func<object, bool> select, UnityEngine.Object owner)
        {
            var popup = OpenNative(root, select, owner, () => valid);
            if (popup != null) owned.Add(popup);
            t.True(popup != null, "Every explicit open request creates a popup without a cooldown");
            t.True(ReferenceEquals(popup, current.GetValue(null)), "Latest request owns the active popup");
            t.True(popup.rootVisualElement.panel != null, "Reopened popup is attached to its native window");
            t.True(ReferenceEquals(owner, Get(popup, "owner")), "Latest request retains its own owner");
            return popup;
        }
        try
        {
            var first = Open(_ => { retiredCalls++; return true; }, firstOwner);
            var next = Open(_ => { activeCalls++; return true; }, secondOwner);
            t.True(first == null, "A new request replaces the existing popup instead of only closing it");
            t.True(Get(first, "select") == null, "Retired popup releases its selection callback");
            Key(next, KeyCode.DownArrow); Key(next, KeyCode.Return);
            t.Equal(0, retiredCalls, "Reopening never invokes the retired callback");
            t.Equal(1, activeCalls, "Selection invokes only the latest callback once");
            t.True(next == null, "Accepted choice closes the latest popup");
            // Deliberately no Editor tick between closing and opening: request timing
            // must not decide whether a menu is created.
            for (int i = 0; i < 12; i++)
            {
                valid = true;
                var popup = Open(_ => true, i % 2 == 0 ? firstOwner : secondOwner);
                switch (i % 4)
                {
                    case 0: popup.Close(); break;
                    case 1: Key(popup, KeyCode.Escape); break;
                    case 2: Call(popup, "OnLostFocus"); break;
                    case 3: valid = false; Call(popup, "Update"); break;
                }
                t.True(popup == null, "Close, Escape, focus loss and invalidation still dismiss the popup");
                t.True((EditorWindow)current.GetValue(null) == null, "Dismissed popup releases the global handle");
            }
        }
        finally
        {
            AsyncD.CleanupOwned(() => { foreach (var popup in owned) if (popup != null) popup.Close(); });
        }
    }

    static async Task Thumbnails(TestContext t, EditorWindow host, CancellationToken token)
    {
        var assembly = typeof(ShaderGraphWindow).Assembly;
        var preference = assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings").GetProperty("GraphCatalogThumbnails", F);
        const string key = "DCFApixels.WhimTex.ShaderGraph.CatalogThumbnails";
        bool existed = EditorPrefs.HasKey(key), previous = (bool)preference.GetValue(null);
        var previewType = assembly.GetType("DCFApixels.WhimTex.ShaderGraphCatalogPreview");
        var appearanceType = assembly.GetType("DCFApixels.WhimTex.ShaderGraphCatalogIcon");
        var catalogType = assembly.GetType("DCFApixels.WhimTex.ShaderGraphCatalogThumbnails");
        var cache = (IList)assembly.GetType("DCFApixels.WhimTex.ShaderExecutableCache").GetField("entries", F).GetValue(null);
        int executables = cache.Count;
        EditorWindow popup = null;
        try
        {
            preference.SetValue(null, true);
            var root = Make("Thumbnails", true);
            var category = Make("Mixed", true); Add(root, category);
            var appearance = appearanceType.GetMethod("ForNode", F).Invoke(null, new object[] { ShaderGraphNodeKind.PerlinNoise });
            var compactAppearance = appearanceType.GetMethod("ForNode", F).Invoke(null, new object[] { ShaderGraphNodeKind.Add });
            for (int i = 0; i < 80; i++)
            {
                bool thumbnail = i % 2 == 0;
                var item = Make((thumbnail ? "Perlin " : "Add ") + i);
                Set(item, "makeIcon", new Func<VisualElement>(() =>
                {
                    var view = (VisualElement)Activator.CreateInstance(previewType, true);
                    Call(view, "Bind", thumbnail ? appearance : compactAppearance); return view;
                }));
                Add(category, item);
            }
            popup = Open(root, _ => false, new[] { "Mixed" }, host, () => true);
            Func<object, float> height = item => (bool)preference.GetValue(null) && ((string)Get(item, "text")).StartsWith("Perlin ") ? 35 : 20;
            Set(popup, "rowHeight", Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(Item, typeof(float)), height.Target, height.Method));
            Set(popup, "makeOptions", new Func<Action, VisualElement>(changed =>
                (VisualElement)catalogType.GetMethod("CreateSelector", F).Invoke(null, new object[] { changed })));
            Call(popup, "CreateGUI"); for (int i = 0; i < 3; i++) await Step(token);
            var list = Page(popup).Q<ListView>();
            t.Equal(CollectionVirtualizationMethod.DynamicHeight, list.virtualizationMethod, "Mixed rows remain virtualized");
            void CheckRows()
            {
                var rows = Page(popup).Query<VisualElement>(className: "whimtex-search-row").ToList();
                t.True(rows.Count > 0 && rows.Count < 40, "Mixed list creates only visible rows");
                foreach (var row in rows)
                    if (row.userData != null && row.resolvedStyle.display != DisplayStyle.None)
                        t.Near(height(row.userData), row.resolvedStyle.height, .1, "Mixed popup row height");
            }
            CheckRows();
            await Capture(host, PathForOutput() + "/mixed-thumbnails.png", token);
            var images = Page(popup).Query<Image>().ToList().Where(image => image.image != null).ToList();
            t.True(images.Count > 0 && images.Count < 20, "Visible thumbnail rows are recycled");
            t.True(images.All(image => image.image == images[0].image), "One shared atlas for all rows");
            t.Near(32, images[0].resolvedStyle.width, .01, "Thumbnail display width");
            t.Near(32, images[0].resolvedStyle.height, .01, "Thumbnail display height");
            foreach (int index in new[] { 0, 17, 79, 50 })
            {
                Call(popup, "Select", index, true);
                for (int i = 0; i < 5; i++) await Step(token);
                CheckRows();
            }
            (object item, float offset) TopRow()
            {
                float top = Page(popup).Q<ListView>().Q<ScrollView>().contentViewport.worldBound.yMin;
                var row = Page(popup).Query<VisualElement>(className: "whimtex-search-row").ToList()
                    .First(value => value.userData != null && value.worldBound.yMin <= top + .1f && value.worldBound.yMax > top);
                return (row.userData, top - row.worldBound.yMin);
            }
            var savedTop = TopRow();
            Call(popup, "Back"); for (int i = 0; i < 7; i++) await Step(token);
            Call(popup, "Activate", false); for (int i = 0; i < 12; i++) await Step(token);
            list = Page(popup).Q<ListView>();
            var restoredTop = TopRow();
            t.True(ReferenceEquals(savedTop.item, restoredTop.item), "Mixed category restores first visible item");
            t.Near(savedTop.offset, restoredTop.offset, 1, "Mixed category restores within-row scroll");
            preference.SetValue(null, false); for (int i = 0; i < 3; i++) await Step(token);
            t.Near(20, list.fixedItemHeight, .01, "Compact updates open popup");
            foreach (var row in list.Query<VisualElement>(className: ListView.itemUssClassName).ToList())
                if (row.resolvedStyle.display != DisplayStyle.None) t.Near(20, row.resolvedStyle.height, .1, "Actual compact popup row height");
            t.Equal(50, (int)Get(Get(Get(popup, "page"), "state"), "selected"), "Display change preserves selection");
            t.Equal(0, Page(popup).Query<Image>().ToList().Count(image => image.image != null), "Compact uses icons");
            preference.SetValue(null, true); for (int i = 0; i < 3; i++) await Step(token);
            t.True(Page(popup).Query<Image>().ToList().Any(image => image.image != null), "Thumbnails return without compilation");
            CheckRows();
            t.Equal(executables, cache.Count, "Catalog does not create executable shaders");
            var fallback = (VisualElement)Activator.CreateInstance(previewType, true);
            Call(fallback, "Bind", appearanceType.GetMethod("ForNode", F).Invoke(null, new object[] { ShaderGraphNodeKind.CustomFunction }));
            t.Equal(0, fallback.Query<Image>().ToList().Count(image => image.image != null), "External function uses icon fallback");
        }
        finally
        {
            if (popup != null) UnityEngine.Object.DestroyImmediate(popup);
            preference.SetValue(null, previous); if (!existed) EditorPrefs.DeleteKey(key);
        }
    }
    static async Task PaletteModes(TestContext t, EditorWindow host, CancellationToken token)
    {
        var assembly = typeof(ShaderGraphWindow).Assembly;
        var preference = assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings").GetProperty("GraphCatalogThumbnails", F);
        const string key = "DCFApixels.WhimTex.ShaderGraph.CatalogThumbnails";
        bool existed = EditorPrefs.HasKey(key), previous = (bool)preference.GetValue(null);
        Rect position = host.position;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        var layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph };
        var owner = ScriptableObject.CreateInstance<ShaderGraphWindow>();
        VisualElement palette = null;
        try
        {
            Call((Layer)layer, "AssignNewId"); document.layers.Add(layer);
            Set(owner, "document", document); Set(owner, "layerId", ((Layer)layer).Id);
            preference.SetValue(null, true);
            host.rootVisualElement.Clear(); host.position = new Rect(position.x, position.y, 282, 760);
            assembly.GetType("DCFApixels.WhimTex.WhimTexUI").GetMethod("ApplyWindowStyles", F).Invoke(null, new object[] { host.rootVisualElement });
            host.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/ShaderGraphWindow.uss"));
            palette = (VisualElement)Activator.CreateInstance(typeof(ShaderGraphWindow).GetNestedType("NodePalette", F), F, null, new object[] { owner }, null);
            host.rootVisualElement.Add(palette); Call(palette, "RefreshIfNeeded");
            var tree = palette.Q<TreeView>();
            var rootNames = ((IList)Get(palette, "roots")).Cast<object>().Select(item => (string)Get(item, "label")).ToArray();
            void Heights()
            {
                foreach (var row in tree.Query<VisualElement>(className: TreeView.itemUssClassName).ToList())
                    if (row.resolvedStyle.display != DisplayStyle.None)
                    {
                        bool image = row.Query<Image>().ToList().Any(value => value.image != null);
                        t.Near(image ? 35 : 22, row.resolvedStyle.height, .1, "Actual mixed palette row height");
                    }
            }
            for (int i = 0; i < 4; i++)
            {
                preference.SetValue(null, i % 2 != 0);
                for (int j = 0; j < 3; j++) await Step(token);
                Heights();
                var labels = tree.Query<Label>(className: "whimtex-shader-graph-palette-label").ToList().Select(label => label.text).ToArray();
                foreach (string name in rootNames) t.True(labels.Contains(name), "Category remains visible: " + name);
            }
            tree.ExpandAll(); palette.Q<ToolbarSearchField>().value = "Noise";
            for (int j = 0; j < 3; j++) await Step(token);
            tree.SetSelection(3); object selection = tree.selectedItem;
            for (int i = 0; i < 4; i++)
            {
                preference.SetValue(null, i % 2 != 0);
                for (int j = 0; j < 3; j++) await Step(token);
                Heights();
                t.True(ReferenceEquals(selection, tree.selectedItem), "Mode switch preserves palette selection");
                t.Equal("Noise", palette.Q<ToolbarSearchField>().value, "Mode switch preserves search");
            }
        }
        finally
        {
            palette?.RemoveFromHierarchy(); UnityEngine.Object.DestroyImmediate(owner);
            Call((Layer)layer, "ReleaseTransientResources"); UnityEngine.Object.DestroyImmediate(document);
            host.position = position; preference.SetValue(null, previous); if (!existed) EditorPrefs.DeleteKey(key);
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
