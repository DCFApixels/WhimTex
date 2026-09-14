// Unity Pipeline eval_file: managed algorithm regression, no persistent assets.
var f = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
var t = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ContentAwareFill", true);
var inputType = t.GetNestedType("Input", f);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
byte[] Mask(byte[] mask, UnityEngine.Color[] pixels, int w, int h, bool border, int width, bool empty = false) =>
    (byte[])t.GetMethod("TargetMask", f).Invoke(null, new object[] { mask, pixels, w, h, border, width, empty, System.Threading.CancellationToken.None });
object Input(UnityEngine.Color[] p, byte[] m, byte[] d, int w, int h, int seed = 1, int quality = 0)
{
    var input = System.Activator.CreateInstance(inputType, true);
    void Set(string n, object v) => inputType.GetField(n, f).SetValue(input, v);
    Set("pixels", p); Set("target", m); Set("donors", d); Set("width", w); Set("height", h); Set("seed", seed); Set("quality", quality);
    return input;
}
object Run(object input, System.Threading.CancellationToken token = default) => t.GetMethod("Run", f).Invoke(null, new object[] { input, token, null });
UnityEngine.Color[] Pixels(object result) => (UnityEngine.Color[])result.GetType().GetField("pixels", f).GetValue(result);
void Error(System.Action action, System.Type expected, string message)
{
    bool failed = false;
    try { action(); } catch (System.Reflection.TargetInvocationException e) { failed = expected.IsInstanceOfType(e.InnerException); }
    Check(failed, message);
}
const int w = 32, h = 24;
var colors = new UnityEngine.Color[w*h]; var selection = new byte[w*h]; var donors = new byte[w*h];
for (int y=0;y<h;y++) for(int x=0;x<w;x++)
{ int i=y*w+x; colors[i]=new UnityEngine.Color(3.5f,.125f,-.25f,1); donors[i]=255; if(x>=4&&x<28&&y>=3&&y<21)selection[i]=255; }
var strip = Mask(selection,colors,w,h,true,3);
for(int y=0;y<h;y++)for(int x=0;x<w;x++)
{
    int i=y*w+x;
    bool inside=x>=4&&x<28&&y>=3&&y<21;
    bool edge=inside && (x<7||x>=25||y<6||y>=18);
    Check(strip[i]==(edge?255:0),"Inward strip exact width, outside/center untouched");
}
var whole = new byte[w*h]; System.Array.Fill(whole,(byte)255);
var canvasBorder=Mask(whole,colors,w,h,true,2);
for(int y=0;y<h;y++)for(int x=0;x<w;x++)Check(canvasBorder[y*w+x]==((x<2||y<2||x>=w-2||y>=h-2)?255:0),"Canvas border counted");
whole[12*w+16]=0;
var holed=Mask(whole,colors,w,h,true,1);
Check(holed[12*w+16]==0&&holed[12*w+15]==255&&holed[11*w+15]==0,"Hole contour and diagonal distance");
selection[4*w+4]=96;
var soft=Mask(selection,colors,w,h,true,2);
Check(soft[4*w+4]==96,"Soft selection coverage preserved");
var emptyOnly=Mask(selection,colors,w,h,false,1,true);
foreach(byte coverage in emptyOnly)Check(coverage==0,"Visible pixels excluded in Transparent Only");
colors[8*w+8]=UnityEngine.Color.clear;
emptyOnly=Mask(selection,colors,w,h,false,1,true);
Check(emptyOnly[8*w+8]==255&&emptyOnly[8*w+9]==0,"Empty pixel targeted");
var filled=Pixels(Run(Input(colors,emptyOnly,donors,w,h)));
for(int i=0;i<colors.Length;i++)Check(emptyOnly[i]==0?filled[i].Equals(colors[i]):filled[i].Equals(new UnityEngine.Color(3.5f,.125f,-.25f,1)),"HDR retained and known pixels unchanged");
Check(colors[8*w+8].a==0,"Input is immutable");
Error(()=>Run(Input(colors,new byte[w*h],donors,w,h)),typeof(System.InvalidOperationException),"Empty targets rejected");
Error(()=>Run(Input(colors,whole,new byte[w*h],w,h)),typeof(System.InvalidOperationException),"Missing donors rejected");
Error(()=>Run(Input(colors,selection,donors,5000,5000)),typeof(System.ArgumentException),"Oversized job rejected before allocation");
using(var cancellation=new System.Threading.CancellationTokenSource())
{ cancellation.Cancel(); Error(()=>Run(Input(colors,selection,donors,w,h),cancellation.Token),typeof(System.OperationCanceledException),"Cancelled job exits"); }
// Odd dimensions exercise the coarse-to-fine pyramid and NNF upsampling.
int pw=97,ph=73; var pattern=new UnityEngine.Color[pw*ph];var hole=new byte[pw*ph];var sourceMask=new byte[pw*ph];
for(int y=0;y<ph;y++)for(int x=0;x<pw;x++)
{
    int i=y*pw+x; float stripe=(x%12<6?.2f:.7f);
    pattern[i]=new UnityEngine.Color(stripe, stripe*.6f, stripe*.3f,1);sourceMask[i]=255;
    if(x>=38&&x<57&&y>=26&&y<47){hole[i]=255;pattern[i]=UnityEngine.Color.clear;}
}
var watch=System.Diagnostics.Stopwatch.StartNew();
var a=Pixels(Run(Input(pattern,hole,sourceMask,pw,ph,123,1)));
var b=Pixels(Run(Input(pattern,hole,sourceMask,pw,ph,123,1)));
watch.Stop();
for(int i=0;i<a.Length;i++)
{
    Check(a[i].Equals(b[i]),"Fixed seed deterministic");
    Check(hole[i]!=0 || a[i].Equals(pattern[i]),"Pyramid preserves outside pixels");
    Check(a[i].a>.999f&&!float.IsNaN(a[i].r),"All holes filled with finite pixels");
}
// Invalid numeric pixels can never become exemplars.
var isDonor=t.GetMethod("IsDonor",f);
Check(!(bool)isDonor.Invoke(null,new object[]{new UnityEngine.Color(float.NaN,0,0,1)}),"NaN donor excluded");
Check(!(bool)isDonor.Invoke(null,new object[]{new UnityEngine.Color(0,float.PositiveInfinity,0,1)}),"Infinite donor excluded");
Check(!(bool)isDonor.Invoke(null,new object[]{UnityEngine.Color.clear}),"Transparent donor excluded");
// Visual comparison, test artifact only (not an imported project asset).
var preview=new UnityEngine.Texture2D(pw*2,ph,UnityEngine.TextureFormat.RGBA32,false);
try
{
    var output=new UnityEngine.Color[pw*2*ph];
    for(int y=0;y<ph;y++)for(int x=0;x<pw;x++)
    { output[y*pw*2+x]=pattern[y*pw+x];output[y*pw*2+pw+x]=a[y*pw+x]; }
    preview.SetPixels(output);preview.Apply();
    System.IO.File.WriteAllBytes("D:/DCFA/Projects/Test6.6/Temp/WhimTexContentFillTest.png",UnityEngine.ImageConversion.EncodeToPNG(preview));
}
finally { UnityEngine.Object.DestroyImmediate(preview); }
return $"Content-Aware Fill: {checks} checks passed; two 97x73 patterned fills: {watch.ElapsedMilliseconds}ms. Visual: Temp/WhimTexContentFillTest.png";
