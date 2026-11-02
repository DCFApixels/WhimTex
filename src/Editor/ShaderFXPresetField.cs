using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXPresetField : BaseField<string>
    {
        private readonly Func<string> sourcePath;
        private readonly Image icon;
        private readonly Label fileName;
        private UnityEngine.Object asset;

        internal ShaderFXPresetField(ShaderFX effect) : this(() => effect != null ? effect.PresetSourcePath : null) { }

        internal ShaderFXPresetField(Func<string> sourcePath, Action openSource = null) : this(sourcePath, openSource, new VisualElement()) { }

        private ShaderFXPresetField(Func<string> sourcePath, Action openSource, VisualElement input) : base("Preset", input)
        {
            this.sourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
            name = "shaderFXPresetSource";
            AddToClassList(ObjectField.ussClassName);
            AddToClassList("whimtex-shader-fx-preset-source");
            labelElement.AddToClassList(ObjectField.labelUssClassName);
            input.AddToClassList(ObjectField.inputUssClassName);
            var display = new VisualElement { pickingMode = PickingMode.Ignore };
            display.AddToClassList(ObjectField.objectUssClassName);
            display.AddToClassList("unity-object-field-display");
            icon = new Image { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("unity-object-field-display__icon");
            fileName = new Label { pickingMode = PickingMode.Ignore };
            fileName.AddToClassList("unity-object-field-display__label");
            display.Add(icon);
            display.Add(fileName);
            input.Add(display);
            input.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (evt.clickCount == 2)
                {
                    if (openSource != null) openSource();
                    else ShaderFXExternalCode.OpenPresetSource(value);
                }
                else if (evt.clickCount == 1 && asset != null) EditorGUIUtility.PingObject(asset);
                evt.StopPropagation();
            });
            RegisterCallback<DragUpdatedEvent>(evt => { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; evt.StopPropagation(); });
            RegisterCallback<DragPerformEvent>(evt => evt.StopPropagation());
            this.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                string path = string.IsNullOrEmpty(value) ? null : PresetLibraryPaths.PhysicalPath(value);
                string action = Application.platform == RuntimePlatform.OSXEditor ? "Reveal in Finder" :
                    Application.platform == RuntimePlatform.WindowsEditor ? "Show in Explorer" : "Show in File Manager";
                evt.menu.AppendAction(action, _ => EditorUtility.RevealInFinder(path),
                    File.Exists(path) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.StopPropagation();
            }));
            Refresh();
        }

        internal void Refresh()
        {
            string path = sourcePath();
            EnableInClassList("whimtex-shader-fx-hidden", string.IsNullOrEmpty(path));
            if (path == value) return;
            SetValueWithoutNotify(path);
            tooltip = path;
            string assetPath = string.IsNullOrEmpty(path) ? null : Path.IsPathRooted(path) ? PresetLibraryPaths.AssetPath(path) : path;
            asset = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadMainAssetAtPath(assetPath);
            fileName.text = string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path);
            icon.image = asset != null ? AssetPreview.GetMiniThumbnail(asset) : EditorGUIUtility.IconContent("TextAsset Icon").image;
        }
    }
}
