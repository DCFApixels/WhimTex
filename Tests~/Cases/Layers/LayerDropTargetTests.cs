using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;
using WhimTex.Tests.UnityD;

// Inspect only package-owned methods on one owned utility window and in-memory model.
// No native drag session, global drag payload or global Undo/Redo execution.
public static class LayerDropTargetTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private static object Call(object owner, string method, params object[] args)
        => owner.GetType().GetMethod(method, Flags).Invoke(owner, args);
    private static object Field(object owner, string field)
        => owner.GetType().GetField(field, Flags).GetValue(owner);
    private static Layer Fill(string name)
        => new Layer(new ColorFillLayerBehaviour { color = Color.red }) { layerName = name };
    private static Layer Group(string name)
        => new Layer(new GroupLayerBehaviour()) { layerName = name };

    private readonly struct Target
    {
        internal readonly List<Layer> Container;
        internal readonly int Index, Depth;
        internal readonly Layer Expand;
        internal readonly VisualElement Indicator;
        internal readonly bool Before;
        internal Target(object value)
        {
            Container = (List<Layer>)Field(value, "Container");
            Index = (int)Field(value, "Index");
            Depth = (int)Field(value, "Depth");
            Expand = (Layer)Field(value, "Expand");
            Indicator = (VisualElement)Field(value, "Indicator");
            Before = (bool)Field(value, "Before");
        }
    }

    private static bool Resolve(WhimTexWindow window, Vector2 point, out Target target)
    {
        object[] args = { point, null };
        bool resolved = (bool)Call(window, "TryResolveLayerDropTarget", args);
        target = resolved ? new Target(args[1]) : default;
        return resolved;
    }

    private static Task Execute(TestContext context, CancellationToken token)
        => UnityAScope.RunOwnedAsync(async scope =>
    {
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 8;
        var lead = Fill("Leading sibling");
        var outer = Group("Outer group");
        var firstChild = Fill("First child");
        var inner = Group("Inner group");
        var deep = Fill("Deep last child");
        inner.children.Add(deep);
        outer.children.Add(firstChild); outer.children.Add(inner);
        var tail = Fill("Following sibling");
        var empty = Group("Empty group");
        document.layers.Add(lead); document.layers.Add(outer); document.layers.Add(tail); document.layers.Add(empty);
        outer.transform.position = new Vector2(1f, 2f);
        outer.transform.rotation = 20d;
        inner.transform.position = new Vector2(-1f, .5f);
        inner.transform.scale = new Vector2(.8f, 1.2f);
        deep.transform.position = new Vector2(.75f, -.5f);

        var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
        Call(window, "SetDocument", document);
        window.GetType().GetField("layerSettingsPaneHeight", Flags).SetValue(window, 100f);
        window.titleContent = new GUIContent("WhimTex safe layer drop targets");
        window.ShowUtility(); window.position = new Rect(80, 80, 1100, 800); window.CreateGUI();
        var root = window.rootVisualElement;
        root.Q<TwoPaneSplitView>(className: "whimtex-workspace-split").fixedPaneInitialDimension = 520f;
        var scroll = (ScrollView)Field(window, "toolkitSettingsScroll");
        var hierarchy = (VisualElement)Field(window, "toolkitLayerHierarchyRoot");
        var expansion = (Dictionary<string, bool>)Field(window, "groupExpansion");
        expansion[outer.Id] = true; expansion[inner.Id] = true;

        async Task Settle(int ticks = 3)
        { for (int tick = 0; tick < ticks; tick++) await AsyncD.Tick(root, token); }
        void Refresh() => Call(window, "RefreshToolkitInterface", false);
        VisualElement Row(Layer layer)
        {
            foreach (var row in hierarchy.Children())
                if (row.ClassListContains("whimtex-layer-row") && Equals(row.userData, layer.Id)) return row;
            throw new InvalidOperationException("Missing owned row: " + layer.layerName);
        }
        Vector2 Point(VisualElement row, float fraction, int depth = 0)
            => new Vector2(row.worldBound.xMin + 26f + depth * 14f + 7f,
                Mathf.Lerp(row.worldBound.yMin, row.worldBound.yMax, fraction));
        Target CheckTarget(Vector2 point, List<Layer> container, int index, int depth,
            Layer expand, string message)
        {
            context.True(Resolve(window, point, out Target target), message + " resolves");
            context.True(ReferenceEquals(container, target.Container), message + " owns the correct container");
            context.Equal(index, target.Index, message + " insertion slot");
            context.Equal(depth, target.Depth, message + " indentation depth");
            context.True(ReferenceEquals(expand, target.Expand), message + " parent highlight");
            return target;
        }
        void RectNear(Rect expected, Rect actual, string message)
        {
            context.Near(expected.x, actual.x, .01, message + " X");
            context.Near(expected.y, actual.y, .01, message + " Y");
            context.Near(expected.width, actual.width, .01, message + " width");
            context.Near(expected.height, actual.height, .01, message + " height");
        }

        Refresh(); await Settle(5);
        context.True(scroll.contentViewport.worldBound.Contains(Row(deep).worldBound.center),
            "Nested rows fit in the owned Layers viewport");
        var groupRow = Row(outer);
        var beforeGroup = CheckTarget(Point(groupRow, .05f), document.layers, 1, 0, null,
            "Expanded group top edge");
        context.True(beforeGroup.Before, "Expanded group top edge shows a before line");
        foreach (float fraction in new[] { .3f, .5f, .9f })
            CheckTarget(Point(groupRow, fraction), outer.children, 0, 1, outer,
                "Expanded group header fraction " + fraction);
        CheckTarget(Point(Row(firstChild), .1f, 1), outer.children, 0, 1, null,
            "Before the first child stays inside its parent");
        CheckTarget(Point(Row(inner), .5f, 1), inner.children, 0, 2, inner,
            "Nested expanded group accepts its first child");

        // The deepest closing boundary also closes both ancestor groups.
        foreach (int depth in new[] { 2, 1, 0 })
        {
            var destination = depth == 2 ? inner.children : depth == 1 ? outer.children : document.layers;
            int index = depth == 2 ? 1 : 2;
            var lower = CheckTarget(Point(Row(deep), .95f, depth), destination, index, depth, null,
                "Nested closing lower boundary at depth " + depth);
            context.True(!lower.Before && lower.Indicator == Row(deep),
                "Lower boundary marks the visible child bottom at depth " + depth);
            var upper = CheckTarget(Point(Row(tail), .05f, depth), destination, index, depth, null,
                "Same closing upper boundary at depth " + depth);
            context.True(upper.Before && upper.Indicator == Row(tail),
                "Upper boundary marks the following sibling top at depth " + depth);
        }
        CheckTarget(Point(Row(deep), .05f, 0), inner.children, 0, 2, null,
            "A boundary with no closing ancestor cannot unindent arbitrarily");

        var endpoint = (VisualElement)Field(window, "toolkitLayerEndDropZone");
        var view = scroll.contentViewport.worldBound;
        var blank = new Vector2(view.center.x, Mathf.Min(view.yMax - 5f, endpoint.worldBound.yMax + 20f));
        context.True(blank.y >= endpoint.worldBound.yMin, "Owned list leaves a real empty area below its last row");
        CheckTarget(blank, document.layers, document.layers.Count, 0, null,
            "Empty viewport below the list appends at root depth");
        var header = root.Q<VisualElement>(className: "whimtex-layer-table-header");
        context.True(!Resolve(window, header.worldBound.center, out _),
            "The column header is not a layer insertion target");
        context.True(!Resolve(window, new Vector2(view.xMin - 1f, view.center.y), out _),
            "A point outside the Layers viewport is rejected by the tree resolver");

        foreach (VisualElement element in new[] { Row(deep), hierarchy, endpoint })
        {
            Rect elementBefore = element.worldBound, firstBefore = Row(lead).worldBound, deepBefore = Row(deep).worldBound;
            Call(window, "SetToolkitDropIndicator", element, false, true, 1);
            await Settle(2);
            RectNear(elementBefore, element.worldBound, "Insertion marker preserves target geometry");
            RectNear(firstBefore, Row(lead).worldBound, "Insertion marker preserves first-row geometry");
            RectNear(deepBefore, Row(deep).worldBound, "Insertion marker preserves nested-row geometry");
            VisualElement marker = null;
            foreach (var child in element.Children())
                if (child.ClassListContains("whimtex-layer-drop-marker")) { marker = child; break; }
            context.True(marker != null && marker.pickingMode == PickingMode.Ignore &&
                marker.resolvedStyle.position == Position.Absolute,
                "Insertion feedback is an absolute non-picking overlay");
            context.Near(40f, marker.resolvedStyle.left, .1, "Marker line follows the resolved parent depth");
            Call(window, "ClearToolkitDropIndicator"); await Settle(2);
            RectNear(elementBefore, element.worldBound, "Clearing marker preserves target geometry");
        }
        Rect insideBefore = Row(outer).worldBound;
        Call(window, "SetToolkitDropIndicator", Row(outer), true, true, 1);
        await Settle(2);
        RectNear(insideBefore, Row(outer).worldBound, "Into-group highlight preserves header geometry");
        context.True(Row(outer).ClassListContains("whimtex-layer-row--drop-inside"),
            "Into-group target visibly differs from a sibling insertion line");
        string previewDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Temp", "WhimTex");
        System.IO.Directory.CreateDirectory(previewDirectory);
        await UnityACapture.Capture(window, System.IO.Path.Combine(previewDirectory, "layer-drop-inside.png"), token);
        foreach (int depth in new[] { 2, 0 })
        {
            Call(window, "SetToolkitDropIndicator", Row(deep), false, false, depth);
            await Settle(2);
            await UnityACapture.Capture(window, System.IO.Path.Combine(previewDirectory,
                depth == 0 ? "layer-drop-root.png" : "layer-drop-nested.png"), token);
        }
        Call(window, "ClearToolkitDropIndicator");

        expansion[outer.Id] = false; Refresh(); await Settle();
        var collapsed = CheckTarget(Point(Row(outer), .9f), document.layers, 2, 0, null,
            "Collapsed group lower edge places a sibling after the group");
        context.True(!collapsed.Before, "Collapsed group lower edge uses an after line");
        CheckTarget(Point(Row(outer), .5f), outer.children, 0, 1, outer,
            "Collapsed group center accepts children and requests expansion");
        CheckTarget(Point(Row(empty), .5f), empty.children, 0, 1, empty,
            "An empty group has an inside target");
        CheckTarget(Point(Row(empty), .9f), document.layers, 4, 0, null,
            "An empty group retains a lower sibling target");
        expansion[outer.Id] = true; Refresh(); await Settle();

        // Adapter inspection does not publish any drag payload or open a native drag loop.
        foreach (VisualElement outside in new[] { (VisualElement)Field(window, "toolkitCanvasView"),
            (VisualElement)Field(window, "toolkitLayerFooter") })
        {
            object[] args = { outside.worldBound.center, null, -1, null, null, false, -1 };
            context.True((bool)Call(window, "ResolveCrossWindowLayerDrop", args),
                "Explicit outside-tree copy destination resolves");
            context.True(ReferenceEquals(document.layers, args[1]) && (int)args[2] == 0,
                "Canvas and footer copy destinations retain root prepend placement");
        }

        bool Allowed(List<Layer> layers, List<Layer> destination)
            => (bool)Call(window, "CanDropLayers", layers, destination);
        bool Moves(List<Layer> layers, List<Layer> destination, int index)
            => (bool)Call(window, "CanMoveLayerRoots", layers, destination, index);
        var other = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        other.layers.Add(Fill("Foreign"));
        context.True(!Allowed(new List<Layer> { lead }, other.layers), "Foreign model containers are rejected");
        context.True(!Allowed(new List<Layer> { lead }, new List<Layer>()), "Detached containers are rejected");
        context.True(!Allowed(new List<Layer> { Fill("Detached") }, document.layers), "Detached source layers are rejected");
        context.True(!Allowed(new List<Layer> { lead, lead }, document.layers), "Duplicate roots are rejected");
        context.True(!Allowed(new List<Layer> { outer, deep }, document.layers) &&
            !Allowed(new List<Layer> { deep, outer }, document.layers), "Ancestor and child payloads are rejected in either order");
        context.True(!Allowed(new List<Layer> { outer }, outer.children) &&
            !Allowed(new List<Layer> { outer }, inner.children), "A group cannot move into itself or descendants");
        context.True(!Allowed(new List<Layer>(), document.layers) && !Allowed(null, document.layers),
            "Missing root selections are rejected");
        context.True(Allowed(new List<Layer> { lead, tail }, inner.children), "Independent roots may move into a nested group");
        object[] singleArgs = { lead, other.layers, 0, null, -1, -1 };
        context.True(!(bool)Call(window, "TryResolveLayerDrop", singleArgs),
            "Single-layer destination ownership uses the same guard");
        singleArgs = new object[] { lead, document.layers, 1, null, -1, -1 };
        context.True(!(bool)Call(window, "TryResolveLayerDrop", singleArgs), "Single-layer adjacent insertion is a no-op");
        context.True(!Moves(new List<Layer> { lead, outer }, document.layers, 0) &&
            !Moves(new List<Layer> { lead, outer }, document.layers, 1) &&
            !Moves(new List<Layer> { lead, outer }, document.layers, 2),
            "An already contiguous range rejects every insertion slot inside itself");
        context.True(!Moves(new List<Layer> { tail, empty }, document.layers, document.layers.Count),
            "Appending an already final range is a no-op");
        context.True(Moves(new List<Layer> { lead, tail }, document.layers, document.layers.Count),
            "A noncontiguous range can move to the end");
        context.True(Moves(new List<Layer> { lead, outer }, document.layers, 3),
            "A contiguous range can move past its following sibling");
        context.True(Moves(new List<Layer> { deep, tail }, document.layers, 0),
            "Roots from different containers can move together");

        var selected = (List<string>)Field(window, "selectedLayerIds");
        selected.Clear(); selected.Add(deep.Id); selected.Add(outer.Id); selected.Add(lead.Id);
        var selectedRoots = (List<Layer>)Call(window, "GetSelectedRoots");
        context.True(selectedRoots.Count == 2 && selectedRoots[0] == lead && selectedRoots[1] == outer,
            "Selected roots preserve tree order and omit selected descendants of selected parents");
        selected.Clear();

        Call(window, "PerformSelectedLayersDrop", new List<Layer> { lead, tail }, document.layers, document.layers.Count, null);
        context.True(document.layers.Count == 4 && document.layers[0] == outer && document.layers[1] == empty &&
            document.layers[2] == lead && document.layers[3] == tail,
            "A noncontiguous same-container move adjusts the index for all removed roots");
        context.True(outer.children.Count == 2 && outer.children[0] == firstChild && outer.children[1] == inner &&
            inner.children.Count == 1 && inner.children[0] == deep, "Reordering roots preserves nested topology");

        TextureTransform Canvas(Layer layer) => (TextureTransform)Call(document, "GetCanvasTransform", layer);
        void SamePlacement(TextureTransform expected, Layer layer, string message)
        {
            var actual = Canvas(layer);
            foreach (Vector2 uv in new[] { Vector2.zero, Vector2.one, new Vector2(.3f, .7f) })
                context.Near(0d, Vector2.Distance(expected.Map(uv, new Vector2(8f, 8f)),
                    actual.Map(uv, new Vector2(8f, 8f))), .0001d, message);
        }
        var deepBeforeMove = Canvas(deep);
        var tailBeforeMove = Canvas(tail);
        Call(window, "PerformSelectedLayersDrop", new List<Layer> { deep, tail }, outer.children, 1, outer);
        context.True(outer.children.Count == 4 && outer.children[0] == firstChild && outer.children[1] == deep &&
            outer.children[2] == tail && outer.children[3] == inner && inner.children.Count == 0 && !document.layers.Contains(tail),
            "Mixed-parent move preserves selected root order at the requested parent slot");
        SamePlacement(deepBeforeMove, deep, "Moving between transformed groups preserves canvas placement");
        SamePlacement(tailBeforeMove, tail, "Moving a root into a transformed group preserves canvas placement");
        Call(window, "PerformLayerDrop", deep, document.layers, document.layers.Count, null);
        context.True(document.layers[document.layers.Count - 1] == deep && !outer.children.Contains(deep),
            "The single-layer path can move a nested layer out to the root end");
        SamePlacement(deepBeforeMove, deep, "Moving back to root preserves canvas placement");

        var fxType = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.LayerShaderFXView", true);
        var reorder = fxType.GetNestedType("ReorderManipulator", BindingFlags.NonPublic);
        var clamp = reorder.GetMethod("ClampScrollOffset", Flags);
        foreach (float high in new[] { -300f, -1f, 0f, 100f })
            foreach (float offset in new[] { -30f, 0f, 10f, 1000f })
                context.Near(Mathf.Clamp(offset, 0f, Mathf.Max(0f, high)),
                    (float)clamp.Invoke(null, new object[] { offset, 0f, high }), 0d,
                    "FX autoscroll cannot produce a negative or inverted offset");
        scroll.scrollOffset = new Vector2(0f, -20f);
        reorder.GetMethod("NormalizeScrollOffset", Flags).Invoke(null, new object[] { scroll });
        context.True(scroll.scrollOffset.y >= 0f, "Leaving an FX scroll target removes an obsolete negative offset");

        var retainedLayers = new List<Layer>(document.layers);
        try
        {
            document.layers.Clear(); Refresh(); await Settle();
            hierarchy = (VisualElement)Field(window, "toolkitLayerHierarchyRoot");
            view = scroll.contentViewport.worldBound;
            CheckTarget(view.center, document.layers, 0, 0, null, "An empty document accepts root insertion");
            context.True(Field(window, "toolkitLayerEndDropZone") is VisualElement,
                "The empty-document help area retains its root-end target");
        }
        finally { document.layers.AddRange(retainedLayers); }
    });
}
