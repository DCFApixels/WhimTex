using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexLayerNameField : TextField
    {
        private const string EditingClass = "whimtex-layer-name--editing";
        private readonly Label display;
        private readonly VisualElement editorInput;
        private readonly Action returnFocus;
        private readonly IVisualElementScheduledItem pendingFocus;
        private readonly IVisualElementScheduledItem availabilityCheck;
        private IVisualElementScheduledItem pendingTab;

        internal bool isEditing { get; private set; }

        internal WhimTexLayerNameField(Action returnFocus)
        {
            this.returnFocus = returnFocus;
            isDelayed = true;
            focusable = false;
            pickingMode = PickingMode.Position;
            AddToClassList("whimtex-layer-name");
            editorInput = this.Q<VisualElement>(className: BaseField<string>.inputUssClassName);
            editorInput.AddToClassList("whimtex-layer-name__editor");
            display = new Label { pickingMode = PickingMode.Ignore };
            display.AddToClassList("whimtex-layer-name__display");
            hierarchy.Add(display);
            pendingFocus = schedule.Execute(FocusEditor);
            pendingFocus.Pause();
            availabilityCheck = schedule.Execute(CheckAvailability).Every(16);
            availabilityCheck.Pause();
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<FocusOutEvent>(OnFocusOut, TrickleDown.TrickleDown);
            RegisterCallback<DetachFromPanelEvent>(_ => FinishEditing(false, false));
        }

        public override void SetValueWithoutNotify(string newValue)
        {
            string draft = isEditing ? text : null;
            base.SetValueWithoutNotify(newValue ?? string.Empty);
            if (display != null) display.text = value;
            if (isEditing) text = draft;
        }

        internal void BeginEditing()
        {
            if (isEditing || isReadOnly || !Available()) return;
            pendingTab?.Pause();
            text = value;
            isEditing = true;
            focusable = true;
            AddToClassList(EditingClass);
            availabilityCheck.Resume();
            pendingFocus.ExecuteLater(0);
        }

        private void FocusEditor()
        {
            if (!isEditing) return;
            if (!Available()) { FinishEditing(false, false); return; }
            if (editorInput.resolvedStyle.display == DisplayStyle.None)
            {
                pendingFocus.ExecuteLater(1);
                return;
            }
            Focus();
            SelectAll();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!isEditing) return;
            if (evt.keyCode == KeyCode.Tab)
            {
                value = text;
                pendingTab ??= schedule.Execute(() => FinishEditing(true, false));
                pendingTab.ExecuteLater(0);
                return;
            }
            if (evt.keyCode != KeyCode.Escape && evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;
            FinishEditing(evt.keyCode != KeyCode.Escape, true);
            evt.StopPropagation();
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            var next = evt.relatedTarget as VisualElement;
            if (next == this || (next != null && Contains(next))) return;
            FinishEditing(Available(), false);
        }

        private void FinishEditing(bool commit, bool focusOwner)
        {
            if (!isEditing) return;
            string next = commit ? text : value;
            isEditing = false;
            pendingFocus.Pause();
            pendingTab?.Pause();
            availabilityCheck.Pause();
            if (commit) value = next;
            else SetValueWithoutNotify(next);
            RemoveFromClassList(EditingClass);
            focusable = false;
            if (focusOwner && panel != null) returnFocus?.Invoke();
        }

        private void CheckAvailability()
        {
            if (!Available() || isReadOnly) FinishEditing(false, false);
        }

        private bool Available()
        {
            if (panel == null || !enabledInHierarchy) return false;
            for (var current = (VisualElement)this; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
            return true;
        }
    }
}
