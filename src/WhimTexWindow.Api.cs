using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private string agentSessionId;
        [NonSerialized] private WhimTexDocument agentSessionDocument;
        [SerializeField, HideInInspector] private long agentFocusOrder;
        private static long agentFocusSequence;

        internal long AgentFocusOrder => agentFocusOrder;

        private void RecordAgentFocus()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                agentFocusSequence = Math.Max(agentFocusSequence, window.agentFocusOrder);
            agentFocusOrder = ++agentFocusSequence;
        }

        internal string AgentSessionId
        {
            get
            {
                if (agentSessionId == null || agentSessionDocument != activeDocument)
                {
                    agentSessionId = Guid.NewGuid().ToString("N");
                    agentSessionDocument = activeDocument;
                }
                return agentSessionId;
            }
        }
        internal WhimTexDocument AgentDocument => activeDocument;
        internal Layer AgentSelectedLayer => GetSelectedLayer();
        internal string[] AgentSelectedIds => selectedLayerIds.ToArray();
        internal CanvasSelection AgentSelection => GetAreaSelection();

        internal void RefreshAgentLocks()
        {
            ResetToolkitLayerInspector();
            RefreshToolkitInterface(forceValues: true);
            Repaint();
        }

        internal static bool IsDocumentBusyForLiveApi(WhimTexDocument document)
        {
            if (IsDocumentBusyForApi(document)) return true;
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
            {
                if (window.activeDocument != document) continue;
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

        internal static bool IsDocumentBusyForApi(WhimTexDocument document)
        {
            foreach (WhimTexWindow window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                if (window.activeDocument == document && (window.paintingLayer != null || window.healingLayer != null ||
                    window.shapeManipulator != null && window.shapeManipulator.IsDragging ||
                    window.gradientCanvasManipulator?.IsDragging == true ||
                    window.pointManipulator?.IsDragging == true ||
                    window.normalManipulator?.IsDragging == true ||
                    window.canvasTransformManipulator != null && window.canvasTransformManipulator.IsDragging))
                    return true;
            return false;
        }
    }
}
