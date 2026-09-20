using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    [CustomEditor(typeof(TextureCompositor))]
    public sealed class TextureCompositorEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            root.style.paddingTop = 4f;

            Button open = new Button(() => TextureCompositorWindow.Open((TextureCompositor)target))
            {
                text = "Open in WhimTex",
                tooltip = "Open this saved composition in the WhimTex window."
            };
            open.style.height = 28f;
            open.style.marginBottom = 6f;
            root.Add(open);

            TextureCompositor document = (TextureCompositor)target;
            WhimTexUI.ApplyWindowStyles(root);
            Button save = new Button(() =>
            {
                serializedObject.ApplyModifiedProperties();
                TextureCompositorWindow.SaveDocumentAsTiff(document);
            }) { text = "Save As TIFF", tooltip = "Legacy .asset documents are read-only. Create a new editable TIFF document." };
            save.SetEnabled(WhimTexLegacyMigration.IsLegacyAsset(document));
            root.Add(save);
            root.Add(new HelpBox(WhimTexLegacyMigration.IsLegacyAsset(document)
                ? "This legacy .asset is read-only. Use Save As TIFF to create the editable document; the source remains unchanged."
                : "Output Settings take effect when you apply or save the document. " +
                  "Use the main asset in texture fields, or expand it in Project to use Output Sprite. " +
                  "Double-click either to edit the layers.", HelpBoxMessageType.Info));

            root.Add(new Button(() => WhimTexOutputSettingsWindow.Open(document)) { text = "Output Settings…" });

            Image preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            preview.AddToClassList("whimtex-saved-output-preview");
            root.Add(preview);
            void RefreshOutputPreview()
            {
                if (document == null)
                    return;
                preview.image = document.OutputTexture;
                preview.EnableInClassList("whimtex-saved-output-preview--empty", document.OutputTexture == null);
                preview.MarkDirtyRepaint();
            }
            RefreshOutputPreview();
            root.RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.projectChanged += RefreshOutputPreview);
            root.RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.projectChanged -= RefreshOutputPreview);

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "outputFilter") continue;
                PropertyField field = new PropertyField(property.Copy());
                if (property.propertyPath == "m_Script")
                    field.SetEnabled(false);
                root.Add(field);
            }

            root.Bind(serializedObject);
            root.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                foreach (Object inspectedTarget in targets)
                {
                    if (inspectedTarget is TextureCompositor compositor)
                        compositor.MarkChanged();
                }
            });
            return root;
        }

        internal static VisualElement BuildOutputSettings(SerializedObject serializedObject)
        {
            var root = new VisualElement();
            root.AddToClassList("whimtex-output-settings-fields");
            var document = (TextureCompositor)serializedObject.targetObject;
            document.EnsureOutputSettingsBaseline();
            var output = new VisualElement();
            var textureSettings = new Foldout { text = "Texture", value = true, viewDataKey = "output-texture" };
            textureSettings.AddToClassList("whimtex-output-section");
            var settings = serializedObject.FindProperty("outputSettings");
            var linkedProperty = settings.FindPropertyRelative("linkedTextureGuid");
            var linkedTexture = new ObjectField { objectType = typeof(Texture2D), allowSceneObjects = false, name = "linked-output-texture" };
            WhimTexOutputSettingsRow.Add(output, "Linked Output", linkedTexture,
                "Overwrite this image on each document save, at canvas resolution. Its own Unity import settings are preserved. PNG, TGA, JPG or EXR in Assets. None disables this additional output.", "linkedTextureGuid");
            var linkedActions = new VisualElement();
            linkedActions.AddToClassList("whimtex-linked-output-actions");
            var linkedSettings = new Button(() => Selection.activeObject = linkedTexture.value) { text = "Texture Settings", tooltip = "Select the linked image to edit its standard Unity import settings." };
            var clearLinked = new Button(() =>
            {
                linkedProperty.stringValue = "";
                serializedObject.ApplyModifiedProperties();
                document.MarkChanged();
            }) { text = "Clear", tooltip = "Stop updating this image. The existing file is not deleted." };
            linkedActions.Add(linkedSettings);
            linkedActions.Add(clearLinked);
            output.Add(linkedActions);
            var linkedNote = new Label("Save overwrites the linked image. Import settings are edited on that image; settings below affect embedded output only.");
            linkedNote.AddToClassList("whimtex-linked-output-note");
            output.Add(linkedNote);
            void RefreshLinked(SerializedProperty property)
            {
                string path = AssetDatabase.GUIDToAssetPath(property.stringValue);
                linkedTexture.SetValueWithoutNotify(string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                bool assigned = !string.IsNullOrEmpty(property.stringValue);
                linkedActions.EnableInClassList("whimtex-output-settings-hidden", !assigned);
                linkedNote.EnableInClassList("whimtex-output-settings-hidden", !assigned);
                linkedSettings.SetEnabled(linkedTexture.value != null);
            }
            RefreshLinked(linkedProperty);
            root.TrackPropertyValue(linkedProperty, RefreshLinked);
            linkedTexture.RegisterValueChangedCallback(evt =>
            {
                string guid = evt.newValue == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(evt.newValue));
                try
                {
                    if (evt.newValue != null)
                    {
                        if (!AssetDatabase.IsMainAsset(evt.newValue) || string.IsNullOrEmpty(guid))
                            throw new System.InvalidOperationException("Choose a PNG, TGA, JPG or EXR image asset.");
                        string path = TextureCompositor.GetLinkedTexturePath(guid);
                        if (guid != linkedProperty.stringValue && !EditorUtility.DisplayDialog("Link Output Texture",
                            "Each save of this document will overwrite the image in:\n" + path +
                            "\n\nIts import settings will be preserved. Do not use a source image you want to keep unchanged.", "Link", "Cancel"))
                        { RefreshLinked(linkedProperty); return; }
                    }
                    linkedProperty.stringValue = guid;
                    serializedObject.ApplyModifiedProperties();
                    document.MarkChanged();
                }
                catch (System.Exception error)
                {
                    RefreshLinked(linkedProperty);
                    EditorUtility.DisplayDialog("Linked Output", error.Message, "OK");
                }
            });
            WhimTexOutputSettingsRow.AddProperty(output, settings.FindPropertyRelative("outputType"), "Output Type", "Texture only, or texture with sprite subassets.");
            output.Add(textureSettings);
            void AddSetting(VisualElement parent, string path, string label, string tooltip)
            {
                WhimTexOutputSettingsRow.AddProperty(parent, settings.FindPropertyRelative(path), label, tooltip);
            }
            var storageProperty = settings.FindPropertyRelative("storage");
            var storage = new PopupField<string>(new System.Collections.Generic.List<string> { "HDR Half", "HDR Float", "RGBA32" }, 0);
            var srgb = new Toggle();
            WhimTexOutputSettingsRow.Add(textureSettings, "Storage", storage, "Output channel precision. RGBA32 clamps HDR values.", "storage");
            var srgbRow = WhimTexOutputSettingsRow.Add(textureSettings, "sRGB (Color Texture)", srgb, "Enable for color textures; disable for linear data. HDR storage remains linear.");
            void RefreshStorage(SerializedProperty property)
            {
                storage.SetValueWithoutNotify(property.intValue < 2 ? storage.choices[property.intValue] : "RGBA32");
                srgb.SetValueWithoutNotify(property.intValue == (int)TextureCompositor.OutputStorage.SrgbRgba32);
                srgbRow.SetEnabled(property.intValue >= 2);
            }
            RefreshStorage(storageProperty);
            root.TrackPropertyValue(storageProperty, RefreshStorage);
            storage.RegisterValueChangedCallback(evt =>
            {
                storageProperty.intValue = evt.newValue == "HDR Half" ? 0 : evt.newValue == "HDR Float" ? 1 : srgb.value ? 3 : 2;
                serializedObject.ApplyModifiedProperties();
                ((TextureCompositor)serializedObject.targetObject).MarkChanged();
            });
            srgb.RegisterValueChangedCallback(evt =>
            {
                storageProperty.intValue = evt.newValue ? 3 : 2;
                serializedObject.ApplyModifiedProperties();
                ((TextureCompositor)serializedObject.targetObject).MarkChanged();
            });
            var advanced = new Foldout { text = "Advanced", value = true, viewDataKey = "output-advanced" };
            advanced.AddToClassList("whimtex-output-advanced");
            AddSetting(textureSettings, "alphaIsTransparency", "Alpha Is Transparency", "Extend edge colors into transparent pixels to reduce filtering fringes. Alpha is preserved. Applied on save, not Live Update.");
            textureSettings.Add(advanced);
            AddSetting(advanced, "readable", "Read/Write", "Keep a CPU copy. Required for Live Update and Sprite Editor. Disabling reduces memory after saving.");
            AddSetting(advanced, "mipMaps", "Generate Mip Maps", "Generate smaller texture levels using Unity's texture generator.");
            var mipOptions = new VisualElement();
            AddSetting(mipOptions, "mipFilter", "Mipmap Filtering", "Unity's Box or Kaiser mipmap filter.");
            AddSetting(mipOptions, "preserveCoverage", "Preserve Coverage", "Preserve alpha-test coverage in smaller mip levels.");
            var cutoff = WhimTexOutputSettingsRow.AddProperty(mipOptions, settings.FindPropertyRelative("alphaCutoff"), "Alpha Cutoff", "Alpha-test threshold for coverage preservation.");
            void RefreshCoverage(SerializedProperty property) => cutoff.parent.SetEnabled(property.boolValue);
            RefreshCoverage(settings.FindPropertyRelative("preserveCoverage"));
            root.TrackPropertyValue(settings.FindPropertyRelative("preserveCoverage"), RefreshCoverage);
            advanced.Add(mipOptions);
            void RefreshMips(SerializedProperty property) => mipOptions.SetEnabled(property.boolValue);
            RefreshMips(settings.FindPropertyRelative("mipMaps"));
            root.TrackPropertyValue(settings.FindPropertyRelative("mipMaps"), RefreshMips);
            AddSetting(textureSettings, "wrapU", "Wrap U", "Horizontal sampling outside the texture. Does not change layer tiling.");
            AddSetting(textureSettings, "wrapV", "Wrap V", "Vertical sampling outside the texture. Does not change layer tiling.");
            WhimTexOutputSettingsRow.AddProperty(textureSettings, serializedObject.FindProperty("outputFilter"), "Filter Mode", "Saved texture sampling filter.");
            AddSetting(textureSettings, "anisoLevel", "Aniso Level", "Anisotropic filtering, 0–16. Useful for textures seen at grazing angles.");
            var spriteSettings = new Foldout { text = "Sprite", value = true, viewDataKey = "output-sprite" };
            spriteSettings.AddToClassList("whimtex-output-section");
            spriteSettings.name = "output-sprite-settings";
            output.Add(spriteSettings);
            var textureOnlyNote = new HelpBox("Texture creates no sprites. Applying this type removes existing output sprites and breaks references to them. Sprite settings and slicing are kept for switching back.", HelpBoxMessageType.Warning);
            output.Add(textureOnlyNote);
            void RefreshType(SerializedProperty property)
            {
                bool sprite = property.intValue == (int)TextureCompositor.OutputType.Sprite;
                spriteSettings.EnableInClassList("whimtex-output-settings-hidden", !sprite);
                textureOnlyNote.EnableInClassList("whimtex-output-settings-hidden", sprite || document.OutputSprite == null);
            }
            RefreshType(settings.FindPropertyRelative("outputType"));
            root.TrackPropertyValue(settings.FindPropertyRelative("outputType"), RefreshType);
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                RefreshType(settings.FindPropertyRelative("outputType"));
                WhimTexOutputSettingsRow.Validate(root, document);
            });
            var mode = WhimTexOutputSettingsRow.AddProperty(spriteSettings, settings.FindPropertyRelative("spriteMode"), "Sprite Mode", "Single sprite or multiple slices.");
            mode.SetEnabled(WhimTexSpriteEditorBridge.Available);
            var spriteEditor = new Button(() =>
            {
                serializedObject.ApplyModifiedProperties();
                if (!document.SpriteOutputSettings.readable)
                {
                    EditorUtility.DisplayDialog("Sprite Editor", "Enable Read/Write before editing sprites.", "OK");
                    return;
                }
                if (document.SpriteOutputSettings.spriteMode == TextureCompositor.OutputSpriteMode.Multiple && document.OutputTexture != null)
                {
                    try { TextureCompositor.ValidateSpriteSlices(document.GetSpriteSlices(), document.width, document.height); }
                    catch (System.InvalidOperationException error)
                    {
                        if (EditorUtility.DisplayDialog("Adjust Sprite Slicing", error.Message +
                            "\nOpen Sprite Editor using the last saved image to correct the slices?", "Open Sprite Editor", "Cancel"))
                            WhimTexSpriteEditorBridge.Open?.Invoke(document);
                        return;
                    }
                }
                if (WhimTexLegacyMigration.IsLegacyAsset(document))
                {
                    EditorUtility.DisplayDialog("Legacy WhimTex asset is read-only",
                        "Migrate this document to TIFF before editing sprite settings.", "OK");
                    return;
                }
                EditorUtility.DisplayDialog("Sprite Editor",
                    "Legacy .asset documents are read-only. Save the document as TIFF before editing sprite settings.", "OK");
            }) { text = "Sprite Editor", tooltip = "Save output and open Unity Sprite Editor to slice sprites and edit their pivots and borders." };
            spriteEditor.SetEnabled(WhimTexSpriteEditorBridge.Available && AssetDatabase.Contains(serializedObject.targetObject) &&
                !WhimTexLegacyMigration.IsLegacyAsset(document));
            spriteSettings.Add(spriteEditor);
            if (!WhimTexSpriteEditorBridge.Available)
                spriteSettings.Add(new HelpBox("Install 2D Sprite (com.unity.2d.sprite) to edit slicing. Saved slices still work without it.", HelpBoxMessageType.Info));
            else if (!AssetDatabase.Contains(serializedObject.targetObject))
                spriteSettings.Add(new HelpBox("Save the document in WhimTex before opening Sprite Editor.", HelpBoxMessageType.Info));
            AddSetting(spriteSettings, "pixelsPerUnit", "Pixels Per Unit", "Canvas pixels per world unit; must be greater than zero.");
            var single = new VisualElement();
            spriteSettings.Add(single);
            var pivotProperty = settings.FindPropertyRelative("pivot");
            var pivotNames = new System.Collections.Generic.List<string>
                { "Custom", "Center", "Top Left", "Top Center", "Top Right", "Left Center", "Right Center", "Bottom Left", "Bottom Center", "Bottom Right" };
            var pivots = new[] { Vector2.zero, new Vector2(.5f, .5f), new Vector2(0, 1), new Vector2(.5f, 1), Vector2.one,
                new Vector2(0, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(.5f, 0), new Vector2(1, 0) };
            var pivotPreset = new PopupField<string>(pivotNames, 0);
            WhimTexOutputSettingsRow.Add(single, "Pivot Alignment", pivotPreset);
            void RefreshPivot(SerializedProperty property)
            {
                int index = 0;
                for (int i = 1; i < pivots.Length; i++) if (property.vector2Value.Equals(pivots[i])) { index = i; break; }
                pivotPreset.SetValueWithoutNotify(pivotNames[index]);
            }
            RefreshPivot(pivotProperty);
            root.TrackPropertyValue(pivotProperty, RefreshPivot);
            pivotPreset.RegisterValueChangedCallback(_ =>
            {
                if (pivotPreset.index == 0) return;
                pivotProperty.vector2Value = pivots[pivotPreset.index];
                serializedObject.ApplyModifiedProperties();
                document.MarkChanged();
            });
            AddSetting(single, "pivot", "Pivot", "Normalized position. Edit either coordinate for a custom pivot.");
            var border = new VisualElement();
            border.AddToClassList("whimtex-output-border");
            string[] borderNames = { "Left", "Bottom", "Right", "Top" }, components = { "x", "y", "z", "w" };
            for (int i = 0; i < borderNames.Length; i++)
            {
                var field = new FloatField(borderNames[i]);
                field.BindProperty(settings.FindPropertyRelative("border").FindPropertyRelative(components[i]));
                border.Add(field);
            }
            WhimTexOutputSettingsRow.Add(single, "Border (px)", border, "9-slice borders in canvas pixels. Opposing borders must fit within the canvas.", "border");
            void RefreshMode(SerializedProperty property) => single.SetEnabled(property.enumValueIndex == (int)TextureCompositor.OutputSpriteMode.Single);
            RefreshMode(settings.FindPropertyRelative("spriteMode"));
            single.TrackPropertyValue(settings.FindPropertyRelative("spriteMode"), RefreshMode);
            AddSetting(spriteSettings, "meshType", "Mesh Type", "Full Rect or Unity-generated Tight geometry. Use Full Rect for 9-sliced sprites.");
            AddSetting(spriteSettings, "extrude", "Extrude", "Sprite mesh extrusion passed to Unity's sprite generation (0–32).");
            AddSetting(spriteSettings, "generatePhysicsShape", "Generate Physics Shape", "Ask Unity to generate a fallback physics shape from the sprite.");
            root.Add(output);
            WhimTexOutputSettingsRow.Validate(root, document);
            return root;
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            Texture2D output = ((TextureCompositor)target).OutputTexture;
            if (output == null || width <= 0 || height <= 0)
                return null;
            float scale = Mathf.Min((float)width / output.width, (float)height / output.height);
            RenderTexture thumbnail = RenderTexture.GetTemporary(
                Mathf.Max(1, Mathf.RoundToInt(output.width * scale)),
                Mathf.Max(1, Mathf.RoundToInt(output.height * scale)),
                0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(output, thumbnail);
                return TextureCompositor.CopyToTexture2D(thumbnail);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(thumbnail);
            }
        }

        [MenuItem("Assets/Open in WhimTex", false, 2000)]
        private static void OpenSelectedDocument()
        {
            TextureCompositorWindow.Open(TextureCompositor.FindDocument(Selection.activeObject));
        }

        [MenuItem("Assets/Open in WhimTex", true)]
        private static bool CanOpenSelectedDocument()
        {
            return Selection.objects.Length == 1 && TextureCompositor.FindDocument(Selection.activeObject) != null;
        }

        [OnOpenAsset]
#if UNITY_6000_2_OR_NEWER
        public static bool OpenTextureCompositor(EntityId instanceId, int line)
#else
        public static bool OpenTextureCompositor(int instanceId, int line)
#endif
        {
#if UNITY_6000_2_OR_NEWER
            Object asset = EditorUtility.EntityIdToObject(instanceId);
#else
            Object asset = EditorUtility.InstanceIDToObject(instanceId);
#endif
            TextureCompositor compositor = TextureCompositor.FindDocument(asset);
            if (compositor == null)
                return false;

            TextureCompositorWindow.Open(compositor);
            return true;
        }
    }
}
