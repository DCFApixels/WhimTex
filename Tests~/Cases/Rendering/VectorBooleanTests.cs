using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorBooleanTests
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static VectorContour Rect(float x0,float y0,float x1,float y1) => new VectorContour { points = new List<VectorPoint> {
        new(new Vector2(x0,y0)),new(new Vector2(x1,y0)),new(new Vector2(x1,y1)),new(new Vector2(x0,y1)) } };
    static Layer Figure(float x0,float y0,float x1,float y1,Color color)
    {
        var shape=new VectorShape { fillColor=color }; shape.contours.Add(Rect(x0,y0,x1,y1));
        return new Layer(new VectorLayerBehaviour { referenceSize=new Vector2(96,80), shapes=new List<VectorShape>{shape} });
    }
    static Color[] Pixels(WhimTexDocument doc)
    { var t=doc.ComposeCanvas();try{return t.GetPixels();}finally{Object.DestroyImmediate(t);} }
    static void Same(TestContext t,Color[] a,Color[] b,string label,float tolerance=.003f)
    {
        t.Equal(a.Length,b.Length,label+" length");
        for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)t.Near(a[i][c],b[i][c],tolerance,label+" "+i+"/"+c);
    }
    static WhimTexDocument NewDoc() => ScriptableObject.CreateInstance<WhimTexDocument>();
    public static string Run() => TestContext.Run("Vector layer frames and Boolean operations", t =>
    {
        var doc=NewDoc();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=96;doc.height=80;doc.outputPrecision=WhimTexOutputPrecision.Float32;
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        ShaderFX ignored=null;
        try
        {
            var model=new BooleanGroupLayerBehaviour { referenceSize=new Vector2(96,80) };
            var group=new Layer(model);doc.layers.Add(group);
            var bottom=Figure(.15f,.2f,.6f,.8f,Color.red);var top=Figure(.4f,.1f,.85f,.7f,Color.green);
            group.children.Add(top);group.children.Add(bottom);model.style.fillColor=Color.blue;
            foreach(VectorBooleanOperation op in Enum.GetValues(typeof(VectorBooleanOperation)))
            {
                top.booleanOperation=op;var pixels=Pixels(doc);
                for(int y=0;y<80;y+=3)for(int x=0;x<96;x+=3)
                {
                    float u=(x+.5f)/96,v=(y+.5f)/80;
                    if(Mathf.Min(Mathf.Abs(u-.15f),Mathf.Abs(u-.6f),Mathf.Abs(u-.4f),Mathf.Abs(u-.85f))<.015f ||
                       Mathf.Min(Mathf.Abs(v-.2f),Mathf.Abs(v-.8f),Mathf.Abs(v-.1f),Mathf.Abs(v-.7f))<.015f)continue;
                    bool a=u>.15f&&u<.6f&&v>.2f&&v<.8f,b=u>.4f&&u<.85f&&v>.1f&&v<.7f;
                    bool inside=op is VectorBooleanOperation.Union or VectorBooleanOperation.Combine?a||b:op==VectorBooleanOperation.Subtract?a&&!b:op==VectorBooleanOperation.Intersect?a&&b:a!=b;
                    t.Near(inside?1:0,pixels[y*96+x].a,.002,"Boolean occupancy "+op);
                    if(inside)t.Near(1,pixels[y*96+x].b,.002,"Group owns fill");
                }
            }
            Combined(t, doc, group, model, top, bottom);
            top.booleanOperation=VectorBooleanOperation.Subtract;
            var baseline=Pixels(doc);string savedStyle=JsonUtility.ToJson(((VectorLayerBehaviour)bottom.Behaviour).shapes[0]);
            bottom.opacity=0;bottom.clippingMask=true;bottom.blendMode=BlendMode.None;
            var bottomStyle=((VectorLayerBehaviour)bottom.Behaviour).shapes[0];bottomStyle.stroke=true;bottomStyle.strokeWidth=30;
            ignored=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",Hidden|BindingFlags.Static,null,
                new[]{typeof(WhimTexDocument),typeof(string),typeof(List<ShaderFXParameter>)},null)
                .Invoke(null,new object[]{doc,"float4 ApplyFX(float2 uv,float4 color){return 0;}",new List<ShaderFXParameter>()});
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",Hidden).Invoke(ignored,null);bottom.fx.Add(ignored);
            Same(t,baseline,Pixels(doc),"Child appearance/FX not rendered");
            bottom.fx.Clear();bottom.opacity=1;bottom.clippingMask=false;bottom.blendMode=BlendMode.Normal;bottomStyle.stroke=false;bottomStyle.strokeWidth=2;
            t.Equal(savedStyle,JsonUtility.ToJson(bottomStyle),"Preserved style data except explicit test edits");
            var original=top.transform; top.transform.TrySetMatrix(ProjectiveMatrix.Translate(9d/96,0)*top.transform.ToMatrix(96,80));
            t.True(Pixels(doc)[20*96+45].a>.9f,"Moving subtract operand updates result");top.transform=original;
            Same(t,baseline,Pixels(doc),"Restoring child transform restores geometry");
            var nested=new Layer(new BooleanGroupLayerBehaviour());nested.children.Add(top);nested.children.Add(bottom);
            group.children.Clear();group.children.Add(nested);Same(t,baseline,Pixels(doc),"Nested boolean geometry");
            group.children.Clear();group.children.Add(top);group.children.Add(bottom);nested.children.Clear();
            group.transform.positionF=new Vector2(7,3);var shifted=Pixels(doc);t.True(shifted[35*96+19].a<.1f,"Group transform applied");
            group.transform=TextureTransform.Default;group.transform.tiling=TransformTilingMode.Unbounded;
            foreach(var method in new[]{"RenderPsdGroupContent"})
            {
                var image=(Texture2D)typeof(WhimTexDocument).GetMethod(method,Hidden).Invoke(doc,new object[]{group});
                try{t.Near(1,image.GetPixel(20,35).a,.01,"Export geometry");t.Near(0,image.GetPixel(45,35).a,.01,"Export subtraction");}finally{Object.DestroyImmediate(image);}
            }
            var preview=(RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreviewFallback",Hidden).Invoke(doc,new object[]{group,96});
            t.True(preview!=null,"Group preview");RenderTexture.ReleaseTemporary(preview);
            group.enabled=false;
            var effect=new Layer(new BlurLayerBehaviour { inputMode=EffectInputMode.Specific,TargetLayerId=group.Id,radius=0,strength=0 });doc.layers.Insert(0,effect);
            Same(t,baseline,Pixels(doc),"Hidden Boolean Group Target");doc.layers.Remove(effect);
            typeof(Layer).GetMethod("ReleaseTransientResources",Hidden).Invoke(effect,null);group.enabled=true;
            var clip=new Layer(new ColorFillLayerBehaviour{color=Color.white}){clippingMask=true};doc.layers.Insert(0,clip);
            var clipped=Pixels(doc);for(int i=0;i<baseline.Length;i++)t.Near(baseline[i].a,clipped[i].a,.003,"Boolean clipping base");doc.layers.Remove(clip);
            top.enabled=false;var alone=Pixels(doc);t.Near(1,alone[35*96+45].a,.002,"Disabled operand skipped");top.enabled=true;
            var open=Figure(.1f,.1f,.9f,.9f,Color.white);((VectorLayerBehaviour)open.Behaviour).shapes[0].contours[0].closed=false;
            group.children.Add(open);bool rejected=false;try{model.Validate();}catch(InvalidOperationException){rejected=true;}t.True(rejected,"Open operand explicit rejection");group.children.Remove(open);
            group.children.Add(new Layer(new ColorFillLayerBehaviour()));rejected=false;try{model.Validate();}catch(InvalidOperationException){rejected=true;}t.True(rejected,"Raster operand explicit rejection");group.children.RemoveAt(group.children.Count-1);
            foreach(var mode in new[]{VectorFillMode.LinearGradient,VectorFillMode.RadialGradient,VectorFillMode.SquareGradient,VectorFillMode.AngularGradient})
            {model.style.fillMode=mode;model.style.stroke=true;model.style.strokeWidth=2;model.style.cap=VectorStrokeCap.Round;model.style.dashPattern=new List<float>{.1f,2};model.style.dashPatternRelative=true;Pixels(doc);t.True(true,"Gradient and dashes "+mode);}
            model.style.fillMode=VectorFillMode.Solid;model.style.stroke=false;
            var output=doc.ComposeCanvas();var png=new Texture2D(96,80,TextureFormat.RGBA32,false);
            try{png.SetPixels(output.GetPixels());png.Apply();Directory.CreateDirectory("Temp/WhimTex");File.WriteAllBytes("Temp/WhimTex/vector-boolean-" + System.Guid.NewGuid().ToString("N") + ".png",png.EncodeToPNG());}
            finally{Object.DestroyImmediate(output);Object.DestroyImmediate(png);}
            t.True(RenderTexture.active==previous && GL.sRGBWrite==srgb,"Render state preserved");
        }
        finally {RenderTexture.active=previous;GL.sRGBWrite=srgb;Object.DestroyImmediate(doc);if(ignored!=null)Object.DestroyImmediate(ignored);}
    });

    static void Combined(TestContext t, WhimTexDocument doc, Layer group, BooleanGroupLayerBehaviour model, Layer top, Layer bottom)
    {
        var savedStyle = model.style.Clone();
        try
        {
            top.booleanOperation = VectorBooleanOperation.Combine;
            model.style.fillColor = new Color(0, 0, 1, .5f);
            var combined = Pixels(doc);
            t.Near(.5, combined[30*96+20].a, .003, "Combine keeps individual half-alpha fill");
            t.Near(.75, combined[30*96+45].a, .003, "Combine overlays fills instead of resolving Union");
            top.booleanOperation = VectorBooleanOperation.Union;
            t.Near(.5, Pixels(doc)[30*96+45].a, .003, "Union resolves overlap once");
            model.style.fillMode = VectorFillMode.None; model.style.stroke = true;
            model.style.strokeWidth = 4; model.style.strokeColor = Color.white;
            t.Near(0, Pixels(doc)[30*96+38].a, .003, "Union has no interior outline");
            top.booleanOperation = VectorBooleanOperation.Combine;
            t.True(Pixels(doc)[30*96+38].a > .95f, "Combine keeps the upper figure's complete outline");
            model.style = savedStyle.Clone(); model.appearance = BooleanGroupAppearance.Layers;
            var layers = Pixels(doc);
            t.Near(1, layers[30*96+20].r, .003, "Layers appearance retains bottom red fill");
            t.Near(1, layers[30*96+45].g, .003, "Layers appearance retains upper green fill");
            bottom.opacity = 0; bottom.blendMode = BlendMode.Multiply;
            Same(t, layers, Pixels(doc), "Appearance does not enable child opacity or blend settings");
            bottom.opacity = 1; bottom.blendMode = BlendMode.Normal;
            var shape = new Layer(new ShapeLayerBehaviour { kind=ShapeLayerBehaviour.ShapeKind.Ellipse,
                fillMode=VectorFillMode.RadialGradient, stroke=true,strokeWidth=3,feather=2 });
            shape.transform.positionF = new Vector2(11,-4); shape.transform.scaleF = new Vector2(.7f,.4f);
            shape.transform.rotation = 23; shape.booleanOperation = VectorBooleanOperation.Combine;
            group.children.Insert(0, shape);
            try
            {
                var expected = Pixels(doc);
                group.SetBehaviour(new GroupLayerBehaviour()); group.compositing = GroupCompositing.Isolated;
                foreach (var child in group.children) child.blendRange = LayerBlendRange.HDR;
                try { Same(t, expected, Pixels(doc), "Layers keeps native Shape gradient, stroke, Feather and transforms"); }
                finally { foreach (var child in group.children) child.blendRange = LayerBlendRange.Standard; group.SetBehaviour(model); }
                group.transform.positionF = new Vector2(5,3); var shifted = Pixels(doc);
                var source = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerContent", Hidden).Invoke(doc,new object[]{group,96,80,null});
                Texture2D local = new Texture2D(96,80,TextureFormat.RGBAFloat,false,true);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active=source;local.ReadPixels(new Rect(0,0,96,80),0,0,false);local.Apply(false);
                    Same(t,expected,local.GetPixels(),"Untransformed content omits group transform but keeps child transforms",.008f);
                }
                finally{RenderTexture.active=previous;Object.DestroyImmediate(local);RenderTexture.ReleaseTemporary(source);}
                Same(t,shifted,Pixels(doc),"Untransformed preview restores hierarchy caches");
                group.transform.positionF=Vector2.zero;
                var exported=(Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdGroupContent",Hidden).Invoke(doc,new object[]{group});
                try
                {
                    var encoded = new Color[expected.Length];
                    for (int i = 0; i < encoded.Length; i++) encoded[i] = expected[i].gamma;
                    Same(t, encoded, exported.GetPixels(), "Combine Layers export", .008f);
                }
                finally {Object.DestroyImmediate(exported);}
                group.enabled=false;
                var effect=new Layer(new BlurLayerBehaviour{inputMode=EffectInputMode.Specific,TargetLayerId=group.Id,radius=0,strength=0});doc.layers.Insert(0,effect);
                try { Same(t, expected, Pixels(doc), "Combine Layers hidden Target", .008f); }
                finally {doc.layers.Remove(effect);typeof(Layer).GetMethod("ReleaseTransientResources",Hidden).Invoke(effect,null);group.enabled=true;}
            }
            finally {group.children.Remove(shape);typeof(Layer).GetMethod("ReleaseTransientResources",Hidden).Invoke(shape,null);}
            top.booleanOperation=VectorBooleanOperation.Union;
            t.Near(1,Pixels(doc)[30*96+45].b,.003,"Boolean operation uses group styling despite retained Layers preference");
            t.Equal(BooleanGroupAppearance.Layers,model.appearance,"Appearance preference is retained outside Combine");
            top.booleanOperation=VectorBooleanOperation.Combine;
            t.Near(1,Pixels(doc)[30*96+45].g,.003,"Switching back to Combine restores layer styling");
            var nested=new Layer(new BooleanGroupLayerBehaviour{appearance=BooleanGroupAppearance.Layers});
            nested.children.Add(top);nested.children.Add(bottom);group.children.Clear();group.children.Add(nested);
            nested.booleanOperation=VectorBooleanOperation.Combine;
            try {Same(t,layers,Pixels(doc),"Nested Combine keeps layer styles");}
            finally {group.children.Clear();group.children.Add(top);group.children.Add(bottom);nested.children.Clear();typeof(Layer).GetMethod("ReleaseTransientResources",Hidden).Invoke(nested,null);}
        }
        finally
        {
            model.style=savedStyle;model.appearance=BooleanGroupAppearance.Group;
            group.transform.positionF=Vector2.zero;bottom.opacity=1;bottom.blendMode=BlendMode.Normal;
        }
    }

    public static string Frames() => TestContext.Run("One figure per layer, cubic bounds and mask reframe",t=>
    {
        var doc=NewDoc();doc.width=96;doc.height=80;doc.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var layer=Figure(.2f,.3f,.7f,.8f,Color.white);var vector=(VectorLayerBehaviour)layer.Behaviour;
            vector.shapes[0].contours[0].points[0].outTangent=new Vector2(.1f,-.3f);
            vector.shapes[0].stroke=true;vector.shapes[0].strokeWidth=2;vector.shapes[0].fillMode=VectorFillMode.RadialGradient;
            var shape=vector.shapes[0];string id=shape.id;var old=shape.Clone();var oldRef=vector.referenceSize;
            VectorLayerStructure.FitFrame(layer,96,80);var bounds=VectorLayerStructure.Bounds(shape);
            t.Near(0,bounds.xMin,1e-5,"frame minX");t.Near(0,bounds.yMin,1e-5,"frame minY");t.Near(1,bounds.xMax,1e-5,"frame maxX");t.Near(1,bounds.yMax,1e-5,"frame maxY");
            for(int i=0;i<4;i++)
            {
                var a=old.contours[0].points[i];var b=shape.contours[0].points[i];
                var mapped=layer.transform.Map(b.position,new Vector2(96,80));t.Near(a.position.x,mapped.x,1e-6,"Rebase preserves X");t.Near(a.position.y,mapped.y,1e-6,"Rebase preserves Y");
                t.Near(a.outTangent.x*oldRef.x,b.outTangent.x*vector.referenceSize.x,1e-5,"Physical tangent preserved");
            }
            t.Equal(id,shape.id,"Figure ID retained");doc.layers.Add(layer);
            var once=layer.transform;VectorLayerStructure.FitFrame(layer,96,80);t.Equal(once,layer.transform,"Reframing is idempotent");
            var multi=new VectorLayerBehaviour {referenceSize=new Vector2(96,80),shapes=new List<VectorShape>{old,old.Clone(false)}};
            var shell=new Layer(multi){layerName="Compound figures",opacity=.4f};shell.transform.positionF=new Vector2(4,8);string shellId=shell.Id;
            var oldTransform=shell.transform;doc.layers.Clear();doc.layers.Add(shell);VectorLayerStructure.Normalize(doc.layers,96,80);
            t.True(shell.Behaviour is GroupLayerBehaviour,"Multi-figure shell becomes group");t.Equal(shellId,shell.Id,"Shell ID retained");t.Equal(oldTransform,shell.transform,"Common transform retained");t.Equal(2,shell.children.Count,"One child per figure");
            t.Equal(multi.shapes[1].id,((VectorLayerBehaviour)shell.children[0].Behaviour).shapes[0].id,"Top-to-bottom order preserved");
            var draft=new VectorBrushDraft{width=32,height=32,coverage=new byte[1024],tolerance=.5f};
            for(int y=8;y<24;y++)for(int x=10;x<23;x++)draft.coverage[y*32+x]=255;
            var source=VectorBrushGeometry.Rebuild(draft,new Vector2(96,80));var before=source.Clone();
            var brushLayer=new Layer(new VectorLayerBehaviour {referenceSize=new Vector2(96,80),shapes=new List<VectorShape>{source},brushDraft=draft});
            VectorLayerStructure.FitFrame(brushLayer,96,80);var vb=(VectorLayerBehaviour)brushLayer.Behaviour;
            var rebuilt=VectorBrushGeometry.Rebuild(draft,vb.referenceSize);
            for(int c=0;c<before.contours.Count;c++)for(int p=0;p<before.contours[c].points.Count;p++)
            {
                var a=before.contours[c].points[p].position;var b=brushLayer.transform.Map(rebuilt.contours[c].points[p].position,new Vector2(96,80));
                t.Near(a.x,b.x,1e-5,"Saved mask rebuild X");t.Near(a.y,b.y,1e-5,"Saved mask rebuild Y");
            }
            var frame=draft.frame;draft.expansion=2;var expanded=VectorBrushGeometry.Rebuild(draft,vb.referenceSize);t.Equal(frame,draft.frame,"Rebuild keeps mask frame");t.True(VectorLayerStructure.Bounds(expanded).width>1,"Expansion can exceed fitted frame");
            doc.layers.Clear();doc.layers.Add(brushLayer);
            typeof(WhimTexDocument).GetMethod("NormalizeModel",Hidden).Invoke(doc,null);
            foreach(WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {using var read=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc,new WhimTexJsonWriteOptions{Mode=mode}).Json,false);var restored=(VectorLayerBehaviour)read.Document.layers[0].Behaviour;t.Equal(draft.frame,restored.brushDraft.frame,"Mask frame JSON "+mode);t.Equal(1,restored.shapes.Count,"One figure JSON "+mode);}
        }
        finally {Object.DestroyImmediate(doc);}
    });
}
