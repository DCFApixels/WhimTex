using System;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class StrokeStyleEditor : VisualElement
    {
        internal StrokeStyleEditor(Func<StrokeStyle> read, Action<StrokeStyle> write, StrokeDashEditor dashes,
            Action<string, Action> change, WhimTexUI.ValueBindings bindings)
        {
            name = "strokeOptions";
            var position = WhimTexUI.ConfigureField(new StrokeStyleField<VectorStrokePosition>("Position",
                (VectorStrokePosition.Inside, LayerActionIcon.Kind.StrokeInside, "Inside\nPlace the stroke inside closed contours. Open paths stay centered."),
                (VectorStrokePosition.Center, LayerActionIcon.Kind.StrokeCenter, "Center\nPlace half the stroke on each side of the contour."),
                (VectorStrokePosition.Outside, LayerActionIcon.Kind.StrokeOutside, "Outside\nPlace the stroke outside closed contours. Open paths stay centered.")));
            Add(position); bindings.Track(position, () => read().strokePosition);
            position.RegisterValueChangedCallback(e => change("Change Stroke Position", () => { var s = read(); s.strokePosition = e.newValue; write(s); }));
            var join = WhimTexUI.ConfigureField(new StrokeStyleField<VectorStrokeJoin>("Join",
                (VectorStrokeJoin.Miter, LayerActionIcon.Kind.StrokeJoinMiter, "Miter\nExtend the sides to a sharp corner, limited by Miter Limit."),
                (VectorStrokeJoin.Round, LayerActionIcon.Kind.StrokeJoinRound, "Round\nRound off the outer corner."),
                (VectorStrokeJoin.Bevel, LayerActionIcon.Kind.StrokeJoinBevel, "Bevel\nCut the outer corner with a straight edge.")));
            Add(join); bindings.Track(join, () => read().join);
            join.RegisterValueChangedCallback(e => change("Change Stroke Join", () => { var s = read(); s.join = e.newValue; write(s); }));
            var cap = WhimTexUI.ConfigureField(new StrokeStyleField<VectorStrokeCap>("Cap",
                (VectorStrokeCap.Butt, LayerActionIcon.Kind.StrokeCapButt, "Butt\nEnd exactly at the dash or open path endpoint."),
                (VectorStrokeCap.Round, LayerActionIcon.Kind.StrokeCapRound, "Round\nAdd a semicircle beyond the endpoint."),
                (VectorStrokeCap.Square, LayerActionIcon.Kind.StrokeCapSquare, "Square\nExtend a square end by half the stroke width.")));
            Add(cap); bindings.Track(cap, () => read().cap);
            cap.RegisterValueChangedCallback(e => change("Change Stroke Cap", () => { var s = read(); s.cap = e.newValue; write(s); }));
            var limit = WhimTexUI.ConfigureField(new FloatField("Miter Limit") { name = "strokeMiterLimit" });
            Add(limit); bindings.Track(limit, () => read().miterLimit);
            limit.RegisterValueChangedCallback(e => change("Change Stroke Miter Limit", () =>
            { var s = read(); s.miterLimit = ShapeLayerBehaviour.Limit(e.newValue, 1, 10000, s.miterLimit); write(s); }));
            bindings.Add(() => limit.SetEnabled(read().join == VectorStrokeJoin.Miter));
            Add(dashes); bindings.Add(dashes.Refresh);
            var offset = WhimTexUI.ConfigureField(new FloatField("Dash Offset (px)") { name = "strokeDashOffset" });
            Add(offset); bindings.Track(offset, () => read().dashOffset);
            offset.RegisterValueChangedCallback(e => change("Change Stroke Dash Offset", () =>
            { var s = read(); s.dashOffset = ShapeLayerBehaviour.Limit(e.newValue, -100000, 100000, s.dashOffset); write(s); }));
        }
    }
}
