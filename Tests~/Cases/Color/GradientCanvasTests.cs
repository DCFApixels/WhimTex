using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
public static class GradientCanvasTests
{
    static string ExecuteMain()
    {
        var type=typeof(GradientLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.GradientCanvasGeometry");
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        var ends=type.GetMethod("Endpoints",flags);
        var move=type.GetMethod("MoveEndpoint",flags);
        int checks=0;
        void Check(bool ok,string message){UnityBRun.Check(!(!ok), message);checks++;}
        var manipulator=typeof(WhimTexWindow).GetNestedType("GradientCanvasManipulator",BindingFlags.NonPublic);
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
        foreach(var kind in new[]{GradientLayerBehaviour.GradientType.Linear,
            GradientLayerBehaviour.GradientType.Radial,GradientLayerBehaviour.GradientType.Square})
        foreach(float angle in new[]{0f,37f,-125f})
        foreach(bool first in new[]{true,false})
        {
            var g=new GradientLayerBehaviour();g.gradientType=kind;
            g.transform.rotation=angle;g.transform.position=new Vector2(30,-20);g.transform.scale=new Vector2(1.2f,.8f);
            Points(g,size,out var a,out var b);
            Vector2 delta=new Vector2(43,-27);
            move.Invoke(null,new object[]{g,size,a,b,first,delta});
            Points(g,size,out var nextA,out var nextB);
            bool linear=kind==GradientLayerBehaviour.GradientType.Linear;
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
        var window=UnityBRun.Create<WhimTexWindow>();
        var instanceFlags=BindingFlags.Instance|BindingFlags.NonPublic;
        var document=(WhimTexDocument)typeof(WhimTexWindow).GetField("activeDocument",instanceFlags).GetValue(window);
        try
        {
            var g=new GradientLayerBehaviour();
            typeof(Layer).GetField("id",instanceFlags).SetValue(g.Owner,Guid.NewGuid().ToString("N"));
            document.layers.Add(g.Owner);
            typeof(WhimTexWindow).GetField("activeDocument",instanceFlags).SetValue(window,document);
            var selection=typeof(WhimTexWindow).GetField("selectedLayerId",instanceFlags);
            var visible=typeof(WhimTexWindow).GetProperty("IsGradientCanvasEnabled",instanceFlags);
            selection.SetValue(window,g.Id);
            var tool=typeof(WhimTexWindow).GetField("canvasTool",instanceFlags);
            foreach(var value in Enum.GetValues(tool.FieldType))
            {
                tool.SetValue(window,value);
                Check((bool)visible.GetValue(window) == (value.ToString() == "GradientHandles"),
                    "Handles belong only to the gradient context tool: "+value);
            }
            selection.SetValue(window,null);
            Check(!(bool)visible.GetValue(window),"Hidden without selected layer");
            selection.SetValue(window,g.Id);g.gradientType=GradientLayerBehaviour.GradientType.Angular;
            tool.SetValue(window,Enum.Parse(tool.FieldType,"GradientHandles"));
            Check((bool)visible.GetValue(window),"Angular has canvas handles");
            var angular=typeof(GradientLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.AngularGradientGeometry");
            var boundary=angular.GetMethod("MoveBoundary",flags);
            foreach(float origin in new[]{0f,83f,359f})
            foreach(bool first in new[]{false,true})
            {
                var args=new object[]{origin,360f,first?60f:-60f,first,1f,null,null,false};
                boundary.Invoke(null,args);
                Check(Mathf.Abs((float)args[6]-300f)<.001f,"Both directions separate the seam");
                Check(Mathf.Abs(Mathf.DeltaAngle(origin+(first?60:0),(float)args[5]))<.001f,"Opposite boundary remains fixed");
                args=new object[]{(float)args[5],300f,first?-59.5f:59.5f,first,1f,null,null,false};
                boundary.Invoke(null,args);
                Check((float)args[6]==360f,"Near-full circle snaps closed");
                Check(Mathf.Abs(Mathf.DeltaAngle(origin,(float)args[5]))<.001f,"Closing preserves the fixed boundary");
                foreach(int repetitions in new[]{1,2,3,7,29,180,360})
                {
                    float delta=360f-360f/(repetitions+.1f);
                    args=new object[]{origin,360f,first?delta:-delta,first,1f,null,null,true};
                    boundary.Invoke(null,args);
                    float actualSpan=(float)args[6];
                    Check(Mathf.Abs(actualSpan-360f/repetitions)<.001f,"Ctrl snaps repetitions, not degrees");
                    float fixedAngle=(float)args[5]+(first?actualSpan:0);
                    Check(Mathf.Abs(Mathf.DeltaAngle(origin,fixedAngle))<.001f,"Ctrl preserves opposite endpoint");
                }
            }
            var project=angular.GetMethod("Project",flags);
            Check((float)project.Invoke(null,new object[]{-1f,360f,1f})>.99f,"End key does not jump to zero across seam");
            Check((float)project.Invoke(null,new object[]{1f,360f,0f})<.01f,"Start key stays on start side of seam");
            g.circularStartAngle=90;g.circularRepetitions=1;
            Check(Mathf.Abs(g.GetGradientCoord(1,.5f)-.25f)<.00001f,"Start angle rotates the field without modifying keys");
        }
        finally
        {
            typeof(WhimTexWindow).GetField("activeDocument",instanceFlags).SetValue(window,null);
            UnityEngine.Object.DestroyImmediate(window);UnityEngine.Object.DestroyImmediate(document);
        }
Check(new GradientLayerBehaviour().Owner.transform.tiling==TransformTilingMode.Source,"New Gradient uses Source tiling");
Check(new NoiseLayerBehaviour().Owner.transform.tiling==TransformTilingMode.Source,"New Noise uses Source tiling");
        return "Gradient canvas geometry and selection checks: "+checks;
    }
    public static string Main() => UnityBRun.Run("GradientCanvasSmoke.Main", () => ExecuteMain());
}

