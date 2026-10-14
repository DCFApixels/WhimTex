using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [Serializable]
        private sealed class TextToolSettings
        {
            public string fontFamily = "";
            public FontStyle fontStyle;
            public TextCasing casing;
            public TextEdgeMode edgeMode = TextEdgeMode.HintedAA;
            public float fontSize = 64;
            public TextAnchor alignment = TextAnchor.UpperLeft;
            public bool justify;
            public Color color = Color.white;
            internal string ResolvedFont => !string.IsNullOrEmpty(fontFamily) ? fontFamily : SystemFontCatalog.DefaultName;
            internal void SetFontSize(float value) => fontSize = Mathf.Clamp(float.IsFinite(value) ? value : 64, 1, 2048);
        }
        [SerializeField] private TextToolSettings textToolSettings = new TextToolSettings();
        private TextManipulator textManipulator;
        private Button canvasTextButton;
        private TextField canvasTextInput;
        private CanvasElement textInputCanvas;
        private IVisualElementScheduledItem textFocusRequest;
        private WhimTexDocument textEditingDocument;
        private Layer textEditingLayer;
        private TextLayerBehaviour textEditingBehaviour;
        private string textBeforeEditing, editorFontFamily;
        private FontAsset editorFont;
        private int editorFontRevision = -1;
        private int textUndoGroup = -1;

        private void BuildTextTool()
        {
            EndTextEditing(false);
            textManipulator?.Cancel();
            if (textInputCanvas != null) textInputCanvas.ViewChanged -= RefreshTextEditing;
            textInputCanvas = toolkitCanvas;
            textToolSettings ??= new TextToolSettings();
            textManipulator = new TextManipulator(this);
            toolkitCanvas.AddManipulator(textManipulator);
            canvasTextInput = new TextField { name = "canvasTextInput", multiline = true, maxLength = TextLayerBehaviour.MaxCharacters };
            canvasTextInput.AddToClassList("whimtex-canvas-text-input"); canvasTextInput.AddToClassList("whimtex-hidden");
            toolkitCanvas.Add(canvasTextInput);
            var input = canvasTextInput;
            input.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (ReferenceEquals(canvasTextInput, input)) EndTextEditing(false);
            });
            canvasTextInput.RegisterValueChangedCallback(e =>
            {
                if (!TextEditingValid) return;
                if (textUndoGroup < 0) { Undo.IncrementCurrentGroup(); textUndoGroup = Undo.GetCurrentGroup(); }
                InspectorChangeFor(textEditingLayer)("Edit Text", () => textEditingBehaviour.text = e.newValue);
            });
            canvasTextInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape || (e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.Return)
                { EndTextEditing(e.keyCode == KeyCode.Escape); toolkitCanvas.Focus(); WhimTexUI.ConsumeEvent(e); }
            }, TrickleDown.TrickleDown);
            textInputCanvas.ViewChanged += RefreshTextEditing;
        }

        private bool TextEditingValid => textEditingLayer != null && activeDocument == textEditingDocument &&
            ReferenceEquals(activeDocument.FindLayer(textEditingLayer.Id), textEditingLayer) && ReferenceEquals(textEditingLayer.Behaviour, textEditingBehaviour) &&
            ReferenceEquals(GetSelectedLayer(), textEditingLayer) && canvasTool == CanvasTool.Text && !WhimTexApi.IsLayerContentLocked(activeDocument, textEditingLayer) && textEditingBehaviour.FontAvailable;

        private void RefreshTextEditing()
        {
            if (textEditingLayer == null || canvasTextInput == null) return;
            if (!TextEditingValid) { EndTextEditing(false); return; }
            var text = textEditingBehaviour;
            if (editorFontFamily != text.ResolvedFont || editorFontRevision != SystemFontCatalog.Revision)
            {
                canvasTextInput.style.unityFontDefinition = StyleKeyword.Null;
                SystemFontPreview.Destroy(editorFont);
                editorFontFamily = text.ResolvedFont; editorFont = SystemFontPreview.Create(editorFontFamily, 64);
                editorFontRevision = SystemFontCatalog.Revision;
                if (editorFont != null) canvasTextInput.style.unityFontDefinition = FontDefinition.FromSDFFont(editorFont);
            }
            Rect bounds = text.GetLayoutBounds(activeDocument.width, activeDocument.height);
            float pixelScale = text.Owner.GetPixelScale();
            if (text.layoutMode == TextLayoutMode.Point) bounds.width = Mathf.Max(bounds.width + text.fontSize * pixelScale, text.fontSize * pixelScale * 3);
            var dimensions = new Vector2(activeDocument.width, activeDocument.height);
            TextureTransform transform = activeDocument.GetCanvasTransform(textEditingLayer);
            Vector2 View(Vector2 p)
            {
                Vector2 uv = transform.Map(new Vector2(p.x / dimensions.x + .5f, p.y / dimensions.y + .5f), dimensions);
                Rect image = toolkitCanvas.ImageRect;
                return toolkitCanvas.ToView(new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height));
            }
            Vector2 origin = View(new Vector2(bounds.xMin, bounds.yMax));
            Vector2 right = View(new Vector2(bounds.xMax, bounds.yMax)) - origin;
            Vector2 down = View(new Vector2(bounds.xMin, bounds.yMin)) - origin;
            float determinant = right.x * down.y - right.y * down.x;
            if (!TextPointFinite(origin) || !TextPointFinite(right) || !TextPointFinite(down) ||
                !float.IsFinite(determinant) || Mathf.Abs(determinant) < .000001f || right.magnitude < .001f || down.magnitude < .001f)
            { canvasTextInput.EnableInClassList("whimtex-hidden", true); return; }
            float fontSize = text.GetFontSize(activeDocument.width, activeDocument.height) * pixelScale;
            float horizontalScale = text.characterHorizontalScale;
            canvasTextInput.style.left = origin.x; canvasTextInput.style.top = origin.y;
            canvasTextInput.style.width = bounds.width / horizontalScale;
            canvasTextInput.style.height = text.layoutMode == TextLayoutMode.Frame ? bounds.height : Mathf.Max(bounds.height, fontSize * 1.25f);
            canvasTextInput.style.transformOrigin = new TransformOrigin(0, 0, 0);
            canvasTextInput.style.rotate = new Rotate(new Angle(Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg));
            float sign = determinant < 0 ? -1 : 1;
            canvasTextInput.style.scale = new Scale(new Vector3(right.magnitude / bounds.width * horizontalScale, sign * down.magnitude / bounds.height, 1));
            canvasTextInput.style.fontSize = fontSize;
            canvasTextInput.style.letterSpacing = text.spacing.character * fontSize / horizontalScale;
            canvasTextInput.style.wordSpacing = text.spacing.word * fontSize / horizontalScale;
            canvasTextInput.style.unityParagraphSpacing = text.spacing.paragraph * fontSize;
            canvasTextInput.style.unityFontStyleAndWeight = text.fontStyle;
            canvasTextInput.style.unityTextAlign = text.LayoutAlignment;
            canvasTextInput.EnableInClassList("whimtex-canvas-text-input--manual", text.layoutMode == TextLayoutMode.Point || text.wrapping == TextWrapping.Manual);
            if (canvasTextInput.value != text.text) canvasTextInput.SetValueWithoutNotify(text.text ?? "");
            canvasTextInput.EnableInClassList("whimtex-hidden", false);
            canvasTextInput.BringToFront();
        }

        private static bool TextPointFinite(Vector2 point) => float.IsFinite(point.x) && float.IsFinite(point.y);

        private void EndTextEditing(bool cancel)
        {
            textFocusRequest?.Pause(); textFocusRequest = null;
            var document = textEditingDocument; var layer = textEditingLayer; var behaviour = textEditingBehaviour;
            textEditingLayer = null; textEditingDocument = null; textEditingBehaviour = null;
            if (cancel && document != null && ReferenceEquals(document.FindLayer(layer.Id), layer) && ReferenceEquals(layer.Behaviour, behaviour) && activeDocument == document)
                ApplyToolkitChange("Cancel Text Edit", () => behaviour.text = textBeforeEditing);
            if (textUndoGroup >= 0) { Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(textUndoGroup); Undo.IncrementCurrentGroup(); }
            textUndoGroup = -1;
            canvasTextInput?.EnableInClassList("whimtex-hidden", true);
            if (canvasTextInput != null) canvasTextInput.style.unityFontDefinition = StyleKeyword.Null;
            SystemFontPreview.Destroy(editorFont); editorFont = null; editorFontFamily = null; editorFontRevision = -1;
        }

        private void ChangeTextToolSettings(Action<TextToolSettings> change)
        {
            textManipulator?.Cancel();
            change(textToolSettings);
            toolkitHeaderBindings.Refresh();
        }

        private void AddTextSettings()
        {
            textToolSettings ??= new TextToolSettings();
            var row = CreateCanvasSettingsRow(); BindCanvasSettingsRow(row, CanvasTool.Text);
            var font = new SystemFontField("Font") { name = "textToolFont" };
            font.AddToClassList("whimtex-text-tool-font"); row.Add(font);
            toolkitHeaderBindings.Track(font, () => textToolSettings.ResolvedFont ?? "");
            toolkitHeaderBindings.Add(font.RefreshPreview);
            font.RegisterValueChangedCallback(e => ChangeTextToolSettings(settings => settings.fontFamily = e.newValue));
            var size = new FloatField("Size") { name = "textToolSize" }; size.AddToClassList("whimtex-view-field"); row.Add(size);
            toolkitHeaderBindings.Track(size, () => textToolSettings.fontSize);
            size.RegisterValueChangedCallback(e => ChangeTextToolSettings(settings => settings.SetFontSize(e.newValue)));
            var style = new TextStyleField { name = "textToolStyle" }; row.Add(style);
            toolkitHeaderBindings.Track(style, () => textToolSettings.fontStyle);
            style.RegisterValueChangedCallback(e => ChangeTextToolSettings(settings => settings.fontStyle = e.newValue));
            var casing = new TextCasingField { name = "textToolCasing" }; row.Add(casing);
            toolkitHeaderBindings.Track(casing, () => textToolSettings.casing);
            casing.RegisterValueChangedCallback(e => ChangeTextToolSettings(settings => settings.casing = e.newValue));
            var edges = new UnityEngine.UIElements.EnumField("Edges", textToolSettings.edgeMode) { name = "textToolEdgeMode" };
            edges.AddToClassList("whimtex-view-field"); row.Add(edges);
            toolkitHeaderBindings.Track(edges, () => (Enum)textToolSettings.edgeMode);
            edges.RegisterValueChangedCallback(e => ChangeTextToolSettings(settings => settings.edgeMode = (TextEdgeMode)e.newValue));
            var alignment = new TextAlignmentField { name = "textToolAlignment" }; row.Add(alignment);
            alignment.SetCanJustify(true);
            toolkitHeaderBindings.Track(alignment, () => new TextAlignmentField.Settings(textToolSettings.alignment, textToolSettings.justify));
            alignment.RegisterValueChangedCallback(e => ChangeTextToolSettings(settings =>
                { settings.alignment = e.newValue.alignment; settings.justify = e.newValue.justify; }));
            var color = WhimTexColorInputs.Bind(new WhimTexColorField("Color") { name = "textToolColor" }, toolkitHeaderBindings, () => textToolSettings.color);
            color.AddToClassList("whimtex-shape-color"); row.Add(color);
            color.RegisterValueChangedCallback(e => ChangeTextToolSettings(settings => settings.color = e.newValue));
            var edit = new Button(() => FocusTextInput(false)) { text = "Edit Text" }; row.Add(edit);
            var done = new Button(() => { EndTextEditing(false); toolkitCanvas?.Focus(); }) { text = "Done", tooltip = "Finish editing (Ctrl+Enter)" }; row.Add(done);
            var cancel = new Button(() => { EndTextEditing(true); toolkitCanvas?.Focus(); }) { text = "Cancel", tooltip = "Discard this text edit (Escape)" }; row.Add(cancel);
            toolkitHeaderBindings.Add(() =>
            {
                bool locked = GetSelectedLayer() is Layer selected && WhimTexApi.IsLayerContentLocked(activeDocument, selected);
                edit.SetEnabled(!locked && GetSelectedLayer()?.Behaviour is TextLayerBehaviour);
                textManipulator?.Repaint();
                RefreshTextEditing();
                done.EnableInClassList("whimtex-hidden", textEditingLayer == null); cancel.EnableInClassList("whimtex-hidden", textEditingLayer == null);
            });
            toolkitCanvasViewHeader.Add(row);
        }

        private void FocusTextInput(bool selectAll)
        {
            if (!(GetSelectedLayer()?.Behaviour is TextLayerBehaviour) || WhimTexApi.IsLayerContentLocked(activeDocument, GetSelectedLayer())) return;
            var layer = GetSelectedLayer(); var text = (TextLayerBehaviour)layer.Behaviour;
            if (!text.FontAvailable) { ShowNotification(new GUIContent("Choose an installed font before editing text.")); return; }
            if (!ReferenceEquals(layer, textEditingLayer))
            {
                EndTextEditing(false); textEditingDocument = activeDocument; textEditingLayer = layer;
                textEditingBehaviour = text; textBeforeEditing = text.text; textUndoGroup = -1;
            }
            RefreshTextEditing();
            textFocusRequest?.Pause();
            var input = canvasTextInput;
            textFocusRequest = input.schedule.Execute(() =>
            {
                textFocusRequest = null;
                if (TextEditingValid && ReferenceEquals(textEditingLayer, layer) && ReferenceEquals(canvasTextInput, input) &&
                    !input.ClassListContains("whimtex-hidden"))
                { input.Focus(); if (selectAll) input.SelectAll(); }
            });
            toolkitHeaderBindings.Refresh();
        }

        private void CreateText(Vector2 start, Vector2 end, bool frame, Layer insertionAnchor)
        {
            if (activeDocument == null) return;
            var settings = textToolSettings;
            float pixelScale = activeDocument.GetPixelScale();
            var behaviour = new TextLayerBehaviour { text = "Text", fontFamily = settings.ResolvedFont ?? "", fontSize = settings.fontSize,
                fontStyle = settings.fontStyle, casing = settings.casing, edgeMode = settings.edgeMode, color = settings.color, alignment = settings.alignment,
                layoutMode = frame ? TextLayoutMode.Frame : TextLayoutMode.Point, justify = settings.justify };
            if (frame) behaviour.frameSize = new Vector2(Mathf.Clamp(Mathf.Abs(end.x - start.x) / pixelScale, 1, 32768),
                Mathf.Clamp(Mathf.Abs(end.y - start.y) / pixelScale, 1, 32768));
            behaviour.SetAlignment(behaviour.alignment, behaviour.justify);
            var layer = new Layer(behaviour);
            Vector2 position = (frame ? (start + end) * .5f : start) - new Vector2(activeDocument.width, activeDocument.height) * .5f;
            if (frame) behaviour.SetLayoutOffset(position / pixelScale);
            else layer.transform.position = position;
            if (behaviour.color.maxColorComponent > 1) { layer.colorRange = LayerColorRange.HDR; layer.blendRange = LayerBlendRange.HDR; }
            List<Layer> container = activeDocument.layers; int index = 0;
            if (insertionAnchor != null && activeDocument.TryFindLayer(insertionAnchor, out var selectedContainer, out int selectedIndex))
            { container = selectedContainer; index = selectedIndex; }
            activeDocument.PlaceCanvasTransform(layer, container);
            AddLayer(container, index, layer, "Text");
            FocusTextInput(true);
        }

        private sealed class TextManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private readonly VisualElement overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            private const int MoveHandle = 8;
            private int pointer = -1, handle = -1;
            private Vector2 start, current, startView, dimensions;
            private WhimTexDocument document;
            private Layer anchor;
            private Rect originalRect;
            private TextureTransform original;
            private Layer moved;
            private TextLayerBehaviour movedText;
            private Vector2 moveOriginalOffset, moveLastOffset;
            private Vector2 originalFrameSize, lastFrameSize;
            private string moveLayoutKey;
            private Vector2 moveAnchor;
            private int moveUndoGroup = -1;
            private float pixelScale = 1f;
            internal bool IsDragging => pointer >= 0;
            internal TextManipulator(WhimTexWindow owner)
            {
                this.owner = owner; overlay.AddToClassList("whimtex-area-overlay"); overlay.generateVisualContent += Draw;
            }
            internal void Repaint() => overlay.MarkDirtyRepaint();
            protected override void RegisterCallbacksOnTarget()
            {
                target.Add(overlay);
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted); target.RegisterCallback<DetachFromPanelEvent>(Detached);
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                owner.toolkitCanvas.ViewChanged += Repaint;
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel(); target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted); target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                owner.toolkitCanvas.ViewChanged -= Repaint; overlay.RemoveFromHierarchy();
            }
            private Vector2 Point(Vector2 position, bool control)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                Vector2 p = owner.toolkitCanvas.ToCanvas(position);
                p = new Vector2((p.x - image.x) / image.width * dimensions.x, (image.yMax - p.y) / image.height * dimensions.y);
                return control ? p : owner.SnapCanvasGuidePoint(p);
            }
            private Vector2 View(Vector2 pixels)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                return owner.toolkitCanvas.ToView(new Vector2(image.x + pixels.x / dimensions.x * image.width, image.yMax - pixels.y / dimensions.y * image.height));
            }
            private Vector2 Local(Vector2 point, TextureTransform transform) => Vector2.Scale(transform.Unmap(new Vector2(point.x / dimensions.x, point.y / dimensions.y), dimensions) - Vector2.one * .5f, dimensions);
            private Vector2 World(Vector2 local, TextureTransform transform) => Vector2.Scale(transform.Map(new Vector2(local.x / dimensions.x + .5f, local.y / dimensions.y + .5f), dimensions), dimensions);
            private static Vector2 Corner(Rect rect, int index) => new Vector2(index == 0 || index == 3 ? rect.xMin : rect.xMax, index < 2 ? rect.yMin : rect.yMax);
            private static Vector2 FrameHandle(Rect rect, int index) => index < 4 ? Corner(rect, index) : index == MoveHandle ? rect.center :
                (Corner(rect, index - 4) + Corner(rect, (index - 3) % 4)) * .5f;
            private int HitFrame(Vector2 view, Rect rect, TextureTransform transform)
            {
                int hit = -1; float distance = 64;
                for (int i = 0; i <= MoveHandle; i++)
                {
                    float candidate = (View(World(FrameHandle(rect, i), transform)) - view).sqrMagnitude;
                    if (candidate < distance) { distance = candidate; hit = i; }
                }
                return hit;
            }
            internal bool TryGetCursor(Vector2 view, out MouseCursor cursor)
            {
                cursor = MouseCursor.Pan;
                if (owner.textEditingLayer != null || owner.activeDocument == null ||
                    !(owner.GetSelectedLayer()?.Behaviour is TextLayerBehaviour text) || !text.FontAvailable ||
                    WhimTexApi.IsLayerContentLocked(owner.activeDocument, text.Owner)) return false;
                if (IsDragging && !Valid) return false;
                if (!IsDragging) dimensions = new Vector2(owner.activeDocument.width, owner.activeDocument.height);
                var transform = owner.activeDocument.GetCanvasTransform(text.Owner);
                if (!transform.ToMatrix(dimensions.x, dimensions.y).TryInverse(out _)) return false;
                int hit = IsDragging ? handle : text.layoutMode == TextLayoutMode.Frame
                    ? HitFrame(view, text.GetLayoutBounds((int)dimensions.x, (int)dimensions.y), transform)
                    : (View(World(text.GetPointAnchor((int)dimensions.x, (int)dimensions.y), transform)) - view).sqrMagnitude <= 64 ? MoveHandle : -1;
                if (hit < 0) return false;
                cursor = hit == MoveHandle ? MouseCursor.MoveArrow : MouseCursor.ScaleArrow;
                return true;
            }
            private void Down(PointerDownEvent e)
            {
                if (IsDragging || owner.canvasTool != CanvasTool.Text || owner.activeDocument == null || e.button != 0 || e.altKey ||
                    e.target != target || !target.contentRect.Contains(e.localPosition) || owner.toolkitCanvas.PixelScale <= 0) return;
                if (owner.textEditingLayer != null)
                {
                    owner.EndTextEditing(false); target.Focus(); WhimTexUI.ConsumeEvent(e); return;
                }
                owner.FinishPaintingStroke(); owner.FinishCanvasTransform(); owner.Focus(); target.Focus();
                owner.EndTextEditing(false);
                document = owner.activeDocument; dimensions = new Vector2(document.width, document.height); anchor = owner.GetSelectedLayer();
                pixelScale = document.GetPixelScale();
                startView = e.localPosition; start = current = Point(startView, e.ctrlKey);
                if (!TextPointFinite(start)) { Cancel(); return; }
                handle = -1;
                if (!e.ctrlKey && anchor?.Behaviour is TextLayerBehaviour text && !WhimTexApi.IsLayerContentLocked(document, anchor))
                {
                    original = document.GetCanvasTransform(anchor);
                    Rect rect = text.GetLayoutBounds(document.width, document.height);
                    if (text.FontAvailable && original.ToMatrix(dimensions.x, dimensions.y).TryInverse(out _))
                        handle = text.layoutMode == TextLayoutMode.Frame ? HitFrame(startView, rect, original) :
                            (View(World(text.GetPointAnchor(document.width, document.height), original)) - startView).sqrMagnitude <= 64 ? MoveHandle : -1;
                    if (handle >= 0)
                    {
                        moved = anchor; movedText = text;
                        originalRect = new Rect(rect.position / pixelScale, rect.size / pixelScale);
                        originalFrameSize = lastFrameSize = text.frameSize;
                        moveOriginalOffset = moveLastOffset = text.layoutOffset;
                        moveLayoutKey = text.LayoutKey(document.width, document.height);
                        moveAnchor = World(text.layoutMode == TextLayoutMode.Point ? text.GetPointAnchor(document.width, document.height) : FrameHandle(rect, handle), original);
                        start = current = Point(startView, true);
                    }
                    else if (moved == null && rect.Contains(Local(start, original))) { Cancel(); owner.FocusTextInput(false); WhimTexUI.ConsumeEvent(e); return; }
                }
                if (!e.ctrlKey && moved == null)
                {
                    var hit = document.PickLayerAtPixel(Mathf.FloorToInt(start.x), Mathf.FloorToInt(start.y), WhimTexUserSettings.LayerPickAlphaThreshold, true, null);
                    if (hit?.Behaviour is TextLayerBehaviour && !WhimTexApi.IsLayerContentLocked(document, hit))
                    { owner.SelectOnlyLayer(hit.Id); owner.RefreshToolkitInterface(); Cancel(); owner.FocusTextInput(false); WhimTexUI.ConsumeEvent(e); return; }
                }
                pointer = e.pointerId; target.CapturePointer(pointer); Repaint(); WhimTexUI.ConsumeEvent(e);
            }
            private bool Valid => document != null && document == owner.activeDocument && owner.canvasTool == CanvasTool.Text && dimensions == new Vector2(document.width, document.height) &&
                document.GetPixelScale() == pixelScale &&
                ReferenceEquals(owner.GetSelectedLayer(), anchor) &&
                (moved == null || ReferenceEquals(document.FindLayer(moved.Id), moved) && ReferenceEquals(moved.Behaviour, movedText) &&
                movedText.LayoutKey(document.width, document.height) == moveLayoutKey &&
                document.GetCanvasTransform(moved).Equals(original) && movedText.FontAvailable && !WhimTexApi.IsLayerContentLocked(document, moved));
            private Rect ResizeFrame(Vector2 delta, bool proportional)
            {
                bool left = handle == 0 || handle == 3 || handle == 7, right = handle == 1 || handle == 2 || handle == 5;
                bool bottom = handle == 0 || handle == 1 || handle == 4, top = handle == 2 || handle == 3 || handle == 6;
                Vector2 size = originalFrameSize;
                if (left || right) size.x = Mathf.Clamp(size.x + (left ? -delta.x : delta.x), 1, 32768);
                if (bottom || top) size.y = Mathf.Clamp(size.y + (bottom ? -delta.y : delta.y), 1, 32768);
                if (proportional && handle < 4)
                {
                    float x = size.x / originalFrameSize.x, y = size.y / originalFrameSize.y;
                    float scale = Mathf.Abs(x - 1) >= Mathf.Abs(y - 1) ? x : y;
                    scale = Mathf.Clamp(scale, Mathf.Max(1 / originalFrameSize.x, 1 / originalFrameSize.y), Mathf.Min(32768 / originalFrameSize.x, 32768 / originalFrameSize.y));
                    size = originalFrameSize * scale;
                }
                return new Rect(new Vector2(left ? originalRect.xMax - size.x : originalRect.xMin,
                    bottom ? originalRect.yMax - size.y : originalRect.yMin), size);
            }
            private bool Update(Vector2 position, bool control, bool shift = false)
            {
                current = Point(position, moved != null || control);
                if (!TextPointFinite(current)) return false;
                if (moved != null)
                {
                    if (moveUndoGroup < 0 && (position - startView).sqrMagnitude < 9) return true;
                    Vector2 delta = current - start;
                    if (shift && handle == MoveHandle) { if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) delta.y = 0; else delta.x = 0; }
                    if (!control) delta = owner.SnapCanvasGuidePoint(moveAnchor + delta) - moveAnchor;
                    Vector2 localDelta = (Local(moveAnchor + delta, original) - Local(moveAnchor, original)) / pixelScale;
                    Vector2 next = moveOriginalOffset + localDelta, nextSize = originalFrameSize;
                    if (handle != MoveHandle)
                    {
                        Rect rect = ResizeFrame(localDelta, shift);
                        next = rect.center; nextSize = rect.size;
                    }
                    if (!TextPointFinite(next)) return false;
                    next = new Vector2(Mathf.Clamp(next.x, -1000000, 1000000), Mathf.Clamp(next.y, -1000000, 1000000));
                    if (next != moveLastOffset || nextSize != lastFrameSize)
                    {
                        if (moveUndoGroup < 0)
                        {
                            Undo.IncrementCurrentGroup(); moveUndoGroup = Undo.GetCurrentGroup();
                            string label = handle == MoveHandle ? "Move Text Layout" : "Resize Text Frame";
                            Undo.SetCurrentGroupName(label); Undo.RegisterCompleteObjectUndo(document, label);
                        }
                        movedText.layoutOffset = moveLastOffset = next;
                        movedText.frameSize = lastFrameSize = nextSize;
                        moveLayoutKey = movedText.LayoutKey(document.width, document.height);
                        owner.RequestTransformCanvas();
                    }
                }
                Repaint();
                return true;
            }
            private void Move(PointerMoveEvent e)
            {
                if (!IsDragging || e.pointerId != pointer) return;
                if (!Valid || (e.pressedButtons & 1) == 0 || !Update(e.localPosition, e.ctrlKey, e.shiftKey)) Cancel();
                WhimTexUI.ConsumeEvent(e);
            }
            private void Up(PointerUpEvent e)
            {
                if (!IsDragging || e.pointerId != pointer || e.button != 0) return;
                if (Valid && Update(e.localPosition, e.ctrlKey, e.shiftKey))
                {
                    if (moved != null)
                    {
                        FinishLayoutEdit(false); Cancel(); WhimTexUI.ConsumeEvent(e); return;
                    }
                    var insertion = anchor; Vector2 a = start, b = current;
                    bool drag = ((Vector2)e.localPosition - startView).sqrMagnitude >= 9;
                    Cancel();
                    owner.CreateText(a, b, drag, insertion);
                }
                else Cancel();
                WhimTexUI.ConsumeEvent(e);
            }
            internal void Cancel()
            {
                FinishLayoutEdit(true);
                int captured = pointer; pointer = -1; document = null; anchor = null; handle = -1;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                Repaint();
            }
            private void FinishLayoutEdit(bool cancel)
            {
                if (moved == null) return;
                if (moveUndoGroup >= 0)
                {
                    if (cancel && document != null && ReferenceEquals(document.FindLayer(moved.Id), moved) &&
                        ReferenceEquals(moved.Behaviour, movedText))
                    {
                        if (movedText.layoutOffset == moveLastOffset) movedText.layoutOffset = moveOriginalOffset;
                        if (movedText.frameSize == lastFrameSize) movedText.frameSize = originalFrameSize;
                    }
                    Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(moveUndoGroup); Undo.IncrementCurrentGroup();
                    if (document != null && document == owner.activeDocument)
                    {
                        owner.applyingToolkitChange = true;
                        try { owner.CommitModelChange(); }
                        finally { owner.applyingToolkitChange = false; }
                        owner.RequestCanvasRender(true); owner.toolkitRefreshRequested = true;
                    }
                }
                moved = null; movedText = null; moveLayoutKey = null; moveUndoGroup = -1;
            }
            private void Lost(PointerCaptureOutEvent e) { if (e.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent e) => Cancel();
            private void Detached(DetachFromPanelEvent e) => Cancel();
            private void Key(KeyDownEvent e) { if (IsDragging && e.keyCode == KeyCode.Escape) { Cancel(); WhimTexUI.ConsumeEvent(e); } }
            private void Draw(MeshGenerationContext context)
            {
                if (owner.canvasTool != CanvasTool.Text || owner.activeDocument == null) return;
                if (IsDragging && !Valid) return;
                if (!IsDragging) dimensions = new Vector2(owner.activeDocument.width, owner.activeDocument.height);
                Rect rect; TextureTransform transform; bool handles;
                if (IsDragging && Valid && moved == null)
                {
                    rect = Rect.MinMaxRect(Mathf.Min(start.x, current.x), Mathf.Min(start.y, current.y), Mathf.Max(start.x, current.x), Mathf.Max(start.y, current.y));
                    transform = TextureTransform.Default; handles = false;
                }
                else if (owner.GetSelectedLayer()?.Behaviour is TextLayerBehaviour text)
                {
                    transform = owner.activeDocument.GetCanvasTransform(text.Owner);
                    if (text.layoutMode == TextLayoutMode.Point)
                    {
                        DrawPoint(context.painter2D, text, transform);
                        return;
                    }
                    rect = text.GetLayoutBounds(owner.activeDocument.width, owner.activeDocument.height);
                    handles = text.FontAvailable && !WhimTexApi.IsLayerContentLocked(owner.activeDocument, text.Owner) && transform.ToMatrix(dimensions.x, dimensions.y).TryInverse(out _);
                }
                else return;
                var p = context.painter2D; p.lineWidth = 1; p.strokeColor = new Color(.3f, .85f, 1, .9f); p.fillColor = p.strokeColor;
                p.BeginPath();
                for (int i = 0; i < 4; i++)
                {
                    Vector2 point = Corner(rect, i);
                    point = View(IsDragging && moved == null ? point : World(point, transform));
                    if (!TextPointFinite(point)) return;
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.ClosePath(); p.Stroke();
                if (handles) for (int i = 0; i <= MoveHandle; i++)
                {
                    Vector2 point = View(World(FrameHandle(rect, i), transform));
                    if (!TextPointFinite(point)) continue;
                    if (i == MoveHandle)
                    {
                        p.BeginPath(); p.Arc(point, 4, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill();
                        continue;
                    }
                    p.BeginPath(); p.MoveTo(point + new Vector2(-3, -3)); p.LineTo(point + new Vector2(3, -3));
                    p.LineTo(point + new Vector2(3, 3)); p.LineTo(point + new Vector2(-3, 3)); p.ClosePath(); p.Fill();
                }
            }
            private void DrawPoint(Painter2D painter, TextLayerBehaviour text, TextureTransform transform)
            {
                painter.lineWidth = 1; painter.strokeColor = new Color(.3f, .85f, 1, .9f);
                foreach (var line in text.GetPointLines(owner.activeDocument.width, owner.activeDocument.height))
                {
                    Vector2 a = View(World(new Vector2(line.x, line.z), transform)), b = View(World(new Vector2(line.y, line.z), transform));
                    if (!TextPointFinite(a) || !TextPointFinite(b) || (b - a).sqrMagnitude < .01f) continue;
                    painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke();
                }
                if (!text.FontAvailable || WhimTexApi.IsLayerContentLocked(owner.activeDocument, text.Owner) || !transform.ToMatrix(dimensions.x, dimensions.y).TryInverse(out _)) return;
                Vector2 point = View(World(text.GetPointAnchor(owner.activeDocument.width, owner.activeDocument.height), transform));
                if (!TextPointFinite(point)) return;
                painter.fillColor = painter.strokeColor;
                painter.BeginPath(); painter.Arc(point, 4, Angle.Degrees(0), Angle.Degrees(360)); painter.ClosePath(); painter.Fill();
            }
        }
    }
}
