using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class TextFrameHandleTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Vector2[] Grips = { new(0,0), new(1,0), new(1,1), new(0,1), new(.5f,0), new(1,.5f), new(.5f,1), new(0,.5f), new(.5f,.5f) };
    static object Read(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    static object Call(object obj, string name, params object[] args) => Array.Find(obj.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(obj, args);
    static void Near(TestContext t, Vector2 a, Vector2 b, string label)
    { t.Near(a.x, b.x, .02, label + " X"); t.Near(a.y, b.y, .02, label + " Y"); }
    static Vector2 View(WhimTexWindow window, WhimTexDocument document, Layer layer, Vector2 local)
    {
        var canvas = (VisualElement)Read(window, "toolkitCanvas");
        var dimensions = new Vector2(document.width, document.height);
        var transform = (TextureTransform)Call(document, "GetCanvasTransform", layer);
        Vector2 uv = transform.Map(Vector2.one * .5f + Vector2.Scale(local, new Vector2(1 / dimensions.x, 1 / dimensions.y)), dimensions);
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height));
    }
    static void Pointer(VisualElement target, EventType type, Vector2 point, EventModifiers modifiers = EventModifiers.None)
    {
        var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(point), modifiers = modifiers };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    static void Escape(VisualElement target)
    { using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }); e.target = target; target.SendEvent(e); }
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);
    static async Task Execute(TestContext t, CancellationToken token)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 512;
        var text = new TextLayerBehaviour { text = "Framed text that wraps\nSecond paragraph", fontSize = 22, layoutMode = TextLayoutMode.Frame };
        var layer = new Layer(text); Call(layer, "AssignNewId"); document.layers.Add(layer);
        var group = new Layer(new GroupLayerBehaviour()); Call(group, "AssignNewId");
        var offset = new Vector2(13.37f, -6.28f); var size = new Vector2(200, 90);
        WhimTexWindow window = null;
        const string pref = "DCFApixels.WhimTex.Canvas.Tool"; bool hadPref = EditorPrefs.HasKey(pref); string oldPref = EditorPrefs.GetString(pref);
        try
        {
            text.layoutOffset = offset; text.frameSize = size;
            window = ScriptableObject.CreateInstance<WhimTexWindow>(); Write(window, "activeDocument", document);
            window.ShowUtility(); window.position = new Rect(80,80,1200,780); window.CreateGUI();
            var root = window.rootVisualElement;
            for (int n = 0; n < 4; n++) await AsyncD.Tick(root, token);
            var canvas = (VisualElement)Read(window, "toolkitCanvas");
            Call(window, "SetCanvasTool", Enum.Parse(Read(window, "canvasTool").GetType(), "Text"));
            Call(window, "SelectOnlyLayer", layer.Id); Call(window, "RefreshToolkitInterface", false);
            await AsyncD.Tick(root, token);
            for (int parent = 0; parent < 2; parent++)
            {
                if (parent == 1)
                {
                    document.layers.Clear(); group.children.Add(layer); document.layers.Add(group);
                    var value = TextureTransform.Default; value.rotation = 23; value.scale = new Vector2(.8f, 1.2f); group.transform = value;
                    value = TextureTransform.Default; value.rotation = -11; layer.transform = value;
                    Call(window, "RefreshToolkitInterface", false); await AsyncD.Tick(root, token);
                }
                for (int i = 0; i < Grips.Length; i++)
                {
                    text.layoutOffset = offset; text.frameSize = size;
                    var transform = layer.transform; var parentTransform = group.transform;
                    Vector2 local = offset + Vector2.Scale(Grips[i] - Vector2.one * .5f, size);
                    Vector2 delta = new Vector2(Grips[i].x == 0 ? -13 : 17, Grips[i].y == 0 ? -11 : 19);
                    Vector2 grab = new Vector2(2, 1), a = View(window, document, layer, local), b = View(window, document, layer, local + delta);
                    Pointer(canvas, EventType.MouseDown, a + grab);
                    t.True(canvas.HasPointerCapture(PointerId.mousePointerId), "Frame handle captures " + parent + "/" + i);
                    Pointer(canvas, EventType.MouseDrag, b + grab, EventModifiers.Control);
                    Vector2 expectedSize = size, expectedOffset = offset;
                    if (i == 8) expectedOffset += delta;
                    else
                    {
                        if (Grips[i].x != .5f) { expectedSize.x += Grips[i].x == 0 ? -delta.x : delta.x; expectedOffset.x += delta.x * .5f; }
                        if (Grips[i].y != .5f) { expectedSize.y += Grips[i].y == 0 ? -delta.y : delta.y; expectedOffset.y += delta.y * .5f; }
                    }
                    Near(t, expectedSize, text.frameSize, "Live frame size " + i);
                    Near(t, expectedOffset, text.layoutOffset, "Live frame offset " + i);
                    t.True(layer.transform.Equals(transform) && group.transform.Equals(parentTransform), "Handle changes no child/parent Transform");
                    t.True(text.fontSize == 22 && text.characterHorizontalScale == 1, "Frame resize reflows rather than stretching glyphs");
                    Pointer(canvas, EventType.MouseUp, b + grab, EventModifiers.Control);
                    t.True(!canvas.HasPointerCapture(PointerId.mousePointerId) && Read(window, "textEditingLayer") == null, "Handle neither starts text input nor retains capture");
                    t.Equal(1, document.layers.Count, "Handles never create additional layers");
                }
            }
            document.layers.Clear(); group.children.Clear(); document.layers.Add(layer); layer.transform = TextureTransform.Default;
            text.frameSize = size; text.layoutOffset = offset;
            Call(window, "RefreshToolkitInterface", false); await AsyncD.Tick(root, token);
            foreach (int grip in new[] { 2, 5, 8 })
            {
                Undo.ClearUndo(document);
                Vector2 a = View(window, document, layer, offset + Vector2.Scale(Grips[grip] - Vector2.one * .5f, size));
                Pointer(canvas, EventType.MouseDown, a); Pointer(canvas, EventType.MouseDrag, a + new Vector2(35, -21), EventModifiers.Control);
                Pointer(canvas, EventType.MouseUp, a + new Vector2(35, -21), EventModifiers.Control);
                Vector2 changedOffset = text.layoutOffset, changedSize = text.frameSize;
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
                layer = document.layers[0]; text = (TextLayerBehaviour)layer.Behaviour;
                Near(t, offset, text.layoutOffset, "One Undo restores offset"); Near(t, size, text.frameSize, "One Undo restores size");
                Undo.PerformRedo(); await AsyncD.Tick(root, token);
                layer = document.layers[0]; text = (TextLayerBehaviour)layer.Behaviour;
                Near(t, changedOffset, text.layoutOffset, "Redo offset"); Near(t, changedSize, text.frameSize, "Redo size");
                t.True(layer.transform.Equals(TextureTransform.Default), "Undo/Redo never inserts a Transform");
                text.layoutOffset = offset; text.frameSize = size;
                a = View(window, document, layer, offset + Vector2.Scale(Grips[grip] - Vector2.one * .5f, size));
                Pointer(canvas, EventType.MouseDown, a); Pointer(canvas, EventType.MouseDrag, a + new Vector2(40, 20), EventModifiers.Control);
                Escape(canvas); Pointer(canvas, EventType.MouseUp, a + new Vector2(40, 20));
                Near(t, offset, text.layoutOffset, "Escape restores offset"); Near(t, size, text.frameSize, "Escape restores size");
            }
            Vector2 corner = View(window, document, layer, offset + size * .5f);
            Pointer(canvas, EventType.MouseDown, corner); Pointer(canvas, EventType.MouseDrag, corner + new Vector2(40,-10), EventModifiers.Control | EventModifiers.Shift);
            t.Near(size.x / size.y, text.frameSize.x / text.frameSize.y, .001, "Shift corner resize preserves frame proportions");
            Escape(canvas); Pointer(canvas, EventType.MouseUp, corner);
            Vector2 center = View(window, document, layer, offset);
            Pointer(canvas, EventType.MouseDown, center); Pointer(canvas, EventType.MouseDrag, center + new Vector2(40,10), EventModifiers.Control | EventModifiers.Shift);
            t.Near(offset.y, text.layoutOffset.y, .01, "Shift center drag constrains movement to one canvas axis");
            Escape(canvas); Pointer(canvas, EventType.MouseUp, center);
            Pointer(canvas, EventType.MouseDown, corner); Pointer(canvas, EventType.MouseDrag, corner + new Vector2(40,-20), EventModifiers.Control);
            canvas.ReleasePointer(PointerId.mousePointerId); Pointer(canvas, EventType.MouseDrag, corner + new Vector2(40,-20), EventModifiers.Control);
            await AsyncD.Tick(root, token);
            Near(t, offset, text.layoutOffset, "Capture loss restores offset"); Near(t, size, text.frameSize, "Capture loss restores size");
            Pointer(canvas, EventType.MouseDown, corner); Pointer(canvas, EventType.MouseDrag, corner + new Vector2(40,-20), EventModifiers.Control);
            Vector2 externalSize = new Vector2(177, 66); text.frameSize = externalSize;
            Pointer(canvas, EventType.MouseUp, corner + new Vector2(40,-20), EventModifiers.Control);
            t.Equal(externalSize, text.frameSize, "Cancellation preserves newer externally edited size");
            Near(t, offset, text.layoutOffset, "Cancellation restores only the owned offset");
            text.frameSize = size;
            Vector2 opposite = View(window, document, layer, offset - size * .5f - Vector2.one * 10);
            Pointer(canvas, EventType.MouseDown, corner); Pointer(canvas, EventType.MouseDrag, opposite, EventModifiers.Control);
            Near(t, Vector2.one, text.frameSize, "Crossing the opposite corner clamps to one pixel without flipping");
            Escape(canvas); Pointer(canvas, EventType.MouseUp, opposite);
            root.Q<Vector2Field>("textFrameSize").value = new Vector2(210, 100);
            t.True(layer.transform.Equals(TextureTransform.Default) && text.layoutOffset == offset, "Frame Size properties are independent of Transform and center");
            root.Q<EnumField>("textLayoutMode").value = TextLayoutMode.Point;
            root.Q<EnumField>("textLayoutMode").value = TextLayoutMode.Frame;
            t.True(layer.transform.Equals(TextureTransform.Default) && text.layoutOffset == offset, "Layout switches do not move Transform or layout offset");
            window.DiscardChanges();
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (window != null) { window.DiscardChanges(); window.Close(); } },
                () => { if (document != null) Undo.ClearUndo(document); },
                () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); },
                () => { if (hadPref) EditorPrefs.SetString(pref, oldPref); else EditorPrefs.DeleteKey(pref); });
        }
    }
}
