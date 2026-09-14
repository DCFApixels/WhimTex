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
            }) { text = "Save & Update Output" };
            save.SetEnabled(AssetDatabase.Contains(document));
            root.Add(save);
            root.Add(new HelpBox("Save updates the embedded Texture2D and Output Sprite (100 pixels per unit, centered pivot). " +
                "Use the main asset in texture fields, or expand it in Project to use Output Sprite. " +
                "Double-click either to edit the layers.", HelpBoxMessageType.Info));

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
