using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXPresetField : BaseField<string>
    {
        private readonly Image icon;
        private readonly Label fileName;
        private readonly VisualElement fieldInput;
        private UnityEngine.Object asset;

        internal ShaderFXPresetField() : this(new VisualElement()) { }

        private ShaderFXPresetField(VisualElement input) : base("Preset", input)
        {
            name = "shaderFXPresetSource";
            AddToClassList(ObjectField.ussClassName);
            AddToClassList("whimtex-shader-fx-preset-source");
            labelElement.AddToClassList(ObjectField.labelUssClassName);
            fieldInput = input;
            var source = new VisualElement();
            source.AddToClassList(ObjectField.inputUssClassName);
            source.AddToClassList("whimtex-shader-preset-file");
            var display = new VisualElement { pickingMode = PickingMode.Ignore };
            display.AddToClassList(ObjectField.objectUssClassName);
            display.AddToClassList("unity-object-field-display");
            icon = new Image { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("unity-object-field-display__icon");
            fileName = new Label { pickingMode = PickingMode.Ignore };
            fileName.AddToClassList("unity-object-field-display__label");
            display.Add(icon);
            display.Add(fileName);
            source.Add(display);
            input.Add(source);
            source.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (evt.clickCount == 2 && File.Exists(PhysicalPath)) ShaderFXExternalCode.OpenPresetSource(value);
                else if (evt.clickCount == 1 && asset != null) EditorGUIUtility.PingObject(asset);
                evt.StopPropagation();
            });
            RegisterCallback<DragUpdatedEvent>(evt => { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; evt.StopPropagation(); });
            RegisterCallback<DragPerformEvent>(evt => evt.StopPropagation());
        }

        internal void AddButton(Button button) => fieldInput.Add(button);

        string PhysicalPath => string.IsNullOrEmpty(value) ? null : PresetLibraryPaths.PhysicalPath(value);

        internal void AddSourceActions(DropdownMenu menu)
        {
            DropdownMenuAction.Status SourceStatus(DropdownMenuAction _) => string.IsNullOrEmpty(value) ? DropdownMenuAction.Status.Hidden :
                File.Exists(PhysicalPath) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            string reveal = Application.platform == RuntimePlatform.OSXEditor ? "Reveal in Finder" :
                Application.platform == RuntimePlatform.WindowsEditor ? "Show in Explorer" : "Show in File Manager";
            menu.AppendAction("Open Preset Source", _ => ShaderFXExternalCode.OpenPresetSource(value), SourceStatus);
            menu.AppendAction(reveal, _ => EditorUtility.RevealInFinder(PhysicalPath), SourceStatus);
        }

        internal void Refresh(string path, string placeholder)
        {
            bool hasPath = !string.IsNullOrEmpty(path);
            fileName.text = hasPath ? Path.GetFileName(path) : placeholder;
            icon.EnableInClassList("whimtex-hidden", !hasPath);
            if (path == value) return;
            SetValueWithoutNotify(path);
            tooltip = path;
            string assetPath = string.IsNullOrEmpty(path) ? null : Path.IsPathRooted(path) ? PresetLibraryPaths.AssetPath(path) : path;
            asset = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadMainAssetAtPath(assetPath);
            icon.image = asset != null ? AssetPreview.GetMiniThumbnail(asset) : hasPath ? EditorGUIUtility.IconContent("TextAsset Icon").image : null;
        }
    }
}
