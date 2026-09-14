using System;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private PreviewEyedropperManipulator previewEyedropper;
        private bool CanUsePreviewEyedropper => HasPreviewLayers && (IsPreviewPaintTool || previewTool == PreviewTool.Fill);
        private bool OwnsScreenEyedropper => previewEyedropper?.IsOpen ?? false;

        private void CancelPreviewEyedropper() => previewEyedropper?.Cancel();

        private static Color PreviewScreenSample(Color[] pixels, int side, float alpha)
        {
            Color color = pixels[(side / 2) * side + side / 2];
            color.a = alpha;
            return color;
        }

        private static Rect PreviewEyedropperLensRect(Vector2 point, Rect bounds)
        {
            const float width = 96f, height = 116f, gap = 24f;
            float x = point.x + gap;
            if (x + width > bounds.xMax) x = point.x - gap - width;
            float y = point.y + gap;
            if (y + height > bounds.yMax) y = point.y - gap - height;
            return new Rect(x, Mathf.Clamp(y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - height)), width, height);
        }

        private static Texture2D CreateScreenEyedropperCursor(Texture2D source)
        {
            if (source == null) return null;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, !source.isDataSRGB)
            {
                name = "WhimTex Eyedropper Cursor",
                hideFlags = HideFlags.HideAndDontSave,
                alphaIsTransparency = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture temporary = null;
            try
            {
                if (source.isReadable)
                    copy.SetPixels32(source.GetPixels32());
                else
                {
                    temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                        source.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                    GL.sRGBWrite = source.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                }
                copy.Apply(false, false);
                return copy;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private sealed class PreviewEyedropperManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private ScreenEyedropperWindow picker;
            private bool failedUntilAltReleased;

            internal bool IsOpen => picker != null;

            internal void SuppressUntilAltReleased() => failedUntilAltReleased = true;

            internal PreviewEyedropperManipulator(TextureCompositorWindow owner) => this.owner = owner;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            internal void UpdateCursor(Vector2 point, bool alt)
            {
                if (IsOpen) return;
                if (!alt) { failedUntilAltReleased = false; return; }
                if (target.contentRect.Contains(point))
                    Begin(GUIUtility.GUIToScreenPoint(target.LocalToWorld(point)));
            }

            internal void UpdateModifier(bool alt)
            {
                if (!alt)
                {
                    failedUntilAltReleased = false;
                    if (picker != null) picker.Finish(true);
                    return;
                }
                if (Event.current != null)
                    Begin(GUIUtility.GUIToScreenPoint(Event.current.mousePosition));
            }

            private void Begin(Vector2 screenPoint)
            {
                if (IsOpen || failedUntilAltReleased || focusedWindow != owner || !owner.CanUsePreviewEyedropper || owner.paintingLayer != null ||
                    (owner.previewZoomManipulator?.IsNavigating ?? false) ||
                    (owner.previewGuideManipulator?.IsDragging ?? false) ||
                    (Event.current != null && (Event.current.control || Event.current.command))) return;
                try
                {
                    picker = ScriptableObject.CreateInstance<ScreenEyedropperWindow>();
                    picker.Open(owner, this, screenPoint);
                }
                catch (Exception exception)
                {
                    failedUntilAltReleased = true;
                    Cancel();
                    Debug.LogException(exception);
                    owner.ShowNotification(new GUIContent("Screen eyedropper is unavailable in this Editor."));
                }
            }

            internal void Closed(ScreenEyedropperWindow closed)
            {
                if (picker == closed) picker = null;
                if (owner == null) return;
                owner.ClearPreviewPointerCursor();
                owner.Repaint();
            }

            internal void Cancel()
            {
                if (picker != null) picker.Finish(false);
            }

            private void OnDetach(DetachFromPanelEvent evt) => Cancel();
        }

        private sealed class ScreenEyedropperWindow : EditorWindow
        {
            private const int SampleSide = 11;
            private static ScreenEyedropperWindow current;
            private TextureCompositorWindow owner;
            private PreviewEyedropperManipulator controller;
            private ScreenEyedropperCapture capture;
            private ScreenEyedropperLensWindow lensWindow;
            private Image magnified;
            private VisualElement swatch;
            private Texture2D sampleTexture;
            private Texture2D cursorTexture;
            private Vector2 screenPosition;
            private Vector2 pendingPickPosition;
            private float pendingPickAlpha;
            private Vector2 displayedSamplePosition;
            private Color displayedSampleColor;
            private Color pendingPickColor;
            private bool hasDisplayedSample;
            private bool pendingPickUsesDisplayedSample;
            private double pointerStableSince;
            private double clearUntil;
            private double nextRead;
            private bool picking;
            private bool pendingPick;
            private bool finishAfterPick;
            private bool sourceOccluded;
            private bool closing;
            private bool shortcutsSuppressed;
            private bool starting;

            internal void Open(TextureCompositorWindow owner, PreviewEyedropperManipulator controller, Vector2 point)
            {
                starting = true;
                if (current != null) current.Finish(false);
                current = this;
                this.owner = owner;
                this.controller = controller;
                screenPosition = point;
                pointerStableSince = EditorApplication.timeSinceStartup;
                hideFlags = HideFlags.HideAndDontSave;
                titleContent = new GUIContent("WhimTex Color Capture");
                minSize = maxSize = new Vector2(96f, 116f);
                position = new Rect(point, minSize);
                wantsMouseMove = true;
                cursorTexture = CreateScreenEyedropperCursor(EditorGUIUtility.FindTexture("EyeDropper.Large"));
                shortcutsSuppressed = AcquireUnityShortcutSuppression();
                lensWindow = ScriptableObject.CreateInstance<ScreenEyedropperLensWindow>();
                lensWindow.hideFlags = HideFlags.HideAndDontSave;
                lensWindow.titleContent = new GUIContent("WhimTex Color Sample");
                lensWindow.minSize = lensWindow.maxSize = minSize;
                lensWindow.position = PreviewEyedropperLensRect(point, InternalEditorUtility.GetBoundsOfDesktopAtPoint(point));
                BuildLens(lensWindow.rootVisualElement);
                lensWindow.ShowPopup();
                ShowPopup();
                if (closing) return;
                Focus();
                rootVisualElement.Focus();
                rootVisualElement.CapturePointer(PointerId.mousePointerId);
                capture = new ScreenEyedropperCapture(this, cursorTexture);
                clearUntil = EditorApplication.timeSinceStartup + 0.05;
                EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload += Cancel;
                EditorApplication.quitting += Cancel;
                starting = false;
            }

            public void CreateGUI()
            {
                VisualElement root = rootVisualElement;
                root.focusable = true;
                root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
                root.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
                root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
                root.RegisterCallback<WheelEvent>(WhimTexUI.ConsumeEvent, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerCancelEvent>(_ => Finish(false));
            }

            private void BuildLens(VisualElement root)
            {
                WhimTexUI.ApplyWindowStyles(root);
                root.AddToClassList("whimtex-eyedropper-lens");
                if (cursorTexture != null)
                    root.style.cursor = new UnityEngine.UIElements.Cursor
                    {
                        texture = cursorTexture,
                        hotspot = new Vector2(2f, cursorTexture.height - 2f)
                    };
                magnified = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                magnified.AddToClassList("whimtex-eyedropper-sample");
                root.Add(magnified);
                var grid = new VisualElement { pickingMode = PickingMode.Ignore };
                grid.AddToClassList("whimtex-eyedropper-sample");
                grid.generateVisualContent += DrawGrid;
                root.Add(grid);
                swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("whimtex-eyedropper-swatch");
                root.Add(swatch);
            }

            private void OnMove(PointerMoveEvent evt)
            {
                WhimTexUI.ConsumeEvent(evt);
                if (closing || finishAfterPick) return;
                if (evt.imguiEvent == null ||
                    (evt.imguiEvent.rawType != EventType.MouseMove && evt.imguiEvent.rawType != EventType.MouseDrag)) return;
                if (!evt.altKey) { Finish(true); return; }
                try
                {
                    picking = picking && (evt.pressedButtons & 1) != 0;
                    MoveSample();
                    if (picking) QueuePick();
                    ReadSample(false);
                    capture?.Refresh();
                }
                catch (Exception exception) { Fail(exception); }
            }

            private void OnDown(PointerDownEvent evt)
            {
                WhimTexUI.ConsumeEvent(evt);
                if (closing || finishAfterPick) return;
                if (!evt.altKey) { Finish(true); return; }
                try
                {
                    MoveSample();
                    if (evt.button == 0)
                    {
                        picking = true;
                        QueuePick();
                        ReadSample(true);
                    }
                    capture?.Refresh();
                }
                catch (Exception exception) { Fail(exception); }
            }

            private void OnUp(PointerUpEvent evt)
            {
                WhimTexUI.ConsumeEvent(evt);
                if (closing || finishAfterPick) return;
                try
                {
                    MoveSample();
                    if (evt.button == 0 && picking)
                    {
                        QueuePick();
                        picking = false;
                        ReadSample(true);
                    }
                    if (closing) return;
                    if (!evt.altKey) { Finish(true); return; }
                    rootVisualElement.CapturePointer(PointerId.mousePointerId);
                    capture?.Refresh();
                }
                catch (Exception exception) { Fail(exception); }
            }

            private void OnKeyDown(KeyDownEvent evt)
            {
                WhimTexUI.ConsumeEvent(evt);
                if (!evt.altKey) Finish(true);
            }

            private void OnKeyUp(KeyUpEvent evt)
            {
                WhimTexUI.ConsumeEvent(evt);
                if (!evt.altKey) Finish(true);
            }

            private void MoveSample()
            {
                if (capture == null) return;
                Vector2 previous = screenPosition;
                screenPosition = capture.GetScreenPosition();
                if (!screenPosition.Equals(previous)) pointerStableSince = EditorApplication.timeSinceStartup;
                DeferCoveredSample();
            }

            private void QueuePick()
            {
                pendingPickPosition = screenPosition;
                pendingPickAlpha = owner.paintSettings.brushColor.a;
                pendingPickUsesDisplayedSample = CanReuseDisplayedSample(EditorApplication.timeSinceStartup);
                pendingPickColor = displayedSampleColor;
                pendingPickColor.a = pendingPickAlpha;
                pendingPick = true;
                DeferCoveredSample();
            }

            private bool CanReuseDisplayedSample(double now)
            {
                return hasDisplayedSample && screenPosition.Equals(displayedSamplePosition) &&
                    now - pointerStableSince >= 2.0 / 30.0;
            }

            private void DeferCoveredSample()
            {
                float half = (SampleSide + 2f) * 0.5f / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                Vector2 point = pendingPick ? pendingPickPosition : screenPosition;
                Rect probe = new Rect(point.x - half, point.y - half, half * 2f, half * 2f);
                if (lensWindow != null && lensWindow.position.Overlaps(probe))
                {
                    sourceOccluded = true;
                    clearUntil = EditorApplication.timeSinceStartup + 0.05;
                }
            }

            private void MoveLens()
            {
                if (lensWindow == null) return;
                Vector2 point = pendingPick ? pendingPickPosition : screenPosition;
                Rect next = PreviewEyedropperLensRect(point, InternalEditorUtility.GetBoundsOfDesktopAtPoint(point));
                if (lensWindow.position == next) return;
                DeferCoveredSample();
                lensWindow.position = next;
            }

            private void Tick()
            {
                if (closing) return;
                try
                {
                    if (owner == null || lensWindow == null || !owner.CanUsePreviewEyedropper || focusedWindow != this)
                    {
                        Finish(false);
                        return;
                    }
                    if (!finishAfterPick) MoveSample();
                    MoveLens();
                    ReadSample(false);
                    if (finishAfterPick && !pendingPick) Finish(true);
                }
                catch (Exception exception) { Fail(exception); }
            }

            private void ReadSample(bool force)
            {
                if (closing || magnified == null || owner == null) return;
                double now = EditorApplication.timeSinceStartup;
                try
                {
                    if (pendingPick && pendingPickUsesDisplayedSample)
                    {
                        ApplySample(pendingPickColor);
                        return;
                    }
                    if (now < clearUntil && (!force || sourceOccluded)) return;
                    if (!force && now < nextRead) return;
                    bool commitPick = pendingPick;
                    Vector2 point = commitPick ? pendingPickPosition : screenPosition;
                    sourceOccluded = false;
                    Color[] pixels = InternalEditorUtility.ReadScreenPixelUnderCursor(point, SampleSide, SampleSide);
                    if (pixels == null || pixels.Length != SampleSide * SampleSide)
                        throw new InvalidOperationException("Unity did not return the requested screen pixels.");
                    Color color = PreviewScreenSample(pixels, SampleSide,
                        commitPick ? pendingPickAlpha : owner.paintSettings.brushColor.a);
                    for (int i = 0; i < pixels.Length; i++) pixels[i].a = 1f;
                    if (sampleTexture == null)
                    {
                        sampleTexture = new Texture2D(SampleSide, SampleSide, TextureFormat.RGBA32, false, true)
                        {
                            name = "WhimTex Eyedropper Sample",
                            hideFlags = HideFlags.HideAndDontSave,
                            filterMode = FilterMode.Point,
                            wrapMode = TextureWrapMode.Clamp
                        };
                        magnified.image = sampleTexture;
                    }
                    sampleTexture.SetPixels(pixels);
                    sampleTexture.Apply(false, false);
                    magnified.MarkDirtyRepaint();
                    swatch.style.backgroundColor = new Color(color.r, color.g, color.b, 1f);
                    displayedSamplePosition = point;
                    displayedSampleColor = color;
                    hasDisplayedSample = true;
                    nextRead = now + 1.0 / 30.0;
                    if (commitPick) ApplySample(color);
                    lensWindow?.Repaint();
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }

            private void ApplySample(Color color)
            {
                pendingPick = false;
                if (owner.paintSettings.brushColor != color)
                    owner.ApplyPaintToolChange(() => owner.paintSettings.brushColor = color);
            }

            internal void Finish(bool returnToOwner)
            {
                if (closing) return;
                if (returnToOwner && pendingPick && capture != null)
                {
                    picking = false;
                    finishAfterPick = true;
                    return;
                }
                Cleanup();
                TryCleanup(Close);
                if (returnToOwner) TryCleanup(ReturnFocus);
            }

            private void ReturnFocus()
            {
                if (owner != null)
                {
                    owner.Focus();
                    owner.toolkitPreviewCanvas?.Focus();
                }
            }

            private void Fail(Exception exception)
            {
                if (closing) return;
                controller?.SuppressUntilAltReleased();
                bool returnToOwner = focusedWindow == this;
                Finish(false);
                if (returnToOwner) TryCleanup(ReturnFocus);
                Debug.LogException(exception);
                TryCleanup(() =>
                {
                    if (owner != null) owner.ShowNotification(new GUIContent("Screen eyedropper failed. Release Alt to try again."));
                });
            }

            private void TryCleanup(Action action)
            {
                try { action(); }
                catch (Exception exception)
                {
                    controller?.SuppressUntilAltReleased();
                    Debug.LogException(exception);
                }
            }

            private void Cleanup()
            {
                if (closing) return;
                closing = true;
                picking = pendingPick = false;
                EditorApplication.update -= Tick;
                AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
                EditorApplication.quitting -= Cancel;
                TryCleanup(() => rootVisualElement.style.cursor = StyleKeyword.Null);
                TryCleanup(() => capture?.Dispose());
                capture = null;
                TryCleanup(() =>
                {
                    if (rootVisualElement.HasPointerCapture(PointerId.mousePointerId))
                        rootVisualElement.ReleasePointer(PointerId.mousePointerId);
                });
                if (shortcutsSuppressed) TryCleanup(ReleaseUnityShortcutSuppression);
                shortcutsSuppressed = false;
                TryCleanup(() => { if (magnified != null) magnified.image = null; });
                if (lensWindow != null)
                {
                    TryCleanup(() => lensWindow.rootVisualElement.style.cursor = StyleKeyword.Null);
                    TryCleanup(() => lensWindow.Close());
                }
                lensWindow = null;
                TryCleanup(() => { if (sampleTexture != null) UnityEngine.Object.DestroyImmediate(sampleTexture); });
                sampleTexture = null;
                TryCleanup(() => { if (cursorTexture != null) UnityEngine.Object.DestroyImmediate(cursorTexture); });
                cursorTexture = null;
                if (current == this) current = null;
                TryCleanup(() => controller?.Closed(this));
            }

            private void Cancel() => Finish(false);
            private void OnLostFocus() { if (!starting) Finish(false); }
            private void OnDisable() => Cleanup();

            private static void DrawGrid(MeshGenerationContext context)
            {
                Painter2D p = context.painter2D;
                p.lineWidth = 1f;
                p.strokeColor = new Color(0f, 0f, 0f, 0.2f);
                p.BeginPath();
                for (int i = 1; i < SampleSide; i++)
                {
                    float offset = i * 8f;
                    p.MoveTo(new Vector2(offset, 0f)); p.LineTo(new Vector2(offset, 88f));
                    p.MoveTo(new Vector2(0f, offset)); p.LineTo(new Vector2(88f, offset));
                }
                p.Stroke();
                for (int pass = 0; pass < 2; pass++)
                {
                    p.lineWidth = pass == 0 ? 3f : 1f;
                    p.strokeColor = pass == 0 ? Color.black : Color.white;
                    p.BeginPath();
                    p.MoveTo(new Vector2(40f, 40f));
                    p.LineTo(new Vector2(48f, 40f));
                    p.LineTo(new Vector2(48f, 48f));
                    p.LineTo(new Vector2(40f, 48f));
                    p.ClosePath();
                    p.Stroke();
                }
            }
        }

        private sealed class ScreenEyedropperLensWindow : EditorWindow { }

        private sealed class ScreenEyedropperCapture : IDisposable
        {
            private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private static readonly FieldInfo Parent = typeof(EditorWindow).GetField("m_Parent", InstanceFlags);
            private static readonly Type ViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView");
            private static readonly PropertyInfo Container = ViewType?.GetProperty("window", InstanceFlags);
            private static readonly MethodInfo SetInvisible = typeof(EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow")?.GetMethod("SetInvisible", InstanceFlags);
            private static readonly MethodInfo CaptureMouse = ViewType?.GetMethod("StealMouseCapture", InstanceFlags);
            private static readonly MethodInfo SetOpen = ViewType?.GetMethod("SetEyeDropperOpen", InstanceFlags);
            private static readonly MethodInfo SetCursor = typeof(EditorGUIUtility).GetMethod("SetCurrentViewCursor", BindingFlags.Static | BindingFlags.NonPublic);
            private static readonly MethodInfo MousePosition = typeof(UnityEditor.Editor).GetMethod("GetCurrentMousePosition", BindingFlags.Static | BindingFlags.NonPublic);
            private readonly Action captureMouse;
            private readonly Action<bool> setOpen;
            private readonly Action<Texture2D, Vector2, MouseCursor> setCursor;
            private readonly Func<Vector2> getScreenPosition;
            private readonly Texture2D cursorTexture;
            private bool opened;

            internal ScreenEyedropperCapture(EditorWindow window, Texture2D cursorTexture)
            {
                object view = Parent?.GetValue(window);
                object container = view != null ? Container?.GetValue(view) : null;
                if (view == null || container == null || SetInvisible == null || CaptureMouse == null || SetOpen == null || SetCursor == null || MousePosition == null)
                    throw new NotSupportedException("Unity screen pointer capture API is unavailable.");
                captureMouse = (Action)Delegate.CreateDelegate(typeof(Action), view, CaptureMouse);
                setOpen = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), view, SetOpen);
                setCursor = (Action<Texture2D, Vector2, MouseCursor>)Delegate.CreateDelegate(typeof(Action<Texture2D, Vector2, MouseCursor>), SetCursor);
                getScreenPosition = (Func<Vector2>)Delegate.CreateDelegate(typeof(Func<Vector2>), MousePosition);
                this.cursorTexture = cursorTexture;
                try
                {
                    SetInvisible.Invoke(container, null);
                    window.Focus();
                    opened = true;
                    setOpen(true);
                    Refresh();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal Vector2 GetScreenPosition() => getScreenPosition();

            internal void Refresh()
            {
                if (!opened) return;
                captureMouse();
                setCursor(cursorTexture, cursorTexture != null ? new Vector2(2f, cursorTexture.height - 2f) : Vector2.zero,
                    cursorTexture != null ? MouseCursor.CustomCursor : MouseCursor.ArrowPlus);
            }

            public void Dispose()
            {
                if (!opened) return;
                opened = false;
                try { setOpen(false); }
                finally { InternalEditorUtility.ResetCursor(); }
            }
        }
    }
}
