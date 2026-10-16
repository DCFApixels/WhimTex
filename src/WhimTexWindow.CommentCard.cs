using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private const float CommentLineWidth = 1.5f, CommentOutlineWidth = 1;
        private const float CommentNeckHalfWidth = (CommentLineWidth + CommentOutlineWidth) * .5f;
        private enum CommentHeaderAction { Delete, Locate, Link, Close }

        private sealed class CommentHeaderIcon : VisualElement
        {
            private readonly CommentHeaderAction action;

            internal CommentHeaderIcon(CommentHeaderAction action)
            {
                this.action = action;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-comment-header-icon");
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1 || contentRect.height < 1) return;
                var p = context.painter2D;
                float unit = Mathf.Min(contentRect.width, contentRect.height) / 18f;
                Vector2 Point(float x, float y) => contentRect.center + new Vector2(x - 9, y - 9) * unit;
                void Line(float x1, float y1, float x2, float y2)
                { p.MoveTo(Point(x1, y1)); p.LineTo(Point(x2, y2)); }
                p.strokeColor = p.fillColor = resolvedStyle.color;
                p.lineWidth = 1.5f * unit; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                switch (action)
                {
                    case CommentHeaderAction.Delete:
                        Line(3.5f, 5.5f, 14.5f, 5.5f);
                        p.MoveTo(Point(6.5f, 5.5f)); p.LineTo(Point(6.5f, 2.5f));
                        p.LineTo(Point(11.5f, 2.5f)); p.LineTo(Point(11.5f, 5.5f));
                        p.MoveTo(Point(4.5f, 5.5f)); p.LineTo(Point(5.5f, 15.5f));
                        p.LineTo(Point(12.5f, 15.5f)); p.LineTo(Point(13.5f, 5.5f));
                        Line(7.5f, 8.5f, 7.5f, 12.5f); Line(10.5f, 8.5f, 10.5f, 12.5f);
                        break;
                    case CommentHeaderAction.Locate:
                        p.Arc(Point(9, 9), 5.5f * unit, 0, 360); p.ClosePath(); p.Stroke();
                        p.BeginPath();
                        Line(9, 2, 9, 4); Line(9, 14, 9, 16);
                        Line(2, 9, 4, 9); Line(14, 9, 16, 9); p.Stroke();
                        p.BeginPath(); p.Arc(Point(9, 9), 1.5f * unit, 0, 360); p.ClosePath(); p.Fill();
                        return;
                    case CommentHeaderAction.Link:
                        Line(3.5f, 14.5f, 14.5f, 3.5f);
                        p.MoveTo(Point(7.5f, 3.5f)); p.LineTo(Point(14.5f, 3.5f)); p.LineTo(Point(14.5f, 10.5f));
                        break;
                    case CommentHeaderAction.Close:
                        Line(3.5f, 3.5f, 14.5f, 14.5f); Line(14.5f, 3.5f, 3.5f, 14.5f);
                        break;
                }
                p.Stroke();
            }
        }

        private void BuildFloatingComment(CommentEditorView editor, VisualElement header)
        {
            header.tooltip = "Drag to move. Double-click to restore automatic position and size.";
            editor.connector = new VisualElement { name = "commentConnector", pickingMode = PickingMode.Ignore };
            editor.connector.AddToClassList("whimtex-comment-connector");
            editor.connector.generateVisualContent += context => DrawCommentConnector(context.painter2D, editor);
            // Keep every connector below every card, independent of focus/stacking order.
            int index = rootVisualElement.IndexOf(editor.card);
            foreach (var other in commentEditors)
                if (other.card?.parent == rootVisualElement) index = Mathf.Min(index, rootVisualElement.IndexOf(other.card));
            rootVisualElement.Insert(index, editor.connector);
            // Each arrow stays directly above its card, below any cards stacked higher.
            editor.contact = new VisualElement { name = "commentContact", pickingMode = PickingMode.Ignore };
            editor.contact.AddToClassList("whimtex-comment-connector");
            editor.contact.generateVisualContent += context => DrawCommentContact(context.painter2D, editor);
            rootVisualElement.Insert(rootVisualElement.IndexOf(editor.card) + 1, editor.contact);
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                var handle = new CommentResizeHandle(new Vector2(x, y));
                handle.name = "commentResize" + x + "_" + y;
                handle.AddToClassList("whimtex-comment-resize");
                handle.AddToClassList(x == 0 ? "whimtex-comment-resize-v" : y == 0 ? "whimtex-comment-resize-h" :
                    x == y ? "whimtex-comment-resize-nw" : "whimtex-comment-resize-ne");
                bool corner = x != 0 && y != 0;
                if (x == 0) { handle.style.left = 9; handle.style.right = 9; }
                else { handle.style.width = corner ? 9 : 4; if (x < 0) handle.style.left = 0; else handle.style.right = 0; }
                if (y == 0) { handle.style.top = 9; handle.style.bottom = 9; }
                else { handle.style.height = corner ? 9 : 4; if (y < 0) handle.style.top = 0; else handle.style.bottom = 0; }
                editor.card.Add(handle);
            }
            editor.gesture = new CommentCardManipulator(this, editor, header);
            editor.card.AddManipulator(editor.gesture);
        }

        private float MinimumCommentCardHeight(CommentEditorView editor)
        {
            var header = editor.card.Q("commentCardHeader");
            float minimum = CommentMessageEditing(editor) ? 220f : 100f;
            if (header == null || !float.IsFinite(header.layout.height)) return minimum;
            var cardStyle = editor.card.resolvedStyle;
            var headerStyle = header.resolvedStyle;
            float fixedHeight = header.layout.height + headerStyle.marginTop + headerStyle.marginBottom +
                cardStyle.paddingTop + cardStyle.paddingBottom + cardStyle.borderTopWidth + cardStyle.borderBottomWidth;
            return Mathf.Max(minimum, fixedHeight + 40f);
        }

        private Rect ClampCommentCard(CommentEditorView editor, Rect rect)
        {
            Rect bounds = rootVisualElement.contentRect;
            float availableWidth = Mathf.Max(1, bounds.width - 16), availableHeight = Mathf.Max(1, bounds.height - 16);
            rect.width = Mathf.Clamp(rect.width, Mathf.Min(248, availableWidth), availableWidth);
            rect.height = Mathf.Clamp(rect.height, editor.state?.sized == true ? Mathf.Min(MinimumCommentCardHeight(editor), availableHeight) : 1, availableHeight);
            rect.x = Mathf.Clamp(rect.x, bounds.xMin + 8, Mathf.Max(bounds.xMin + 8, bounds.xMax - rect.width - 8));
            rect.y = Mathf.Clamp(rect.y, bounds.yMin + 8, Mathf.Max(bounds.yMin + 8, bounds.yMax - rect.height - 8));
            return rect;
        }

        private bool PositionFloatingComment(CommentEditorView editor, Vector2 anchor)
        {
            if (editor.state?.positioned != true) return false;
            Rect desired = editor.state.rect;
            if (!editor.state.sized && float.IsFinite(editor.card.layout.height)) desired.height = editor.card.layout.height;
            Rect rect = ClampCommentCard(editor, desired);
            editor.card.style.left = editor.card.style.top = 0;
            editor.card.style.translate = new Translate(rect.x, rect.y);
            editor.card.style.width = rect.width;
            editor.card.style.height = editor.state.sized ? new StyleLength(rect.height) : StyleKeyword.Auto;
            editor.card.style.maxHeight = Mathf.Max(1, rootVisualElement.contentRect.yMax - rect.y - 8);
            editor.card.EnableInClassList("whimtex-comment-card--sized", editor.state.sized);
            RefreshCommentConnector(editor, anchor, rect);
            return true;
        }

        private void RefreshCommentConnector(CommentEditorView editor, Vector2 anchor, Rect card)
        {
            if (editor.connector == null) return;
            Rect view = rootVisualElement.WorldToLocal(toolkitCanvas.worldBound);
            editor.lineStart = new Vector2(Mathf.Clamp(anchor.x, view.xMin, view.xMax), Mathf.Clamp(anchor.y, view.yMin, view.yMax));
            editor.contactBounds = card;
            editor.contactRadius = Mathf.Clamp(editor.card.resolvedStyle.borderTopLeftRadius, 0, Mathf.Min(card.width, card.height) * .5f);
            editor.contactOffset = CommentContactOffset(card, editor.contactRadius, editor.lineStart);
            editor.contactArrow = CommentContactArrow(card, editor.contactRadius, editor.contactOffset, editor.lineStart);
            editor.lineEnd = editor.contactArrow.visible ? (editor.contactArrow.tipStart + editor.contactArrow.tipEnd) * .5f :
                CommentOutlinePoint(card, editor.contactRadius, editor.contactOffset);
            var item = activeDocument.FindComment(editor.state.id);
            editor.lineColor = Color.Lerp(item == null ? editor.state.color : WhimTexUserSettings.GetCommentColor(item), Color.white, .35f);
            editor.lineColor.a = .85f;
            editor.connector.MarkDirtyRepaint();
            editor.contact.MarkDirtyRepaint();
        }

        private static void DrawCommentConnector(Painter2D painter, CommentEditorView editor)
        {
            var arrow = editor.contactArrow;
            if (!arrow.visible)
            {
                painter.lineCap = LineCap.Round;
                painter.BeginPath(); painter.MoveTo(editor.lineStart); painter.LineTo(editor.lineEnd);
                painter.strokeColor = new Color(0, 0, 0, .45f);
                painter.lineWidth = CommentLineWidth + 2 * CommentOutlineWidth; painter.Stroke();
                painter.strokeColor = editor.lineColor; painter.lineWidth = CommentLineWidth; painter.Stroke();
                return;
            }
            Vector2 across = (arrow.tipEnd - arrow.tipStart) * .5f;
            void Path()
            {
                painter.BeginPath();
                painter.MoveTo(arrow.tipStart);
                painter.LineTo(editor.lineStart - across);
                painter.LineTo(editor.lineStart + across);
                painter.LineTo(arrow.tipEnd);
            }
            Path(); painter.ClosePath();
            painter.fillColor = editor.lineColor; painter.Fill();
            Path();
            painter.strokeColor = new Color(0, 0, 0, .45f);
            painter.lineWidth = CommentOutlineWidth;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Round;
            painter.Stroke();
        }

        private static void DrawCommentContact(Painter2D painter, CommentEditorView editor)
        {
            if (editor.contactBounds.width <= 0 || editor.contactBounds.height <= 0) return;
            const float outside = 3, inside = 2, halfLength = 11;
            Vector2 Point(float distance, float inset)
            {
                Vector2 point = CommentOutlinePoint(editor.contactBounds, editor.contactRadius, distance, out var normal);
                return point + normal * inset;
            }
            void Edge(float from, float to, float inset)
            {
                int steps = Mathf.CeilToInt(Mathf.Abs(to - from) * 2);
                for (int i = 1; i <= steps; i++) painter.LineTo(Point(Mathf.Lerp(from, to, (float)i / steps), inset));
            }
            void Cap(float distance, bool atEnd)
            {
                Vector2 point = CommentOutlinePoint(editor.contactBounds, editor.contactRadius, distance, out var normal);
                Vector2 center = point + normal * ((outside - inside) * .5f);
                Vector2 tangent = new Vector2(-normal.y, normal.x);
                float radius = (outside + inside) * .5f;
                for (int i = 1; i <= 12; i++)
                {
                    float angle = Mathf.PI * i / 12;
                    Vector2 direction = -normal * Mathf.Cos(angle) + tangent * Mathf.Sin(angle);
                    painter.LineTo(center + direction * (atEnd ? radius : -radius));
                }
            }
            float start = editor.contactOffset - halfLength, end = editor.contactOffset + halfLength;
            void Path(bool fill)
            {
                painter.BeginPath();
                painter.MoveTo(Point(start, -inside));
                Edge(start, end, -inside);
                Cap(end, true);
                var arrow = editor.contactArrow;
                if (arrow.visible)
                {
                    Edge(end, arrow.end, outside);
                    painter.QuadraticCurveTo(arrow.endControl, arrow.tipEnd);
                    if (fill) painter.LineTo(arrow.tipStart);
                    else painter.MoveTo(arrow.tipStart);
                    painter.QuadraticCurveTo(arrow.startControl, Point(arrow.start, outside));
                    Edge(arrow.start, start, outside);
                }
                else Edge(end, start, outside);
                Cap(start, false);
                if (fill) painter.ClosePath();
            }
            Path(true);
            painter.fillColor = editor.lineColor;
            painter.Fill();
            Path(false);
            painter.strokeColor = new Color(0, 0, 0, .45f);
            painter.lineWidth = CommentOutlineWidth;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Round;
            painter.Stroke();
        }

        private struct CommentArrowGeometry
        {
            internal float start, end;
            internal Vector2 startControl, endControl, tipStart, tipEnd;
            internal bool visible => end > start;
        }

        private static CommentArrowGeometry CommentContactArrow(Rect card, float radius, float offset, Vector2 anchor)
        {
            Vector2 contact = CommentOutlinePoint(card, radius, offset, out var normal);
            Vector2 delta = anchor - contact;
            float distance = delta.magnitude;
            if (distance <= .001f || Vector2.Dot(delta, normal) <= 0) return default;
            float extension = Mathf.Max(0, Mathf.Min(28, distance * .65f) - 3);
            if (extension <= .5f) return default;
            float halfSpan = Mathf.Min(11, extension);
            float Root(float side)
            {
                CommentOutlinePoint(card, radius, offset + side * halfSpan, out var edgeNormal);
                float alignment = Mathf.Max(0, Vector2.Dot(normal, edgeNormal));
                return offset + side * halfSpan * (.25f + .75f * alignment * alignment);
            }
            Vector2 Outer(float position, out Vector2 edgeNormal)
            {
                Vector2 point = CommentOutlinePoint(card, radius, position, out edgeNormal);
                return point + edgeNormal * 3;
            }
            float start = Root(-1), end = Root(1);
            Vector2 a = Outer(start, out var startNormal), b = Outer(end, out var endNormal);
            Vector2 center = (a + b) * .5f, half = (b - a) * .5f;
            delta = anchor - center;
            distance = delta.magnitude;
            if (distance <= .001f || Vector2.Dot(delta, normal) <= 0) return default;
            Vector2 direction = delta / distance, across = new Vector2(-direction.y, direction.x);
            float halfWidth = Vector2.Dot(half, across);
            if (halfWidth <= CommentNeckHalfWidth + .05f) return default;
            Vector2 tip = center + direction * Mathf.Min(extension, distance * .65f);
            Vector2 shoulder = half * (CommentNeckHalfWidth / halfWidth);
            Vector2 Control(Vector2 root, Vector2 control, Vector2 edgeNormal)
            {
                // Keep the curve outside the band's tangent plane, including at rounded corners.
                // Moving along the connector axis also preserves the tangent at the narrow neck.
                float inset = Mathf.Max(0, Vector2.Dot(root - control, edgeNormal));
                return control + direction * (inset / Mathf.Max(.001f, Vector2.Dot(direction, edgeNormal)));
            }
            Vector2 startControl = Control(a, center - shoulder, startNormal);
            Vector2 endControl = Control(b, center + shoulder, endNormal);
            Vector2 tipStart = tip - across * CommentNeckHalfWidth, tipEnd = tip + across * CommentNeckHalfWidth;
            if (Vector2.Dot(tipStart - startControl, direction) <= 0 || Vector2.Dot(tipEnd - endControl, direction) <= 0)
                return default;
            return new CommentArrowGeometry
            {
                start = start, end = end,
                startControl = startControl, endControl = endControl,
                tipStart = tipStart, tipEnd = tipEnd
            };
        }

        private static float CommentContactOffset(Rect card, float radius, Vector2 anchor)
        {
            Vector2 end = new Vector2(Mathf.Clamp(anchor.x, card.xMin, card.xMax), Mathf.Clamp(anchor.y, card.yMin, card.yMax));
            if (card.Contains(anchor))
            {
                float left = anchor.x - card.xMin, right = card.xMax - anchor.x;
                float top = anchor.y - card.yMin, bottom = card.yMax - anchor.y;
                float nearest = Mathf.Min(left, right, top, bottom);
                if (nearest == left) end.x = card.xMin;
                else if (nearest == right) end.x = card.xMax;
                else if (nearest == top) end.y = card.yMin;
                else end.y = card.yMax;
            }
            float w = card.width - 2 * radius, h = card.height - 2 * radius, arc = Mathf.PI * radius * .5f;
            float l = card.xMin + radius, r = card.xMax - radius, t = card.yMin + radius, b = card.yMax - radius;
            float Angle(float y, float x) => Mathf.Atan2(Mathf.Max(0, y), Mathf.Max(0, x)) * radius;
            if (radius > 0)
            {
                if (end.x >= r && end.y <= t) return w + Angle(anchor.x - r, t - anchor.y);
                if (end.x >= r && end.y >= b) return w + h + arc + Angle(anchor.y - b, anchor.x - r);
                if (end.x <= l && end.y >= b) return 2 * w + h + 2 * arc + Angle(l - anchor.x, anchor.y - b);
                if (end.x <= l && end.y <= t) return 2 * w + 2 * h + 3 * arc + Angle(t - anchor.y, l - anchor.x);
            }
            if (end.y == card.yMin) return end.x - l;
            if (end.x == card.xMax) return w + arc + end.y - t;
            if (end.y == card.yMax) return w + h + 2 * arc + r - end.x;
            return 2 * w + h + 3 * arc + b - end.y;
        }

        private static Vector2 CommentOutlinePoint(Rect card, float radius, float distance)
            => CommentOutlinePoint(card, radius, distance, out _);

        private static Vector2 CommentOutlinePoint(Rect card, float radius, float distance, out Vector2 normal)
        {
            float w = card.width - 2 * radius, h = card.height - 2 * radius, arc = Mathf.PI * radius * .5f;
            distance = Mathf.Repeat(distance, 2 * (w + h) + 4 * arc);
            for (int side = 0; side < 4; side++)
            {
                float length = side % 2 == 0 ? w : h;
                if (distance <= length)
                {
                    normal = side == 0 ? Vector2.down : side == 1 ? Vector2.right : side == 2 ? Vector2.up : Vector2.left;
                    return side == 0 ? new Vector2(card.xMin + radius + distance, card.yMin) :
                        side == 1 ? new Vector2(card.xMax, card.yMin + radius + distance) :
                        side == 2 ? new Vector2(card.xMax - radius - distance, card.yMax) :
                        new Vector2(card.xMin, card.yMax - radius - distance);
                }
                distance -= length;
                if (distance < arc)
                {
                    Vector2 center = new Vector2(side < 2 ? card.xMax - radius : card.xMin + radius,
                        side == 0 || side == 3 ? card.yMin + radius : card.yMax - radius);
                    float angle = (side - 1) * Mathf.PI * .5f + distance / radius;
                    normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    return center + normal * radius;
                }
                distance -= arc;
            }
            normal = Vector2.down;
            return new Vector2(card.xMin + radius, card.yMin);
        }

        private sealed class CommentResizeHandle : VisualElement
        {
            internal readonly Vector2 direction;
            internal CommentResizeHandle(Vector2 direction)
            {
                this.direction = direction; tooltip = "Resize comment";
                if (direction == Vector2.one) generateVisualContent += context =>
                {
                    var p = context.painter2D; p.lineWidth = 1; p.strokeColor = new Color(.5f, .5f, .5f, .65f);
                    p.BeginPath(); p.MoveTo(new Vector2(2, 7)); p.LineTo(new Vector2(7, 2));
                    p.MoveTo(new Vector2(5, 7)); p.LineTo(new Vector2(7, 5)); p.Stroke();
                };
            }
        }

        private sealed class CommentCardManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private readonly VisualElement header;
            private readonly CommentEditorView editor;
            private int pointer = -1;
            private Vector2 down, direction;
            private Rect original, savedRect;
            private bool wasPositioned, wasSized;
            internal bool IsDragging => pointer >= 0;

            internal CommentCardManipulator(WhimTexWindow owner, CommentEditorView editor, VisualElement header)
            { this.owner = owner; this.editor = editor; this.header = header; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted);
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                target.RegisterCallback<DetachFromPanelEvent>(Detached);
                target.RegisterCallback<TooltipEvent>(Tooltip, TrickleDown.TrickleDown);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel(); target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted); target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<TooltipEvent>(Tooltip, TrickleDown.TrickleDown);
            }
            private void Tooltip(TooltipEvent evt)
            {
                if (!IsDragging) return;
                evt.tooltip = string.Empty;
                evt.StopImmediatePropagation();
            }
            private void Down(PointerDownEvent evt)
            {
                if (evt.button != 0 || pointer >= 0) return;
                bool heading = evt.target == header || evt.target == editor.title;
                var handle = evt.target as CommentResizeHandle;
                if (!heading && handle == null) return;
                var state = editor.state;
                if (heading && evt.clickCount == 2)
                {
                    state.positioned = state.sized = false;
                    target.style.translate = new Translate(0, 0); target.style.height = StyleKeyword.Auto;
                    target.RemoveFromClassList("whimtex-comment-card--sized"); owner.PositionCanvasComments();
                    WhimTexUI.ConsumeEvent(evt); return;
                }
                pointer = evt.pointerId; down = owner.rootVisualElement.WorldToLocal(evt.position);
                direction = handle?.direction ?? Vector2.zero;
                original = owner.rootVisualElement.WorldToLocal(target.worldBound);
                savedRect = state.rect; wasPositioned = state.positioned; wasSized = state.sized;
                state.rect = original; state.positioned = true;
                if (handle != null) state.sized = true;
                target.CapturePointer(pointer); target.Focus(); WhimTexUI.ConsumeEvent(evt);
            }
            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer || !target.HasPointerCapture(pointer)) return;
                Vector2 delta = owner.rootVisualElement.WorldToLocal(evt.position) - down;
                Rect rect = original;
                if (direction == Vector2.zero) rect.position += delta;
                else
                {
                    Rect bounds = owner.rootVisualElement.contentRect;
                    float minWidth = Mathf.Min(248, Mathf.Max(1, bounds.width - 16));
                    float minHeight = Mathf.Min(owner.MinimumCommentCardHeight(editor), Mathf.Max(1, bounds.height - 16));
                    if (direction.x < 0) rect.xMin = Mathf.Clamp(original.xMin + delta.x, bounds.xMin + 8, original.xMax - minWidth);
                    if (direction.x > 0) rect.xMax = Mathf.Clamp(original.xMax + delta.x, original.xMin + minWidth, bounds.xMax - 8);
                    if (direction.y < 0) rect.yMin = Mathf.Clamp(original.yMin + delta.y, bounds.yMin + 8, original.yMax - minHeight);
                    if (direction.y > 0) rect.yMax = Mathf.Clamp(original.yMax + delta.y, original.yMin + minHeight, bounds.yMax - 8);
                }
                editor.state.rect = owner.ClampCommentCard(editor, rect);
                owner.PositionCanvasComments(); WhimTexUI.ConsumeEvent(evt);
            }
            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                Release(); WhimTexUI.ConsumeEvent(evt);
            }
            private void Release()
            {
                int captured = pointer; pointer = -1;
                if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
            }
            internal void Cancel()
            {
                if (pointer < 0) return;
                if (editor.state != null)
                {
                    var state = editor.state;
                    state.rect = savedRect; state.positioned = wasPositioned; state.sized = wasSized;
                    if (!wasPositioned) { target.style.translate = new Translate(0, 0); target.style.height = StyleKeyword.Auto; }
                    target.EnableInClassList("whimtex-comment-card--sized", wasSized);
                }
                Release(); owner.PositionCanvasComments();
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Detached(DetachFromPanelEvent evt) => Cancel();
            private void Key(KeyDownEvent evt)
            { if (pointer >= 0 && evt.keyCode == KeyCode.Escape) { Cancel(); WhimTexUI.ConsumeEvent(evt); } }
        }
    }
}
