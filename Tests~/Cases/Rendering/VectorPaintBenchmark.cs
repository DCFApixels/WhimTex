using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Retains pre-change pixels in Temp, never regenerates a baseline during comparison.
public static class VectorPaintBenchmark
{
    static readonly MethodInfo Render = typeof(WhimTexDocument).GetMethod("RenderCanvas", BindingFlags.Instance|BindingFlags.NonPublic);
    const string Folder = "Temp/WhimTex/vector-paint-benchmark-v2";
    [Serializable] public sealed class Result { public string status; public int checks; public string message; public string[] failures; }
    public static string Check()
    {
        try { return JsonUtility.ToJson(new Result { status="passed",checks=1,message=Run("after-check",0),failures=Array.Empty<string>() }); }
        catch(Exception e) { return JsonUtility.ToJson(new Result { status="failed",checks=1,message="Vector paint benchmark",failures=new[]{e.ToString()} }); }
    }
    public static string Run(string phase, int scenario)
    {
        if(phase!="before" && phase!="after" && phase!="after-final" && phase!="after-check")throw new ArgumentException("Unknown measurement phase.");
        Directory.CreateDirectory(Folder);
        if(File.Exists(Folder+"/"+phase+"-"+scenario+".txt"))throw new InvalidOperationException("Report already exists; preserve original measurements.");
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        var lines=new List<string>();int current=0;
        try
        {
            foreach(int size in new[]{512,1024})foreach(bool boolean in new[]{false,true})
            foreach(bool keys in new[]{false,true})
            {
                if(current++!=scenario)continue;
                var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;
                doc.width=doc.height=size;doc.outputPrecision=WhimTexOutputPrecision.Float32;
                var shape=Figure();
                if(boolean)
                {
                    var group=new BooleanGroupLayerBehaviour{referenceSize=Vector2.one*size,style=shape.Clone(false)};
                    group.style.contours.Clear();shape=group.style;doc.layers.Add(group.Owner);
                    for(int i=0;i<4;i++)
                    {
                        var operand=new VectorLayerBehaviour{referenceSize=Vector2.one*size,shapes=new List<VectorShape>{Figure()}};
                        operand.transform.scaleF=Vector2.one*.78f;
                        operand.transform.positionF=new Vector2((i%2-.5f)*size*.35f,(i/2-.5f)*size*.3f);
                        group.Owner.children.Add(operand.Owner);
                        operand.Owner.booleanOperation=i==0?VectorBooleanOperation.Subtract:VectorBooleanOperation.Union;
                    }
                }
                else doc.layers.Add(new VectorLayerBehaviour{referenceSize=Vector2.one*size,shapes=new List<VectorShape>{shape}}.Owner);
                doc.layers[0].transform.scaleF=Vector2.one;doc.layers[0].colorRange=LayerColorRange.HDR;
                var readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
                string label=(boolean?"boolean":"vector")+"-"+size+"-"+(keys?"keys":"anchors");
                var submission=new List<double>();var complete=new List<double>();
                try
                {
                    for(int i=-2;i<12;i++)
                    {
                        float value=(i+2)/16f;
                        if(keys)shape.gradient.SetKeys(new[]{new GradientColorKey(new Color(.8f,.03f,.1f),0),new GradientColorKey(new Color(.05f,.6f,1.4f),.3f+value*.3f),new GradientColorKey(Color.white,1)},
                            new[]{new GradientAlphaKey(.4f,0),new GradientAlphaKey(.9f,1)});
                        else{shape.gradientEnd=new Vector2(.65f+value*.2f,.4f+value*.17f);shape.strokeGradientStart=new Vector2(.4f+value*.1f,.35f);}
                        var sw=Stopwatch.StartNew();
                        var rt=(RenderTexture)Render.Invoke(doc,new object[]{size});
                        try
                        {
                            double submit=sw.Elapsed.TotalMilliseconds;
                            RenderTexture.active=rt;readback.ReadPixels(new Rect(0,0,size,size),0,0,false);
                            double wall=sw.Elapsed.TotalMilliseconds;
                            if(i>=0){submission.Add(submit);complete.Add(wall);}
                            if(i==11)
                            {
                                string path=Folder+"/"+label+".rgba";
                                var raw=readback.GetRawTextureData<float>();
                                if(phase=="before")
                                {
                                    if(File.Exists(path))throw new InvalidOperationException("Reference exists: "+path);
                                    using(var writer=new BinaryWriter(File.Create(path)))for(int p=0;p<raw.Length;p++)writer.Write(raw[p]);
                                    File.WriteAllBytes(Folder+"/"+label+".png",readback.EncodeToPNG());
                                }
                                else
                                {
                                    File.WriteAllBytes(Folder+"/"+label+".after.png",readback.EncodeToPNG());
                                    object model=doc.layers[0].Behaviour;
                                    if(boolean)model=typeof(BooleanGroupLayerBehaviour).GetField("result",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(model);
                                    var masks=(System.Collections.IList)typeof(VectorLayerBehaviour).GetField("coverage",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(model);
                                    if(masks!=null && masks.Count>0)
                                    {
                                        var mask=(Texture2D)masks[0].GetType().GetField("texture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(masks[0]);
                                        File.WriteAllBytes(Folder+"/"+label+".coverage.png",mask.EncodeToPNG());
                                        var painted=(RenderTexture)typeof(VectorLayerBehaviour).GetField("painted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(model);
                                        RenderTexture.active=painted;var paintRead=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
                                        try{paintRead.ReadPixels(new Rect(0,0,size,size),0,0,false);File.WriteAllBytes(Folder+"/"+label+".paint.png",paintRead.EncodeToPNG());}
                                        finally{Object.DestroyImmediate(paintRead);RenderTexture.active=rt;}
                                    }
                                    double maxRgb=0,maxAlpha=0,sum=0;
                                    using(var reader=new BinaryReader(File.OpenRead(path)))for(int p=0;p<raw.Length;p++)
                                    {double delta=Math.Abs(raw[p]-reader.ReadSingle());if(!double.IsFinite(delta))throw new Exception("Nonfinite pixel");if(p%4==3)maxAlpha=Math.Max(maxAlpha,delta);else maxRgb=Math.Max(maxRgb,delta);sum+=delta;}
                                    lines.Add(label+" pixels maxRGB="+maxRgb.ToString("F7")+" maxAlpha="+maxAlpha.ToString("F7")+" mean="+(sum/raw.Length).ToString("F8"));
                                    if(maxRgb>.015 || maxAlpha>.001)throw new Exception("Pixel parity exceeded: "+lines[lines.Count-1]);
                                }
                            }
                        }
                        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
                    }
                    submission.Sort();complete.Sort();
                    lines.Add(label+" submitMedianMs="+submission[6].ToString("F3")+" completedMedianMs="+complete[6].ToString("F3")+" completedMaxMs="+complete[11].ToString("F3"));
                }
                finally{Object.DestroyImmediate(readback);Object.DestroyImmediate(doc);}
            }
            string report=string.Join("\n",lines);File.WriteAllText(Folder+"/"+phase+"-"+scenario+".txt",report);return report;
        }
        finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;}
    }
    static VectorShape Figure()
    {
        var s=new VectorShape{fillMode=VectorFillMode.CircularGradient,gradientStart=new Vector2(.43f,.47f),gradientEnd=new Vector2(.8f,.6f),gradientRepetitions=1.7f,
            stroke=true,strokeWidth=14,strokeMode=VectorFillMode.LinearGradient,strokeGradientStart=new Vector2(.2f,.3f),strokeGradientEnd=new Vector2(.8f,.6f),
            join=VectorStrokeJoin.Round,cap=VectorStrokeCap.Round,dashPatternRelative=true,dashPattern=new List<float>{1,1.3f},strokePosition=VectorStrokePosition.Inside};
        s.gradient.WrapMode=WhimTexGradientWrapMode.Mirror;s.gradient.Mode=WhimTexGradientMode.Perceptual;
        s.gradient.SetKeys(new[]{new GradientColorKey(new Color(.8f,.03f,.1f),0),new GradientColorKey(new Color(.05f,.6f,1.4f),.45f),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.4f,0),new GradientAlphaKey(.9f,1)});
        s.strokeGradient.SetKeys(new[]{new GradientColorKey(Color.yellow,0),new GradientColorKey(Color.magenta,1)},new[]{new GradientAlphaKey(.35f,0),new GradientAlphaKey(.8f,1)});
        var contour=new VectorContour();s.contours.Add(contour);
        for(int i=0;i<24;i++){float angle=i*Mathf.PI/12,r=i%2==0?.44f:.32f;contour.points.Add(new VectorPoint(new Vector2(.5f+Mathf.Cos(angle)*r,.5f+Mathf.Sin(angle)*r)));}
        return s;
    }
}
