using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private float brushStrokePreviewScale = 1f;
        [NonSerialized] private Image brushStrokePreview;
        [NonSerialized] private IVisualElementScheduledItem brushStrokePreviewSchedule;
        [NonSerialized] private DrawingLayerBehaviour brushStrokePreviewLayer;
        [NonSerialized] private RenderTexture brushStrokePreviewTexture;
        [NonSerialized] private Material brushStrokePreviewMaterial;
        [NonSerialized] private bool brushStrokePreviewDirty = true;
        [NonSerialized] private bool brushStrokePreviewActive;
        [NonSerialized] private bool brushStrokePreviewHdr;

        private void BuildBrushStrokePreview(VisualElement parent)
        {
            ReleaseBrushStrokePreview();
            brushStrokePreview = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                tooltip = "Sample stroke with the current brush settings. Large tips are scaled to fit. Erasing is shown on gray paint; layer effects and symmetry are not applied."
            };
            brushStrokePreview.AddToClassList("whimtex-brush-stroke-preview");
            brushStrokePreview.RegisterCallback<GeometryChangedEvent>(_ => brushStrokePreviewDirty = true);
            brushStrokePreview.RegisterCallback<DetachFromPanelEvent>(_ => ReleaseBrushStrokePreview());
            parent.Add(brushStrokePreview);
            brushStrokePreviewScale = ClampBrushPreviewScale(brushStrokePreviewScale);
            var scale = new Slider("Preview Scale (%)", 5f, 100f)
            {
                value = brushStrokePreviewScale * 100f, showInputField = true,
                tooltip = "Scale only the sample brush to fit its scatter. Does not change the painting size or brush settings."
            };
            scale.AddToClassList("whimtex-brush-preview-scale");
            scale.RegisterValueChangedCallback(evt =>
            {
                brushStrokePreviewScale = ClampBrushPreviewScale(evt.newValue * .01f);
                scale.SetValueWithoutNotify(brushStrokePreviewScale * 100f);
                brushStrokePreviewDirty = true;
            });
            parent.Add(scale);
            brushStrokePreviewSchedule = brushStrokePreview.schedule.Execute(UpdateBrushStrokePreview).Every(75);
            brushStrokePreviewSchedule.Pause();
        }

        private void SetBrushStrokePreviewActive(bool active)
        {
            if (active == brushStrokePreviewActive) return;
            brushStrokePreviewActive = active;
            if (active)
            {
                brushStrokePreviewDirty = true;
                brushStrokePreviewSchedule?.Resume();
            }
            else brushStrokePreviewSchedule?.Pause();
        }

        private void UpdateBrushStrokePreview()
        {
            if (!brushStrokePreviewActive || brushStrokePreview?.panel == null ||
                EditorApplication.isCompiling || EditorApplication.isUpdating || paintSettings == null) return;
            if (brushStrokePreviewHdr != WhimTexColorInputs.Hdr)
            {
                brushStrokePreviewHdr = WhimTexColorInputs.Hdr;
                brushStrokePreviewDirty = true;
            }
            if (!brushStrokePreviewDirty) return;
            float availableWidth = brushStrokePreview.contentRect.width;
            if (!(availableWidth > 0f)) return;
            int width = Mathf.Clamp(Mathf.RoundToInt(availableWidth * 2f), 128, 768);
            const int height = 192;
            brushStrokePreviewDirty = false;
            try
            {
                if (brushStrokePreviewMaterial == null)
                {
                    Material source = WhimTexMaterials.PreviewChannels;
                    if (source == null) return;
                    brushStrokePreviewMaterial = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                    brushStrokePreviewMaterial.SetVector("_Channels", Vector4.one);
                    brushStrokePreviewMaterial.SetFloat("_Exposure", 1f);
                    brushStrokePreviewMaterial.SetFloat("_Debug", 0f);
                }
                if (brushStrokePreviewTexture == null || brushStrokePreviewTexture.width != width)
                {
                    ReleaseBrushStrokePreviewTexture();
                    brushStrokePreviewTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                    {
                        name = "Brush Stroke Preview", hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                    };
                    brushStrokePreviewTexture.Create();
                }
                brushStrokePreviewLayer ??= new DrawingLayerBehaviour();
                var dynamics = JsonUtility.FromJson<BrushDynamics>(JsonUtility.ToJson(paintSettings.dynamics));
                dynamics.tip = paintSettings.dynamics.tip;
                dynamics.Normalize();
                float size = Mathf.Max(1f, Mathf.Clamp(paintSettings.brushSize * 2f, 2f, 56f) * brushStrokePreviewScale);
                bool erase = paintSettings.tool == PaintToolMode.Eraser;
                var parameters = new PaintStrokeParameters(WhimTexColorInputs.DisplayColor(paintSettings.brushColor),
                    size, paintSettings.brushHardness, paintSettings.brushSpacing, erase, dynamics: dynamics,
                    standardColorInputs: !WhimTexColorInputs.Hdr);
                brushStrokePreviewLayer.RenderBrushPreview(brushStrokePreviewTexture, parameters, brushStrokePreviewMaterial);
                brushStrokePreview.image = brushStrokePreviewTexture;
                brushStrokePreview.MarkDirtyRepaint();
            }
            catch (Exception exception)
            {
                ReleaseBrushStrokePreview();
                Debug.LogException(exception);
            }
        }

        private void ReleaseBrushStrokePreviewTexture()
        {
            if (brushStrokePreview != null) brushStrokePreview.image = null;
            if (brushStrokePreviewTexture == null) return;
            brushStrokePreviewTexture.Release();
            DestroyImmediate(brushStrokePreviewTexture);
            brushStrokePreviewTexture = null;
        }

        private static float ClampBrushPreviewScale(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp(value, .05f, 1f);

        private void ReleaseBrushStrokePreview()
        {
            brushStrokePreviewSchedule?.Pause();
            brushStrokePreviewActive = false;
            brushStrokePreviewDirty = true;
            ReleaseBrushStrokePreviewTexture();
            brushStrokePreviewLayer?.ReleaseTransientResources();
            brushStrokePreviewLayer = null;
            if (brushStrokePreviewMaterial != null) DestroyImmediate(brushStrokePreviewMaterial);
            brushStrokePreviewMaterial = null;
        }
    }
}
