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
        private Button lightsTab;
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
                if (lightsExpanded)
                {
                    brushesExpanded = commentsExpanded = postFxExpanded = uvExpanded = false;
                    if (canvasTool != CanvasTool.Lights) SetCanvasTool(CanvasTool.Lights);
                }
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
            var title = new Label("Lights") { name = "lightsTitle", pickingMode = PickingMode.Ignore };
            title.AddToClassList("whimtex-canvas-drawer-title");
            AddCanvasDrawerHeader(lightsDrawer).Add(title);
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
            var direction = Field(new Vector3Field("Direction"), () => selected.EmissionDirection, value => selected.EmissionDirection = value);
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
            Color outline = new Color(.08f, .08f, .08f, color.a);
            float threadRadius = Mathf.Min(width, 1) * .5f;
            float threadOutline = Mathf.Max(outlineWidth, scale);
            // Filled contours avoid overlapping stroke joins on these small silhouettes.
            if (type == WhimTexLightType.Point) PaintBase(threadRadius + threadOutline, outline);
            if (outlineWidth > 0) Paint(width * .5f + outlineWidth, outline);
            Paint(width * .5f, color);
            if (type == WhimTexLightType.Point) PaintBase(threadRadius, color);

            void PaintBase(float radius, Color fill)
            {
                float first = 4 * scale + width * .5f + threadOutline + threadRadius;
                float second = first + 2 * threadRadius + threadOutline;
                // One shared outline separates the bands. Separate fills unite their
                // overlapping dark contours instead of cutting holes with OddEven.
                Band(3 * scale, first);
                Band(1.5f * scale, second);
                void Band(float halfWidth, float y)
                {
                    p.BeginPath();
                    CanvasHandlePainter.Capsule(p, center + new Vector2(-halfWidth, y),
                        center + new Vector2(halfWidth, y), radius);
                    p.fillColor = fill; p.Fill();
                }
            }
            void Paint(float radius, Color fill)
            {
                p.BeginPath(); Body(radius / scale);
                if (!filled) Body(-radius / scale);
                void Ray(Vector2 a, Vector2 b) => CanvasHandlePainter.Capsule(p, a, b, radius);
                switch (type)
                {
                    case WhimTexLightType.Directional:
                        for (int i = 0; i < 8; i++)
                        {
                            float angle = i * Mathf.PI / 4;
                            Vector2 ray = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                            Ray(center + ray * (10.5f * scale), center + ray * (13 * scale));
                        }
                        break;
                    case WhimTexLightType.Spot:
                        Ray(F(10, -4), F(13, -6)); Ray(F(10, 0), F(14, 0)); Ray(F(10, 4), F(13, 6));
                        break;
                    case WhimTexLightType.Area:
                        Ray(P(-6, 0), P(-10, -8)); Ray(P(0, -1), P(0, -11)); Ray(P(6, 0), P(10, -8));
                        break;
                }
                p.fillColor = fill; p.Fill(FillRule.OddEven);
            }
            void Body(float d)
            {
                switch (type)
                {
                    case WhimTexLightType.Directional:
                        CanvasHandlePainter.Circle(p, center, (6 + d) * scale); break;
                    case WhimTexLightType.Point:
                        p.MoveTo(P(-3 - d, 4 + d));
                        p.BezierCurveTo(P(-3 - d, 1), P(-8 - d, 0), P(-8 - d, -5));
                        p.BezierCurveTo(P(-8 - d, -9 - d), P(-4, -12 - d), P(0, -12 - d));
                        p.BezierCurveTo(P(4, -12 - d), P(8 + d, -9 - d), P(8 + d, -5));
                        p.BezierCurveTo(P(8 + d, 0), P(3 + d, 1), P(3 + d, 4 + d));
                        p.ClosePath(); break;
                    case WhimTexLightType.Spot:
                        // Parallel offsets retain the short cylindrical head after the tapered shoulder.
                        float shoulder = -1 - .30277564f * d, head = 2 - .30277564f * d;
                        p.MoveTo(F(-11 - d, -3 - d)); p.LineTo(F(shoulder, -3 - d));
                        p.LineTo(F(head, -5 - d)); p.LineTo(F(5 + d, -5 - d));
                        p.LineTo(F(5 + d, 5 + d)); p.LineTo(F(head, 5 + d));
                        p.LineTo(F(shoulder, 3 + d)); p.LineTo(F(-11 - d, 3 + d)); p.ClosePath(); break;
                    case WhimTexLightType.Area:
                        if (d <= -2) break;
                        p.MoveTo(P(-11 - d, 5 - d)); p.LineTo(P(11 + d, 5 - d));
                        p.LineTo(P(11 + d, 9 + d)); p.LineTo(P(-11 - d, 9 + d)); p.ClosePath(); break;
                }
            }
        }

        private static bool BuildLightSpotContour(List<Vector2> points, WhimTexLight item,
            CanvasCoordinates coordinates, float halfAngle, bool closeAtRange, float tolerance)
        {
            points.Clear();
            double height = Math.Abs(item.position.z), range = item.range;
            if (range <= height) return false;
            Vector3 axis = item.direction.normalized;
            double cosine = Math.Cos(halfAngle * Math.PI / 180), sine = Math.Sin(halfAngle * Math.PI / 180);
            double horizontal = Math.Sqrt(axis.x * axis.x + axis.y * axis.y);
            float radius = (float)Math.Sqrt((range - height) * (range + height));
            Vector2 center = item.position;
            Vector2 CirclePoint(float angle) => coordinates.DocumentToView(center + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            void Circle()
            {
                CanvasHandlePainter.SampleCurve(points, CirclePoint, 0, Mathf.PI * 2, tolerance);
                points[points.Count - 1] = points[0];
            }
            // On the canvas plane the cone becomes a circular sector, not a perspective conic.
            if (height < 1e-6)
            {
                if (horizontal <= cosine) return false;
                float heading = Mathf.Atan2(axis.y, axis.x), spread = (float)Math.Acos(cosine / horizontal);
                points.Add(coordinates.DocumentToView(center));
                CanvasHandlePainter.SampleCurve(points, CirclePoint, heading - spread, heading + spread, tolerance);
                if (!closeAtRange)
                {
                    // The soft boundary has two radial sides; no duplicate range arc.
                    Vector2 last = points[points.Count - 1], first = points[1];
                    points.Clear(); points.Add(first); points.Add(coordinates.DocumentToView(center)); points.Add(last);
                }
                return closeAtRange;
            }
            float sign = Mathf.Sign(item.position.z);
            double towardPlane = -axis.z * sign;
            double a = towardPlane * cosine, b = horizontal * sine;
            double threshold = b > 1e-12 ? (height / range - a) / b : a >= height / range ? -2 : 2;
            if (threshold >= 1)
            {
                if (!closeAtRange || towardPlane < cosine) return false;
                Circle(); return true;
            }
            Vector3 u = horizontal > 1e-9 ? new Vector3(-axis.y, axis.x, 0) / (float)horizontal : Vector3.right;
            Vector3 v = Vector3.Cross(axis, u) * -sign;
            Vector2 ConePoint(float phi)
            {
                Vector3 ray = axis * (float)cosine + (v * Mathf.Cos(phi) + u * Mathf.Sin(phi)) * (float)sine;
                float distance = -item.position.z / ray.z;
                return coordinates.DocumentToView(center + (Vector2)ray * distance);
            }
            if (threshold <= -1)
            {
                CanvasHandlePainter.SampleCurve(points, ConePoint, -Mathf.PI, Mathf.PI, tolerance);
                points[points.Count - 1] = points[0]; return true;
            }
            float extent = (float)Math.Acos(threshold);
            CanvasHandlePainter.SampleCurve(points, ConePoint, -extent, extent, tolerance);
            if (!closeAtRange) return false;
            Vector2 firstPoint = points[0], lastPoint = points[points.Count - 1];
            Vector2 end = coordinates.ViewToDocument(lastPoint) - center, start = coordinates.ViewToDocument(firstPoint) - center;
            float from = Mathf.Atan2(end.y, end.x), to = Mathf.Atan2(start.y, start.x);
            float sweep = Mathf.Repeat(to - from, Mathf.PI * 2), mid = from + sweep * .5f;
            Vector3 middle = new Vector3(radius * Mathf.Cos(mid), radius * Mathf.Sin(mid), -item.position.z);
            if (Vector3.Dot(middle / (float)range, axis) < cosine) sweep -= Mathf.PI * 2;
            // Reuse the cone endpoints exactly, so the two boundaries cannot open a seam.
            Vector2 RangePoint(float t) => t <= 0 ? lastPoint : t >= 1 ? firstPoint : CirclePoint(from + sweep * t);
            CanvasHandlePainter.SampleCurve(points, RangePoint, 0, 1, tolerance);
            return true;
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
            readonly List<Vector2> contour = new(256);
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
                Vector3 d = item.EmissionDirection.normalized;
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
                    light.EmissionDirection = ProjectNormal(xy, oldDirection.z >= 0);
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
                var coordinates = owner.toolkitCanvas.Coordinates;
                float border = 1 / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint), tolerance = border * .25f;
                Color outline = new Color(.08f, .08f, .08f);
                // All guides sit behind all markers; another source's guide cannot cut through an icon.
                for (int pass = 0; pass < 2; pass++)
                foreach (var item in owner.activeDocument.lights)
                {
                    Vector2 center = View(item.position);
                    bool selected = owner.selectedLightIds.Contains(item.id), active = item == owner.SelectedLight;
                    Color color = new Color(Mathf.Max(0, item.color.r), Mathf.Max(0, item.color.g), Mathf.Max(0, item.color.b));
                    color /= Mathf.Max(1, color.maxColorComponent);
                    color = HdrUtility.Encode(color);
                    Color.RGBToHSV(color, out float hue, out float saturation, out _);
                    color = Color.HSVToRGB(hue, saturation, item.enabled ? 1 : .45f);
                    float width = selected ? 2 : 1;
                    p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                    if (pass == 1)
                    {
                        if (active)
                        {
                            Grip(Handle(item), item.EmitsAlongDirection);
                            if (item.EmitsAlongDirection) Grip(RangeHandle(item), false);
                            if (item.type == WhimTexLightType.Area)
                            { Grip(SizeHandle(item, false), true); Grip(SizeHandle(item, true), true); }
                        }
                        DrawLightIcon(p, item.type, center, color, selected, 1, 2, border);
                        continue;
                    }
                    if (!active && item.type != WhimTexLightType.Area) continue;
                    CanvasHandlePainter.StrokeOutlined(p, Path, color, width, outline, border);
                    void Path()
                    {
                        if (item.type == WhimTexLightType.Area) AreaBoundary(item);
                        if (active)
                        {
                            Connector(center, Handle(item));
                            if (item.EmitsAlongDirection) Connector(center, RangeHandle(item));
                            if (item.type == WhimTexLightType.Point)
                                CanvasHandlePainter.Circle(p, center, Vector2.Distance(center, RangeHandle(item)));
                            if (item.type == WhimTexLightType.Spot)
                            {
                                bool closed = BuildLightSpotContour(contour, item, coordinates, item.angle * .5f, true, tolerance);
                                CanvasHandlePainter.Polyline(p, contour, closed);
                                if (item.softness > 0 && item.softness < 1)
                                {
                                    closed = BuildLightSpotContour(contour, item, coordinates,
                                        item.angle * .5f * (1 - item.softness), false, tolerance);
                                    CanvasHandlePainter.Polyline(p, contour, closed);
                                }
                            }
                        }
                    }
                    void Grip(Vector2 point, bool diamond)
                    {
                        if ((point - center).sqrMagnitude < 16 * 16) return;
                        float edge = diamond ? Mathf.Sqrt(2) : 1;
                        Shape(5 + (width * .5f + border) * edge, outline);
                        Shape(5 + width * .5f * edge, color);
                        Shape(5 - width * .5f * edge, outline);
                        void Shape(float radius, Color fill)
                        {
                            p.BeginPath();
                            if (diamond) CanvasHandlePainter.Diamond(p, point, radius);
                            else CanvasHandlePainter.Circle(p, point, radius);
                            p.fillColor = fill; p.Fill();
                        }
                    }
                }
                void Connector(Vector2 center, Vector2 end)
                {
                    Vector2 delta = end - center;
                    if (delta.magnitude > 24)
                        CanvasHandlePainter.Line(p, center + delta.normalized * 16, end - delta.normalized * 8);
                }
                void AreaBoundary(WhimTexLight item)
                {
                    item.Frame(out Vector3 right, out Vector3 up);
                    right *= item.size.x * .5f;
                    up *= (item.areaShape == WhimTexAreaLightShape.Disk ? item.size.x : item.size.y) * .5f;
                    contour.Clear();
                    if (item.areaShape == WhimTexAreaLightShape.Rectangle)
                    {
                        contour.Add(View(item.position - right - up)); contour.Add(View(item.position + right - up));
                        contour.Add(View(item.position + right + up)); contour.Add(View(item.position - right + up));
                    }
                    else
                    {
                        Vector2 Point(float phi) => View(item.position + right * Mathf.Cos(phi) + up * Mathf.Sin(phi));
                        CanvasHandlePainter.SampleCurve(contour, Point, 0, Mathf.PI * 2, tolerance);
                        contour[contour.Count - 1] = contour[0];
                    }
                    // Edge-on emitters project to one line; do not retrace it back over itself.
                    Vector2 x = View(item.position + right) - View(item.position);
                    Vector2 y = View(item.position + up) - View(item.position);
                    if (Mathf.Abs(x.x * y.y - x.y * y.x) < .01f)
                    {
                        Vector2 axis = (x.sqrMagnitude >= y.sqrMagnitude ? x : y).normalized;
                        float extent = item.areaShape == WhimTexAreaLightShape.Disk
                            ? Mathf.Sqrt(x.sqrMagnitude + y.sqrMagnitude)
                            : Mathf.Abs(Vector2.Dot(x, axis)) + Mathf.Abs(Vector2.Dot(y, axis));
                        if (extent > .01f) CanvasHandlePainter.Line(p, View(item.position) - axis * extent, View(item.position) + axis * extent);
                    }
                    else CanvasHandlePainter.Polyline(p, contour, true);
                }
            }
        }
    }
}
