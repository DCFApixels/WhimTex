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
using Object = UnityEngine.Object;

public static class LightSelectionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static object Get(object obj, string name) => obj.GetType().GetField(name, F).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, F).SetValue(obj, value);
    static void Tool(object obj, string value) => Set(obj, "canvasTool", Enum.Parse(obj.GetType().GetField("canvasTool", F).FieldType, value));

    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    static async Task Execute(TestContext t, CancellationToken token)
    {
        WhimTexWindow window = null;
        EditorWindow host = null;
        WhimTexDocument doc = null, other = null;
        PointerManipulator manipulator = null;
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            doc = (WhimTexDocument)Get(window, "activeDocument"); doc.width = doc.height = 128;
            doc.layers.Clear(); doc.lights.Clear();
            var a = doc.AddLight(WhimTexLightType.Point); a.position = new Vector3(20, 20, 32); a.color = new Color(3, 2, 1);
            var b = doc.AddLight(WhimTexLightType.Directional); b.position = new Vector3(60, 60, 32);
            var c = doc.AddLight(WhimTexLightType.Spot); c.position = new Vector3(100, 100, 32);
            foreach (string field in new[] { "brushesExpanded", "commentsExpanded", "postFxExpanded", "uvExpanded" }) Set(window, field, false);
            Set(window, "lightsExpanded", true); Tool(window, "Lights");
            host = ScriptableObject.CreateInstance<EditorWindow>();
            host.titleContent = new GUIContent("Light selection verification"); host.position = new Rect(100, 100, 400, 720);
            Call(window, "BuildLightsDrawer", host.rootVisualElement); Call(window, "RefreshLightsPanel");
            host.ShowUtility(); await AsyncD.Tick(host.rootVisualElement, token);
            var selected = (HashSet<string>)Get(window, "selectedLightIds");
            void Selection(params string[] expected) => t.True(selected.SetEquals(expected), "Selected IDs: " + string.Join(",", expected) + "; actual " + string.Join(",", selected));
            void Select(string id, bool toggle = false) => Call(window, "SelectLight", id, toggle);
            string initial = JsonUtility.ToJson(doc);
            Selection(a.id);
            Select(b.id, true); Selection(a.id, b.id);
            t.Equal(b.id, (string)Get(window, "selectedLightId"), "Last added source is active");
            Select(a.id, true); Selection(b.id);
            Select(c.id); Selection(c.id);
            Select(c.id, true); Selection(); Call(window, "RefreshLightsPanel"); Selection();
            t.Equal(initial, JsonUtility.ToJson(doc), "Selection does not change document contents");

            var canvasType = typeof(WhimTexWindow).GetNestedType("CanvasElement", F);
            var canvas = (VisualElement)Activator.CreateInstance(canvasType, F, null, new[] { Get(window, "canvasViewport") }, null);
            Set(window, "toolkitCanvas", canvas); Set(canvas, "documentWidth", 128); Set(canvas, "documentHeight", 128);
            canvasType.GetProperty("ImageRect", F).SetValue(canvas, new Rect(0, 0, 128, 128));
            Call(window, "BuildCanvasTransformTool");
            Call(window, "BuildLightsCanvasTool"); manipulator = (PointerManipulator)Get(window, "lightManipulator");
            canvas.style.width = 128; canvas.style.height = 128; canvas.style.flexShrink = 0;
            host.rootVisualElement.Add(canvas); await AsyncD.Tick(host.rootVisualElement, token);
            canvasType.GetProperty("ImageRect", F).SetValue(canvas, new Rect(0, 0, 128, 128));
            t.Equal("Lights", Get(window, "canvasTool").ToString(), "Fixture uses Lights canvas tool");
            void Pointer(VisualElement target, Vector2 local, EventType type, EventModifiers modifiers = EventModifiers.None)
            {
                var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(local), modifiers = modifiers };
                if (type == EventType.MouseDown) { using var evt = PointerDownEvent.GetPooled(input); evt.target = target; target.SendEvent(evt); }
                else if (type == EventType.MouseUp) { using var evt = PointerUpEvent.GetPooled(input); evt.target = target; target.SendEvent(evt); }
                else { using var evt = PointerMoveEvent.GetPooled(input); evt.target = target; target.SendEvent(evt); }
            }
            void Click(WhimTexLight light, EventModifiers modifiers = EventModifiers.None)
            {
                Vector2 view = (Vector2)Call(manipulator, "View", (Vector2)light.position);
                Pointer(canvas, view, EventType.MouseDown, modifiers);
                Pointer(canvas, view, EventType.MouseUp, modifiers);
            }
            Click(a); Selection(a.id);
            Click(b, EventModifiers.Control); Selection(a.id, b.id);
            Click(a, EventModifiers.Control); Selection(b.id);
            Click(c, EventModifiers.Command); Selection(b.id, c.id);
            Click(a); Selection(a.id);

            async Task ListClick(WhimTexLight light, EventModifiers modifiers)
            {
                await AsyncD.Tick(host.rootVisualElement, token);
                var content = (VisualElement)Get(window, "lightsContent");
                var button = content.Query<Button>().ToList().Find(item => item.text == light.name);
                t.True(button != null, "Source list button exists");
                var point = button.contentRect.center;
                Pointer(button, point, EventType.MouseDown, modifiers); Pointer(button, point, EventType.MouseUp, modifiers);
            }
            await ListClick(b, EventModifiers.Control); Selection(a.id, b.id);
            await ListClick(a, EventModifiers.Control); Selection(b.id);
            await ListClick(c, EventModifiers.None); Selection(c.id);

            canvasType.GetProperty("ImageRect", F).SetValue(canvas, new Rect(0, 0, 128, 128));
            Vector2 grip = (Vector2)Call(manipulator, "Handle", c);
            Vector2 anchor = (Vector2)Call(manipulator, "View", (Vector2)c.position);
            t.True(Vector2.Distance(grip, anchor) >= 23.9f, "Spot aiming grip does not overlap downward source");
            Pointer(canvas, grip, EventType.MouseDown); Pointer(canvas, grip + new Vector2(12, -8), EventType.MouseDrag);
            Pointer(canvas, grip + new Vector2(12, -8), EventType.MouseUp);
            t.True(Vector3.Distance(new Vector3(12, 8, -32).normalized, c.direction) < .0001f, "Spot aims at dragged canvas target");
            Undo.PerformUndo(); c = doc.lights.Find(item => item.id == c.id);
            t.Equal(Vector3.back, c.direction, "Undo restores Spot direction");
            Call(window, "RefreshLightsPanel");
            grip = (Vector2)Call(manipulator, "RangeHandle", c);
            Pointer(canvas, grip, EventType.MouseDown); Pointer(canvas, grip + new Vector2(10, 0), EventType.MouseDrag);
            Pointer(canvas, grip + new Vector2(10, 0), EventType.MouseUp);
            t.Near(138, c.range, .001, "Spot range handle changes distance");
            Undo.PerformUndo(); c = doc.lights.Find(item => item.id == c.id);
            t.Equal(128f, c.range, "Undo restores Spot range");
            a = doc.lights.Find(item => item.id == a.id); b = doc.lights.Find(item => item.id == b.id);

            Select(a.id); Select(b.id, true);
            await AsyncD.Tick(host.rootVisualElement, token);
            canvasType.GetProperty("ImageRect", F).SetValue(canvas, new Rect(0, 0, 128, 128));
            Vector3 originalA = a.position, originalB = b.position, originalC = c.position;
            Vector2 start = (Vector2)Call(manipulator, "View", (Vector2)a.position);
            Pointer(canvas, start, EventType.MouseDown);
            Pointer(canvas, start + new Vector2(12, -8), EventType.MouseDrag);
            Pointer(canvas, start + new Vector2(12, -8), EventType.MouseUp);
            Selection(a.id, b.id);
            t.Equal(originalA + new Vector3(12, 8, 0), a.position, "Dragged source moved");
            t.Equal(originalB + new Vector3(12, 8, 0), b.position, "Other selected source moved by identical delta");
            t.Equal(originalC, c.position, "Unselected source did not move");
            Undo.PerformUndo();
            a = doc.lights.Find(item => item.id == a.id); b = doc.lights.Find(item => item.id == b.id); c = doc.lights.Find(item => item.id == c.id);
            t.Equal(originalA, a.position, "One Undo restores first source position"); t.Equal(originalB, b.position, "One Undo restores second source position");
            Undo.PerformRedo();
            a = doc.lights.Find(item => item.id == a.id); b = doc.lights.Find(item => item.id == b.id);
            t.Equal(originalA + new Vector3(12, 8, 0), a.position, "One Redo restores first source movement");
            t.Equal(originalB + new Vector3(12, 8, 0), b.position, "One Redo restores second source movement");
            Undo.PerformUndo();
            a = doc.lights.Find(item => item.id == a.id); b = doc.lights.Find(item => item.id == b.id);
            Call(window, "RefreshLightsPanel");
            start = (Vector2)Call(manipulator, "View", (Vector2)a.position);
            Pointer(canvas, start, EventType.MouseDown); Pointer(canvas, start + new Vector2(7, 5), EventType.MouseDrag);
            using (var escape = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
            { escape.target = canvas; t.True((bool)Call(window, "HandleLightSelectionKey", escape), "Escape cancels group movement"); }
            t.Equal(originalA, a.position, "Cancel restores first source"); t.Equal(originalB, b.position, "Cancel restores all selected sources");
            Select(a.id);

            bool Key(VisualElement target, EventModifiers modifiers = EventModifiers.None)
            {
                using var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Delete, modifiers = modifiers });
                evt.target = target;
                return (bool)Call(window, "HandleLightSelectionKey", evt);
            }
            var drawer = (VisualElement)Get(window, "lightsDrawer");
            foreach (VisualElement field in new VisualElement[] { new TextField(), new FloatField(), new IntegerField() })
            { drawer.Add(field); t.True(!Key(field), "Delete remains a text/numeric edit"); field.RemoveFromHierarchy(); }
            t.True(!Key(new Button()), "Unrelated UI does not delete lights");
            t.True(!Key(canvas, EventModifiers.Control), "Ctrl+Delete is not light deletion");
            Set(manipulator, "pointer", 0); t.True(Key(canvas), "Gesture consumes Delete");
            t.Equal(3, doc.lights.Count, "Gesture keeps light data"); Set(manipulator, "pointer", -1);
            Select(b.id, true); Selection(a.id, b.id);
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc, "Parameter before light deletion");
            doc.width = 96; Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            string beforeDelete = JsonUtility.ToJson(doc);
            t.True(Key(canvas), "Delete on Lights canvas handled");
            t.Equal(1, doc.lights.Count, "Delete removes all selected lights"); t.Equal(c.id, doc.lights[0].id, "Unselected light retained");
            Selection(); t.True(Key(canvas), "Repeated Delete with empty selection is consumed"); t.Equal(1, doc.lights.Count, "Repeated Delete does not select and delete another source");
            Undo.PerformUndo(); t.Equal(beforeDelete, JsonUtility.ToJson(doc), "One Undo restores source order, IDs, HDR and all settings");
            Undo.PerformRedo(); t.Equal(1, doc.lights.Count, "One Redo removes the same sources");
            Undo.PerformUndo(); Undo.PerformUndo(); t.Equal(128, doc.width, "Adjacent parameter Undo remains separate");
            Tool(window, "Brush"); Select(c.id); t.True(!Key(canvas), "Other canvas tools do not delete selected lights");
            t.True(Key(drawer), "Focused Lights drawer deletes without canvas editing"); t.Equal(2, doc.lights.Count, "Drawer removed only selected source");
            Select(a.id); Select(b.id, true); doc.RemoveLight(a.id); Call(window, "RefreshLightsPanel"); Selection(b.id);
            other = ScriptableObject.CreateInstance<WhimTexDocument>();
            other.AddLight(WhimTexLightType.Point).id = a.id; other.AddLight(WhimTexLightType.Point).id = b.id;
            Set(window, "activeDocument", other); Call(window, "RefreshLightsPanel"); Selection(a.id);
            t.Equal(2, other.lights.Count, "Document switch only initializes its own selection");
        }
        finally
        {
            if (manipulator != null) manipulator.target = null;
            if (host != null) host.Close();
            if (window != null) { if (doc != null) Set(window, "activeDocument", doc); Object.DestroyImmediate(window); }
            if (doc != null) { Undo.ClearUndo(doc); Object.DestroyImmediate(doc); }
            if (other != null) { Undo.ClearUndo(other); Object.DestroyImmediate(other); }
        }
    }
}
