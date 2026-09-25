using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private PreviewTool lastBasePreviewTool;
        [NonSerialized] private PreviewTool temporaryReturnTool;
        [NonSerialized] private TextureCompositor toolContextDocument, temporaryDocument;
        [NonSerialized] private string toolContextLayerId, temporaryLayerId;
        private VisualElement contextToolSeparator;
        private Button gradientToolButton, uvIslandToolButton, temporaryToolButton;

        private static bool IsBasePreviewTool(PreviewTool tool) => tool <= PreviewTool.Shape || tool == PreviewTool.HealingBrush;
        private static bool IsTemporaryPreviewTool(PreviewTool tool) =>
            tool == PreviewTool.FXTransform || tool == PreviewTool.FXPoint || tool == PreviewTool.FXNormal;
        private bool IsUvToolAvailable => compositor != null && uvEnabled;
        private bool IsContextToolAvailable(PreviewTool tool) =>
            tool == PreviewTool.GradientHandles ? IsGradientCanvasAvailable :
            tool == PreviewTool.UvIslandSelect && IsUvToolAvailable;

        private void BuildContextToolButtons(VisualElement toolbar)
        {
            contextToolSeparator = new VisualElement { pickingMode = PickingMode.Ignore };
            contextToolSeparator.AddToClassList("whimtex-context-tool-separator");
            toolbar.Add(contextToolSeparator);
            gradientToolButton = CreatePreviewToolButton("gradientHandlesTool", PreviewTool.GradientHandles,
                "Gradient Handles. Edit the active layer's gradient on the canvas. Click again or press Escape to return to the basic tool.");
            uvIslandToolButton = CreatePreviewToolButton("uvIslandSelectTool", PreviewTool.UvIslandSelect,
                "UV Island Select. Select islands from the UV reference mesh. Shift adds; Alt subtracts. Escape returns to the basic tool.");
            temporaryToolButton = new Button(ExitContextTool) { name = "temporaryCanvasTool" };
            temporaryToolButton.AddToClassList("whimtex-tool-button");
            temporaryToolButton.Add(new PreviewToolIcon(PreviewTool.FXTransform));
            toolbar.Add(gradientToolButton);
            toolbar.Add(uvIslandToolButton);
            toolbar.Add(temporaryToolButton);
            RefreshContextToolButtons();
        }

        private string TemporaryToolDescription
        {
            get
            {
                var parameter = previewTool == PreviewTool.FXTransform ? PreviewFXParameter :
                    previewTool == PreviewTool.FXPoint ? PointParameter : NormalParameter;
                string kind = previewTool == PreviewTool.FXTransform ? "FX Transform" :
                    previewTool == PreviewTool.FXPoint ? "FX Point" : "FX Normal";
                return kind + (parameter != null ? " · " + parameter.name : string.Empty);
            }
        }

        private void RefreshContextToolButtons()
        {
            bool gradient = IsGradientCanvasAvailable, uv = IsUvToolAvailable, temporary = IsTemporaryPreviewTool(previewTool);
            contextToolSeparator?.EnableInClassList("whimtex-context-tool--hidden", !gradient && !uv && !temporary);
            RefreshContextButton(gradientToolButton, gradient, previewTool == PreviewTool.GradientHandles);
            RefreshContextButton(uvIslandToolButton, uv, IsUvSelectionTool);
            RefreshContextButton(temporaryToolButton, temporary, temporary);
            if (temporaryToolButton != null && temporary)
                temporaryToolButton.tooltip = TemporaryToolDescription + ". Edit on canvas; click again or press Escape to return to the previous tool.";
        }

        private static void RefreshContextButton(Button button, bool visible, bool selected)
        {
            button?.EnableInClassList("whimtex-context-tool--hidden", !visible);
            button?.EnableInClassList("whimtex-tool-button--selected", selected);
        }

        private bool ReconcilePreviewToolContext()
        {
            string layerId = GetSelectedLayer()?.Id;
            bool changedContext = toolContextDocument != compositor || toolContextLayerId != layerId;
            toolContextDocument = compositor;
            toolContextLayerId = layerId;
            PreviewTool before = previewTool;
            if (IsTemporaryPreviewTool(previewTool) && !IsTemporaryTargetValid())
                ChangePreviewTool(ResolveTemporaryReturnTool());
            if (!IsBasePreviewTool(previewTool) && !IsTemporaryPreviewTool(previewTool) && !IsContextToolAvailable(previewTool))
                ChangePreviewTool(lastBasePreviewTool);
            if (changedContext)
            {
                // A changed active layer ends gestures even when the tool kind stays the same.
                FinishPreviewTransform();
                if (IsGradientCanvasAvailable && !IsTemporaryPreviewTool(previewTool))
                    ChangePreviewTool(PreviewTool.GradientHandles);
            }
            return changedContext || before != previewTool;
        }

        private bool IsTemporaryTargetValid()
        {
            if (temporaryDocument != compositor || temporaryLayerId != GetSelectedLayer()?.Id) return false;
            return previewTool == PreviewTool.FXTransform ? PreviewFXParameter != null :
                previewTool == PreviewTool.FXPoint ? PointParameter != null :
                previewTool == PreviewTool.FXNormal && NormalParameter != null;
        }

        private PreviewTool ResolveTemporaryReturnTool()
        {
            if (IsBasePreviewTool(temporaryReturnTool)) return temporaryReturnTool;
            bool sameContext = temporaryDocument == compositor &&
                (temporaryReturnTool == PreviewTool.UvIslandSelect || temporaryLayerId == GetSelectedLayer()?.Id);
            return sameContext && IsContextToolAvailable(temporaryReturnTool) ? temporaryReturnTool : lastBasePreviewTool;
        }

        private void ExitContextTool()
        {
            if (IsBasePreviewTool(previewTool)) return;
            ChangePreviewTool(IsTemporaryPreviewTool(previewTool) ? ResolveTemporaryReturnTool() : lastBasePreviewTool);
            RefreshToolkitInterface();
            toolkitPreviewCanvas?.Focus();
        }

        private bool HandleContextToolEscape(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || evt.ctrlKey || evt.commandKey || evt.altKey || IsBasePreviewTool(previewTool)) return false;
            if (previewTransformManipulator?.IsDragging == true || pointManipulator?.IsDragging == true || normalManipulator?.IsDragging == true)
                FinishPreviewTransform(true);
            else ExitContextTool();
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private void ActivateTemporaryTool(PreviewTool tool, ShaderFX effect, string id)
        {
            ReconcilePreviewToolContext();
            bool same = previewTool == tool && (tool == PreviewTool.FXTransform
                ? previewTransformFX == effect && previewTransformParameterId == id
                : tool == PreviewTool.FXPoint ? pointFX == effect && pointParameterId == id
                : normalFX == effect && normalParameterId == id);
            if (same) { ExitContextTool(); return; }
            PreviewTool returnTool = IsTemporaryPreviewTool(previewTool) ? temporaryReturnTool : previewTool;
            ChangePreviewTool(tool);
            temporaryReturnTool = returnTool;
            temporaryDocument = compositor;
            temporaryLayerId = GetSelectedLayer()?.Id;
            if (tool == PreviewTool.FXTransform) { previewTransformFX = effect; previewTransformParameterId = id; }
            else if (tool == PreviewTool.FXPoint) { pointFX = effect; pointParameterId = id; }
            else { normalFX = effect; normalParameterId = id; }
            RefreshToolkitInterface();
            Focus();
            toolkitPreviewCanvas?.Focus();
        }

        private void ChangePreviewTool(PreviewTool tool)
        {
            StopKeyboardNudge();
            areaSelectionManipulator?.Cancel();
            shapeManipulator?.Cancel();
            CancelPreviewEyedropper();
            CancelPreviewZoomGesture();
            FinishPreviewTransform();
            FinishPaintingStroke();
            normalFX = null;
            normalParameterId = null;
            pointFX = null;
            pointParameterId = null;
            previewTransformFX = null;
            previewTransformParameterId = null;
            temporaryDocument = null;
            temporaryLayerId = null;
            bool changePixelPreview = (previewTool == PreviewTool.Pencil) != (tool == PreviewTool.Pencil);
            if (IsBasePreviewTool(tool))
            {
                if (tool == PreviewTool.Transform && lastBasePreviewTool != PreviewTool.Transform)
                {
                    previewTransformReturnTool = lastBasePreviewTool;
                    EditorPrefs.SetString(PreviewTransformReturnToolPrefKey, previewTransformReturnTool.ToString());
                }
                lastBasePreviewTool = tool;
                EditorPrefs.SetString(PreviewToolPrefKey, tool.ToString());
            }
            previewTool = tool;
            selectedPreviewGuide = -1;
            lineAnchorLayer = null;
            pointOverlay?.MarkDirtyRepaint();
            normalOverlay?.MarkDirtyRepaint();
            RevealActivePreviewTool();
            if (changePixelPreview) RequestPreview(immediate: true);
        }
    }
}
