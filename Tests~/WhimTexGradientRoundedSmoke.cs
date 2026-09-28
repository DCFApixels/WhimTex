using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using DCFApixels.WhimTex;

// Run in the connected Editor. Transient gradients and Temp outputs only.
public static class WhimTexGradientRoundedSmoke
{
    static int checks;
    static void Near(float a, float b, float tolerance = .00001f)
    {
        checks++;
        if (float.IsNaN(a) || Mathf.Abs(a-b)>tolerance) throw new Exception(a+" != "+b);
    }
    static WhimTexGradient Create(GradientColorKey[] colors) => new WhimTexGradient
        {Mode=WhimTexGradientMode.Perceptual}
        .WithKeys(colors);
    static WhimTexGradient WithKeys(this WhimTexGradient g, GradientColorKey[] colors)
    {
        g.SetKeys(colors, new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
        return g;
    }
    public static string Main()
    {
        checks=0;
        const float a=.5891156f;
        var g=Create(new[] {new GradientColorKey(Color.white,a),new GradientColorKey(Color.black,1)});
        var padded=Create(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,a),new GradientColorKey(Color.black,1)});
        // Independent analytic neutral fixture: OKLab lightness cubed to linear RGB.
        double d=.5*(1-a), left=a-d, right=a+d;
        for(int i=0;i<=10000;i++)
        {
            float t=i/10000f;
            double mapped=t;
            if(t<left) mapped=a;
            else if(t<right)
            {
                double u=(t-left)/(right-left);
                mapped=a+d*(2*u*u*u-u*u*u*u);
            }
            double l=Math.Max(0,Math.Min(1,(1-mapped)/(1-a))), v=l*l*l;
            float expected=(float)(v<=.0031308 ? v*12.92 : 1.055*Math.Pow(v,1/2.4)-.055);
            var c=g.Evaluate(t); var p=padded.Evaluate(t);
            for(int channel=0;channel<3;channel++) { Near(c[channel],expected); Near(c[channel],p[channel]); }
        }
        double keyLinear=Math.Pow(1-.5*.1875,3);
        Near(g.Evaluate(a).r,(float)(1.055*Math.Pow(keyLinear,1/2.4)-.055));
        Near(g.Evaluate(1).r,0,0);
        // Independent alpha boundaries cannot move RGB. Short held regions remain bounded.
        padded.SetKeys(padded.ColorKeys,new[] {new GradientAlphaKey(0,.1f),new GradientAlphaKey(1,.49f),
            new GradientAlphaKey(1,.51f),new GradientAlphaKey(0,.9f)});
        for(int i=0;i<=4000;i++)
        {
            float t=i/4000f; var c=padded.Evaluate(t);
            Near(c.r,g.Evaluate(t).r);
            Near(c.a,Mathf.Clamp01(c.a),0);
        }
        // A domain-edge patch has zero first/second derivatives; compare shrinking steps
        // in double precision through the analytic values rather than differencing 8-bit images.
        g=Create(new[] {new GradientColorKey(Color.black,0),new GradientColorKey(Color.white,1)});
        g.Mode=WhimTexGradientMode.Classic;
        Near(g.Evaluate(.0001f).r,0,1e-8f);
        Near(g.Evaluate(.9999f).r,1,1e-7f);
        // Prevent the previous 1.512x catch-up hump from returning.
        float maxSpeed=0;
        for(int i=1;i<500;i++)
        {
            float t=i/1000f;
            float speed=(g.Evaluate(t+.0005f).r-g.Evaluate(t-.0005f).r)/.001f;
            maxSpeed=Mathf.Max(maxSpeed,speed);
            Near(Mathf.Clamp(speed,0,1.19f),speed,.0001f);
        }
        Near(maxSpeed,1.1875f,.0002f);
        return "Rounded: "+checks+" analytic/duplicate-stop/independent-alpha/boundary checks passed.";
    }
    public static string Capture()
    {
        var csv=new StringBuilder("fixture,t,r,g,b,a\n");
        for(int fixture=0;fixture<3;fixture++)
        {
            var g=Create(fixture==2 ? new[] {new GradientColorKey(Color.white,.5891156f),new GradientColorKey(Color.black,1)} :
                new[] {new GradientColorKey(Color.white,0),new GradientColorKey(new Color(fixture==0?0:16/255f,81/255f,125/255f),.5f),new GradientColorKey(Color.red,1)});
            for(int i=0;i<=4096;i++)
            {
                float t=i/4096f; var c=g.Evaluate(t);
                csv.Append(fixture).Append(',').Append(t.ToString("R",CultureInfo.InvariantCulture));
                for(int j=0;j<4;j++)csv.Append(',').Append(c[j].ToString("R",CultureInfo.InvariantCulture));
                csv.Append('\n');
            }
        }
        Directory.CreateDirectory("Temp/WhimTex");
        File.WriteAllText("Temp/WhimTex/rounded-capture.csv",csv.ToString());
        return "Rounded: captured 12291 samples to Temp/WhimTex/rounded-capture.csv.";
    }
}
