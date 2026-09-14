// Opt-in after manual compilation. Transient objects only; no asset writes or Undo.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var compositorType = typeof(DCFApixels.WhimTex.TextureCompositor);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = 33; document.height = 25;
var texture = new UnityEngine.Texture2D(33,25,UnityEngine.TextureFormat.RGBAFloat,false,true);
texture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
var source = new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture = texture, colorRange = DCFApixels.WhimTex.LayerColorRange.HDR };
var motion = new DCFApixels.WhimTex.BlurLayerBehaviour { mode = DCFApixels.WhimTex.BlurType.Linear, distance = 8, colorRange = DCFApixels.WhimTex.LayerColorRange.HDR };
document.layers.Add(motion); document.layers.Add(source);
compositorType.GetMethod("NormalizeModel",flags).Invoke(document,null);
int checks = 0;
void Check(bool value,string message) { if(!value) throw new System.Exception(message); checks++; }
void Upload(UnityEngine.Color[] values) { texture.SetPixels(values); texture.Apply(false,false); }
UnityEngine.Color[] Render()
{
    var rt = (UnityEngine.RenderTexture)compositorType.GetMethod("RenderLayerPreview",flags).Invoke(document,new object[]{motion.Owner,33});
    var previous = UnityEngine.RenderTexture.active;
    var read = new UnityEngine.Texture2D(33,25,UnityEngine.TextureFormat.RGBAFloat,false,true);
    try
    {
        UnityEngine.RenderTexture.active = rt;
        read.ReadPixels(new UnityEngine.Rect(0,0,33,25),0,0,false);
        return read.GetPixels();
    }
    finally { UnityEngine.RenderTexture.active = previous; UnityEngine.RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(read); }
}
void Same(UnityEngine.Color[] a,UnityEngine.Color[] b,float tolerance,string message)
{ for(int i=0;i<a.Length;i++) for(int c=0;c<4;c++) Check(System.Math.Abs(a[i][c]-b[i][c])<=tolerance,message); }
try
{
    var input = new UnityEngine.Color[33*25];
    for(int i=0;i<input.Length;i++) input[i] = new UnityEngine.Color(0,8,0,0);
    input[12*33+16] = new UnityEngine.Color(4,0,0,1);
    Upload(input);
    foreach(float angle in new[]{0f,90f})
    {
        motion.angle = angle;
        var pixels = Render();
        for(int y=0;y<25;y++) for(int x=0;x<33;x++)
        {
            int d=angle==0?x-16:y-12;
            bool row=angle==0?y==12:x==16;
            float expected=!row||System.Math.Abs(d)>4?0:System.Math.Abs(d)==4?1f/16:1f/8;
            var p=pixels[y*33+x];
            Check(System.Math.Abs(p.a-expected)<.002,"Linear impulse coverage");
            if(expected>0) { Check(System.Math.Abs(p.r-4)<.02,"HDR retained"); Check(System.Math.Abs(p.g)<.001,"Hidden RGB rejected"); }
        }
    }
    motion.angle=0;
    motion.strength=0;
    Check(Render()[12*33+16].a>.999f,"Zero strength keeps original");
    motion.strength=.5f;
    Check(System.Math.Abs(Render()[12*33+16].a-.5625f)<.002,"Half strength premultiplied mix");
    motion.strength=2;
    var intensified=Render()[12*33+18];
    Check(System.Math.Abs(intensified.a-2f/9f)<.002 && System.Math.Abs(intensified.r-4)<.02,"Higher strength thickens trail without brightening HDR");
    motion.strength=1;
    var baseline=Render();source.enabled=false;
    Same(baseline,Render(),.001f,"Hidden source remains usable");source.enabled=true;
    motion.distance=0;Check(Render()[12*33+16].a>.999f,"Zero distance identity");motion.distance=8;
    motion.direction=DCFApixels.WhimTex.BlurLayerBehaviour.MotionDirection.Forward;
    var forward=Render();Check(forward[12*33+20].a>.1f && forward[12*33+12].a<.001,"Forward direction");
    motion.direction=DCFApixels.WhimTex.BlurLayerBehaviour.MotionDirection.Backward;
    var backward=Render();Check(backward[12*33+12].a>.1f && backward[12*33+20].a<.001,"Backward direction");
    motion.direction=DCFApixels.WhimTex.BlurLayerBehaviour.MotionDirection.Centered;
    motion.mode=DCFApixels.WhimTex.BlurType.Circular;
    motion.arc=180;Check(Render()[12*33+16].a>.999f,"Circular center fixed");
    motion.arc=0;Check(Render()[12*33+16].a>.999f,"Zero arc identity");
    // A point to the right of center sweeps upward when moving forward.
    System.Array.Clear(input,0,input.Length);input[12*33+22]=new UnityEngine.Color(4,0,0,1);Upload(input);
    motion.arc=90;motion.direction=DCFApixels.WhimTex.BlurLayerBehaviour.MotionDirection.Forward;
    var spin=Render();Check(spin[18*33+16].a>.01f && spin[6*33+16].a<.002f,"Circular forward is counterclockwise on a nonsquare canvas");
    motion.center=new UnityEngine.Vector2(22.5f/33f,12.5f/25f);
    Check(Render()[12*33+22].a>.995f,"Offset center fixed");
    motion.center=new UnityEngine.Vector2(.5f,.5f);
    for(int i=0;i<input.Length;i++) input[i]=new UnityEngine.Color(.25f,.5f,2f,.7f);Upload(input);
    foreach(var mode in new[]{DCFApixels.WhimTex.BlurType.Linear,DCFApixels.WhimTex.BlurType.Circular})
    foreach(var edge in new[]{DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Clamp,
        DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Repeat,DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Mirror})
    {
        motion.mode=mode;motion.edges=edge;motion.arc=190;
        Same(input,Render(),.004f,"Constant image under "+mode+" "+edge);
    }
    motion.mode=DCFApixels.WhimTex.BlurType.Linear;
    motion.direction=DCFApixels.WhimTex.BlurLayerBehaviour.MotionDirection.Centered;
    motion.edges=DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Transparent;
    Check(Render()[0].a<.6f,"Transparent boundary fades");
    System.Array.Clear(input,0,input.Length);input[12*33]=new UnityEngine.Color(1,0,0,1);Upload(input);
    motion.edges=DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Repeat;
    var seam = Render(); Check(seam[12*33+32].a>.01f,"Repeat crosses seam: alpha=" + seam[12*33+32].a + ", left=" + seam[12*33].a);
    motion.edges=DCFApixels.WhimTex.BlurLayerBehaviour.EdgeMode.Clamp;
    Check(Render()[12*33+32].a<.001f,"Clamp does not cross seam");
    return "Motion Blur GPU checks passed: "+checks;
}
finally { UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(texture); }
