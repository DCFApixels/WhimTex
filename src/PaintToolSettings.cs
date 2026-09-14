using System;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal sealed class PaintToolSettings
    {
        public PaintToolMode tool = PaintToolMode.Brush;
        public Color brushColor = Color.white;
        public Color secondaryBrushColor = Color.black;
        public float brushSize = 32f;
        public float brushHardness = 0.8f;
        public float brushSpacing = 0.16f;
        public BrushDynamics dynamics = new BrushDynamics();
        public string brushTipGuid;
        public long brushTipLocalId;
        public string brushTipPresetPath;
        [NonSerialized] private Texture2D ownedPresetTip;
        public int pencilSize = 1;
        public PencilShape pencilShape = PencilShape.Circle;
        public FillSampleMode fillSampleMode = FillSampleMode.CurrentLayer;
        public bool fillContiguous = true;
        public int fillTolerance = 32;
        public bool fillAntialias = true;
        public int fillExpand;

        internal static float GetSizeShortcutStep(float size) => Mathf.Max(1f, Mathf.Floor(size * 0.1f));

        internal void ResetBrushTip()
        {
            ReleasePresetTip();
            var defaults = new PaintToolSettings();
            dynamics.tip = defaults.dynamics.tip;
            dynamics.tipChannel = defaults.dynamics.tipChannel;
            dynamics.tipSdf = defaults.dynamics.tipSdf;
            dynamics.proceduralMode = defaults.dynamics.proceduralMode;
            dynamics.tipGradient = defaults.dynamics.tipGradient;
            brushTipGuid = string.Empty;
            brushTipLocalId = 0;
            brushTipPresetPath = string.Empty;
        }

        internal void SetBrushTip(Texture2D texture)
        {
            if (texture == ownedPresetTip && texture != null) return;
            ReleasePresetTip();
            dynamics.tip = texture;
            brushTipGuid = string.Empty;
            brushTipLocalId = 0;
            brushTipPresetPath = string.Empty;
            RememberBrushTip();
        }

        internal void ApplyPreset(BrushPresetLibrary.Preset preset, Texture2D tip, string path)
        {
            ReleasePresetTip();
            brushSize = preset.size;
            brushHardness = preset.hardness;
            brushSpacing = preset.spacing;
            dynamics = preset.dynamics;
            dynamics.tip = tip;
            ownedPresetTip = tip;
            brushTipGuid = string.Empty;
            brushTipLocalId = 0;
            brushTipPresetPath = tip != null ? path : string.Empty;
        }

        internal void ReleasePresetTip()
        {
            if (ownedPresetTip == null) return;
            if (dynamics?.tip == ownedPresetTip) dynamics.tip = null;
            UnityEngine.Object.DestroyImmediate(ownedPresetTip);
            ownedPresetTip = null;
        }

        private bool RestorePresetTip()
        {
            try
            {
                BrushPresetLibrary.Load(brushTipPresetPath, out Texture2D tip);
                dynamics.tip = tip;
                ownedPresetTip = tip;
                return tip != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not restore the preset brush tip: " + exception.Message);
                return false;
            }
        }

        internal void RememberBrushTip()
        {
            // A temporarily unavailable asset must not erase its persistent identity.
            if (dynamics?.tip != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(dynamics.tip, out string guid, out long localId) &&
                !string.IsNullOrEmpty(guid))
            {
                brushTipGuid = guid;
                brushTipLocalId = localId;
            }
        }

        internal bool TryRestoreBrushTip()
        {
            if (dynamics == null || dynamics.tip != null ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
            if (!string.IsNullOrEmpty(brushTipPresetPath)) return RestorePresetTip();
            if (string.IsNullOrEmpty(brushTipGuid)) return false;
            string path = AssetDatabase.GUIDToAssetPath(brushTipGuid);
            if (string.IsNullOrEmpty(path)) return false;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null && (brushTipLocalId == 0 || MatchesBrushTip(texture)))
            {
                dynamics.tip = texture;
                return true;
            }
            if (brushTipLocalId != 0)
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Texture2D candidate && MatchesBrushTip(candidate))
                    {
                        dynamics.tip = candidate;
                        return true;
                    }
            return false;
        }

        private bool MatchesBrushTip(Texture2D texture) =>
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId) &&
            guid == brushTipGuid && localId == brushTipLocalId;

        internal void ResetBrushStamps()
        {
            var defaults = new PaintToolSettings();
            dynamics.randomAlgorithm = defaults.dynamics.randomAlgorithm;
            dynamics.scatter = defaults.dynamics.scatter;
            dynamics.scatterBias = defaults.dynamics.scatterBias;
            dynamics.sizeJitter = defaults.dynamics.sizeJitter;
            dynamics.angleJitter = defaults.dynamics.angleJitter;
            dynamics.angleOffset = defaults.dynamics.angleOffset;
            dynamics.rotationMode = defaults.dynamics.rotationMode;
            dynamics.flipX = defaults.dynamics.flipX;
            dynamics.flipY = defaults.dynamics.flipY;
        }

        internal void ResetBrushColor()
        {
            dynamics.ResetTint();
            dynamics.blend = BlendMode.Normal;
            dynamics.blendApplication = BrushBlendApplication.Stroke;
        }

        internal PaintStrokeParameters GetStrokeParameters(bool erase, Color? colorOverride = null)
        {
            return new PaintStrokeParameters(colorOverride ?? brushColor, brushSize, brushHardness, brushSpacing, erase,
                dynamics: dynamics, standardColorInputs: !WhimTexColorInputs.Hdr);
        }

        internal PaintStrokeParameters GetPencilParameters(bool erase, Color? colorOverride = null)
        {
            return new PaintStrokeParameters(colorOverride ?? brushColor, pencilSize, 1f, 0f, erase, true, pencilShape);
        }

        internal void SwapBrushColors()
        {
            Color previous = brushColor;
            brushColor = secondaryBrushColor;
            secondaryBrushColor = previous;
        }
    }
}
