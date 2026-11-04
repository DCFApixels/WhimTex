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
            lightsDrawer = new VisualElement { name = "lightsDrawer" };
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
            if (lightsDrawer == null) return;
            if (brushesExpanded || commentsExpanded || postFxExpanded || uvExpanded) lightsExpanded = false;
            if (!lightsExpanded && canvasTool == CanvasTool.Lights) ChangeCanvasTool(lastBaseCanvasTool);
            RefreshCanvasDrawerTab(lightsTab, lightsExpanded);
            lightsDrawer.EnableInClassList("whimtex-post-fx-drawer--hidden", !lightsExpanded);
            lightsOverlay?.MarkDirtyRepaint();
            editLightsButton.SetEnabled(activeDocument != null && activeDocument.lights.Count > 0);
            editLightsButton.text = canvasTool == CanvasTool.Lights ? "Done" : "Edit on Canvas";
            if (!lightsExpanded) return;
            if (SelectedLight == null && activeDocument?.lights.Count > 0) selectedLightId = activeDocument.lights[0].id;
            string structure = activeDocument == null ? "" : string.Join("|", activeDocument.lights.ConvertAll(light => light.id));
            if (lightsBoundDocument != activeDocument || lightsBoundStructure != structure || lightsBoundSelection != SelectedLight)
            {
                lightsBoundDocument = activeDocument; lightsBoundStructure = structure; lightsBoundSelection = SelectedLight;
                RebuildLightsFields();
            }
            foreach (var binding in lightsBindings) binding();
        }

        private void EditLight(Action change, string undoName = "Edit Light")
        {
            if (activeDocument == null) return;
            Undo.RecordObject(activeDocument, undoName);
            change();
            CommitModelChange();
            RefreshLightsPanel();
        }

        private void RebuildLightsFields()
        {
            lightsContent.Clear(); lightsBindings.Clear();
            if (activeDocument == null) return;
            foreach (var light in activeDocument.lights)
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var enabled = new Toggle { value = light.enabled };
                enabled.RegisterValueChangedCallback(e => EditLight(() => light.enabled = e.newValue));
                row.Add(enabled);
                var select = new Button(() => { selectedLightId = light.id; RefreshLightsPanel(); }) { text = light.name };
                select.style.flexGrow = 1;
                select.EnableInClassList("whimtex-tool-button--selected", light == SelectedLight);
                row.Add(select); lightsContent.Add(row);
                lightsBindings.Add(() => { enabled.SetValueWithoutNotify(light.enabled); select.text = light.name; });
            }
            var add = new VisualElement(); add.style.flexDirection = FlexDirection.Row;
            foreach (WhimTexLightType type in Enum.GetValues(typeof(WhimTexLightType)))
            {
                var button = new Button(() =>
                {
                    EditLight(() => selectedLightId = activeDocument.AddLight(type).id, "Add Light");
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
            Field(new EnumField("Type", selected.type), () => (Enum)selected.type, value => selected.type = (WhimTexLightType)value);
            Field(new ColorField("Color") { hdr = true, showAlpha = false }, () => selected.color, value => selected.color = value);
            Field(new FloatField("Intensity"), () => selected.intensity, value => selected.intensity = value);
            Field(new Vector3Field("Position (px)"), () => selected.position, value => selected.position = value);
            var direction = Field(new Vector3Field("Direction"), () => selected.direction, value => selected.direction = value);
            var range = Field(new FloatField("Range (px)"), () => selected.range, value => selected.range = value);
            var falloff = Field(new FloatField("Falloff"), () => selected.falloff, value => selected.falloff = value);
            lightsBindings.Add(() =>
            {
                direction.style.display = selected.type == WhimTexLightType.Directional ? DisplayStyle.Flex : DisplayStyle.None;
                range.style.display = falloff.style.display = selected.type == WhimTexLightType.Point ? DisplayStyle.Flex : DisplayStyle.None;
            });
            lightsContent.Add(new Button(() => EditLight(() => activeDocument.RemoveLight(selected.id), "Remove Light")) { text = "Remove Light" });
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

        private void BuildLightsCanvasTool()
        {
            lightsOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            lightsOverlay.StretchToParentSize(); toolkitCanvas.Add(lightsOverlay);
            lightManipulator = new LightCanvasManipulator(this);
            lightsOverlay.generateVisualContent += lightManipulator.Draw;
            toolkitCanvas.AddManipulator(lightManipulator);
        }

        private sealed class LightCanvasManipulator : PointerManipulator
        {
            readonly WhimTexWindow owner;
            WhimTexDocument document;
            WhimTexLight light;
            Vector3 oldPosition, oldDirection;
            float oldRange;
            Vector2 start;
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
            Vector2 View(Vector2 pixel)
            {
                Rect r = owner.toolkitCanvas.ImageRect;
                return owner.toolkitCanvas.ToView(new Vector2(r.x + pixel.x / owner.activeDocument.width * r.width,
                    r.yMax - pixel.y / owner.activeDocument.height * r.height));
            }
            Vector2 Pixel(Vector2 view)
            {
                Rect r = owner.toolkitCanvas.ImageRect; Vector2 p = owner.toolkitCanvas.ToCanvas(view);
                return new Vector2((p.x-r.x)/r.width*owner.activeDocument.width, (r.yMax-p.y)/r.height*owner.activeDocument.height);
            }
            Vector2 Handle(WhimTexLight item)
            {
                if (item.type == WhimTexLightType.Point)
                    return View((Vector2)item.position + Vector2.right * item.range);
                Vector3 d = item.direction.normalized;
                // Project the document direction through the same rotated canvas as the anchor.
                Vector2 axis = View((Vector2)item.position + new Vector2(d.x, d.y)) - View(item.position);
                float scale = (View((Vector2)item.position + Vector2.right) - View(item.position)).magnitude;
                return View(item.position) + axis * (70 / Mathf.Max(scale, 1e-6f));
            }
            void Down(PointerDownEvent e)
            {
                if (owner.canvasTool != CanvasTool.Lights || e.button != 0 || e.altKey || owner.activeDocument == null) return;
                var selected = owner.SelectedLight;
                light = null; handle = 0;
                if (selected != null && (Handle(selected) - (Vector2)e.localPosition).sqrMagnitude < 100) { light = selected; handle = 1; }
                if (light == null)
                    foreach (var item in owner.activeDocument.lights)
                        if ((View(item.position) - (Vector2)e.localPosition).sqrMagnitude < 144) light = item;
                if (light == null) return;
                owner.selectedLightId = light.id; owner.RefreshLightsPanel();
                document = owner.activeDocument; start = e.localPosition;
                oldPosition = light.position; oldDirection = light.direction; oldRange = light.range;
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
                    Undo.RegisterCompleteObjectUndo(document, "Move Light");
                }
                Vector2 pixel = Pixel(e.localPosition);
                if (handle == 0) { Vector2 delta = pixel-Pixel(start); light.position = oldPosition + new Vector3(delta.x, delta.y, 0); }
                else if (light.type == WhimTexLightType.Point) light.range = Mathf.Max(.001f, Vector2.Distance(pixel, light.position));
                else
                {
                    float scale = (View((Vector2)light.position + Vector2.right)-View(light.position)).magnitude;
                    Vector2 xy = (pixel-(Vector2)light.position) * scale / 70;
                    if (xy.sqrMagnitude > 1) xy.Normalize();
                    light.direction = new Vector3(xy.x, xy.y, Mathf.Sqrt(Mathf.Max(0, 1-xy.sqrMagnitude)));
                }
                owner.RequestTransformCanvas(); owner.RefreshLightsPanel(); e.StopImmediatePropagation();
            }
            void Up(PointerUpEvent e) { if (pointer == e.pointerId) { End(false); e.StopImmediatePropagation(); } }
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
                    if (cancel) { light.position = oldPosition; light.direction = oldDirection; light.range = oldRange; }
                    Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo); Undo.IncrementCurrentGroup(); undo = -1;
                    document.NotifyLightsChanged();
                    owner.RefreshLightsPanel();
                }
                light = null; document = null;
            }
            internal void Draw(MeshGenerationContext context)
            {
                if (owner.canvasTool != CanvasTool.Lights || owner.activeDocument == null) return;
                var p = context.painter2D;
                foreach (var item in owner.activeDocument.lights)
                {
                    Vector2 center = View(item.position);
                    bool selected = item == owner.SelectedLight;
                    Color color = new Color(Mathf.Max(0, item.color.r), Mathf.Max(0, item.color.g), Mathf.Max(0, item.color.b));
                    color /= Mathf.Max(1, color.maxColorComponent);
                    color = HdrUtility.Encode(color);
                    Color.RGBToHSV(color, out float hue, out float saturation, out _);
                    color = Color.HSVToRGB(hue, saturation, 1);
                    color.a = item.enabled ? 1 : .45f;
                    p.BeginPath();
                    Circle(center, 7);
                    for (int i=0;i<8;i++) { float angle=i*Mathf.PI/4; Vector2 v=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)); Line(center+v*10,center+v*13); }
                    if (selected)
                    {
                        Vector2 h = Handle(item); Line(center,h); Circle(h,5);
                        if (item.type == WhimTexLightType.Point) Circle(center,Vector2.Distance(center,h));
                    }
                    float width = selected ? 2 : 1;
                    p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                    p.strokeColor = new Color(.08f, .08f, .08f, color.a);
                    p.lineWidth = width + 2 / Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
                    p.Stroke();
                    p.strokeColor = color; p.lineWidth = width;
                    p.Stroke();
                }
                void Circle(Vector2 c,float radius) { p.MoveTo(c+Vector2.right*radius); p.Arc(c,radius,0,360); p.ClosePath(); }
                void Line(Vector2 a,Vector2 b) { p.MoveTo(a); p.LineTo(b); }
            }
        }
    }
}
