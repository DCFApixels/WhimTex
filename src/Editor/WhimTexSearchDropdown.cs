using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    /// <summary>A searchable, hierarchical editor popup. No GraphView dependency.</summary>
    internal sealed class WhimTexSearchDropdown : EditorWindow
    {
        internal sealed class Item
        {
            internal readonly string text;
            internal readonly bool group;
            internal readonly object data;
            internal readonly List<Item> children = new List<Item>();
            internal Texture2D icon;
            internal bool enabled = true, highlighted;
            internal Item(string text, bool group = false, object data = null)
            { this.text = text ?? string.Empty; this.group = group; this.data = data; }
        }

        // Geometry and timing of Unity's GraphView SearchWindow; theme metrics below
        // come from the actual Editor GUIStyles rather than a second color/font palette.
        internal const float DefaultWidth = 240, DefaultHeight = 320, TransitionSeconds = .25f;
        static WhimTexSearchDropdown current;
        static double lastClosed = double.NegativeInfinity;
        Item root;
        Func<Item, bool> select;
        UnityEngine.Object owner;
        bool hasOwner;
        Func<bool> valid;
        readonly List<PageState> stack = new List<PageState>();
        readonly Dictionary<Item, PageState> states = new Dictionary<Item, PageState>();
        readonly List<Item> leaves = new List<Item>();
        ToolbarSearchField search;
        VisualElement viewport;
        Page page, outgoing;
        Theme theme;
        IVisualElementScheduledItem animation;
        double transitionStart;
        int transitionDirection;
        string query = string.Empty;
        string[] openingPath;
        internal Item CurrentGroup => stack.Count == 0 ? null : stack[stack.Count - 1].group;
        internal bool IsAnimating => outgoing != null;

        internal static WhimTexSearchDropdown Show(Vector2 screenPosition, Item root, Func<Item, bool> onSelect,
            IReadOnlyList<string> initialPath = null, Vector2 size = default, UnityEngine.Object owner = null, Func<bool> valid = null)
        {
            if (root == null || !root.group) throw new ArgumentException("A group root is required.", nameof(root));
            if (current != null) { current.Close(); return null; }
            if (EditorApplication.timeSinceStartup - lastClosed < .05) return null;
            var popup = CreateInstance<WhimTexSearchDropdown>();
            popup.root = root; popup.select = onSelect; popup.owner = owner; popup.hasOwner = owner != null; popup.valid = valid;
            popup.wantsMouseMove = true;
            popup.openingPath = initialPath?.ToArray();
            float width = Mathf.Max(DefaultWidth, size.x), height = Mathf.Max(DefaultHeight, size.y);
            current = popup;
            popup.ShowAsDropDown(new Rect(screenPosition.x - width * .5f, screenPosition.y - 16, width, 1), new Vector2(width, height));
            if (popup.search == null) popup.CreateGUI();
            popup.Focus(); popup.search.Focus();
            return popup;
        }

        void OnEnable() => AssemblyReloadEvents.beforeAssemblyReload += Close;
        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Close;
            animation?.Pause(); animation = null;
            if (current == this) { current = null; lastClosed = EditorApplication.timeSinceStartup; }
            select = null; valid = null; root = null; owner = null;
        }
        void OnLostFocus() { if (search != null) Close(); }
        bool ContextValid() => (!hasOwner || owner != null) && (valid == null || valid());
        void Update() { if (root != null && !ContextValid()) Close(); }

        public void CreateGUI()
        {
            if (root == null) return;
            FinishTransition(); rootVisualElement.Clear(); stack.Clear(); states.Clear(); leaves.Clear();
            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            theme = new Theme();
            rootVisualElement.style.unityFont = theme.font;
            rootVisualElement.style.fontSize = theme.fontSize;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/WhimTexSearchDropdown.uss");
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.AddToClassList("whimtex-search-popup");
            Theme.Background(rootVisualElement, theme.background, theme.background.normal);
            search = new ToolbarSearchField { name = "search" };
            search.AddToClassList("whimtex-search-input"); rootVisualElement.Add(search);
            search.RegisterValueChangedCallback(e => Search(e.newValue));
            viewport = new VisualElement { name = "pages" }; viewport.AddToClassList("whimtex-search-viewport");
            rootVisualElement.Add(viewport);
            var visited = new HashSet<Item>();
            void Index(Item item)
            {
                if (!visited.Add(item)) throw new ArgumentException("Dropdown items must form a tree.");
                if (item.group) { foreach (var child in item.children) Index(child); }
                else leaves.Add(item);
            }
            Index(root); stack.Add(State(root));
            if (!OpenPath(openingPath)) RebuildPage();
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            rootVisualElement.schedule.Execute(() => search?.Focus());
        }

        internal bool OpenPath(string path) => OpenPath(string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split('/'));
        PageState State(Item item)
        {
            if (!states.TryGetValue(item, out var state)) states.Add(item, state = new PageState(item));
            return state;
        }
        internal bool OpenPath(IReadOnlyList<string> path)
        {
            if (root == null) return false;
            var next = new List<PageState> { State(root) };
            Item item = root;
            if (path != null) foreach (string part in path)
            {
                var child = item.children.FirstOrDefault(e => e.group && e.enabled && e.text == part);
                if (child == null) return false;
                next.Add(State(child)); item = child;
            }
            for (int i = 0; i + 1 < next.Count; i++) next[i].selected = next[i].group.children.IndexOf(next[i + 1].group);
            FinishTransition(); stack.Clear(); stack.AddRange(next);
            query = string.Empty; search?.SetValueWithoutNotify(query);
            if (viewport != null) RebuildPage();
            return true;
        }

        internal static List<Item> FindMatches(IEnumerable<Item> items, string text)
        {
            var words = text.ToLowerInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return items.Where(item => !item.group).OrderBy(item => item.text, StringComparer.OrdinalIgnoreCase).ToList();
            string Name(Item item) => string.Concat(item.text.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
            return items.Where(item => !item.group && words.All(word => Name(item).Contains(word)))
                .OrderBy(item => Name(item).StartsWith(words[0], StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(item => item.text, StringComparer.OrdinalIgnoreCase).ToList();
        }

        void Search(string text)
        {
            query = text ?? string.Empty;
            FinishTransition(); RebuildPage();
        }
        void RebuildPage()
        {
            if (viewport == null) return;
            SaveScroll(); viewport.Clear();
            var state = stack[stack.Count - 1];
            if (query.Length != 0)
            {
                var matches = new Item("Search", true);
                matches.children.AddRange(FindMatches(leaves, query));
                state = new PageState(matches) { selected = matches.children.Count == 0 ? -1 : 0 };
            }
            page = BuildPage(state, query.Length == 0 && stack.Count > 1); viewport.Add(page.root);
            RestoreScroll(page);
        }
        void SaveScroll() { if (page != null) page.state.scroll = page.scroll.scrollOffset; }
        void RestoreScroll(Page target)
        {
            bool firstVisit = !target.state.presented;
            target.state.presented = true;
            target.scroll.schedule.Execute(() =>
            {
                if (target.root.panel == null) return;
                target.scroll.scrollOffset = target.state.scroll;
                int visible = target.state.selected >= 0 ? target.state.selected : target.rows.FindIndex(row => row.item.highlighted);
                if (firstVisit && visible >= 0 && visible < target.rows.Count) target.scroll.ScrollTo(target.rows[visible].root);
            });
        }

        Page BuildPage(PageState state, bool back)
        {
            var view = new Page(state);
            view.root.AddToClassList("whimtex-search-page");
            var header = new VisualElement { name = "categoryHeader" }; header.AddToClassList("whimtex-search-header");
            Theme.Background(header, theme.header, theme.header.normal);
            var label = new Label(state.group.text); label.AddToClassList("whimtex-search-title"); Theme.Text(label, theme.header, theme.header.normal);
            header.Add(label);
            if (back)
            {
                header.Add(theme.Arrow(true));
                header.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { Back(); e.StopPropagation(); } });
            }
            view.root.Add(header);
            view.scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Auto };
            view.scroll.AddToClassList("whimtex-search-list"); view.root.Add(view.scroll);
            for (int i = 0; i < state.group.children.Count; i++)
            {
                int index = i; var item = state.group.children[i];
                var row = new Row(item, item.group ? theme.group : theme.entry, theme);
                view.rows.Add(row); view.scroll.Add(row.root);
                row.root.RegisterCallback<PointerMoveEvent>(e => { if (e.deltaPosition.sqrMagnitude > 0 && page == view && !IsAnimating) Select(index, false); });
                row.root.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0 || page != view || IsAnimating) return;
                    Select(index, false); Activate(true); e.StopPropagation(); search.Focus();
                });
                row.State(i == state.selected);
            }
            if (view.rows.Count == 0)
            {
                var empty = new Label("No results"); empty.AddToClassList("whimtex-search-empty");
                view.scroll.Add(empty);
            }
            return view;
        }

        void Select(int index, bool scroll)
        {
            if (page == null || page.rows.Count == 0) return;
            page.state.selected = Mathf.Clamp(index, 0, page.rows.Count - 1);
            for (int i = 0; i < page.rows.Count; i++) page.rows[i].State(i == page.state.selected);
            if (scroll) page.scroll.ScrollTo(page.rows[page.state.selected].root);
        }
        void Activate(bool invoke)
        {
            if (!ContextValid()) { Close(); return; }
            if (page == null || page.state.selected < 0 || page.state.selected >= page.rows.Count) return;
            var item = page.rows[page.state.selected].item;
            if (!item.enabled) return;
            if (item.group)
            {
                if (query.Length != 0) return;
                if (IsAnimating)
                {
                    if (transitionDirection < 0 && outgoing.state.group == item)
                    { stack.Add(outgoing.state); ReverseTransition(); }
                    return;
                }
                SaveScroll(); var state = State(item); stack.Add(state);
                Navigate(BuildPage(state, true), 1);
            }
            else if (invoke && !IsAnimating && select?.Invoke(item) == true) Close();
        }
        void Back()
        {
            if (query.Length != 0 || stack.Count <= 1) return;
            SaveScroll(); stack.RemoveAt(stack.Count - 1);
            if (outgoing != null && outgoing.state == stack[stack.Count - 1])
            {
                ReverseTransition();
                return;
            }
            FinishTransition(); Navigate(BuildPage(stack[stack.Count - 1], stack.Count > 1), -1);
        }
        void ReverseTransition()
        {
            double elapsed = Math.Min(1, (EditorApplication.timeSinceStartup - transitionStart) / TransitionSeconds);
            var swap = page; page = outgoing; outgoing = swap;
            transitionDirection = -transitionDirection;
            transitionStart = EditorApplication.timeSinceStartup - (1 - elapsed) * TransitionSeconds;
        }
        void Navigate(Page next, int direction)
        {
            FinishTransition(); outgoing = page; page = next; viewport.Add(page.root);
            transitionDirection = direction; transitionStart = EditorApplication.timeSinceStartup;
            TickTransition(transitionStart); RestoreScroll(page);
            animation = rootVisualElement.schedule.Execute(() => TickTransition(EditorApplication.timeSinceStartup)).Every(16);
        }
        void TickTransition(double now)
        {
            if (outgoing == null) return;
            float t = Mathf.Clamp01((float)((now - transitionStart) / TransitionSeconds));
            float eased = t * t * (3 - 2 * t);
            Translate(page.root, transitionDirection * (1 - eased) * 100);
            Translate(outgoing.root, -transitionDirection * eased * 100);
            Repaint();
            if (t >= 1) FinishTransition();
        }
        static void Translate(VisualElement element, float percent) => element.style.translate = new Translate(Length.Percent(percent), 0);
        void FinishTransition()
        {
            animation?.Pause(); animation = null;
            outgoing?.root.RemoveFromHierarchy(); outgoing = null;
            if (page != null) Translate(page.root, 0);
        }
        void OnKey(KeyDownEvent e)
        {
            if (e.altKey || e.ctrlKey || e.commandKey) return;
            switch (e.keyCode)
            {
                case KeyCode.DownArrow: Select((page?.state.selected ?? -1) + 1, true); break;
                case KeyCode.UpArrow: Select((page?.state.selected ?? 0) - 1, true); break;
                case KeyCode.Return: case KeyCode.KeypadEnter: Activate(true); break;
                case KeyCode.RightArrow: if (query.Length != 0) return; Activate(false); break;
                case KeyCode.LeftArrow: case KeyCode.Backspace: if (query.Length != 0) return; Back(); break;
                case KeyCode.Escape: if (query.Length != 0) search.value = string.Empty; else Close(); break;
                default: return;
            }
            e.StopImmediatePropagation();
        }

        sealed class PageState
        {
            internal readonly Item group;
            internal int selected = -1;
            internal bool presented;
            internal Vector2 scroll;
            internal PageState(Item group) { this.group = group; }
        }
        sealed class Page
        {
            internal readonly PageState state;
            internal readonly VisualElement root = new VisualElement();
            internal readonly List<Row> rows = new List<Row>();
            internal ScrollView scroll;
            internal Page(PageState state) { this.state = state; }
        }
        sealed class Row
        {
            internal readonly Item item;
            internal readonly VisualElement root = new VisualElement();
            readonly VisualElement background = new VisualElement { pickingMode = PickingMode.Ignore };
            readonly Label label;
            readonly GUIStyle style;
            internal Row(Item item, GUIStyle style, Theme theme)
            {
                this.item = item; this.style = style;
                root.userData = item; root.AddToClassList("whimtex-search-row");
                root.EnableInClassList("whimtex-search-highlighted", item.highlighted);
                background.AddToClassList("whimtex-search-row-background"); root.Add(background);
                root.style.paddingLeft = style.padding.left; root.style.paddingRight = style.padding.right;
                if (item.icon != null)
                {
                    var icon = new Image { image = item.icon, pickingMode = PickingMode.Ignore };
                    icon.AddToClassList("whimtex-search-icon"); root.Add(icon);
                }
                label = new Label(item.text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("whimtex-search-row-label"); Theme.Text(label, style, style.normal, false); root.Add(label);
                if (item.group) root.Add(theme.Arrow(false));
                root.SetEnabled(item.enabled);
            }
            internal void State(bool selected)
            {
                root.EnableInClassList("whimtex-search-selected", selected);
                var state = selected || item.highlighted ? style.onFocused : style.normal;
                Theme.Background(background, style, state);
                background.style.opacity = !selected && item.highlighted ? .22f : 1;
                label.style.color = selected ? style.onFocused.textColor : style.normal.textColor;
            }
        }
        sealed class Theme
        {
            internal readonly GUIStyle entry, group, header, background, left, right;
            internal readonly Font font;
            internal readonly int fontSize;
            internal Theme()
            {
                // GetBuiltinSkin(Inspector) returns LightSkin even in a dark Editor.
                // Load the same generated skin used by Editor GUI through the public resource API.
                string name = EditorGUIUtility.isProSkin ? "DarkSkin" : "LightSkin";
                var skin = EditorGUIUtility.Load("Builtin Skins/Generated/Skins/" + name + ".guiskin") as GUISkin;
                if (skin == null) skin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);
                font = skin.font;
                fontSize = skin.label.fontSize > 0 ? skin.label.fontSize : font.fontSize;
                var baseSkin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);
                GUIStyle Find(string styleName)
                {
                    var result = new GUIStyle(skin.FindStyle(styleName) ?? EditorStyles.label);
                    var fallback = baseSkin.FindStyle(styleName);
                    // Some generated dark styles omit their 1x textures; IMGUI resolves
                    // those while drawing. UITK needs the actual themed resource.
                    void BackgroundAsset(GUIStyleState state, GUIStyleState reference)
                    {
                        if (state.background == null && reference?.background != null)
                            state.background = EditorGUIUtility.Load("Builtin Skins/" + name + "/Images/" + reference.background.name + ".png") as Texture2D;
                    }
                    BackgroundAsset(result.normal, fallback?.normal);
                    BackgroundAsset(result.onNormal, fallback?.onNormal);
                    BackgroundAsset(result.onFocused, fallback?.onFocused);
                    return result;
                }
                entry = Find("AC ComponentButton"); group = Find("AC GroupButton"); header = Find("AC BoldHeader");
                background = Find("grey_border"); left = Find("ArrowNavigationLeft"); right = Find("ArrowNavigationRight");
            }
            internal VisualElement Arrow(bool back)
            {
                var source = back ? left : right;
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList(back ? "whimtex-search-back" : "whimtex-search-forward");
                icon.style.width = source.fixedWidth; icon.style.height = source.fixedHeight;
                if (back) icon.style.left = source.margin.left; else icon.style.right = source.margin.right;
                Background(icon, source, source.normal); return icon;
            }
            internal static void Text(VisualElement element, GUIStyle source, GUIStyleState state, bool padding = true)
            {
                element.style.color = state.textColor;
                if (source.font != null) element.style.unityFont = source.font;
                if (source.fontSize > 0) element.style.fontSize = source.fontSize;
                element.style.unityFontStyleAndWeight = source.fontStyle;
                element.style.unityTextAlign = source.alignment;
                if (!padding) return;
                element.style.paddingLeft = source.padding.left; element.style.paddingRight = source.padding.right;
                element.style.paddingTop = source.padding.top; element.style.paddingBottom = source.padding.bottom;
            }
            internal static void Background(VisualElement element, GUIStyle source, GUIStyleState state)
            {
                var texture = state.background;
                int scale = 1;
                var scaled = state.scaledBackgrounds;
                if ((texture == null || EditorGUIUtility.pixelsPerPoint > 1) && scaled.Length > 0 && scaled[0] != null)
                { texture = scaled[0]; scale = 2; }
                element.style.backgroundImage = texture == null ? StyleKeyword.None : new StyleBackground(texture);
                element.style.unitySliceLeft = source.border.left * scale; element.style.unitySliceRight = source.border.right * scale;
                element.style.unitySliceTop = source.border.top * scale; element.style.unitySliceBottom = source.border.bottom * scale;
                element.style.unitySliceScale = 1f / scale;
            }
        }
    }
}
