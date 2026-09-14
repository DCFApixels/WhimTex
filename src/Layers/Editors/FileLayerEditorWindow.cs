using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class FileLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(FileLayerBehaviour);

        public static void Open(FileLayerBehaviour layer, TextureCompositor compositor)
        {
            OpenPropertiesWindow<FileLayerEditorWindow>(layer, compositor);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (FileLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings);
        }

        internal static void BuildFields(
            VisualElement root, FileLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {

            ObjectField texture = WhimTexUI.ConfigureField(new ObjectField("Source Texture")) as ObjectField;
            texture.objectType = typeof(Texture2D);
            texture.allowSceneObjects = false;
            texture.SetValueWithoutNotify(layer.sourceTexture);
            bindings.Track(texture, () => (UnityEngine.Object)layer.sourceTexture);
            texture.RegisterValueChangedCallback(evt =>
                applyChange("Change Source Texture", () => layer.AssignSourceTexture(evt.newValue as Texture2D, compositor, initializeCanvas: true)));
            root.Add(texture);
        }
    }

    public sealed class ColorFillLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(ColorFillLayerBehaviour);

        public static void Open(ColorFillLayerBehaviour layer, TextureCompositor compositor)
        {
            OpenPropertiesWindow<ColorFillLayerEditorWindow>(layer, compositor);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (ColorFillLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings);
        }

        internal static void BuildFields(
            VisualElement root, ColorFillLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {

            ColorField color = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new ColorField("Color"), bindings, () => layer.color));
            color.RegisterValueChangedCallback(evt =>
                applyChange("Change Fill Color", () => layer.color = evt.newValue));
            root.Add(color);
        }
    }
}
