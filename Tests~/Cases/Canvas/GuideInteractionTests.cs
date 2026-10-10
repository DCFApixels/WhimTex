using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class GuideInteractionTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Instance).GetValue(value);
    static void Write(object value, string name, object next) => value.GetType().GetField(name, Instance).SetValue(value, next);
    static object Call(object value, string name, params object[] args) => Array.Find(value.GetType().GetMethods(Instance),
        method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(value, args);
    static bool Property(object value, string name) => (bool)value.GetType().GetProperty(name, Instance).GetValue(value);

    static void Pointer(VisualElement target, EventType type, Vector2 world, bool control = false)
    {
        var input = new Event { type = type, button = 0, mousePosition = world,
            modifiers = control ? EventModifiers.Control : EventModifiers.None };
        if (type == EventType.MouseDown)
        { using var evt = PointerDownEvent.GetPooled(input); evt.target = target; target.SendEvent(evt); }
        else if (type == EventType.MouseDrag)
        { using var evt = PointerMoveEvent.GetPooled(input); evt.target = target; target.SendEvent(evt); }
        else
        { using var evt = PointerUpEvent.GetPooled(input); evt.target = target; target.SendEvent(evt); }
    }

    public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) =>
        WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope =>
        {
            var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
            var document = (WhimTexDocument)Read(window, "activeDocument");
            document.width = document.height = 64;
            Layer front = new ColorFillLayerBehaviour { color = Color.red };
            Layer back = new ColorFillLayerBehaviour { color = Color.blue };
            Call(front, "AssignNewId"); Call(back, "AssignNewId");
            context.True(!string.IsNullOrEmpty(front.Id) && !string.IsNullOrEmpty(back.Id) && front.Id != back.Id, "Owned layers have distinct pickable identities");
            document.layers.Add(front); document.layers.Add(back);
            Call(window, "SelectOnlyLayer", back.Id);
            window.ShowUtility(); window.position = new Rect(90, 90, 1300, 800); window.CreateGUI();
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, cancellation);
            var canvas = (VisualElement)Read(window, "toolkitCanvas");
            var corner = window.rootVisualElement.Q("canvasGuideCorner");
            var guides = (IList)Read(window, "canvasGuides");
            var manipulator = Read(window, "canvasGuideManipulator");
            var viewport = Read(window, "canvasViewport");
            var toolType = Read(window, "canvasTool").GetType();
            void Tool(string name) => Call(window, "SetCanvasTool", Enum.Parse(toolType, name));
            Vector2 World(Vector2 point) => canvas.LocalToWorld(point);
            bool Dragging() => Property(manipulator, "IsDragging");
            void Release(Vector2 point) => Pointer(canvas, EventType.MouseUp, World(point));
            void BeginPair()
            {
                var picked = window.rootVisualElement.panel.Pick(corner.worldBound.center);
                context.True(picked == corner, "The ruler intersection is an actual interactive target");
                Pointer(picked, EventType.MouseDown, corner.worldBound.center);
                context.True(Dragging(), "Corner starts the shared guide gesture");
            }
            void Cancel() => Call(manipulator, "Cancel");
            Tool("None");
            Write(window, "canvasGuidesSnap", false);
            Vector2 destination = canvas.contentRect.center + new Vector2(-52, 20);
            foreach (float angle in new[] { 0f, 37f })
            {
                Call(viewport, "SetRotation", angle, false);
                Call(canvas, "UpdateImageLayout");
                await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, cancellation);
                int before = guides.Count;
                BeginPair();
                Pointer(canvas, EventType.MouseDrag, World(destination));
                context.True(Dragging(), "Pair remains captured during a pointer move");
                context.Equal(before, guides.Count, "Pair preview does not mutate committed guides");
                Release(destination);
                context.Equal(before + 2, guides.Count, "Corner commits exactly two guides");
                context.True(!Dragging() && !canvas.HasPointerCapture(PointerId.mousePointerId), "Pair releases pointer capture");
                Vector2 a = (Vector2)Read(guides[before], "normal"), b = (Vector2)Read(guides[before + 1], "normal");
                context.Near(0, Vector2.Dot(a, b), .00001, "Pair guides are perpendicular");
                Vector2 screenA = (Vector2)Call(viewport, "ToViewDelta", a), screenB = (Vector2)Call(viewport, "ToViewDelta", b);
                context.Near(1, screenA.x, .00001, "Vertical guide follows the current view axis");
                context.Near(1, screenB.y, .00001, "Horizontal guide follows the current view axis");
                Vector2 documentPoint = ((Vector2)Call(canvas, "ToCanvas", destination) -
                    ((Rect)canvas.GetType().GetProperty("ImageRect", Instance).GetValue(canvas)).position) /
                    (float)canvas.GetType().GetProperty("PixelScale", Instance).GetValue(canvas);
                context.Near(Vector2.Dot(documentPoint, a), (float)Read(guides[before], "position"), .0001, "Vertical line passes through release position");
                context.Near(Vector2.Dot(documentPoint, b), (float)Read(guides[before + 1], "position"), .0001, "Horizontal line passes through release position");
                Call(window, "RestoreCanvasGuides", false);
                context.Equal(before, guides.Count, "One Undo removes the whole pair");
                Call(window, "RestoreCanvasGuides", true);
                context.Equal(before + 2, guides.Count, "One Redo restores the whole pair");
                Call(window, "ClearCanvasGuides");
            }
            Call(viewport, "SetRotation", 0f, false); Call(canvas, "UpdateImageLayout");
            BeginPair(); Cancel(); Release(destination);
            context.Equal(0, guides.Count, "Cancelling the pair commits neither line");
            BeginPair(); Release(new Vector2(canvas.contentRect.xMax + 20, canvas.contentRect.center.y));
            context.Equal(0, guides.Count, "Dropping outside the view commits neither line");
            BeginPair(); Release(destination);
            Call(window, "SelectOnlyLayer", back.Id);
            float original = (float)Read(guides[0], "position");
            Pointer(canvas, EventType.MouseDown, World(destination));
            context.True(Dragging(), "Layer Select yields a guide hit to its manipulator");
            context.Equal(back.Id, (string)Read(window, "selectedLayerId"), "Grabbing a guide does not pick the visible front layer");
            destination += Vector2.right * 30;
            Pointer(canvas, EventType.MouseDrag, World(destination)); Release(destination);
            context.True(Mathf.Abs((float)Read(guides[0], "position") - original) > .1f, "Layer Select moves the existing guide");
            Pointer(canvas, EventType.MouseDown, World(destination), true);
            context.True(!Dragging(), "Ctrl bypasses guide capture");
            context.Equal(front.Id, (string)Read(window, "selectedLayerId"), "Ctrl retains layer picking through a guide");
            Pointer(canvas, EventType.MouseUp, World(destination), true);
            Tool("Zoom");
            Pointer(canvas, EventType.MouseDown, World(destination));
            context.True(Dragging() && !Property(Read(window, "canvasZoomManipulator"), "IsDragging"), "Zoom yields an existing guide hit");
            Release(destination);
            Vector2 away = destination + new Vector2(25, 25);
            Pointer(canvas, EventType.MouseDown, World(away));
            context.True(!Dragging() && Property(Read(window, "canvasZoomManipulator"), "IsDragging"), "Zoom still works away from guides");
            Call(window, "CancelCanvasZoomGesture"); Release(away);
            Tool("Transform");
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(150, cancellation);
            Pointer(canvas, EventType.MouseDown, World(destination));
            context.True(!Dragging() && Property(Read(window, "canvasTransformManipulator"), "IsDragging"),
                "Transform frame retains priority over guides: guide=" + Dragging() + ", transform=" +
                Property(Read(window, "canvasTransformManipulator"), "IsDragging") + ", enabled=" + Property(window, "IsCanvasTransformEnabled") +
                ", hit=" + Call(Read(window, "canvasTransformManipulator"), "WantsPointer", destination) + ", point=" + destination +
                ", image=" + canvas.GetType().GetProperty("ImageRect", Instance).GetValue(canvas));
            canvas.ReleasePointer(PointerId.mousePointerId); Release(destination);
            foreach (object tool in Enum.GetValues(toolType))
            {
                Write(window, "canvasTool", tool);
                string name = tool.ToString();
                bool allowed = name == "None" || name == "Transform" || name == "Zoom" || name == "FXTransform" || name == "FXPoint" || name == "FXNormal";
                context.Equal(allowed, Property(window, "CanMoveCanvasGuides"), name + " guide policy");
            }
            Tool("None");
            Write(window, "canvasGuidesLocked", true);
            context.True(!(bool)Call(manipulator, "WantsPointer", destination, false, false), "Locked guides do not steal tool clicks");
            Write(window, "canvasGuidesLocked", false); Write(window, "canvasGuidesHidden", true);
            context.True(!(bool)Call(manipulator, "WantsPointer", destination, false, false), "Hidden guides do not steal tool clicks");
            Write(window, "canvasGuidesHidden", false);
            object guide = guides[0];
            while (guides.Count < 255) guides.Add(guide);
            Pointer(corner, EventType.MouseDown, corner.worldBound.center);
            context.True(!Dragging(), "Pair creation requires room for both lines");
            Release(destination);
            context.Equal(255, guides.Count, "Guide limit does not create a partial pair");
            window.DiscardChanges();
        }));

    public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
    public static Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
    public static Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
