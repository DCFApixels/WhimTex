// Pipeline run_script: NoiseScaleDistributionTests.Run. No documents or assets.
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class NoiseScaleDistributionTests
{
    static string ExecuteRun()
    {
        var method=typeof(NoiseLayerEditorWindow).GetMethod("RandomizeScale",BindingFlags.NonPublic|BindingFlags.Static);
        var sample=(Func<NoiseLayerBehaviour,System.Random,Vector2>)Delegate.CreateDelegate(
            typeof(Func<NoiseLayerBehaviour,System.Random,Vector2>),method);
        int checks=0;double worst=0;
        void Check(bool ok,string message){ WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, message); checks++; }
        int Bin(Vector2 v)=>Math.Max(0,Math.Min(39,(int)Math.Floor(Math.Log((v.x+v.y)*.5,2)*2+14)));
        double Weight(Vector2 v){double d=Math.Log((v.x+v.y)/16.0,2);return 1+Math.Exp(-d*d*.5);}
        var profiles = new[] { new Vector2(8,8), new Vector2(2,8), new Vector2(8,8),
            new Vector2(.01f,1000), new Vector2(1,100), new Vector2(100,1), new Vector2(.01f,10) };
        for(int profile=0;profile<profiles.Length;profile++)
        {
            var n=new NoiseLayerBehaviour{linkScale=profile!=2,
                Scale=profiles[profile],
                WarpScale=new Vector2(2,5),linkWarpScale=false};
            var old=n.Scale;var random=new System.Random(7231+profile);
            var actual=new int[40];var expected=new double[40];double total=0;
            double mean=(old.x+(double)old.y)*.5;
            double legalLow=mean*.01/Math.Min(old.x,old.y),legalHigh=mean*1000/Math.Max(old.x,old.y);
            double lo=Math.Max(1,legalLow),hi=Math.Min(64,legalHigh);
            if(lo>hi){lo=legalLow;hi=legalHigh;}
            Vector2 Candidate(double x,double y)
            {
                if(!n.linkScale)return new Vector2((float)x,(float)y);
                double m=lo*Math.Pow(hi/lo,Math.Log(x)/Math.Log(64));
                return new Vector2((float)(old.x*m/mean),(float)(old.y*m/mean));
            }
            int grid=n.linkScale?65536:512;
            for(int x=0;x<grid;x++)for(int y=0;y<(n.linkScale?1:grid);y++)
            {
                var v=Candidate(Math.Pow(64,(x+.5)/grid),Math.Pow(64,(y+.5)/grid));
                double w=Weight(v);Check(w>=1&&w<=2,"Weight remains within [1,2]");
                expected[Bin(v)]+=w;total+=w;
            }
            const int count=200000;float low=float.MaxValue,high=0;
            for(int i=0;i<count;i++)
            {
                var v=sample(n,random);actual[Bin(v)]++;
                Check(v.x>=.009999f&&v.y>=.009999f&&v.x<=1000.001f&&v.y<=1000.001f,"Legal axes");
                if(n.linkScale)Check(Math.Abs(v.y/v.x/(old.y/old.x)-1)<1e-5,"Linked proportions retained");
                low=Mathf.Min(low,(v.x+v.y)*.5f);high=Mathf.Max(high,(v.x+v.y)*.5f);
            }
            for(int b=0;b<actual.Length;b++)
            {
                double error=Math.Abs(actual[b]/(double)count-expected[b]/total);worst=Math.Max(worst,error);
                Check(error<.0035,"Distribution matches weighted baseline: profile="+profile+" bin="+b+" error="+error);
            }
            if(profile==0||profile==1||profile==4||profile==5)
            {
                Check(low<1.01&&high>63.9,"Both mean tails remain reachable regardless of linked ratio");
                Check(low>=.99999&&high<=64.00001,"Linked mean uses 1-64, not X");
            }
            Check(n.Scale==old && n.WarpScale==new Vector2(2,5) && !n.linkWarpScale,"Sampler has no model side effects");
        }
        var linked=new NoiseLayerBehaviour{linkScale=true,Scale=new Vector2(1,100)};
        var flipped=new NoiseLayerBehaviour{linkScale=true,Scale=new Vector2(100,1)};
        var equal=new NoiseLayerBehaviour{linkScale=true,Scale=new Vector2(8,8)};
        var rng=new System.Random(921);var rngFlip=new System.Random(921);var rngEqual=new System.Random(921);
        for(int i=0;i<10000;i++)
        {
            var a=sample(linked,rng);var b=sample(flipped,rngFlip);var c=sample(equal,rngEqual);
            Check(a.x==b.y&&a.y==b.x,"Axis exchange does not change mean distribution");
            Check(Math.Abs((a.x+(double)a.y)*.5-c.x)<.00001,"Same mean distribution as equal axes");
        }
        linked.Scale=new Vector2(1,100);
        for(int i=0;i<10000;i++)
        {
            linked.Scale=sample(linked,rng);
            double m=(linked.Scale.x+(double)linked.Scale.y)*.5;
            Check(m>=.99999&&m<=64.00001,"Repeated randomization does not drift upward");
            Check(Math.Abs(linked.Scale.y/linked.Scale.x-100)<.003,"Repeated randomization retains proportions");
        }
        return "PASS weighted Scale distribution: 1400000 distribution samples plus symmetry/repeated-call checks; "+checks+" checks; max bin error="+worst;
    }

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("NoiseScaleDistributionTests.Run", () => { ExecuteRun(); });
}
