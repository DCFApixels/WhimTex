using System;
using UnityEngine;
using DCFApixels.WhimTex;

public static class WhimTexGradientSmoke
{
    private static int checks;
    private static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    private static void Near(float a, float b, float epsilon = .0001f) => Check(Mathf.Abs(a-b) <= epsilon, a + " != " + b);
    public static string Main()
    {
        checks = 0;
        var g = new WhimTexGradient();
        var c = new[] { new GradientColorKey(new Color(.1f,.2f,.3f),0),
            new GradientColorKey(new Color(.4f,.6f,.8f),.27f), new GradientColorKey(new Color(2,3,4),1) };
        var a = new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(.6f,.7f), new GradientAlphaKey(1,1) };
        g.SetKeys(c,a);
        g.Mode = WhimTexGradientMode.Fixed;
        var native = new Gradient { mode = GradientMode.Fixed };
        native.SetKeys(c,a);
        for (int i=0;i<=1000;i++)
        {
            float t = Mathf.Clamp01((i + .37f) / 1000f);
            Color actual=g.Evaluate(t), expected=native.Evaluate(t);
            for(int k=0;k<4;k++) Near(actual[k],expected[k]);
        }
        Near(g.Evaluate(.27f).r, c[1].color.r);
        Near(g.Evaluate(.7f).a, a[1].alpha);
        g.ColorSpace=ColorSpace.Gamma;
        foreach (WhimTexGradientMode mode in new[] { WhimTexGradientMode.Classic, WhimTexGradientMode.Linear, WhimTexGradientMode.Perceptual })
        {
            g.Mode=mode;
            foreach(var key in c)
                for(int k=0;k<3;k++) Near(g.Evaluate(key.time)[k],key.color[k],.0002f);
            foreach(var key in a) Near(g.Evaluate(key.time).a,key.alpha);
            var clone=JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(g));
            for(int i=0;i<=100;i++)
            {
                Color result=g.Evaluate(i/100f), copy=clone.Evaluate(i/100f);
                for(int k=0;k<4;k++) { Check(!float.IsNaN(result[k])&&!float.IsInfinity(result[k]),"Nonfinite output"); Near(result[k],copy[k]); }
            }
        }
        g.Mode=WhimTexGradientMode.Classic;
        g.SetKeys(new[] { new GradientColorKey(Color.black,0), new GradientColorKey(Color.gray,.2f),
            new GradientColorKey(Color.white,1) },a);
        float dt=.0001f, center=g.Evaluate(.2f).r;
        float left=(center-g.Evaluate(.2f-dt).r)/dt, right=(g.Evaluate(.2f+dt).r-center)/dt;
        Near(left,right,.01f);
        for(int i=0;i<=1000;i++) Check(g.Evaluate(i/1000f).r>=0 && g.Evaluate(i/1000f).r<=1,"Overshoot");
        g.Smoothness=0;
        Near(g.Evaluate(.1f).r,.25f);
        var returned=g.ColorKeys; returned[0]=new GradientColorKey(Color.red,0);
        Near(g.Evaluate(0).r,0);
        var simple=new[] { new GradientColorKey(Color.black,0),new GradientColorKey(Color.white,1) };
        g.SetKeys(simple,a); g.Mode=WhimTexGradientMode.Linear;
        Near(g.Evaluate(.5f).r,.7353569f);
        g.Mode=WhimTexGradientMode.Classic; Near(g.Evaluate(.5f).r,.5f);
        var buffer=new Color[257]; g.Bake(buffer); Near(buffer[0].r,0); Near(buffer[256].r,1);
        g.Evaluate(.5f);
        long before=GC.GetAllocatedBytesForCurrentThread();
        float sink=0; for(int i=0;i<10000;i++) sink+=g.Evaluate((i%100)/100f).r;
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated==0,"Evaluate allocations: "+allocated);
        Check(sink>0,"Evaluation loop");
        bool rejected=false;
        try { g.SetKeys(new[] {simple[0],simple[0]},a); } catch(ArgumentException) { rejected=true; }
        Check(rejected,"Duplicate keys accepted");
        Near(g.Evaluate(1).r,1);
        return "WhimTexGradient: "+checks+" checks passed; Fixed equivalence, HDR, serialization, C1 joins, bounds, color spaces, zero warm allocations.";
    }
}
