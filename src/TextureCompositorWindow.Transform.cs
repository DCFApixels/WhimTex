using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private double nextTransformPreviewAt;
        private VisualElement previewTransformOverlay;
        private PreviewTransformManipulator previewTransformManipulator;
        [NonSerialized] private ShaderFX previewTransformFX;
        [NonSerialized] private string previewTransformParameterId;

        private ShaderFXParameter PreviewFXParameter
        {
            get
            {
                if (previewTransformFX == null || compositor == null || GetSelectedLayer() is not Layer selected ||
                    !selected.modifiers.Contains(previewTransformFX) || WhimTexApi.IsLayerContentLocked(compositor, selected) ||
                    WhimTexApi.IsShaderFXContentLocked(previewTransformFX)) return null;
                foreach (var p in previewTransformFX.Parameters)
                    if (p != null && p.id == previewTransformParameterId && p.type == ShaderFXParameterType.Transform2D) return p;
                return null;
            }
        }

        private TextureTransform CurrentPreviewTransform => PreviewFXParameter is ShaderFXParameter p
            ? p.transformValue.ToLayerTransform(new Vector2(compositor.width, compositor.height)) : GetSelectedLayer().transform;

        internal static void EditFXTransform(ShaderFX effect, string parameterId)
        {
            if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
            bool found = false;
            foreach (var p in effect.Parameters)
                found |= p != null && p.id == parameterId && p.type == ShaderFXParameterType.Transform2D;
            if (!found) return;
            TextureCompositorWindow best = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window.compositor != null && window.GetSelectedLayer() is Layer selected && selected.modifiers.Contains(effect) &&
                    !WhimTexApi.IsLayerContentLocked(window.compositor, selected) &&
                    (best == null || window == focusedWindow || best != focusedWindow && window.AgentFocusOrder > best.AgentFocusOrder)) best = window;
            if (best == null) { EditorUtility.DisplayDialog("FX Transform", "Select a layer using this FX in a WhimTex window first.", "OK"); return; }
            bool toggleOff = best.previewTransformFX == effect && best.previewTransformParameterId == parameterId;
            best.SetPreviewTool(toggleOff ? best.previewTransformReturnTool : PreviewTool.Transform);
            if (!toggleOff)
            {
                best.previewTransformFX = effect;
                best.previewTransformParameterId = parameterId;
            }
            best.RefreshToolkitInterface();
            best.Focus();
        }

        private bool IsPreviewTransformEnabled => !IsGradientCanvasEnabled && previewTool == PreviewTool.Transform &&
            GetSelectedLayer() is Layer layer && layer.Behaviour != null && (!layer.IsGroup || PreviewFXParameter != null) &&
            !WhimTexApi.IsLayerContentLocked(compositor, layer) && !WhimTexApi.ContainsReservation(layer);

        private void BuildPreviewTransformTool()
        {
            previewTransformOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            previewTransformOverlay.StretchToParentSize();
            toolkitPreviewCanvas.Add(previewTransformOverlay);
            previewTransformManipulator = new PreviewTransformManipulator(this);
            previewTransformOverlay.generateVisualContent += previewTransformManipulator.Draw;
            toolkitPreviewCanvas.AddManipulator(previewTransformManipulator);
            toolkitPreviewCanvas.RegisterCallback<GeometryChangedEvent>(_ => previewTransformOverlay.MarkDirtyRepaint());
        }

        private void AddPreviewTransformSettings()
        {
            VisualElement row = WhimTexUI.CreateToolbar();
            row.AddToClassList("whimtex-transform-settings");
            toolkitHeaderBindings.Add(() => row.SetEnabled(PreviewFXParameter == null));
            VisualElement tilingGroup = WhimTexUI.CreateRow();
            tilingGroup.AddToClassList("whimtex-transform-option");
            tilingGroup.Add(CreateCompactLabel("Tiling", 38f));
            EnumField tiling = CompactField(new EnumField(TransformTilingMode.Clip), 100f);
            tiling.tooltip = "Clip: transparent outside the frame. Repeat: tile. Mirror: reflected tiles. " +
                "Source: inherit texture wrap modes. Clamp: extend edge pixels. " +
                "Unbounded: continue procedural UVs; raster layers use Clip.";
            toolkitHeaderBindings.Track(tiling, () => (Enum)(GetSelectedLayer()?.transform.tiling ?? TransformTilingMode.Clip));
            toolkitHeaderBindings.Add(() => tiling.SetEnabled(IsPreviewToolAvailable(PreviewTool.Transform)));
            BindPreviewSettingsRow(row, PreviewTool.Transform);
            tiling.RegisterValueChangedCallback(evt =>
            {
                Layer selected = GetSelectedLayer();
                if (selected == null || selected.IsGroup)
                    return;
                FinishPreviewTransform();
                ApplyToolkitChange("Change Transform Tiling", () => selected.transform.tiling = (TransformTilingMode)evt.newValue);
            });
            tilingGroup.Add(tiling);
            row.Add(tilingGroup);
            VisualElement filterGroup = WhimTexUI.CreateRow();
            filterGroup.AddToClassList("whimtex-transform-option");
            filterGroup.Add(CreateCompactLabel("Filter", 36f));
            EnumField filter = CompactField(new EnumField(LayerFilterMode.Source), 100f);
            filter.tooltip = "Source: inherit the texture's Filter Mode. Point: sharp pixels. Bilinear: smooth. " +
                "Trilinear: smooth mip transitions (requires source mipmaps). Independent of Tiling.";
            toolkitHeaderBindings.Track(filter, () => (Enum)(GetSelectedLayer()?.filterMode ?? LayerFilterMode.Source));
            toolkitHeaderBindings.Add(() => filter.SetEnabled(IsPreviewToolAvailable(PreviewTool.Transform)));
            filter.RegisterValueChangedCallback(evt =>
            {
                Layer selected = GetSelectedLayer();
                if (selected == null || selected.IsGroup)
                    return;
                FinishPreviewTransform();
                FinishPaintingStroke();
                ApplyToolkitChange("Change Layer Filter", () => selected.filterMode = (LayerFilterMode)evt.newValue);
            });
            filterGroup.Add(filter);
            row.Add(filterGroup);
            row.Add(WhimTexUI.CreateOriginalAspectButton(
                GetSelectedLayer, () => compositor,
                (undoName, change) =>
                {
                    FinishPreviewTransform();
                    FinishPaintingStroke();
                    ApplyToolkitChange(undoName, change);
                }, toolkitHeaderBindings));
            row.Add(WhimTexUI.CreateOriginalAspectButton(
                GetSelectedLayer, () => compositor,
                (undoName, change) =>
                {
                    FinishPreviewTransform();
                    FinishPaintingStroke();
                    ApplyToolkitChange(undoName, change);
                }, toolkitHeaderBindings, originalSize: true));
            Button reset = WhimTexUI.CreateButton("Reset", () =>
            {
                Layer selected = GetSelectedLayer();
                if (selected == null || selected.IsGroup)
                    return;
                FinishPreviewTransform();
                FinishPaintingStroke();
                TextureTransform value = selected.transform;
                value.Reset();
                if (!value.Equals(selected.transform))
                    ApplyToolkitChange("Reset Layer Transform", () => selected.transform = value);
            });
            reset.tooltip = "Reset position, scale, rotation and pivot; restore Tiling to Clip. Keep Filter unchanged.";
            toolkitHeaderBindings.Add(() => reset.SetEnabled(IsPreviewTransformEnabled));
            row.Add(reset);
            toolkitPreviewHeader.Add(row);
        }

        private void TogglePreviewTransform()
        {
            SetPreviewTool(previewTool == PreviewTool.Transform
                ? previewTransformReturnTool
                : PreviewTool.Transform);
        }

        private void SetPreviewTool(PreviewTool tool)
        {
            areaSelectionManipulator?.Cancel();
            CancelPreviewEyedropper();
            bool changePixelPreview = (previewTool == PreviewTool.Pencil) != (tool == PreviewTool.Pencil);
            CancelPreviewZoomGesture();
            FinishPreviewTransform();
            FinishPaintingStroke();
            previewTransformFX = null;
            previewTransformParameterId = null;
            if (tool == PreviewTool.Transform && previewTool != PreviewTool.Transform)
            {
                previewTransformReturnTool = previewTool;
                EditorPrefs.SetString(PreviewTransformReturnToolPrefKey, previewTransformReturnTool.ToString());
            }
            previewTool = tool;
            selectedPreviewGuide = -1;
            EditorPrefs.SetString(PreviewToolPrefKey, tool.ToString());
            previewSettingsTool = tool;
            lineAnchorLayer = null;
            if (changePixelPreview) RequestPreview(immediate: true);
            RefreshToolkitInterface();
            toolkitPreviewCanvas?.Focus();
        }

        private bool HandlePreviewTransformKey(KeyDownEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey || evt.altKey)
                return false;
            if (evt.keyCode == KeyCode.T)
                TogglePreviewTransform();
            else if (evt.keyCode == KeyCode.B)
                SetPreviewTool(PreviewTool.Brush);
            else if (evt.keyCode == KeyCode.P)
                SetPreviewTool(PreviewTool.Pencil);
            else if (evt.keyCode == KeyCode.G)
                SetPreviewTool(PreviewTool.Fill);
            else if (evt.keyCode == KeyCode.M)
                SetPreviewTool(PreviewTool.RectangleSelect);
            else if (evt.keyCode == KeyCode.L)
                SetPreviewTool(PreviewTool.PolygonSelect);
            else if (evt.keyCode == KeyCode.U)
                SetPreviewTool(PreviewTool.Shape);
            else if (evt.keyCode == KeyCode.Z)
                SetPreviewTool(PreviewTool.Zoom);
            else if (IsPreviewZoomEnabled && evt.keyCode == KeyCode.Escape)
                CancelPreviewZoomGesture();
            else if (evt.keyCode == KeyCode.V)
                SetPreviewTool(PreviewTool.None);
            else if (IsPreviewTransformEnabled && evt.keyCode == KeyCode.Escape)
            {
                if (previewTransformManipulator != null && previewTransformManipulator.IsDragging)
                    FinishPreviewTransform(true);
                else
                    TogglePreviewTransform();
            }
            else if (IsPreviewTransformEnabled && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                TogglePreviewTransform();
            else
                return false;
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private void FinishPreviewTransform(bool cancel = false)
        {
            gradientCanvasManipulator?.End(cancel);
            previewTransformManipulator?.End(cancel, true);
        }

        private void RefreshPreviewTransformTool()
        {
            gradientCanvasManipulator?.Validate();
            gradientCanvasOverlay?.MarkDirtyRepaint();
            previewTransformManipulator?.ValidateSelection();
            previewTransformOverlay?.MarkDirtyRepaint();
        }

        private void RequestTransformPreview()
        {
            double requestedAt = Math.Max(EditorApplication.timeSinceStartup, nextTransformPreviewAt);
            if (!previewRequested || previewAt > requestedAt)
                previewAt = requestedAt;
            previewRequested = true;
            previewTransformOverlay.MarkDirtyRepaint();
        }

        private sealed class PreviewTransformManipulator : PointerManipulator
        {
            private static readonly Vector2[] Handles =
            {
                new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 0.5f)
            };
            private const int MoveHandle = 8;
            private const int RotateHandle = 9;
            private const int PivotHandle = 10;
            private static float PivotSnapDistance => WhimTexUserSettings.SnapRadius;
            private static float CanvasSnapDistance => WhimTexUserSettings.SnapRadius;
            private readonly TextureCompositorWindow owner;
            private Layer layer;
            private LayerBehaviour gestureBehaviour;
            private TextureTransform original;
            private Vector2 size;
            private Vector2 pointerStart;
            private Vector2 lastPointerPosition;
            private int pointerId = -1;
            private int handle;
            private int undoGroup = -1;
            private bool lastAlt;
            private readonly Double2[] corners = new Double2[4];
            private Rect gestureImageRect;
            private ShaderFX gestureFX;
            private ShaderFXParameter gestureParameter;

            public bool IsDragging => pointerId >= 0;

            public PreviewTransformManipulator(TextureCompositorWindow owner) => this.owner = owner;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.RegisterCallback<KeyDownEvent>(OnModifierDown);
                target.RegisterCallback<KeyUpEvent>(OnModifierUp);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                End(false, true);
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.UnregisterCallback<KeyDownEvent>(OnModifierDown);
                target.UnregisterCallback<KeyUpEvent>(OnModifierUp);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            public void ValidateSelection()
            {
                if (IsDragging && (!owner.IsPreviewTransformEnabled ||
                    !ReferenceEquals(gestureParameter, owner.PreviewFXParameter) ||
                    !ReferenceEquals(layer, owner.GetSelectedLayer()) ||
                    !ReferenceEquals(gestureBehaviour, layer?.Behaviour) ||
                    size != new Vector2(owner.compositor.width, owner.compositor.height)))
                    End(false, true);
                if (owner.previewTransformFX != null && owner.PreviewFXParameter == null)
                {
                    owner.previewTransformFX = null;
                    owner.previewTransformParameterId = null;
                }
            }

            private static Vector2 Rotate(Vector2 point, float degrees)
            {
                float angle = degrees * Mathf.Deg2Rad;
                float cosine = Mathf.Cos(angle);
                float sine = Mathf.Sin(angle);
                return new Vector2(cosine * point.x - sine * point.y, sine * point.x + cosine * point.y);
            }

            private static Vector2 TransformPoint(Vector2 uv, TextureTransform transform, Vector2 dimensions)
            {
                return Vector2.Scale(transform.Map(uv, dimensions), dimensions);
            }

            private static Vector2 ToPreview(Vector2 pixels, Rect imageRect, Vector2 dimensions) =>
                new Vector2(imageRect.x + pixels.x / dimensions.x * imageRect.width,
                    imageRect.yMax - pixels.y / dimensions.y * imageRect.height);

            private static Vector2 ToDocument(Vector2 point, Rect imageRect, Vector2 dimensions) =>
                new Vector2((point.x - imageRect.x) / imageRect.width * dimensions.x,
                    (imageRect.yMax - point.y) / imageRect.height * dimensions.y);

            private static Vector2 RotationHandle(TextureTransform transform, Rect imageRect, Vector2 dimensions)
            {
                Vector2 center = ToPreview(TransformPoint(new Vector2(0.5f, 0.5f), transform, dimensions), imageRect, dimensions);
                Vector2 top = ToPreview(TransformPoint(Handles[5], transform, dimensions), imageRect, dimensions);
                Vector2 direction = top - center;
                if (direction.sqrMagnitude < 0.01f)
                    direction = Vector2.up * -1f;
                return top + direction.normalized * 24f;
            }

            private static int HitTest(Vector2 point, TextureTransform transform, Rect imageRect, Vector2 dimensions, bool showPivot = true)
            {
                Vector2 pivot = ToPreview(TransformPoint(transform.pivotF, transform, dimensions), imageRect, dimensions);
                if (showPivot && (point - pivot).sqrMagnitude <= 81f)
                    return CanMovePivot(transform) ? PivotHandle : -1;
                if ((point - RotationHandle(transform, imageRect, dimensions)).sqrMagnitude <= 81f)
                    return RotateHandle;
                int nearest = -1;
                float distance = 81f;
                for (int i = 0; i < Handles.Length; i++)
                {
                    float candidate = (point - ToPreview(TransformPoint(Handles[i], transform, dimensions), imageRect, dimensions)).sqrMagnitude;
                    if (candidate < distance)
                    {
                        nearest = i;
                        distance = candidate;
                    }
                }
                if (nearest >= 0)
                    return nearest;
                Vector2 source = transform.Unmap(Vector2.Scale(ToDocument(point,imageRect,dimensions),
                    new Vector2(1/dimensions.x,1/dimensions.y)),dimensions);
                return source.x>=0 && source.y>=0 && source.x<=1 && source.y<=1 ? MoveHandle : -1;
            }

            internal MouseCursor GetCursor(Vector2 point, bool alt)
            {
                if (!owner.IsPreviewTransformEnabled) return MouseCursor.Pan;
                point = owner.toolkitPreviewCanvas.ToCanvas(point);
                Rect rect = owner.toolkitPreviewCanvas.ImageRect;
                if (rect.width <= 0f || rect.height <= 0f) return MouseCursor.Pan;
                int hit = IsDragging ? handle : HitTest(point, owner.CurrentPreviewTransform, rect,
                    new Vector2(owner.compositor.width, owner.compositor.height), owner.PreviewFXParameter == null);
                if (hit == RotateHandle) return MouseCursor.RotateArrow;
                if (hit == PivotHandle) return MouseCursor.MoveArrow;
                return hit >= 0 && hit < Handles.Length ? MouseCursor.ScaleArrow : MouseCursor.Pan;
            }

            private void OnDown(PointerDownEvent evt)
            {
                if (!owner.IsPreviewTransformEnabled || evt.button != 0 || IsDragging)
                    return;
                Rect rect = owner.toolkitPreviewCanvas.ImageRect;
                if (rect.width <= 0f || rect.height <= 0f)
                    return;
                Layer selected = owner.GetSelectedLayer();
                Vector2 dimensions = new Vector2(owner.compositor.width, owner.compositor.height);
                Vector2 canvasPoint = owner.toolkitPreviewCanvas.ToCanvas(evt.localPosition);
                int hit = HitTest(canvasPoint, owner.CurrentPreviewTransform, rect, dimensions, owner.PreviewFXParameter == null);
                if (hit < 0)
                    return;
                owner.Focus();
                target.Focus();
                layer = selected;
                gestureBehaviour = selected.Behaviour;
                original = owner.CurrentPreviewTransform;
                lastAlt = evt.altKey;
                gestureParameter = owner.PreviewFXParameter;
                gestureFX = gestureParameter != null ? owner.previewTransformFX : null;
                size = dimensions;
                handle = hit;
                gestureImageRect = rect;
                pointerStart = ToDocument(canvasPoint, rect, size);
                lastPointerPosition = evt.localPosition;
                pointerId = evt.pointerId;
                undoGroup = -1;
                target.CapturePointer(pointerId);
                owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                evt.StopImmediatePropagation();
            }

            private void OnMove(PointerMoveEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointerId)
                    return;
                lastAlt = evt.altKey;
                UpdateTransform(evt.localPosition, evt.shiftKey, evt.ctrlKey || evt.commandKey);
                owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                evt.StopImmediatePropagation();
            }


            private static bool CanMovePivot(TextureTransform transform) =>
                TiledCanvasUtility.IsInvertible(transform);

            private void OnModifierDown(KeyDownEvent evt)
            {
                if (RefreshTransformModifiers(evt.keyCode, evt.shiftKey, evt.ctrlKey || evt.commandKey, evt.altKey))
                    evt.StopPropagation();
            }

            private void OnModifierUp(KeyUpEvent evt)
            {
                if (RefreshTransformModifiers(evt.keyCode, evt.shiftKey, evt.ctrlKey || evt.commandKey, evt.altKey))
                    evt.StopPropagation();
            }

            private bool RefreshTransformModifiers(KeyCode key, bool shift, bool control, bool alt)
            {
                if (!IsDragging ||
                    (key != KeyCode.LeftControl && key != KeyCode.RightControl &&
                     key != KeyCode.LeftShift && key != KeyCode.RightShift &&
                     key != KeyCode.LeftAlt && key != KeyCode.RightAlt &&
                     key != KeyCode.LeftCommand && key != KeyCode.RightCommand))
                    return false;
                lastAlt = alt;
                UpdateTransform(lastPointerPosition, shift, control);
                return true;
            }

            private Vector2 SnapPivot(Vector2 pivot, Vector2 documentPosition)
            {
                Vector2 previewPosition = ToPreview(documentPosition, gestureImageRect, size);
                float nearestDistance = PivotSnapDistance * PivotSnapDistance;
                Vector2 result = pivot;
                for (int i = 0; i <= Handles.Length; i++)
                {
                    Vector2 anchor = i < Handles.Length ? Handles[i] : new Vector2(0.5f, 0.5f);
                    Vector2 anchorPosition = ToPreview(TransformPoint(anchor, original, size), gestureImageRect, size);
                    float distance = (anchorPosition - previewPosition).sqrMagnitude;
                    if (distance <= nearestDistance)
                    {
                        nearestDistance = distance;
                        result = anchor;
                    }
                }
                bool intersection = owner.TrySnapPreviewGuideIntersection(documentPosition, Vector2.zero, out Vector2 guidePoint);
                if (!intersection) guidePoint = owner.SnapPreviewGuidePoint(documentPosition);
                if (intersection || (guidePoint != documentPosition &&
                    (ToPreview(guidePoint, gestureImageRect, size) - previewPosition).sqrMagnitude < nearestDistance))
                {
                    Vector2 local = Rotate(guidePoint - Vector2.Scale(original.pivotF, size) - original.positionF, -original.rotationF);
                    result = original.pivotF + new Vector2(local.x / original.scaleF.x / size.x, local.y / original.scaleF.y / size.y);
                }
                return result;
            }

            private static float CanvasEdgeOffset(float value, float extent, float tolerance)
            {
                float offset = NearestCanvasEdgeOffset(value, extent);
                return Mathf.Abs(offset) <= tolerance ? offset : 0f;
            }

            private static float NearestCanvasEdgeOffset(float value, float extent) =>
                Mathf.Abs(value) <= Mathf.Abs(extent - value) ? -value : extent - value;

            private Vector2 SnapMove(TextureTransform transform, bool horizontal, bool vertical)
            {
                Vector2 min = TransformPoint(Handles[0], transform, size);
                Vector2 max = min;
                for (int i = 2; i < Handles.Length; i += 2)
                {
                    Vector2 corner = TransformPoint(Handles[i], transform, size);
                    min = Vector2.Min(min, corner);
                    max = Vector2.Max(max, corner);
                }
                Vector2 tolerance = new Vector2(CanvasSnapDistance * size.x / gestureImageRect.width,
                    CanvasSnapDistance * size.y / gestureImageRect.height);
                Vector2 first = new Vector2(NearestCanvasEdgeOffset(min.x, size.x), NearestCanvasEdgeOffset(min.y, size.y));
                Vector2 last = new Vector2(NearestCanvasEdgeOffset(max.x, size.x), NearestCanvasEdgeOffset(max.y, size.y));
                Vector2 offset = new Vector2(Mathf.Abs(first.x) <= Mathf.Abs(last.x) ? first.x : last.x,
                    Mathf.Abs(first.y) <= Mathf.Abs(last.y) ? first.y : last.y);
                Vector2 canvasOffset = new Vector2(horizontal && Mathf.Abs(offset.x) <= tolerance.x ? offset.x : 0f,
                    vertical && Mathf.Abs(offset.y) <= tolerance.y ? offset.y : 0f);
                Vector2 center = TransformPoint(new Vector2(.5f, .5f), transform, size);
                Vector2 halfSize = new Vector2(Mathf.Abs(transform.scaleF.x) * size.x * .5f, Mathf.Abs(transform.scaleF.y) * size.y * .5f);
                return owner.SnapPreviewGuideMove(center, Rotate(Vector2.right, transform.rotationF), halfSize, canvasOffset, horizontal, vertical);
            }

            private Vector2 SnapResize(Vector2 point, Vector2 direction, bool free)
            {
                Vector2 pixelsToPreview = new Vector2(gestureImageRect.width / size.x, gestureImageRect.height / size.y);
                if (free)
                {
                    Vector2 canvasPoint = point + new Vector2(CanvasEdgeOffset(point.x, size.x, CanvasSnapDistance / pixelsToPreview.x),
                        CanvasEdgeOffset(point.y, size.y, CanvasSnapDistance / pixelsToPreview.y));
                    return owner.SnapPreviewGuideResize(point, direction, true, Rotate(Vector2.right, original.rotationF), canvasPoint);
                }

                Vector2 result = point;
                float nearest = CanvasSnapDistance * CanvasSnapDistance;
                for (int axis = 0; axis < 2; axis++)
                {
                    if (Mathf.Abs(direction[axis]) < 0.00001f) continue;
                    for (int edge = 0; edge < 2; edge++)
                    {
                        Vector2 offset = direction * ((edge * size[axis] - point[axis]) / direction[axis]);
                        float distance = Vector2.Scale(offset, pixelsToPreview).sqrMagnitude;
                        if (distance > nearest) continue;
                        nearest = distance;
                        result = point + offset;
                    }
                }
                return owner.SnapPreviewGuideResize(point, direction, false, Rotate(Vector2.right, original.rotationF), result);
            }

            private void UpdateTransform(Vector2 point, bool constrain, bool disableSnap)
            {
                ValidateSelection();
                if (!IsDragging)
                    return;
                lastPointerPosition = point;
                point = owner.toolkitPreviewCanvas.ToCanvas(point);
                Vector2 current = ToDocument(point, gestureImageRect, size);
                Vector2 delta = current - pointerStart;
                if (undoGroup < 0 && delta.sqrMagnitude < 0.000001f)
                    return;
                TextureTransform next = original;
                bool complex = gestureFX == null && (original.storage == TransformStorage.Projective || (disableSnap && handle < MoveHandle));
                if (complex)
                {
                    if (!UpdateProjective(current, delta, constrain, disableSnap, out next)) return;
                }
                else if (handle == MoveHandle || handle == PivotHandle)
                {
                    if (constrain)
                    {
                        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) delta.y = 0f;
                        else delta.x = 0f;
                    }
                    if (handle == PivotHandle)
                    {
                        var localDelta=ProjectiveMatrix.Rotate(-original.rotation).Point(delta);
                        next.pivot=new Double2(original.pivot.x+localDelta.x/original.scale.x/size.x,
                            original.pivot.y+localDelta.y/original.scale.y/size.y);
                        if(!disableSnap)
                        {
                            Vector2 proposed=next.pivot;
                            Vector2 snapped=SnapPivot(proposed,TransformPoint(original.pivot,original,size)+delta);
                            if(snapped!=proposed)next.pivot=snapped;
                        }
                        var dp=new Double2((next.pivot.x-original.pivot.x)*size.x,(next.pivot.y-original.pivot.y)*size.y);
                        next.position=original.position+ProjectiveMatrix.Rotate(original.rotation).Point(new Double2(dp.x*original.scale.x,dp.y*original.scale.y))-dp;
                    }
                    else
                    {
                        next.position = original.position + (Double2)delta;
                        if (!disableSnap)
                            next.position += (Double2)SnapMove(next, !constrain || delta.x != 0f, !constrain || delta.y != 0f);
                    }
                }
                else if (handle == RotateHandle)
                {
                    Vector2 pivot = Vector2.Scale(original.pivotF, size) + original.positionF;
                    Vector2 from = pointerStart - pivot;
                    Vector2 to = current - pivot;
                    if (from.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f)
                        return;
                    next.rotation = original.rotation + Math.Atan2((double)from.x*to.y-(double)from.y*to.x, (double)from.x*to.x+(double)from.y*to.y)*180/Math.PI;
                    if (constrain)
                        next.rotation = Math.Round(next.rotation / 15d) * 15d;
                    else if (!disableSnap)
                    {
                        float snapped = owner.SnapPreviewGuideRotation(next.rotationF);
                        if (snapped != next.rotationF) next.rotation = snapped;
                    }
                }
                else
                {
                    Double2 grip=Handles[handle],anchor=lastAlt ? original.pivot : new Double2(1-grip.x,1-grip.y);
                    var m=original.ToMatrix(size.x,size.y);
                    var fixedUv=m.Point(anchor);
                    var fixedPoint=new Double2(fixedUv.x*size.x,fixedUv.y*size.y);
                    var gripUv=m.Point(grip);
                    var handlePoint=new Double2(gripUv.x*size.x+delta.x,gripUv.y*size.y+delta.y);
                    var inverseRotation=ProjectiveMatrix.Rotate(-original.rotation);
                    var local=inverseRotation.Point(handlePoint-fixedPoint);
                    var span=new Double2((grip.x-anchor.x)*size.x,(grip.y-anchor.y)*size.y);
                    bool x=grip.x!=.5 && Math.Abs(span.x)>1e-12;
                    bool y=grip.y!=.5 && Math.Abs(span.y)>1e-12;
                    if(x) next.scale.x=SafeDoubleScale(local.x/span.x);
                    if(y) next.scale.y=SafeDoubleScale(local.y/span.y);
                    if(constrain)
                    {
                        double rx=next.scale.x/original.scale.x,ry=next.scale.y/original.scale.y;
                        double ratio=x && (!y || Math.Abs(rx-1)>=Math.Abs(ry-1)) ? rx : ry;
                        next.scale=new Double2(SafeDoubleScale(original.scale.x*ratio),SafeDoubleScale(original.scale.y*ratio));
                    }
                    if(!disableSnap)
                    {
                        Vector2 actual=fixedPoint+ProjectiveMatrix.Rotate(original.rotation).Point(new Double2(span.x*next.scale.x,span.y*next.scale.y));
                        Vector2 direction=constrain ? (Vector2)ProjectiveMatrix.Rotate(original.rotation).Point(new Double2(span.x*original.scale.x,span.y*original.scale.y))
                            : Rotate(x?Vector2.right:Vector2.up,original.rotationF);
                        Vector2 snapped=SnapResize(actual,direction,x && y && !constrain);
                        if(snapped!=actual)
                        {
                            var sl=inverseRotation.Point((Double2)snapped-fixedPoint);
                            if(constrain)
                            {
                                var os=new Double2(span.x*original.scale.x,span.y*original.scale.y);
                                double ratio=(sl.x*os.x+sl.y*os.y)/(os.x*os.x+os.y*os.y);
                                next.scale=new Double2(SafeDoubleScale(original.scale.x*ratio),SafeDoubleScale(original.scale.y*ratio));
                            }
                            else
                            {
                                if(x)next.scale.x=SafeDoubleScale(sl.x/span.x);
                                if(y)next.scale.y=SafeDoubleScale(sl.y/span.y);
                            }
                        }
                    }
                    var pivot=new Double2(original.pivot.x*size.x,original.pivot.y*size.y);
                    var offset=new Double2((anchor.x*size.x-pivot.x)*next.scale.x,(anchor.y*size.y-pivot.y)*next.scale.y);
                    next.position=fixedPoint-pivot-ProjectiveMatrix.Rotate(original.rotation).Point(offset);
                }
                TextureTransform currentValue = owner.CurrentPreviewTransform;
                if (next.Equals(currentValue))
                    return;
                if (undoGroup < 0)
                {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    string undoName = gestureFX != null ? "Transform FX Area" : handle == PivotHandle ? "Move Layer Pivot" : "Transform Layer";
                    Undo.SetCurrentGroupName(undoName);
                    Undo.RegisterCompleteObjectUndo(gestureFX != null ? (UnityEngine.Object)gestureFX : owner.compositor, undoName);
                }
                WriteTransform(next);
                if (handle == PivotHandle)
                    owner.previewTransformOverlay.MarkDirtyRepaint();
                else
                    owner.RequestTransformPreview();
            }

            private static double SafeDoubleScale(double value) => value < 0 ? Math.Min(value,-1e-5) : Math.Max(value,1e-5);

            private bool UpdateProjective(Vector2 current, Vector2 delta, bool shift, bool control, out TextureTransform next)
            {
                next = original;
                var m = original.ToMatrix(size.x,size.y);
                if (!m.TryInverse(out var inv)) return false;
                var uv = new Double2(current.x/size.x,current.y/size.y);
                var move = new Double2(delta.x/size.x,delta.y/size.y);
                if (handle == PivotHandle)
                {
                    if (!inv.TryPoint(uv,out var pivot)) return false;
                    next.pivot=pivot;
                    return true;
                }
                if (handle == MoveHandle)
                {
                    if (shift) { if (Math.Abs(delta.x)>=Math.Abs(delta.y)) move.y=0; else move.x=0; }
                    return next.TrySetMatrix(ProjectiveMatrix.Translate(move.x,move.y)*m);
                }
                if (handle == RotateHandle)
                {
                    Vector2 pivot=Vector2.Scale(m.Point(original.pivot),size);
                    Vector2 from=pointerStart-pivot,to=current-pivot;
                    double angle=Math.Atan2((double)from.x*to.y-(double)from.y*to.x,(double)from.x*to.x+(double)from.y*to.y)*180/Math.PI;
                    if (shift) angle=Math.Round(angle/15)*15;
                    next.AroundPivot(ProjectiveMatrix.Rotate(angle),size);
                    return true;
                }
                if (control)
                {
                    corners[0]=m.Point(new Double2(0,0)); corners[1]=m.Point(new Double2(1,0));
                    corners[2]=m.Point(new Double2(1,1)); corners[3]=m.Point(new Double2(0,1));
                    int i=handle/2;
                    if ((handle&1)!=0)
                    {
                        int j=(i+1)%4;
                        var edge=corners[j]-corners[i];
                        double t=(move.x*edge.x+move.y*edge.y)/(edge.x*edge.x+edge.y*edge.y);
                        var slide=edge*t;
                        corners[i]=corners[i]+slide; corners[j]=corners[j]+slide;
                    }
                    else
                    {
                        bool horizontal=Math.Abs(delta.x)>=Math.Abs(delta.y);
                        if (shift) { if(horizontal) move.y=0; else move.x=0; }
                        corners[i]=corners[i]+move;
                        if (lastAlt && shift)
                        {
                            int j=horizontal ? (i^1) : (3-i);
                            corners[j]=corners[j]-move;
                        }
                        else if (lastAlt) corners[(i+2)%4]=corners[(i+2)%4]-move;
                    }
                    if (!ProjectiveMatrix.TryQuad(corners[0],corners[1],corners[2],corners[3],out var result)) return false;
                    double before=m.m00*(m.m11*m.m22-m.m12*m.m21)-m.m01*(m.m10*m.m22-m.m12*m.m20)+m.m02*(m.m10*m.m21-m.m11*m.m20);
                    double after=result.m00*(result.m11*result.m22-result.m12*result.m21)-result.m01*(result.m10*result.m22-result.m12*result.m20)+result.m02*(result.m10*result.m21-result.m11*result.m20);
                    return before*after>0 && next.TrySetMatrix(result);
                }
                Double2 grip=Handles[handle], anchor=lastAlt ? original.pivot : new Double2(1-grip.x,1-grip.y);
                if (!inv.TryPoint(m.Point(grip)+move,out var local)) return false;
                double sx=grip.x==.5 ? 1 : (local.x-anchor.x)/(grip.x-anchor.x);
                double sy=grip.y==.5 ? 1 : (local.y-anchor.y)/(grip.y-anchor.y);
                if(shift) sx=sy=Math.Abs(sx-1)>=Math.Abs(sy-1)?sx:sy;
                return next.TrySetMatrix(m*ProjectiveMatrix.Translate(anchor.x,anchor.y)*ProjectiveMatrix.Scale(sx,sy)*ProjectiveMatrix.Translate(-anchor.x,-anchor.y));
            }

            private void OnUp(PointerUpEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointerId || evt.button != 0)
                    return;
                lastAlt = evt.altKey;
                UpdateTransform(evt.localPosition, evt.shiftKey, evt.ctrlKey || evt.commandKey);
                End(false, true);
                owner.UpdatePreviewCursor(evt.localPosition, evt.altKey);
                evt.StopImmediatePropagation();
            }

            private void OnCaptureOut(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId == pointerId)
                    End(false, true);
            }

            private void OnDetach(DetachFromPanelEvent evt) => End(false, true);

            private void WriteTransform(TextureTransform value)
            {
                if (gestureParameter != null)
                    gestureParameter.transformValue = ShaderFXTransform.FromLayerTransform(value, size);
                else if (layer != null) layer.transform = value;
            }

            public void End(bool cancel, bool commit)
            {
                if (!IsDragging)
                    return;
                int captured = pointerId;
                pointerId = -1;
                if (target.HasPointerCapture(captured))
                    target.ReleasePointer(captured);
                if (commit && undoGroup >= 0 && owner.compositor != null)
                {
                    if (cancel)
                        WriteTransform(original);
                    if (gestureFX != null)
                    {
                        EditorUtility.SetDirty(gestureFX);
                        gestureFX.NotifyValuesChanged();
                    }
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.IncrementCurrentGroup();
                    owner.applyingToolkitChange = true;
                    try { owner.CommitModelChange(); }
                    finally { owner.applyingToolkitChange = false; }
                    owner.lineAnchorLayer = null;
                    owner.RequestPreview(true);
                    owner.toolkitRefreshRequested = true;
                }
                layer = null;
                gestureBehaviour = null;
                gestureFX = null;
                gestureParameter = null;
                undoGroup = -1;
                owner.previewTransformOverlay?.MarkDirtyRepaint();
                if (owner.previewTool == PreviewTool.Transform) owner.RefreshPreviewPointerCursor();
            }

            public void Draw(MeshGenerationContext context)
            {
                if (!owner.IsPreviewTransformEnabled)
                    return;
                Rect rect = owner.toolkitPreviewCanvas.ImageRect;
                if (rect.width <= 0f || rect.height <= 0f)
                    return;
                TextureTransform transform = owner.CurrentPreviewTransform;
                bool fxTransform = owner.PreviewFXParameter != null;
                Vector2 dimensions = new Vector2(owner.compositor.width, owner.compositor.height);
                Vector2 ViewPoint(Vector2 pixels) => owner.toolkitPreviewCanvas.ToView(ToPreview(pixels, rect, dimensions));
                Vector2 rotationHandle = owner.toolkitPreviewCanvas.ToView(RotationHandle(transform, rect, dimensions));
                Painter2D painter = context.painter2D;
                for (int pass = 0; pass < 2; pass++)
                {
                    painter.lineWidth = pass == 0 ? 3f : 1f;
                    painter.strokeColor = pass == 0 ? new Color(0f, 0f, 0f, 0.85f) : fxTransform ? new Color(0.35f, 1f, 0.5f) : new Color(0.35f, 0.75f, 1f);
                    painter.BeginPath();
                    painter.MoveTo(ViewPoint(TransformPoint(Handles[0], transform, dimensions)));
                    for (int i = 2; i < Handles.Length; i += 2)
                        painter.LineTo(ViewPoint(TransformPoint(Handles[i], transform, dimensions)));
                    painter.ClosePath();
                    painter.Stroke();
                    painter.BeginPath();
                    painter.MoveTo(ViewPoint(TransformPoint(Handles[5], transform, dimensions)));
                    painter.LineTo(rotationHandle);
                    painter.Stroke();
                }
                painter.lineWidth = 1f;
                painter.strokeColor = Color.black;
                painter.fillColor = fxTransform ? new Color(0.35f, 1f, 0.5f) : Color.white;
                for (int i = 0; i < Handles.Length; i++)
                {
                    Vector2 p = ViewPoint(TransformPoint(Handles[i], transform, dimensions));
                    painter.BeginPath();
                    painter.MoveTo(p + new Vector2(-3f, -3f));
                    painter.LineTo(p + new Vector2(3f, -3f));
                    painter.LineTo(p + new Vector2(3f, 3f));
                    painter.LineTo(p + new Vector2(-3f, 3f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
                painter.BeginPath();
                painter.Arc(rotationHandle, 4f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
                painter.Stroke();
                if (fxTransform) return;
                Vector2 center = ViewPoint(TransformPoint(transform.pivotF, transform, dimensions));
                for (int pass = 0; pass < 2; pass++)
                {
                    painter.lineWidth = pass == 0 ? 3f : 1f;
                    painter.strokeColor = pass == 0 ? Color.black :
                        CanMovePivot(transform) ? new Color(1f, 0.78f, 0.2f) : Color.gray;
                    painter.BeginPath();
                    painter.Arc(center, 6f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    painter.Stroke();
                    painter.BeginPath();
                    painter.MoveTo(center - new Vector2(8f, 0f));
                    painter.LineTo(center + new Vector2(8f, 0f));
                    painter.MoveTo(center - new Vector2(0f, 8f));
                    painter.LineTo(center + new Vector2(0f, 8f));
                    painter.Stroke();
                }
            }
        }
    }
}
