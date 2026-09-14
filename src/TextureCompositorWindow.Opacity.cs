using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private const double OpacityDigitInterval = 0.6d;
        [NonSerialized] private int opacityFirstDigit = -1;
        [NonSerialized] private double opacityDigitTime;
        [NonSerialized] private Layer opacityDigitLayer;
        [NonSerialized] private int opacityUndoGroup = -1;
        [NonSerialized] private float opacityDigitValue;

        private void ResetOpacityEntry()
        {
            opacityFirstDigit = -1;
            opacityDigitLayer = null;
            opacityUndoGroup = -1;
        }

        private void OnOpacityPointerDown(PointerDownEvent evt) => ResetOpacityEntry();

        private bool HandleOpacityKey(KeyDownEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey || evt.altKey || evt.shiftKey)
                return false;
            Layer layer = GetSelectedLayer();
            if (layer == null || layer?.Behaviour is PendingLayerBehaviour || WhimTexApi.IsLayerContentLocked(compositor, layer))
                return false;
            if (evt.keyCode == KeyCode.None && evt.character >= '0' && evt.character <= '9')
            {
                WhimTexUI.ConsumeEvent(evt);
                return true;
            }
            int digit;
            int key = (int)evt.keyCode;
            if (key >= (int)KeyCode.Alpha0 && key <= (int)KeyCode.Alpha9)
                digit = key - (int)KeyCode.Alpha0;
            else if (key >= (int)KeyCode.Keypad0 && key <= (int)KeyCode.Keypad9)
                digit = key - (int)KeyCode.Keypad0;
            else
                return false;

            FinishPreviewTransform();
            FinishPaintingStroke();
            double now = EditorApplication.timeSinceStartup;
            bool pair = opacityFirstDigit >= 0 && ReferenceEquals(opacityDigitLayer, layer) &&
                now - opacityDigitTime <= OpacityDigitInterval && Mathf.Approximately(layer.opacity, opacityDigitValue);
            int percent = pair ? opacityFirstDigit * 10 + digit : digit == 0 ? 100 : digit * 10;
            int previousGroup = opacityUndoGroup;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Change Layer Opacity");
            ApplySelectedOpacity(layer, percent / 100f);
            Undo.FlushUndoRecordObjects();
            if (pair && previousGroup >= 0)
                Undo.CollapseUndoOperations(previousGroup);
            Undo.IncrementCurrentGroup();
            ResetOpacityEntry();
            if (!pair)
            {
                opacityFirstDigit = digit;
                opacityDigitTime = now;
                opacityDigitLayer = layer;
                opacityDigitValue = layer.opacity;
                opacityUndoGroup = group;
            }
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }
    }
}
