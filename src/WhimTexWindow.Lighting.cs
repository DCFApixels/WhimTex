using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private bool lightsExpanded;
        private string selectedLightId;
        private readonly HashSet<string> selectedLightIds = new(StringComparer.Ordinal);
        private WhimTexDocument lightSelectionDocument;
        private Button lightsTab, editLightsButton;
        private VisualElement lightsDrawer, lightsContent, lightsOverlay;
        private LightCanvasManipulator lightManipulator;
        private WhimTexDocument lightsBoundDocument;
        private string lightsBoundStructure;
        private WhimTexLight lightsBoundSelection;
        private readonly List<Action> lightsBindings = new List<Action>();
        private WhimTexLight SelectedLight => activeDocument?.lights?.Find(light => light.id == selectedLightId);

        private void BuildLightsTab(VisualElement tabs)
        {
            lightsTab = CreateCanvasDrawerTab("lightsTab", "Lights", new CanvasPreviewPanelIcon(CanvasPreviewPanelIcon.Kind.Lights), () =>
            {
                lightsExpanded = !lightsExpanded;
                if (lightsExpanded) brushesExpanded = commentsExpanded = postFxExpanded = uvExpanded = false;
                else if (canvasTool == CanvasTool.Lights) ExitContextTool();
                RefreshPostFxPanel();
                RefreshLightsPanel();
            });
            tabs.Add(lightsTab);
        }

        private void BuildLightsDrawer(VisualElement parent)
        {
            lightsDrawer = new VisualElement { name = "lightsDrawer", focusable = true };
            lightsDrawer.AddToClassList("whimtex-post-fx-drawer");
            editLightsButton = AddCanvasDrawerActivationHeader(lightsDrawer, "Lights", "lightsTitle", "editLights", "Edit on Canvas", () => SetCanvasTool(CanvasTool.Lights));
            lightsContent = new ScrollView(ScrollViewMode.Vertical);
            lightsContent.AddToClassList("whimtex-post-fx-settings");
            lightsDrawer.Add(lightsContent);
            parent.Add(lightsDrawer);
            lightsBoundDocument = null;
            lightsBoundStructure = null;
        }

        private void RefreshLightsPanel()
        {
            NormalizeLightSelection();
            if (lightsDrawer == null) return;
            if (brushesExpanded || commentsExpanded || postFxExpanded || uvExpanded) lightsExpanded = false;
            if (!lightsExpanded && canvasTool == CanvasTool.Lights) ChangeCanvasTool(lastBaseCanvasTool);
            RefreshCanvasDrawerTab(lightsTab, lightsExpanded);
            lightsDrawer.EnableInClassList("whimtex-post-fx-drawer--hidden", !lightsExpanded);
            lightsOverlay?.MarkDirtyRepaint();
            editLightsButton.SetEnabled(activeDocument != null && activeDocument.lights.Count > 0);
            editLightsButton.text = canvasTool == CanvasTool.Lights ? "Done" : "Edit on Canvas";
            if (!lightsExpanded) return;
            string structure = activeDocument == null ? "" : string.Join("|", activeDocument.lights.ConvertAll(light => light.id));
            if (lightsBoundDocument != activeDocument || lightsBoundStructure != structure || lightsBoundSelection != SelectedLight)
            {
                lightsBoundDocument = activeDocument; lightsBoundStructure = structure; lightsBoundSelection = SelectedLight;
                RebuildLightsFields();
            }
            foreach (var binding in lightsBindings) binding();
        }

        private void NormalizeLightSelection()
        {
            if (lightSelectionDocument != activeDocument)
            {
                selectedLightIds.Clear();
                lightSelectionDocument = activeDocument;
                selectedLightId = activeDocument?.lights?.Count > 0 ? activeDocument.lights[0].id : null;
                if (selectedLightId != null) selectedLightIds.Add(selectedLightId);
            }
            selectedLightIds.RemoveWhere(id => activeDocument?.lights?.Exists(item => item.id == id) != true);
            if (selectedLightId == null || !selectedLightIds.Contains(selectedLightId))
                selectedLightId = activeDocument?.lights?.Find(item => selectedLightIds.Contains(item.id))?.id;
        }

        private void SelectLight(string id, bool toggle)
        {
            NormalizeLightSelection();
            if (id != null && activeDocument?.lights?.Exists(item => item.id == id) != true) return;
            if (!toggle) selectedLightIds.Clear();
            if (id != null && (!toggle || !selectedLightIds.Remove(id)))
            {
                selectedLightIds.Add(id);
                selectedLightId = id;
            }
            NormalizeLightSelection();
            RefreshLightsPanel();
        }

        private void DeleteSelectedLights()
        {
            if (lightManipulator?.IsDragging == true) return;
            NormalizeLightSelection();
            if (selectedLightIds.Count == 0) return;
            var ids = new HashSet<string>(selectedLightIds);
            if (!ExecuteContextChange(ids.Count == 1 ? "Remove Light" : "Remove Lights", () =>
            {
                WhimTexDocumentJson.RestoreMissingAssets(activeDocument);
                activeDocument.lights.RemoveAll(item => ids.Contains(item.id));
                WhimTexDocumentJson.CaptureMissingAssets(activeDocument);
            })) return;
            selectedLightIds.Clear(); selectedLightId = null;
            RefreshLightsPanel();
        }

        private bool HandleLightSelectionKey(KeyDownEvent evt)
        {
            if (evt.target is not VisualElement element || IsTextInputTarget(element) ||
                IsTextInputTarget(rootVisualElement.panel?.focusController?.focusedElement as VisualElement)) return false;
            bool onCanvas = canvasTool == CanvasTool.Lights && toolkitCanvas != null &&
                (element == toolkitCanvas || toolkitCanvas.Contains(element));
            bool inDrawer = lightsExpanded && lightsDrawer != null &&
                (element == lightsDrawer || lightsDrawer.Contains(element));
            if (!onCanvas && !inDrawer) return false;
            if (evt.keyCode == KeyCode.Escape && lightManipulator?.IsDragging == true)
            {
                lightManipulator.End(true); WhimTexUI.ConsumeEvent(evt); return true;
            }
            if (evt.keyCode != KeyCode.Delete || evt.ctrlKey || evt.commandKey || evt.altKey) return false;
            WhimTexUI.ConsumeEvent(evt);
            DeleteSelectedLights();
            return true;
        }

        private void EditLight(Action change, string undoName = "Edit Light")
        {
            if (activeDocument == null) return;
            Undo.RecordObject(activeDocument, undoName);
            WhimTexDocumentJson.RestoreMissingAssets(activeDocument);
            change();
            WhimTexDocumentJson.CaptureMissingAssets(activeDocument);
            CommitModelChange();
            RefreshLightsPanel();
        }

        private void RebuildLightsFields()
        {
            lightsContent.Clear(); lightsBindings.Clear();
            if (activeDocument == null) return;
            Field(new ColorField("Ambient Color") { hdr = true, showAlpha = false },
                () => activeDocument.ambientColor, value => activeDocument.ambientColor = value);
            foreach (var light in activeDocument.lights)
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var enabled = new Toggle { value = light.enabled };
                enabled.RegisterValueChangedCallback(e => EditLight(() => light.enabled = e.newValue));
                row.Add(enabled);
                var select = new Button { text = light.name, iconImage = Background.FromVectorImage(LightListIcons.Get(light.type, selectedLightIds.Contains(light.id))) };
                select.clickable.activators.Add(new ManipulatorActivationFilter { button = 0, modifiers = EventModifiers.Control });
                select.clickable.activators.Add(new ManipulatorActivationFilter { button = 0, modifiers = EventModifiers.Command });
                select.clickable.clickedWithEventInfo += evt =>
                {
                    bool toggle = evt is IPointerEvent p ? p.ctrlKey || p.commandKey :
                        evt is IMouseEvent m ? m.ctrlKey || m.commandKey :
                        evt is IKeyboardEvent k && (k.ctrlKey || k.commandKey);
                    SelectLight(light.id, toggle);
                    lightsDrawer.Focus();
                };
                select.style.flexGrow = 1;
                row.Add(select); lightsContent.Add(row);
                lightsBindings.Add(() =>
                {
                    enabled.SetValueWithoutNotify(light.enabled); select.text = light.name;
                    bool selected = selectedLightIds.Contains(light.id);
                    select.iconImage = Background.FromVectorImage(LightListIcons.Get(light.type, selected));
                    select.EnableInClassList("whimtex-tool-button--selected", selected);
                });
            }
            var add = new VisualElement(); add.style.flexDirection = FlexDirection.Row;
            foreach (WhimTexLightType type in Enum.GetValues(typeof(WhimTexLightType)))
            {
                var button = new Button(() =>
                {
                    EditLight(() => SelectLight(activeDocument.AddLight(type).id, false), "Add Light");
                    if (canvasTool != CanvasTool.Lights) SetCanvasTool(CanvasTool.Lights);
                }) { text = "+ " + type };
                button.style.flexGrow = 1; button.SetEnabled(activeDocument.lights.Count < WhimTexDocument.MaximumLights);
                add.Add(button);
            }
            lightsContent.Add(add);
            var selected = SelectedLight;
            if (selected == null) return;
            lightsContent.Add(WhimTexUI.CreateHeading("Selected Light"));
            Field(new TextField("Name") { isDelayed = true }, () => selected.name, value => selected.name = value);
            Field(new EnumField("Type", selected.type), () => (Enum)selected.type, value =>
            {
                var next = (WhimTexLightType)value;
                selected.direction = selected.DirectionForType(next);
                selected.type = next;
            });
            Field(new ColorField("Color") { hdr = true, showAlpha = false }, () => selected.color, value => selected.color = value);
            Field(new FloatField("Intensity"), () => selected.intensity, value => selected.intensity = value);
            Field(new Vector3Field("Position (px)"), () => selected.position, value => selected.position = value);
            var direction = Field(new Vector3Field("Direction"), () => selected.direction, value => selected.direction = value);
            var range = Field(new FloatField("Range (px)"), () => selected.range, value => selected.range = value);
            var falloff = Field(new FloatField("Falloff"), () => selected.falloff, value => selected.falloff = value);
            var angle = Field(new FloatField("Angle (deg)"), () => selected.angle, value => selected.angle = Mathf.Clamp(value, 1, 179));
            var softness = Field(new Slider("Softness", 0, 1) { showInputField = true }, () => selected.softness, value => selected.softness = value);
            var shape = Field(new EnumField("Shape", selected.areaShape), () => (Enum)selected.areaShape, value => selected.areaShape = (WhimTexAreaLightShape)value);
            var size = Field(new Vector2Field("Size (px)"), () => selected.size, value => selected.size = Vector2.Max(value, Vector2.one * .001f));
            var diameter = Field(new FloatField("Diameter (px)"), () => selected.size.x, value => selected.size = Vector2.one * Mathf.Max(.001f, value));
            var cookie = Field(new ObjectField("Cookie") { objectType = typeof(Texture2D), allowSceneObjects = false },
                () => (UnityEngine.Object)selected.cookie, value => WhimTexDocumentJson.SetLightCookie(selected, (Texture2D)value));
            var rotation = Field(new FloatField("Rotation (deg)"), () => selected.rotation, value => selected.rotation = value);
            lightsBindings.Add(() =>
            {
                direction.style.display = selected.type != WhimTexLightType.Point ? DisplayStyle.Flex : DisplayStyle.None;
                range.style.display = falloff.style.display = selected.type != WhimTexLightType.Directional ? DisplayStyle.Flex : DisplayStyle.None;
                angle.style.display = softness.style.display = selected.type == WhimTexLightType.Spot ? DisplayStyle.Flex : DisplayStyle.None;
                bool area = selected.type == WhimTexLightType.Area;
                shape.style.display = area ? DisplayStyle.Flex : DisplayStyle.None;
                size.style.display = area && selected.areaShape == WhimTexAreaLightShape.Rectangle ? DisplayStyle.Flex : DisplayStyle.None;
                diameter.style.display = area && selected.areaShape == WhimTexAreaLightShape.Disk ? DisplayStyle.Flex : DisplayStyle.None;
                cookie.style.display = selected.type == WhimTexLightType.Spot ? DisplayStyle.Flex : DisplayStyle.None;
                rotation.style.display = area || selected.type == WhimTexLightType.Spot && selected.cookie != null ? DisplayStyle.Flex : DisplayStyle.None;
            });
            var remove = new Button(DeleteSelectedLights);
            lightsBindings.Add(() => remove.text = selectedLightIds.Count > 1 ? "Remove Lights" : "Remove Light");
            lightsContent.Add(remove);
        }

        private BaseField<T> Field<T>(BaseField<T> field, Func<T> get, Action<T> set)
        {
            WhimTexUI.ConfigureField(field);
            field.SetValueWithoutNotify(get());
            field.RegisterValueChangedCallback(e => EditLight(() => set(e.newValue)));
            lightsBindings.Add(() => field.SetValueWithoutNotify(get()));
            lightsContent.Add(field);
            return field;
        }

        private static class LightListIcons
        {
            private static readonly Dictionary<(WhimTexLightType, bool), VectorImage> images = new();

            static LightListIcons()
            {
                AssemblyReloadEvents.beforeAssemblyReload += Clear;
                EditorApplication.quitting += Clear;
            }

            internal static VectorImage Get(WhimTexLightType type, bool filled)
            {
                var key = (type, filled);
                if (images.TryGetValue(key, out var image) && image != null) return image;
                using var painter = new Painter2D();
                painter.fillColor = Color.clear; painter.BeginPath();
                CanvasHandlePainter.Square(painter, new Vector2(8, 8), 8); painter.Fill();
                DrawLightIcon(painter, type, new Vector2(8, 8), Color.white, filled, .5f, 1);
                image = ScriptableObject.CreateInstance<VectorImage>();
                image.name = "WhimTex " + type + (filled ? " filled icon" : " outline icon"); image.hideFlags = HideFlags.HideAndDontSave;
                painter.SaveToVectorImage(image);
                images[key] = image;
                return image;
            }

            private static void Clear()
            {
                foreach (var image in images.Values) UnityEngine.Object.DestroyImmediate(image);
                images.Clear();
            }
        }

        private static void DrawLightIcon(Painter2D p, WhimTexLightType type, Vector2 center,
            Color color, bool filled, float scale, float width, float outlineWidth = 0)
        {
            Vector2 P(float x, float y) => center + new Vector2(x, y) * scale;
            Vector2 F(float x, float y) => P((x + y) * .70710678f, (y - x) * .70710678f);
            void Line(float x1, float y1, float x2, float y2) => CanvasHandlePainter.Line(p, P(x1, y1), P(x2, y2));
            void Stroke(bool fill = false)
            {
                if (outlineWidth > 0)
                {
                    p.strokeColor = new Color(.08f, .08f, .08f, color.a);
                    p.lineWidth = width + outlineWidth * 2; p.Stroke();
                }
                if (fill) { p.fillColor = color; p.Fill(); }
                p.strokeColor = color; p.lineWidth = width; p.Stroke();
            }
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            switch (type)
            {
                case WhimTexLightType.Directional:
                    CanvasHandlePainter.Circle(p, center, 7 * scale);
                    break;
                case WhimTexLightType.Point:
                    p.MoveTo(P(-3, 5));
                    p.BezierCurveTo(P(-3, 1), P(-8, 0), P(-8, -5));
                    p.BezierCurveTo(P(-8, -9), P(-4, -12), P(0, -12));
                    p.BezierCurveTo(P(4, -12), P(8, -9), P(8, -5));
                    p.BezierCurveTo(P(8, 0), P(3, 1), P(3, 5));
                    p.ClosePath();
                    break;
                case WhimTexLightType.Spot:
                    p.MoveTo(F(-11, -3)); p.LineTo(F(-1, -3));
                    p.LineTo(F(2, -5)); p.LineTo(F(5, -5));
                    p.LineTo(F(5, 5)); p.LineTo(F(2, 5));
                    p.LineTo(F(-1, 3)); p.LineTo(F(-11, 3)); p.ClosePath();
                    break;
                case WhimTexLightType.Area:
                    p.MoveTo(P(-11, 4)); p.LineTo(P(11, 4));
                    p.LineTo(P(11, 8)); p.LineTo(P(-11, 8)); p.ClosePath();
                    break;
            }
            Stroke(filled);
            p.BeginPath();
            switch (type)
            {
                case WhimTexLightType.Directional:
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4;
                        Vector2 ray = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        CanvasHandlePainter.Line(p, center + ray * (10 * scale), center + ray * (13 * scale));
                    }
                    break;
                case WhimTexLightType.Point:
                    Line(-3, 8, 3, 8); Line(-1.5f, 11, 1.5f, 11);
                    if (!filled)
                    {
                        p.MoveTo(P(-2, -2)); p.LineTo(P(0, 0)); p.LineTo(P(2, -2));
                        Line(0, 0, 0, 5);
                    }
                    break;
                case WhimTexLightType.Spot:
                    if (!filled) CanvasHandlePainter.Line(p, F(2, -5), F(2, 5));
                    CanvasHandlePainter.Line(p, F(8, -4), F(12, -6));
                    CanvasHandlePainter.Line(p, F(8, 0), F(13, 0));
                    CanvasHandlePainter.Line(p, F(8, 4), F(12, 6));
                    break;
                case WhimTexLightType.Area:
                    Line(-6, 1, -10, -7); Line(0, 0, 0, -11); Line(6, 1, 10, -7);
                    break;
            }
            Stroke();
        }


        private void BuildLightsCanvasTool()
        {
            lightManipulator = new LightCanvasManipulator(this);
            lightsOverlay = toolkitCanvas.Overlays.Create(lightManipulator.Draw);
            toolkitCanvas.AddManipulator(lightManipulator);
        }

        private sealed class LightCanvasManipulator : PointerManipulator
        {
            readonly WhimTexWindow owner;
            WhimTexDocument document;
            WhimTexLight light;
            Vector3 oldPosition, oldDirection;
            readonly Dictionary<WhimTexLight, Vector3> movingPositions = new();
            bool collapseSelectionOnClick;
            float oldRange;
            Vector2 oldSize;
            Vector2 start;
            Vector2 oldSpotTarget;
            int pointer = -1, handle, undo = -1;
            internal bool IsDragging => pointer >= 0;
            public LightCanvasManipulator(WhimTexWindow owner) => this.owner = owner;
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<DetachFromPanelEvent>(Detach);
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                End(false);
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<DetachFromPanelEvent>(Detach);
                target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }
            Vector2 View(Vector2 pixel) => owner.toolkitCanvas.Coordinates.DocumentToView(pixel);
            Vector2 Pixel(Vector2 view) => owner.toolkitCanvas.Coordinates.ViewToDocument(view);
            Vector2 RangeHandle(WhimTexLight item) => View((Vector2)item.position + Vector2.right * item.range);
            Vector2 SizeAxis(WhimTexLight item, bool vertical)
            {
                item.Frame(out Vector3 right, out Vector3 up);
                Vector2 axis = vertical ? (Vector2)up : (Vector2)right;
                // A source viewed edge-on still needs an operable size handle.
                return axis.sqrMagnitude >= .01f ? axis : axis.sqrMagnitude > .000001f
                    ? axis.normalized : vertical ? Vector2.up : Vector2.right;
            }
            Vector2 SizeHandle(WhimTexLight item, bool vertical)
            {
                float size = vertical && item.areaShape == WhimTexAreaLightShape.Rectangle ? item.size.y : item.size.x;
                Vector2 delta = View((Vector2)item.position + SizeAxis(item, vertical) * (size * .5f)) - View(item.position);
                if (delta.sqrMagnitude < 18 * 18) delta = (delta.sqrMagnitude > .001f ? delta.normalized : vertical ? Vector2.up : Vector2.right) * 18;
                return View(item.position) + delta;
            }
            Vector2 SpotTarget(WhimTexLight item)
            {
                Vector3 axis = item.direction.normalized;
                float distance = Mathf.Abs(axis.z) > 1e-6f ? -item.position.z / axis.z : -1;
                return (Vector2)(item.position + axis * (distance > 0 ? Mathf.Min(distance, 1e7f) : item.range));
            }
            Vector2 Handle(WhimTexLight item)
            {
                if (item.type == WhimTexLightType.Point)
                    return RangeHandle(item);
                if (item.EmitsAlongDirection)
                {
                    Vector2 center = View(item.position), aim = View(SpotTarget(item));
                    Vector2 delta = aim - center;
                    // Keep the aiming grip separate from the source when it points straight down.
                    return delta.sqrMagnitude >= 24 * 24 ? aim : center + (delta.sqrMagnitude > .001f ? delta.normalized : Vector2.down) * 24;
                }
                Vector3 d = item.direction.normalized;
                // Project the document direction through the same rotated canvas as the anchor.
                Vector2 axis = View((Vector2)item.position + new Vector2(d.x, d.y)) - View(item.position);
                float scale = (View((Vector2)item.position + Vector2.right) - View(item.position)).magnitude;
                return View(item.position) + axis * (70 / Mathf.Max(scale, 1e-6f));
            }
            void Down(PointerDownEvent e)
            {
                if (owner.canvasTool != CanvasTool.Lights || e.button != 0 || e.altKey || owner.activeDocument == null) return;
                owner.NormalizeLightSelection();
                var selected = owner.SelectedLight;
                light = null; handle = 0;
                if (selected?.type == WhimTexLightType.Area && (SizeHandle(selected, false) - (Vector2)e.localPosition).sqrMagnitude < 64)
                { light = selected; handle = 3; }
                else if (selected?.type == WhimTexLightType.Area && (SizeHandle(selected, true) - (Vector2)e.localPosition).sqrMagnitude < 64)
                { light = selected; handle = 4; }
                else if (selected != null && (Handle(selected) - (Vector2)e.localPosition).sqrMagnitude < 100) { light = selected; handle = 1; }
                else if (selected != null && selected.EmitsAlongDirection && (RangeHandle(selected) - (Vector2)e.localPosition).sqrMagnitude < 100)
                { light = selected; handle = 2; }
                if (light == null)
                    foreach (var item in owner.activeDocument.lights)
                        if ((View(item.position) - (Vector2)e.localPosition).sqrMagnitude < 144) light = item;
                bool toggle = e.ctrlKey || e.commandKey;
                if (light == null)
                {
                    if (!toggle) owner.SelectLight(null, false);
                    target.Focus(); return;
                }
                collapseSelectionOnClick = !toggle && handle == 0 && owner.selectedLightIds.Contains(light.id) && owner.selectedLightIds.Count > 1;
                if (toggle || !owner.selectedLightIds.Contains(light.id)) owner.SelectLight(light.id, toggle);
                else { owner.selectedLightId = light.id; owner.RefreshLightsPanel(); }
                if (!owner.selectedLightIds.Contains(light.id))
                { light = null; target.Focus(); e.StopImmediatePropagation(); return; }
                document = owner.activeDocument; start = e.localPosition;
                oldPosition = light.position; oldDirection = light.direction; oldRange = light.range;
                oldSize = light.size;
                oldSpotTarget = SpotTarget(light);
                movingPositions.Clear();
                if (handle == 0)
                    foreach (var item in document.lights)
                        if (owner.selectedLightIds.Contains(item.id)) movingPositions.Add(item, item.position);
                pointer = e.pointerId; target.CapturePointer(pointer); target.Focus(); e.StopImmediatePropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (pointer != e.pointerId) return;
                if (document != owner.activeDocument || owner.canvasTool != CanvasTool.Lights) { End(false); return; }
                if (undo < 0)
                {
                    if (((Vector2)e.localPosition-start).sqrMagnitude < 4) return;
                    Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
                    Undo.RegisterCompleteObjectUndo(document, movingPositions.Count > 1 ? "Move Lights" : "Move Light");
                }
                Vector2 pixel = Pixel(e.localPosition);
                if (handle == 0)
                {
                    Vector2 delta = pixel - Pixel(start);
                    foreach (var entry in movingPositions) entry.Key.position = entry.Value + new Vector3(delta.x, delta.y, 0);
                }
                else if (handle == 3 || handle == 4)
                {
                    Vector2 axis = SizeAxis(light, handle == 4);
                    Vector2 delta = pixel - Pixel(start);
                    float amount = Vector2.Dot(delta, axis) / Mathf.Max(axis.sqrMagnitude, .01f) * 2;
                    if (light.areaShape == WhimTexAreaLightShape.Disk) light.size = Vector2.one * Mathf.Max(.001f, oldSize.x + amount);
                    else { light.size = oldSize; light.size[handle - 3] = Mathf.Max(.001f, oldSize[handle - 3] + amount); }
                }
                else if (light.type == WhimTexLightType.Point || handle == 2) light.range = Mathf.Max(.001f, Vector2.Distance(pixel, light.position));
                else if (light.EmitsAlongDirection)
                {
                    Vector2 aim = oldSpotTarget + pixel - Pixel(start);
                    light.direction = new Vector3(aim.x - light.position.x, aim.y - light.position.y,
                        Mathf.Abs(light.position.z) > .001f ? -light.position.z : -1).normalized;
                }
                else
                {
                    float scale = (View((Vector2)light.position + Vector2.right)-View(light.position)).magnitude;
                    Vector2 xy = (pixel-(Vector2)light.position) * scale / 70;
                    if (xy.sqrMagnitude > 1) xy.Normalize();
                    light.direction = new Vector3(xy.x, xy.y, Mathf.Sqrt(Mathf.Max(0, 1-xy.sqrMagnitude)));
                }
                owner.RequestTransformCanvas(); owner.RefreshLightsPanel(); e.StopImmediatePropagation();
            }
            void Up(PointerUpEvent e)
            {
                if (pointer != e.pointerId) return;
                string single = collapseSelectionOnClick && undo < 0 ? light?.id : null;
                End(false);
                if (single != null) owner.SelectLight(single, false);
                e.StopImmediatePropagation();
            }
            void Lost(PointerCaptureOutEvent e) { if (pointer == e.pointerId) End(false); }
            void Detach(DetachFromPanelEvent e) => End(false);
            void Key(KeyDownEvent e)
            {
                if (owner.canvasTool != CanvasTool.Lights || e.keyCode != KeyCode.Escape) return;
                if (pointer >= 0) End(true); else owner.ExitContextTool();
                e.StopImmediatePropagation();
            }
            internal void End(bool cancel)
            {
                int captured = pointer; pointer = -1;
                if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                if (undo >= 0 && document != null)
                {
                    if (cancel)
                    {
                        foreach (var entry in movingPositions) entry.Key.position = entry.Value;
                        light.position = oldPosition; light.direction = oldDirection; light.range = oldRange;
                        light.size = oldSize;
                    }
                    Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo); Undo.IncrementCurrentGroup(); undo = -1;
                    document.NotifyLightsChanged();
                    owner.RefreshLightsPanel();
                }
                movingPositions.Clear(); collapseSelectionOnClick = false;
                light = null; document = null;
            }
            internal void Draw(MeshGenerationContext context)
            {
                if (owner.canvasTool != CanvasTool.Lights || owner.activeDocument == null) return;
                var p = context.painter2D;
                foreach (var item in owner.activeDocument.lights)
                {
                    Vector2 center = View(item.position);
                    bool selected = owner.selectedLightIds.Contains(item.id);
                    Color color = new Color(Mathf.Max(0, item.color.r), Mathf.Max(0, item.color.g), Mathf.Max(0, item.color.b));
                    color /= Mathf.Max(1, color.maxColorComponent);
                    color = HdrUtility.Encode(color);
                    Color.RGBToHSV(color, out float hue, out float saturation, out _);
                    color = Color.HSVToRGB(hue, saturation, 1);
                    color.a = item.enabled ? 1 : .45f;
                    p.BeginPath();
                    if (item.type == WhimTexLightType.Area) AreaBoundary(item);
                    if (item == owner.SelectedLight)
                    {
                        Vector2 h = Handle(item);
                        if (item.EmitsAlongDirection)
                        {
                            Vector2 aim = View(SpotTarget(item)); LineFromMarker(center, aim); Line(aim, h);
                            CanvasHandlePainter.Diamond(p, h, 5);
                            Vector2 range = RangeHandle(item); LineFromMarker(center, range); Circle(range, 5);
                            if (item.type == WhimTexLightType.Spot)
                            {
                                SpotBoundary(item, item.angle * .5f);
                                if (item.softness > 0 && item.softness < 1) SpotBoundary(item, item.angle * .5f * (1 - item.softness));
                                SpotRangeBoundary(item);
                            }
                            else
                            {
                                CanvasHandlePainter.Diamond(p, SizeHandle(item, false), 5);
                                CanvasHandlePainter.Diamond(p, SizeHandle(item, true), 5);
                            }
                        }
                        else
                        {
                            LineFromMarker(center,h); Circle(h,5);
                            if (item.type == WhimTexLightType.Point) Circle(center,Vector2.Distance(center,h));
                        }
                    }
                    float width = selected ? 2 : 1;
                    p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                    CanvasHandlePainter.StrokeOutlined(p, color, width, new Color(.08f, .08f, .08f, color.a),
                        1 / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint));
                    DrawLightIcon(p, item.type, center, color, selected, 1, width,
                        1 / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint));
                }
                void Circle(Vector2 c,float radius) => CanvasHandlePainter.Circle(p,c,radius);
                void Line(Vector2 a,Vector2 b) => CanvasHandlePainter.Line(p,a,b);
                void LineFromMarker(Vector2 center, Vector2 end)
                {
                    Vector2 delta = end - center;
                    if (delta.sqrMagnitude > 14 * 14) Line(center + delta.normalized * 14, end);
                }
                void AreaBoundary(WhimTexLight item)
                {
                    item.Frame(out Vector3 right, out Vector3 up);
                    right *= item.size.x * .5f;
                    up *= (item.areaShape == WhimTexAreaLightShape.Disk ? item.size.x : item.size.y) * .5f;
                    if (item.areaShape == WhimTexAreaLightShape.Rectangle)
                    {
                        Vector2 a = View(item.position - right - up), b = View(item.position + right - up);
                        Vector2 c = View(item.position + right + up), d = View(item.position - right + up);
                        Line(a,b); Line(b,c); Line(c,d); Line(d,a);
                    }
                    else
                    {
                        Vector2 previous = View(item.position + right);
                        for (int i = 1; i <= 64; i++)
                        {
                            float phi = i * Mathf.PI / 32;
                            Vector2 point = View(item.position + right * Mathf.Cos(phi) + up * Mathf.Sin(phi));
                            Line(previous, point); previous = point;
                        }
                    }
                }
                void SpotBoundary(WhimTexLight item, float halfAngle)
                {
                    Vector3 axis = item.direction.normalized;
                    Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
                    Vector3 v = Vector3.Cross(axis, u);
                    float cos = Mathf.Cos(halfAngle * Mathf.Deg2Rad), sin = Mathf.Sin(halfAngle * Mathf.Deg2Rad);
                    bool previousValid = false;
                    Vector2 previous = default;
                    for (int i = 0; i <= 64; i++)
                    {
                        float phi = i * Mathf.PI / 32;
                        Vector3 ray = axis * cos + (u * Mathf.Cos(phi) + v * Mathf.Sin(phi)) * sin;
                        float distance = Mathf.Abs(ray.z) > 1e-6f ? -item.position.z / ray.z : -1;
                        bool valid = distance > 0 && distance <= item.range;
                        if (valid)
                        {
                            Vector2 point = View(item.position + ray * distance);
                            if (previousValid) Line(previous, point);
                            previous = point;
                        }
                        previousValid = valid;
                    }
                }
                void SpotRangeBoundary(WhimTexLight item)
                {
                    if (item.range <= Mathf.Abs(item.position.z)) return;
                    float radius = Mathf.Sqrt(item.range * item.range - item.position.z * item.position.z);
                    float limit = Mathf.Cos(item.angle * .5f * Mathf.Deg2Rad);
                    Vector3 axis = item.direction.normalized;
                    bool previousValid = false;
                    Vector2 previous = default;
                    for (int i = 0; i <= 64; i++)
                    {
                        float phi = i * Mathf.PI / 32;
                        Vector3 offset = new Vector3(Mathf.Cos(phi) * radius, Mathf.Sin(phi) * radius, -item.position.z);
                        bool valid = Vector3.Dot(offset / item.range, axis) >= limit;
                        Vector2 point = View(item.position + offset);
                        if (valid && previousValid) Line(previous, point);
                        previous = point; previousValid = valid;
                    }
                }
            }
        }
    }
}
