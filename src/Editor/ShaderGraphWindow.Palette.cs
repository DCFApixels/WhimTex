using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        [SerializeField] private string nodePaletteSearch = string.Empty;
        [SerializeField] private List<string> expandedNodeCategories = new();
        private NodePalette nodePalette;
        private const string NodePaletteDragKey = "WhimTex.ShaderGraph.NodePalette";

        private sealed class NodePaletteDragData
        {
            internal NodePalette palette;
            internal ShaderGraph graph;
            internal object choice;
            internal bool svmOnly;
        }

        private bool ReadNodePaletteDrag(out object choice)
        {
            choice = null;
            if (!Editable || Graph == null || DragAndDrop.GetGenericData(NodePaletteDragKey) is not NodePaletteDragData data ||
                data.palette != nodePalette || data.svmOnly != svmNodesOnly || !ReferenceEquals(data.graph, Graph)) return false;
            nodePalette.RefreshIfNeeded();
            if (!nodePalette.choices.Contains(data.choice)) return false;
            choice = data.choice;
            return true;
        }

        private sealed class NodePalette : VisualElement
        {
            private sealed class Item
            {
                internal int id;
                internal Item parent;
                internal string label, path;
                internal object choice;
                internal ShaderGraphCatalogIcon.Appearance appearance;
                internal readonly List<Item> children = new();
            }

            private readonly ShaderGraphWindow window;
            private readonly WhimTexVirtualList tree;
            private readonly List<Item> visibleItems = new();
            private readonly HashSet<int> expanded = new();
            private List<TreeViewItemData<Item>> filtered = new();
            private Item selected;
            private readonly Label empty;
            private readonly List<Item> roots = new();
            private readonly Dictionary<string, int> ids = new(StringComparer.Ordinal);
            internal readonly HashSet<object> choices = new();
            private IReadOnlyList<ShaderGraphFunctionCatalog.Entry> functions;
            private List<SearchTreeEntry> catalogEntries;
            private bool dirty = true;
            private bool thumbnails;
            private string[] terms = Array.Empty<string>();

            internal NodePalette(ShaderGraphWindow window)
            {
                this.window = window;
                name = "node-palette";
                AddToClassList("whimtex-shader-graph-palette");
                var toolbar = new Toolbar();
                var search = new ToolbarSearchField { name = "node-palette-search", value = window.nodePaletteSearch };
                search.AddToClassList("whimtex-shader-graph-palette-search");
                search.RegisterValueChangedCallback(e =>
                {
                    window.nodePaletteSearch = e.newValue;
                    Filter();
                });
                toolbar.Add(search);
                toolbar.Add(ShaderGraphCatalogThumbnails.CreateSelector(RefreshPresentation)); Add(toolbar);
                tree = new WhimTexVirtualList { name = "node-palette-tree", focusable = true,
                    fixedItemHeight = 22, itemsSource = visibleItems,
                    itemHeight = index => ShaderGraphCatalogThumbnails.Height(visibleItems[index].appearance.kind, 22) };
                tree.AddToClassList("whimtex-shader-graph-palette-tree");
                tree.AddToClassList(TreeView.ussClassName);
                tree.AddToClassList(BaseVerticalCollectionView.ussClassName);
                tree.makeItem = () => new PaletteRow(this);
                tree.bindItem = (element, index) => ((PaletteRow)element).Bind(visibleItems[index]);
                tree.unbindItem = (element, _) => ((PaletteRow)element).Unbind();
                tree.RegisterCallback<KeyDownEvent>(OnKey);
                RefreshPresentation();
                Add(tree);
                empty = new Label("No matching nodes.");
                empty.AddToClassList("whimtex-shader-graph-palette-empty");
                empty.AddToClassList("whimtex-hidden"); Add(empty);
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (DragAndDrop.GetGenericData(NodePaletteDragKey) is NodePaletteDragData data && data.palette == this)
                    { DragAndDrop.SetGenericData(NodePaletteDragKey, null); window.paletteDragPreview?.Clear(); }
                });
            }

            private void RefreshPresentation()
            {
                if (tree == null) return;
                if (thumbnails == WhimTexUserSettings.GraphCatalogThumbnails) return;
                thumbnails = WhimTexUserSettings.GraphCatalogThumbnails;
                tree.GetAnchor(out int index, out float fraction);
                tree.Rebuild();
                tree.RestoreAnchor(index, fraction);
            }

            private sealed class PaletteRow : VisualElement
            {
                readonly NodePalette palette;
                readonly VisualElement indents, body, accent;
                readonly Toggle toggle;
                readonly Label label;
                readonly ShaderGraphCatalogPreview preview;
                internal PaletteRow(NodePalette palette)
                {
                    this.palette = palette;
                    AddToClassList(BaseVerticalCollectionView.itemUssClassName);
                    AddToClassList(TreeView.itemUssClassName);
                    style.flexDirection = FlexDirection.Row; style.alignItems = Align.Center;
                    indents = new VisualElement { pickingMode = PickingMode.Ignore };
                    indents.AddToClassList(TreeView.itemIndentUssClassName);
                    indents.style.flexShrink = 0; Add(indents);
                    // Take the arrow and its metrics from the native Foldout, without
                    // maintaining a second copy of the Editor's toggle styles.
                    var foldout = new Foldout();
                    toggle = foldout.Q<Toggle>(); toggle.RemoveFromHierarchy();
                    toggle.AddToClassList(TreeView.itemToggleUssClassName);
                    toggle.focusable = false; Add(toggle);
                    toggle.RegisterValueChangedCallback(e =>
                    { if (userData is Item item) palette.SetExpanded(item, e.newValue); });
                    body = new VisualElement(); body.AddToClassList("whimtex-shader-graph-palette-row"); Add(body);
                    preview = new ShaderGraphCatalogPreview(); body.Add(preview);
                    label = new Label { pickingMode = PickingMode.Ignore, displayTooltipWhenElided = false };
                    label.AddToClassList("whimtex-shader-graph-palette-label"); body.Add(label);
                    body.AddManipulator(new PaletteDrag(palette));
                    RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0 || userData is not Item item) return;
                        palette.Select(item);
                    });
                    RegisterCallback<ClickEvent>(e =>
                    {
                        if (e.button != 0 || userData is not Item item || item.choice != null ||
                            e.target is VisualElement target && (target == toggle || toggle.Contains(target))) return;
                        palette.SetExpanded(item, !palette.expanded.Contains(item.id));
                    });
                    accent = new VisualElement { pickingMode = PickingMode.Ignore };
                    accent.AddToClassList("whimtex-shader-graph-palette-accent"); Add(accent);
                }
                internal void Bind(Item item)
                {
                    userData = body.userData = item;
                    int depth = 0; for (var parent = item.parent; parent != null; parent = parent.parent) depth++;
                    // Native TreeView sets this inline (not through the USS class):
                    // 15px per level, measured on the standard control in 6000.7.0a6.
                    indents.style.width = depth * 15;
                    toggle.style.visibility = item.choice == null ? Visibility.Visible : Visibility.Hidden;
                    toggle.SetValueWithoutNotify(palette.expanded.Contains(item.id));
                    label.text = item.label; preview.Bind(item.appearance);
                    accent.style.backgroundColor = item.appearance.color;
                    EnableInClassList(BaseVerticalCollectionView.itemSelectedVariantUssClassName, item == palette.selected);
                }
                internal void Unbind() { userData = body.userData = null; }
            }

            internal void Invalidate() => dirty = true;

            internal void RefreshIfNeeded()
            {
                var catalog = window.svmNodesOnly ? null : ShaderGraphFunctionCatalog.Entries;
                if (!dirty && ReferenceEquals(functions, catalog)) return;
                dirty = false; functions = catalog;
                var entries = CreateNodeSearchTree(window.Graph, false, window.svmNodesOnly, window.WouldRecurse);
                if (SameCatalog(entries)) return;
                catalogEntries = entries;
                roots.Clear(); choices.Clear();
                var parents = new List<Item>();
                for (int i = 1; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    while (parents.Count >= entry.level) parents.RemoveAt(parents.Count - 1);
                    string path = parents.Count == 0 ? entry.content.text : parents[parents.Count - 1].path + "/" + entry.content.text;
                    string key = (entry is SearchTreeGroupEntry ? "group/" : "node/") + path;
                    if (!ids.TryGetValue(key, out int id)) ids.Add(key, id = ids.Count + 1);
                    var item = new Item { id = id, label = entry.content.text, path = path, choice = entry.userData,
                        parent = parents.Count == 0 ? null : parents[parents.Count - 1],
                        appearance = CatalogAppearance(entry, path) };
                    if (parents.Count == 0) roots.Add(item); else parents[parents.Count - 1].children.Add(item);
                    if (entry is SearchTreeGroupEntry) parents.Add(item); else choices.Add(item.choice);
                }
                Filter();
            }

            private bool SameCatalog(List<SearchTreeEntry> entries)
            {
                if (catalogEntries == null || catalogEntries.Count != entries.Count) return false;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].level != catalogEntries[i].level || entries[i].content.text != catalogEntries[i].content.text ||
                        !Equals(entries[i].userData, catalogEntries[i].userData)) return false;
                return true;
            }

            private void Filter()
            {
                terms = (window.nodePaletteSearch ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                filtered = FilterItems(roots); selected = null; expanded.Clear();
                if (terms.Length != 0) AddGroups(filtered);
                else foreach (string path in window.expandedNodeCategories)
                    if (ids.TryGetValue("group/" + path, out int id)) expanded.Add(id);
                RebuildVisible(); tree.RestoreAnchor(0, 0);
                empty.EnableInClassList("whimtex-hidden", filtered.Count != 0);
            }

            private void AddGroups(IEnumerable<TreeViewItemData<Item>> items)
            {
                foreach (var item in items) if (item.hasChildren) { expanded.Add(item.id); AddGroups(item.children); }
            }
            private void RebuildVisible()
            {
                visibleItems.Clear();
                void Visit(IEnumerable<TreeViewItemData<Item>> items)
                {
                    foreach (var item in items)
                    { visibleItems.Add(item.data); if (expanded.Contains(item.id)) Visit(item.children); }
                }
                Visit(filtered); tree.Rebuild();
            }
            private void SetExpanded(Item item, bool value)
            {
                if (item.choice != null) return;
                if (value) expanded.Add(item.id); else expanded.Remove(item.id);
                if (terms.Length == 0)
                {
                    if (value) { if (!window.expandedNodeCategories.Contains(item.path)) window.expandedNodeCategories.Add(item.path); }
                    else window.expandedNodeCategories.Remove(item.path);
                }
                // Preserve the first visibleItems row when another category changes above it.
                tree.GetAnchor(out int index, out float fraction);
                var anchor = index >= 0 ? visibleItems[index] : null;
                RebuildVisible();
                while (anchor != null && !visibleItems.Contains(anchor)) { anchor = anchor.parent; fraction = 0; }
                if (anchor != null) tree.RestoreAnchor(visibleItems.IndexOf(anchor), fraction);
            }
            private void Select(Item item)
            { selected = item; tree.Focus(); tree.RefreshVisible(); }
            private void OnKey(KeyDownEvent e)
            {
                if (e.altKey || e.ctrlKey || e.commandKey || visibleItems.Count == 0) return;
                int index = visibleItems.IndexOf(selected);
                switch (e.keyCode)
                {
                    case KeyCode.DownArrow: index = Mathf.Min(index + 1, visibleItems.Count - 1); break;
                    case KeyCode.UpArrow: index = Mathf.Max(0, index - 1); break;
                    case KeyCode.Home: index = 0; break;
                    case KeyCode.End: index = visibleItems.Count - 1; break;
                    case KeyCode.RightArrow:
                        if (index < 0) index = 0;
                        else if (selected.choice == null && !expanded.Contains(selected.id)) SetExpanded(selected, true);
                        else if (selected.choice == null) index = Mathf.Min(index + 1, visibleItems.Count - 1);
                        break;
                    case KeyCode.LeftArrow:
                        if (index < 0) index = 0;
                        else if (expanded.Contains(selected.id)) SetExpanded(selected, false);
                        else if (selected.parent != null) index = visibleItems.IndexOf(selected.parent);
                        break;
                    default: return;
                }
                Select(visibleItems[Mathf.Max(0, index)]); tree.ScrollToItem(Mathf.Max(0, index)); e.StopPropagation();
            }

            private List<TreeViewItemData<Item>> FilterItems(List<Item> source)
            {
                var result = new List<TreeViewItemData<Item>>();
                foreach (var item in source)
                {
                    if (item.choice == null)
                    {
                        var children = FilterItems(item.children);
                        if (children.Count != 0) result.Add(new TreeViewItemData<Item>(item.id, item, children));
                    }
                    else
                    {
                        bool match = true;
                        foreach (string term in terms)
                            if (item.path.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0 &&
                                !(item.choice is ShaderGraphNodeKind kind && ShaderGraphNodes.SearchAliases(kind).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                            { match = false; break; }
                        if (match) result.Add(new TreeViewItemData<Item>(item.id, item));
                    }
                }
                return result;
            }

            private sealed class PaletteDrag : PointerManipulator
            {
                private readonly NodePalette palette;
                private Item item;
                private Vector2 start;
                private int pointerId;
                internal PaletteDrag(NodePalette palette) => this.palette = palette;
                protected override void RegisterCallbacksOnTarget()
                {
                    target.RegisterCallback<PointerDownEvent>(Down);
                    target.RegisterCallback<PointerMoveEvent>(Move);
                    target.RegisterCallback<PointerUpEvent>(Up);
                    target.RegisterCallback<PointerCaptureOutEvent>(Cancel);
                }
                protected override void UnregisterCallbacksFromTarget()
                {
                    target.UnregisterCallback<PointerDownEvent>(Down);
                    target.UnregisterCallback<PointerMoveEvent>(Move);
                    target.UnregisterCallback<PointerUpEvent>(Up);
                    target.UnregisterCallback<PointerCaptureOutEvent>(Cancel);
                }
                private void Down(PointerDownEvent e)
                {
                    if (e.button != 0 || !palette.window.Editable || target.userData is not Item candidate || candidate.choice == null) return;
                    item = candidate; start = e.position; pointerId = e.pointerId;
                    palette.Select(item);
                    target.CapturePointer(pointerId); e.StopImmediatePropagation();
                }
                private void Move(PointerMoveEvent e)
                {
                    if (item == null || e.pointerId != pointerId || (e.pressedButtons & 1) == 0 || ((Vector2)e.position - start).sqrMagnitude < 25) return;
                    var selected = item;
                    item = null; target.ReleasePointer(pointerId);
                    if (!palette.window.Editable || target.userData != selected) return;
                    DragAndDrop.PrepareStartDrag();
                    var data = new NodePaletteDragData
                    { palette = palette, graph = palette.window.Graph, choice = selected.choice, svmOnly = palette.window.svmNodesOnly };
                    DragAndDrop.SetGenericData(NodePaletteDragKey, data);
                    DragAndDrop.StartDrag(selected.label);
                    palette.window.paletteDragPreview?.Begin(data, e.position);
                    e.StopImmediatePropagation();
                }
                private void Up(PointerUpEvent e)
                {
                    if (item == null || e.pointerId != pointerId || e.button != 0) return;
                    item = null; target.ReleasePointer(pointerId); e.StopImmediatePropagation();
                }
                private void Cancel(PointerCaptureOutEvent e) => item = null;
            }
        }
    }
}
