// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using DCFApixels.WhimTex;

// Run in the connected Editor. Transient gradients and Temp outputs only.
public static class WhimTexGradientRoundedTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("WhimTexGradientRoundedTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    static string CaptureKey(string runId)
    {
        if (!Guid.TryParseExact(runId, "D", out _)) throw new ArgumentException("Expected per-run GUID");
        return "WhimTex.Tests.UnityD.RoundedCapture." + runId;
    }
    static string CaptureFolder(string runId) => MigrationD.ProjectPath(
        "Temp/WhimTex/TestMigration/" + Guid.Parse(runId).ToString("N"));
    [Serializable] sealed class CaptureArtifact
    {
        public string name, encoding, content;
    }
    [Serializable] sealed class CaptureResult
    {
        public string status, message;
        public int checks;
        public string[] failures;
        public CaptureArtifact[] artifacts;
    }
    public static string Capture(string runId)
    {
        string key = CaptureKey(runId), folder = CaptureFolder(runId);
        if (UnityEditor.SessionState.GetString(key, "").Length != 0 || Directory.Exists(folder))
            return TestContext.Result("failed", 0, "Diagnostic fixture already exists; no files overwritten", "Cleanup the previous owned diagnostic GUID before reuse").ToJson();
        try
        {
            UnityEditor.SessionState.SetString(key, folder);
            Directory.CreateDirectory(folder);
            string path = ExecuteCapture(folder);
            var result = new CaptureResult {
                status = "skipped", checks = 0,
                message = "Diagnostic only: 12291 CSV samples at " + path + "; actual CSV retained in artifacts before owned cleanup",
                failures = Array.Empty<string>(),
                artifacts = new[] { new CaptureArtifact {
                    name = Path.GetFileName(path), encoding = "base64",
                    content = Convert.ToBase64String(File.ReadAllBytes(path)) } }
            };
            return (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
                .GetMethod("SerializeObject", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(object) }, null).Invoke(null, new object[] { result });
        }
        catch (Exception error)
        {
            try { RemoveCapture(runId); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            return TestContext.Result("failed", 0, "Diagnostic capture failed", error.ToString()).ToJson();
        }
    }
    public static string CleanupCapture(string runId)
    {
        try
        {
            RemoveCapture(runId);
            return TestContext.Result("passed", 0, "Cleanup acknowledgement only: owned diagnostic CSV removed; no regression assertions").ToJson();
        }
        catch (Exception error) { return TestContext.Result("failed", 0, "Diagnostic cleanup failed", error.ToString()).ToJson(); }
    }
    static void RemoveCapture(string runId)
    {
        string key = CaptureKey(runId), folder = UnityEditor.SessionState.GetString(key, "");
        if (folder.Length == 0) return;
        string expected = CaptureFolder(runId);
        string root = MigrationD.ProjectPath("Temp/WhimTex/TestMigration") + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(folder);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(full.Substring(root.Length), "N", out _) ||
            !string.Equals(full, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Diagnostic cleanup outside owned project GUID folder: " + folder);
        if (Directory.Exists(full)) Directory.Delete(full, true);
        UnityEditor.SessionState.EraseString(key);
    }

    static int checks;
    static void Near(float a, float b, float tolerance = .00001f)
    {
        checks++;
        context.True(!(float.IsNaN(a) || Mathf.Abs(a-b)>tolerance), a+" != "+b);
    }
    static WhimTexGradient Create(GradientColorKey[] colors) => new WhimTexGradient
        {Mode=WhimTexGradientMode.Perceptual}
        .WithKeys(colors);
    static WhimTexGradient WithKeys(this WhimTexGradient g, GradientColorKey[] colors)
    {
        g.SetKeys(colors, new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
        return g;
    }
    private static void ExecuteMain()
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
        return;
    }
    private static string ExecuteCapture(string folder)
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
        string path = Path.Combine(folder, "rounded-capture.csv");
        File.WriteAllText(path, csv.ToString());
        return path;
    }
}
