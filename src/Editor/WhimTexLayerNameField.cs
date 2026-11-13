using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexLayerNameField : TextField
    {
        private const string EditingClass = "whimtex-layer-name--editing";
        private readonly EditorWindow owner;
        private readonly VisualElement editorInput;
        private IVisualElementScheduledItem editCheck;
        private bool awaitingFocus;

        internal Label Display { get; }
        internal bool isEditing => ClassListContains(EditingClass);

        internal WhimTexLayerNameField(EditorWindow owner)
        {
            this.owner = owner;
            isDelayed = true;
            focusable = false;
            pickingMode = PickingMode.Position;
            AddToClassList("whimtex-layer-name");
            editorInput = this.Q<VisualElement>(className: BaseField<string>.inputUssClassName);
            editorInput.AddToClassList("whimtex-layer-name__editor");
            Display = new Label { pickingMode = PickingMode.Ignore };
            Display.AddToClassList("whimtex-layer-name__display");
            hierarchy.Add(Display);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<FocusOutEvent>(evt => { if (!Owns(evt.relatedTarget)) FinishEditing(true, releaseFocus: false); }, TrickleDown.TrickleDown);
            RegisterCallback<DetachFromPanelEvent>(_ => FinishEditing(false));
        }

        public override void SetValueWithoutNotify(string newValue)
        {
            string draft = isEditing ? text : null;
            base.SetValueWithoutNotify(newValue ?? string.Empty);
            if (Display != null) Display.text = value;
            if (draft != null) text = draft;
        }

        internal void BeginEditing()
        {
            if (isEditing || !Available()) return;
            text = value;
            awaitingFocus = true;
            focusable = true;
            AddToClassList(EditingClass);
            owner.rootVisualElement.RegisterCallback<PointerDownEvent>(OnOutsidePress, TrickleDown.TrickleDown);
            editCheck ??= schedule.Execute(CheckEditing).Every(16);
            editCheck.Resume();
        }

        private void CheckEditing()
        {
            if (!Available()) { FinishEditing(false); return; }
            if (EditorWindow.focusedWindow != owner || (!awaitingFocus && !Owns(focusController?.focusedElement)))
            { FinishEditing(true); return; }
            if (!awaitingFocus || editorInput.resolvedStyle.display == DisplayStyle.None) return;
            awaitingFocus = false;
            Focus();
            SelectAll();
        }

        private bool Owns(object element) => element is VisualElement view && (view == this || Contains(view));

        private void OnOutsidePress(PointerDownEvent evt)
        {
            if (!Owns(evt.target)) FinishEditing(true);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!isEditing) return;
            if (evt.keyCode == KeyCode.Tab) { FinishEditing(true, releaseFocus: false); return; }
            if (evt.keyCode is not (KeyCode.Escape or KeyCode.Return or KeyCode.KeypadEnter)) return;
            FinishEditing(evt.keyCode != KeyCode.Escape, releaseFocus: false);
            owner.rootVisualElement.Focus();
            evt.StopPropagation();
        }

        private void FinishEditing(bool commit, bool releaseFocus = true)
        {
            if (!isEditing) return;
            RemoveFromClassList(EditingClass);
            focusable = false;
            editCheck.Pause();
            owner.rootVisualElement.UnregisterCallback<PointerDownEvent>(OnOutsidePress, TrickleDown.TrickleDown);
            if (commit && Available()) value = text;
            else SetValueWithoutNotify(value);
            if (releaseFocus && owner.rootVisualElement.focusController?.focusedElement is VisualElement focused && Owns(focused))
                focused.Blur();
        }

        private bool Available()
        {
            if (panel == null || !enabledInHierarchy || isReadOnly) return false;
            for (var current = (VisualElement)this; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
            return true;
        }
    }
}
