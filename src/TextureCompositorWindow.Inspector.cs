using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private bool transformSettingsExpanded;
        [SerializeField] private bool colorSettingsExpanded;
        [SerializeField] private bool layerPropertiesExpanded = true;
        [SerializeField] private bool layerFxExpanded;
        [NonSerialized] private ScrollView toolkitLayerSettingsScroll;
        [NonSerialized] private Label toolkitLayerSettingsTitle;
        [NonSerialized] private Button toolkitLayerGuidButton;
        [NonSerialized] private Layer toolkitInspectorLayer;
        [NonSerialized] private LayerBehaviour toolkitInspectorBehaviour;
        [NonSerialized] private TextureCompositor toolkitInspectorDocument;
        [NonSerialized] private bool toolkitInspectorBuilt;
        [NonSerialized] private bool toolkitInspectorLocked;
        [NonSerialized] private EffectTargetSettingsView toolkitInspectorEffectTarget;
        [NonSerialized] private LayerShaderFXView toolkitInspectorShaderFX;
        private readonly WhimTexUI.ValueBindings toolkitInspectorBindings = new WhimTexUI.ValueBindings();

        private void ResetToolkitLayerInspector()
        {
            toolkitInspectorBuilt = false;
            toolkitInspectorLayer = null;
            toolkitInspectorBehaviour = null;
            toolkitInspectorDocument = null;
            toolkitInspectorEffectTarget = null;
            toolkitInspectorShaderFX = null;
            toolkitInspectorBindings.Clear();
        }

        private void RefreshToolkitLayerInspector(bool forceValues)
        {
            if (toolkitLayerSettingsScroll == null)
                return;

            Layer selected = GetSelectedLayer();
            bool locked = WhimTexApi.IsLayerContentLocked(compositor, selected);
            string title = selected == null ? "Layer Settings" : selected.layerName;
            if (toolkitLayerSettingsTitle != null && toolkitLayerSettingsTitle.text != title)
                toolkitLayerSettingsTitle.text = title;
            if (toolkitLayerGuidButton != null)
            {
                toolkitLayerGuidButton.SetEnabled(selected != null);
                toolkitLayerGuidButton.tooltip = selected == null
                    ? "Select a layer to copy its GUID."
                    : selected.Id;
            }
            // Rebind only when selection/document identity changes (including Undo replacement).
            // Normal value changes must preserve text editing, pointer capture and scroll position.
            if (!toolkitInspectorBuilt || !ReferenceEquals(toolkitInspectorLayer, selected) ||
                toolkitInspectorDocument != compositor || toolkitInspectorLocked != locked ||
                !ReferenceEquals(toolkitInspectorBehaviour, selected?.Behaviour))
            {
                ResetToolkitLayerInspector();
                toolkitInspectorBuilt = true;
                toolkitInspectorLayer = selected;
                toolkitInspectorBehaviour = selected?.Behaviour;
                toolkitInspectorDocument = compositor;
                toolkitInspectorLocked = locked;
                toolkitLayerSettingsScroll.Clear();
                toolkitLayerSettingsScroll.scrollOffset = Vector2.zero;
                BuildToolkitLayerInspector(toolkitLayerSettingsScroll, selected);
            }
            if (forceValues) toolkitInspectorEffectTarget?.Invalidate();
            toolkitInspectorShaderFX?.Refresh();
            toolkitInspectorBindings.Refresh(forceValues);
        }

        private void BuildToolkitLayerInspector(VisualElement root, Layer layer)
        {
            if (layer == null)
            {
                WhimTexUI.AddHelpBox(root, "Select a layer below to edit its settings.", HelpBoxMessageType.Info);
                return;
            }

            if (layer.Behaviour == null) { BuildMissingBehaviourInspector(root, layer); return; }
            if (layer?.Behaviour is PendingLayerBehaviour pending)
            {
                var status = new HelpBox(WhimTexApi.LiveReservationStatus(pending), HelpBoxMessageType.Info);
                root.Add(status);
                toolkitInspectorBindings.Add(() => status.text = WhimTexApi.LiveReservationStatus(pending));
                var cancel = new Button(() => WhimTexApi.CancelLiveReservation(compositor, pending)) { text = "Cancel Generation" };
                root.Add(cancel);
                return;
            }

            if (WhimTexApi.IsLayerContentLocked(compositor, layer))
            {
                root.Add(new HelpBox("The agent is editing this layer. You can rename, hide or move it. Cancel the edit to unlock its settings.", HelpBoxMessageType.Info));
                root.Add(new Button(() => WhimTexApi.CancelLayerEdit(compositor, layer)) { text = "Cancel Agent Edit" });
                var settings = new VisualElement();
                root.Add(settings);
                settings.SetEnabled(false);
                root = settings;
            }
            Action<string, Action> apply = InspectorChangeFor(layer);
            toolkitInspectorShaderFX = WhimTexUI.BuildLayerInspectorSections(root, layer, compositor,
                apply, toolkitInspectorBindings, properties => BuildToolkitLayerProperties(properties, layer, apply),
                colorSettingsExpanded, value => colorSettingsExpanded = value,
                layerPropertiesExpanded, value => layerPropertiesExpanded = value,
                layerFxExpanded, value => layerFxExpanded = value,
                transformSettingsExpanded, value => transformSettingsExpanded = value);
        }

        private Action<string, Action> InspectorChangeFor(Layer layer)
        {
            var document = compositor;
            var behaviour = layer.Behaviour;
            return (undoName, change) =>
            {
                if (compositor != document || compositor == null ||
                    !ReferenceEquals(compositor.FindLayer(layer.Id), layer) ||
                    !ReferenceEquals(layer.Behaviour, behaviour) ||
                    !ReferenceEquals(GetSelectedLayer(), layer) ||
                    WhimTexApi.IsLayerContentLocked(compositor, layer))
                {
                    toolkitRefreshRequested = true;
                    return;
                }
                ApplyToolkitChange(undoName, change);
            };
        }

        private void BuildToolkitLayerProperties(VisualElement root, Layer layer, Action<string, Action> apply)
        {
            switch (layer?.Behaviour)
            {
                case ShaderProcessorLayerBehaviour:
                    WhimTexUI.AddHelpBox(root, "Processes the composited layers below. Normal blends between the original and processed image using Opacity. In a Pass Through group, the external backdrop is included. Add or edit Shader FX below.", HelpBoxMessageType.Info);
                    break;
                case DrawingLayerBehaviour drawing:
                    DrawingLayerEditorWindow.BuildFields(root, drawing, compositor, apply, toolkitInspectorBindings);
                    break;
                case FileLayerBehaviour file:
                    FileLayerEditorWindow.BuildFields(root, file, compositor, apply, toolkitInspectorBindings);
                    break;
                case ColorFillLayerBehaviour fill:
                    ColorFillLayerEditorWindow.BuildFields(root, fill, compositor, apply, toolkitInspectorBindings);
                    break;
                case GradientLayerBehaviour gradient:
                    GradientLayerEditorWindow.BuildFields(root, gradient, compositor, apply, toolkitInspectorBindings);
                    break;
                case NoiseLayerBehaviour noise:
                    NoiseLayerEditorWindow.BuildFields(root, noise, compositor, apply, toolkitInspectorBindings);
                    break;
                case ShapeLayerBehaviour shape:
                    ShapeLayerEditorWindow.BuildFields(root, shape, compositor, apply, toolkitInspectorBindings);
                    break;
                case OutlineLayerBehaviour outline:
                    OutlineLayerEditorWindow.BuildFields(root, outline, compositor, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case SDFLayerBehaviour sdf:
                    SDFLayerEditorWindow.BuildFields(root, sdf, compositor, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case NormalMapLayerBehaviour normalMap:
                    NormalMapLayerEditorWindow.BuildFields(root, normalMap, compositor, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case BlurLayerBehaviour blur:
                    BlurLayerEditorWindow.BuildFields(root, blur, compositor, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case MakeSeamlessLayerBehaviour seamless:
                    MakeSeamlessLayerEditorWindow.BuildFields(root, seamless, compositor, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
            }
        }

        private void AddToolkitInspectorEffectTarget(VisualElement root, TargetedLayerBehaviour effect)
        {
            toolkitInspectorEffectTarget = new EffectTargetSettingsView(
                compositor, InspectorChangeFor(effect.Owner), toolkitInspectorBindings);
            toolkitInspectorEffectTarget.Build(root, effect);
        }
    }
}
