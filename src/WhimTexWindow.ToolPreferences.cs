using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private WhimTexUserSettings.ToolPreferenceSession toolPreferences;

        private static T ToolEnum<T>(T value) where T : struct, Enum => Enum.IsDefined(typeof(T), value) ? value : default;

        // One registration point for every tool's persistent defaults. Layer properties,
        // pointer state, drafts, crop frames, selected IDs and viewport poses stay local.
        private void InitializeToolPreferences(WhimTexUserSettings.IToolPreferenceStore store = null)
        {
            toolPreferences?.Dispose();
            toolPreferences = new WhimTexUserSettings.ToolPreferenceSession(store);
            var preferences = toolPreferences;
            preferences.Register("paint", () => paintSettings, ReplacePaintPreferences, () => new PaintToolSettings(),
                encode: value => value.EncodePreferences(), decode: PaintToolSettings.DecodePreferences);
            preferences.Register("activeTool", () => lastBaseCanvasTool, value => canvasTool = lastBaseCanvasTool = value,
                () => CanvasTool.None, encode: value => value.ToString(), decode: ParseCanvasTool, refresh: false);
            preferences.Register("returnTool", () => canvasTransformReturnTool, value => canvasTransformReturnTool = value,
                () => CanvasTool.None, value => value == CanvasTool.Transform ? CanvasTool.None : value,
                value => value.ToString(), ParseCanvasTool, refresh: false);
            preferences.Register("shape", () => shapeToolSettings, value => shapeToolSettings = value,
                () => new ShapeLayerBehaviour(), value =>
                {
                    value ??= new ShapeLayerBehaviour(); value.kind = ToolEnum(value.kind);
                    value.strokeWidth = ShapeLayerBehaviour.Limit(value.strokeWidth, 0, 8192, 2);
                    value.arcThickness = ShapeLayerBehaviour.Limit(value.arcThickness, 0, 8192, 8);
                    value.EnsureCorners(); return value;
                }, encode: value =>
                {
                    var json = JObject.Parse(JsonUtility.ToJson(value)); json.Remove("recoveryId");
                    return new JObject { ["value"] = json }.ToString(Formatting.None);
                });
            preferences.Register("text", () => textToolSettings, value => textToolSettings = value,
                () => new TextToolSettings(), value =>
                {
                    value ??= new TextToolSettings(); value.SetFontSize(value.fontSize);
                    value.fontStyle = ToolEnum(value.fontStyle); value.casing = ToolEnum(value.casing);
                    value.edgeMode = ToolEnum(value.edgeMode); value.alignment = ToolEnum(value.alignment); return value;
                });
            preferences.Register("gradient", () => gradientCreationSettings, value => gradientCreationSettings = value,
                () => new GradientCreationSettings(), value =>
                {
                    value ??= new GradientCreationSettings(); value.type = ToolEnum(value.type);
                    value.gradient ??= GradientUtility.Create(GradientUtility.WhiteToBlack); return value;
                });
            preferences.Register("selectionMode", () => areaSelectionMode, value => areaSelectionMode = value, () => default, ToolEnum);
            preferences.Register("quickSelectionSize", () => quickSelectionSize, value => quickSelectionSize = value, () => 64f,
                value => ShapeLayerBehaviour.Limit(value, 1, 2048, 64));
            preferences.Register("quickSelectionTolerance", () => quickSelectionTolerance, value => quickSelectionTolerance = value, () => .18f,
                value => ShapeLayerBehaviour.Limit(value, .01f, .6f, .18f));
            preferences.Register("quickSelectionMode", () => quickSelectionMode, value => quickSelectionMode = value, () => QuickSelectionMode.Add, ToolEnum);
            preferences.Register("quickSelectionStrength", () => quickSelectionStrength, value => quickSelectionStrength = value, () => 1f,
                value => ShapeLayerBehaviour.Limit(value, 0, 1, 1));
            preferences.Register("quickSelectionSource", () => quickSelectionSource, value => quickSelectionSource = value, () => QuickSelectionSource.VisibleLayers, ToolEnum);
            preferences.Register("selectionEdges", () => selectionEdgeMode, value => selectionEdgeMode = value, () => SelectionEdgeMode.Aliased, ToolEnum);
            preferences.Register("marquee", () => marqueeShape, value => marqueeShape = value, () => MarqueeShape.Rectangle, ToolEnum);
            preferences.Register("lasso", () => lassoMode, value => lassoMode = value, () => LassoMode.Polygonal, ToolEnum);
            preferences.Register("lassoCurve", () => lassoCurveMode, value => lassoCurveMode = value, () => LassoCurveMode.Straight, ToolEnum);
            preferences.Register("vectorColor", () => vectorFillColor, value => vectorFillColor = value, () => Color.white);
            preferences.Register("vectorSize", () => vectorBrushSize, value => vectorBrushSize = value, () => 24f,
                value => ShapeLayerBehaviour.Limit(value, 1, 2048, 24));
            preferences.Register("vectorShape", () => vectorBrushShape, value => vectorBrushShape = value, () => VectorBrushShape.Circle, ToolEnum);
            preferences.Register("vectorTolerance", () => vectorBrushTolerance, value => vectorBrushTolerance = value, () => .75f,
                value => ShapeLayerBehaviour.Limit(value, VectorBrushDraft.MinTolerance, VectorBrushDraft.MaxTolerance, .75f));
            preferences.Register("cropMode", () => canvasCropMode, value => canvasCropMode = value, () => CanvasCropMode.Resize,
                value => Enum.IsDefined(typeof(CanvasCropMode), value) ? value : CanvasCropMode.Resize);
            preferences.Register("cropAutoPixelScale", () => canvasCropAutoPixelScale, value => canvasCropAutoPixelScale = value, () => true);
            preferences.Register("brushPreset", () => selectedBrushPreset, value => selectedBrushPreset = value, () => (string)null);
            preferences.Register("brushPresetSnapshot", () => selectedBrushPresetSnapshot, value => selectedBrushPresetSnapshot = value, () => (string)null);
            preferences.Register("brushPreviewScale", () => brushStrokePreviewScale, value => brushStrokePreviewScale = value, () => 1f,
                ClampBrushPreviewScale);
            preferences.Register("guideHidden", () => canvasGuidesHidden, value => canvasGuidesHidden = value, () => false);
            preferences.Register("guideLocked", () => canvasGuidesLocked, value => canvasGuidesLocked = value, () => false);
            preferences.Register("guideSnap", () => canvasGuidesSnap, value => canvasGuidesSnap = value, () => true);
            preferences.Register("commentFilter", () => commentFilter, value => commentFilter = value, () => CommentFilter.Open, ToolEnum);
            preferences.Register("uvColor", () => uvLineColor, value => uvLineColor = value, () => new Color(.35f, .85f, 1f, 1));
            preferences.Register("uvOpacity", () => uvLineOpacity, value => uvLineOpacity = value, () => .65f,
                value => ShapeLayerBehaviour.Limit(value, 0, 1, .65f));
            preferences.Restored = RefreshToolPreferenceControls;
            gradientHandlesSelectedManually = false; previousCanvasTool = null; canvasToolToggleKeyHeld = false;
        }

        private void ReplacePaintPreferences(PaintToolSettings value)
        {
            paintSettings?.ReleasePresetTip(); paintSettings = value;
            paintSettings.dynamics ??= new BrushDynamics(); paintSettings.dynamics.Normalize();
            paintSettings.smoothing ??= new StrokeSmoothingSettings(); paintSettings.smoothing.Normalize();
            brushStrokePreviewDirty = true;
        }

        private void SaveToolPreferences() => toolPreferences?.ScheduleSave();

        private void RefreshToolPreferenceControls()
        {
            RestoreBrushTipAfterReload();
            shapeToolIcon?.SetKind(shapeToolSettings.kind);
            gradientCreationToolIcon?.SetGradientType(gradientCreationSettings.type);
            RefreshCanvasToolToolbar(); toolkitHeaderBindings.Refresh(); brushSettingsBindings?.Refresh();
            RefreshBrushPresetButton(); RefreshCanvasGuidesButton(); RefreshCanvasGuides();
            ApplyCommentFilter();
            uvOverlay?.MarkDirtyRepaint(); RefreshCanvasPointerCursor();
        }

        // Native controls may stop bubbling their ChangeEvent; observe before dispatch,
        // then capture after all control callbacks have committed their actual values.
        private void TrackToolPreferenceChanges(VisualElement root)
        {
            void Track<T>() => root.RegisterCallback<ChangeEvent<T>>(_ => SaveToolPreferences(), TrickleDown.TrickleDown);
            Track<bool>(); Track<int>(); Track<float>(); Track<string>(); Track<Enum>(); Track<Color>();
            Track<Vector2>(); Track<Vector3>(); Track<Vector4>(); Track<WhimTexGradient>();
        }
    }
}
