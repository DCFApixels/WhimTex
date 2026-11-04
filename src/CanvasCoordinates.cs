using UnityEngine;

namespace DCFApixels.WhimTex
{
    // View coordinates are UI points; document pixels/UV use bottom-left Y.
    internal readonly struct CanvasCoordinates
    {
        readonly CanvasViewport viewport;
        readonly Rect view, image;
        readonly Vector2 size;
        internal CanvasCoordinates(CanvasViewport viewport, Rect view, Rect image, Vector2 size)
        { this.viewport = viewport; this.view = view; this.image = image; this.size = size; }
        internal Vector2 UvToView(Vector2 uv) => viewport.ToView(view, UvToCanvas(uv, image));
        internal Vector2 ViewToUv(Vector2 point) => CanvasToUv(viewport.ToCanvas(view, point), image);
        internal Vector2 DocumentToView(Vector2 point) => viewport.ToView(view, DocumentToCanvas(point, image, size));
        internal Vector2 ViewToDocument(Vector2 point) => CanvasToDocument(viewport.ToCanvas(view, point), image, size);
        internal static Vector2 UvToCanvas(Vector2 uv, Rect image) =>
            new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height);
        internal static Vector2 CanvasToUv(Vector2 point, Rect image) =>
            new Vector2((point.x - image.x) / image.width, (image.yMax - point.y) / image.height);
        internal static Vector2 DocumentToCanvas(Vector2 point, Rect image, Vector2 size) =>
            UvToCanvas(new Vector2(point.x / size.x, point.y / size.y), image);
        internal static Vector2 CanvasToDocument(Vector2 point, Rect image, Vector2 size) =>
            Vector2.Scale(CanvasToUv(point, image), size);
    }
}
