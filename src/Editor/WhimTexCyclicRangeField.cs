using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexCyclicRangeField : BaseField<Vector2>
    {
        private readonly float minimum, maximum;
        private readonly FloatField startNumber, endNumber;
        private readonly VisualElement rail, firstSelection, secondSelection;
        internal VisualElement Track { get; }
        internal VisualElement StartHandle { get; }
        internal VisualElement EndHandle { get; }

        internal WhimTexCyclicRangeField(string label, float minimum, float maximum) : base(label, new VisualElement())
        {
            this.minimum = minimum; this.maximum = maximum;
            WhimTexUI.ApplyWindowStyles(this);
            AddToClassList("whimtex-minmax-range");
            AddToClassList("whimtex-cyclic-range");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            var slider = new VisualElement { name = "cyclic-range-slider" };
            slider.AddToClassList("whimtex-minmax-range-slider");
            slider.AddToClassList("whimtex-cyclic-range-slider");
            rail = new VisualElement(); rail.AddToClassList("whimtex-cyclic-range-rail");
            Track = new VisualElement { pickingMode = PickingMode.Ignore };
            Track.AddToClassList("whimtex-cyclic-range-track");
            firstSelection = Selection(); secondSelection = Selection();
            StartHandle = Handle("cyclic-range-start", -1, "Range start (X)");
            EndHandle = Handle("cyclic-range-end", 1, "Range end (Y)");
            rail.Add(Track); rail.Add(firstSelection); rail.Add(secondSelection);
            rail.Add(StartHandle); rail.Add(EndHandle); slider.Add(rail);
            startNumber = Number("Range start (X); values outside the limits wrap");
            endNumber = Number("Range end (Y); values outside the limits wrap");
            input.Add(slider); input.Add(startNumber); input.Add(endNumber);
            startNumber.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                if (ProjectiveMatrix.Finite(e.newValue)) value = new Vector2(Repeat(e.newValue), Display(value).y);
                startNumber.SetValueWithoutNotify(Display(value).x);
            });
            endNumber.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                if (ProjectiveMatrix.Finite(e.newValue)) value = new Vector2(Display(value).x, Repeat(e.newValue));
                endNumber.SetValueWithoutNotify(Display(value).y);
            });
            slider.AddManipulator(new RangeDrag(this));
            SetValueWithoutNotify(new Vector2(minimum, maximum));
        }

        private static VisualElement Selection()
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList("whimtex-cyclic-range-selection");
            return element;
        }

        private static VisualElement Handle(string name, int side, string tooltip)
        {
            var element = new VisualElement { name = name, focusable = true, tabIndex = 0, tooltip = tooltip };
            element.AddToClassList("whimtex-cyclic-range-handle");
            element.AddToClassList(side < 0 ? "whimtex-cyclic-range-handle--start" : "whimtex-cyclic-range-handle--end");
            element.Add(new ShaderFXPreviewThumb(side));
            return element;
        }

        private static FloatField Number(string tooltip)
        {
            var field = new FloatField { tooltip = tooltip };
            field.AddToClassList("whimtex-minmax-range-number");
            return field;
        }

        private float Period => maximum - minimum;
        private float Repeat(float v) => ShaderFXParameter.RepeatRange(v, minimum, maximum);
        private Vector2 Display(Vector2 v)
        {
            if (ShaderFXParameter.IsFullCycle(v, minimum, maximum) && (v.x < minimum || v.y > maximum))
            {
                float anchor = v.x < minimum ? v.y : v.x;
                return new Vector2(anchor, anchor);
            }
            return new Vector2(Repeat(v.x), Repeat(v.y));
        }
        private float Width(Vector2 v)
        {
            if (ShaderFXParameter.IsFullCycle(v, minimum, maximum)) return Period;
            float width = v.y - v.x;
            return width < 0 ? width + Period : width;
        }

        public override void SetValueWithoutNotify(Vector2 newValue)
        {
            if (!ShaderFXParameter.IsFullCycle(newValue, minimum, maximum))
                newValue = new Vector2(Repeat(newValue.x), Repeat(newValue.y));
            base.SetValueWithoutNotify(newValue);
            var display = Display(newValue);
            float x = display.x, y = display.y;
            startNumber?.SetValueWithoutNotify(x);
            endNumber?.SetValueWithoutNotify(y);
            if (rail == null) return;
            float start = (x - minimum) / Period, end = (y - minimum) / Period;
            StartHandle.style.left = Length.Percent(start * 100);
            EndHandle.style.left = Length.Percent(end * 100);
            bool full = Width(newValue) >= Period;
            Segment(firstSelection, full ? 0 : start, full || start > end ? 1 : end);
            Segment(secondSelection, 0, !full && start > end ? end : 0);
        }

        private static void Segment(VisualElement element, float start, float end)
        {
            element.style.left = Length.Percent(start * 100);
            element.style.width = Length.Percent(Mathf.Max(0, end - start) * 100);
        }

        private Vector2 MoveRange(Vector2 initial, float delta)
        {
            float width = Width(initial);
            float start = Repeat(Display(initial).x + delta);
            if (width >= Period) return new Vector2(start, start + Period);
            return new Vector2(start, Repeat(start + width));
        }

        private Vector2 MoveEndpoint(Vector2 initial, int endpoint, float delta)
        {
            float widthChange = endpoint == 0 ? -delta : delta;
            float width = Width(initial);
            float nextWidth = Mathf.Clamp(width + widthChange, 0, Period);
            if (nextWidth == width) return initial;
            var display = Display(initial);
            float start = display.x, end = display.y;
            if (nextWidth >= Period)
                return endpoint == 0 ? new Vector2(end - Period, end) : new Vector2(start, start + Period);
            if (nextWidth <= 0)
                return endpoint == 0 ? new Vector2(end, end) : new Vector2(start, start);
            return endpoint == 0 ? new Vector2(Repeat(end - nextWidth), end) :
                new Vector2(start, Repeat(start + nextWidth));
        }

        private sealed class RangeDrag : PointerManipulator
        {
            private readonly WhimTexCyclicRangeField field;
            private int pointer = -1, endpoint;
            private Vector2 initial;
            private float origin;

            internal RangeDrag(WhimTexCyclicRangeField field) { this.field = field; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCancelEvent>(Cancel);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<KeyDownEvent>(Key);
                target.RegisterCallback<DetachFromPanelEvent>(Detach);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Release();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCancelEvent>(Cancel);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<KeyDownEvent>(Key);
                target.UnregisterCallback<DetachFromPanelEvent>(Detach);
            }
            private float Position(Vector3 position) => field.rail.WorldToLocal(position).x / Mathf.Max(1, field.rail.resolvedStyle.width) * field.Period + field.minimum;
            private void Down(PointerDownEvent e)
            {
                if (pointer >= 0 || e.button != 0 || !target.enabledInHierarchy) return;
                initial = field.value; origin = Position(e.position);
                if (e.target == field.StartHandle) endpoint = 0;
                else if (e.target == field.EndHandle) endpoint = 1;
                else
                {
                    float p = Mathf.Clamp(origin, field.minimum, field.maximum);
                    bool inside = field.Width(initial) >= field.Period ||
                        (initial.x <= initial.y ? p > initial.x && p < initial.y : p > initial.x || p < initial.y);
                    endpoint = inside ? -1 : Mathf.Abs(p - initial.x) <= Mathf.Abs(p - initial.y) ? 0 : 1;
                    if (endpoint >= 0)
                    {
                        field.value = field.MoveEndpoint(initial, endpoint, p - (endpoint == 0 ? initial.x : initial.y));
                        initial = field.value;
                    }
                }
                (endpoint == 0 ? field.StartHandle : field.EndHandle).Focus();
                pointer = e.pointerId;
                target.CapturePointer(pointer);
                e.StopPropagation();
            }
            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || !target.HasPointerCapture(pointer)) return;
                float position = Position(e.position);
                float delta = position - origin; origin = position;
                field.value = endpoint < 0 ? field.MoveRange(field.value, delta) : field.MoveEndpoint(field.value, endpoint, delta);
                e.StopPropagation();
            }
            private void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer || e.button != 0) return;
                Release(); e.StopPropagation();
            }
            private void Cancel(PointerCancelEvent e)
            {
                if (e.pointerId != pointer) return;
                field.value = initial; Release(); e.StopPropagation();
            }
            private void Lost(PointerCaptureOutEvent e) { if (e.pointerId == pointer) pointer = -1; }
            private void Detach(DetachFromPanelEvent e) { Release(); }
            private void Release()
            {
                int previous = pointer; pointer = -1;
                if (previous >= 0 && target.HasPointerCapture(previous)) target.ReleasePointer(previous);
            }
            private void Key(KeyDownEvent e)
            {
                if (pointer >= 0 && e.keyCode == KeyCode.Escape)
                {
                    field.value = initial; Release(); e.StopPropagation(); return;
                }
                int direction = e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.DownArrow ? -1 :
                    e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.UpArrow ? 1 : 0;
                if (direction == 0) return;
                float delta = direction * field.Period * .01f * (e.shiftKey ? 10 : e.altKey ? .1f : 1);
                field.value = e.ctrlKey || e.commandKey ? field.MoveRange(field.value, delta) :
                    field.MoveEndpoint(field.value, e.target == field.StartHandle ? 0 : 1, delta);
                e.StopPropagation();
            }
        }
    }
}
