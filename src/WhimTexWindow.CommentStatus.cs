using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private static readonly Color CommentReviewColor = new Color32(232, 109, 115, 255);
        private CommentChoicePopup activeCommentChoice;

        private sealed class CommentStatusIcon : VisualElement
        {
            internal WhimTexCommentStatus Status { get; private set; }

            internal CommentStatusIcon()
            {
                AddToClassList("whimtex-comment-status-icon");
                generateVisualContent += Draw;
                RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
                RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
            }

            internal void SetStatus(WhimTexCommentStatus value)
            {
                Status = value;
                tooltip = CommentStatusLabels[(int)value];
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f) return;
                var p = context.painter2D;
                Vector2 center = contentRect.center;
                float unit = Mathf.Min(contentRect.width, contentRect.height) / 14f;
                Vector2 Point(float x, float y) => center + new Vector2(x - 7, y - 7) * unit;
                p.strokeColor = p.fillColor = resolvedStyle.color;
                p.lineWidth = 1.5f * unit;
                if (Status == WhimTexCommentStatus.Resolved)
                {
                    p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                    p.BeginPath(); p.MoveTo(Point(2.75f, 7.25f)); p.LineTo(Point(5.75f, 10.25f));
                    p.LineTo(Point(11.25f, 3.75f)); p.Stroke();
                }
                else if (Status == WhimTexCommentStatus.AwaitingReview)
                {
                    p.fillColor = CommentReviewColor;
                    p.BeginPath(); p.Arc(center, 5f * unit, 0, 360); p.ClosePath(); p.Fill();
                }
                else
                {
                    p.BeginPath(); p.Arc(center, 4.25f * unit, 0, 360); p.ClosePath(); p.Stroke();
                    p.BeginPath(); p.Arc(center, 4.25f * unit, 90, 270); p.ClosePath(); p.Fill();
                }
            }
        }

        private sealed class CommentChoiceField : BaseField<string>
        {
            private readonly WhimTexWindow owner;
            private readonly CommentStatusIcon icon;
            private readonly Label selectedLabel;
            internal readonly List<string> choices;
            internal int index => choices.IndexOf(value);

            internal CommentChoiceField(WhimTexWindow owner, List<string> choices, int index) : base(null, new VisualElement())
            {
                this.owner = owner;
                this.choices = choices;
                AddToClassList("whimtex-comment-choice");
                EnableInClassList("whimtex-comment-choice--light", !EditorGUIUtility.isProSkin);
                icon = new CommentStatusIcon { pickingMode = PickingMode.Ignore };
                var input = this.Q(className: inputUssClassName);
                input.AddToClassList("whimtex-comment-choice-input");
                selectedLabel = new Label { pickingMode = PickingMode.Ignore };
                selectedLabel.AddToClassList("whimtex-comment-choice-label");
                input.Add(icon);
                input.Add(selectedLabel);
                var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
                arrow.generateVisualContent += context =>
                {
                    var p = context.painter2D;
                    Vector2 center = arrow.contentRect.center;
                    p.fillColor = arrow.resolvedStyle.color;
                    p.BeginPath(); p.MoveTo(center + new Vector2(-3, -1));
                    p.LineTo(center + new Vector2(3, -1)); p.LineTo(center + new Vector2(0, 2));
                    p.ClosePath(); p.Fill();
                };
                arrow.AddToClassList("whimtex-comment-choice-arrow"); input.Add(arrow);
                SetValueWithoutNotify(choices[index]);
                RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || !input.worldBound.Contains(evt.position)) return;
                    WhimTexUI.ConsumeEvent(evt); OpenChoices(evt.pointerId);
                }, TrickleDown.TrickleDown);
                RegisterCallback<NavigationSubmitEvent>(evt => { WhimTexUI.ConsumeEvent(evt); OpenChoices(); }, TrickleDown.TrickleDown);
                RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Space && evt.keyCode != KeyCode.Return &&
                        evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.DownArrow) return;
                    WhimTexUI.ConsumeEvent(evt); OpenChoices();
                }, TrickleDown.TrickleDown);
            }

            public override void SetValueWithoutNotify(string newValue)
            {
                base.SetValueWithoutNotify(newValue);
                if (icon == null) return;
                int index = CommentStatusLabels.IndexOf(newValue);
                selectedLabel.text = index < 0 ? newValue : "";
                selectedLabel.EnableInClassList("whimtex-hidden", index >= 0);
                icon.EnableInClassList("whimtex-hidden", index < 0);
                if (index >= 0) icon.SetStatus((WhimTexCommentStatus)index);
                tooltip = newValue;
            }

            private void OpenChoices(int openingPointer = -1)
            {
                if (panel == null || !enabledInHierarchy) return;
                owner.activeCommentChoice?.Close();
                Focus();
                owner.activeCommentChoice = new CommentChoicePopup(owner, this);
                owner.activeCommentChoice.Open(openingPointer);
            }
        }

        private sealed class CommentChoicePopup
        {
            private readonly WhimTexWindow owner;
            private readonly CommentChoiceField field;
            private readonly List<Button> buttons = new();
            private VisualElement overlay, menu;
            private int pointer = -1, hovered = -1;
            internal CommentChoicePopup(WhimTexWindow owner, CommentChoiceField field)
            { this.owner = owner; this.field = field; }

            internal void Open(int openingPointer)
            {
                field.AddToClassList("whimtex-comment-choice--open");
                var root = owner.rootVisualElement;
                overlay = new VisualElement { name = "commentChoiceOverlay", userData = field };
                overlay.AddToClassList("whimtex-comment-choice-overlay");
                menu = new VisualElement { name = "commentChoices" };
                menu.AddToClassList("whimtex-comment-status-options");
                menu.EnableInClassList("whimtex-comment-status-options--light", !EditorGUIUtility.isProSkin);
                overlay.Add(menu);
                foreach (string label in field.choices)
                {
                    var button = new Button(() => Choose(label))
                        { name = "commentChoice" + field.choices.IndexOf(label), tooltip = label };
                    button.AddToClassList("whimtex-comment-status-option");
                    button.EnableInClassList("whimtex-comment-status-option--selected", field.value == label);
                    int status = CommentStatusLabels.IndexOf(label);
                    if (status < 0) button.text = label;
                    else
                    {
                        var icon = new CommentStatusIcon { pickingMode = PickingMode.Ignore };
                        icon.SetStatus((WhimTexCommentStatus)status); button.Add(icon);
                    }
                    buttons.Add(button); menu.Add(button);
                }
                Vector2 size = new Vector2(Mathf.Max(36, field.worldBound.width), field.choices.Count * 22 + 6);
                Rect anchor = root.WorldToLocal(field.worldBound), bounds = root.contentRect;
                menu.style.width = size.x;
                menu.style.height = size.y;
                menu.style.left = Mathf.Clamp(anchor.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x));
                menu.style.top = anchor.yMax + size.y <= bounds.yMax ? anchor.yMax : Mathf.Max(bounds.yMin, anchor.yMin - size.y);
                overlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (menu.worldBound.Contains(evt.position)) return;
                    WhimTexUI.ConsumeEvent(evt); Close(); field.Focus();
                }, TrickleDown.TrickleDown);
                overlay.RegisterCallback<NavigationMoveEvent>(evt =>
                {
                    int current = buttons.FindIndex(button => button == root.focusController.focusedElement);
                    int delta = evt.direction == NavigationMoveEvent.Direction.Up ? -1 :
                        evt.direction == NavigationMoveEvent.Direction.Down ? 1 : 0;
                    if (delta == 0) return;
                    buttons[(Mathf.Max(0, current) + delta + buttons.Count) % buttons.Count].Focus();
                    WhimTexUI.ConsumeEvent(evt);
                });
                root.RegisterCallback<GeometryChangedEvent>(Geometry);
                field.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                field.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                field.RegisterCallback<PointerCancelEvent>(Cancel);
                field.RegisterCallback<PointerCaptureOutEvent>(Lost);
                field.RegisterCallback<DetachFromPanelEvent>(Detached);
                field.RegisterCallback<GeometryChangedEvent>(Geometry);
                root.Add(overlay);
                pointer = openingPointer;
                if (pointer >= 0) field.CapturePointer(pointer);
                buttons[Mathf.Max(0, field.index)].Focus();
            }

            private int Hover(Vector2 position)
            {
                int next = buttons.FindIndex(button => button.enabledInHierarchy && button.worldBound.Contains(position));
                if (next != hovered)
                {
                    if (hovered >= 0) buttons[hovered].RemoveFromClassList("whimtex-comment-status-option--hover");
                    hovered = next;
                    if (hovered >= 0) buttons[hovered].AddToClassList("whimtex-comment-status-option--hover");
                }
                return next;
            }

            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer) return;
                WhimTexUI.ConsumeEvent(evt);
                if ((evt.pressedButtons & 1) == 0 || !field.HasPointerCapture(pointer)) { Close(); return; }
                Hover(evt.position);
            }

            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                WhimTexUI.ConsumeEvent(evt);
                int choice = Hover(evt.position);
                bool click = field.worldBound.Contains(evt.position);
                ReleasePointer();
                if (choice >= 0) Choose(field.choices[choice]);
                else if (!click) { Close(); field.Focus(); }
            }

            private void ReleasePointer()
            {
                int captured = pointer; pointer = -1;
                if (captured >= 0 && field.HasPointerCapture(captured)) field.ReleasePointer(captured);
            }

            internal void HandleKey(KeyDownEvent evt)
            {
                if (evt.keyCode != KeyCode.Escape) return;
                WhimTexUI.ConsumeEvent(evt); Close(); field.Focus();
            }

            private void Cancel(PointerCancelEvent evt) { if (evt.pointerId == pointer) Close(); }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Close(); }
            private void Detached(DetachFromPanelEvent evt) => Close();
            private void Geometry(GeometryChangedEvent evt) => Close();

            private void Choose(string label)
            {
                Close();
                if (field.panel != null && field.enabledInHierarchy) field.value = label;
                field.Focus();
            }

            internal void Close()
            {
                if (overlay == null) return;
                ReleasePointer();
                field.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                field.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                field.UnregisterCallback<PointerCancelEvent>(Cancel);
                field.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                field.UnregisterCallback<DetachFromPanelEvent>(Detached);
                field.UnregisterCallback<GeometryChangedEvent>(Geometry);
                owner.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(Geometry);
                overlay.RemoveFromHierarchy(); overlay = null;
                field.RemoveFromClassList("whimtex-comment-choice--open");
                if (owner.activeCommentChoice == this) owner.activeCommentChoice = null;
            }

            internal void BringToFront() => overlay?.BringToFront();
        }
    }
}
