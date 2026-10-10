using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [SerializeField] private bool transformSettingsExpanded;
        [SerializeField] private bool renderingSettingsExpanded;
        [SerializeField] private bool layerPropertiesExpanded = true;
        [SerializeField] private bool layerFxExpanded;
        [SerializeField] private LayerPreviewPanel.ViewState layerPreviewState = new LayerPreviewPanel.ViewState { collapsed = true };
        [NonSerialized] private LayerPreviewPanel toolkitLayerPreview;
        [NonSerialized] private ScrollView toolkitLayerSettingsScroll;
        [NonSerialized] private Label toolkitLayerSettingsTitle;
        [NonSerialized] private Button toolkitLayerGuidButton;
        [NonSerialized] private Layer toolkitInspectorLayer;
        [NonSerialized] private LayerBehaviour toolkitInspectorBehaviour;
        [NonSerialized] private WhimTexDocument toolkitInspectorDocument;
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
            toolkitLayerPreview?.Bind(activeDocument, selected);
            bool locked = WhimTexApi.IsLayerContentLocked(activeDocument, selected);
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
                toolkitInspectorDocument != activeDocument || toolkitInspectorLocked != locked ||
                !ReferenceEquals(toolkitInspectorBehaviour, selected?.Behaviour))
            {
                ResetToolkitLayerInspector();
                toolkitInspectorBuilt = true;
                toolkitInspectorLayer = selected;
                toolkitInspectorBehaviour = selected?.Behaviour;
                toolkitInspectorDocument = activeDocument;
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
                var cancel = new Button(() => WhimTexApi.CancelLiveReservation(activeDocument, pending)) { text = "Cancel Generation" };
                root.Add(cancel);
                return;
            }

            if (WhimTexApi.IsLayerContentLocked(activeDocument, layer))
            {
                root.Add(new HelpBox("The agent is editing this layer. You can rename, hide or move it. Cancel the edit to unlock its settings.", HelpBoxMessageType.Info));
                root.Add(new Button(() => WhimTexApi.CancelLayerEdit(activeDocument, layer)) { text = "Cancel Agent Edit" });
                var settings = new VisualElement();
                root.Add(settings);
                settings.SetEnabled(false);
                root = settings;
            }
            Action<string, Action> apply = InspectorChangeFor(layer);
            toolkitInspectorShaderFX = WhimTexUI.BuildLayerInspectorSections(root, layer, activeDocument,
                apply, toolkitInspectorBindings, properties => BuildToolkitLayerProperties(properties, layer, apply),
                renderingSettingsExpanded, value => renderingSettingsExpanded = value,
                layerPropertiesExpanded, value => layerPropertiesExpanded = value,
                layerFxExpanded, value => layerFxExpanded = value,
                transformSettingsExpanded, value => transformSettingsExpanded = value);
        }

        private Action<string, Action> InspectorChangeFor(Layer layer)
        {
            var document = activeDocument;
            var behaviour = layer.Behaviour;
            return (undoName, change) =>
            {
                if (activeDocument != document || activeDocument == null ||
                    !ReferenceEquals(activeDocument.FindLayer(layer.Id), layer) ||
                    !ReferenceEquals(layer.Behaviour, behaviour) ||
                    !ReferenceEquals(GetSelectedLayer(), layer) ||
                    WhimTexApi.IsLayerContentLocked(activeDocument, layer))
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
                    DrawingLayerEditorWindow.BuildFields(root, drawing, activeDocument, apply, toolkitInspectorBindings);
                    break;
                case FileLayerBehaviour file:
                    FileLayerEditorWindow.BuildFields(root, file, activeDocument, apply, toolkitInspectorBindings);
                    break;
                case ColorFillLayerBehaviour fill:
                    ColorFillLayerEditorWindow.BuildFields(root, fill, activeDocument, apply, toolkitInspectorBindings);
                    break;
                case GradientLayerBehaviour gradient:
                    GradientLayerEditorWindow.BuildFields(root, gradient, activeDocument, apply, toolkitInspectorBindings);
                    break;
                case TextLayerBehaviour text:
                    TextLayerEditorWindow.BuildFields(root, text, apply, toolkitInspectorBindings);
                    break;
                case NoiseLayerBehaviour noise:
                    NoiseLayerEditorWindow.BuildFields(root, noise, activeDocument, apply, toolkitInspectorBindings);
                    break;
                case ShapeLayerBehaviour shape:
                    ShapeLayerEditorWindow.BuildFields(root, shape, activeDocument, apply, toolkitInspectorBindings);
                    break;
                case OutlineLayerBehaviour outline:
                    OutlineLayerEditorWindow.BuildFields(root, outline, activeDocument, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case SDFLayerBehaviour sdf:
                    SDFLayerEditorWindow.BuildFields(root, sdf, activeDocument, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case NormalMapLayerBehaviour normalMap:
                    NormalMapLayerEditorWindow.BuildFields(root, normalMap, activeDocument, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case BlurLayerBehaviour blur:
                    BlurLayerEditorWindow.BuildFields(root, blur, activeDocument, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case SharpenLayerBehaviour sharpen:
                    SharpenLayerEditorWindow.BuildFields(root, sharpen, activeDocument, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
                case MakeSeamlessLayerBehaviour seamless:
                    MakeSeamlessLayerEditorWindow.BuildFields(root, seamless, activeDocument, apply, toolkitInspectorBindings,
                        AddToolkitInspectorEffectTarget);
                    break;
            }
        }

        private void AddToolkitInspectorEffectTarget(VisualElement root, TargetedLayerBehaviour effect)
        {
            toolkitInspectorEffectTarget = new EffectTargetSettingsView(
                activeDocument, InspectorChangeFor(effect.Owner), toolkitInspectorBindings);
            toolkitInspectorEffectTarget.Build(root, effect);
        }
    }
}
