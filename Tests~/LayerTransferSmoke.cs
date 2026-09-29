using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class LayerTransferSmoke
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string SourceName = "LayerTransferSmoke.Source", TargetName = "LayerTransferSmoke.Target";
    static object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Flags).Invoke(owner, args);
    static object Get(object owner, string field) => owner.GetType().GetField(field, Flags).GetValue(owner);
    static TextureCompositorWindow Window(string name) => Resources.FindObjectsOfTypeAll<TextureCompositorWindow>().Single(w => w.name == name);
    static TextureCompositor Document(TextureCompositorWindow window) => (TextureCompositor)Get(window, "compositor");
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static Layer Fill(string name) => new Layer(new ColorFillLayerBehaviour { color = Color.red }) { layerName = name };
    static Texture2D Pixels(Layer layer) => (Texture2D)layer.Behaviour.GetType().GetProperty("StoredTexture", Flags).GetValue(layer.Behaviour);
    static void Payload(TextureCompositorWindow source, List<Layer> roots)
    {
        DragAndDrop.PrepareStartDrag();
        DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
        DragAndDrop.SetGenericData("DCFApixels.WhimTex.DraggedWindow", source);
        DragAndDrop.SetGenericData("DCFApixels.WhimTex.DraggedCompositorId", Document(source));
        DragAndDrop.SetGenericData("DCFApixels.WhimTex.DraggedLayerId", roots[0].Id);
        DragAndDrop.SetGenericData("DCFApixels.WhimTex.DraggedLayers", roots);
    }
    public static string Setup()
    {
        SessionState.SetInt("WhimTex.LayerTransferSmoke.Focus", EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetHashCode() : 0);
        Undo.IncrementCurrentGroup();SessionState.SetInt("WhimTex.LayerTransferSmoke.Undo",Undo.GetCurrentGroup());
        var source = ScriptableObject.CreateInstance<TextureCompositorWindow>(); source.name = SourceName;
        var target = ScriptableObject.CreateInstance<TextureCompositorWindow>(); target.name = TargetName;
        var a = Document(source); var b = Document(target);
        a.width = a.height = b.width = b.height = 16;
        a.layers.Clear(); b.layers.Clear();
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); texture.Apply();
        var drawing = (DrawingLayerBehaviour)typeof(DrawingLayerBehaviour).GetMethod("FromMergedTexture", Flags).Invoke(null, new object[] { texture });
        Layer paint = drawing; paint.layerName = "Paint";
        var group = new Layer(new GroupLayerBehaviour()) { layerName = "Source Group" };
        group.children.Add(paint); group.transform.position = new Vector2(.1f, .2f);
        var seamless=MakeSeamlessLayerBehaviour.CreateDefault();seamless.mode=MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting;
        seamless.quiltingAlongSearch=.175f;seamless.quiltingSeed=-184;seamless.TargetLayerId=group.Id;
        Layer seamlessLayer=seamless;seamlessLayer.layerName="Source A";
        a.layers.Add(seamlessLayer); a.layers.Add(group);
        b.layers.Add(Fill("Target A"));
        var targetGroup = new Layer(new GroupLayerBehaviour()) { layerName = "Target Group" };
        targetGroup.transform.position = new Vector2(.25f, .1f);
        targetGroup.children.Add(Fill("Target Child"));
        b.layers.Add(targetGroup); b.layers.Add(Fill("Target B"));
        Call(a, "NormalizeModel"); Call(b, "NormalizeModel");
        seamless.TargetLayerId=group.Id;
        target.titleContent = new GUIContent(TargetName);
        target.position = new Rect(100, 100, 1100, 720); target.ShowUtility();
        return "Ready: run Drop with top, before, after, group, end, footer, then Cleanup.";
    }
    public static string Drop(string mode)
    {
        var source = Window(SourceName); var target = Window(TargetName);
        var a = Document(source); var b = Document(target);
        var roots = new List<Layer> { a.layers[0], a.layers[1], a.layers[1].children[0] };
        var scroll = (ScrollView)Get(target, "toolkitSettingsScroll");
        VisualElement element = (VisualElement)Get(target, "toolkitPreviewPane");
        Vector2 point = element.worldBound.center;
        List<Layer> destination = b.layers;
        int index = 0;
        var targetGroup = b.layers.Single(l => l.layerName == "Target Group");
        if (mode == "before" || mode == "after" || mode == "group")
        {
            var layer = mode == "group" ? targetGroup : b.layers.Single(l => l.layerName == "Target B");
            element = scroll.Query<VisualElement>(className: "whimtex-layer-row").ToList().Single(e => (string)e.userData == layer.Id);
            point = new Vector2(element.worldBound.center.x, mode == "group" ? element.worldBound.center.y :
                mode == "before" ? element.worldBound.yMin + 2 : element.worldBound.yMax - 2);
            destination = mode == "group" ? targetGroup.children : b.layers;
            index = mode == "group" ? 0 : b.layers.IndexOf(layer) + (mode == "after" ? 1 : 0);
        }
        else if (mode == "end")
        {
            element = (VisualElement)Get(target, "toolkitLayerEndDropZone");
            point = element.worldBound.center; index = b.layers.Count;
        }
        else if (mode == "footer")
        {
            element = (VisualElement)Get(target, "toolkitLayerFooter"); point = element.worldBound.center;
        }
        Check(element.panel != null && element.worldBound.width > 0, "UI laid out");
        string clipboard = GUIUtility.systemCopyBuffer;
        string sourceBefore = JsonUtility.ToJson(a);
        int oldCount = destination.Count, oldRootCount = b.layers.Count;
        Payload(source, roots);
        using (var exit = DragExitedEvent.GetPooled(new Event { type = EventType.DragExited }))
            Call(source, "OnToolkitDragExited", exit);
        Check((bool)Call(target, "IsCrossWindowLayerDrag"), "Leaving source window retains payload");
        using (var update = DragUpdatedEvent.GetPooled(new Event { type = EventType.DragUpdated, mousePosition = point }))
        { update.target = element; element.SendEvent(update); }
        Check(DragAndDrop.visualMode == DragAndDropVisualMode.Copy, "Foreign layers use Copy cursor");
        using (var perform = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = point }))
        { perform.target = element; element.SendEvent(perform); }
        Check(destination.Count == oldCount + 2, "Copies two roots, without duplicating selected descendant: " + mode);
        Check(destination[index].layerName == "Source A" && destination[index + 1].layerName == "Source Group", "Correct insertion order: " + mode);
        var copiedGroup = destination[index + 1];
        var copiedEffect=(MakeSeamlessLayerBehaviour)destination[index].Behaviour;
        Check(copiedEffect.quiltingAlongSearch==.175f&&copiedEffect.quiltingSeed==-184&&copiedEffect.TargetLayerId==copiedGroup.Id,$"Quilting settings and copied target preserved: along={copiedEffect.quiltingAlongSearch}, seed={copiedEffect.quiltingSeed}, target={copiedEffect.TargetLayerId}, group={copiedGroup.Id}, sourceTarget={((MakeSeamlessLayerBehaviour)a.layers[0].Behaviour).TargetLayerId}");
        Check(copiedGroup.Id != a.layers[1].Id && copiedGroup.children[0].Id != a.layers[1].children[0].Id, "Independent IDs");
        Check(Pixels(copiedGroup.children[0]) != Pixels(a.layers[1].children[0]), "Independent Drawing storage");
        Check(Pixels(copiedGroup.children[0]).GetPixel(0, 0) == Color.red, "Drawing pixels preserved");
        Check(sourceBefore == JsonUtility.ToJson(a), "Source unchanged");
        Check(clipboard == GUIUtility.systemCopyBuffer, "Clipboard unchanged");
        Check((bool)Get(target, "temporaryDocumentDirty"), "Destination dirty");
        Check((string)Get(target, "selectedLayerId") == copiedGroup.Id, "Copies selected");
        Check(DragAndDrop.GetGenericData("DCFApixels.WhimTex.DraggedLayers") == null, "Payload cleared after drop");
        if (mode == "group")
        {
            var sourceTransform = (TextureTransform)Call(a, "GetCanvasTransform", a.layers[1]);
            var copiedTransform = (TextureTransform)Call(b, "GetCanvasTransform", copiedGroup);
            foreach (var uv in new[] { Vector2.zero, Vector2.one, new Vector2(.3f, .7f) })
                Check(Vector2.Distance(sourceTransform.Map(uv, new Vector2(16, 16)), copiedTransform.Map(uv, new Vector2(16, 16))) < .0001f,
                    "Canvas placement preserved in transformed parent");
            Check(b.layers.Count == oldRootCount, "Group insertion leaves root stack intact");
        }
        if (mode == "top")
        {
            string id = copiedGroup.Id;
            Undo.PerformUndo();
            Check(b.layers.Count == oldCount && Call(b, "FindLayer", id) == null, "One Undo removes entire transfer");
            Undo.PerformRedo();
            Check(b.layers.Count == oldCount + 2 && Call(b, "FindLayer", id) != null, "Redo restores transfer");
            Check(Pixels(((Layer)Call(b, "FindLayer", id)).children[0]).GetPixel(0, 0) == Color.red, "Redo restores Drawing pixels");
        }
        return "PASS " + mode;
    }
    public static string Guards()
    {
        var source = Window(SourceName); var target = Window(TargetName);
        var a = Document(source); var b = Document(target);
        var root = a.layers[0];
        Payload(source, new List<Layer> { root });
        Check(!(bool)Call(source, "IsCrossWindowLayerDrag"), "Source window keeps move semantics");
        typeof(TextureCompositorWindow).GetField("compositor", Flags).SetValue(target, a);
        try { Check((bool)Call(target, "IsCrossWindowLayerDrag"), "Another window of same document still copies"); }
        finally { typeof(TextureCompositorWindow).GetField("compositor", Flags).SetValue(target, b); }
        var scroll = (ScrollView)Get(target, "toolkitSettingsScroll");
        var autoScroll = Get(target, "layerDragAutoScroll");
        var viewport = scroll.contentViewport.worldBound;
        Call(autoScroll, "UpdatePointer", new Vector2(viewport.center.x, viewport.yMin + 1));
        Check((bool)Get(autoScroll, "running"), "Cross-window drag starts edge autoscroll");
        Call(autoScroll, "Stop");
        var element = (VisualElement)Get(target, "toolkitPreviewPane");
        var point = element.worldBound.center;
        using (var perform = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = point }))
        { perform.target = element; element.SendEvent(perform); }
        Check(b.layers[0].layerName == root.layerName && b.layers[0].Id != root.Id, "Single layer copied");
        Payload(source, new List<Layer> { Fill("Invalid detached layer") });
        int count = b.layers.Count;
        using (var update = DragUpdatedEvent.GetPooled(new Event { type = EventType.DragUpdated, mousePosition = point }))
        { update.target = element; element.SendEvent(update); }
        Check(DragAndDrop.visualMode == DragAndDropVisualMode.Rejected, "Stale layer rejected");
        using (var perform = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = point }))
        { perform.target = element; element.SendEvent(perform); }
        Check(b.layers.Count == count, "Invalid drop does not mutate destination");
        Payload(source, new List<Layer> { root });
        Call(source, "PerformSelectedLayersDrop", new List<Layer> { root }, a.layers, a.layers.Count, null);
        Check(a.layers.Count == 2 && a.layers[1] == root, "Local drag still moves original layer");
        using (var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
        { escape.target = target.rootVisualElement; target.rootVisualElement.SendEvent(escape); }
        Check(DragAndDrop.GetGenericData("DCFApixels.WhimTex.DraggedLayers") == null, "Escape clears drag payload");
        return "PASS: single copy, local move, same-document window detection, autoscroll, stale payload rejection, Escape.";
    }
    public static string Cleanup()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>().Where(w => w.name == SourceName || w.name == TargetName))
        {
            Undo.ClearUndo(Document(window));
            typeof(EditorWindow).GetProperty("hasUnsavedChanges", Flags).SetValue(window, false);
            if(window.rootVisualElement.panel!=null)window.Close();else UnityEngine.Object.DestroyImmediate(window);
        }
        Undo.RevertAllDownToGroup(SessionState.GetInt("WhimTex.LayerTransferSmoke.Undo",Undo.GetCurrentGroup()));
        var previous=Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w=>w.GetHashCode()==SessionState.GetInt("WhimTex.LayerTransferSmoke.Focus",0));
        if(previous!=null)previous.Focus();
        return "Temporary windows removed.";
    }
}
