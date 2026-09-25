using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ApplyFXSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Main()
    {
        int checks = 0;
        var owned = new List<UnityEngine.Object>();
        var documents = new List<TextureCompositor>();
        string folder = null;
        void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; }
        object Call(object target, string method, params object[] args)
        {
            try { return target.GetType().GetMethod(method, F).Invoke(target, args); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }
        TextureCompositor Document()
        {
            var doc = ScriptableObject.CreateInstance<TextureCompositor>();
            doc.width = 64; doc.height = 48;
            documents.Add(doc); return doc;
        }
        ShaderFX FX(TextureCompositor doc, string body)
        {
            var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            var fx = (ShaderFX)create.Invoke(null, new object[] { doc,
                "float4 ApplyFX(float2 uv, float4 color) { " + body + " }", new List<ShaderFXParameter>() });
            owned.Add(fx); Call(fx, "ApplyAgentDraft");
            Check(typeof(ShaderFX).GetField("compiledShader", F).GetValue(fx) != null, "FX compiled");
            return fx;
        }
        Color[] Pixels(TextureCompositor doc)
        {
            var tex = doc.Compose();
            try { return tex.GetPixels(); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }
        void Same(Color[] a, Color[] b, string message)
        {
            float max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                max = Mathf.Max(max, Mathf.Abs(a[i].r * a[i].a - b[i].r * b[i].a),
                    Mathf.Abs(a[i].g * a[i].a - b[i].g * b[i].a), Mathf.Abs(a[i].b * a[i].a - b[i].b * b[i].a),
                    Mathf.Abs(a[i].a - b[i].a));
            }
            Check(max < .006f, message + ": max premultiplied difference " + max);
        }
        List<Layer> Children(Layer group) => (List<Layer>)typeof(Layer).GetProperty("layers", F).GetValue(group);
        try
        {
            var doc = Document();
            Layer layer = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.UV };
            layer.transform = TextureTransform.Default;
            layer.transform.position = new Vector2(.07f, -.04f);
            layer.transform.scale = new Vector2(.7f, .8f);
            layer.transform.rotation = 14;
            layer.opacity = .72f;
            doc.layers.Add(layer);
            layer.modifiers.Add(FX(doc, "color.rgb *= 2; return color;"));
            layer.modifiers.Add(FX(doc, "color.rgb -= .25; return color;"));
            var tail = FX(doc, "color.rgb *= .6 + .2 * LayerToLocal(uv).x; color.a *= .8; return color;");
            layer.modifiers.Add(tail);
            var originalTransform = layer.transform;
            var before = Pixels(doc);
            string id = layer.Id;
            Call(doc, "ApplyLayerFX", layer, 1);
            Check(ReferenceEquals(doc.layers[0], layer) && layer.Id == id, "Stable layer wrapper and ID");
            Check(layer.Behaviour is DrawingLayerBehaviour && layer.modifiers.Count == 1 && layer.modifiers[0] == tail,
                "Prefix removed and tail retained");
            Check(layer.transform.Equals(originalTransform) && layer.opacity == .72f, "Transform and opacity unchanged");
            Same(before, Pixels(doc), "Transformed cascade with local-coordinate tail preserves composite");
            var paintTransform = (TextureTransform)Call(doc, "GetPaintTransform", layer);
            Check(Vector2.Distance(paintTransform.Map(new Vector2(.2f, .7f), new Vector2(64, 48)), new Vector2(.2f, .7f)) < .0001f,
                "Painting maps to baked canvas pixels");
            Undo.PerformUndo();
            layer = doc.layers[0];
            Check(layer.Behaviour is ColorFillLayerBehaviour && layer.modifiers.Count == 3, "Undo restores generator and entire stack");
            Same(before, Pixels(doc), "Undo restores pixels");
            Undo.PerformRedo();
            layer = doc.layers[0];
            Check(layer.Behaviour is DrawingLayerBehaviour && layer.modifiers.Count == 1, "Redo restores baked Drawing");
            Same(before, Pixels(doc), "Redo restores pixels");

            folder = "Assets/WhimTexApplyFXSmoke_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            var loaded = WhimTexDocumentFile.Load(WhimTexDocumentFile.Save(doc, folder + "/baked.tiff"));
            documents.Add(loaded);
            Check(loaded.layers[0].transform.Equals(originalTransform), "TIFF retains editable transform");
            Same(before, Pixels(loaded), "TIFF retains baked pixel frame and remaining FX");
            var clipboard = (TextureCompositor)Call(doc, "CaptureLayerClipboard", new List<Layer> { layer });
            documents.Add(clipboard);
            Same(before, Pixels(clipboard), "Native clipboard retains baked pixel frame");

            var drawing = (DrawingLayerBehaviour)layer.Behaviour;
            var transformed = layer.transform;
            transformed.position.x += .13;
            layer.transform = transformed;
            var moved = Pixels(doc);
            float difference = 0;
            for (int i = 0; i < before.Length; i++) difference += Mathf.Abs(before[i].a - moved[i].a);
            Check(difference > 1, "Editing transform moves baked image");
            Call(doc, "ApplyLayerFX", layer, 0);
            Check(layer.modifiers.Count == 0 && layer.transform.Equals(transformed), "Apply All keeps edited transform");
            Same(moved, Pixels(doc), "Repeated Apply retains composite");

            drawing = (DrawingLayerBehaviour)layer.Behaviour;
            drawing.brushColor = Color.red; drawing.brushSize = 6; drawing.brushHardness = 1;
            paintTransform = (TextureTransform)Call(doc, "GetPaintTransform", layer);
            Vector2 canvasPoint = new Vector2(.5f, .5f);
            Vector2 sourcePoint = paintTransform.Unmap(canvasPoint, new Vector2(doc.width, doc.height));
            var stroke = Call(drawing, "GetStrokeParameters", false);
            Call(drawing, "PaintPoint", sourcePoint, doc.width, doc.height, stroke);
            Call(drawing, "SyncSurfaceToTexture");
            var painted = Pixels(doc);
            Check(painted[24 * 64 + 32].r > .65f && painted[24 * 64 + 32].g < .05f, "Brush still hits canvas point after Apply");

            var grouped = Document();
            Layer group = new GroupLayerBehaviour();
            grouped.layers.Add(group);
            Layer child = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.UV };
            Children(group).Add(child);
            group.transform.position = new Vector2(.1f, .04f);
            group.transform.rotation = 9;
            child.transform = TextureTransform.Default;
            child.transform.scale = new Vector2(.8f, .7f);
            child.modifiers.Add(FX(grouped, "return float4(color.rgb * .6, color.a);"));
            var groupBefore = Pixels(grouped);
            var childTransform = child.transform;
            Call(grouped, "ApplyLayerFX", child, 0);
            Check(child.transform.Equals(childTransform), "Nested Drawing retains local transform");
            Same(groupBefore, Pixels(grouped), "Nested transformed layer preserves pixels");
            group.modifiers.Add(FX(grouped, "return float4(1 - color.rgb, color.a);"));
            group.modifiers.Add(FX(grouped, "color.rgb *= .5 + .3 * LayerToLocal(uv).y; return color;"));
            group.modifiers.Add(FX(grouped, "color.rgb *= .7 + .1 * LayerToLocal(uv).x; return color;"));
            group.opacity = .65f;
            groupBefore = Pixels(grouped);
            var groupTransform = group.transform;
            Call(grouped, "ApplyLayerFX", group, 0);
            Check(group.Behaviour is DrawingLayerBehaviour && group.transform.Equals(groupTransform), "Group becomes Drawing without resetting transform");
            Same(groupBefore, Pixels(grouped), "Group opacity and child composite baked once");
            group.transform.position.x += .05;
            groupBefore = Pixels(grouped);
            Call(grouped, "ApplyLayerFX", group, 0);
            Same(groupBefore, Pixels(grouped), "Remaining group FX keeps its coordinate frame on repeated Apply");

            var clipped = Document();
            Layer clippingBase = new ColorFillLayerBehaviour { color = new Color(.3f, .4f, .5f, .4f) };
            Layer clippingTop = new ColorFillLayerBehaviour { color = new Color(.7f, .3f, .2f, .6f) };
            clippingTop.clippingMask = true; clippingTop.opacity = .6f;
            clipped.layers.Add(clippingTop); clipped.layers.Add(clippingBase);
            var disabled = FX(clipped, "return float4(1,0,1,1);");
            typeof(ShaderFX).GetProperty("Active", F).SetValue(disabled, false);
            var shared = FX(clipped, "color.rgb *= .8; return color;");
            clippingTop.modifiers.Add(disabled); clippingTop.modifiers.Add(shared);
            clippingBase.modifiers.Add(shared);
            var clippedBefore = Pixels(clipped);
            Call(clipped, "ApplyLayerFX", clippingTop, 1);
            Same(clippedBefore, Pixels(clipped), "Disabled FX skipped; clipping and opacity not baked twice");
            Check(shared != null && clippingBase.modifiers[0] == shared, "Shared FX reference remains intact");
            Check(clippingTop.modifiers.Count == 0 && clippingTop.clippingMask, "Prefix removed while retaining clipping flag");

            var targeted = Document();
            Layer target = new ColorFillLayerBehaviour { color = Color.white };
            target.transform.scale = new Vector2(.6f, .6f);
            Layer outline = new OutlineLayerBehaviour { TargetLayerId = target.Id, inputMode = EffectInputMode.Specific };
            targeted.layers.Add(outline); targeted.layers.Add(target);
            outline.modifiers.Add(FX(targeted, "color.rgb *= float3(.5,.8,.3); return color;"));
            var targetBefore = Pixels(targeted);
            var renderState = RenderTexture.active;
            Call(targeted, "ApplyLayerFX", outline, 0);
            Check(RenderTexture.active == renderState, "Apply restores caller render target");
            Same(targetBefore, Pixels(targeted), "Target-input Outline keeps rendered appearance after conversion");

            var transparent = Document();
            Layer transparentLayer = new ColorFillLayerBehaviour(); transparent.layers.Add(transparentLayer);
            transparentLayer.modifiers.Add(FX(transparent, "return float4(.3,.6,.9,0);"));
            transparentLayer.modifiers.Add(FX(transparent, "color.a = 1; return color;"));
            var transparentBefore = Pixels(transparent);
            Call(transparent, "ApplyLayerFX", transparentLayer, 0);
            Same(transparentBefore, Pixels(transparent), "Partial Apply preserves RGB beneath zero alpha for remaining FX");
            drawing = (DrawingLayerBehaviour)transparentLayer.Behaviour;
            drawing.brushColor = Color.red; drawing.brushSize = 4;
            Call(drawing, "PaintPoint", new Vector2(.5f, .5f), 64, 48, Call(drawing, "GetStrokeParameters", false));
            Call(drawing, "SyncSurfaceToTexture");
            var transparentPainted = Pixels(transparent);
            Check(Mathf.Abs(transparentPainted[0].b - transparentBefore[0].b) < .006f,
                "Sync after painting retains hidden RGB outside the stroke");
            var transparentLoaded = WhimTexDocumentFile.Load(WhimTexDocumentFile.Save(transparent, folder + "/transparent.tiff"));
            documents.Add(transparentLoaded);
            Same(transparentPainted, Pixels(transparentLoaded), "TIFF preserves hidden RGB after painting");

            foreach (int nesting in new[] { 0, 1, 2, 3, 4, 5, 6 })
            foreach (BlendMode blend in new[] { BlendMode.Normal, BlendMode.Overwrite, BlendMode.Multiply })
            foreach (float opacity in new[] { .35f, 1f })
            {
                var processorDoc = Document();
                Layer backdrop = new ColorFillLayerBehaviour { color = new Color(.8f, .2f, .4f, .3f) };
                Layer processor = new ShaderProcessorLayerBehaviour();
                Layer orphan = new ColorFillLayerBehaviour { color = Color.green }; orphan.clippingMask = true;
                processor.blendMode = blend; processor.opacity = opacity;
                processor.transform.rotation = 7;
                processor.transform.scale = new Vector2(.8f, .9f);
                List<Layer> stack = processorDoc.layers;
                if (nesting != 0)
                {
                    Layer parent = new GroupLayerBehaviour();
                    parent.compositing = nesting == 3 ? GroupCompositing.Isolated : GroupCompositing.PassThrough;
                    parent.opacity = nesting == 2 ? .6f : 1;
                    parent.transform.position = new Vector2(.07f, .1f);
                    parent.transform.rotation = 9;
                    stack.Add(parent); stack.Add(backdrop);
                    if (nesting == 4) parent.clippingMask = true;
                    if (nesting == 5)
                    {
                        Layer parentClip = new ColorFillLayerBehaviour { color = new Color(.1f, .2f, .9f, .2f) };
                        parentClip.clippingMask = true; stack.Insert(0, parentClip);
                    }
                    stack = Children(parent);
                    if (nesting == 6)
                    {
                        parent.opacity = .7f;
                        Layer inner = new GroupLayerBehaviour(); inner.transform.rotation = -5;
                        stack.Add(inner); stack = Children(inner);
                    }
                    Layer local = new ColorFillLayerBehaviour { color = new Color(.1f, .6f, .2f, .2f) };
                    stack.Add(local);
                }
                else stack.Add(backdrop);
                stack.Insert(0, processor); stack.Insert(0, orphan);
                processor.modifiers.Add(FX(processorDoc, "color.rgb = 1 - color.rgb; color.a = uv.x < .4 ? 0 : .8; return color;"));
                processor.modifiers.Add(FX(processorDoc, "color.rgb *= .7 + .1 * LayerToLocal(uv).x; return color;"));
                var processorTransform = processor.transform;
                var processorBefore = Pixels(processorDoc);
                processor.opacity = .7f;
                var changedOpacity = Pixels(processorDoc);
                processor.opacity = opacity;
                Check(Call(processorDoc, "ApplyFXUnavailable", processor, 0) == null, "Processor Apply is available");
                Call(processorDoc, "ApplyLayerFX", processor, 0);
                Check(processor.Behaviour is DrawingLayerBehaviour && processor.transform.Equals(processorTransform) &&
                    processor.blendMode == (blend == BlendMode.Normal ? BlendMode.Overwrite : blend), "Processor snapshot preserves Transform and maps Normal to Overwrite");
                Same(processorBefore, Pixels(processorDoc), $"Processor prefix: nesting {nesting}, blend {blend}, opacity {opacity}");
                processor.opacity = .7f;
                Same(changedOpacity, Pixels(processorDoc), "Opacity remains editable with the original interpolation");
                processor.opacity = opacity;
                Check(processorDoc.GetClippingBase(orphan) == null, "Orphan clipping remains detached after Processor Apply");
                Call(processorDoc, "ApplyLayerFX", processor, 0);
                Same(processorBefore, Pixels(processorDoc), "Repeated Processor Apply retains blending semantics");
                if (nesting == 2 && blend == BlendMode.Normal && opacity < 1)
                {
                    var processorLoaded = WhimTexDocumentFile.Load(WhimTexDocumentFile.Save(processorDoc, folder + "/processor.tiff"));
                    documents.Add(processorLoaded);
                    Same(processorBefore, Pixels(processorLoaded), "TIFF retains Processor snapshot blending");
                    var processorCopy = (TextureCompositor)Call(processorDoc, "CaptureLayerClipboard", new List<Layer>(processorDoc.layers));
                    documents.Add(processorCopy);
                    Same(processorBefore, Pixels(processorCopy), "Native clipboard retains Processor snapshot blending");
                    Undo.PerformUndo();
                    Same(processorBefore, Pixels(processorDoc), "Processor Apply Undo restores prefix snapshot");
                    Undo.PerformUndo();
                    Check(Children(processorDoc.layers[0])[1].Behaviour is ShaderProcessorLayerBehaviour, "Undo restores original Processor");
                    Same(processorBefore, Pixels(processorDoc), "Processor Undo restores image");
                    Undo.PerformRedo();
                    Same(processorBefore, Pixels(processorDoc), "Processor Redo restores image");
                }
            }
            Check(((string)Call(doc, "ApplyFXUnavailable", layer, 0)).Contains("no effects"), "Empty stack disabled");
            var viewType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerShaderFXView");
            var view = (VisualElement)Activator.CreateInstance(viewType, F, null,
                new object[] { transparentLayer, transparent, new Action<string, Action>((name, change) => change()) }, null);
            Button applyAll = null;
            view.Query<Button>().ForEach(button => { if (button.text == "Apply All") applyAll = button; });
            Check(applyAll != null && applyAll.enabledSelf, "FX toolbar offers enabled Apply All for a populated Drawing stack");
            Call(view, "ApplyThrough", 0);
            Check(transparentLayer.modifiers.Count == 0 && !applyAll.enabledSelf, "UI Apply All bakes Drawing and disables the empty stack");
            Undo.PerformUndo();
            Same(transparentPainted, Pixels(transparent), "UI Apply is undoable");
            return "PASS ApplyFXSmoke: " + checks + " checks; cascade, transforms, brush coordinates, HDR intermediate, groups, TIFF and Undo/Redo.";
        }
        finally
        {
            foreach (var doc in documents) if (doc != null) { Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc); }
            foreach (var item in owned) if (item != null) { Undo.ClearUndo(item); UnityEngine.Object.DestroyImmediate(item); }
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }
    }
}
