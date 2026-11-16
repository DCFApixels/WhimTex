using ColorField = DCFApixels.WhimTex.WhimTexColorField;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using UnityEditor.UIElements;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed class CanvasPreviewPanelIcon : VisualElement
        {
            internal enum Kind { PostFx, Uv, Lights }
            private readonly Kind kind;

            internal CanvasPreviewPanelIcon(Kind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-icon");
                generateVisualContent += Draw;
            }

            private Vector2 P(float x, float y) => new Vector2(
                contentRect.x + x * contentRect.width / 22f,
                contentRect.y + y * contentRect.height / 22f);

            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f) return;
                var painter = context.painter2D;
                painter.strokeColor = painter.fillColor = resolvedStyle.color;
                painter.lineWidth = 1.35f;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                if (kind == Kind.Lights)
                {
                    painter.Arc(P(11, 11), contentRect.width * 4f / 22f, 0, 360);
                    painter.Stroke();
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4;
                        var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        painter.BeginPath(); painter.MoveTo(P(11+d.x*7, 11+d.y*7));
                        painter.LineTo(P(11+d.x*10, 11+d.y*10)); painter.Stroke();
                    }
                }
                else if (kind == Kind.Uv)
                {
                    painter.lineWidth = 1f;
                    painter.lineJoin = LineJoin.Miter;
                    painter.lineCap = LineCap.Butt;
                    painter.MoveTo(P(.5f, 8.5f)); painter.LineTo(P(5.5f, 8.5f));
                    painter.LineTo(P(5.5f, 3.5f)); painter.LineTo(P(10.5f, 3.5f));
                    painter.LineTo(P(10.5f, 8.5f)); painter.LineTo(P(20.5f, 8.5f));
                    painter.LineTo(P(20.5f, 13.5f)); painter.LineTo(P(10.5f, 13.5f));
                    painter.LineTo(P(10.5f, 18.5f)); painter.LineTo(P(5.5f, 18.5f));
                    painter.LineTo(P(5.5f, 13.5f)); painter.LineTo(P(.5f, 13.5f));
                    painter.ClosePath();
                    painter.MoveTo(P(5.5f, 8.5f)); painter.LineTo(P(5.5f, 13.5f));
                    painter.MoveTo(P(10.5f, 8.5f)); painter.LineTo(P(10.5f, 13.5f));
                    painter.MoveTo(P(15.5f, 8.5f)); painter.LineTo(P(15.5f, 13.5f));
                    painter.MoveTo(P(5.5f, 8.5f)); painter.LineTo(P(10.5f, 8.5f));
                    painter.MoveTo(P(5.5f, 13.5f)); painter.LineTo(P(10.5f, 13.5f));
                    painter.Stroke();
                }
                else
                {
                    painter.MoveTo(P(9, 6)); painter.LineTo(P(3, 6));
                    painter.LineTo(P(3, 19)); painter.LineTo(P(17, 19));
                    painter.LineTo(P(17, 13)); painter.Stroke();
                    DrawSparkle(painter, 15, 7, 6);
                    DrawSparkle(painter, 7, 13, 2.5f);
                }
            }

            private void DrawSparkle(Painter2D painter, float x, float y, float radius)
            {
                float neck = radius * .24f;
                painter.BeginPath();
                painter.MoveTo(P(x, y - radius));
                painter.QuadraticCurveTo(P(x + neck, y - neck), P(x + radius, y));
                painter.QuadraticCurveTo(P(x + neck, y + neck), P(x, y + radius));
                painter.QuadraticCurveTo(P(x - neck, y + neck), P(x - radius, y));
                painter.QuadraticCurveTo(P(x - neck, y - neck), P(x, y - radius));
                painter.ClosePath(); painter.Fill();
            }
        }

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

        private static class LightListIcons
        {
            private static readonly Dictionary<(WhimTexLightType, bool), VectorImage> images = new();

            static LightListIcons()
            {
                AssemblyReloadEvents.beforeAssemblyReload += Clear;
                EditorApplication.quitting += Clear;
            }

            internal static VectorImage Get(WhimTexLightType type, bool filled)
            {
                var key = (type, filled);
                if (images.TryGetValue(key, out var image) && image != null) return image;
                using var painter = new Painter2D();
                painter.fillColor = Color.clear; painter.BeginPath();
                CanvasHandlePainter.Square(painter, new Vector2(8, 8), 8); painter.Fill();
                DrawLightIcon(painter, type, new Vector2(8, 8), Color.white, filled, .5f, 1);
                image = ScriptableObject.CreateInstance<VectorImage>();
                image.name = "WhimTex " + type + (filled ? " filled icon" : " outline icon"); image.hideFlags = HideFlags.HideAndDontSave;
                painter.SaveToVectorImage(image);
                images[key] = image;
                return image;
            }

            private static void Clear()
            {
                foreach (var image in images.Values) UnityEngine.Object.DestroyImmediate(image);
                images.Clear();
            }
        }

        private static void DrawLightIcon(Painter2D p, WhimTexLightType type, Vector2 center,
            Color color, bool filled, float scale, float width, float outlineWidth = 0)
        {
            Vector2 P(float x, float y) => center + new Vector2(x, y) * scale;
            Vector2 F(float x, float y) => P((x + y) * .70710678f, (y - x) * .70710678f);
            Color outline = new Color(.08f, .08f, .08f, color.a);
            float threadRadius = Mathf.Min(width, 1) * .5f;
            float threadOutline = Mathf.Max(outlineWidth, scale);
            // Filled contours avoid overlapping stroke joins on these small silhouettes.
            if (type == WhimTexLightType.Point) PaintBase(threadRadius + threadOutline, outline);
            if (outlineWidth > 0) Paint(width * .5f + outlineWidth, outline);
            Paint(width * .5f, color);
            if (type == WhimTexLightType.Point) PaintBase(threadRadius, color);

            void PaintBase(float radius, Color fill)
            {
                float first = 4 * scale + width * .5f + threadOutline + threadRadius;
                float second = first + 2 * threadRadius + threadOutline;
                // One shared outline separates the bands. Separate fills unite their
                // overlapping dark contours instead of cutting holes with OddEven.
                Band(3 * scale, first);
                Band(1.5f * scale, second);
                void Band(float halfWidth, float y)
                {
                    p.BeginPath();
                    CanvasHandlePainter.Capsule(p, center + new Vector2(-halfWidth, y),
                        center + new Vector2(halfWidth, y), radius);
                    p.fillColor = fill; p.Fill();
                }
            }
            void Paint(float radius, Color fill)
            {
                p.BeginPath(); Body(radius / scale);
                if (!filled) Body(-radius / scale);
                void Ray(Vector2 a, Vector2 b) => CanvasHandlePainter.Capsule(p, a, b, radius);
                switch (type)
                {
                    case WhimTexLightType.Directional:
                        for (int i = 0; i < 8; i++)
                        {
                            float angle = i * Mathf.PI / 4;
                            Vector2 ray = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                            Ray(center + ray * (10.5f * scale), center + ray * (13 * scale));
                        }
                        break;
                    case WhimTexLightType.Spot:
                        Ray(F(10, -4), F(13, -6)); Ray(F(10, 0), F(14, 0)); Ray(F(10, 4), F(13, 6));
                        break;
                    case WhimTexLightType.Area:
                        Ray(P(-6, 0), P(-10, -8)); Ray(P(0, -1), P(0, -11)); Ray(P(6, 0), P(10, -8));
                        break;
                }
                p.fillColor = fill; p.Fill(FillRule.OddEven);
            }
            void Body(float d)
            {
                switch (type)
                {
                    case WhimTexLightType.Directional:
                        CanvasHandlePainter.Circle(p, center, (6 + d) * scale); break;
                    case WhimTexLightType.Point:
                        p.MoveTo(P(-3 - d, 4 + d));
                        p.BezierCurveTo(P(-3 - d, 1), P(-8 - d, 0), P(-8 - d, -5));
                        p.BezierCurveTo(P(-8 - d, -9 - d), P(-4, -12 - d), P(0, -12 - d));
                        p.BezierCurveTo(P(4, -12 - d), P(8 + d, -9 - d), P(8 + d, -5));
                        p.BezierCurveTo(P(8 + d, 0), P(3 + d, 1), P(3 + d, 4 + d));
                        p.ClosePath(); break;
                    case WhimTexLightType.Spot:
                        // Parallel offsets retain the short cylindrical head after the tapered shoulder.
                        float shoulder = -1 - .30277564f * d, head = 2 - .30277564f * d;
                        p.MoveTo(F(-11 - d, -3 - d)); p.LineTo(F(shoulder, -3 - d));
                        p.LineTo(F(head, -5 - d)); p.LineTo(F(5 + d, -5 - d));
                        p.LineTo(F(5 + d, 5 + d)); p.LineTo(F(head, 5 + d));
                        p.LineTo(F(shoulder, 3 + d)); p.LineTo(F(-11 - d, 3 + d)); p.ClosePath(); break;
                    case WhimTexLightType.Area:
                        if (d <= -2) break;
                        p.MoveTo(P(-11 - d, 5 - d)); p.LineTo(P(11 + d, 5 - d));
                        p.LineTo(P(11 + d, 9 + d)); p.LineTo(P(-11 - d, 9 + d)); p.ClosePath(); break;
                }
            }
        }

        private sealed class ToolDropdownMarker : VisualElement
        {
            internal ToolDropdownMarker()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-dropdown-marker");
                generateVisualContent += Draw;
            }
            private void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect;
                if (r.width <= 0f || r.height <= 0f) return;
                var p = context.painter2D;
                p.fillColor = resolvedStyle.color;
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMax, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.xMin, r.yMax));
                p.ClosePath();
                p.Fill();
            }
        }

        private sealed class ShapeToolIcon : VisualElement
        {
            private ShapeLayerBehaviour.ShapeKind kind;
            internal ShapeToolIcon(ShapeLayerBehaviour.ShapeKind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-icon");
                generateVisualContent += Draw;
            }
            internal void SetKind(ShapeLayerBehaviour.ShapeKind value)
            {
                if (kind == value) return;
                kind = value;
                MarkDirtyRepaint();
            }
            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f) return;
                var p = context.painter2D;
                p.strokeColor = resolvedStyle.color;
                p.lineWidth = 1.4f;
                p.lineJoin = LineJoin.Round;
                p.lineCap = LineCap.Round;
                Vector2 center = contentRect.center;
                float radius = Mathf.Min(contentRect.width, contentRect.height) * .37f;
                p.BeginPath();
                if (kind == ShapeLayerBehaviour.ShapeKind.Line)
                {
                    p.MoveTo(center + new Vector2(-radius, radius));
                    p.LineTo(center + new Vector2(radius, -radius));
                }
                else if (kind == ShapeLayerBehaviour.ShapeKind.Arc || kind == ShapeLayerBehaviour.ShapeKind.Sector)
                {
                    p.Arc(center, radius, -60f, 200f);
                    if (kind == ShapeLayerBehaviour.ShapeKind.Sector) { p.LineTo(center); p.ClosePath(); }
                }
                else if (kind == ShapeLayerBehaviour.ShapeKind.Ellipse)
                {
                    p.Arc(center, radius, 0f, 360f);
                    p.ClosePath();
                }
                else
                {
                    int count = kind == ShapeLayerBehaviour.ShapeKind.Rectangle ? 4 : kind == ShapeLayerBehaviour.ShapeKind.Star ? 10 : 5;
                    for (int i = 0; i < count; i++)
                    {
                        float a = -Mathf.PI * .5f + i * Mathf.PI * 2f / count;
                        float r = kind == ShapeLayerBehaviour.ShapeKind.Star && (i & 1) != 0 ? radius * .45f : radius;
                        Vector2 point = kind == ShapeLayerBehaviour.ShapeKind.Rectangle
                            ? center + new Vector2(i == 0 || i == 3 ? -radius : radius, i < 2 ? -radius : radius)
                            : center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (i == 0) p.MoveTo(point); else p.LineTo(point);
                    }
                    p.ClosePath();
                }
                p.Stroke();
            }
        }

        private sealed class CanvasToolIcon : VisualElement
        {
            private readonly CanvasTool tool;
            private bool ellipse;
            private bool freehand;
            private bool hasComments;
            private bool hasReviewComments;
            private CanvasCropMode cropMode;
            private GradientCreationType gradientType;
            private readonly bool largeGradient;
            private readonly VectorFillMode? flatFill;
            private readonly Image healingVectorImage;
            private readonly Image alignedVectorImage;
            private Vector2 healingVectorOffset;
            private bool hasHealingVectorOffset;
            private Vector2 alignedVectorOffset;
            private bool hasAlignedVectorOffset;

            internal CanvasToolIcon(CanvasTool tool, bool ellipse = false, CanvasCropMode cropMode = CanvasCropMode.Resize,
                GradientCreationType gradientType = GradientCreationType.Linear, bool largeGradient = false, VectorFillMode? flatFill = null)
            {
                this.tool = tool;
                this.ellipse = ellipse;
                this.cropMode = cropMode;
                this.gradientType = gradientType;
                this.largeGradient = largeGradient;
                this.flatFill = flatFill;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-icon");
                generateVisualContent += Draw;
                if (tool == CanvasTool.QuickSelection)
                {
                    AddToClassList("whimtex-tool-icon--quick-selection");
                    Add(new CanvasToolIcon(CanvasTool.Brush));
                }
#if UNITY_6000_3_OR_NEWER
                if (tool == CanvasTool.HealingBrush)
                {
                    var vector = AssetDatabase.LoadAssetAtPath<VectorImage>(
                        "Packages/com.dcfapixels.whimtex/src/Editor/Icons/HealingBrush.svg");
                    if (vector != null)
                    {
                        healingVectorImage = new Image
                        {
                            vectorImage = vector,
                            scaleMode = ScaleMode.ScaleToFit,
                            pickingMode = PickingMode.Ignore
                        };
                        healingVectorImage.AddToClassList("whimtex-tool-vector-image");
                        Add(healingVectorImage);
                        RegisterCallback<GeometryChangedEvent>(OnHealingVectorGeometryChanged);
                        healingVectorImage.RegisterCallback<GeometryChangedEvent>(OnHealingVectorGeometryChanged);
                        RegisterCallback<AttachToPanelEvent>(_ => AlignHealingVectorImage());
                    }
                }
                else if (tool == CanvasTool.Fill || tool == CanvasTool.Pencil)
                {
                    var vector = AssetDatabase.LoadAssetAtPath<VectorImage>(
                        "Packages/com.dcfapixels.whimtex/src/Editor/Icons/" +
                        (tool == CanvasTool.Fill ? "Fill.svg" : "Pencil.svg"));
                    if (vector != null)
                    {
                        alignedVectorImage = new Image
                        {
                            vectorImage = vector,
                            scaleMode = ScaleMode.ScaleToFit,
                            pickingMode = PickingMode.Ignore
                        };
                        alignedVectorImage.AddToClassList("whimtex-tool-vector-image");
                        if (tool == CanvasTool.Pencil)
                            alignedVectorImage.AddToClassList("whimtex-tool-vector-image--pencil");
                        Add(alignedVectorImage);
                        RegisterCallback<GeometryChangedEvent>(_ => AlignVectorImage());
                        alignedVectorImage.RegisterCallback<GeometryChangedEvent>(_ => AlignVectorImage());
                        RegisterCallback<AttachToPanelEvent>(_ => AlignVectorImage());
                    }
                }
#endif
            }

            private void AlignVectorImage()
            {
                if (alignedVectorImage == null || panel == null || alignedVectorImage.contentRect.width < 1f) return;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 pixels = alignedVectorImage.LocalToWorld(Vector2.zero) * pixelsPerPoint;
                Vector2 snapped = new Vector2(Mathf.Round(pixels.x), Mathf.Round(pixels.y));
                Translate applied = alignedVectorImage.resolvedStyle.translate;
                Vector2 offset = new Vector2(applied.x.value, applied.y.value) + this.WorldToLocal(snapped / pixelsPerPoint)
                    - this.WorldToLocal(pixels / pixelsPerPoint);
                if (hasAlignedVectorOffset && (offset - alignedVectorOffset).sqrMagnitude < .000001f) return;
                alignedVectorOffset = offset; hasAlignedVectorOffset = true;
                alignedVectorImage.style.translate = new Translate(offset.x, offset.y);
            }

            private void OnHealingVectorGeometryChanged(GeometryChangedEvent evt) => AlignHealingVectorImage();

            internal void AlignHealingVectorImage()
            {
                if (healingVectorImage == null || panel == null || healingVectorImage.contentRect.width < 1f)
                    return;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 pixels = healingVectorImage.LocalToWorld(healingVectorImage.contentRect.center) * pixelsPerPoint;
                Vector2 snapped = new Vector2(Mathf.Floor(pixels.x) + 0.5f,
                    Mathf.Floor(pixels.y) + 0.5f);
                Translate applied = healingVectorImage.resolvedStyle.translate;
                Vector2 offset = new Vector2(applied.x.value, applied.y.value) + this.WorldToLocal(snapped / pixelsPerPoint)
                    - this.WorldToLocal(pixels / pixelsPerPoint);
                if (hasHealingVectorOffset && (offset - healingVectorOffset).sqrMagnitude < 0.000001f)
                    return;
                healingVectorOffset = offset;
                hasHealingVectorOffset = true;
                healingVectorImage.style.translate = new Translate(offset.x, offset.y);
            }

            internal void SetEllipse(bool value)
            {
                if (ellipse == value) return;
                ellipse = value;
                MarkDirtyRepaint();
            }

            internal void SetCropMode(CanvasCropMode value)
            {
                if (cropMode == value) return;
                cropMode = value;
                MarkDirtyRepaint();
            }

            internal void SetFreehand(bool value)
            {
                if (freehand == value) return;
                freehand = value;
                MarkDirtyRepaint();
            }

            internal void SetGradientType(GradientCreationType value)
            {
                if (gradientType == value) return;
                gradientType = value;
                MarkDirtyRepaint();
            }

            internal void SetCommentsState(bool value, bool review)
            {
                if (hasComments == value && hasReviewComments == review) return;
                hasComments = value;
                hasReviewComments = review;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f)
                    return;
                if (healingVectorImage != null || alignedVectorImage != null)
                    return;
                Painter2D painter = context.painter2D;
                painter.strokeColor = resolvedStyle.color;
                painter.fillColor = resolvedStyle.color;
                painter.lineWidth = 1.35f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                if (tool == CanvasTool.None)
                    DrawPointer(painter);
                else if (IsVectorTool(tool))
                    DrawVectorToolIcon(painter, tool, P);
                else if (tool == CanvasTool.Transform)
                    DrawTransform(painter);
                else if (tool == CanvasTool.Text)
                    DrawTextTool(painter);
                else if (tool == CanvasTool.Comment)
                    DrawCommentTool(painter);
                else if (tool == CanvasTool.Crop)
                {
                    if (cropMode == CanvasCropMode.Scale) DrawCanvasScaleTool(painter);
                    else DrawCropTool(painter);
                }
                else if (tool == CanvasTool.GradientHandles || IsTemporaryCanvasTool(tool))
                    DrawHand(painter, withHandle: true);
                else if (tool == CanvasTool.UvIslandSelect)
                    DrawUvSelect(painter);
                else if (tool == CanvasTool.Fill)
                    DrawBucket(painter);
                else if (tool == CanvasTool.Gradient)
                    DrawGradient(context);
                else if (tool == CanvasTool.Zoom)
                    DrawMagnifier(painter);
                else if (tool == CanvasTool.Pencil)
                    DrawPencil(painter);
                else if (tool == CanvasTool.BlurBrush)
                    DrawBlurBrush(context);
                else if (tool == CanvasTool.SmudgeBrush)
                    DrawSmudgeBrush(painter);
                else if (tool == CanvasTool.HealingBrush)
                    DrawHealingBrush(painter);
                else if (tool == CanvasTool.RectangleSelect)
                {
                    if (ellipse) DrawEllipseSelect(painter);
                    else DrawRectangleSelect(painter);
                }
                else if (tool == CanvasTool.PolygonSelect)
                {
                    if (freehand) DrawFreehandSelect(painter);
                    else DrawPolygonSelect(painter);
                }
                else if (tool == CanvasTool.QuickSelection)
                    DrawQuickSelection(painter);
                else
                    DrawBrush(painter);
            }

            private void DrawQuickSelection(Painter2D painter)
            {
                painter.lineWidth = 1.2f;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.MoveTo(P(1.00f, 13.00f));
                painter.BezierCurveTo(P(1.00f, 11.83f), P(1.41f, 10.69f), P(2.17f, 9.79f));
                painter.MoveTo(P(4.29f, 8.30f));
                painter.BezierCurveTo(P(5.40f, 7.90f), P(6.60f, 7.90f), P(7.71f, 8.30f));
                painter.MoveTo(P(9.83f, 9.79f));
                painter.BezierCurveTo(P(10.59f, 10.69f), P(11.00f, 11.83f), P(11.00f, 13.00f));
                painter.MoveTo(P(11.00f, 18.00f));
                painter.BezierCurveTo(P(11.00f, 19.18f), P(10.59f, 20.31f), P(9.83f, 21.21f));
                painter.MoveTo(P(7.71f, 22.70f));
                painter.BezierCurveTo(P(6.60f, 23.10f), P(5.40f, 23.10f), P(4.29f, 22.70f));
                painter.MoveTo(P(2.17f, 21.21f));
                painter.BezierCurveTo(P(1.41f, 20.31f), P(1.00f, 19.18f), P(1.00f, 18.00f));
                painter.MoveTo(P(1.00f, 14.10f));
                painter.LineTo(P(1.00f, 16.90f));
                painter.MoveTo(P(11.00f, 14.10f));
                painter.LineTo(P(11.00f, 16.90f));
                painter.Stroke();
            }

            private void DrawCommentTool(Painter2D painter)
            {
                float unit = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                painter.BeginPath();
                painter.MoveTo(P(20, hasReviewComments ? 9.5f : 5)); painter.LineTo(P(20, 16));
                painter.LineTo(P(11, 16)); painter.LineTo(P(5, 21)); painter.LineTo(P(5, 5));
                painter.LineTo(P(hasReviewComments ? 15.5f : 20, 5));
                if (!hasReviewComments) painter.ClosePath();
                painter.Stroke();
                if (hasReviewComments)
                {
                    // Leave a real transparent gap; do not paint a panel-colored ring over the icon.
                    painter.Arc(P(20, 5), 4.5f * unit, 180, 90, ArcDirection.CounterClockwise);
                    painter.ClosePath();
                }
                if (hasComments)
                {
                    TraceRectangle(painter, P(8, 8), P(hasReviewComments ? 16 : 17, 10));
                    TraceRectangle(painter, P(8, 11), P(15, 13));
                    painter.Fill(FillRule.OddEven);
                    if (hasReviewComments)
                        DrawCommentReviewBadge(painter, P(20, 5), 3f * unit, false);
                    return;
                }
                painter.BeginPath(); painter.MoveTo(P(8, 9)); painter.LineTo(P(17, 9));
                painter.MoveTo(P(8, 12)); painter.LineTo(P(15, 12)); painter.Stroke();
            }

            private Vector2 P(float x, float y) => new Vector2(
                contentRect.x + x * contentRect.width / 24f,
                contentRect.y + y * contentRect.height / 24f);

            private void DrawCanvasScaleTool(Painter2D painter)
            {
                float ppi = EditorGUIUtility.pixelsPerPoint;
                int strokePixels = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.Min(contentRect.width, contentRect.height) / 22f * ppi));
                float offset = (strokePixels & 1) == 0 ? 0f : .5f;
                Vector2 Pixel(float x, float y)
                {
                    Vector2 local = new Vector2(contentRect.x + x * contentRect.width / 22f,
                        contentRect.y + y * contentRect.height / 22f);
                    Vector2 world = this.LocalToWorld(local) * ppi;
                    return this.WorldToLocal(new Vector2(Mathf.Round(world.x - offset) + offset,
                        Mathf.Round(world.y - offset) + offset) / ppi);
                }
                painter.lineWidth = strokePixels / ppi;
                painter.lineCap = LineCap.Butt;
                painter.lineJoin = LineJoin.Miter;
                painter.BeginPath();
                painter.MoveTo(Pixel(6, 6)); painter.LineTo(Pixel(16, 6));
                painter.LineTo(Pixel(16, 16)); painter.LineTo(Pixel(6, 16)); painter.ClosePath();
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(Pixel(16, 6)); painter.LineTo(Pixel(20, 2));
                painter.MoveTo(Pixel(16, 2)); painter.LineTo(Pixel(20, 2)); painter.LineTo(Pixel(20, 6));
                painter.MoveTo(Pixel(6, 16)); painter.LineTo(Pixel(2, 20));
                painter.MoveTo(Pixel(2, 16)); painter.LineTo(Pixel(2, 20)); painter.LineTo(Pixel(6, 20));
                painter.Stroke();
            }

            private void DrawCropTool(Painter2D painter)
            {
                float ppi = EditorGUIUtility.pixelsPerPoint;
                Vector2 Pixel(float x, float y)
                {
                    Vector2 world = this.LocalToWorld(P(x, y)) * ppi;
                    return this.WorldToLocal(new Vector2(Mathf.Round(world.x), Mathf.Round(world.y)) / ppi);
                }
                painter.BeginPath();
                painter.MoveTo(Pixel(5, 2)); painter.LineTo(Pixel(8, 2)); painter.LineTo(Pixel(8, 16));
                painter.LineTo(Pixel(22, 16)); painter.LineTo(Pixel(22, 19)); painter.LineTo(Pixel(5, 19)); painter.ClosePath();
                painter.Fill();
                painter.BeginPath();
                painter.MoveTo(Pixel(2, 5)); painter.LineTo(Pixel(19, 5)); painter.LineTo(Pixel(19, 22));
                painter.LineTo(Pixel(16, 22)); painter.LineTo(Pixel(16, 8)); painter.LineTo(Pixel(2, 8)); painter.ClosePath();
                painter.Fill();
            }

            private void DrawTextTool(Painter2D painter)
            {
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 Pixel(float x, float y)
                {
                    Vector2 point = this.LocalToWorld(P(x, y)) * pixelsPerPoint;
                    return this.WorldToLocal(new Vector2(Mathf.Round(point.x), Mathf.Round(point.y)) / pixelsPerPoint);
                }
                painter.BeginPath();
                painter.MoveTo(Pixel(4, 3)); painter.LineTo(Pixel(20, 3));
                painter.LineTo(Pixel(20, 7)); painter.LineTo(Pixel(18.5f, 7));
                painter.BezierCurveTo(Pixel(18.4f, 5.6f), Pixel(17.8f, 5), Pixel(16, 5));
                painter.LineTo(Pixel(13.5f, 5)); painter.LineTo(Pixel(13.5f, 18));
                painter.BezierCurveTo(Pixel(13.5f, 19.1f), Pixel(14.2f, 19.6f), Pixel(16, 19.6f));
                painter.LineTo(Pixel(16, 21)); painter.LineTo(Pixel(8, 21));
                painter.LineTo(Pixel(8, 19.6f));
                painter.BezierCurveTo(Pixel(9.8f, 19.6f), Pixel(10.5f, 19.1f), Pixel(10.5f, 18));
                painter.LineTo(Pixel(10.5f, 5)); painter.LineTo(Pixel(8, 5));
                painter.BezierCurveTo(Pixel(6.2f, 5), Pixel(5.6f, 5.6f), Pixel(5.5f, 7));
                painter.LineTo(Pixel(4, 7)); painter.ClosePath();
                painter.Fill();
            }

            private void DrawGradient(MeshGenerationContext context)
            {
                var painter = context.painter2D;
                Color tint = resolvedStyle.color;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 halfStroke = Vector2.one * (painter.lineWidth * .5f);
                float inset = largeGradient ? 2 : 4;
                Vector2 pixelMin = this.LocalToWorld(P(inset, inset) - halfStroke) * pixelsPerPoint;
                Vector2 pixelMax = this.LocalToWorld(P(24 - inset, 24 - inset) + halfStroke) * pixelsPerPoint;
                pixelMin = new Vector2(Mathf.Round(pixelMin.x), Mathf.Round(pixelMin.y));
                pixelMax = new Vector2(Mathf.Round(pixelMax.x), Mathf.Round(pixelMax.y));
                Vector2 outerMin = this.WorldToLocal(pixelMin / pixelsPerPoint);
                Vector2 outerMax = this.WorldToLocal(pixelMax / pixelsPerPoint);
                Vector2 innerMin = this.WorldToLocal((pixelMin + Vector2.one) / pixelsPerPoint);
                Vector2 innerMax = this.WorldToLocal((pixelMax - Vector2.one) / pixelsPerPoint);

                painter.fillColor = tint;
                painter.BeginPath();
                TraceRectangle(painter, outerMin, outerMax);
                if (flatFill == VectorFillMode.Solid) { painter.Fill(); return; }
                TraceRectangle(painter, innerMin, innerMax);
                painter.Fill(FillRule.OddEven);
                if (flatFill == VectorFillMode.None)
                {
                    painter.strokeColor = tint;
                    painter.lineWidth = 1.5f / pixelsPerPoint;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(innerMin.x, innerMax.y));
                    painter.LineTo(new Vector2(innerMax.x, innerMin.y));
                    painter.Stroke();
                    return;
                }

                Color light = new Color(.95f, .95f, .95f, tint.a), dark = new Color(.1f, .1f, .1f, tint.a);
                Vector2 center = (innerMin + innerMax) * .5f, extent = (innerMax - innerMin) * .5f;
                bool linear = gradientType == GradientCreationType.Linear;
                bool radial = gradientType == GradientCreationType.Radial;
                painter.fillColor = linear || radial ? Color.white : dark;
                if (linear)
                    painter.fillGradient = FillGradient.MakeLinearGradient(light, dark, innerMin, new Vector2(innerMax.x, innerMin.y), AddressMode.Clamp);
                else if (radial)
                    painter.fillGradient = FillGradient.MakeRadialGradient(light, dark, center, extent.x, center, AddressMode.Clamp);
                painter.BeginPath();
                TraceRectangle(painter, innerMin, innerMax);
                painter.Fill();
                if (linear || radial) return;
                if (gradientType == GradientCreationType.Mesh)
                {
                    painter.lineWidth = 1 / pixelsPerPoint; painter.strokeColor = tint;
                    painter.BeginPath();
                    for (int i = 1; i < 3; i++)
                    {
                        float t = i / 3f;
                        painter.MoveTo(new Vector2(Mathf.Lerp(innerMin.x, innerMax.x, t), innerMin.y));
                        painter.BezierCurveTo(innerMin + Vector2.Scale(new Vector2(t + .12f, .33f), innerMax - innerMin), innerMin + Vector2.Scale(new Vector2(t - .12f, .67f), innerMax - innerMin), new Vector2(Mathf.Lerp(innerMin.x, innerMax.x, t), innerMax.y));
                        painter.MoveTo(new Vector2(innerMin.x, Mathf.Lerp(innerMin.y, innerMax.y, t)));
                        painter.LineTo(new Vector2(innerMax.x, Mathf.Lerp(innerMin.y, innerMax.y, t)));
                    }
                    painter.Stroke(); return;
                }
                if (gradientType == GradientCreationType.Freeform)
                {
                    const int steps = 16, stride = steps + 1;
                    var field = context.Allocate(stride * stride, steps * steps * 6);
                    for (int y = 0; y <= steps; y++) for (int x = 0; x <= steps; x++)
                    {
                        Vector2 uv = new Vector2(x / (float)steps, y / (float)steps);
                        float distance = Mathf.Min(Vector2.Distance(uv, new Vector2(.22f, .72f)),
                            Mathf.Min(Vector2.Distance(uv, new Vector2(.78f, .72f)), Vector2.Distance(uv, new Vector2(.5f, .22f))));
                        Vector2 position = innerMin + Vector2.Scale(uv, innerMax - innerMin);
                        field.SetNextVertex(new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ),
                            tint = Color.Lerp(light, dark, Mathf.SmoothStep(0, 1, distance / .4f)) });
                    }
                    for (int y = 0; y < steps; y++) for (int x = 0; x < steps; x++)
                    {
                        int i = y * stride + x;
                        field.SetNextIndex((ushort)i); field.SetNextIndex((ushort)(i + 1)); field.SetNextIndex((ushort)(i + stride));
                        field.SetNextIndex((ushort)(i + 1)); field.SetNextIndex((ushort)(i + stride + 1)); field.SetNextIndex((ushort)(i + stride));
                    }
                    return;
                }

                bool angular = gradientType == GradientCreationType.Angular;
                int segments = angular ? 64 : 4;
                var mesh = context.Allocate(segments * 3, segments * 3);
                for (int i = 0; i < segments; i++)
                {
                    Vector2 Edge(int step)
                    {
                        float angle = step * Mathf.PI * 2f / segments + (gradientType == GradientCreationType.Square ? Mathf.PI * .25f : 0f);
                        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        direction /= Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
                        return center + Vector2.Scale(direction, extent);
                    }
                    Color a = angular ? Color.Lerp(light, dark, i / (float)segments) : dark;
                    Color b = angular ? Color.Lerp(light, dark, (i + 1f) / segments) : dark;
                    void VertexAt(Vector2 position, Color color) => mesh.SetNextVertex(new Vertex
                    { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = color });
                    VertexAt(center, angular ? Color.Lerp(a, b, .5f) : light);
                    VertexAt(Edge(i), a);
                    VertexAt(Edge(i + 1), b);
                    mesh.SetNextIndex((ushort)(i * 3)); mesh.SetNextIndex((ushort)(i * 3 + 1)); mesh.SetNextIndex((ushort)(i * 3 + 2));
                }
            }

            private static void TraceRectangle(Painter2D painter, Vector2 min, Vector2 max)
            {
                painter.MoveTo(min); painter.LineTo(new Vector2(max.x, min.y));
                painter.LineTo(max); painter.LineTo(new Vector2(min.x, max.y));
                painter.ClosePath();
            }

            private void DrawUvSelect(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(3, 5)); painter.LineTo(P(11, 3)); painter.LineTo(P(14, 9));
                painter.LineTo(P(9, 15)); painter.LineTo(P(3, 12)); painter.ClosePath(); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(P(17, 12)); painter.LineTo(P(22, 14)); painter.LineTo(P(20, 21));
                painter.LineTo(P(13, 20)); painter.LineTo(P(12, 17)); painter.ClosePath(); painter.Stroke();
                painter.BeginPath(); painter.Arc(P(8, 9), 1.2f, 0, 360); painter.ClosePath(); painter.Fill();
            }

            private void DrawMagnifier(Painter2D painter)
            {
                painter.BeginPath();
                painter.Arc(P(9.5f, 9.5f), contentRect.width * 6.5f / 24f, 0f, 360f);
                painter.ClosePath();
                painter.Stroke();
                painter.lineWidth = 3f;
                painter.BeginPath();
                painter.MoveTo(P(14.5f, 14.5f));
                painter.LineTo(P(21f, 21f));
                painter.Stroke();
            }

            private void DrawEllipseSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                for (int i = 0; i < 8; i++)
                {
                    float start = i * 45f - 13f;
                    float angle = start * Mathf.Deg2Rad;
                    painter.MoveTo(P(12f + Mathf.Cos(angle) * 9f, 12f + Mathf.Sin(angle) * 9f));
                    painter.Arc(P(12f, 12f), contentRect.width * 9f / 24f, start, start + 26f);
                }
                painter.Stroke();
            }
            private void DrawRectangleSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Butt;
                painter.lineJoin = LineJoin.Miter;
                painter.BeginPath();
                painter.MoveTo(P(3, 6)); painter.LineTo(P(3, 3)); painter.LineTo(P(6, 3));
                painter.MoveTo(P(9, 3)); painter.LineTo(P(15, 3));
                painter.MoveTo(P(18, 3)); painter.LineTo(P(21, 3)); painter.LineTo(P(21, 6));
                painter.MoveTo(P(21, 9)); painter.LineTo(P(21, 15));
                painter.MoveTo(P(21, 18)); painter.LineTo(P(21, 21)); painter.LineTo(P(18, 21));
                painter.MoveTo(P(15, 21)); painter.LineTo(P(9, 21));
                painter.MoveTo(P(6, 21)); painter.LineTo(P(3, 21)); painter.LineTo(P(3, 18));
                painter.MoveTo(P(3, 15)); painter.LineTo(P(3, 9));
                painter.Stroke();
            }
            private void DrawPolygonSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(P(14.8f, 16.3f));
                painter.LineTo(P(5f, 13.5f));
                painter.LineTo(P(8.7f, 10.3f));
                painter.LineTo(P(2.5f, 7.1f));
                painter.LineTo(P(11.2f, 2.7f));
                painter.LineTo(P(21.3f, 6.8f));
                painter.LineTo(P(15f, 16.4f));
                painter.Stroke();
                DrawLassoTail(painter);
            }

            private void DrawFreehandSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(P(15f, 16.4f));
                painter.BezierCurveTo(P(12f, 17f), P(5f, 15f), P(4f, 11f));
                painter.BezierCurveTo(P(2f, 5f), P(8f, 2f), P(13f, 3f));
                painter.BezierCurveTo(P(19f, 2f), P(23f, 6f), P(20f, 11f));
                painter.BezierCurveTo(P(19f, 14f), P(17f, 15f), P(15f, 16.4f));
                painter.Stroke();
                DrawLassoTail(painter);
            }

            private void DrawLassoTail(Painter2D painter)
            {
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(P(15f, 16.4f));
                painter.BezierCurveTo(P(12.2f, 15.7f), P(11.5f, 17.5f), P(13.4f, 18.1f));
                painter.BezierCurveTo(P(15.5f, 18.8f), P(18f, 17.3f), P(15f, 16.4f));
                painter.BezierCurveTo(P(14.4f, 18.1f), P(18.3f, 18.8f), P(16.9f, 20.4f));
                painter.BezierCurveTo(P(16.3f, 21.1f), P(14.8f, 21.6f), P(13.3f, 21.6f));
                painter.Stroke();
            }

            private void DrawBucket(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(9.95f, 4.65f));
                painter.BezierCurveTo(P(10.9f, 3.8f), P(12.28f, 3.86f), P(13.28f, 5));
                painter.LineTo(P(20.96f, 13.76f));
                painter.BezierCurveTo(P(21.72f, 14.63f), P(21.5f, 15.1f), P(20.33f, 15.04f));
                painter.LineTo(P(18.66f, 15.01f)); painter.LineTo(P(12.91f, 20.06f));
                painter.BezierCurveTo(P(10.85f, 22), P(8.72f, 22.24f), P(6.86f, 20.13f));
                painter.LineTo(P(2.25f, 14.94f));
                painter.BezierCurveTo(P(1, 13.57f), P(1.37f, 11.95f), P(2.4f, 11.04f));
                painter.ClosePath();
                painter.MoveTo(P(11.41f, 5.93f));
                painter.BezierCurveTo(P(11.65f, 5.88f), P(11.76f, 6.1f), P(11.9f, 6.27f));
                painter.LineTo(P(18.27f, 13.32f));
                painter.QuadraticCurveTo(P(18.37f, 13.44f), P(18.2f, 13.62f));
                painter.LineTo(P(11.76f, 19.22f));
                painter.BezierCurveTo(P(10.51f, 20.33f), P(8.91f, 19.98f), P(7.89f, 18.83f));
                painter.LineTo(P(3.72f, 14.2f));
                painter.BezierCurveTo(P(3.16f, 13.49f), P(3.18f, 12.81f), P(3.84f, 12.22f));
                painter.LineTo(P(10.95f, 6.15f));
                painter.QuadraticCurveTo(P(11.24f, 5.9f), P(11.41f, 5.93f));
                painter.ClosePath(); painter.Fill(FillRule.OddEven);
                painter.BeginPath();
                painter.MoveTo(P(5.2f, 13.2f)); painter.LineTo(P(16.34f, 13.2f));
                painter.BezierCurveTo(P(16.84f, 13.2f), P(17.02f, 13.36f), P(16.64f, 13.72f));
                painter.LineTo(P(11.35f, 18.27f));
                painter.BezierCurveTo(P(10.33f, 19.15f), P(9.16f, 18.99f), P(8.28f, 18.02f));
                painter.LineTo(P(4.9f, 14.2f));
                painter.BezierCurveTo(P(4.38f, 13.6f), P(4.45f, 13.2f), P(5.2f, 13.2f));
                painter.ClosePath(); painter.Fill();
                painter.BeginPath();
                painter.MoveTo(P(20.16f, 16.44f));
                painter.BezierCurveTo(P(20.34f, 16.09f), P(20.7f, 16.09f), P(20.88f, 16.44f));
                painter.BezierCurveTo(P(21.49f, 17.57f), P(22.48f, 19.55f), P(22.48f, 20.59f));
                painter.BezierCurveTo(P(22.48f, 21.75f), P(21.6f, 22.7f), P(20.5f, 22.7f));
                painter.BezierCurveTo(P(19.36f, 22.7f), P(18.46f, 21.78f), P(18.46f, 20.61f));
                painter.BezierCurveTo(P(18.46f, 19.55f), P(19.52f, 17.57f), P(20.16f, 16.44f));
                painter.ClosePath(); painter.Fill();
                painter.lineWidth = 1.8f * contentRect.width / 24;
                painter.BeginPath();
                painter.MoveTo(P(11.73f, 8.43f)); painter.LineTo(P(6.03f, 2.73f));
                painter.BezierCurveTo(P(5.35f, 2.05f), P(4.23f, 2.05f), P(3.55f, 2.73f));
                painter.BezierCurveTo(P(2.87f, 3.41f), P(2.87f, 4.53f), P(3.55f, 5.21f));
                painter.LineTo(P(6.64f, 8.3f)); painter.Stroke();
            }

            private void DrawPointer(Painter2D painter)
            {
                Color ink = resolvedStyle.color;
                painter.fillColor = new Color(ink.r, ink.g, ink.b, ink.a * 0.2f);
                painter.BeginPath();
                painter.MoveTo(P(5f, 2.5f));
                painter.LineTo(P(19f, 12.5f));
                painter.LineTo(P(12.8f, 13.5f));
                painter.LineTo(P(16.3f, 20.1f));
                painter.LineTo(P(12.8f, 21.8f));
                painter.LineTo(P(9.5f, 15.2f));
                painter.LineTo(P(5f, 19f));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }

            private void DrawBrush(Painter2D painter)
            {
                painter.fillColor = resolvedStyle.color;
                painter.BeginPath();
                painter.MoveTo(P(10.6788f, 9.5538f));
                painter.BezierCurveTo(P(13.3108f, 7.5569f), P(18.4064f, 3.4138f), P(19.8064f, 2.0138f));
                painter.BezierCurveTo(P(21.2596f, .5606f), P(23.4394f, 2.7404f), P(21.9862f, 4.1936f));
                painter.BezierCurveTo(P(20.5862f, 5.5936f), P(16.4431f, 10.6892f), P(14.4462f, 13.3212f));
                painter.BezierCurveTo(P(13.3968f, 11.4047f), P(12.5953f, 10.6032f), P(10.6788f, 9.5538f));
                painter.ClosePath();
                painter.MoveTo(P(10.0939f, 10.3283f));
                painter.BezierCurveTo(P(11.7831f, 11.3378f), P(12.9751f, 12.7642f), P(13.9061f, 14.3982f));
                painter.LineTo(P(12.6846f, 16.2940f));
                painter.BezierCurveTo(P(11.4771f, 13.9615f), P(10.1410f, 12.8363f), P(8.0341f, 11.5966f));
                painter.ClosePath();
                painter.MoveTo(P(7.2375f, 12.6f));
                painter.BezierCurveTo(P(3.8375f, 12f), P(3.3375f, 15.4f), P(3.6375f, 17.1f));
                painter.BezierCurveTo(P(3.9375f, 19.2f), P(2.2375f, 20.7f), P(1.2375f, 21.4f));
                painter.BezierCurveTo(P(5.8375f, 22.6f), P(11.1375f, 20.9f), P(11.7375f, 18.4f));
                painter.BezierCurveTo(P(12.375f, 16.1016f), P(9.3047f, 12.8906f), P(7.2375f, 12.6f));
                painter.ClosePath();
                painter.MoveTo(P(6.5375f, 14.6f));
                painter.BezierCurveTo(P(5.3375f, 16f), P(6.4375f, 17.3f), P(4.5375f, 19.6f));
                painter.BezierCurveTo(P(7.4375f, 18.3f), P(6.6375f, 16.6f), P(6.5375f, 14.6f));
                painter.ClosePath();
                painter.Fill(FillRule.OddEven);
            }

            private static readonly Vector2[] BlurBrushOutline = CreateBlurBrushOutline();

            private static Vector2[] CreateBlurBrushOutline()
            {
                const int steps = 16;
                var points = new Vector2[steps * 4];
                void Curve(int segment, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
                {
                    for (int i = 0; i < steps; i++)
                    {
                        float t = i / (float)steps;
                        float u = 1f - t;
                        points[segment * steps + i] = u * u * u * a + 3f * u * u * t * b
                            + 3f * u * t * t * c + t * t * t * d;
                    }
                }
                Curve(0, new Vector2(12f, 2.2f), new Vector2(10.2f, 5.1f), new Vector2(5.1f, 10.2f), new Vector2(5.1f, 14.1f));
                Curve(1, new Vector2(5.1f, 14.1f), new Vector2(5.1f, 18.5f), new Vector2(8.1f, 21.6f), new Vector2(12f, 21.6f));
                Curve(2, new Vector2(12f, 21.6f), new Vector2(15.9f, 21.6f), new Vector2(18.9f, 18.5f), new Vector2(18.9f, 14.1f));
                Curve(3, new Vector2(18.9f, 14.1f), new Vector2(18.9f, 10.2f), new Vector2(13.8f, 5.1f), new Vector2(12f, 2.2f));
                return points;
            }

            private void DrawBlurBrush(MeshGenerationContext context)
            {
                Color ink = resolvedStyle.color;
                var mesh = context.Allocate(BlurBrushOutline.Length + 1, BlurBrushOutline.Length * 3);
                void AddVertex(Vector2 point)
                {
                    Vector2 position = P(point.x, point.y);
                    Color tint = ink;
                    tint.a *= Mathf.Lerp(0.015f, 0.55f, Mathf.InverseLerp(2f, 22f, point.y));
                    mesh.SetNextVertex(new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = tint });
                }
                AddVertex(new Vector2(12f, 14.1f));
                foreach (Vector2 point in BlurBrushOutline) AddVertex(point);
                for (int i = 0; i < BlurBrushOutline.Length; i++)
                {
                    mesh.SetNextIndex(0);
                    mesh.SetNextIndex((ushort)((i + 1) % BlurBrushOutline.Length + 1));
                    mesh.SetNextIndex((ushort)(i + 1));
                }
                Painter2D painter = context.painter2D;
                painter.strokeColor = ink;
                painter.lineWidth = 1.35f;
                painter.BeginPath();
                painter.MoveTo(P(12f, 2.2f));
                painter.BezierCurveTo(P(10.2f, 5.1f), P(5.1f, 10.2f), P(5.1f, 14.1f));
                painter.BezierCurveTo(P(5.1f, 18.5f), P(8.1f, 21.6f), P(12f, 21.6f));
                painter.BezierCurveTo(P(15.9f, 21.6f), P(18.9f, 18.5f), P(18.9f, 14.1f));
                painter.BezierCurveTo(P(18.9f, 10.2f), P(13.8f, 5.1f), P(12f, 2.2f));
                painter.ClosePath();
                painter.Stroke();
            }

            private void DrawSmudgeBrush(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(12.27f, 2.02f));
                painter.BezierCurveTo(P(9.35f, 2.28f), P(3.33f, 2.92f), P(3.45f, 6.96f));
                painter.BezierCurveTo(P(3.51f, 9.06f), P(4.25f, 11.24f), P(5.42f, 12.32f));
                painter.BezierCurveTo(P(5.75f, 12.64f), P(6.30f, 12.62f), P(6.55f, 12.75f));
                painter.BezierCurveTo(P(6.20f, 13.25f), P(5.75f, 13.76f), P(5.31f, 14.39f));
                painter.BezierCurveTo(P(4.48f, 15.57f), P(3.53f, 16.94f), P(2.72f, 18.18f));
                painter.BezierCurveTo(P(2.01f, 19.26f), P(1.58f, 20.36f), P(2.29f, 21.14f));
                painter.BezierCurveTo(P(3.00f, 21.92f), P(4.02f, 21.57f), P(4.81f, 20.80f));
                painter.BezierCurveTo(P(6.67f, 18.98f), P(8.04f, 16.59f), P(10.54f, 14.98f));
                painter.BezierCurveTo(P(12.76f, 13.55f), P(18.56f, 10.15f), P(16.56f, 15.12f));
                painter.BezierCurveTo(P(15.85f, 16.89f), P(12.56f, 15.10f), P(11.79f, 16.75f));
                painter.BezierCurveTo(P(10.03f, 20.55f), P(17.43f, 18.50f), P(18.76f, 17.75f));
                painter.BezierCurveTo(P(21.84f, 16.01f), P(22.40f, 11.77f), P(22.35f, 8.65f));
                painter.BezierCurveTo(P(22.33f, 7.50f), P(21.74f, 6.57f), P(20.80f, 5.91f));
                painter.BezierCurveTo(P(18.10f, 4.02f), P(15.35f, 3.01f), P(12.27f, 2.02f));
                painter.ClosePath();
                painter.Fill();
            }

            private void DrawHealingBrush(Painter2D painter)
            {
                const float diagonal = 0.70710678f;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 center = this.LocalToWorld(contentRect.center) * pixelsPerPoint;
                center = this.WorldToLocal(new Vector2(Mathf.Floor(center.x) + 0.5f,
                    Mathf.Floor(center.y) + 0.5f) / pixelsPerPoint);
                float unit = Mathf.Min(contentRect.width, contentRect.height) * (20f / 22f)
                    / (2f * (9.09f * diagonal + 4.05f));
                Vector2 Point(float along, float across) => center +
                    new Vector2((along + across) * diagonal, (across - along) * diagonal) * unit;

                void Wing(float direction)
                {
                    painter.MoveTo(Point(direction * 5.07f, -4.05f));
                    painter.LineTo(Point(direction * 9.09f, -4.05f));
                    painter.Arc(Point(direction * 9.09f, 0f), 4.05f * unit,
                        -135f, direction > 0f ? 45f : -315f,
                        direction > 0f ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
                    painter.LineTo(Point(direction * 5.07f, 4.05f));
                    painter.ClosePath();
                }

                void Corner(float along, float across, float start, float end)
                    => painter.Arc(Point(along, across), 1.11f * unit, start, end);

                void Pore(float along, float across)
                {
                    Vector2 point = Point(along, across);
                    painter.MoveTo(point + Vector2.right * (1.07f * unit));
                    painter.Arc(point, 1.07f * unit, 0f, 360f);
                    painter.ClosePath();
                }

                Color ink = resolvedStyle.color;
                ink.a = 1f;
                painter.fillColor = ink;
                painter.BeginPath();
                Wing(1f);
                Wing(-1f);
                painter.MoveTo(Point(-3.22f, -4.75f));
                painter.LineTo(Point(3.22f, -4.75f));
                Corner(3.22f, -3.64f, -135f, -45f);
                painter.LineTo(Point(4.33f, 3.64f));
                Corner(3.22f, 3.64f, -45f, 45f);
                painter.LineTo(Point(-3.22f, 4.75f));
                Corner(-3.22f, 3.64f, 45f, 135f);
                painter.LineTo(Point(-4.33f, -3.64f));
                Corner(-3.22f, -3.64f, 135f, 225f);
                painter.ClosePath();
                Pore(-2.2f, -2.2f);
                Pore(-2.2f, 2.2f);
                Pore(2.2f, -2.2f);
                Pore(2.2f, 2.2f);
                Pore(0f, 0f);
                painter.Fill(FillRule.OddEven);
            }

            private void DrawPencil(Painter2D painter)
            {
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                float unit = Mathf.Min(contentRect.width, contentRect.height) / 22f;
                Vector2 origin = this.LocalToWorld(contentRect.center - Vector2.one * (12f * unit)) * pixelsPerPoint;
                origin = this.WorldToLocal(new Vector2(Mathf.Round(origin.x), Mathf.Round(origin.y)) / pixelsPerPoint);
                Vector2 Point(float x, float y) => origin + new Vector2(x, y) * unit;
                painter.BeginPath();
                painter.MoveTo(Point(17f, 2.4f));
                painter.BezierCurveTo(Point(17.9f, 1.5f), Point(19.19f, 1.59f), Point(20.225f, 2.625f));
                painter.LineTo(Point(21.375f, 3.775f));
                painter.BezierCurveTo(Point(22.41f, 4.81f), Point(22.5f, 6.1f), Point(21.6f, 7f));
                painter.LineTo(Point(20.5f, 8.1f));
                painter.LineTo(Point(19.21f, 8.01f));
                painter.LineTo(Point(15.99f, 4.79f));
                painter.LineTo(Point(15.9f, 3.5f));
                painter.ClosePath();
                painter.MoveTo(Point(13.6f, 5.8f));
                painter.LineTo(Point(14.2f, 5.8f));
                painter.LineTo(Point(18.2f, 9.8f));
                painter.LineTo(Point(18.2f, 10.4f));
                painter.LineTo(Point(7.9f, 20.7f));
                painter.LineTo(Point(7.2f, 21f));
                painter.LineTo(Point(3.6f, 21f));
                painter.LineTo(Point(3f, 20.4f));
                painter.LineTo(Point(3f, 16.8f));
                painter.LineTo(Point(3.3f, 16.1f));
                painter.ClosePath();
                painter.MoveTo(Point(4f, 16.8f));
                painter.LineTo(Point(5.1f, 15.7f));
                painter.LineTo(Point(8.3f, 18.9f));
                painter.LineTo(Point(7.2f, 20f));
                painter.LineTo(Point(5f, 20f));
                painter.LineTo(Point(5.95f, 19.35f));
                painter.LineTo(Point(4.65f, 18.05f));
                painter.LineTo(Point(4f, 19f));
                painter.ClosePath();
                painter.Fill(FillRule.OddEven);
            }

            private void DrawTransform(Painter2D painter)
            {
                float ppi = EditorGUIUtility.pixelsPerPoint;
                Vector2 Point(float x, float y) => new Vector2(
                    contentRect.x + x * contentRect.width / 22f,
                    contentRect.y + y * contentRect.height / 22f);
                Vector2 Pixel(float x, float y)
                {
                    Vector2 world = this.LocalToWorld(Point(x, y)) * ppi;
                    return this.WorldToLocal(new Vector2(Mathf.Round(world.x), Mathf.Round(world.y)) / ppi);
                }

                void Box(float x0, float y0, float x1, float y1)
                {
                    painter.BeginPath();
                    TraceRectangle(painter, Pixel(x0, y0), Pixel(x1, y1));
                    painter.Fill();
                }
                Box(5, 3, 17, 4);
                Box(3, 5, 4, 17);
                Box(18, 5, 19, 11);
                Box(5, 18, 11, 19);
                Box(2, 2, 5, 5);
                Box(17, 2, 20, 5);
                Box(2, 17, 5, 20);

                float radius = 1.25f * Mathf.Min(contentRect.width, contentRect.height) / 22f;
                void Dot(float x, float y)
                {
                    painter.BeginPath();
                    painter.Arc(Point(x, y), radius, 0, 360);
                    painter.ClosePath();
                    painter.Fill();
                }
                Dot(11, 3.5f);
                Dot(3.5f, 11);
                Dot(18.5f, 11);
                Dot(11, 18.5f);

                painter.BeginPath();
                painter.MoveTo(Point(11.25f, 10.6f));
                painter.LineTo(Point(20.8f, 14.8f));
                painter.BezierCurveTo(Point(21.15f, 14.96f), Point(21.1f, 15.4f), Point(20.72f, 15.49f));
                painter.LineTo(Point(16.62f, 16.44f));
                painter.BezierCurveTo(Point(16.52f, 16.46f), Point(16.46f, 16.52f), Point(16.44f, 16.62f));
                painter.LineTo(Point(15.49f, 20.72f));
                painter.BezierCurveTo(Point(15.4f, 21.1f), Point(14.96f, 21.15f), Point(14.8f, 20.8f));
                painter.LineTo(Point(10.6f, 11.25f));
                painter.BezierCurveTo(Point(10.41f, 10.82f), Point(10.82f, 10.41f), Point(11.25f, 10.6f));
                painter.ClosePath();
                painter.Fill();
            }

            private void DrawHand(Painter2D painter, bool withHandle = false)
            {
                if (withHandle)
                {
                    painter.BeginPath();
                    painter.Arc(P(7f, 6.5f), 4.6f * contentRect.width / 24f, 120f, 325f);
                    painter.Stroke();
                }
                Vector2 HandPoint(float x, float y) => withHandle ? P(x * .8f + 4.5f, y * .8f + 4f) : P(x, y);
                painter.BeginPath();
                painter.MoveTo(HandPoint(7f, 12.2f));
                painter.LineTo(HandPoint(7f, 6f));
                painter.BezierCurveTo(HandPoint(7f, 3.8f), HandPoint(10f, 3.8f), HandPoint(10f, 6f));
                painter.LineTo(HandPoint(10f, 11f));
                painter.LineTo(HandPoint(10f, 3.8f));
                painter.BezierCurveTo(HandPoint(10f, 1.6f), HandPoint(13f, 1.6f), HandPoint(13f, 3.8f));
                painter.LineTo(HandPoint(13f, 11f));
                painter.LineTo(HandPoint(13f, 5f));
                painter.BezierCurveTo(HandPoint(13f, 2.8f), HandPoint(16f, 2.8f), HandPoint(16f, 5f));
                painter.LineTo(HandPoint(16f, 11.8f));
                painter.LineTo(HandPoint(16f, 8f));
                painter.BezierCurveTo(HandPoint(16f, 5.8f), HandPoint(19f, 5.8f), HandPoint(19f, 8f));
                painter.LineTo(HandPoint(19f, 14f));
                painter.BezierCurveTo(HandPoint(19f, 18f), HandPoint(17f, 18.5f), HandPoint(17f, 21f));
                painter.LineTo(HandPoint(9f, 21f));
                painter.BezierCurveTo(HandPoint(9f, 18.7f), HandPoint(7f, 18f), HandPoint(5.8f, 16f));
                painter.LineTo(HandPoint(3.5f, 12.5f));
                painter.BezierCurveTo(HandPoint(2f, 10f), HandPoint(4.3f, 9.2f), HandPoint(5.8f, 11f));
                painter.LineTo(HandPoint(7f, 12.2f));
                painter.ClosePath();
                painter.Stroke();
            }
        }

        private static void DrawVectorToolIcon(Painter2D p, CanvasTool tool, Func<float, float, Vector2> point)
        {
            if (tool == CanvasTool.VectorNodes)
            {
                p.BeginPath(); p.MoveTo(point(5, 3)); p.LineTo(point(18, 13)); p.LineTo(point(12, 14)); p.LineTo(point(9, 21)); p.ClosePath();
                p.Stroke();
            }
            else
            {
                p.BeginPath(); p.MoveTo(point(3, 19)); p.BezierCurveTo(point(4, 3), point(19, 22), point(21, 5)); p.Stroke();
                if (tool == CanvasTool.VectorBrush) { p.lineWidth = 4; p.BeginPath(); p.MoveTo(point(7, 8)); p.LineTo(point(15, 16)); p.Stroke(); }
                else foreach (var xy in new[] { point(3, 19), point(21, 5) })
                { p.BeginPath(); p.Arc(xy, 2, 0, 360); p.Fill(); }
            }
        }
    }
}
