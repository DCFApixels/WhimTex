using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    [CustomEditor(typeof(BrushPresetAsset))]
    public sealed class BrushPresetAssetEditor : Editor
    {
        private Texture2D inspectorPreview;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            WhimTexUI.ApplyWindowStyles(root);
            root.Add(new HelpBox("Drag this preset into WhimTex to choose the brush, or select it from the Project section of the brush preset menu.", HelpBoxMessageType.Info));
            if (inspectorPreview != null) DestroyImmediate(inspectorPreview);
            inspectorPreview = RenderPreview(512, 192, false);
            if (inspectorPreview != null)
            {
                var image = new Image { image = inspectorPreview, scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore };
                image.AddToClassList("whimtex-saved-output-preview");
                root.Add(image);
            }
            return root;
        }

        private void OnDisable()
        {
            if (inspectorPreview != null) DestroyImmediate(inspectorPreview);
            inspectorPreview = null;
        }

        public override Texture2D RenderStaticPreview(string assetPath, UnityEngine.Object[] subAssets, int width, int height)
        {
            if (width <= 0 || height <= 0) return null;
            int side = Mathf.Clamp(Mathf.Max(width, height), 16, 768);
            return RenderPreview(side, side, true);
        }

        private Texture2D RenderPreview(int width, int height, bool projectThumbnail)
        {
            var preset = ((BrushPresetAsset)target).preset;
            if (preset == null || width <= 0 || height <= 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return null;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture surface = null;
            Material display = null;
            Texture2D result = null;
            var layer = new DrawingLayerBehaviour();
            try
            {
                int outputHeight = height;
                if (projectThumbnail) height = Mathf.Max(8, Mathf.RoundToInt(width * .375f));
                var source = WhimTexMaterials.PreviewChannels;
                if (source == null || !source.shader.isSupported) return null;
                display = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                display.SetVector("_Channels", Vector4.one);
                display.SetFloat("_Exposure", 1f); display.SetFloat("_Debug", 0f);
                surface = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var dynamics = JsonUtility.FromJson<BrushDynamics>(JsonUtility.ToJson(preset.dynamics));
                dynamics.tip = preset.dynamics.tip;
                dynamics.Normalize();
                float size = Mathf.Clamp(preset.size * 2f, 2f, Mathf.Min(56f, height * .3f));
                var parameters = new PaintStrokeParameters(Color.white, size, preset.hardness, preset.spacing,
                    false, dynamics: dynamics, standardColorInputs: false);
                layer.RenderBrushPreview(surface, parameters, display, marginScale: projectThumbnail ? .6f : 1f);
                RenderTexture.active = surface;
                result = new Texture2D(width, outputHeight, TextureFormat.RGBA32, false)
                    { name = target.name + " Preview", hideFlags = HideFlags.HideAndDontSave };
                if (projectThumbnail) result.SetPixels32(new Color32[width * outputHeight]);
                result.ReadPixels(new Rect(0, 0, width, height), 0, (outputHeight - height) / 2);
                if (projectThumbnail)
                {
                    var pixels = result.GetPixels();
                    Color background = new Color(.18f, .18f, .18f, 1f).linear;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color value = Color.Lerp(background, pixels[i].linear, pixels[i].a).gamma;
                        value.a = 1f;
                        pixels[i] = value;
                    }
                    result.SetPixels(pixels);
                }
                result.Apply(false, false);
                return result;
            }
            catch (Exception error)
            {
                if (result != null) DestroyImmediate(result);
                Debug.LogWarning("WhimTex brush preview: " + error.Message, target);
                return null;
            }
            finally
            {
                layer.ReleaseTransientResources();
                if (display != null) DestroyImmediate(display);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (surface != null) RenderTexture.ReleaseTemporary(surface);
            }
        }
    }
}
