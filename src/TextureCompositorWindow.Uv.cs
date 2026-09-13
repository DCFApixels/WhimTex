using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private bool uvEnabled, uvExpanded;
        [SerializeField] private Color uvLineColor = new Color(.35f, .85f, 1f, 1f);
        [SerializeField] private float uvLineOpacity = .65f;
        private Button uvButton, uvTab;
        private VisualElement uvDrawer, uvOverlay;
        private ObjectField uvMeshField;
        private DropdownField uvChannelField, uvSubmeshField;
        private HelpBox uvStatus;
        private UvIslandMap uvMap;
        private Mesh uvCachedMesh;
        private TextureCompositor uvCachedDocument;
        private int uvCachedChannel = -1, uvCachedSubmesh = -2, uvCachedDirty, uvHoveredIsland = -1;
        private Hash128 uvCachedHash;
        private string uvMessage;
        private bool uvDisplayedSelection, uvDisplayedLayers;
        private bool IsUvSelectionTool => previewTool == PreviewTool.RectangleSelect && marqueeShape == MarqueeShape.UvIsland;

        private Button BuildUvButton()
        {
            uvButton = new Button(() =>
            {
                uvEnabled = !uvEnabled;
                if (uvEnabled) OpenUvDrawer();
                else { uvExpanded = false; uvMap = null; uvCachedChannel = -1; RefreshPostFxPanel(); uvOverlay?.MarkDirtyRepaint(); }
            }) { text = "UV", tooltip = "Show mesh UV island outlines. Preview only; never included in the texture or export." };
            uvButton.AddToClassList("sprite-editor-channel-button");
            RefreshUvPanel();
            return uvButton;
        }
        private void OpenUvDrawer()
        {
            uvEnabled = uvExpanded = true;
            brushesExpanded = postFxExpanded = false;
            RefreshUvReference();
            RefreshPostFxPanel();
        }
        private void BuildUvTab(VisualElement tabs)
        {
            uvTab = new Button(() =>
            {
                uvExpanded = !uvExpanded;
                if (uvExpanded) brushesExpanded = postFxExpanded = false;
                RefreshPostFxPanel();
            }) { tooltip = "Show or hide UV settings. Closing this panel keeps the UV outlines visible." };
            uvTab.AddToClassList("sprite-editor-post-fx-tab"); tabs.Add(uvTab);
        }
        private void BuildUvDrawer(VisualElement panel)
        {
            uvDrawer = new VisualElement { name = "uvDrawer" };
            uvDrawer.AddToClassList("sprite-editor-post-fx-drawer");
            uvDrawer.Add(CreatePaneHeader("UV", "uvTitle"));
            var fields = new ScrollView(ScrollViewMode.Vertical);
            fields.AddToClassList("sprite-editor-post-fx-settings");
            uvDrawer.Add(fields); panel.Add(uvDrawer);
            uvMeshField = SpriteEditorUI.ConfigureField(new ObjectField("Mesh")
            {
                objectType = typeof(Mesh), allowSceneObjects = false,
                tooltip = "Drop a Mesh from the Project window. Expand a model asset to find its meshes. Stored with this document; the mesh is never modified."
            });
            uvMeshField.RegisterValueChangedCallback(evt => ChangeUvReference(() =>
            { compositor.uvReferenceMesh = evt.newValue as Mesh; compositor.uvReferenceSubmesh = -1; }));
            fields.Add(uvMeshField);
            uvChannelField = SpriteEditorUI.ConfigureField(new DropdownField("UV Channel",
                new List<string> { "UV0", "UV1", "UV2", "UV3", "UV4", "UV5", "UV6", "UV7" }, 0));
            uvChannelField.RegisterValueChangedCallback(evt => ChangeUvReference(() => compositor.uvReferenceChannel = uvChannelField.index));
            fields.Add(uvChannelField);
            uvSubmeshField = SpriteEditorUI.ConfigureField(new DropdownField("Submesh", new List<string> { "All" }, 0)
            { tooltip = "Limit the overlay to one material slot, or show all submeshes." });
            uvSubmeshField.RegisterValueChangedCallback(evt => ChangeUvReference(() => compositor.uvReferenceSubmesh = uvSubmeshField.index - 1));
            fields.Add(uvSubmeshField);
            var color = SpriteEditorUI.ConfigureField(new ColorField("Line Color") { value = uvLineColor, hdr = false, showAlpha = false });
            color.RegisterValueChangedCallback(evt => { uvLineColor = evt.newValue; uvOverlay?.MarkDirtyRepaint(); }); fields.Add(color);
            var opacity = SpriteEditorUI.ConfigureField(new Slider("Opacity", 0f, 1f) { value = uvLineOpacity, showInputField = true });
            opacity.RegisterValueChangedCallback(evt =>
            { uvLineOpacity = float.IsNaN(evt.newValue) ? .65f : Mathf.Clamp01(evt.newValue); opacity.SetValueWithoutNotify(uvLineOpacity); uvOverlay?.MarkDirtyRepaint(); }); fields.Add(opacity);
            fields.Add(new Button(() =>
            {
                marqueeShape = MarqueeShape.UvIsland;
                areaSelectionManipulator?.Cancel();
                SetPreviewTool(PreviewTool.RectangleSelect);
            }) { text = "Select UV Islands", tooltip = "Click inside an island to select its pixels. Shift adds, Alt subtracts. Switch to a brush to paint inside the selection." });
            fields.Add(new Button(() => { uvCachedChannel = -1; RefreshUvReference(); }) { text = "Refresh UV" });
            uvStatus = new HelpBox("Assign a Mesh to show its UV islands.", HelpBoxMessageType.Info);
            fields.Add(uvStatus);
        }
        private void ChangeUvReference(Action change)
        {
            if (compositor == null) return;
            FinishPaintingStroke(); FinishPreviewTransform();
            Undo.RecordObject(compositor, "Change UV Reference");
            change();
            EditorUtility.SetDirty(compositor);
            temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
            uvCachedChannel = -1;
            RefreshUvReference(); UpdateUnsavedChangesState();
        }
        private void RefreshUvPanel()
        {
            uvButton?.EnableInClassList("sprite-editor-channel-button--enabled", uvEnabled);
            uvTab?.EnableInClassList("sprite-editor-post-fx-drawer--hidden", !uvEnabled);
            if (uvTab != null) uvTab.text = uvExpanded ? "›" : "‹";
            uvDrawer?.EnableInClassList("sprite-editor-post-fx-drawer--hidden", !uvEnabled || !uvExpanded);
            uvDrawer?.SetEnabled(compositor != null);
        }
        private void RefreshUvReference()
        {
            if (!uvEnabled || compositor == null) return;
            Mesh mesh = compositor.uvReferenceMesh;
            int channel = compositor.uvReferenceChannel, submesh = compositor.uvReferenceSubmesh;
            int dirty = mesh != null ? EditorUtility.GetDirtyCount(mesh) : 0;
            string path = mesh != null ? AssetDatabase.GetAssetPath(mesh) : string.Empty;
            Hash128 hash = string.IsNullOrEmpty(path) ? default : AssetDatabase.GetAssetDependencyHash(path);
            if (uvCachedDocument == compositor && ReferenceEquals(mesh, uvCachedMesh) && channel == uvCachedChannel &&
                submesh == uvCachedSubmesh && dirty == uvCachedDirty && hash == uvCachedHash) return;
            uvCachedDocument = compositor; uvCachedMesh = mesh; uvCachedChannel = channel;
            uvCachedSubmesh = submesh; uvCachedDirty = dirty; uvCachedHash = hash;
            uvMap = null; uvHoveredIsland = -1;
            uvMeshField?.SetValueWithoutNotify(mesh);
            uvChannelField?.SetValueWithoutNotify("UV" + channel);
            if (uvSubmeshField != null)
            {
                var choices = new List<string> { "All" };
                for (int i = 0; mesh != null && i < mesh.subMeshCount; i++) choices.Add("Submesh " + i);
                uvSubmeshField.choices = choices;
                uvSubmeshField.SetValueWithoutNotify(submesh < 0 ? "All" : "Submesh " + submesh);
            }
            try
            {
                uvMap = UvIslandMap.Build(mesh, channel, submesh);
                uvMessage = $"{uvMap.islands.Count} UV islands. Choose UV Island in Area Select to select one. Overlapping UVs share texture pixels. Only the 0–1 UV tile is shown.";
                if (uvMap.SkippedTriangles > 0) uvMessage += $" Skipped {uvMap.SkippedTriangles} empty or invalid UV faces.";
            }
            catch (Exception error) { uvMessage = error.Message; }
            if (uvStatus != null)
            {
                uvStatus.text = uvMessage;
                uvStatus.messageType = uvMap == null && mesh != null ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
            }
            uvOverlay?.MarkDirtyRepaint();
        }
        private void BuildUvOverlay()
        {
            uvOverlay = new VisualElement { name = "uvOverlay", pickingMode = PickingMode.Ignore };
            uvOverlay.AddToClassList("sprite-editor-area-overlay");
            uvOverlay.generateVisualContent += DrawUvOverlay;
            toolkitPreviewCanvas.Add(uvOverlay);
            toolkitPreviewCanvas.ViewChanged += UvViewChanged;
            toolkitPreviewCanvas.RegisterCallback<PointerLeaveEvent>(_ => SetUvHovered(-1));
            uvOverlay.schedule.Execute(RefreshUvReference).Every(750);
            uvCachedChannel = -1;
        }
        private void UvViewChanged() { SetUvHovered(-1); uvOverlay?.MarkDirtyRepaint(); }
        private void SetUvHovered(int island)
        {
            if (island == uvHoveredIsland) return;
            uvHoveredIsland = island; uvOverlay?.MarkDirtyRepaint();
        }
        private int PickUvIsland(Vector2 point)
        {
            if (!uvEnabled || uvMap == null || uvCachedDocument != compositor || !HasPreviewLayers || !toolkitPreviewCanvas.contentRect.Contains(point)) return -1;
            Rect image = toolkitPreviewCanvas.ImageRect;
            if (image.width <= 0 || image.height <= 0) return -1;
            point = toolkitPreviewCanvas.ToCanvas(point);
            return uvMap.Pick(new Vector2((point.x - image.x) / image.width, 1f - (point.y - image.y) / image.height));
        }
        private void SelectUvIsland(Vector2 point, SelectionCombine combine)
        {
            RefreshUvReference();
            if (!uvEnabled || uvMap == null) { OpenUvDrawer(); ShowNotification(new GUIContent(uvMessage ?? "Assign a Mesh in UV settings.")); return; }
            int island = PickUvIsland(point);
            SetUvHovered(island);
            if (island >= 0) ChangeAreaSelection(s => s.Set(uvMap.Rasterize(island, s.Width, s.Height), combine));
            else if (combine == SelectionCombine.Replace) ChangeAreaSelection(s => s.Clear());
        }
        private static bool ClipUvLine(ref Vector2 a, ref Vector2 b, Rect bounds)
        {
            Vector2 d = b - a;
            float first = 0, last = 1;
            bool Clip(float p, float q)
            {
                if (p == 0) return q >= 0;
                float t = q / p;
                if (p < 0) first = Mathf.Max(first, t); else last = Mathf.Min(last, t);
                return first <= last;
            }
            if (!Clip(-d.x, a.x - bounds.xMin) || !Clip(d.x, bounds.xMax - a.x) ||
                !Clip(-d.y, a.y - bounds.yMin) || !Clip(d.y, bounds.yMax - a.y)) return false;
            b = a + d * last; a += d * first; return true;
        }
        private void DrawUvOverlay(MeshGenerationContext context)
        {
            if (!uvEnabled || uvMap == null || !HasPreviewLayers || uvCachedDocument != compositor) return;
            Rect image = toolkitPreviewCanvas.ImageRect;
            Vector2 View(Vector2 uv) => toolkitPreviewCanvas.ToView(new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height));
            var p = context.painter2D;
            p.lineCap = LineCap.Round;
            int highlighted = IsUvSelectionTool ? uvHoveredIsland : -1;
            for (int pass = 0; pass < 2; pass++)
            {
                bool hovered = pass == 1;
                if (hovered && highlighted < 0) continue;
                p.lineWidth = hovered ? 2f : 1f;
                Color color = hovered ? Color.Lerp(uvLineColor, Color.white, .6f) : uvLineColor;
                color.a = hovered ? Mathf.Max(.85f, uvLineOpacity) : uvLineOpacity;
                p.strokeColor = color; p.BeginPath();
                int batch = 0;
                for (int i = hovered ? highlighted : 0; i < (hovered ? highlighted + 1 : uvMap.islands.Count); i++)
                {
                    if (!hovered && i == highlighted) continue;
                    foreach (Vector4 edge in uvMap.islands[i].edges)
                    {
                        Vector2 a = new Vector2(edge.x, edge.y), b = new Vector2(edge.z, edge.w);
                        if (!ClipUvLine(ref a, ref b, new Rect(0, 0, 1, 1))) continue;
                        a = View(a); b = View(b);
                        if (!ClipUvLine(ref a, ref b, uvOverlay.contentRect)) continue;
                        p.MoveTo(a); p.LineTo(b);
                        if (++batch == 512) { p.Stroke(); p.BeginPath(); batch = 0; }
                    }
                }
                if (batch > 0) p.Stroke();
                if (!IsUvSelectionTool) continue;
                p.fillColor = color; p.BeginPath(); batch = 0;
                for (int i = hovered ? highlighted : 0; i < (hovered ? highlighted + 1 : uvMap.islands.Count); i++)
                {
                    if (!hovered && i == highlighted) continue;
                    Vector2 marker = uvMap.islands[i].marker;
                    if (marker.x < 0 || marker.y < 0 || marker.x > 1 || marker.y > 1) continue;
                    marker = View(marker);
                    if (!uvOverlay.contentRect.Contains(marker)) continue;
                    float radius = hovered ? 3f : 2f;
                    p.MoveTo(marker + Vector2.right * radius); p.Arc(marker, radius, 0f, 360f); p.ClosePath();
                    if (++batch == 512) { p.Fill(); p.BeginPath(); batch = 0; }
                }
                if (batch > 0) p.Fill();
            }
        }
    }
}
