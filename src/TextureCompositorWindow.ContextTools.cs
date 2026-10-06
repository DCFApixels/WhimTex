using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private CanvasTool lastBaseCanvasTool;
        [NonSerialized] private CanvasTool temporaryReturnTool;
        [NonSerialized] private TextureCompositor toolContextDocument, temporaryDocument;
        [NonSerialized] private string toolContextLayerId, temporaryLayerId;
        private VisualElement contextToolSeparator;
        private Button gradientToolButton, uvIslandToolButton, temporaryToolButton;

        private static bool IsBaseCanvasTool(CanvasTool tool) => tool <= CanvasTool.Shape || tool == CanvasTool.HealingBrush || tool == CanvasTool.SmudgeBrush;
        private static bool IsTemporaryCanvasTool(CanvasTool tool) =>
            tool == CanvasTool.FXTransform || tool == CanvasTool.FXPoint || tool == CanvasTool.FXNormal;
        private bool IsUvToolAvailable => compositor != null && uvEnabled;
        private bool IsContextToolAvailable(CanvasTool tool) =>
            tool == CanvasTool.GradientHandles ? IsGradientCanvasAvailable :
            tool == CanvasTool.UvIslandSelect && IsUvToolAvailable;

        private void BuildContextToolButtons(VisualElement toolbar)
        {
            contextToolSeparator = new VisualElement { pickingMode = PickingMode.Ignore };
            contextToolSeparator.AddToClassList("whimtex-context-tool-separator");
            toolbar.Add(contextToolSeparator);
            gradientToolButton = CreateCanvasToolButton("gradientHandlesTool", CanvasTool.GradientHandles,
                "Gradient Handles. Edit the active layer's gradient on the canvas. Click again or press Escape to return to the basic tool.");
            uvIslandToolButton = CreateCanvasToolButton("uvIslandSelectTool", CanvasTool.UvIslandSelect,
                "UV Island Select. Select islands from the UV reference mesh. Shift adds; Alt subtracts. Escape returns to the basic tool.");
            temporaryToolButton = new Button(ExitContextTool) { name = "temporaryCanvasTool" };
            temporaryToolButton.AddToClassList("whimtex-tool-button");
            temporaryToolButton.Add(new CanvasToolIcon(CanvasTool.FXTransform));
            toolbar.Add(gradientToolButton);
            toolbar.Add(uvIslandToolButton);
            toolbar.Add(temporaryToolButton);
            RefreshContextToolButtons();
        }

        private string TemporaryToolDescription
        {
            get
            {
                var parameter = canvasTool == CanvasTool.FXTransform ? CanvasFXParameter :
                    canvasTool == CanvasTool.FXPoint ? PointParameter : NormalParameter;
                string kind = canvasTool == CanvasTool.FXTransform ? "FX Transform" :
                    canvasTool == CanvasTool.FXPoint ? "FX Point" : "FX Normal";
                return kind + (parameter != null ? " · " + parameter.name : string.Empty);
            }
        }

        private void RefreshContextToolButtons()
        {
            bool gradient = IsGradientCanvasAvailable, uv = IsUvToolAvailable, temporary = IsTemporaryCanvasTool(canvasTool);
            contextToolSeparator?.EnableInClassList("whimtex-context-tool--hidden", !gradient && !uv && !temporary);
            RefreshContextButton(gradientToolButton, gradient, canvasTool == CanvasTool.GradientHandles);
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

        private bool ReconcileCanvasToolContext()
        {
            string layerId = GetSelectedLayer()?.Id;
            bool changedContext = toolContextDocument != compositor || toolContextLayerId != layerId;
            toolContextDocument = compositor;
            toolContextLayerId = layerId;
            CanvasTool before = canvasTool;
            if (IsTemporaryCanvasTool(canvasTool) && !IsTemporaryTargetValid())
                ChangeCanvasTool(ResolveTemporaryReturnTool());
            if (!IsBaseCanvasTool(canvasTool) && !IsTemporaryCanvasTool(canvasTool) && !IsContextToolAvailable(canvasTool))
                ChangeCanvasTool(lastBaseCanvasTool);
            if (changedContext)
            {
                // A changed active layer ends gestures even when the tool kind stays the same.
                FinishCanvasTransform();
                if (IsGradientCanvasAvailable && !IsTemporaryCanvasTool(canvasTool))
                    ChangeCanvasTool(CanvasTool.GradientHandles);
            }
            return changedContext || before != canvasTool;
        }

        private bool IsTemporaryTargetValid()
        {
            if (temporaryDocument != compositor || temporaryLayerId != GetSelectedLayer()?.Id) return false;
            return canvasTool == CanvasTool.FXTransform ? CanvasFXParameter != null :
                canvasTool == CanvasTool.FXPoint ? PointParameter != null :
                canvasTool == CanvasTool.FXNormal && NormalParameter != null;
        }

        private CanvasTool ResolveTemporaryReturnTool()
        {
            if (IsBaseCanvasTool(temporaryReturnTool)) return temporaryReturnTool;
            bool sameContext = temporaryDocument == compositor &&
                (temporaryReturnTool == CanvasTool.UvIslandSelect || temporaryLayerId == GetSelectedLayer()?.Id);
            return sameContext && IsContextToolAvailable(temporaryReturnTool) ? temporaryReturnTool : lastBaseCanvasTool;
        }

        private void ExitContextTool()
        {
            if (IsBaseCanvasTool(canvasTool)) return;
            ChangeCanvasTool(IsTemporaryCanvasTool(canvasTool) ? ResolveTemporaryReturnTool() : lastBaseCanvasTool);
            RefreshToolkitInterface();
            toolkitCanvas?.Focus();
        }

        private bool HandleContextToolEscape(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || evt.ctrlKey || evt.commandKey || evt.altKey || IsBaseCanvasTool(canvasTool)) return false;
            if (canvasTransformManipulator?.IsDragging == true || pointManipulator?.IsDragging == true || normalManipulator?.IsDragging == true)
                FinishCanvasTransform(true);
            else ExitContextTool();
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private void ActivateTemporaryTool(CanvasTool tool, ShaderFX effect, string id)
        {
            ReconcileCanvasToolContext();
            bool same = canvasTool == tool && (tool == CanvasTool.FXTransform
                ? canvasTransformFX == effect && canvasTransformParameterId == id
                : tool == CanvasTool.FXPoint ? pointFX == effect && pointParameterId == id
                : normalFX == effect && normalParameterId == id);
            if (same) { ExitContextTool(); return; }
            CanvasTool returnTool = IsTemporaryCanvasTool(canvasTool) ? temporaryReturnTool : canvasTool;
            ChangeCanvasTool(tool);
            temporaryReturnTool = returnTool;
            temporaryDocument = compositor;
            temporaryLayerId = GetSelectedLayer()?.Id;
            if (tool == CanvasTool.FXTransform) { canvasTransformFX = effect; canvasTransformParameterId = id; }
            else if (tool == CanvasTool.FXPoint) { pointFX = effect; pointParameterId = id; }
            else { normalFX = effect; normalParameterId = id; }
            RefreshToolkitInterface();
            Focus();
            toolkitCanvas?.Focus();
        }

        private void ChangeCanvasTool(CanvasTool tool)
        {
            CanvasTool previous = IsTemporaryCanvasTool(canvasTool) ? ResolveTemporaryReturnTool() : canvasTool;
            if (previous != tool && !IsTemporaryCanvasTool(tool) &&
                (IsBaseCanvasTool(previous) || IsContextToolAvailable(previous)))
                previousCanvasTool = previous;
            StopKeyboardNudge();
            areaSelectionManipulator?.Cancel();
            shapeManipulator?.Cancel();
            CancelCanvasEyedropper();
            CancelCanvasZoomGesture();
            FinishCanvasTransform();
            FinishPaintingStroke();
            normalFX = null;
            normalParameterId = null;
            pointFX = null;
            pointParameterId = null;
            canvasTransformFX = null;
            canvasTransformParameterId = null;
            temporaryDocument = null;
            temporaryLayerId = null;
            bool changePixelCanvas = (canvasTool == CanvasTool.Pencil) != (tool == CanvasTool.Pencil);
            if (IsBaseCanvasTool(tool))
            {
                if (tool == CanvasTool.Transform && lastBaseCanvasTool != CanvasTool.Transform)
                {
                    canvasTransformReturnTool = lastBaseCanvasTool;
                    EditorPrefs.SetString(CanvasTransformReturnToolPrefKey, canvasTransformReturnTool.ToString());
                }
                lastBaseCanvasTool = tool;
                EditorPrefs.SetString(CanvasToolPrefKey, tool.ToString());
            }
            canvasTool = tool;
            selectedCanvasGuide = -1;
            lineAnchorLayer = null;
            pointOverlay?.MarkDirtyRepaint();
            normalOverlay?.MarkDirtyRepaint();
            RevealActiveCanvasTool();
            if (changePixelCanvas) RequestCanvasRender(immediate: true);
        }

        private bool HandlePreviousCanvasToolKey(KeyDownEvent evt)
        {
            if (evt.keyCode != CanvasToolToggleKey || evt.ctrlKey || evt.commandKey || evt.altKey || evt.shiftKey)
                return false;
            WhimTexUI.ConsumeEvent(evt);
            if (canvasToolToggleKeyHeld) return true;
            canvasToolToggleKeyHeld = true;
            if (paintingLayer != null || healingPointer >= 0 || activeLayerDrag != null ||
                canvasTransformManipulator?.IsDragging == true || canvasZoomManipulator?.IsDragging == true ||
                canvasGuideManipulator?.IsDragging == true || gradientCanvasManipulator?.IsDragging == true ||
                pointManipulator?.IsDragging == true || normalManipulator?.IsDragging == true ||
                shapeManipulator?.IsDragging == true || areaSelectionManipulator?.HasGesture == true)
                return true;
            ReconcileCanvasToolContext();
            if (IsTemporaryCanvasTool(canvasTool))
                ExitContextTool();
            else if (previousCanvasTool is CanvasTool previous &&
                (IsBaseCanvasTool(previous) || IsContextToolAvailable(previous)))
                SetCanvasTool(previous);
            return true;
        }
    }
}
