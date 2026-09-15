using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
public static class GradientCanvasSmoke
{
    public static string Main()
    {
        var type=typeof(GradientLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.GradientCanvasGeometry");
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        var ends=type.GetMethod("Endpoints",flags);
        var move=type.GetMethod("MoveEndpoint",flags);
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var manipulator=typeof(TextureCompositorWindow).GetNestedType("GradientCanvasManipulator",BindingFlags.NonPublic);
        var remove=manipulator.GetMethod("WithoutColorKey",flags);
        var source=new WhimTexGradient();
        source.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.green,.5f),new GradientColorKey(Color.blue,1)},
            new[]{new GradientAlphaKey(.2f,0),new GradientAlphaKey(.8f,1)});
        foreach(int index in new[]{0,1,2})
        {
            var result=(WhimTexGradient)remove.Invoke(null,new object[]{source,index});
            Check(result.ColorKeys.Length==2,"Remove endpoint or interior key");
            Check(source.ColorKeys.Length==3,"Removal does not mutate snapshot");
            Check(result.AlphaKeys.Length==2 && result.AlphaKeys[0].alpha==.2f,"Keep alpha track");
            result=(WhimTexGradient)remove.Invoke(null,new object[]{result,0});
            Check(result.ColorKeys.Length==1,"Single remaining color key");
        }
        void Points(GradientLayerBehaviour g,Vector2 size,out Vector2 a,out Vector2 b)
        {
            var args=new object[]{g,size,null,null}; ends.Invoke(null,args); a=(Vector2)args[2];b=(Vector2)args[3];
        }
        foreach(var size in new[]{new Vector2(512,512),new Vector2(1024,256)})
        foreach(var kind in new[]{GradientLayerBehaviour.GradientType.Horizontal,GradientLayerBehaviour.GradientType.Vertical,
            GradientLayerBehaviour.GradientType.Radial,GradientLayerBehaviour.GradientType.Diamond,GradientLayerBehaviour.GradientType.Square})
        foreach(float angle in new[]{0f,37f,-125f})
        foreach(bool first in new[]{true,false})
        {
            var g=new GradientLayerBehaviour();g.gradientType=kind;
            g.transform.rotation=angle;g.transform.position=new Vector2(30,-20);g.transform.scale=new Vector2(1.2f,.8f);
            Points(g,size,out var a,out var b);
            Vector2 delta=new Vector2(43,-27);
            move.Invoke(null,new object[]{g,size,a,b,first,delta});
            Points(g,size,out var nextA,out var nextB);
            bool linear=kind==GradientLayerBehaviour.GradientType.Horizontal || kind==GradientLayerBehaviour.GradientType.Vertical;
            if(first)
            {
                Check(Vector2.Distance(nextA,a+delta)<.003f,kind+" start handle");
                Check(Vector2.Distance(nextB,linear?b:b+delta)<.003f,kind+" anchored/translated end");
            }
            else
            {
                Check(Vector2.Distance(nextA,a)<.003f,kind+" fixed center/start");
                Check(Vector2.Distance(nextB,b+delta)<.003f,$"{kind} end handle {size} angle={angle}: {nextB} vs {b+delta}, error={Vector2.Distance(nextB,b+delta)}");
            }
            Check(typeof(GradientLayerBehaviour).GetField("center")==null && typeof(GradientLayerBehaviour).GetField("radius")==null,"No redundant geometry fields");
        }
        var window=ScriptableObject.CreateInstance<TextureCompositorWindow>();
        var document=ScriptableObject.CreateInstance<TextureCompositor>();
        var instanceFlags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            var g=new GradientLayerBehaviour();
            typeof(Layer).GetField("id",instanceFlags).SetValue(g.Owner,Guid.NewGuid().ToString("N"));
            document.layers.Add(g.Owner);
            typeof(TextureCompositorWindow).GetField("compositor",instanceFlags).SetValue(window,document);
            var selection=typeof(TextureCompositorWindow).GetField("selectedLayerId",instanceFlags);
            var visible=typeof(TextureCompositorWindow).GetProperty("IsGradientCanvasEnabled",instanceFlags);
            selection.SetValue(window,g.Id);
            var tool=typeof(TextureCompositorWindow).GetField("previewTool",instanceFlags);
            foreach(var value in Enum.GetValues(tool.FieldType))
            {
                tool.SetValue(window,value);
                Check((bool)visible.GetValue(window),"Visible for selected gradient with "+value);
            }
            selection.SetValue(window,null);
            Check(!(bool)visible.GetValue(window),"Hidden without selected layer");
            selection.SetValue(window,g.Id);g.gradientType=GradientLayerBehaviour.GradientType.Circular;
            Check(!(bool)visible.GetValue(window),"Circular excluded");
        }
        finally
        {
            typeof(TextureCompositorWindow).GetField("compositor",instanceFlags).SetValue(window,null);
            UnityEngine.Object.DestroyImmediate(window);UnityEngine.Object.DestroyImmediate(document);
        }
        Check(new GradientLayerBehaviour().Owner.transform.tiling==TransformTilingMode.Unbounded,"New Gradient is Unbounded");
        Check(new NoiseLayerBehaviour().Owner.transform.tiling==TransformTilingMode.Unbounded,"New Noise is Unbounded");
        return "Gradient canvas geometry and selection checks: "+checks;
    }
}
