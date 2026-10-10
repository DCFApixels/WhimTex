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
        try
        {
            Set(settings, "brushSize", 4f); Set(settings, "brushSpacing", 1f);
            object dynamics = Get(settings, "dynamics");
            Set(dynamics, "tipChannel", Enum.Parse(Get(dynamics, "tipChannel").GetType(), "Color"));
            string code = "// @whimtex-brush Tests/Delta\nfloat4 BrushTip(float2 uv, DynamicBrushContext brush) { return float4(0.5+brush.deltaPixels/32.0,brush.stampIndex/32.0,1); }";
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
            try
            {
                Call(a,"BuildBrushSegment",At(16,56),At(112,56),width,height,true,Parameters(),96f,2);
                T.Equal(25u,(uint)Get(a,"brushStampIndex"),"Budget advances all logical stamps");
                T.Equal(2,Stamps(a).Count,"Budget emits only two stamps");
                ContextDelta(Stamps(a)[0],0,0,"Budget first stamp");
                ContextDelta(Stamps(a)[1],4,0,"Budget delta uses previous logical stamp, not previous emitted stamp");
                Stamps(a).Clear();
                Call(a,"BuildBrushSegment",At(112,56),At(116,56),width,height,false,Parameters(),4f,2);
                T.Equal(1,Stamps(a).Count,"Next input event emits one stamp");
                ContextDelta(Stamps(a)[0],4,0,"Next event retains the last logical, budget-skipped position");
            }
            finally { Call(a,"EndStroke"); }
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
            string dynamicCode = declarations + "float4 BrushTip(float2 uv, DynamicBrushContext brush) " + body;
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
        string body = "float4 BrushTip(float2 uv, DynamicBrushContext brush) { return float4(_Color*brush.distance/128,brush.totalDistance/128,(brush.strokeIndex*16u+brush.stampIndex)/128.0,brush.seed==2147483647u?1:0); }";
        string code = "// @whimtex-brush Tests/Context\n// @param float _CheckIndex = 0 [0 .. 2]\n// @param float _Color = 1 [0 .. 1]\n" + body.Replace("return float4", "if (_CheckIndex>1.5) return float4(BrushRandom(brush.seed,brush.strokeIndex,brush.stampIndex),BrushRandom(brush.seed,brush.stampIndex),0,1); if (_CheckIndex>0.5) return float4(1,1,1,brush.stampIndex==16777217u?1:0); return float4");
        T.True(Mode(code), "Actual Dynamic entry point determines mode");
        T.True(!Mode("float4 BrushTip(float2 uv) { return 1; } // DynamicBrushContext\nvoid Helper(DynamicBrushContext brush) {}"), "Helper context and comments do not change Static mode");
        T.True(Mode("float4 BrushTip(float2 uv);\n" + code), "Prototypes do not select a second entry point");
        T.True(!Mode("/*" + body + "*/\nfloat4 BrushTip(float2 uv) { return 1; }"), "Commented Dynamic entry point ignored");
        foreach (string invalid in new[] { code + "\nfloat4 BrushTip(float2 uv) { return 1; }", "float4 BrushTip(float2 uv);", "float4 BrushTip(float2 uv, float x) { return 1; }" })
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
            T.True(json.Contains("DynamicBrushContext") && !json.Contains("hlslProgram") && !json.Contains("nextStroke"), "Serialization keeps code/settings, not runtime program or progression");
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
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
