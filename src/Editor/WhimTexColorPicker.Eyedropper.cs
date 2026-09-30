using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexColorPicker
    {
        private static readonly EyedropperPreviewApi NativeEyedropper = EyedropperPreviewApi.Bind(typeof(EditorWindow).Assembly.GetType("UnityEditor.EyeDropper"));
        private EyedropperPreviewApi eyedropperApi = NativeEyedropper;
        private IMGUIContainer eyedropperPreview;
        private bool eyedropperPending, eyedropperOwned;
        private double nextEyedropperRepaint;

        private void BuildEyedropperPreview(VisualElement wheel)
        {
            eyedropperPreview = new IMGUIContainer(DrawEyedropperPreview) { pickingMode = PickingMode.Ignore, focusable = false };
            eyedropperPreview.AddToClassList("whimtex-picker-eyedropper-preview");
            wheel.Add(eyedropperPreview);
            ShowEyedropperPreview(false);
            eyedropper.RegisterCallback<PointerDownEvent>(OnEyedropperPointerDown, TrickleDown.TrickleDown);
            eyedropper.Q(className: UnityEditor.UIElements.ColorField.eyeDropperUssClassName)?.RegisterCallback<PointerDownEvent>(_ => ClaimEyedropper());
        }

        private void OnEyedropperPointerDown(PointerDownEvent e)
        {
            if (e.button != 0 || finished || eyedropperApi == null || eyedropperApi.IsOpened()) return;
            var button = eyedropper.Q(className: UnityEditor.UIElements.ColorField.eyeDropperUssClassName);
            var target = e.target as VisualElement;
            if (button == null || target == null || target != button && !button.Contains(target)) return;
            eyedropperPending = true;
        }

        private void ClaimEyedropper()
        {
            if (!eyedropperPending) return;
            eyedropperPending = false;
            eyedropperOwned = eyedropperApi != null && eyedropperApi.IsOpened();
            ShowEyedropperPreview(eyedropperOwned && eyedropperApi.CanDraw);
        }

        private void ShowEyedropperPreview(bool show)
        {
            eyedropperPreview?.EnableInClassList("whimtex-picker-hidden", !show);
            hueRing?.EnableInClassList("whimtex-picker-hidden", show);
            plane?.EnableInClassList("whimtex-picker-hidden", show);
        }

        private void UpdateEyedropper()
        {
            ClaimEyedropper();
            if (!eyedropperOwned) return;
            if (!eyedropperApi.IsOpened())
            {
                eyedropperOwned = false;
                ShowEyedropperPreview(false);
                return;
            }
            if (!eyedropperApi.CanDraw || EditorApplication.timeSinceStartup < nextEyedropperRepaint) return;
            nextEyedropperRepaint = EditorApplication.timeSinceStartup + 1.0 / 30;
            eyedropperPreview.MarkDirtyRepaint();
            Repaint();
        }

        private void DrawEyedropperPreview()
        {
            if (Event.current.type != EventType.Repaint || !eyedropperOwned || !eyedropperApi.IsOpened()) return;
            var rect = eyedropperPreview.contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;
            var previous = GUI.color;
            try
            {
                GUI.color = Color.white;
                if (!eyedropperApi.Draw(rect)) ShowEyedropperPreview(false);
            }
            finally { GUI.color = previous; }
        }

        private bool StopEyedropper()
        {
            ClaimEyedropper();
            bool owned = eyedropperOwned;
            eyedropperOwned = false;
            ShowEyedropperPreview(false);
            if (owned && eyedropperApi.IsOpened()) eyedropperApi.End();
            return owned;
        }

        private sealed class EyedropperPreviewApi
        {
            private readonly Func<bool> isOpened;
            private readonly Action<Rect> draw;
            private readonly Action end;
            private bool canDraw = true, warned;
            public bool CanDraw => canDraw;

            private EyedropperPreviewApi(Func<bool> isOpened, Action<Rect> draw, Action end)
            { this.isOpened = isOpened; this.draw = draw; this.end = end; }

            public static EyedropperPreviewApi Bind(Type type)
            {
                if (type == null) return null;
                try
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                    var state = type.GetProperty("IsOpened", flags)?.GetGetMethod();
                    var preview = type.GetMethod("DrawPreview", flags, null, new[] { typeof(Rect) }, null);
                    var stop = type.GetMethod("End", flags, null, Type.EmptyTypes, null);
                    if (state == null || preview == null || stop == null) return null;
                    return new EyedropperPreviewApi((Func<bool>)state.CreateDelegate(typeof(Func<bool>)),
                        (Action<Rect>)preview.CreateDelegate(typeof(Action<Rect>)), (Action)stop.CreateDelegate(typeof(Action)));
                }
                catch (Exception) { return null; }
            }

            public bool IsOpened()
            {
                try { return isOpened(); }
                catch (Exception e) { Unavailable(e); return false; }
            }
            public bool Draw(Rect rect)
            {
                if (!canDraw) return false;
                try { draw(rect); return true; }
                catch (ExitGUIException) { throw; }
                catch (Exception e) { Unavailable(e); return false; }
            }
            public void End()
            {
                try { end(); }
                catch (ExitGUIException) { throw; }
                catch (Exception e) { Unavailable(e); }
            }
            private void Unavailable(Exception e)
            {
                canDraw = false;
                if (warned) return;
                warned = true;
                Debug.LogWarning("WhimTex: eyedropper magnifier unavailable; standard color picking remains available. " + e.Message);
            }
        }
    }
}
