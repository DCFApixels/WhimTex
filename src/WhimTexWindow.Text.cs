using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
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
        private Font editorFont;
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
            if (editorFont == null || editorFontFamily != text.ResolvedFont || editorFontRevision != SystemFontCatalog.Revision)
            {
                if (editorFont != null) DestroyImmediate(editorFont);
                editorFontFamily = text.ResolvedFont; editorFont = Font.CreateDynamicFontFromOSFont(editorFontFamily, 64);
                editorFontRevision = SystemFontCatalog.Revision;
                editorFont.hideFlags = HideFlags.HideAndDontSave;
                canvasTextInput.style.unityFontDefinition = FontDefinition.FromFont(editorFont);
            }
            Rect bounds = text.GetLayoutBounds(activeDocument.width, activeDocument.height);
            if (text.layoutMode == TextLayoutMode.Point) bounds.width = Mathf.Max(bounds.width + text.fontSize, text.fontSize * 3);
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
            float fontSize = text.GetFontSize(activeDocument.width, activeDocument.height);
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
            if (editorFont != null) DestroyImmediate(editorFont); editorFont = null; editorFontFamily = null; editorFontRevision = -1;
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
            var behaviour = new TextLayerBehaviour { text = "Text", fontFamily = settings.ResolvedFont ?? "", fontSize = settings.fontSize,
                fontStyle = settings.fontStyle, casing = settings.casing, color = settings.color, alignment = settings.alignment,
                layoutMode = frame ? TextLayoutMode.Frame : TextLayoutMode.Point, justify = settings.justify };
            if (frame) behaviour.frameSize = new Vector2(Mathf.Clamp(Mathf.Abs(end.x - start.x), 1, 32768), Mathf.Clamp(Mathf.Abs(end.y - start.y), 1, 32768));
            behaviour.SetAlignment(behaviour.alignment, behaviour.justify);
            var layer = new Layer(behaviour);
            layer.transform.position = (frame ? (start + end) * .5f : start) - new Vector2(activeDocument.width, activeDocument.height) * .5f;
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
            private int pointer = -1, corner = -1;
            private Vector2 start, current, startView, dimensions;
            private WhimTexDocument document;
            private Layer anchor, resized;
            private TextLayerBehaviour resizedText;
            private Rect originalRect;
            private TextureTransform original;
            private Rect resizedRect;
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
                startView = e.localPosition; start = current = Point(startView, e.ctrlKey);
                if (!TextPointFinite(start)) { Cancel(); return; }
                corner = -1;
                if (!e.ctrlKey && anchor?.Behaviour is TextLayerBehaviour text && !WhimTexApi.IsLayerContentLocked(document, anchor))
                {
                    original = document.GetCanvasTransform(anchor);
                    Rect rect = text.GetLayoutBounds(document.width, document.height);
                    if (text.layoutMode == TextLayoutMode.Frame)
                        for (int i = 0; i < 4; i++) if ((View(World(Corner(rect, i), original)) - startView).sqrMagnitude <= 64) { corner = i; break; }
                    if (corner >= 0) { resized = anchor; resizedText = text; originalRect = resizedRect = rect; }
                    else if (rect.Contains(Local(start, original))) { Cancel(); owner.FocusTextInput(false); WhimTexUI.ConsumeEvent(e); return; }
                }
                if (!e.ctrlKey && resized == null)
                {
                    var hit = document.PickLayerAtPixel(Mathf.FloorToInt(start.x), Mathf.FloorToInt(start.y), WhimTexUserSettings.LayerPickAlphaThreshold, true, null);
                    if (hit?.Behaviour is TextLayerBehaviour && !WhimTexApi.IsLayerContentLocked(document, hit))
                    { owner.SelectOnlyLayer(hit.Id); owner.RefreshToolkitInterface(); Cancel(); owner.FocusTextInput(false); WhimTexUI.ConsumeEvent(e); return; }
                }
                pointer = e.pointerId; target.CapturePointer(pointer); Repaint(); WhimTexUI.ConsumeEvent(e);
            }
            private bool Valid => document != null && document == owner.activeDocument && owner.canvasTool == CanvasTool.Text && dimensions == new Vector2(document.width, document.height) &&
                ReferenceEquals(owner.GetSelectedLayer(), anchor) &&
                (resized == null || ReferenceEquals(document.FindLayer(resized.Id), resized) && ReferenceEquals(resized.Behaviour, resizedText) &&
                resizedText.layoutMode == TextLayoutMode.Frame && resizedText.frameSize == originalRect.size &&
                document.GetCanvasTransform(resized).Equals(original) && !WhimTexApi.IsLayerContentLocked(document, resized));
            private bool Update(Vector2 position, bool control)
            {
                current = Point(position, control);
                if (!TextPointFinite(current)) return false;
                if (resized != null)
                {
                    Vector2 fixedPoint = Corner(originalRect, (corner + 2) % 4), moved = Local(current, original);
                    if (!TextPointFinite(moved)) return false;
                    Vector2 delta = moved - fixedPoint;
                    delta.x = Mathf.Clamp(Mathf.Abs(delta.x), 1, 32768) * (corner == 0 || corner == 3 ? -1 : 1);
                    delta.y = Mathf.Clamp(Mathf.Abs(delta.y), 1, 32768) * (corner < 2 ? -1 : 1);
                    moved = fixedPoint + delta;
                    resizedRect = Rect.MinMaxRect(Mathf.Min(fixedPoint.x, moved.x), Mathf.Min(fixedPoint.y, moved.y), Mathf.Max(fixedPoint.x, moved.x), Mathf.Max(fixedPoint.y, moved.y));
                }
                Repaint();
                return true;
            }
            private void Move(PointerMoveEvent e)
            {
                if (!IsDragging || e.pointerId != pointer) return;
                if (!Valid || (e.pressedButtons & 1) == 0 || !Update(e.localPosition, e.ctrlKey)) Cancel();
                WhimTexUI.ConsumeEvent(e);
            }
            private void Up(PointerUpEvent e)
            {
                if (!IsDragging || e.pointerId != pointer || e.button != 0) return;
                if (Valid && Update(e.localPosition, e.ctrlKey))
                {
                    var layer = resized; var insertion = anchor; Vector2 a = start, b = current;
                    bool drag = ((Vector2)e.localPosition - startView).sqrMagnitude >= 9;
                    Rect rect = resizedRect; var transform = original;
                    var text = resizedText; var size = dimensions;
                    Cancel();
                    if (layer != null && drag)
                        owner.ExecuteModelChange("Resize Text Frame", () =>
                        {
                            if (transform.TrySetMatrix(transform.ToMatrix(size.x, size.y) * ProjectiveMatrix.Translate(rect.center.x / size.x, rect.center.y / size.y)) &&
                                owner.activeDocument.SetCanvasTransform(layer, transform)) text.frameSize = rect.size;
                        });
                    else if (layer == null) owner.CreateText(a, b, drag, insertion);
                }
                else Cancel();
                WhimTexUI.ConsumeEvent(e);
            }
            internal void Cancel()
            {
                int captured = pointer; pointer = -1; document = null; anchor = null; resized = null; resizedText = null; corner = -1;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                Repaint();
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
                if (IsDragging && Valid && resized == null)
                {
                    rect = Rect.MinMaxRect(Mathf.Min(start.x, current.x), Mathf.Min(start.y, current.y), Mathf.Max(start.x, current.x), Mathf.Max(start.y, current.y));
                    transform = TextureTransform.Default; handles = false;
                }
                else if (owner.GetSelectedLayer()?.Behaviour is TextLayerBehaviour text)
                {
                    transform = owner.activeDocument.GetCanvasTransform(text.Owner);
                    rect = resized != null ? resizedRect : text.GetLayoutBounds(owner.activeDocument.width, owner.activeDocument.height);
                    handles = text.layoutMode == TextLayoutMode.Frame && !WhimTexApi.IsLayerContentLocked(owner.activeDocument, text.Owner);
                }
                else return;
                var p = context.painter2D; p.lineWidth = 1; p.strokeColor = new Color(.3f, .85f, 1, .9f); p.fillColor = p.strokeColor;
                p.BeginPath();
                for (int i = 0; i < 4; i++)
                {
                    Vector2 point = Corner(rect, i);
                    point = View(IsDragging && resized == null ? point : World(point, transform));
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.ClosePath(); p.Stroke();
                if (handles) for (int i = 0; i < 4; i++)
                {
                    Vector2 point = View(World(Corner(rect, i), transform));
                    p.BeginPath(); p.MoveTo(point + new Vector2(-3, -3)); p.LineTo(point + new Vector2(3, -3));
                    p.LineTo(point + new Vector2(3, 3)); p.LineTo(point + new Vector2(-3, 3)); p.ClosePath(); p.Fill();
                }
            }
        }
    }
}
