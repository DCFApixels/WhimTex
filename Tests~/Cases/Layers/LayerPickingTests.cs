using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Independent port: complete original body, assertion inputs and finally cleanup retained.
public static class LayerPickingTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("LayerPickingTests", Body);
    static void Body()
    {
        // Run through Unity Pipeline eval_file. Transient document only; no user windows, assets or preferences are changed.
        var document = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexDocument>());
        document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        document.width = document.height = 32;
        var method = document.GetType().GetMethod("PickLayerAtPixel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        int checks = 0;
        void Check(bool ok, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, message); checks++; }
        DCFApixels.WhimTex.Layer Pick(int x = 16, int y = 16, float threshold = .1f, bool deep = false, DCFApixels.WhimTex.Layer selected = null) =>
            (DCFApixels.WhimTex.Layer)method.Invoke(document, new object[] { x, y, threshold, deep, selected });
        DCFApixels.WhimTex.Layer NewLayer(DCFApixels.WhimTex.LayerBehaviour behaviour)
        {
            var layer = new DCFApixels.WhimTex.Layer(behaviour);
            typeof(DCFApixels.WhimTex.Layer).GetMethod("AssignNewId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(layer, null);
            return layer;
        }
        DCFApixels.WhimTex.Layer Fill(float alpha) => NewLayer(new DCFApixels.WhimTex.ColorFillLayerBehaviour
            { color = new UnityEngine.Color(1f, 1f, 1f, alpha) });
        var back = Fill(1f); var front = Fill(.1f);
        document.layers.Add(front); document.layers.Add(back);
        try
        {
            Check(Pick() == front, "10% inclusive, including half-float roundoff");
            Check(Pick(threshold: .11f) == back, "alpha below threshold passes through");
            front.enabled = false; Check(Pick() == back, "hidden layer ignored"); front.enabled = true;
            front.opacity = .5f; Check(Pick() == back, "layer opacity included"); front.opacity = 1f;
            front.blendMode = DCFApixels.WhimTex.BlendMode.None; Check(Pick() == back, "None blend ignored");
            front.blendMode = DCFApixels.WhimTex.BlendMode.Normal;
            Check(Pick(-1, 0) == null && Pick(32, 0) == null, "outside canvas");
            ((DCFApixels.WhimTex.ColorFillLayerBehaviour)front.Behaviour).color = UnityEngine.Color.clear;
            Check(Pick(threshold: 0f) == back, "zero threshold still skips transparent pixels");
            front.channelMapping = new DCFApixels.WhimTex.LayerChannelMapping();
            ((DCFApixels.WhimTex.ColorFillLayerBehaviour)front.Behaviour).color = UnityEngine.Color.white;
            var transform = DCFApixels.WhimTex.TextureTransform.Default;
            transform.scale = new UnityEngine.Vector2(.25f, .5f); transform.rotation = 90f;
            front.transform = transform;
            Check(Pick() == front && Pick(2, 2) == back, "transformed shape coverage");
            Check(Pick(threshold: 1f) == front, "fully opaque at 100%");
            var channelMapping = new DCFApixels.WhimTex.LayerChannelMapping();
            channelMapping[3] = DCFApixels.WhimTex.ChannelMappingSource.Zero; front.channelMapping = channelMapping;
            Check(Pick() == back, "ChannelMapping alpha is part of coverage"); front.channelMapping = default;
            front.transform = DCFApixels.WhimTex.TextureTransform.Default;
            var group = NewLayer(new DCFApixels.WhimTex.GroupLayerBehaviour());
            ((DCFApixels.WhimTex.GroupLayerBehaviour)group.Behaviour).layers.Add(front);
            document.layers[0] = group;
            Check(Pick() == group, "default picks group");
            Check(Pick(deep: true) == front, "Ctrl picks nested child");
            group.opacity = .05f;
            Check(Pick(deep: true) == back, "group opacity gates nested children");
            group.opacity = .5f; front.opacity = .15f;
            Check(Pick(deep: true) == back, "parent and child opacity multiply");
            front.opacity = .2f; Check(Pick(deep: true) == front, "combined 10% inclusive");
            group.enabled = false; Check(Pick(deep: true) == back, "hidden parent gates children"); group.enabled = true;
            group.opacity = front.opacity = 1f;
            group.clippingMask = true;
            var mask = Fill(.5f);
            document.layers.Insert(1, mask);
            front.opacity = .15f;
            Check(Pick(deep: true) == mask, "clipped group applies base alpha to descendants");
            Check(Pick(selected: group) == mask, "repeat group picking includes clipping alpha");
            front.opacity = .2f; Check(Pick(deep: true) == front, "clipped group exact threshold");
            Check(Pick(selected: group) == front, "repeat group picking includes exact clipped threshold");
            mask.enabled = false; Check(Pick(deep: true) == back, "hidden clipping base suppresses chain"); mask.enabled = true;
            mask.transform = transform; Check(Pick(2, 2, deep: true) == back, "clipped group outside base shape");
            mask.transform = DCFApixels.WhimTex.TextureTransform.Default;
            mask.opacity = .1f; Check(Pick(deep: true) == back, "base opacity included"); mask.opacity = 1f;
            group.clippingMask = false; document.layers.Remove(mask);
            front.opacity = 1f;
            var childGroup = NewLayer(new DCFApixels.WhimTex.GroupLayerBehaviour());
            ((DCFApixels.WhimTex.GroupLayerBehaviour)childGroup.Behaviour).layers.Add(front);
            ((DCFApixels.WhimTex.GroupLayerBehaviour)group.Behaviour).layers[0] = childGroup;
            Check(Pick(deep: true) == front && Pick() == group, "nested groups");
            Check(Pick(selected: group) == childGroup, "repeat click enters only one group level");
            Check(Pick(selected: childGroup) == front, "next click enters nested group");
            Check(Pick(selected: front) == front, "selected child does not bounce back to its parent");
            Check(Pick(selected: back) == group, "unrelated selection does not enter group");
            Check(Pick(selected: NewLayer(new DCFApixels.WhimTex.GroupLayerBehaviour())) == group, "detached selection ignored");
            front.enabled = false;
            Check(Pick(selected: childGroup) == back, "repeat picking skips hidden content"); front.enabled = true;
            group.opacity = .05f;
            Check(Pick(selected: childGroup) == back, "repeat picking respects ancestor opacity"); group.opacity = 1f;
            front.clippingMask = true; Check(Pick(deep: true) == back, "orphan clip is invisible"); front.clippingMask = false;
            string before = UnityEditor.EditorJsonUtility.ToJson(document);
            for (int i = 0; i < 5; i++) Check(Pick(deep: true) == front, "repeatable picking");
            Check(before == UnityEditor.EditorJsonUtility.ToJson(document), "picking does not mutate document");
            var outline = new DCFApixels.WhimTex.OutlineLayerBehaviour { inputMode = DCFApixels.WhimTex.EffectInputMode.Specific,
                TargetLayerId = front.Id, outlineWidth = 3f, outlineSoftness = 0f };
            var outlineLayer = NewLayer(outline);
            document.layers.Insert(0, outlineLayer);
            front.transform = transform; front.enabled = false;
            Check(Pick(6, 16) == outlineLayer, "effect pixels outside a hidden target are selectable");
            outlineLayer.enabled = false; Check(Pick(6, 16) == back, "hidden effect is ignored");
            document.layers.Remove(outlineLayer);
            front.enabled = true; front.transform = DCFApixels.WhimTex.TextureTransform.Default;
            VectorGeometryPicking();
            document.layers.Clear(); Check(Pick() == null, "empty document");
            document.layers.Add(group); document.layers.Add(back);
            return;
        }
        finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(document); }

        void VectorGeometryPicking()
        {
            var pickGeometry = document.GetType().GetMethod("PickLayerAtPixelWithGeometry", BindingFlags.NonPublic | BindingFlags.Instance);
            var hitGeometry = typeof(WhimTexWindow).GetMethod("PickVectorGeometry", BindingFlags.NonPublic | BindingFlags.Static);
            var contour = new VectorContour();
            foreach (var p in new[] { new Vector2(.1f, .1f), new Vector2(.9f, .1f), new Vector2(.9f, .9f), new Vector2(.1f, .9f) })
                contour.points.Add(new VectorPoint(p));
            var figure = new VectorShape { fillColor = Color.clear, contours = new List<VectorContour> { contour } };
            var vector = new VectorLayerBehaviour { referenceSize = new Vector2(32, 32), shapes = new List<VectorShape> { figure } };
            var vectorLayer = NewLayer(vector);
            Vector2 point = new Vector2(50, 50);
            Func<Vector2, Vector2> map = p => p * 100;
            bool Hit(Layer layer) => layer == vectorLayer && hitGeometry.Invoke(null, new object[] { vector, point, map }) != null;
            Layer GeometryPick(bool deep = false, Layer selected = null) => (Layer)pickGeometry.Invoke(document,
                new object[] { 16, 16, .1f, deep, (Func<Layer, bool>)Hit, selected });
            document.layers.Clear(); document.layers.Add(vectorLayer); document.layers.Add(back);
            Check(Pick() == back, "Raster selection still ignores a transparent fill");
            Check(GeometryPick() == vectorLayer, "Layer Select can pick transparent Vector fill by geometry");
            var before = EditorJsonUtility.ToJson(document);
            point = new Vector2(6, 50); Check(GeometryPick() == vectorLayer, "Contour has six view-point hit tolerance");
            point = new Vector2(3, 50); Check(GeometryPick() == back, "Outside geometry and hit tolerance passes through");
            point = new Vector2(50, 50);
            var hole = contour.Clone(false);
            foreach (var p in hole.points) p.position = Vector2.one * .5f + (p.position - Vector2.one * .5f) * .5f;
            figure.contours.Add(hole);
            Check(GeometryPick() == back, "Even-odd holes remain unselected");
            figure.fillRule = VectorFillRule.NonZero;
            Check(GeometryPick() == vectorLayer, "Nonzero winding uses contour orientation");
            figure.contours.Remove(hole); figure.fillRule = VectorFillRule.EvenOdd;
            contour.closed = false; figure.fillMode = VectorFillMode.None;
            point = new Vector2(50, 11); Check(GeometryPick() == vectorLayer, "Unstroked open paths are selectable along their geometry");
            point = new Vector2(11, 50); Check(GeometryPick() == back, "Open paths do not acquire a closing edge");
            contour.closed = true; figure.fillMode = VectorFillMode.Solid; point = new Vector2(50, 50);
            map = p => new Vector2(160 - 2 * p.y * 100, 20 + 2 * p.x * 100);
            point = map(new Vector2(.5f, .5f)); Check(GeometryPick() == vectorLayer, "Geometry hit follows rotation, translation and zoom");
            map = p => p * 100; point = new Vector2(50, 50);
            document.layers.Insert(0, front); Check(GeometryPick() == front, "Raster foreground keeps stack priority"); document.layers.Remove(front);
            vectorLayer.enabled = false; Check(GeometryPick() == back, "Hidden Vector is not selectable"); vectorLayer.enabled = true;
            figure.visible = false; Check(GeometryPick() == back, "Hidden figure is not selectable"); figure.visible = true;
            var group = NewLayer(new GroupLayerBehaviour()); group.children.Add(vectorLayer); document.layers[0] = group;
            Check(GeometryPick() == group, "Geometry inside an ordinary group picks its group first");
            Check(GeometryPick(selected: group) == vectorLayer && GeometryPick(true) == vectorLayer, "Repeated click and Ctrl enter geometry-only children");
            group.opacity = .05f; Check(GeometryPick(true) == back, "Group opacity still gates geometry selection"); group.opacity = 1;
            var clip = Fill(0); document.layers.Insert(1, clip); group.clippingMask = true;
            Check(GeometryPick(true) == back, "Clipping base still gates geometry selection");
            document.layers.Remove(clip); group.clippingMask = false;
            document.layers[0] = vectorLayer;
            Check(before == EditorJsonUtility.ToJson(document), "Geometry picking does not rewrite vector content or transforms");
        }
        
    }
}
