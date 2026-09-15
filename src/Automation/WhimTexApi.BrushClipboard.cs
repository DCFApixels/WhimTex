using System;
using System.Text;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        internal static PaintToolSettings ReadBrushClipboard(string text,out string url)
        {
            Require(text!=null && Encoding.UTF8.GetByteCount(text)<=1024*1024,"Brush JSON must be at most 1 MiB.");
            text=text.Trim().TrimStart('\uFEFF');
            if(text.StartsWith("```",StringComparison.Ordinal))
            {
                int newline=text.IndexOf('\n');
                Require(newline>=0 && (text.Substring(0,newline).Trim()=="```json" || text.Substring(0,newline).Trim()=="```") &&
                    text.EndsWith("```",StringComparison.Ordinal),"Copy one complete JSON block.");
                text=text.Substring(newline+1,text.Length-newline-4).Trim();
            }
            var root=Parse(text);
            Keys(root,"format","version","name","source","settings","url","code","resolution");
            Require(Text(root,"format")=="whimtex.brush","format must be whimtex.brush.");
            Require(Int(root,"version",0,1,1)==1,"Supported brush JSON version: 1.");
            Require(root["source"]!=null,"source is required: Standard or HLSL.");
            Text(root,"name");
            var result=new PaintToolSettings();
            var d=result.dynamics;
            d.source=Enum(root,"source",BrushTipSource.Standard);
            url=null;
            var settings=root["settings"]==null?new JObject():Obj(root["settings"],"settings");
            Keys(settings,"size","hardness","spacing","opacity","flow","scatter","scatterBias","sizeJitter","angleJitter",
                "angleOffset","flipX","flipY","rotationMode","randomAlgorithm","tintGradient","tipChannel","tipSdf",
                "mode","tipGradient","blend","blendApplication","seed");
            result.brushSize=Number(settings,"size",32,1,4096);
            result.brushHardness=Number(settings,"hardness",.8f,0,1);
            result.brushSpacing=Number(settings,"spacing",.16f,DrawingLayerBehaviour.MinimumBrushSpacing,DrawingLayerBehaviour.MaximumBrushSpacing);
            d.opacity=Number(settings,"opacity",1,0,1);d.flow=Number(settings,"flow",1,0,1);
            d.scatter=Number(settings,"scatter",0,0,4);d.scatterBias=Number(settings,"scatterBias",0,-1,1);
            d.sizeJitter=Number(settings,"sizeJitter",0,0,1);d.angleJitter=Number(settings,"angleJitter",0,0,180);
            d.angleOffset=Number(settings,"angleOffset",0,-180,180);
            d.flipX=Number(settings,"flipX",0,0,1);d.flipY=Number(settings,"flipY",0,0,1);
            d.rotationMode=Enum(settings,"rotationMode",BrushRotationMode.Fixed);
            d.randomAlgorithm=Enum(settings,"randomAlgorithm",BrushRandomAlgorithm.Random);
            d.tipChannel=Enum(settings,"tipChannel",BrushTipChannel.Alpha);d.tipSdf=Bool(settings,"tipSdf");
            string mode=Text(settings,"mode","Hardness");
            Require(mode=="Hardness" || mode=="Gradient","mode must be Hardness or Gradient.");
            d.proceduralMode=mode=="Gradient"?BrushProceduralMode.SdfGradient:BrushProceduralMode.Hardness;
            d.blend=Enum(settings,"blend",BlendMode.Normal);
            Require(d.blend!=BlendMode.None && d.blend!=BlendMode.Overwrite,"Use a color blend mode, not None or Overwrite.");
            d.blendApplication=Enum(settings,"blendApplication",BrushBlendApplication.Stroke);
            d.seed=Int(settings,"seed",1,1,int.MaxValue);
            if(settings["tipGradient"]!=null)d.tipGradient=ReadGradient(settings["tipGradient"],WhimTexGradientMode.Linear);
            if(settings["tintGradient"]!=null)d.tintGradient=ReadGradient(settings["tintGradient"]);
            if(d.source==BrushTipSource.Standard)
            {
                Require(root["code"]==null && root["resolution"]==null,"Standard supports an optional url, not code/resolution.");
                if(root["url"]!=null)
                {
                    url=Text(root,"url");
                    Require(Uri.TryCreate(url,UriKind.Absolute,out var uri) && (uri.Scheme=="https" || uri.Scheme=="http"),"url must be a direct HTTP(S) PNG/JPEG URL, without Markdown.");
                }
            }
            else if(d.source==BrushTipSource.HLSL)
            {
                Require(root["url"]==null,"HLSL uses code, not url.");
                Require(root["code"]?.Type==JTokenType.String,"HLSL requires code.");
                d.hlslCode=(string)root["code"];
                d.hlslParameters=BrushTipProgram.Parse(d.hlslCode,out _);
                d.hlslResolution=Int(root,"resolution",512,32,2048);
            }
            d.Normalize();
            return result;
        }
    }
}
