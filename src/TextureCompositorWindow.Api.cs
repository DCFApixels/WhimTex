using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private string agentSessionId;
        [NonSerialized] private TextureCompositor agentSessionDocument;
        [SerializeField, HideInInspector] private long agentFocusOrder;
        private static long agentFocusSequence;

        internal long AgentFocusOrder => agentFocusOrder;

        private void RecordAgentFocus()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                agentFocusSequence = Math.Max(agentFocusSequence, window.agentFocusOrder);
            agentFocusOrder = ++agentFocusSequence;
        }

        internal string AgentSessionId
        {
            get
            {
                if (agentSessionId == null || agentSessionDocument != compositor)
                {
                    agentSessionId = Guid.NewGuid().ToString("N");
                    agentSessionDocument = compositor;
                }
                return agentSessionId;
            }
        }
        internal TextureCompositor AgentDocument => compositor;
        internal Layer AgentSelectedLayer => GetSelectedLayer();
        internal string[] AgentSelectedIds => selectedLayerIds.ToArray();
        internal CanvasSelection AgentSelection => GetAreaSelection();

        internal void RefreshAgentLocks()
        {
            ResetToolkitLayerInspector();
            RefreshToolkitInterface(forceValues: true);
            Repaint();
        }

        internal static bool IsDocumentBusyForLiveApi(TextureCompositor document)
        {
            if (IsDocumentBusyForApi(document)) return true;
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
            {
                if (window.compositor != document) continue;
                if (window.activeLayerDrag != null || window.GetDraggedRoots() != null ||
                    window.areaSelectionManipulator != null && window.areaSelectionManipulator.HasGesture) return true;
                for (var element = window.rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
                    element != null; element = element.parent)
                {
                    if (element is TextField text && text.isDelayed && text.text != text.value) return true;
                    if (element is FloatField number && number.isDelayed &&
                        (!float.TryParse(number.text, NumberStyles.Float, CultureInfo.CurrentCulture, out float value) || value != number.value)) return true;
                    if (element is IntegerField integer && integer.isDelayed &&
                        (!int.TryParse(integer.text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int valueInt) || valueInt != integer.value)) return true;
                }
            }
            return false;
        }

        internal static bool IsDocumentBusyForApi(TextureCompositor document)
        {
            foreach (TextureCompositorWindow window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (window.compositor == document && (window.paintingLayer != null ||
                    window.shapeManipulator != null && window.shapeManipulator.IsDragging ||
                    window.previewTransformManipulator != null && window.previewTransformManipulator.IsDragging))
                    return true;
            return false;
        }
    }
}
