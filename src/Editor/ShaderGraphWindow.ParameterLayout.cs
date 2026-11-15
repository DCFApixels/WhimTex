using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class ParameterBlock
        {
            internal Foldout foldout;
            internal VisualElement children;
            internal ulong stamp;
            internal readonly List<Action> refresh = new();
        }
        [Serializable] private sealed class ParameterLayoutCopy { public List<ShaderGraphParameterItem> items; }
        private readonly Dictionary<string, ParameterBlock> parameterBlocks = new();
        private readonly HashSet<string> expandedParameterGroups = new();
        private VisualElement parameterList, parameterInterface;
        private string selectedParameterItem;
        private ulong parameterTreeStamp;
        private string dropCheckKey;
        private bool dropCheckAllowed;

        private ShaderGraph LayoutCandidate()
        {
            return new ShaderGraph { parameters = Graph.parameters,
                parameterLayout = JsonUtility.FromJson<ParameterLayoutCopy>(JsonUtility.ToJson(new ParameterLayoutCopy { items = Graph.parameterLayout })).items };
        }
        private bool EditParameterLayout(string name, Action<ShaderGraph> action)
        {
            if (!Editable || Graph == null) return false;
            try
            {
                var candidate = LayoutCandidate(); ShaderGraphParameterLayout.Normalize(candidate); action(candidate); ShaderGraphParameterLayout.Validate(candidate);
                Edit(name, g => g.parameterLayout = candidate.parameterLayout, structural: false, rebuild: false, layoutOnly: true);
                RebuildParameterTree(); return true;
            }
            catch (FormatException e) { ShowError(e.Message); RebuildParameterTree(); return false; }
        }
        private void SelectParameterItem(string id)
        {
            selectedParameterItem = id;
            foreach (var pair in parameterBlocks) pair.Value.foldout.EnableInClassList("whimtex-shader-graph-parameter-selected", pair.Key == id);
        }
        private string ParameterAddParent()
        {
            var item = ShaderGraphParameterLayout.Find(Graph, selectedParameterItem);
            if (item?.IsParameter == true) item = ShaderGraphParameterLayout.FindParent(Graph, item.id);
            if (item?.kind == ShaderContainerKind.Tabs) item = item.children.Find(c => c.kind == ShaderContainerKind.Tab);
            return item?.id;
        }
        private string ContainerAddParent(ShaderContainerKind kind)
        {
            var item = ShaderGraphParameterLayout.Find(Graph, selectedParameterItem);
            if (kind == ShaderContainerKind.Tab)
            {
                if (item?.kind == ShaderContainerKind.Tab) item = ShaderGraphParameterLayout.FindParent(Graph, item.id);
                return item?.kind == ShaderContainerKind.Tabs ? item.id : null;
            }
            return ParameterAddParent();
        }
        private void AddParameterContainer(ShaderContainerKind kind)
        {
            string parent = ContainerAddParent(kind), id = null;
            if (kind == ShaderContainerKind.Tab && parent == null) return;
            if (EditParameterLayout("Add Parameter " + kind, graph =>
            {
                string title = kind.ToString(); int suffix = 1;
                var siblings = parent == null ? graph.parameterLayout : ShaderGraphParameterLayout.Find(graph, parent).children;
                while (siblings.Exists(i => !i.IsParameter && i.title == title)) title = kind + " " + ++suffix;
                var item = ShaderGraphParameterLayout.AddContainer(graph, kind, title, parent); id = item.id;
                expandedParameterGroups.Add(id);
                if (kind == ShaderContainerKind.Tabs) expandedParameterGroups.Add(item.children[0].id);
            })) { SelectParameterItem(id); RebuildParameterTree(); }
        }
        private void BuildParameterAddMenu(DropdownMenu menu, string prefix = "", string parent = null)
        {
            foreach (var type in ParameterTypes)
            {
                var captured = type;
                menu.AppendAction(prefix + ParameterTypeName(type), _ => { if (parent != null) SelectParameterItem(parent); AddParameter(captured); }, _ => Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
            menu.AppendSeparator();
            foreach (ShaderContainerKind kind in Enum.GetValues(typeof(ShaderContainerKind)))
            {
                var captured = kind;
                menu.AppendAction(prefix + kind, _ => { if (parent != null) SelectParameterItem(parent); AddParameterContainer(captured); },
                    _ => CanAddParameterContainer(captured) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }
        private bool CanAddParameterContainer(ShaderContainerKind kind)
        {
            if (!Editable || Graph == null) return false;
            string parent = ContainerAddParent(kind);
            if (kind == ShaderContainerKind.Tab) return parent != null;
            for (var item = ShaderGraphParameterLayout.Find(Graph, parent); item != null; item = ShaderGraphParameterLayout.FindParent(Graph, item.id))
            {
                if (kind == ShaderContainerKind.Tabs && item.kind == ShaderContainerKind.Tabs) return false;
                if (kind == ShaderContainerKind.Group && item.kind is ShaderContainerKind.Group or ShaderContainerKind.Foldout) return item.kind == ShaderContainerKind.Foldout;
            }
            return true;
        }
        private void RefreshParameterTreeLayout()
        {
            if (Graph != null && parameterTreeStamp != ShaderGraphParameterLayout.Fingerprint(Graph)) RebuildParameterTree();
        }
        private void RebuildParameterTree()
        {
            if (declarations == null) return;
            HideParameterInsertion(); dropCheckKey = null;
            if (parameterList?.parent != declarations)
            {
                declarations.Clear(); parameterBlocks.Clear(); parameterInsertion = null;
                parameterInterface = new VisualElement(); parameterList = new VisualElement { name = "graph-parameter-tree" };
                declarations.Add(parameterInterface); declarations.Add(parameterList);
                parameterList.RegisterCallback<PointerDownEvent>(e => { if (e.target == parameterList) SelectParameterItem(null); });
            }
            declarationRefresh.Clear(); parameterInterface.Clear(); RebuildSubGraphInterface(parameterInterface);
            if (Graph == null) { parameterList.Clear(); parameterBlocks.Clear(); return; }
            var alive = new HashSet<string>();
            void Build(List<ShaderGraphParameterItem> items, VisualElement parent)
            {
                int index = 0;
                foreach (var item in items)
                {
                    var p = item.IsParameter ? Graph.FindParameter(item.parameterId) : null;
                    if (item.IsParameter && p == null) continue;
                    ulong stamp = p != null ? ShaderGraphParameters.MetadataFingerprint(p) : ContainerStamp(item);
                    if (!parameterBlocks.TryGetValue(item.id, out var block) || block.stamp != stamp)
                    {
                        block?.foldout.RemoveFromHierarchy(); block = new ParameterBlock { stamp = stamp };
                        int refreshStart = declarationRefresh.Count;
                        block.foldout = p != null ? BuildParameterBlock(p) : BuildParameterContainer(item, block);
                        block.refresh.AddRange(declarationRefresh.GetRange(refreshStart, declarationRefresh.Count - refreshStart));
                        declarationRefresh.RemoveRange(refreshStart, declarationRefresh.Count - refreshStart);
                        parameterBlocks[item.id] = block;
                    }
                    alive.Add(item.id); declarationRefresh.AddRange(block.refresh);
                    if (block.foldout.parent != parent || parent.IndexOf(block.foldout) != index) parent.Insert(index, block.foldout);
                    block.foldout.EnableInClassList("whimtex-shader-graph-parameter-selected", item.id == selectedParameterItem);
                    if (!item.IsParameter) Build(item.children, block.children);
                    index++;
                }
            }
            Build(ShaderGraphParameterLayout.Roots(Graph), parameterList);
            foreach (string id in new List<string>(parameterBlocks.Keys)) if (!alive.Contains(id))
            { parameterBlocks[id].foldout.RemoveFromHierarchy(); parameterBlocks.Remove(id); }
            parameterTreeStamp = ShaderGraphParameterLayout.Fingerprint(Graph);
        }
        private ulong ContainerStamp(ShaderGraphParameterItem item)
        {
            ulong stamp = 14695981039346656037UL;
            void Text(string value) { if (value != null) foreach (char c in value) stamp = RenderFingerprint.Mix(stamp, c); stamp = RenderFingerprint.Mix(stamp, 255); }
            Text(item.title); Text(item.headerParameterId); stamp = RenderFingerprint.Mix(stamp, (ulong)item.kind);
            stamp = RenderFingerprint.Mix(stamp, item.hideTitle ? 1UL : 0UL);
            foreach (var child in item.children)
            {
                Text(child.id);
                if (Graph.FindParameter(child.parameterId) is ShaderParameter p) { Text(p.name); stamp = RenderFingerprint.Mix(stamp, ShaderGraphParameters.MetadataFingerprint(p)); }
            }
            return stamp;
        }
        private Foldout BuildParameterContainer(ShaderGraphParameterItem item, ParameterBlock block)
        {
            string id = item.id;
            var foldout = new Foldout { text = string.IsNullOrEmpty(item.title) ? item.kind.ToString() : item.title,
                value = expandedParameterGroups.Contains(id), userData = id };
            foldout.AddToClassList("whimtex-shader-graph-parameter"); foldout.AddToClassList("whimtex-shader-graph-parameter-group");
            foldout.RegisterValueChangedCallback(e => { if (e.target != foldout) return; if (e.newValue) expandedParameterGroups.Add(id); else expandedParameterGroups.Remove(id); });
            var header = foldout.Q<Toggle>(className: Foldout.toggleUssClassName); header.AddToClassList("whimtex-shader-graph-parameter-header");
            header.AddManipulator(new ParameterDrag(this, id, foldout));
            var type = new Label(item.kind.ToString()) { pickingMode = PickingMode.Ignore }; type.AddToClassList("whimtex-shader-graph-parameter-kind"); header.Add(type);
            header.AddManipulator(new ContextualMenuManipulator(e =>
            {
                SelectParameterItem(id);
                BuildParameterAddMenu(e.menu, "Add/", id);
                e.menu.AppendAction("Ungroup", _ => EditParameterLayout("Ungroup Parameters", graph => ShaderGraphParameterLayout.Ungroup(graph, id)), _ => Editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.StopPropagation();
            }));
            var title = new TextField("Title") { value = item.title ?? "", isDelayed = true };
            title.RegisterValueChangedCallback(e =>
            {
                if (!EditParameterLayout("Rename Parameter Group", graph => ShaderGraphParameterLayout.Find(graph, id).title = e.newValue.Trim())) title.SetValueWithoutNotify(e.previousValue);
            });
            if (ShaderGraphParameterLayout.CanHideTitle(item.kind))
            {
                var titleRow = new VisualElement(); titleRow.AddToClassList("whimtex-shader-graph-parameter-title");
                title.SetEnabled(!item.hideTitle); titleRow.Add(title);
                bool visible = !item.hideTitle && !string.IsNullOrWhiteSpace(item.title);
                var visibility = new Button(() => EditParameterLayout("Change Group Title Visibility", graph =>
                    ShaderGraphParameterLayout.SetTitleVisible(ShaderGraphParameterLayout.Find(graph, id), !visible)))
                    { name = "parameter-title-visibility" };
                visibility.AddToClassList("whimtex-icon-action");
                visibility.Add(new LayerActionIcon(visible ? LayerActionIcon.Kind.Eye : LayerActionIcon.Kind.EyeOff));
                visibility.SetEnabled(string.IsNullOrEmpty(item.headerParameterId));
                titleRow.Add(visibility); foldout.Add(titleRow);
            }
            else foldout.Add(title);
            if (item.kind != ShaderContainerKind.Tabs)
            {
                var ids = new List<string> { null }; var names = new List<string> { "None" };
                foreach (var child in item.children)
                    if (child.IsParameter && Graph.FindParameter(child.parameterId) is ShaderParameter p && ShaderGraphParameterLayout.CanUseHeader(p, item.kind) &&
                        !Graph.parameters.Exists(other => other.id != p.id && other.name == p.name))
                    { ids.Add(p.id); names.Add(p.name); }
                var binding = new DropdownField("Header", names, Math.Max(0, ids.IndexOf(item.headerParameterId)));
                binding.SetEnabled(!item.hideTitle);
                binding.RegisterValueChangedCallback(e =>
                {
                    if (!EditParameterLayout("Change Group Header", graph => ShaderGraphParameterLayout.Find(graph, id).headerParameterId = ids[names.IndexOf(e.newValue)]))
                        binding.SetValueWithoutNotify(e.previousValue);
                });
                foldout.Add(binding);
            }
            block.children = new VisualElement { name = "parameter-group-children" }; block.children.AddToClassList("whimtex-shader-graph-parameter-children");
            foldout.Add(block.children); return foldout;
        }
        private void MoveParameterLayout(string id, string parent, int index)
        {
            if (EditParameterLayout("Move Parameter Declaration", graph => ShaderGraphParameterLayout.Move(graph, id, parent, index)) && parent != null)
            { expandedParameterGroups.Add(parent); if (parameterBlocks.TryGetValue(parent, out var block)) block.foldout.value = true; }
        }
        private bool ReadParameterLayoutDrag(out ParameterDragData data)
        {
            data = DragAndDrop.GetGenericData(ParameterDragKey) as ParameterDragData;
            return data != null && data.window == this && Editable && Graph != null &&
                (Graph.FindParameter(data.id) != null || ShaderGraphParameterLayout.Find(Graph, data.id) != null);
        }
        private bool ParameterDestination(Vector2 position, string moving, out string parentId, out int index, out Rect marker)
        {
            string destination = null; int insertion = 0; Rect area = parameterList.worldBound;
            void Locate(List<ShaderGraphParameterItem> items, string parent, VisualElement container)
            {
                destination = parent; insertion = items.Count;
                area = new Rect(container.worldBound.x, container.worldBound.yMax, container.worldBound.width, 2);
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i]; if (!parameterBlocks.TryGetValue(item.id, out var block)) continue;
                    var bounds = block.foldout.worldBound;
                    var header = block.foldout.Q<Toggle>(className: Foldout.toggleUssClassName).worldBound;
                    if (position.y < header.yMin + header.height * .25f)
                    { insertion = i; area.y = bounds.yMin; return; }
                    if (!item.IsParameter && bounds.Contains(position))
                    {
                        if (header.Contains(position) && position.y < header.yMax - header.height * .25f)
                        { destination = item.id; insertion = item.children.Count; area = new Rect(bounds.x + 4, header.yMax, bounds.width - 8, 2); return; }
                        if (block.foldout.value && position.y > header.yMax)
                        { Locate(item.children, item.id, block.children); return; }
                    }
                    if (position.y < bounds.yMax)
                    { insertion = i + 1; area.y = bounds.yMax; return; }
                }
            }
            Locate(ShaderGraphParameterLayout.Roots(Graph), null, parameterList);
            parentId = destination; index = insertion; marker = area;
            string key = moving + ":" + parentId + ":" + index + ":" + parameterTreeStamp;
            if (dropCheckKey != key)
            {
                dropCheckKey = key;
                try { var candidate = LayoutCandidate(); ShaderGraphParameterLayout.Move(candidate, moving, parentId, index); dropCheckAllowed = true; }
                catch (FormatException) { dropCheckAllowed = false; }
            }
            return dropCheckAllowed;
        }
    }
}
