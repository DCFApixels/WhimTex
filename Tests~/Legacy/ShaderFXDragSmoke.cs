using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShaderFXDragSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void SendPointer(VisualElement target, EventType type, Vector2 position, bool explicitTarget = true)
    {
        var source = new Event { type = type, button = 0, mousePosition = position };
        if (type == EventType.MouseDown)
        {
            using (var e = PointerDownEvent.GetPooled(source)) { if (explicitTarget) e.target = target; target.SendEvent(e); }
        }
        else if (type == EventType.MouseUp)
        {
            using (var e = PointerUpEvent.GetPooled(source)) { if (explicitTarget) e.target = target; target.SendEvent(e); }
        }
        else
        {
            using (var e = PointerMoveEvent.GetPooled(source)) { if (explicitTarget) e.target = target; target.SendEvent(e); }
        }
    }

    public static string Main()
    {
        var previousWindow = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<EditorWindow>();
        window.titleContent = new GUIContent("FX drag smoke");
        try
        {
            window.ShowUtility();
            window.Focus();
            var header = new VisualElement();
            var title = new Label("FX title");
            var button = new Button();
            header.Add(title);
            header.Add(button);
            window.rootVisualElement.Add(header);

            var viewType = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.LayerShaderFXView", true);
            var type = viewType.GetNestedType("ReorderManipulator", BindingFlags.NonPublic);
            var ctor = type.GetConstructors(Private)[0];
            var hitTestType = ctor.GetParameters()[4].ParameterType;
            var resultType = hitTestType.GetGenericArguments()[1];
            var hitTest = Expression.Lambda(hitTestType, Expression.Default(resultType),
                Expression.Parameter(typeof(Vector2))).Compile();
            int moves = 0;
            var manipulator = (IManipulator)ctor.Invoke(new object[] {
                0, (Action<Vector2, bool>)((p, active) => {}),
                (Action<int, int>)((a, b) => moves++), (Func<bool>)(() => true),
                hitTest, (Func<Layer, bool>)(_ => false), (Action<VisualElement>)(_ => {}),
                (Action<int, Layer>)((i, l) => moves++)
            });
            header.AddManipulator(manipulator);
            Func<bool> dragging = () => (bool)type.GetField("dragging", Private).GetValue(manipulator);
            Action checkIdle = () => {
                Check(!header.HasPointerCapture(PointerId.mousePointerId), "Pointer capture leaked");
                Check(!dragging(), "Dragging state leaked");
                Check(type.GetField("autoScrollSchedule", Private).GetValue(manipulator) == null, "Autoscroll leaked");
            };
            Action begin = () => {
                SendPointer(title, EventType.MouseDown, new Vector2(20, 20));
                Check(header.HasPointerCapture(PointerId.mousePointerId), "Header did not capture pointer");
                SendPointer(window.rootVisualElement, EventType.MouseDrag, new Vector2(50, 50), false);
                Check(dragging(), "Drag did not start");
            };

            for (int i = 0; i < 3; i++)
            {
                begin();
                SendPointer(window.rootVisualElement, EventType.MouseUp, new Vector2(50, 50), false);
                checkIdle();
                SendPointer(header, EventType.MouseMove, new Vector2(90, 90));
                checkIdle();
            }

            begin();
            using (var e = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
                window.rootVisualElement.SendEvent(e);
            checkIdle();
            SendPointer(header, EventType.MouseUp, new Vector2(50, 50));

            begin();
            using (var e = PointerCancelEvent.GetPooled()) header.SendEvent(e);
            checkIdle();
            SendPointer(header, EventType.MouseUp, new Vector2(50, 50));

            begin();
            using (var e = MouseLeaveWindowEvent.GetPooled()) window.rootVisualElement.SendEvent(e);
            checkIdle();
            SendPointer(header, EventType.MouseUp, new Vector2(50, 50));

            begin();
            button.CapturePointer(PointerId.mousePointerId);
            SendPointer(window.rootVisualElement, EventType.MouseDrag, new Vector2(60, 60), false);
            checkIdle();
            button.ReleasePointer(PointerId.mousePointerId);
            SendPointer(button, EventType.MouseUp, new Vector2(50, 50));

            begin();
            header.RemoveFromHierarchy();
            checkIdle();
            window.rootVisualElement.Add(header);
            SendPointer(header, EventType.MouseUp, new Vector2(50, 50));

            SendPointer(button, EventType.MouseDown, new Vector2(20, 20));
            Check(!header.HasPointerCapture(PointerId.mousePointerId), "Header stole button capture");
            SendPointer(button, EventType.MouseUp, new Vector2(20, 20));
            checkIdle();
            Check(moves == 0, "Cancelled/invalid drops changed the stack");
            header.RemoveManipulator(manipulator);
            return "PASS: captured event routing, repeated release, idle movement, Escape, pointer cancel, window exit, capture loss, detach, button isolation, no invalid moves.";
        }
        finally
        {
            window.Close();
            if (previousWindow != null) previousWindow.Focus();
        }
    }
}
