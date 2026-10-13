using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    /// <summary>A bounded numeric field with relative dragging and double-click text editing.</summary>
    public sealed class WhimTexSliderField : BaseField<float>
    {
        private const string ClassName = "whimtex-slider-field";
        private const float DragThreshold = 3f;
        private const float FilledToneCompensation = .5f;
        private const float EmptyToneLift = 1f / 3f;
        private static readonly CustomStyleProperty<Color> ToneBase =
            new CustomStyleProperty<Color>("--whimtex-slider-tone-base");
        private static readonly CustomStyleProperty<float> ToneContrast =
            new CustomStyleProperty<float>("--whimtex-slider-tone-contrast");
        private readonly VisualElement input;
        private readonly VisualElement track;
        private readonly VisualElement fill;
        private readonly VisualElement thumb;
        private readonly Label numberShadow;
        private readonly Label number;
        private readonly NumberField editor;
        private readonly IVisualElementScheduledItem availabilityCheck;
        private IVisualElementScheduledItem pendingTabExit;
        private float minimum;
        private float maximum = 1f;
        private float increment;
        private string numberFormat = "0.##";
        private int pointer = -1;
        private float dragOrigin;
        private float dragValue;
        private KeyCode adjustmentKey;
        private bool interaction;
        private bool editing;
        private bool dragging;
        private Color toneBase;
        private float toneContrast;
        private float displayedValue = float.NaN;
        private string displayedFormat;

        public event Action interactionStarted;
        public event Action interactionEnded;

        public float lowValue
        {
            get => minimum;
            set
            {
                ValidateRange(value, maximum);
                minimum = value;
                SetValueWithoutNotify(this.value);
            }
        }

        public float highValue
        {
            get => maximum;
            set
            {
                ValidateRange(minimum, value);
                maximum = value;
                SetValueWithoutNotify(this.value);
            }
        }

        /// <summary>User adjustments snap to this interval from lowValue. Zero allows continuous dragging.</summary>
        public float step
        {
            get => increment;
            set
            {
                if (!Finite(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
                increment = value;
            }
        }

        public string formatString
        {
            get => numberFormat;
            set
            {
                string next = string.IsNullOrEmpty(value) ? "0.##" : value;
                0f.ToString(next, CultureInfo.InvariantCulture);
                numberFormat = next;
                UpdateVisuals();
            }
        }

        public bool isEditing => editing;
        public bool isDragging => dragging;

        public override float value
        {
            get => base.value;
            set => base.value = Sanitize(value);
        }

        public WhimTexSliderField() : this(null, 0f, 1f) { }

        public WhimTexSliderField(string label, float minimum, float maximum) : base(label, new VisualElement())
        {
            ValidateRange(minimum, maximum);
            this.minimum = minimum;
            this.maximum = maximum;
            AddToClassList(ClassName);
            focusable = true;
            delegatesFocus = false;
            input = this.Q<VisualElement>(className: BaseField<float>.inputUssClassName);
            input.AddToClassList(ClassName + "__input");
            input.focusable = false;

            track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList(ClassName + "__track");
            track.AddToClassList(ClassName + "__display");
            var unfilledOutline = new VisualElement { pickingMode = PickingMode.Ignore };
            unfilledOutline.AddToClassList(ClassName + "__unfilled-outline");
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList(ClassName + "__fill");
            thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            thumb.AddToClassList(ClassName + "__thumb");
            thumb.AddToClassList(ClassName + "__display");
            var thumbInset = new VisualElement { pickingMode = PickingMode.Ignore };
            thumbInset.AddToClassList(ClassName + "__thumb-inset");
            thumb.Add(thumbInset);
            numberShadow = new Label { pickingMode = PickingMode.Ignore };
            numberShadow.AddToClassList(ClassName + "__shadow");
            numberShadow.AddToClassList(ClassName + "__display");
            number = new Label { pickingMode = PickingMode.Ignore };
            number.AddToClassList(ClassName + "__value");
            number.AddToClassList(ClassName + "__display");
            editor = new NumberField { isDelayed = true, tabIndex = -1, formatString = "G9" };
            editor.AddToClassList(ClassName + "__editor");
            track.Add(unfilledOutline);
            track.Add(fill);
            input.Add(track);
            input.Add(thumb);
            input.Add(numberShadow);
            input.Add(number);
            input.Add(editor);

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.dcfapixels.whimtex/src/Editor/WhimTexSliderField.uss");
            if (sheet != null) styleSheets.Add(sheet);
            UpdateTheme();
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStylesResolved);
            editor.RegisterCallback<ChangeEvent<float>>(OnEditorChanged);
            editor.RegisterCallback<ChangeEvent<string>>(e => e.StopPropagation());
            input.RegisterCallback<GeometryChangedEvent>(_ => UpdateTrack());
            input.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            input.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            input.RegisterCallback<PointerUpEvent>(OnPointerUp);
            input.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == pointer) FinishPointer(); });
            RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == pointer) FinishPointer(); }, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            RegisterCallback<FocusInEvent>(_ => AddToClassList(ClassName + "--focused"));
            RegisterCallback<FocusOutEvent>(OnFocusOut, TrickleDown.TrickleDown);
            RegisterCallback<AttachToPanelEvent>(_ => { UpdateTheme(); UpdateVisuals(); });
            RegisterCallback<DetachFromPanelEvent>(_ => FinishUnavailableInteraction());
            availabilityCheck = schedule.Execute(CheckAvailability).Every(16);
            availabilityCheck.Pause();
            SetValueWithoutNotify(minimum);
        }

        public override void SetValueWithoutNotify(float newValue)
        {
            base.SetValueWithoutNotify(Sanitize(newValue));
            // Model refreshes must leave the uncommitted keyboard draft intact.
            if (!editing) editor?.ResetDisplay(base.value);
            UpdateVisuals();
        }

        public void BeginEditing()
        {
            if (editing || !AvailableForInteraction()) return;
            FinishPointer();
            FinishAdjustment();
            if (editing || !AvailableForInteraction()) return;
            pendingTabExit?.Pause();
            pendingTabExit = null;
            editing = true;
            editor.ResetDisplay(value);
            AddToClassList(ClassName + "--editing");
            availabilityCheck.Resume();
            StartInteraction();
            if (!editing || !enabledInHierarchy || panel == null) return;
            editor.schedule.Execute(FocusEditorText);
        }

        private void FocusEditorText()
        {
            if (!editing) return;
            if (!AvailableForInteraction())
            {
                FinishUnavailableInteraction();
                return;
            }
            if (!editor.canGrabFocus)
            {
                editor.schedule.Execute(FocusEditorText).StartingIn(1);
                return;
            }
            editor.Focus();
            editor.SelectAll();
        }

        private void OnEditorChanged(ChangeEvent<float> evt)
        {
            evt.StopPropagation();
            if (editing && enabledInHierarchy) value = UserValue(evt.newValue);
        }

        private void FinishEditing(bool commit, bool focusSelf)
        {
            pendingTabExit?.Pause();
            pendingTabExit = null;
            if (!editing) return;
            float next = commit ? UserValue(editor.ReadInput()) : value;
            editing = false;
            RemoveFromClassList(ClassName + "--editing");
            if (commit) value = next;
            editor.ResetDisplay(value);
            EndInteraction();
            if (focusSelf && panel != null && enabledInHierarchy) Focus();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (editing || evt.button != 0 || !evt.isPrimary || !enabledInHierarchy) return;
            FinishAdjustment();
            FinishPointer();
            if (editing || !enabledInHierarchy || panel == null) return;
            Focus();
            if (!enabledInHierarchy || panel == null) return;
            if (evt.clickCount >= 2) BeginEditing();
            else
            {
                pointer = evt.pointerId;
                dragOrigin = evt.position.x;
                dragValue = value;
                input.CapturePointer(pointer);
                availabilityCheck.Resume();
            }
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointer) return;
            if (!enabledInHierarchy) { FinishPointer(); return; }
            if ((evt.pressedButtons & 1) == 0)
            {
                FinishPointer();
                evt.StopPropagation();
                return;
            }
            UpdateDrag(evt.position.x);
            evt.StopPropagation();
        }

        private void UpdateDrag(float position)
        {
            float delta = position - dragOrigin;
            if (!dragging && Mathf.Abs(delta) < DragThreshold) return;
            dragging = true;
            AddToClassList(ClassName + "--dragging");
            float travel = ThumbTravel();
            if (!Finite(travel) || travel <= 0f) return;
            float next = UserValue(dragValue + (double)delta * ((double)maximum - minimum) / travel);
            if (next == value) return;
            StartInteraction();
            if (pointer != -1 && enabledInHierarchy) value = next;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pointer || evt.button != 0) return;
            if (dragging && enabledInHierarchy) UpdateDrag(evt.position.x);
            FinishPointer();
            evt.StopPropagation();
        }

        private void FinishPointer()
        {
            if (pointer == -1) return;
            int released = pointer;
            pointer = -1;
            dragging = false;
            RemoveFromClassList(ClassName + "--dragging");
            if (input.HasPointerCapture(released)) input.ReleasePointer(released);
            EndInteraction();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!enabledInHierarchy || evt.ctrlKey || evt.commandKey) return;
            if (editing)
            {
                if (evt.keyCode == KeyCode.Escape) FinishEditing(false, true);
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) FinishEditing(true, true);
                else if (evt.keyCode == KeyCode.Tab)
                {
                    value = UserValue(editor.ReadInput());
                    editor.ResetDisplay(value);
                    pendingTabExit?.Pause();
                    if (editing) pendingTabExit = schedule.Execute(FinishTabEditing);
                    return;
                }
                else return;
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.F2)
                BeginEditing();
            else if (evt.keyCode == KeyCode.Escape && pointer != -1) FinishPointer();
            else
            {
                if (pointer != -1) return;
                double distance = increment > 0f ? increment : ((double)maximum - minimum) / 100d;
                if (evt.shiftKey) distance *= 10d;
                float next;
                switch (evt.keyCode)
                {
                    case KeyCode.LeftArrow:
                    case KeyCode.DownArrow: next = UserValue(value - distance); break;
                    case KeyCode.RightArrow:
                    case KeyCode.UpArrow: next = UserValue(value + distance); break;
                    case KeyCode.Home: next = minimum; break;
                    case KeyCode.End: next = maximum; break;
                    default: return;
                }
                if (next != value)
                {
                    adjustmentKey = evt.keyCode;
                    availabilityCheck.Resume();
                    StartInteraction();
                    if (adjustmentKey != KeyCode.None && enabledInHierarchy) value = next;
                }
            }
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode != adjustmentKey || adjustmentKey == KeyCode.None) return;
            FinishAdjustment();
            evt.StopPropagation();
        }

        private void FinishTabEditing()
        {
            FinishEditing(enabledInHierarchy, false);
        }

        private void FinishAdjustment()
        {
            if (adjustmentKey == KeyCode.None) return;
            adjustmentKey = KeyCode.None;
            EndInteraction();
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            var next = evt.relatedTarget as VisualElement;
            if (next == this || (next != null && Contains(next))) return;
            if (editing && next != editor && (next == null || !editor.Contains(next)))
                FinishEditing(enabledInHierarchy, false);
            RemoveFromClassList(ClassName + "--focused");
            FinishPointer();
            FinishAdjustment();
        }

        private void StartInteraction()
        {
            if (interaction) return;
            interaction = true;
            interactionStarted?.Invoke();
        }

        private void EndInteraction()
        {
            if (pointer == -1 && !editing && adjustmentKey == KeyCode.None) availabilityCheck?.Pause();
            if (!interaction) return;
            interaction = false;
            interactionEnded?.Invoke();
        }

        private void CheckAvailability()
        {
            if (!AvailableForInteraction()) FinishUnavailableInteraction();
            else if (pointer != -1 && !input.HasPointerCapture(pointer)) FinishPointer();
        }

        private bool AvailableForInteraction()
        {
            if (panel == null || !enabledInHierarchy) return false;
            for (var current = input; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
            return true;
        }

        private void FinishUnavailableInteraction()
        {
            FinishPointer();
            FinishEditing(false, false);
            FinishAdjustment();
            availabilityCheck?.Pause();
            RemoveFromClassList(ClassName + "--focused");
        }

        private float Sanitize(float candidate)
        {
            return Mathf.Clamp(Finite(candidate) ? candidate : base.value, minimum, maximum);
        }

        private float UserValue(double candidate)
        {
            if (double.IsNaN(candidate) || double.IsInfinity(candidate)) return value;
            candidate = Math.Max(minimum, Math.Min(maximum, candidate));
            if (increment > 0f && candidate > minimum && candidate < maximum)
                candidate = minimum + Math.Round((candidate - minimum) / increment, MidpointRounding.AwayFromZero) * increment;
            return (float)Math.Max(minimum, Math.Min(maximum, candidate));
        }

        private float ThumbWidth()
        {
            float width = thumb.resolvedStyle.width;
            return Finite(width) && width > 0f ? width : 3f;
        }

        private float ThumbTravel() => Mathf.Max(0f, input.contentRect.width - ThumbWidth());

        private void UpdateVisuals()
        {
            if (number == null) return;
            if (displayedValue != value || displayedFormat != numberFormat)
            {
                displayedValue = value;
                displayedFormat = numberFormat;
                number.text = value.ToString(numberFormat, CultureInfo.InvariantCulture);
                numberShadow.text = number.text;
            }
            UpdateTrack();
        }

        private void UpdateTrack()
        {
            double span = (double)maximum - minimum;
            float fraction = span > 0d ? (float)((value - (double)minimum) / span) : 0f;
            track.style.backgroundColor = OffsetTone(((1f - fraction) * EmptyToneLift - fraction) * toneContrast);
            fill.style.backgroundColor = OffsetTone((1f - fraction * FilledToneCompensation) * toneContrast);
            fill.EnableInClassList(ClassName + "__fill--empty", fraction <= 0f);
            float width = input.contentRect.width;
            if (!Finite(width) || width < 0f) return;
            float thumbWidth = ThumbWidth();
            float offset = ThumbTravel() * fraction;
            thumb.style.left = offset;
            fill.style.width = fraction > 0f ? Mathf.Clamp(offset + thumbWidth, 0f, width) : 0f;
        }

        private void UpdateTheme()
        {
            EnableInClassList(ClassName + "--dark", EditorGUIUtility.isProSkin);
            EnableInClassList(ClassName + "--light", !EditorGUIUtility.isProSkin);
            ReadToneColors(customStyle);
        }

        private void OnCustomStylesResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.target != this) return;
            ReadToneColors(evt.customStyle);
            UpdateTrack();
        }

        private void ReadToneColors(ICustomStyle styles)
        {
            bool light = ClassListContains(ClassName + "--light");
            float basis = (light ? 240f : 34f) / 255f;
            toneBase = styles.TryGetValue(ToneBase, out var baseColor)
                ? baseColor : new Color(basis, basis, basis);
            toneContrast = styles.TryGetValue(ToneContrast, out var contrast) && Finite(contrast) && contrast > 0f
                ? contrast : (light ? 14f : 22f) / 255f;
        }

        private Color OffsetTone(float offset) => new Color(Mathf.Clamp01(toneBase.r + offset),
            Mathf.Clamp01(toneBase.g + offset), Mathf.Clamp01(toneBase.b + offset), 1f);

        private static bool Finite(float candidate) => !float.IsNaN(candidate) && !float.IsInfinity(candidate);

        private static void ValidateRange(float minimum, float maximum)
        {
            if (!Finite(minimum)) throw new ArgumentOutOfRangeException(nameof(minimum));
            if (!Finite(maximum) || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        private sealed class NumberField : FloatField
        {
            internal float ReadInput() => StringToValue(text);

            internal void ResetDisplay(float next)
            {
                SetValueWithoutNotify(next);
                text = ValueToString(next);
            }
        }
    }
}
