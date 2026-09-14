using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private LayerDragGhost layerDragGhost;
        private LayerDragGhostManipulator layerDragGhostManipulator;
        private Vector2 layerDragGhostOffset;

        private void InstallLayerDragGhost()
        {
            ClearLayerDragGhost();
            if (layerDragGhostManipulator != null)
                rootVisualElement.RemoveManipulator(layerDragGhostManipulator);
            layerDragGhostManipulator = new LayerDragGhostManipulator(this);
            rootVisualElement.AddManipulator(layerDragGhostManipulator);
        }

        private void ShowLayerDragGhost(VisualElement row, Layer layer, Vector2 pressedAt, Vector2 pointer)
        {
            ClearLayerDragGhost();
            layerDragGhostOffset = pressedAt - row.worldBound.position;
            layerDragGhost = new LayerDragGhost(row, layer.layerName);
            rootVisualElement.Add(layerDragGhost);
            MoveLayerDragGhost(pointer);
        }

        private void MoveLayerDragGhost(Vector2 pointer)
        {
            if (layerDragGhost == null) return;
            Vector2 position = rootVisualElement.WorldToLocal(pointer) - layerDragGhostOffset;
            layerDragGhost.style.translate = new Translate(position.x, position.y);
        }

        private void ClearLayerDragGhost()
        {
            layerDragGhost?.RemoveFromHierarchy();
            layerDragGhost = null;
        }

        private sealed class LayerDragGhostManipulator : Manipulator
        {
            private readonly TextureCompositorWindow owner;
            public LayerDragGhostManipulator(TextureCompositorWindow owner) => this.owner = owner;
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(OnUpdated, TrickleDown.TrickleDown);
                target.RegisterCallback<DragPerformEvent>(OnPerform, TrickleDown.TrickleDown);
                target.RegisterCallback<DragExitedEvent>(OnExited, TrickleDown.TrickleDown);
                target.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<DragUpdatedEvent>(OnUpdated, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragPerformEvent>(OnPerform, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragExitedEvent>(OnExited, TrickleDown.TrickleDown);
                target.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                owner.ClearLayerDragGhost();
            }
            private void OnUpdated(DragUpdatedEvent evt)
            {
                if (!ReferenceEquals(DragAndDrop.GetGenericData(DraggedCompositorIdKey), owner.compositor))
                    owner.ClearLayerDragGhost();
                else owner.MoveLayerDragGhost(evt.mousePosition);
            }
            // Only remove the visual here: the drop target still needs the native drag payload.
            private void OnPerform(DragPerformEvent evt) => owner.ClearLayerDragGhost();
            private void OnExited(DragExitedEvent evt) => owner.ClearLayerDragGhost();
            private void OnKey(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Escape) owner.ClearLayerDragGhost();
            }
            private void OnDetach(DetachFromPanelEvent evt)
            {
                if (evt.target == target) owner.ClearLayerDragGhost();
            }
        }

        private sealed class LayerDragGhost : VisualElement
        {
            private readonly Color background;

            public LayerDragGhost(VisualElement row, string layerName)
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-layer-drag-ghost");
                style.width = row.worldBound.width;
                style.height = row.worldBound.height;
                background = row.resolvedStyle.backgroundColor;
                generateVisualContent += DrawBackground;

                Image thumbnail = row.Q<Image>(className: "whimtex-layer-thumbnail");
                if (thumbnail != null)
                    AddCopy(new Image { image = thumbnail.image, scaleMode = thumbnail.scaleMode }, thumbnail, row);
                else
                {
                    VisualElement foldout = row.Q(className: "whimtex-group-foldout");
                    if (foldout != null) AddCopy(new Label(foldout.Q<Label>()?.text), foldout, row);
                }

                TextField name = row.Q<TextField>(className: "whimtex-layer-name");
                if (name != null)
                {
                    var label = new Label(layerName ?? "");
                    label.AddToClassList("whimtex-layer-drag-ghost-name");
                    // Copy the text element's geometry when available, without cloning the input.
                    VisualElement text = name.Q(className: "unity-text-element") ?? name;
                    label.style.color = text.resolvedStyle.color;
                    label.style.fontSize = text.resolvedStyle.fontSize;
                    AddCopy(label, text, row);
                }
            }

            private void AddCopy(VisualElement copy, VisualElement source, VisualElement row)
            {
                copy.pickingMode = PickingMode.Ignore;
                copy.AddToClassList("whimtex-layer-drag-ghost-part");
                Rect rect = source.worldBound;
                Vector2 position = rect.position - row.worldBound.position;
                copy.style.translate = new Translate(position.x, position.y);
                copy.style.width = rect.width;
                copy.style.height = rect.height;
                Add(copy);
            }

            private void DrawBackground(MeshGenerationContext context)
            {
                Rect r = contentRect;
                if (r.width <= 0f || r.height <= 0f) return;
                Color transparent = new Color(background.r, background.g, background.b, 0f);
                // A vertex-color gradient works on the package's oldest supported UI Toolkit too.
                MeshWriteData mesh = context.Allocate(4, 6);
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = background });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = transparent });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = transparent });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = background });
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
            }
        }
    }
}
