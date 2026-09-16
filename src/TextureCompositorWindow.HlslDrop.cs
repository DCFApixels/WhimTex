using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private sealed class HlslEffectDropManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private UnityEngine.Object inspected;
            private ShaderFXCatalog.Entry entry;

            internal HlslEffectDropManipulator(TextureCompositorWindow owner) => this.owner = owner;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(OnUpdated, TrickleDown.TrickleDown);
                target.RegisterCallback<DragPerformEvent>(OnPerform, TrickleDown.TrickleDown);
                target.RegisterCallback<DragLeaveEvent>(OnLeave);
                target.RegisterCallback<DragExitedEvent>(OnExit);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<DragUpdatedEvent>(OnUpdated, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragPerformEvent>(OnPerform, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragLeaveEvent>(OnLeave);
                target.UnregisterCallback<DragExitedEvent>(OnExit);
            }

            private ShaderFXCatalog.Entry Resolve(bool refresh = false)
            {
                if (owner.compositor == null || DragAndDrop.GetGenericData(DraggedCompositorIdKey) != null) return null;
                var objects = DragAndDrop.objectReferences;
                if (objects.Length != 1 || objects[0] == null) return null;
                if (refresh || inspected != objects[0])
                {
                    inspected = objects[0];
                    entry = ShaderFXCatalog.InspectDroppedHlsl(AssetDatabase.GetAssetPath(inspected));
                }
                return entry;
            }

            private Layer FindLayer(VisualElement element, out VisualElement row)
            {
                row = null;
                for (var current = element; current != null && current != target; current = current.parent)
                    if (current.userData is string id && owner.compositor.FindLayer(id) is Layer layer)
                    {
                        row = current;
                        return layer;
                    }
                return null;
            }

            private bool CanApply(Layer layer) => layer == null ||
                (layer.Behaviour != null && !(layer.Behaviour is PendingLayerBehaviour) &&
                 !WhimTexApi.IsLayerContentLocked(owner.compositor, layer));

            private void OnUpdated(DragUpdatedEvent evt)
            {
                var candidate = Resolve();
                if (candidate == null) return;
                var layer = FindLayer(evt.target as VisualElement, out var row);
                owner.ClearFooterDropIndicator();
                bool allowed = candidate.error == null && CanApply(layer);
                if (allowed)
                    owner.SetToolkitDropIndicator(row ?? owner.toolkitLayerHierarchyRoot, row != null, true, 0);
                else owner.ClearToolkitDropIndicator();
                DragAndDrop.visualMode = allowed ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.StopImmediatePropagation();
            }

            private void OnPerform(DragPerformEvent evt)
            {
                var candidate = Resolve(true);
                if (candidate == null) return;
                var layer = FindLayer(evt.target as VisualElement, out _);
                evt.StopImmediatePropagation();
                owner.ClearToolkitDropIndicator();
                if (!CanApply(layer)) return;
                if (candidate.HasError)
                {
                    Debug.LogError("WhimTex FX: " + candidate.error);
                    return;
                }
                DragAndDrop.AcceptDrag();
                owner.FinishPreviewTransform();
                owner.FinishPaintingStroke();
                owner.ApplyDroppedHlsl(candidate, layer);
                inspected = null;
                entry = null;
            }

            private void OnLeave(DragLeaveEvent evt)
            {
                if (entry != null) owner.ClearToolkitDropIndicator();
                inspected = null;
                entry = null;
            }

            private void OnExit(DragExitedEvent evt) => OnLeave(null);
        }

        private void ApplyDroppedHlsl(ShaderFXCatalog.Entry entry, Layer layer)
        {
            ShaderFX effect;
            try { effect = ShaderFX.FromCatalog(compositor, entry); }
            catch (Exception error)
            {
                Debug.LogError("WhimTex FX: " + error.Message);
                ShowNotification(new GUIContent("Could not compile HLSL effect. See Console."));
                return;
            }
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                ExecuteModelChange("Drop HLSL Effect", () =>
                {
                    if (layer == null)
                    {
                        layer = new ShaderProcessorLayerBehaviour();
                        layer.layerName = compositor.AllocateLayerName(layer, effect.name);
                        compositor.layers.Insert(0, layer);
                    }
                    compositor.AdoptAgentShaderFX(effect, "Drop HLSL Effect");
                    layer.modifiers.Add(effect);
                    SelectOnlyLayer(layer.Id);
                    layerFxExpanded = true;
                });
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                Undo.IncrementCurrentGroup();
            }
        }
    }
}
