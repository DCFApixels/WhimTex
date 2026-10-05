// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using UnityEngine;
using DCFApixels.WhimTex;

public static class WhimTexGradientMidpointTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("WhimTexGradientMidpointTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    static void Check(bool value, string message) { context.True(value, message); }
    private static void ExecuteMain()
    {
        var g = new WhimTexGradient();
        g.SetKeys(new[] { new GradientColorKey(Color.black,0), new GradientColorKey(Color.white,1) },
            new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,1) });
        foreach (WhimTexGradientMode mode in Enum.GetValues(typeof(WhimTexGradientMode)))
        {
            g.Mode = mode;
            g.SetMidpoint(false,0,.5f); g.SetMidpoint(true,0,.5f);
            Color half = g.Evaluate(.5f);
            g.SetMidpoint(false,0,.2f); g.SetMidpoint(true,0,.8f);
            if (mode == WhimTexGradientMode.Fixed) continue;
            foreach (float smooth in new[] { 0f, .5f, 1f })
            {
                g.Smoothness = smooth;
                Check(Mathf.Abs(g.Evaluate(.2f).r-half.r)<.0001f,"Color midpoint "+mode);
                Check(Mathf.Abs(g.Evaluate(.8f).a-.5f)<.0001f,"Alpha midpoint "+mode);
            }
            g.Smoothness=1;
            const float e=.0001f;
            float before=(g.Evaluate(.2f).r-g.Evaluate(.2f-e).r)/e;
            float after=(g.Evaluate(.2f+e).r-g.Evaluate(.2f).r)/e;
            Check(Mathf.Abs(before-after)<.03f,"Midpoint tangent discontinuity "+mode);
        }
        var copy=JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(g));
        Check(copy.GetMidpoint(false,0)==.2f && copy.GetMidpoint(true,0)==.8f,"Serialization");
        g.Mode=WhimTexGradientMode.Classic;
        g.Evaluate(.3f);
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)g.Evaluate((i%100)/100f);
        Check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Warm allocations");
        return;
    }
}

