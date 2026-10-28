using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    public sealed class FileLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(FileLayerBehaviour);

        public static void Open(FileLayerBehaviour layer, WhimTexDocument activeDocument)
        {
            OpenPropertiesWindow<FileLayerEditorWindow>(layer, activeDocument);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (FileLayerBehaviour)source, Document, ApplyLayerChange, SettingsBindings);
        }

        internal static void BuildFields(
            VisualElement root, FileLayerBehaviour layer, WhimTexDocument activeDocument,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {

            ObjectField texture = WhimTexUI.ConfigureField(new ObjectField("Source Texture")) as ObjectField;
            texture.objectType = typeof(Texture2D);
            texture.allowSceneObjects = false;
            texture.SetValueWithoutNotify(layer.ResolveSourceTexture(activeDocument));
            bindings.Track(texture, () => (UnityEngine.Object)layer.ResolveSourceTexture(activeDocument));
            texture.RegisterValueChangedCallback(evt =>
                applyChange("Change Source Texture", () => layer.AssignSourceTexture(evt.newValue as Texture2D, activeDocument, initializeCanvas: true)));
            var row = WhimTexUI.CreateRow();
            texture.style.flexGrow = 1;
            texture.style.flexShrink = 1;
            texture.style.minWidth = 0;
            row.Add(texture);
            var error = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            void Refresh()
            {
                texture.tooltip = layer.HasExternalSource ? layer.externalSourcePath : string.Empty;
                error.text = layer.HasExternalSource ? activeDocument?.ExternalFileError(layer.externalSourcePath) ?? string.Empty : string.Empty;
                error.EnableInClassList("whimtex-hidden", string.IsNullOrEmpty(error.text));
            }
            void Assign(string path)
            {
                try
                {
                    FileLayerSource.Prepare(activeDocument, path);
                    applyChange("Change Source File", () => layer.AssignSourcePath(path, activeDocument, initializeCanvas: true));
                }
                catch (Exception exception) { EditorUtility.DisplayDialog("Source Image", exception.Message, "OK"); }
                texture.SetValueWithoutNotify(layer.ResolveSourceTexture(activeDocument));
                Refresh();
            }
            row.Add(new Button(() =>
            {
                string folder = layer.HasExternalSource ? System.IO.Path.GetDirectoryName(layer.externalSourcePath) : string.Empty;
                string path = EditorUtility.OpenFilePanelWithFilters("Select Source Image", folder, FileLayerSource.FileFilters);
                if (!string.IsNullOrEmpty(path)) Assign(path);
            }) { text = "Browse…", tooltip = "Choose a source image, including files outside the project." });
            texture.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var paths = FileLayerSource.DragPaths();
                bool valid = texture.enabledInHierarchy && paths?.Length == 1;
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                texture.EnableInClassList("whimtex-file-source--drop", valid);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            texture.RegisterCallback<DragPerformEvent>(evt =>
            {
                texture.RemoveFromClassList("whimtex-file-source--drop");
                var paths = FileLayerSource.DragPaths();
                if (texture.enabledInHierarchy && paths?.Length == 1) { DragAndDrop.AcceptDrag(); Assign(paths[0]); }
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            texture.RegisterCallback<DragLeaveEvent>(_ => texture.RemoveFromClassList("whimtex-file-source--drop"));
            texture.RegisterCallback<DragExitedEvent>(_ => texture.RemoveFromClassList("whimtex-file-source--drop"));
            bindings.Add(Refresh);
            Refresh();
            root.Add(row);
            root.Add(error);
        }
    }

    public sealed class ColorFillLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(ColorFillLayerBehaviour);
        protected override bool ImmediateLayerPreviewUpdates => true;

        public static void Open(ColorFillLayerBehaviour layer, WhimTexDocument activeDocument)
        {
            OpenPropertiesWindow<ColorFillLayerEditorWindow>(layer, activeDocument);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (ColorFillLayerBehaviour)source, Document, ApplyLayerChange, SettingsBindings);
        }

        internal static void BuildFields(
            VisualElement root, ColorFillLayerBehaviour layer, WhimTexDocument activeDocument,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {

            var mode = WhimTexUI.ConfigureField(new EnumField("Mode", layer.mode));
            bindings.Track(mode, () => (Enum)layer.mode);
            mode.RegisterValueChangedCallback(evt => applyChange("Change Fill Mode", () => layer.mode = (ColorFillLayerBehaviour.FillMode)evt.newValue));
            root.Add(mode);
            ColorField color = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new ColorField("Color"), bindings, () => layer.color));
            bindings.Add(() => color.SetEnabled(layer.mode == ColorFillLayerBehaviour.FillMode.Color));
            color.RegisterValueChangedCallback(evt =>
                applyChange("Change Fill Color", () => layer.color = evt.newValue));
            root.Add(color);
            color.EnableInClassList("whimtex-hidden", layer.mode == ColorFillLayerBehaviour.FillMode.Pattern);
            var pattern = new VisualElement();
            FillPatternFields.Build(pattern, layer, applyChange, bindings);
            root.Add(pattern);
            bindings.Add(() =>
            {
                bool visible = layer.mode == ColorFillLayerBehaviour.FillMode.Pattern;
                pattern.EnableInClassList("whimtex-hidden", !visible);
                color.EnableInClassList("whimtex-hidden", visible);
            });
        }
    }
}
