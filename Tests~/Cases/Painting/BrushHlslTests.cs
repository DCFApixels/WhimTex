using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using UnityEngine.UIElements;

public static class BrushHlslTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;

    private static string BodyRun()
    {
        var assembly=typeof(WhimTexApi).Assembly;
        var stat=BindingFlags.Static|BindingFlags.NonPublic;
        var instance=BindingFlags.Instance|BindingFlags.NonPublic;
        var parser=typeof(WhimTexApi).GetMethod("ReadBrushClipboard",stat);
        var programType=assembly.GetType("DCFApixels.WhimTex.BrushTipProgram");
        var program=Activator.CreateInstance(programType,true);
        var bake=programType.GetMethod("Bake",instance);
        string source=(string)programType.GetField("DefaultSource",stat).GetRawConstantValue();
        void Check(bool value,string message){ T.True(value, message); }
        string Quote(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
        void Reject(string json)
        {
            try{parser.Invoke(null,new object[]{json,null});}
            catch(TargetInvocationException){T.True(true,"Invalid brush JSON rejected by parser");return;}
            T.True(false, "Invalid brush JSON accepted"); throw new Exception("Invalid brush JSON accepted");
        }
        Texture2D first=null,second=null;
        object settings=null;
        object restored=null;
        try
        {
            foreach(string kind in new[]{"Standard","Texture","HLSL"})
            {
                string extra=kind=="Texture"?",\"url\":\"https://example.com/tip.png\"":kind=="HLSL"?",\"code\":"+Quote(source):"";
                string json="{\"format\":\"whimtex.brush\",\"version\":1,\"source\":"+Quote(kind=="Texture"?"Standard":kind)+extra+"}";
                var args=new object[]{json,null};
                var parsed=parser.Invoke(null,args);
                Check(parsed!=null,kind+" parsed");
                Check(kind!="Texture" || (string)args[1]=="https://example.com/tip.png","URL preserved");
            }
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Texture\",\"url\":\"https://example.com/a.png\"}");
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"url\":\"file:///x.png\"}");
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"settings\":{\"flow\":2}}");
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"settings\":{\"typo\":1}}");
            var values=new List<ShaderFXParameter>();
            first=(Texture2D)bake.Invoke(program,new object[]{source,values,128});
            Check(first.width==128 && first.height==128,"Baked resolution");
            Check(first.GetPixel(64,64).a<.01f,"Ring center transparent");
            Check(first.GetPixel(105,64).a>.9f,"Ring visible");
            var shader=programType.GetField("shader",instance).GetValue(program);
            values.Add(new ShaderFXParameter{name="_Thickness",type=ShaderFXParameterType.Float,floatValue=.03f});
            second=(Texture2D)bake.Invoke(program,new object[]{source,values,128});
            Check(ReferenceEquals(shader,programType.GetField("shader",instance).GetValue(program)),"Parameter update reuses shader");
            Check(second.GetPixel(99,64).a<first.GetPixel(99,64).a,"Parameter affects cached pixels");
            string root="{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"HLSL\",\"code\":"+Quote(source)+",\"settings\":{\"tipChannel\":\"Luminance\",\"tipSdf\":true}}";
            settings=parser.Invoke(null,new object[]{root,null});
            var settingsType=settings.GetType();
            var dynamics=settingsType.GetField("dynamics").GetValue(settings);
            var dt=dynamics.GetType();
            var apply=settingsType.GetMethod("ApplyHlsl",instance);
            apply.Invoke(settings,new object[]{source,values,64});
            Check((bool)dt.GetField("tipSdf").GetValue(dynamics),"HLSL preserves SDF");
            Check(dt.GetField("tipChannel").GetValue(dynamics).ToString()=="Luminance","HLSL preserves channel");
            var before=(Texture2D)dt.GetField("tip").GetValue(dynamics);
            try{apply.Invoke(settings,new object[]{source+"\ninvalid syntax",values,64});T.True(false, "Invalid shader accepted"); throw new Exception("Invalid shader accepted");}
            catch(TargetInvocationException){T.True(true,"Invalid HLSL rejected by Bake");}
            Check(ReferenceEquals(dt.GetField("tip").GetValue(dynamics),before),"Failed shader keeps tip");
            string saved=JsonUtility.ToJson(settings);
            restored=JsonUtility.FromJson(saved,settingsType);
            var restoredDynamics=settingsType.GetField("dynamics").GetValue(restored);
            dt.GetField("tip").SetValue(restoredDynamics,null);
            Check((bool)settingsType.GetMethod("TryRestoreBrushTip",instance).Invoke(restored,null),"Restore HLSL after serialization");
            var restoredTip=(Texture2D)dt.GetField("tip").GetValue(restoredDynamics);
            Check(restoredTip!=before && restoredTip.width==64,"Restored independent tip");
            Check(restoredTip.GetPixel(50,32)==before.GetPixel(50,32),"Restored same pixels");
            Check((bool)dt.GetField("tipSdf").GetValue(restoredDynamics),"Restore retains SDF");
            var library=assembly.GetType("DCFApixels.WhimTex.BrushPresetLibrary");
            object preset=library.GetMethod("Capture",stat).Invoke(null,new[]{settings});
            string presetJson=JsonUtility.ToJson(preset);
            Check(presetJson.Contains("BrushTip(float2 uv)") && presetJson.Contains("_Thickness"),"Preset keeps source and parameters");
            string exported=(string)programType.GetMethod("Export",stat).Invoke(null,new object[]{source,values});
            Check(exported.StartsWith("// @whimtex-brush Shapes/Ring"),"Export header first");
            Check(exported.Contains("= 0.03"),"Export current values");
            var previousTarget=RenderTexture.active;
            bool previousSrgb=GL.sRGBWrite;
            foreach(string file in System.IO.Directory.GetFiles("Packages/com.dcfapixels.whimtex/Documentation~/Examples/Brushes","*.json"))
            {
                var example=parser.Invoke(null,new object[]{System.IO.File.ReadAllText(file),null});
                var exampleDynamics=settingsType.GetField("dynamics").GetValue(example);
                Check(example!=null,"Example parsed: "+file);
                if(dt.GetField("source").GetValue(exampleDynamics).ToString()!="HLSL")continue;
                try
                {
                    apply.Invoke(example,new object[]{dt.GetField("hlslCode").GetValue(exampleDynamics),dt.GetField("hlslParameters").GetValue(exampleDynamics),64});
                    Check((bool)dt.GetProperty("HasTip",instance).GetValue(exampleDynamics),"Example shader prepared");
                }
                finally{settingsType.GetMethod("ReleasePresetTip",instance).Invoke(example,null);}
            }
            Check(RenderTexture.active==previousTarget && GL.sRGBWrite==previousSrgb,"Baking preserves render state");
            DynamicChecks();
            DeltaChecks();
            SharedParameterChecks();
            FirstStampChecks();
            return null;
        }
        finally
        {
            if(first!=null)UnityEngine.Object.DestroyImmediate(first);
            if(second!=null)UnityEngine.Object.DestroyImmediate(second);
            ((IDisposable)program).Dispose();
            if(settings!=null)settings.GetType().GetMethod("ReleasePresetTip",instance).Invoke(settings,null);
            if(restored!=null)restored.GetType().GetMethod("ReleasePresetTip",instance).Invoke(restored,null);
        }
    }
    private static Func<TValue> ReadSettings<TValue>(object value) => () => (TValue)value;

    private static void DeltaChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var settingsType = typeof(WhimTexApi).Assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, flags).Invoke(target, args);
        object Get(object target, string name) => target.GetType().GetField(name, flags).GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, flags).SetValue(target, value);
        var settings = Activator.CreateInstance(settingsType, true);
        var layers = new List<DrawingLayerBehaviour>();
        Texture2D readback = null;
        const int width = 128, height = 64;
        Vector2 At(float x, float y) => new Vector2(x / width, y / height);
        DrawingLayerBehaviour Layer() { var result = new DrawingLayerBehaviour(); layers.Add(result); return result; }
        object Parameters() => Call(settings, "GetStrokeParameters", false, (Color?)Color.white);
        void Point(DrawingLayerBehaviour layer, float x, float y) => Call(layer, "PaintPoint", At(x,y), width, height, Parameters());
        void Segment(DrawingLayerBehaviour layer, float x0, float y0, float x1, float y1, object parameters = null)
            => Call(layer, "PaintSegment", At(x0,y0), At(x1,y1), width, height, false, parameters ?? Parameters());
        void PixelDelta(DrawingLayerBehaviour layer, int x, int y, float dx, float dy, string message)
        {
            var previous = RenderTexture.active;
            Color pixel;
            try
            {
                RenderTexture.active = (RenderTexture)Get(layer, "paintSurface");
                readback.ReadPixels(new Rect(0,0,width,height),0,0);
                pixel = readback.GetPixel(x,y);
            }
            finally { RenderTexture.active = previous; }
            T.Near(1, pixel.a, .002, message + ": covered pixel");
            T.Near(.5 + dx / 32, pixel.r, .002, message + ": GPU delta X");
            T.Near(.5 + dy / 32, pixel.g, .002, message + ": GPU delta Y");
        }
        void ContextDelta(object stamp, float dx, float dy, string message)
        {
            Vector2 delta = (Vector2)Get(Get(stamp, "dynamicContext"), "deltaPixels");
            T.Near(dx, delta.x, .001, message + ": X");
            T.Near(dy, delta.y, .001, message + ": Y");
        }
        System.Collections.IList Stamps(DrawingLayerBehaviour layer) => (System.Collections.IList)Get(layer, "segmentStamps");
        var motionType = typeof(WhimTexApi).Assembly.GetType("DCFApixels.WhimTex.BrushPointerMotion", true);
        object motion = Activator.CreateInstance(motionType);
        Vector2 MotionDelta() => (Vector2)motionType.GetProperty("Delta", flags).GetValue(motion);
        void Observe(float x, float y, int pointer = 0) => Call(motion, "Observe", new Vector2(x,y), pointer, 50f);
        void Direction(Vector2 expected, string message)
        {
            T.Near(expected.x, MotionDelta().x, .0001, message + ": X");
            T.Near(expected.y, MotionDelta().y, .0001, message + ": Y");
        }
        Observe(10,20);
        T.Equal(Vector2.zero, MotionDelta(), "First hover event anchors without inventing travel");
        Observe(13,16);
        Direction(new Vector2(.6f,-.8f), "First movement accepts its direction immediately");
        Observe(13,16);
        Direction(new Vector2(.6f,-.8f), "Stationary mouse-down preserves the filtered direction");
        Observe(100,100,5);
        T.Equal(Vector2.zero, MotionDelta(), "Switching input devices never connects their positions");
        Observe(100,96,5);
        Direction(Vector2.down, "New device records its own movement");
        Observe(float.NaN,96,5);
        T.Equal(Vector2.zero, MotionDelta(), "Invalid pointer data clears history");
        Observe(200,200,5);
        T.Equal(Vector2.zero, MotionDelta(), "Re-entry after reset anchors without a jump");
        float FilterDistance(float size) => (float)motionType.GetMethod("SmoothingDistance", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { size });
        T.Near(3, FilterDistance(10), .0001, "Small visible brush uses minimum hover smoothing distance");
        T.Near(4, FilterDistance(50), .0001, "Hover smoothing scales mildly with visible brush size");
        T.Near(6, FilterDistance(100), .0001, "Large visible brush caps hover smoothing distance");
        foreach (float size in new[] { -10f, float.NaN, float.PositiveInfinity })
            T.Near(3, FilterDistance(size), .0001, "Invalid visible brush size has a finite short fallback");
        motion = Activator.CreateInstance(motionType);
        Observe(0,20); Observe(0,0); Observe(1,0);
        float decay = Mathf.Exp(-.25f);
        Direction(new Vector2(1-decay,-decay).normalized, "One-pixel sideways jitter is distance-weighted");
        T.True(MotionDelta().y < -.95f, "One-pixel jitter does not replace a downward approach with a sideways direction");
        Observe(20,0);
        T.True(MotionDelta().x > .999f, "Sustained motion promptly replaces the old hover direction");
        Vector2 SamplePath(int subdivisions)
        {
            motion = Activator.CreateInstance(motionType);
            Observe(0,20); Observe(0,0);
            for (int i=1; i<=subdivisions; i++) Observe(8f*i/subdivisions,0);
            for (int i=1; i<=subdivisions; i++) Observe(8,6f*i/subdivisions);
            return MotionDelta();
        }
        Vector2 sparse = SamplePath(1);
        foreach (int subdivisions in new[] { 2, 10, 100 })
        {
            Vector2 dense = SamplePath(subdivisions);
            T.Near(sparse.x, dense.x, .0001, "Hover X is independent of event subdivision");
            T.Near(sparse.y, dense.y, .0001, "Hover Y is independent of event subdivision");
        }
        motion = Activator.CreateInstance(motionType);
        Observe(0,20); Observe(0,0);
        float reversal = 4f*Mathf.Log(2f);
        Observe(0,reversal);
        Direction(Vector2.down, "Exactly opposed directions retain a valid direction at cancellation");
        Observe(0,reversal+4f);
        Direction(Vector2.up, "Continued reversal resolves cancellation without inventing a sideways direction");
        Observe(float.PositiveInfinity,0);
        T.Equal(Vector2.zero, MotionDelta(), "Infinite pointer data clears history");
        Observe(-float.MaxValue,0); Observe(float.MaxValue,0);
        T.Equal(Vector2.zero, MotionDelta(), "Overflowing pointer travel cannot poison direction history");
        object viewport = Activator.CreateInstance(typeof(WhimTexApi).Assembly.GetType("DCFApixels.WhimTex.CanvasViewport", true), true);
        Call(viewport, "SetRotation", 90f, false);
        Vector2 mapped = (Vector2)Call(viewport, "ToCanvasDelta", new Vector2(3,-4));
        T.Near(-4, mapped.x, .0001, "Hover direction follows rotated Canvas View X");
        T.Near(-3, mapped.y, .0001, "Hover direction follows rotated Canvas View Y before Y-up conversion");
        try
        {
            Set(settings, "brushSize", 4f); Set(settings, "brushSpacing", 1f);
            object dynamics = Get(settings, "dynamics");
            Set(dynamics, "tipChannel", Enum.Parse(Get(dynamics, "tipChannel").GetType(), "Color"));
            string code = "// @whimtex-brush Tests/Delta\nfloat4 BrushTip(float2 uv, BrushSequence brush, BrushMotion motion) { return float4(0.5+motion.deltaPixels/32.0,brush.stampIndex/32.0,1); }";
            Call(settings, "ApplyHlsl", code, new List<ShaderFXParameter>(), 64);
            readback = new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var a = Layer();
            Call(a, "BeginStroke", At(16,32));
            try
            {
                Point(a,16,32);
                PixelDelta(a,16,32,0,0,"First stamp starts at zero");
                Segment(a,16,32,24,32);
                PixelDelta(a,24,32,4,0,"Horizontal logical spacing");
                Segment(a,24,32,24,44);
                PixelDelta(a,24,44,0,4,"Y points upward in non-square canvas pixels");
                Segment(a,24,44,16,44);
                PixelDelta(a,16,44,-4,0,"Signed delta survives GPU transport");
                Point(a,16,44);
                PixelDelta(a,16,44,0,0,"Stationary stamp has zero delta");
            }
            finally { Call(a, "EndStroke"); }
            Call(a, "BeginStroke", At(80,48));
            try { Point(a,80,48); PixelDelta(a,80,48,0,0,"New stroke ignores pointer travel"); }
            finally { Call(a, "EndStroke"); }
            var b = Layer();
            Call(b,"BeginStroke",At(16,12));
            try
            {
                Point(b,16,12);
                Segment(b,16,12,18,12);
                T.Equal(0,Stamps(b).Count,"Sub-spacing input event emits no stamp");
                Segment(b,18,12,18,18);
                PixelDelta(b,18,14,2,2,"Delta is the chord from the previous stamp across a corner");
                PixelDelta(b,18,18,0,4,"Next stamp follows current segment");
            }
            finally { Call(b,"EndStroke"); }
            Set(dynamics,"rotationMode",Enum.Parse(Get(dynamics,"rotationMode").GetType(),"StrokeDirection"));
            Set(dynamics,"angleOffset",30f);
            var sequence = dynamics.GetType().GetProperty("Sequence",flags).GetValue(dynamics);
            double HoverDistance() => (double)sequence.GetType().GetProperty("TotalDistance",flags).GetValue(sequence);
            foreach (Vector2 delta in new[] { new Vector2(3,4), new Vector2(-3,4), new Vector2(-3,-4), new Vector2(3,-4) })
            {
                var seeded = Layer();
                double before = HoverDistance();
                Call(seeded,"BeginStroke",At(64,32));
                Call(seeded,"SetInitialBrushDelta",delta);
                try
                {
                    Point(seeded,64,32);
                    PixelDelta(seeded,64,32,delta.x * .8f,delta.y * .8f,"First stamp receives a spacing-length phantom delta on GPU");
                    object stamp = Stamps(seeded)[0];
                    T.Near(Mathf.Atan2(delta.y,delta.x) + 30*Mathf.Deg2Rad, (float)Get(stamp,"rotation"), .0001,
                        "First direction rotation uses the same delta, then Angle Offset");
                    object context = Get(stamp,"dynamicContext");
                    T.Near(0,(float)Get(context,"distance"),.0001,"Hover does not extend stroke distance");
                    T.Equal(0u,(uint)Get(context,"stampIndex"),"Hover consumes no stamp indices");
                    T.Near(before,HoverDistance(),.0001,"Hover does not advance sequence distance");
                    Segment(seeded,64,32,72,32);
                    PixelDelta(seeded,72,32,4,0,"Later stamps use actual stroke movement instead of hover");
                    T.Near(30*Mathf.Deg2Rad,(float)Get(Stamps(seeded)[0],"rotation"),.0001,"Actual movement replaces the initial direction");
                }
                finally { Call(seeded,"EndStroke"); }
            }
            Set(dynamics,"rotationMode",Enum.Parse(Get(dynamics,"rotationMode").GetType(),"Fixed"));
            var fixedTip = Layer();
            Call(fixedTip,"BeginStroke",At(64,32)); Call(fixedTip,"SetInitialBrushDelta",new Vector2(0,5));
            try
            {
                Point(fixedTip,64,32);
                PixelDelta(fixedTip,64,32,0,4,"Fixed tip still exposes hover direction to its HLSL");
                T.Near(30*Mathf.Deg2Rad,(float)Get(Stamps(fixedTip)[0],"rotation"),.0001,"Hover does not rotate a Fixed tip");
                Point(fixedTip,64,32);
                ContextDelta(Stamps(fixedTip)[0],0,0,"Initial delta is used only once, not on stationary stamps");
            }
            finally { Call(fixedTip,"EndStroke"); }
            Set(dynamics,"angleOffset",0f);
            var pattern = Layer();
            Set(pattern,"repeatMode",PaintRepeatMode.Mirror); Set(pattern,"mirrorAcrossVerticalAxis",true);
            Call(pattern,"BeginStroke",At(16,20));
            try
            {
                Point(pattern,16,20); Segment(pattern,16,20,24,20);
                PixelDelta(pattern,104,20,4,0,"Mirrored copy uses the original stroke basis and preserves clipping");
            }
            finally { Call(pattern,"EndStroke"); }
            Set(dynamics,"scatter",.7f); Set(dynamics,"angleOffset",75f);
            Call(pattern,"BeginStroke",At(32,20));
            try
            {
                Point(pattern,32,20);
                foreach (object stamp in Stamps(pattern)) ContextDelta(stamp,0,0,"First scattered copy");
                Segment(pattern,32,20,48,20);
                T.Equal(8,Stamps(pattern).Count,"Four scattered logical stamps with two mirror copies");
                foreach (object stamp in Stamps(pattern)) ContextDelta(stamp,4,0,"Scatter/rotation/mirror do not modify delta");
            }
            finally { Call(pattern,"EndStroke"); }
            Set(dynamics,"scatter",0f); Set(dynamics,"angleOffset",0f);
            var clipped = Layer();
            Call(clipped,"BeginStroke",At(-16,32));
            Call(clipped,"SetInitialBrushDelta",new Vector2(0,5));
            try
            {
                Point(clipped,-16,32);
                T.Equal(0,Stamps(clipped).Count,"Initial logical stamp is clipped");
                Segment(clipped,-16,32,0,32);
                T.Equal(1,Stamps(clipped).Count,"Only returning edge stamp is emitted");
                ContextDelta(Stamps(clipped)[0],4,0,"Clipped stamps still define logical delta");
                Segment(clipped,0,32,4,32);
                PixelDelta(clipped,4,32,4,0,"Clipped history continues into visible painting");
            }
            finally { Call(clipped,"EndStroke"); }
            var wrapped = Layer();
            Call(wrapped,"BeginStroke",At(124,32));
            try
            {
                Point(wrapped,124,32);
                Segment(wrapped,124,32,132,32,Call(Parameters(),"WithCanvasWrap"));
                PixelDelta(wrapped,4,32,4,0,"Wrapping does not introduce a canvas-width jump");
            }
            finally { Call(wrapped,"EndStroke"); }
            Call(a,"BeginStroke",At(16,56)); Stamps(a).Clear();
            Call(a,"SetInitialBrushDelta",new Vector2(3,-4));
            try
            {
                Call(a,"BuildBrushSegment",At(16,56),At(112,56),width,height,true,Parameters(),96f,2);
                T.Equal(25u,(uint)Get(a,"brushStampIndex"),"Budget advances all logical stamps");
                T.Equal(2,Stamps(a).Count,"Budget emits only two stamps");
                ContextDelta(Stamps(a)[0],2.4f,-3.2f,"Budget first stamp uses spacing-length phantom delta");
                ContextDelta(Stamps(a)[1],4,0,"Budget delta uses previous logical stamp, not previous emitted stamp");
                Stamps(a).Clear();
                Call(a,"BuildBrushSegment",At(112,56),At(116,56),width,height,false,Parameters(),4f,2);
                T.Equal(1,Stamps(a).Count,"Next input event emits one stamp");
                ContextDelta(Stamps(a)[0],4,0,"Next event retains the last logical, budget-skipped position");
            }
            finally { Call(a,"EndStroke"); }

            // Pressure Size controls both logical spacing and the Motion context, including budget skips.
            Set(settings, "brushSize", 8f); Set(settings, "brushSpacing", .5f);
            Set(Get(dynamics, "pressureSize"), "enabled", true);
            Set(Get(dynamics, "pressureFlow"), "enabled", false);
            var pressurePath = Layer();
            Call(pressurePath, "BeginStroke", At(16,32));
            Call(pressurePath, "SetKnownBrushDirection", Vector2.right);
            try
            {
                Call(pressurePath, "PaintPoint", At(16,32), width, height, Call(Parameters(), "WithPressure", .25f));
                PixelDelta(pressurePath,16,32,1,0,"Pressure-sized phantom step reaches GPU Motion");
                Segment(pressurePath,16,32,24,32,Call(Parameters(), "WithPressure", 1f));
                var logical = Stamps(pressurePath);
                T.Equal(4, logical.Count, "Growing Size updates the next logical step");
                float previousX = 16f;
                foreach (object stamp in logical)
                {
                    float x = ((Vector2)Get(stamp,"center")).x * width;
                    ContextDelta(stamp,x-previousX,0,"Motion follows pressure-sized logical positions");
                    previousX = x;
                }
                Call(pressurePath,"BeginStroke",At(16,32));
                Call(pressurePath,"SetKnownBrushDirection",Vector2.right);
                Call(pressurePath,"PaintPoint",At(16,32),width,height,Call(Parameters(),"WithPressure",.25f));
                Stamps(pressurePath).Clear();
                Call(pressurePath,"BuildBrushSegment",At(16,32),At(24,32),width,height,false,Parameters(),8f,2);
                T.Equal(5u,(uint)Get(pressurePath,"brushStampIndex"),"Variable-spacing budget advances every logical index");
                T.Equal(2,Stamps(pressurePath).Count,"Variable-spacing budget limits emitted dabs");
                ContextDelta(Stamps(pressurePath)[1],1.890625f,0,"Skipped pressure-sized stamp still defines Motion delta");
                T.Equal(3u,(uint)Get(Get(Stamps(pressurePath)[1],"dynamicContext"),"stampIndex"),"Rendered context retains its logical index");
            }
            finally { Call(pressurePath,"EndStroke"); }
            Set(Get(dynamics,"pressureSize"),"enabled",false);
            Set(Get(dynamics,"pressureFlow"),"enabled",true);
        }
        finally
        {
            foreach (var layer in layers) Call(layer,"ReleaseTransientResources");
            Call(settings,"ReleasePresetTip");
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
        }
    }

    private static void SharedParameterChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic;
        var assembly = typeof(WhimTexApi).Assembly;
        var settingsType = assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
        var programType = assembly.GetType("DCFApixels.WhimTex.BrushTipProgram", true);
        object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, flags).Invoke(value, args);
        object Get(object value, string field) => value.GetType().GetField(field, flags)?.GetValue(value)
            ?? value.GetType().GetProperty(field, flags)?.GetValue(value);
        string declarations = "// @whimtex-brush Tests/Shared Parameters\n// @control(_Enabled)\n" +
            "// @group(Pattern; _Enabled)\n// @param bool _Enabled = true\n" +
            "// @param enum _Mode = Soft {Soft: 0, Hard: 1}\n// @if _Mode == 1\n" +
            "// @header(Detail)\n// @helpbox(Conditional detail.)\n// @param label(\"Detail Amount\") float _Detail = 0.25 [0 .. ~1] // Detail tooltip.\n// @endif\n" +
            "// @param float _Gain = 0.5 [0 .. ~1]\n// @param enum _Gain {Half: 0.5, Full: 1}\n" +
            "// @param hidden float _Hidden = 0.25\n// @param float2 _Offset = (0.1, 0.2)\n" +
            "// @param point _Center = (0.5, 0.5)\n// @param float3 _Axis = (0, 1, 0)\n" +
            "// @param normal _Normal = (0, 0, 2)\n// @param float4 _Data = (1, 2, 3, 4)\n" +
            "// @param color _Tint = (0.5, 0.25, 1, 1)\n// @param texture2D _Texture\n" +
            "// @param transform2D _Area = (0.5, 0.5, 1, 1, 0)\n" +
            "// @param gradient _Ramp = #FF0000 -> #0000FF\n// @param curve _Profile = linear\n// @endgroup\n";
        string body = "{ float2 p = _Area_ToLocal(uv); return float4(_Ramp_Sample(p.x).r * _Gain + _Offset.x, " +
            "_Profile_Sample(p.y) * _Tint.g, (_Normal.z + _Axis.y + _Data.x + _Center.x + _Hidden) / 8, " +
            "tex2D(_Texture, uv).a * (_Enabled > 0.5 ? 1 : 0)); }";
        string code = declarations + "float4 BrushTip(float2 uv) " + body;
        object settings = Activator.CreateInstance(settingsType, true);
        object restored = null;
        object textureRestored = null;
        DrawingLayerBehaviour layer = null;
        Texture2D readback = null;
        try
        {
            Call(settings, "ApplyHlsl", code, new List<ShaderFXParameter>(), 64);
            object dynamics = Get(settings, "dynamics");
            List<ShaderFXParameter> Values() => (List<ShaderFXParameter>)Get(dynamics, "hlslParameters");
            var tip = (Texture2D)Get(dynamics, "tip");
            float ExpectedRed(float uv) => Values().Find(p => p.name == "_Ramp").gradientValue.Evaluate(uv).linear.r * .5f + .1f;
            T.Near(ExpectedRed(32.5f / 64), tip.GetPixel(32, 32).r, .005, "Gradient helper and float2 reach Static GPU in linear space");
            T.Near((32.5f / 64) * new Color(.5f,.25f,1,1).linear.g, tip.GetPixel(32, 32).g, .002, "Curve and encoded color reach Static GPU in linear space");
            T.Near(.46875, tip.GetPixel(32, 32).b, .002, "Point, normal, float3/4 and hidden values reach GPU");
            T.Near(1, tip.GetPixel(32, 32).a, .001, "Texture parameter defaults to white");
            var sourceType = assembly.GetType("DCFApixels.WhimTex.BrushParameterViewSource", true);
            var read = typeof(BrushHlslTests).GetMethod("ReadSettings", stat).MakeGenericMethod(settingsType).Invoke(null, new[] { settings });
            Action<Action> apply = action => action();
            Action<string> error = message => throw new Exception(message);
            object source = Activator.CreateInstance(sourceType, flags, null, new object[] { read, apply, error }, null);
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView", true);
            var view = (VisualElement)Activator.CreateInstance(viewType, flags, null, new object[] { source, false }, null);
            var main = (VisualElement)Activator.CreateInstance(viewType, flags, null, new object[] { source, true }, null);
            T.True(view.Query<Vector2Field>().ToList().Count >= 4, "Shared vector/transform fields render for brush");
            T.Equal(1, view.Query<UnityEditor.UIElements.CurveField>().ToList().Count, "Curve uses shared renderer");
            T.Equal(1, main.Query<Toggle>().ToList().Count, "@control renders in compact brush field");
            var condition = view.Q<VisualElement>(className: "whimtex-fx-conditional-parameter");
            T.True(condition.ClassListContains("whimtex-fx-conditional-parameter--hidden"), "Conditional field initially hidden");
            var dropdown = view.Query<DropdownField>().ToList().Find(f => f.label == "Mode");
            object lut = null;
            var bindings = Get(Get(dynamics, "hlslProgram"), "parameterBindings");
            foreach (var value in ((System.Collections.IDictionary)Get(bindings,"gradients")).Values) { lut = value; break; }
            int bakes = (int)Get(lut,"BakeCount");
            Call(view, "Change", Values().Find(p => p.name == "_Mode").id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f));
            T.Equal("Hard", dropdown.value, "Shared edit refreshes the enum field");
            T.Equal(bakes, (int)Get(lut,"BakeCount"), "Scalar edit reuses an unchanged gradient LUT despite copied parameter values");
            T.True(!condition.ClassListContains("whimtex-fx-conditional-parameter--hidden"), "Enum change updates condition");
            T.Equal(1f, Values().Find(p => p.name == "_Mode").floatValue, "Brush callback writes current parameter list");
            T.Equal(2, Values().Find(p => p.name == "_Gain").controls.Count, "Linked controls share one uniform");
            T.Equal(1f, Values().Find(p => p.name == "_Normal").vectorValue.z, "Normal is normalized");
            T.True(view.Q<Button>("editFXTransform") == null && view.Q<Button>("editFXPoint") == null, "Brush has no FX canvas handles");
            object shader = Get(Get(dynamics, "hlslProgram"), "shader");
            var changed = new List<ShaderFXParameter>(Values());
            changed.Find(p => p.name == "_Texture").textureSource = ShaderFXTextureSource.None;
            Call(settings, "ApplyHlsl", code, changed, 64);
            T.Near(0, ((Texture2D)Get(dynamics, "tip")).GetPixel(32,32).a, .001, "None samples transparent black");
            T.True(ReferenceEquals(shader, Get(Get(dynamics,"hlslProgram"),"shader")), "Resource edits reuse Static shader");
            changed.Find(p => p.name == "_Texture").textureSource = ShaderFXTextureSource.Texture;
            string exported = (string)programType.GetMethod("Export", stat).Invoke(null, new object[] { code, changed });
            T.True(exported.Contains("@group(Pattern; _Enabled)") && exported.Contains("@if _Mode == 1") &&
                exported.Contains("@param hidden float _Hidden") && exported.Contains("@param enum _Gain"), "Export preserves shared metadata");
            T.True(exported.Contains("@control(_Enabled)") && exported.Contains("Detail tooltip."), "Export keeps compact control and tooltip");
            restored = JsonUtility.FromJson(JsonUtility.ToJson(settings), settingsType);
            var restoredDynamics = Get(restored, "dynamics");
            var restoredValues = (List<ShaderFXParameter>)Get(restoredDynamics, "hlslParameters");
            T.True(restoredValues.Find(p => p.name == "_Ramp").gradientValue != null &&
                restoredValues.Find(p => p.name == "_Profile").curveValue.length == 2, "Rich values survive serialization");
            string dynamicCode = declarations + "float4 BrushTip(float2 uv, BrushSequence brush) " + body;
            Call(settings, "ApplyHlsl", dynamicCode, changed, 64);
            settingsType.GetField("brushSize", flags).SetValue(settings, 32f);
            object channel = Get(dynamics, "tipChannel");
            dynamics.GetType().GetField("tipChannel", flags).SetValue(dynamics, Enum.Parse(channel.GetType(), "Color"));
            layer = new DrawingLayerBehaviour();
            Call(layer, "BeginStroke", new Vector2(.5f,.5f));
            try { Call(layer, "PaintPoint", new Vector2(.5f,.5f), 64,64,Call(settings,"GetStrokeParameters",false,(Color?)Color.white)); }
            finally { Call(layer, "EndStroke"); }
            readback = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            var previous = RenderTexture.active;
            try { RenderTexture.active = (RenderTexture)Get(layer,"paintSurface"); readback.ReadPixels(new Rect(0,0,64,64),0,0); }
            finally { RenderTexture.active = previous; }
            T.Near(ExpectedRed(.5f), readback.GetPixel(32,32).r, .03, "Gradient/curve/textures/transform work in Dynamic stamp shader");
            T.True(Get(dynamics,"tip") == null, "Rich Dynamic parameters still avoid tip baking");
            bool rejected = false;
            try { programType.GetMethod("Parse",stat).Invoke(null,new object[]{code.Replace("@param texture2D _Texture","@param texture2D _Texture = self"),null}); }
            catch(TargetInvocationException) { rejected = true; }
            T.True(rejected, "FX-only Self source rejected clearly for brush");
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.dcfapixels.whimtex/src/WhimTexCanvasViewBackdrop.png");
            T.True(asset != null, "Existing package texture available for read-only reference checks");
            var textured = new List<ShaderFXParameter>(Values());
            textured.Find(p=>p.name=="_Texture").textureValue = asset;
            Call(settings,"ApplyHlsl",dynamicCode,textured,64);
            textureRestored = JsonUtility.FromJson(JsonUtility.ToJson(settings),settingsType);
            var reloadedValues = (List<ShaderFXParameter>)Get(Get(textureRestored,"dynamics"),"hlslParameters");
            reloadedValues.Find(p=>p.name=="_Texture").textureValue = null;
            Call(textureRestored,"ApplyHlsl",dynamicCode,reloadedValues,64);
            T.True(ReferenceEquals(asset,((List<ShaderFXParameter>)Get(Get(textureRestored,"dynamics"),"hlslParameters")).Find(p=>p.name=="_Texture").textureValue),
                "Settings restore texture GUID/local ID without a live object reference");
            var library = assembly.GetType("DCFApixels.WhimTex.BrushPresetLibrary",true);
            var preset = library.GetMethod("Capture",stat).Invoke(null,new[]{settings});
            var presetReloaded = JsonUtility.FromJson(JsonUtility.ToJson(preset),preset.GetType());
            var presetValues = (List<ShaderFXParameter>)Get(Get(presetReloaded,"dynamics"),"hlslParameters");
            presetValues.Find(p=>p.name=="_Texture").textureValue = null;
            assembly.GetType("DCFApixels.WhimTex.BrushParameterTextureReference",true).GetMethod("Restore",stat)
                .Invoke(null,new object[]{presetValues,Get(presetReloaded,"parameterTextures")});
            T.True(ReferenceEquals(asset,presetValues.Find(p=>p.name=="_Texture").textureValue), "Preset transport restores texture identity");
        }
        finally
        {
            if (layer != null) Call(layer,"ReleaseTransientResources");
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (restored != null) Call(restored,"ReleasePresetTip");
            if (textureRestored != null) Call(textureRestored,"ReleasePresetTip");
            Call(settings,"ReleasePresetTip");
        }
    }

    private static void DynamicChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = typeof(WhimTexApi).Assembly;
        var programType = assembly.GetType("DCFApixels.WhimTex.BrushTipProgram", true);
        var settingsType = assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, flags).Invoke(target, args);
        object Get(object target, string name) => target.GetType().GetField(name, flags)?.GetValue(target)
            ?? target.GetType().GetProperty(name, flags)?.GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, flags).SetValue(target, value);
        var readMode = programType.GetMethod("ReadDynamic", BindingFlags.Static | BindingFlags.NonPublic);
        bool Mode(string text) => (bool)readMode.Invoke(null, new object[] { text });
        string body = "float4 BrushTip(float2 uv, BrushSequence brush) { return float4(_Color*brush.distance/128,brush.totalDistance/128,(brush.strokeIndex*16u+brush.stampIndex)/128.0,brush.seed==2147483647u?1:0); }";
        string code = "// @whimtex-brush Tests/Context\n// @param float _CheckIndex = 0 [0 .. 2]\n// @param float _Color = 1 [0 .. 1]\n" + body.Replace("return float4", "if (_CheckIndex>1.5) return float4(BrushRandom(brush.seed,brush.strokeIndex,brush.stampIndex),BrushRandom(brush.seed,brush.stampIndex),0,1); if (_CheckIndex>0.5) return float4(1,1,1,brush.stampIndex==16777217u?1:0); return float4");
        T.True(Mode(code), "Actual Dynamic entry point determines mode");
        T.True(!Mode("float4 BrushTip(float2 uv) { return 1; } // BrushSequence\nvoid Helper(BrushSequence brush) {}"), "Helper context and comments do not change Static mode");
        T.True(Mode("float4 BrushTip(float2 uv);\n" + code), "Prototypes do not select a second entry point");
        T.True(!Mode("/*" + body + "*/\nfloat4 BrushTip(float2 uv) { return 1; }"), "Commented Dynamic entry point ignored");
        foreach (string invalid in new[] { code + "\nfloat4 BrushTip(float2 uv) { return 1; }", "float4 BrushTip(float2 uv);", "float4 BrushTip(float2 uv, float x) { return 1; }",
            "float4 BrushTip(BrushSequence sequence) { return 1; }", "float4 BrushTip(float2 a, float2 b) { return 1; }",
            "float4 BrushTip(float2 uv, BrushMotion a, BrushMotion b) { return 1; }",
            "float4 BrushTip(out float2 uv) { return 1; }", "float4 BrushTip(float2 uv, inout BrushSequence s) { return 1; }" })
        {
            bool rejected = false;
            try { Mode(invalid); } catch (TargetInvocationException) { rejected = true; }
            T.True(rejected, "Ambiguous, missing or unsupported signature rejected");
        }
        object settings = Activator.CreateInstance(settingsType, true);
        var layers = new List<DrawingLayerBehaviour>();
        Texture2D readback = null;
        RenderTexture preview = null;
        Material display = null;
        try
        {
            Set(settings, "brushSize", 8f); Set(settings, "brushSpacing", 1f);
            object dynamics = Get(settings, "dynamics");
            Set(dynamics, "seed", int.MaxValue);
            Set(dynamics, "tipChannel", Enum.Parse(Get(dynamics, "tipChannel").GetType(), "Color"));
            Call(settings, "ApplyHlsl", code, new List<ShaderFXParameter>(), 64);
            T.True(Get(dynamics, "tip") == null && (bool)Get(dynamics, "DynamicTip"), "Dynamic Apply does not bake a frozen texture");
            object program = Get(dynamics, "hlslProgram"), shader = Get(program, "shader");
            object sequence = Get(dynamics, "Sequence");
            var a = new DrawingLayerBehaviour(); var b = new DrawingLayerBehaviour();
            layers.Add(a); layers.Add(b);
            object Parameters() => Call(settings, "GetStrokeParameters", false, (Color?)Color.white);
            Vector2 At(float x) => new Vector2(x/128f, .5f);
            void Stroke(DrawingLayerBehaviour layer, float start, float end, object parameters)
            {
                Call(layer, "BeginStroke", At(start));
                try
                {
                    Call(layer, "PaintPoint", At(start), 128, 64, parameters);
                    if (end != start) Call(layer, "PaintSegment", At(start), At(end), 128, 64, false, parameters);
                }
                finally { Call(layer, "EndStroke"); }
            }
            readback = new Texture2D(128, 64, TextureFormat.RGBAFloat, false, true);
            Color Pixel(DrawingLayerBehaviour layer, int x)
            {
                var previous = RenderTexture.active;
                try { RenderTexture.active = (RenderTexture)Get(layer, "paintSurface"); readback.ReadPixels(new Rect(0,0,128,64),0,0); return readback.GetPixel(x,32); }
                finally { RenderTexture.active = previous; }
            }
            Stroke(a, 16, 48, Parameters());
            Color p = Pixel(a, 48);
            T.Near(1, p.a, .002, "Seed above float exact-integer range arrives intact");
            T.Near(.25, p.r, .002, "GPU sees per-stamp distance");
            T.Near(.25, p.g, .002, "GPU sees totalDistance");
            T.Near(4d/128, p.b, .002, "Multiple contexts in one stamp batch");
            T.Near(32, (double)Get(sequence, "TotalDistance"), .001, "Sequence accumulates path length");
            Stroke(b, 80, 96, Parameters());
            p = Pixel(b, 96);
            T.Near(16d/128, p.r, .002, "Distance restarts on the next stroke");
            T.Near(48d/128, p.g, .002, "Sequence continues across layers without counting pointer travel");
            T.Near(18d/128, p.b, .002, "Stroke index advances; stamp index resets");
            T.True(ReferenceEquals(shader, Get(Get(dynamics, "hlslProgram"), "shader")), "Painting reuses compiled shader");
            var values = new List<ShaderFXParameter> { new ShaderFXParameter { name="_CheckIndex", type=ShaderFXParameterType.Float, floatValue=1 } };
            Call(settings, "ApplyHlsl", code, values, 2048);
            T.True(ReferenceEquals(shader, Get(Get(dynamics, "hlslProgram"), "shader")), "Dynamic parameter/resolution changes do not recompile or bake");
            T.Near(48, (double)Get(sequence, "TotalDistance"), .001, "Parameter edits preserve sequence");
            var exact = new DrawingLayerBehaviour(); layers.Add(exact);
            Call(exact, "BeginStroke", At(64));
            Set(exact, "brushStampIndex", 16777217u);
            try { Call(exact, "PaintPoint", At(64),128,64,Parameters()); }
            finally { Call(exact, "EndStroke"); }
            T.Near(1, Pixel(exact,64).a, .002, "Full stamp index survives vertex transport");
            values[0].floatValue = 0;
            Call(settings, "ApplyHlsl", code, values, 64);
            preview = RenderTexture.GetTemporary(128,64,0,RenderTextureFormat.ARGB32);
            display = new Material(Shader.Find("Hidden/WhimTex/DisplayChannels"));
            var sample = new DrawingLayerBehaviour(); layers.Add(sample);
            Call(sample, "RenderBrushPreview", preview, Parameters(), display, 1f);
            T.Near(48, (double)Get(sequence, "TotalDistance"), .001, "Preview does not advance real sequence");
            T.True(ReferenceEquals(shader, Get(Get(dynamics, "hlslProgram"), "shader")), "Preview does not dispose the active shader");
            string json = JsonUtility.ToJson(settings);
            T.True(json.Contains("BrushSequence") && !json.Contains("hlslProgram") && !json.Contains("nextStroke"), "Serialization keeps code/settings, not runtime program or progression");
            object restored = JsonUtility.FromJson(json, settingsType);
            try
            {
                T.True((bool)Call(restored,"TryRestoreBrushTip"), "Dynamic settings restore without a baked image");
                var restoredDynamics = Get(restored,"dynamics");
                T.True((bool)Get(restoredDynamics,"DynamicTip") && Get(restoredDynamics,"tip")==null, "Restored mode remains Dynamic");
            }
            finally { Call(restored,"ReleasePresetTip"); }
            Call(dynamics,"ResetSequence");
            Stroke(b, 112, 112, Parameters());
            p = Pixel(b,112);
            T.Near(0,p.g,.002,"Explicit reset clears totalDistance");
            T.Near(0,p.b,.002,"Explicit reset clears indices");
            Set(dynamics,"seed",1);
            Stroke(a, 112,112,Parameters());
            T.Near(0,(double)Get(sequence,"TotalDistance"),.001,"Changing Seed restarts the sequence");
            T.Near(0,Pixel(a,112).a,.002,"Shader sees the changed Seed");
            Set(dynamics,"seed",int.MaxValue);
            object pressure = Call(Parameters(),"WithPressure",.25f);
            var pressured = new DrawingLayerBehaviour(); layers.Add(pressured);
            Stroke(pressured,64,64,pressure);
            T.Near(.25,Pixel(pressured,64).a,.002,"Dynamic stamp preserves pressure");
            values[0].floatValue=2;
            Call(settings,"ApplyHlsl",code,values,64);
            var randomA = new DrawingLayerBehaviour(); var randomB = new DrawingLayerBehaviour();
            layers.Add(randomA);layers.Add(randomB);
            Call(dynamics,"ResetSequence"); Stroke(randomA,32,32,Parameters());
            Color random = Pixel(randomA,32);
            Call(dynamics,"ResetSequence"); Stroke(randomB,32,32,Parameters());
            Color repeated = Pixel(randomB,32);
            T.Near(random.r,repeated.r,.0001,"Stroke/stamp BrushRandom is deterministic");
            T.Near(random.g,repeated.g,.0001,"Indexed BrushRandom is deterministic");
            T.True(random.r>=0 && random.r<1 && random.g>=0 && random.g<1,"Random helpers return [0,1)");
            Stroke(randomB,96,96,Parameters());
            Color next = Pixel(randomB,96);
            T.True(Mathf.Abs(next.r-random.r)>.001,"Per-stroke random varies between strokes");
            T.Near(random.g,next.g,.0001,"Seed/index random ignores stroke index");
            bool rejectedDynamic = false;
            try { Call(settings,"ApplyHlsl",code+"\ninvalid syntax",values,64); }
            catch (TargetInvocationException) { rejectedDynamic = true; }
            T.True(rejectedDynamic, "Invalid Dynamic source rejected on Apply");
            T.True(ReferenceEquals(shader,Get(Get(dynamics,"hlslProgram"),"shader")),"Failed Dynamic Apply keeps working program");
            Call(dynamics,"ResetSequence");
            var pattern = new DrawingLayerBehaviour(); layers.Add(pattern);
            Set(pattern,"repeatMode",PaintRepeatMode.Mirror);
            Set(pattern,"mirrorAcrossVerticalAxis",true);
            Stroke(pattern,16,48,Parameters());
            var stamps = (System.Collections.IList)Get(pattern,"segmentStamps");
            var indices = new Dictionary<uint,int>();
            foreach (object stamp in stamps)
            {
                object ctx = Get(stamp,"dynamicContext");
                uint index = (uint)Get(ctx,"stampIndex");
                indices[index] = indices.TryGetValue(index,out int n) ? n+1 : 1;
                T.Near(index*8,(float)Get(ctx,"distance"),.001,"Symmetry copies retain logical path context");
            }
            T.Equal(4,indices.Count,"Segment has four logical stamps after initial point");
            foreach (int copies in indices.Values) T.Equal(2,copies,"Mirror copies share one logical index");
            Call(pattern,"BeginStroke",At(16)); stamps.Clear();
            try { Call(pattern,"BuildBrushSegment",At(16),At(112),128,64,true,Parameters(),96f,2); }
            finally { Call(pattern,"EndStroke"); }
            T.Equal(13u,(uint)Get(pattern,"brushStampIndex"),"Budget skipping advances all thirteen logical indices");
            T.Equal(4,stamps.Count,"Budget creates two sampled stamps with mirror copies, not thirteen GPU stamps");
            T.Near(128,(double)Get(sequence,"TotalDistance"),.001,"Budget skipping preserves full path length");
            var library = assembly.GetType("DCFApixels.WhimTex.BrushPresetLibrary",true);
            object preset = library.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{settings});
            object selected = Activator.CreateInstance(settingsType,true);
            try
            {
                Call(selected,"ApplyPreset",preset,null,"");
                object selectedDynamics = Get(selected,"dynamics");
                T.True((bool)Get(selectedDynamics,"DynamicTip") && Get(selectedDynamics,"tip")==null,"Selecting a saved Dynamic preset prepares code, not a frozen tip");
                T.Near(0,(double)Get(Get(selectedDynamics,"Sequence"),"TotalDistance"),.001,"Preset selection starts a new sequence");
            }
            finally { Call(selected,"ReleasePresetTip"); }
        }
        finally
        {
            foreach (var layer in layers) Call(layer,"ReleaseTransientResources");
            Call(settings,"ReleasePresetTip");
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (display != null) UnityEngine.Object.DestroyImmediate(display);
            if (preview != null) RenderTexture.ReleaseTemporary(preview);
        }
    }
    private static void FirstStampChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = typeof(WhimTexApi).Assembly;
        var settingsType = assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
        object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, flags).Invoke(value, args);
        object Get(object value, string name) => value.GetType().GetField(name, flags)?.GetValue(value)
            ?? value.GetType().GetProperty(name, flags)?.GetValue(value);
        void Set(object value, string name, object data)
        {
            var field = value.GetType().GetField(name,flags);
            if (field != null) field.SetValue(value,data);
            else value.GetType().GetProperty(name,flags).SetValue(value,data);
        }
        var settings = Activator.CreateInstance(settingsType, true);
        var layers = new List<DrawingLayerBehaviour>();
        const int width = 64, height = 48;
        Vector2 origin = new Vector2(.5f,.5f);
        Vector2 At(Vector2 delta) => origin + delta / new Vector2(width,height);
        object Parameters(float pressure = .6f) => Call(Call(settings,"GetStrokeParameters",false,(Color?)Color.white),"WithPressure",pressure);
        var readback = Scope.OwnObject(new Texture2D(width,height,TextureFormat.RGBAFloat,false,true));
        var colors = new Color[width*height];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color(.1f + .3f*(i%width)/width,.25f,.4f, i%5==0 ? .2f : .8f);
        DrawingLayerBehaviour Layer()
        {
            var layer = new DrawingLayerBehaviour(); layers.Add(layer);
            var pixels = Scope.OwnObject(new Texture2D(width,height,TextureFormat.RGBAHalf,false,true));
            pixels.SetPixels(colors); pixels.Apply(); Set(layer,"pixels",pixels);
            return layer;
        }
        Color[] Image(DrawingLayerBehaviour layer)
        {
            var previous = RenderTexture.active;
            try { RenderTexture.active = (RenderTexture)Get(layer,"paintSurface"); readback.ReadPixels(new Rect(0,0,width,height),0,0); return readback.GetPixels(); }
            finally { RenderTexture.active = previous; }
        }
        void Same(Color[] expected, Color[] actual, string message)
        {
            float error = 0;
            for (int i=0;i<expected.Length;i++)
                for (int c=0;c<4;c++) error = Mathf.Max(error,Mathf.Abs(expected[i][c]-actual[i][c]));
            T.Near(0,error,.002,message);
        }
        string header = "// @whimtex-brush Tests/First Stamp\n";
        try
        {
            Set(settings,"brushSize",16f); Set(settings,"brushSpacing",.5f);
            object dynamics = Get(settings,"dynamics");
            Set(dynamics,"tipChannel",Enum.Parse(Get(dynamics,"tipChannel").GetType(),"Color"));
            foreach (string args in new[] { "float2 uv, BrushSequence s, BrushMotion m", "float2 uv, BrushMotion m, BrushSequence s",
                "BrushSequence s, float2 uv, BrushMotion m", "BrushSequence s, BrushMotion m, float2 uv",
                "BrushMotion m, float2 uv, BrushSequence s", "in BrushMotion m, in BrushSequence s, in float2 uv" })
            {
                Call(settings,"ApplyHlsl",header+"float4 BrushTip("+args+") { return float4(.5+m.deltaPixels/32,s.seed==1u?1:0,uv.x>.5?1:0); }",new List<ShaderFXParameter>(),64);
                T.True((bool)Get(Get(dynamics,"hlslProgram"),"UsesMotion"),"Signature order declares motion: "+args);
                var layer = Layer(); Call(layer,"BeginStroke",origin); Call(layer,"SetKnownBrushDirection",Vector2.up);
                Call(layer,"PaintPoint",origin,width,height,Parameters());
                T.True(Get(layer,"advancedStrokeBase")!=null,"Motion allocates the stroke pair");
                T.True(!(bool)Get(layer,"firstBrushPending"),"Known direction needs no provisional correction");
                Call(layer,"EndStroke");
                T.True(Get(layer,"advancedStroke")==null && Get(layer,"advancedStrokeBase")==null,"End releases the pair");
            }
            foreach (string args in new[] { "BrushSequence s, float2 uv", "float2 uv, BrushSequence s", "BrushMotion m, float2 uv", "float2 uv, BrushMotion m", "float2 uv" })
            {
                Call(settings,"ApplyHlsl",header+"float4 BrushTip("+args+") { return 1; }",new List<ShaderFXParameter>(),64);
                var layer = Layer(); Call(layer,"BeginStroke",origin); Call(layer,"PaintPoint",origin,width,height,Parameters());
                T.Equal(args.Contains("BrushMotion"),Get(layer,"advancedStrokeBase")!=null,"Only the motion capability requires a pair: "+args);
                Call(layer,"EndStroke");
            }
            string code = header+"float4 BrushTip(BrushMotion m, float2 uv, BrushSequence s) { " +
                "return float4(.2+.6*BrushRandom(s.seed,s.strokeIndex,s.stampIndex),.5+m.deltaPixels.x/64,.5+m.deltaPixels.y/64," +
                "uv.x>.2 && uv.x<.8 && uv.y>.42 && uv.y<.58 ? .7 : 0); }";
            Call(settings,"ApplyHlsl",code,new List<ShaderFXParameter>(),64);
            Set(dynamics,"angleOffset",35f); Set(dynamics,"angleJitter",18f); Set(dynamics,"sizeJitter",.2f);
            Set(dynamics,"scatter",.3f); Set(dynamics,"flipX",.5f); Set(dynamics,"flipY",.5f);
            foreach (string rotation in new[] { "Fixed", "StrokeDirection" })
            foreach (string blend in new[] { "Normal", "Multiply" })
            foreach (string application in new[] { "Stroke", "Stamp" })
            {
                Set(dynamics,"rotationMode",Enum.Parse(Get(dynamics,"rotationMode").GetType(),rotation));
                Set(dynamics,"blend",Enum.Parse(Get(dynamics,"blend").GetType(),blend));
                Set(dynamics,"blendApplication",Enum.Parse(Get(dynamics,"blendApplication").GetType(),application));
                Set(dynamics,"opacity",.5f); Set(dynamics,"flow",.7f); Set(dynamics,"writeChannels",5);
                var actual = Layer(); var expected = Layer();
                foreach (var layer in new[] {actual,expected}) { Set(layer,"repeatMode",PaintRepeatMode.Mirror); Set(layer,"mirrorAcrossVerticalAxis",true); }
                Call(dynamics,"ResetSequence"); Call(actual,"BeginStroke",origin); Call(actual,"SetInitialBrushDelta",Vector2.right);
                Call(actual,"PaintPoint",origin,width,height,Parameters());
                T.True((bool)Get(actual,"firstBrushPending"),"First dab is immediate and pending");
                T.True(Get(actual,"paintSurfaceDirty") is bool changed && changed,"Immediate stamp has a real layer source, not an overlay");
                uint rng=(uint)Get(actual,"brushRandomState"), index=(uint)Get(actual,"brushStampIndex");
                Call(actual,"PaintSegment",origin,At(new Vector2(0,.5f)),width,height,false,Parameters(0));
                T.True(!(bool)Get(actual,"firstBrushPending"),"Zero-pressure motion corrects with captured first-dab pressure");
                Call(actual,"PaintSegment",origin,At(new Vector2(0,.5f)),width,height,false,Parameters(.1f));
                T.True(!(bool)Get(actual,"firstBrushPending"),"Sub-spacing movement corrects once before the next stamp");
                T.Equal(rng,(uint)Get(actual,"brushRandomState"),"Correction consumes no random draws");
                T.Equal(index,(uint)Get(actual,"brushStampIndex"),"Correction consumes no logical stamps or mirror indices");
                Color[] corrected = Image(actual);
                Call(dynamics,"ResetSequence"); Call(expected,"BeginStroke",origin);
                float angle = (90f * (90f / (90f + 45f/19f)))*Mathf.Deg2Rad;
                Call(expected,"SetKnownBrushDirection",new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)));
                Call(expected,"PaintPoint",origin,width,height,Parameters());
                Same(Image(expected),corrected,"Replay equals one known-direction dab, including protection/blend/randomness: "+rotation+blend+application);
                Call(actual,"PaintSegment",At(new Vector2(0,.5f)),At(new Vector2(0,1)),width,height,false,Parameters());
                Same(corrected,Image(actual),"Further sub-spacing movement does not correct twice");
                Call(actual,"EndStroke"); Call(expected,"EndStroke");
            }
            Set(dynamics,"writeChannels",15); Set(dynamics,"scatter",0f);
            Set(dynamics,"opacity",1f); Set(dynamics,"blend",Enum.Parse(Get(dynamics,"blend").GetType(),"Normal"));
            Call(settings,"ApplyHlsl",header+"float4 BrushTip(BrushMotion m, float2 uv) { return float4(1,1,1,m.strokeDirection); }",new List<ShaderFXParameter>(),64);
            Set(dynamics,"angleOffset",0f); Set(dynamics,"angleJitter",0f); Set(dynamics,"sizeJitter",0f);
            Set(dynamics,"flipX",0f); Set(dynamics,"flipY",0f); Set(dynamics,"flow",1f);
            var fixedFlag=Layer(); var directionFlag=Layer();
            Set(dynamics,"rotationMode",Enum.Parse(Get(dynamics,"rotationMode").GetType(),"Fixed"));
            Call(fixedFlag,"BeginStroke",origin); Call(fixedFlag,"EnsurePaintSurface",width,height);
            Color[] flagBase=Image(fixedFlag); Call(fixedFlag,"PaintPoint",origin,width,height,Parameters());
            Same(flagBase,Image(fixedFlag),"Motion-only GPU strokeDirection is zero for Fixed"); Call(fixedFlag,"EndStroke");
            Set(dynamics,"rotationMode",Enum.Parse(Get(dynamics,"rotationMode").GetType(),"StrokeDirection"));
            Call(directionFlag,"BeginStroke",origin); Call(directionFlag,"PaintPoint",origin,width,height,Parameters());
            T.True(Image(directionFlag)[24*width+32].r > flagBase[24*width+32].r+.1f,"Motion-only GPU strokeDirection is one for Stroke Direction");
            Call(directionFlag,"EndStroke");
            var refine = assembly.GetType("DCFApixels.WhimTex.BrushDynamics",true).GetMethod("RefineStartAngle",BindingFlags.Static|BindingFlags.NonPublic);
            float previousWeight = 0f;
            foreach (float degrees in new[] { 1f, 5f, 15f, 22.5f, 30f, 44f, 45f, 90f, 179f })
            {
                float angle = (float)refine.Invoke(null,new object[] { 0f, degrees*Mathf.Deg2Rad })*Mathf.Rad2Deg;
                float weight = angle/degrees;
                T.True(weight >= previousWeight-.00001f && weight < 1f,"Start correction weight grows but never reaches one: "+degrees);
                T.Near(degrees/(degrees+45d/19d),weight,.00001,"Start correction follows the positive SoftSign curve");
                T.True(degrees-angle < 45f/19f,"Angular residual stays bounded: "+degrees);
                if (degrees == 45) T.Near(.95,weight,.00001,"At 45 degrees the refined weight is 0.95");
                previousWeight = weight;
            }
            T.Near(179+2*(2/(2+45d/19d)),(float)refine.Invoke(null,new object[]{179f*Mathf.Deg2Rad,-179f*Mathf.Deg2Rad})*Mathf.Rad2Deg,.0001,"Curve crosses the angle seam by the shortest arc");
            Call(settings,"ApplyHlsl",code,new List<ShaderFXParameter>(),64);
            var selection = Scope.OwnObject(new Texture2D(width,height,TextureFormat.RGBA32,false,true));
            var mask = new Color[width*height];
            for (int i=0;i<mask.Length;i++) mask[i] = i%width < width/2 ? Color.white : Color.clear;
            selection.SetPixels(mask); selection.Apply();
            foreach (bool erase in new[]{false,true})
            foreach (bool hdr in new[]{false,true})
            {
                var actual=Layer(); var expected=Layer();
                foreach(var layer in new[]{actual,expected})
                {
                    Set(layer,"colorRange",hdr ? LayerColorRange.HDR : LayerColorRange.Standard);
                    Set(layer,"blendRange",hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard);
                }
                object selected = Call(Call(settings,"GetStrokeParameters",erase,(Color?)new Color(2f,.5f,-.3f,.8f)),"WithSelectionMask",selection);
                selected=Call(selected,"WithPressure",.6f);
                Call(dynamics,"ResetSequence"); Call(actual,"BeginStroke",origin);
                Call(actual,"PaintPoint",origin,width,height,selected);
                Call(actual,"PaintSegment",origin,At(new Vector2(.5f,0)),width,height,false,selected);
                Call(dynamics,"ResetSequence"); Call(expected,"BeginStroke",origin); Call(expected,"SetKnownBrushDirection",Vector2.right);
                Call(expected,"PaintPoint",origin,width,height,selected);
                Same(Image(expected),Image(actual),"Correction preserves selection and color precision: erase="+erase+", HDR="+hdr);
                Call(actual,"EndStroke"); Call(expected,"EndStroke");
            }
            // Adding an unused Motion argument must not change ordinary painting mathematics.
            var direct=Layer(); var buffered=Layer();
            Set(dynamics,"rotationMode",Enum.Parse(Get(dynamics,"rotationMode").GetType(),"Fixed"));
            Call(settings,"ApplyHlsl",header+"float4 BrushTip(float2 uv, BrushSequence s) { return 1; }",new List<ShaderFXParameter>(),64);
            Call(direct,"BeginStroke",origin); Call(direct,"PaintPoint",origin,width,height,Parameters());
            T.True(Get(direct,"advancedStrokeBase")==null,"Sequence-only comparison uses the direct path");
            Call(settings,"ApplyHlsl",header+"float4 BrushTip(float2 uv, BrushMotion m) { return 1; }",new List<ShaderFXParameter>(),64);
            Call(buffered,"BeginStroke",origin); Call(buffered,"SetKnownBrushDirection",Vector2.right);
            Call(buffered,"PaintPoint",origin,width,height,Parameters());
            Same(Image(direct),Image(buffered),"Direct and buffered paths preserve the same stamp on a translucent base");
            Call(direct,"EndStroke"); Call(buffered,"EndStroke");
            Set(dynamics,"rotationMode",Enum.Parse(Get(dynamics,"rotationMode").GetType(),"StrokeDirection"));
            Call(settings,"ApplyHlsl",code,new List<ShaderFXParameter>(),64);
            var noHover=Layer(); Call(noHover,"BeginStroke",origin); Call(noHover,"PaintPoint",origin,width,height,Parameters());
            Call(noHover,"PaintSegment",origin,At(new Vector2(-.5f,0)),width,height,false,Parameters());
            T.True(!(bool)Get(noHover,"firstBrushPending"),"No hover uses the actual movement without a delayed image");
            var click=Layer(); Call(click,"BeginStroke",origin); Call(click,"PaintPoint",origin,width,height,Parameters());
            Color[] before=Image(click); Call(click,"EndStroke"); Same(before,Image(click),"A click commits exactly the provisional stamp");
            var saved=Layer(); Call(saved,"BeginStroke",origin); Call(saved,"PaintPoint",origin,width,height,Parameters());
            Call(saved,"SyncSurfaceToTexture"); T.True(!(bool)Get(saved,"firstBrushPending"),"Storage sync commits pending state before export/save");
            Call(saved,"EndStroke");
            var cancelled=Layer(); Call(cancelled,"BeginStroke",origin); Call(cancelled,"EnsurePaintSurface",width,height);
            Color[] source=Image(cancelled); Call(cancelled,"PaintPoint",origin,width,height,Parameters()); Call(cancelled,"CancelStroke");
            Same(source,Image(cancelled),"Cancellation restores the exact GPU base");
            T.True(Get(cancelled,"advancedStrokeBase")==null,"Cancellation releases its pair");
            var live=Layer(); var reference=Layer();
            WhimTexDocument Document(DrawingLayerBehaviour drawing)
            {
                var document=Scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
                document.width=width; document.height=height;
                drawing.Owner.opacity=.6f; drawing.Owner.blendMode=BlendMode.Multiply;
                var group=new Layer(new GroupLayerBehaviour()){opacity=.7f,compositing=GroupCompositing.Isolated};
                group.children.Add(new Layer(new ColorFillLayerBehaviour{color=new Color(.8f,.4f,.3f,.4f)}){clippingMask=true});
                group.children.Add(drawing.Owner);
                var target=new BlurLayerBehaviour{inputMode=EffectInputMode.Specific,radius=1f};
                document.layers.Add(new Layer(target)); document.layers.Add(group);
                document.layers.Add(new Layer(new ColorFillLayerBehaviour{color=new Color(.3f,.7f,.9f,1)}));
                Call(document,"NormalizeModel"); target.TargetLayerId=group.Id;
                return document;
            }
            var liveDocument=Document(live); var referenceDocument=Document(reference);
            Call(dynamics,"ResetSequence"); Call(live,"BeginStroke",origin); Call(live,"SetInitialBrushDelta",Vector2.right);
            Call(live,"PaintPoint",origin,width,height,Parameters());
            Texture2D Thumbnail(WhimTexDocument document) => (Texture2D)Call(document,"GetLayerThumbnail",document.layers[0],width,false);
            T.True(Thumbnail(liveDocument)!=null,"Target thumbnail renders the immediate provisional source");
            T.True((bool)Get(live,"firstBrushPending"),"Thumbnail rendering keeps the provisional source correctable");
            Call(dynamics,"ResetSequence"); Call(reference,"BeginStroke",origin);
            float correctedAngle=(90f*(90f/(90f+45f/19f)))*Mathf.Deg2Rad;
            Call(reference,"SetKnownBrushDirection",new Vector2(Mathf.Cos(correctedAngle),Mathf.Sin(correctedAngle)));
            Call(reference,"PaintPoint",origin,width,height,Parameters());
            Call(live,"PaintSegment",origin,At(new Vector2(0,.5f)),width,height,false,Parameters());
            Same(Image(reference),Image(live),"Rendering consumers do not change the underlying corrected paint surface");
            foreach(string method in new[]{"RenderCanvas","RenderLayerPreview"})
            {
                object[] Args(WhimTexDocument document) => method=="RenderCanvas" ? new object[]{width} : new object[]{document.layers[1],width};
                RenderTexture liveRender=null,referenceRender=null;
                var previous=RenderTexture.active;
                try
                {
                    referenceRender=(RenderTexture)Call(referenceDocument,method,Args(referenceDocument));
                    liveRender=(RenderTexture)Call(liveDocument,method,Args(liveDocument));
                    RenderTexture.active=referenceRender; readback.ReadPixels(new Rect(0,0,width,height),0,0); var expectedPixels=readback.GetPixels();
                    RenderTexture.active=liveRender; readback.ReadPixels(new Rect(0,0,width,height),0,0);
                    Same(expectedPixels,readback.GetPixels(),method+" preserves correction with layer/group opacity, blending and clipping");
                }
                finally
                {
                    RenderTexture.active=previous;
                    if(liveRender!=null) RenderTexture.ReleaseTemporary(liveRender);
                    if(referenceRender!=null) RenderTexture.ReleaseTemporary(referenceRender);
                }
            }
            Call(live,"EndStroke"); Call(reference,"EndStroke"); Call(live,"SyncSurfaceToTexture"); Call(reference,"SyncSurfaceToTexture");
            // Thumbnails use the ordinary post-stroke storage refresh, not the window's live-paint cache.
            Call(liveDocument,"RefreshThumbnailStructure"); Call(referenceDocument,"RefreshThumbnailStructure");
            Same(Thumbnail(referenceDocument).GetPixels(),Thumbnail(liveDocument).GetPixels(),"Target thumbnail uses the corrected grouped/clipped source after commit");
            var exported=Scope.OwnObject(liveDocument.ComposeCanvas()); var referenceExport=Scope.OwnObject(referenceDocument.ComposeCanvas());
            Same(referenceExport.GetPixels(),exported.GetPixels(),"Full-size export composition retains corrected storage after commit");
        }
        finally
        {
            foreach(var layer in layers) Call(layer,"ReleaseTransientResources");
            Call(settings,"ReleasePresetTip");
        }
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
