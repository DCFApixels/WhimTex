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
                document.TrySaveWithOutput();
            }) { text = "Apply & Save Output", tooltip = "Save the document and rebuild its embedded texture and sprite using Output Settings." };
            save.SetEnabled(AssetDatabase.Contains(document));
            root.Add(save);
            root.Add(new HelpBox("Output Settings take effect when you apply or save the document. " +
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
            var output = new Foldout { text = "Output Settings", value = true };
            var textureSettings = new Foldout { text = "Texture", value = true };
            output.Add(textureSettings);
            textureSettings.Add(new PropertyField(serializedObject.FindProperty("outputFilter"), "Filter Mode"));
            var settings = serializedObject.FindProperty("outputSettings");
            void AddSetting(VisualElement parent, string path, string label, string tooltip)
            {
                parent.Add(new PropertyField(settings.FindPropertyRelative(path), label) { tooltip = tooltip });
            }
            AddSetting(textureSettings, "storage", "Storage", "HDR Half: 16-bit float per channel (default). HDR Float: 32-bit storage of the existing half-float composite. Linear RGBA32: 8-bit linear data. sRGB RGBA32: 8-bit encoded color. RGBA32 clamps HDR values.");
            AddSetting(textureSettings, "wrapU", "Wrap U", "Horizontal sampling outside the texture. Does not change layer tiling.");
            AddSetting(textureSettings, "wrapV", "Wrap V", "Vertical sampling outside the texture. Does not change layer tiling.");
            AddSetting(textureSettings, "anisoLevel", "Aniso Level", "Anisotropic filtering, 0–16. Useful for textures seen at grazing angles.");
            AddSetting(textureSettings, "mipMaps", "Generate Mip Maps", "Generate the full mip chain using Unity's standard generation. No custom mip filtering or alpha-coverage preservation.");
            var spriteSettings = new Foldout { text = "Sprite", value = true };
            output.Add(spriteSettings);
            var mode = new PropertyField(settings.FindPropertyRelative("spriteMode"), "Sprite Mode");
            mode.SetEnabled(WhimTexSpriteEditorBridge.Available);
            spriteSettings.Add(mode);
            var spriteEditor = new Button(() =>
            {
                serializedObject.ApplyModifiedProperties();
                var document = (TextureCompositor)serializedObject.targetObject;
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
                if (document.TrySaveWithOutput()) WhimTexSpriteEditorBridge.Open?.Invoke(document);
            }) { text = "Sprite Editor", tooltip = "Save output and open Unity Sprite Editor to slice sprites and edit their pivots and borders." };
            spriteEditor.SetEnabled(WhimTexSpriteEditorBridge.Available && AssetDatabase.Contains(serializedObject.targetObject));
            spriteSettings.Add(spriteEditor);
            if (!WhimTexSpriteEditorBridge.Available)
                spriteSettings.Add(new HelpBox("Install 2D Sprite (com.unity.2d.sprite) to edit slicing. Saved slices still work without it.", HelpBoxMessageType.Info));
            else if (!AssetDatabase.Contains(serializedObject.targetObject))
                spriteSettings.Add(new HelpBox("Save the document in WhimTex before opening Sprite Editor.", HelpBoxMessageType.Info));
            AddSetting(spriteSettings, "pixelsPerUnit", "Pixels Per Unit", "Canvas pixels per world unit; must be greater than zero.");
            var single = new VisualElement();
            spriteSettings.Add(single);
            AddSetting(single, "pivot", "Pivot", "Normalized position: (0, 0) bottom left, (0.5, 0.5) center, (1, 1) top right.");
            AddSetting(single, "border", "Border (px)", "9-slice borders in canvas pixels: X = Left, Y = Bottom, Z = Right, W = Top. Opposing borders must fit within the canvas.");
            void RefreshMode(SerializedProperty property) => single.SetEnabled(property.enumValueIndex == (int)TextureCompositor.OutputSpriteMode.Single);
            RefreshMode(settings.FindPropertyRelative("spriteMode"));
            single.TrackPropertyValue(settings.FindPropertyRelative("spriteMode"), RefreshMode);
            AddSetting(spriteSettings, "meshType", "Mesh Type", "Full Rect or Unity-generated Tight geometry. Use Full Rect for 9-sliced sprites.");
            AddSetting(spriteSettings, "extrude", "Extrude", "Sprite mesh extrusion passed to Unity's sprite generation (0–32).");
            AddSetting(spriteSettings, "generatePhysicsShape", "Generate Physics Shape", "Ask Unity to generate a fallback physics shape from the sprite.");
            root.Add(output);
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
