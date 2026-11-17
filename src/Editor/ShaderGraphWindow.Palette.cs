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
                internal string label, path;
                internal object choice;
                internal ShaderGraphCatalogIcon.Appearance appearance;
                internal readonly List<Item> children = new();
            }

            private readonly ShaderGraphWindow window;
            private readonly TreeView tree;
            private readonly Label empty;
            private readonly List<Item> roots = new();
            private readonly Dictionary<string, int> ids = new(StringComparer.Ordinal);
            internal readonly HashSet<object> choices = new();
            private IReadOnlyList<ShaderGraphFunctionCatalog.Entry> functions;
            private List<SearchTreeEntry> catalogEntries;
            private bool dirty = true, rebuilding;
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
                tree = new TreeView { name = "node-palette-tree", selectionType = SelectionType.Single,
                    fixedItemHeight = 22, virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight };
                tree.AddToClassList("whimtex-shader-graph-palette-tree");
                tree.makeItem = () =>
                {
                    var row = new VisualElement();
                    row.AddToClassList("whimtex-shader-graph-palette-row");
                    row.Add(new ShaderGraphCatalogPreview());
                    var label = new Label { pickingMode = PickingMode.Ignore, displayTooltipWhenElided = false };
                    label.AddToClassList("whimtex-shader-graph-palette-label");
                    row.Add(label);
                    row.RegisterCallback<AttachToPanelEvent>(_ => BindRowAccent(row));
                    row.AddManipulator(new PaletteDrag(this));
                    return row;
                };
                tree.bindItem = (element, index) =>
                {
                    var item = tree.GetItemDataForIndex<Item>(index);
                    element.userData = item;
                    element.style.height = ShaderGraphCatalogThumbnails.Height(item.appearance.kind, 22);
                    ((ShaderGraphCatalogPreview)element[0]).Bind(item.appearance);
                    ((Label)element[1]).text = item.label;
                    BindRowAccent(element);
                };
                tree.unbindItem = (element, _) => { element.userData = null; BindRowAccent(element); };
                tree.itemExpandedChanged += e =>
                {
                    if (rebuilding || terms.Length != 0) return;
                    var item = tree.GetItemDataForId<Item>(e.id);
                    if (item == null || item.choice != null) return;
                    if (e.isExpanded)
                    { if (!window.expandedNodeCategories.Contains(item.path)) window.expandedNodeCategories.Add(item.path); }
                    else window.expandedNodeCategories.Remove(item.path);
                };
                RefreshPresentation();
                Add(tree);
                empty = new Label("No matching nodes.");
                empty.AddToClassList("whimtex-shader-graph-palette-empty");
                empty.AddToClassList("whimtex-hidden"); Add(empty);
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (DragAndDrop.GetGenericData(NodePaletteDragKey) is NodePaletteDragData data && data.palette == this)
                        DragAndDrop.SetGenericData(NodePaletteDragKey, null);
                });
            }

            private void RefreshPresentation()
            {
                if (tree == null) return;
                if (thumbnails == WhimTexUserSettings.GraphCatalogThumbnails) return;
                thumbnails = WhimTexUserSettings.GraphCatalogThumbnails;
                tree.Rebuild();
            }

            private static void BindRowAccent(VisualElement element)
            {
                var row = element.parent;
                while (row != null && !row.ClassListContains(TreeView.itemUssClassName)) row = row.parent;
                if (row == null) return;
                const string accentClass = "whimtex-shader-graph-palette-accent";
                var accent = row.Q<VisualElement>(className: accentClass);
                if (accent == null)
                {
                    accent = new VisualElement { pickingMode = PickingMode.Ignore };
                    accent.AddToClassList(accentClass);
                    row.Add(accent);
                }
                accent.style.backgroundColor = element.userData is Item item && !item.appearance.group
                    ? item.appearance.color : Color.clear;
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
                var items = FilterItems(roots);
                rebuilding = true;
                try
                {
                    tree.ClearSelection();
                    tree.SetRootItems(items);
                    tree.CollapseAll();
                    if (terms.Length != 0) tree.ExpandAll();
                    else foreach (string path in window.expandedNodeCategories)
                        if (ids.TryGetValue("group/" + path, out int id)) tree.ExpandItem(id);
                    tree.RefreshItems();
                }
                finally { rebuilding = false; }
                empty.EnableInClassList("whimtex-hidden", items.Count != 0);
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
                    palette.tree.SetSelectionById(item.id);
                    target.CapturePointer(pointerId); e.StopImmediatePropagation();
                }
                private void Move(PointerMoveEvent e)
                {
                    if (item == null || e.pointerId != pointerId || (e.pressedButtons & 1) == 0 || ((Vector2)e.position - start).sqrMagnitude < 25) return;
                    var selected = item;
                    item = null; target.ReleasePointer(pointerId);
                    if (!palette.window.Editable || target.userData != selected) return;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData(NodePaletteDragKey, new NodePaletteDragData
                    { palette = palette, graph = palette.window.Graph, choice = selected.choice, svmOnly = palette.window.svmNodesOnly });
                    DragAndDrop.StartDrag(selected.label);
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
