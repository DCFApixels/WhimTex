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
using WhimTex.Tests.UnityD;

public static class TextPointHandleTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static void Write(object value, string name, object next) => value.GetType().GetField(name, Flags).SetValue(value, next);
    static object Call(object value, string name, params object[] args) => Array.Find(value.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static IReadOnlyList<Vector3> Lines(TextLayerBehaviour text) => (IReadOnlyList<Vector3>)Call(text, "GetPointLines", 512, 512);
    static Vector2 Anchor(TextLayerBehaviour text) => (Vector2)Call(text, "GetPointAnchor", 512, 512);
    static void Near(TestContext t, Vector2 expected, Vector2 actual, string label)
    { t.Near(expected.x, actual.x, .02, label + " X"); t.Near(expected.y, actual.y, .02, label + " Y"); }
    static void Pointer(VisualElement target, EventType type, Vector2 point, EventModifiers modifiers = EventModifiers.None)
    {
        var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(point), modifiers = modifiers };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    static void Escape(VisualElement target)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
        e.target = target; target.SendEvent(e);
    }
    static Vector2 Handle(WhimTexWindow window, WhimTexDocument document, Layer layer)
    {
        var canvas = (VisualElement)Read(window, "toolkitCanvas");
        var transform = (TextureTransform)Call(document, "GetCanvasTransform", layer);
        Vector2 uv = transform.Map(Vector2.one * .5f + Anchor((TextLayerBehaviour)layer.Behaviour) / 512, Vector2.one * 512);
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height));
    }
    public static string Start(string id) => AsyncD.Start(id, Execute);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);
    static async Task Execute(TestContext t, CancellationToken token)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 512;
        var text = new TextLayerBehaviour { text = "Point Text\nShort\nThird line", fontSize = 30 };
        var layer = new Layer(text); Call(layer, "AssignNewId"); document.layers.Add(layer);
        WhimTexWindow window = null;
        string pref = "DCFApixels.WhimTex.Canvas.Tool"; bool hadPref = EditorPrefs.HasKey(pref); string oldPref = EditorPrefs.GetString(pref);
        try
        {
            foreach (TextEdgeMode mode in Enum.GetValues(typeof(TextEdgeMode)))
            foreach (TextAnchor alignment in Enum.GetValues(typeof(TextAnchor)))
            {
                text.edgeMode = mode; text.alignment = alignment;
                var lines = Lines(text);
                t.Equal(3, lines.Count, "One underline per logical line " + mode + "/" + alignment);
                t.True(lines[0].y - lines[0].x > lines[1].y - lines[1].x, "Shorter text has a shorter underline");
                t.True(lines[0].z > lines[1].z && lines[1].z > lines[2].z, "Baselines follow paragraph order");
                Vector2 anchor = Anchor(text);
                Near(t, new Vector2(0, lines[0].z), anchor, "Anchor stays on first line at aligned local X");
                foreach (var line in lines)
                    t.Near(0, Mathf.Lerp(line.x, line.y, (int)alignment % 3 * .5f), .01, "All lines share the alignment axis");
            }
            text.alignment = TextAnchor.UpperLeft; text.edgeMode = TextEdgeMode.HintedAA;
            float width = Lines(text)[0].y - Lines(text)[0].x;
            float step = Lines(text)[0].z - Lines(text)[1].z;
            text.characterHorizontalScale = 1.5f;
            t.Near(width * 1.5f, Lines(text)[0].y - Lines(text)[0].x, .01, "Underlines account for character scale");
            text.spacing = new TextSpacing { line = .2f, paragraph = .3f, character = .1f, word = .2f };
            t.Near(step + 15, Lines(text)[0].z - Lines(text)[1].z, .01, "Baselines account for line and paragraph spacing");
            text.casing = TextCasing.SmallCaps;
            t.True(Lines(text)[0].y > Lines(text)[0].x, "Small caps produces line geometry");
            text.text = "\nX\n";
            t.Equal(3, Lines(text).Count, "Empty paragraphs retain their baseline slots");
            t.Near(0, Lines(text)[0].y - Lines(text)[0].x, .01, "Blank first line does not invent an underline");
            Near(t, new Vector2(0, Lines(text)[0].z), Anchor(text), "Blank first line retains a usable anchor");
            text.text = "";
            t.Equal(1, Lines(text).Count, "Empty text retains the initial baseline");
            text.layoutMode = TextLayoutMode.Frame;
            t.Equal(0, Lines(text).Count, "Frame does not expose Point underlines");
            text.layoutMode = TextLayoutMode.Point; text.text = "Point Text\nShort\nThird line";
            text.characterHorizontalScale = 1; text.spacing = default; text.casing = TextCasing.Normal;
            window = ScriptableObject.CreateInstance<WhimTexWindow>(); Write(window, "activeDocument", document);
            window.ShowUtility(); window.position = new Rect(80, 80, 1200, 780); window.CreateGUI();
            var root = window.rootVisualElement;
            for (int i = 0; i < 4; i++) await AsyncD.Tick(root, token);
            var canvas = (VisualElement)Read(window, "toolkitCanvas");
            object tool = Read(window, "canvasTool"); Call(window, "SetCanvasTool", Enum.Parse(tool.GetType(), "Text"));
            Call(window, "SelectOnlyLayer", layer.Id); Call(window, "RefreshToolkitInterface", false);
            await AsyncD.Tick(root, token);
            foreach (var alignment in new[] { TextAnchor.UpperLeft, TextAnchor.UpperCenter, TextAnchor.UpperRight })
            {
                text.alignment = alignment; Call(window, "RefreshToolkitInterface", false);
                Call(window, "RequestCanvasRender", true); await AsyncD.Tick(root, token); await AsyncD.Tick(root, token);
                Vector2 p = Handle(window, document, layer); var original = layer.transform; Vector2 originalOffset = text.layoutOffset;
                Pointer(canvas, EventType.MouseDown, p); Pointer(canvas, EventType.MouseUp, p);
                t.True(layer.transform.Equals(original) && Read(window, "textEditingLayer") == null && document.layers.Count == 1,
                    "A click on the anchor neither moves nor edits nor creates text");
                Pointer(canvas, EventType.MouseDown, p);
                Pointer(canvas, EventType.MouseDrag, p + new Vector2(36, -24), EventModifiers.Control);
                Near(t, p + new Vector2(36, -24), Handle(window, document, layer), "Drag updates the visible position before mouse up");
                t.True(layer.transform.Equals(original) && text.layoutOffset != originalOffset, "Drag changes layout offset without touching the layer transform");
                Pointer(canvas, EventType.MouseUp, p + new Vector2(36, -24), EventModifiers.Control);
                t.True(!canvas.HasPointerCapture(PointerId.mousePointerId), "Successful move releases pointer capture");
                t.True(text.fontSize == 30 && text.layoutMode == TextLayoutMode.Point && text.text == "Point Text\nShort\nThird line", "Moving does not alter content or layout");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await AsyncD.Tick(root, token);
                layer = document.layers[0]; text = (TextLayerBehaviour)layer.Behaviour;
                t.True(layer.transform.Equals(original) && text.layoutOffset == originalOffset, "One Undo restores layout position, leaving transform unchanged");
                Undo.PerformRedo(); await AsyncD.Tick(root, token);
                layer = document.layers[0]; text = (TextLayerBehaviour)layer.Behaviour;
                Near(t, p + new Vector2(36, -24), Handle(window, document, layer), "Redo restores the move");
                t.True(layer.transform.Equals(original), "Redo still leaves transform unchanged");
                text.layoutOffset = originalOffset; Call(window, "RefreshToolkitInterface", false); await AsyncD.Tick(root, token);
            }
            Vector2 handle = Handle(window, document, layer); var saved = layer.transform; Vector2 savedOffset = text.layoutOffset;
            Pointer(canvas, EventType.MouseDown, handle); Pointer(canvas, EventType.MouseDrag, handle + new Vector2(50, 30), EventModifiers.Control);
            Escape(canvas); Pointer(canvas, EventType.MouseUp, handle + new Vector2(50, 30));
            t.True(layer.transform.Equals(saved) && text.layoutOffset == savedOffset && document.layers.Count == 1 && !canvas.HasPointerCapture(PointerId.mousePointerId), "Escape restores layout without creating text");
            await AsyncD.Tick(root, token); handle = Handle(window, document, layer);
            Pointer(canvas, EventType.MouseDown, handle); Pointer(canvas, EventType.MouseDrag, handle + new Vector2(50, 30), EventModifiers.Control);
            canvas.ReleasePointer(PointerId.mousePointerId);
            Pointer(canvas, EventType.MouseDrag, handle + new Vector2(50, 30), EventModifiers.Control);
            await AsyncD.Tick(root, token);
            t.True(layer.transform.Equals(saved) && text.layoutOffset == savedOffset, "Lost capture restores layout; capture=" + canvas.HasPointerCapture(PointerId.mousePointerId) +
                "; dragging=" + Read(window, "textManipulator").GetType().GetProperty("IsDragging", Flags).GetValue(Read(window, "textManipulator")));
            handle = Handle(window, document, layer);
            Pointer(canvas, EventType.MouseDown, handle); Pointer(canvas, EventType.MouseDrag, handle + new Vector2(50, 30), EventModifiers.Control);
            var external = layer.transform; external.rotation += 10; layer.transform = external;
            Pointer(canvas, EventType.MouseUp, handle + new Vector2(50, 30));
            t.True(layer.transform.Equals(external) && text.layoutOffset == savedOffset && document.layers.Count == 1, "Stale move restores its offset without overwriting an external transform");
            layer.transform = saved; await AsyncD.Tick(root, token);
            handle = Handle(window, document, layer);
            Pointer(canvas, EventType.MouseDown, handle); Pointer(canvas, EventType.MouseDrag, handle + new Vector2(50, 30), EventModifiers.Control);
            text.text = "Changed elsewhere"; Pointer(canvas, EventType.MouseUp, handle + new Vector2(50, 30));
            t.True(layer.transform.Equals(saved) && text.layoutOffset == savedOffset && text.text == "Changed elsewhere", "Layout change cancels only our move and preserves the new text");
            await AsyncD.Tick(root, token); handle = Handle(window, document, layer);
            Pointer(canvas, EventType.MouseDown, handle); Pointer(canvas, EventType.MouseDrag, handle + new Vector2(20, 15), EventModifiers.Control);
            Vector2 externalOffset = text.layoutOffset + new Vector2(4, 5); text.layoutOffset = externalOffset;
            Pointer(canvas, EventType.MouseUp, handle + new Vector2(20, 15));
            t.True(text.layoutOffset == externalOffset && layer.transform.Equals(saved), "A newer external layout offset is not overwritten on cancellation");
            text.layoutOffset = savedOffset;
            text.text = "Point Text\nShort\nThird line";
            var group = new Layer(new GroupLayerBehaviour()); Call(group, "AssignNewId");
            document.layers.Clear(); group.children.Add(layer); document.layers.Add(group);
            var parentTransform = TextureTransform.Default; parentTransform.rotation = 23; parentTransform.scale = new Vector2(.8f, 1.2f); group.transform = parentTransform;
            var childTransform = TextureTransform.Default; childTransform.rotation = -17; layer.transform = childTransform;
            Call(window, "SelectOnlyLayer", layer.Id); Call(window, "RefreshToolkitInterface", false); await AsyncD.Tick(root, token);
            handle = Handle(window, document, layer);
            Pointer(canvas, EventType.MouseDown, handle + new Vector2(3, 2));
            Pointer(canvas, EventType.MouseDrag, handle + new Vector2(38, -18), EventModifiers.Control);
            Near(t, handle + new Vector2(35, -20), Handle(window, document, layer), "Transformed parent uses canvas delta and retains pointer grab offset");
            Pointer(canvas, EventType.MouseUp, handle + new Vector2(38, -18), EventModifiers.Control);
            t.True(group.transform.Equals(parentTransform) && layer.transform.Equals(childTransform), "Dragging changes neither child nor parent transform");
            await AsyncD.Tick(root, token); handle = Handle(window, document, layer); saved = layer.transform; savedOffset = text.layoutOffset;
            Pointer(canvas, EventType.MouseDown, handle); Pointer(canvas, EventType.MouseDrag, handle + new Vector2(50, 30), EventModifiers.Control);
            Call(window, "SelectOnlyLayer", group.Id); Call(window, "RefreshToolkitInterface", false);
            Pointer(canvas, EventType.MouseUp, handle + new Vector2(50, 30));
            t.True(layer.transform.Equals(saved) && text.layoutOffset == savedOffset && group.children.Count == 1, "Selection change cancels only the in-progress layout move");
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
