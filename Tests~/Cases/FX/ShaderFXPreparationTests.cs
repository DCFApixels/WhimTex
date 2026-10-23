using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEngine;
using Unity.Profiling;
using UnityEditor;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Bounded CPU-stage timings, not GPU execution times or presented frame rate.
public static class ShaderFXPreparationTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly string Root = Path.GetFullPath("Temp/WhimTex/fx-preparation");
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    [Serializable] sealed class Timing { public string name; public double medianUs, p95Us, allocations; }
    [Serializable] sealed class Report { public string phase, unity; public string[] stages; public double[] medianUs, p95Us, allocations; public string[] pixels; }
    public static string Before() => Run("before-v2");
    public static string After() => Run("after-final");
    static string Run(string phase)
    {
        Report report = null;
        string result = TestContext.Run("FX preparation equivalence and CPU timings: " + phase, t =>
        {
            var owned = new List<Object>(); var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            // Do not disturb an existing capture, native recorder or benchmark.
            var profiler = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
            var suppression = (IDisposable)profiler.GetMethod("SuppressInstrumentation", F).Invoke(null, null);
            Exception primaryFailure = null;
            try
            {
                var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(doc);
                doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 32;
                doc.outputPrecision = WhimTexOutputPrecision.Float32;
                Layer consumer = new ColorFillLayerBehaviour { color = new Color(.2f, .4f, .7f, 1) };
                Layer source = new ColorFillLayerBehaviour { color = new Color(.8f, .3f, .1f, 1), enabled = false };
                doc.layers.Add(consumer); doc.layers.Add(source); Call(doc, "NormalizeModel");
                var contextType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
                var self = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                owned.Add(self); self.Create(); RenderTexture.active = self; GL.Clear(false, true, new Color(.2f, .4f, .7f, 1));
                object context = Activator.CreateInstance(contextType, F, null, new object[] { doc, self, 32, 32, 1f, true, true, null }, null);
                var timings = new List<Timing>(); var images = new List<string>();
                foreach (string workload in new[] { "scalar8", "scalar64", "mixed32", "textures12" })
                {
                    var code = new StringBuilder(); int count = workload == "scalar8" ? 8 : workload == "scalar64" ? 64 : workload == "mixed32" ? 32 : 12;
                    for (int i = 0; i < count; i++)
                    {
                        string type = workload == "textures12" ? "texture2D" : workload == "mixed32" ? new[] { "float", "gradient", "curve", "transform2D" }[i % 4] : "float";
                        code.Append("// @param ").Append(type).Append(" _P").Append(i).Append(type == "texture2D" ? " = self\n" : "\n");
                    }
                    code.Append(workload == "textures12" ? "float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return tex2D(_P0,uv)*.5+c*.5;}" :
                        workload == "mixed32" ? "float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return float4(_P1_Sample(_P2_Sample(_P3_ToLocal(uv).x)).rgb*c.rgb*_P0,c.a);}" :
                        "float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return float4(c.rgb*_P0,c.a);}");
                    var fx = ScriptableObject.CreateInstance<ShaderFX>(); owned.Add(fx); fx.hideFlags = HideFlags.HideAndDontSave;
                    Set(fx, "code", code.ToString()); Call(fx, "ApplyAgentDraft"); consumer.fx.Clear(); consumer.fx.Add(fx);
                    var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
                    var get = typeof(ShaderFX).GetMethod("GetMaterial", F); object[] materialArgs = { context };
                    var stamp = (Func<ulong>)typeof(ShaderFX).GetMethod("RenderCacheStamp", F).CreateDelegate(typeof(Func<ulong>), fx);
                    var enumerate = typeof(ShaderFX).GetMethod("TextureParameters", F);
                    var bind = typeof(WhimTexDocument).GetMethod("BindShaderTextureLayers", F); object[] bindArgs = { fx, consumer, context, self };
                    Action prepare = () => get.Invoke(fx, materialArgs);
                    Action fingerprint = () => { stamp(); };
                    Action textures = () => { var binding = (IDisposable)bind.Invoke(doc, bindArgs); binding?.Dispose(); };
                    timings.Add(Measure(workload + "/parameters", prepare, 256));
                    timings.Add(Measure(workload + "/fingerprint", fingerprint, 256));
                    timings.Add(Measure(workload + "/texture-inputs", textures, 128));
                    Material material = (Material)get.Invoke(fx, materialArgs);
                    ulong before = stamp(); values[0].floatValue = .25f;
                    if (workload != "textures12") t.True(before != stamp(), "Direct scalar edit changes stamp");
                    t.Equal(stamp(), stamp(), "Unchanged stamp settles");
                    // Also exercise fallback to applied values while an incompatible draft is being edited.
                    values.Reverse(); prepare(); values.Reverse(); prepare();
                    t.True(material != null, "Prepared material exists");
                    if (workload == "textures12")
                    {
                        foreach (var mode in new[] { ShaderFXTextureSource.Self, ShaderFXTextureSource.None, ShaderFXTextureSource.Layer, ShaderFXTextureSource.Texture })
                        {
                            foreach (var v in values) { v.textureSource = mode; v.textureLayerId = source.Id; }
                            prepare();
                            var binding = (IDisposable)bind.Invoke(doc, bindArgs);
                            try { if (binding != null) Call(binding, "Apply", material); images.Add(Pixels(material, self)); }
                            finally { binding?.Dispose(); }
                        }
                    }
                    else images.Add(Pixels(material, self));
                    t.Equal(workload == "textures12" ? 12 : 0, Count((IEnumerable)enumerate.Invoke(fx, null)), "Only applied texture parameters are enumerated");
                }
                report = new Report { phase = phase, unity = Application.unityVersion, stages = timings.ConvertAll(v => v.name).ToArray(),
                    medianUs = timings.ConvertAll(v => v.medianUs).ToArray(), p95Us = timings.ConvertAll(v => v.p95Us).ToArray(),
                    allocations = timings.ConvertAll(v => v.allocations).ToArray(), pixels = images.ToArray() };
                Directory.CreateDirectory(Root);
                if (phase.StartsWith("after", StringComparison.Ordinal))
                {
                    var baseline = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Root, "before-v2.json")));
                    t.Equal(baseline.pixels.Length, report.pixels.Length, "Baseline covers the same renders");
                    for (int i = 0; i < images.Count; i++) t.Equal(baseline.pixels[i], images[i], "Bit-exact float output " + i);
                }
                string path = Path.Combine(Root, phase + ".json");
                if (File.Exists(path)) throw new IOException("Refusing to replace retained measurement: " + path);
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            catch (Exception error)
            {
                primaryFailure = error;
                throw;
            }
            finally
            {
                var failures = new List<Exception>();
                void Cleanup(Action action)
                {
                    try { action(); }
                    catch (Exception error) { failures.Add(error); }
                }
                Cleanup(() => RenderTexture.active = previous);
                Cleanup(() => GL.sRGBWrite = srgb);
                for (int i = owned.Count - 1; i >= 0; i--)
                {
                    var value = owned[i];
                    if (value != null) Cleanup(() => Object.DestroyImmediate(value));
                }
                Cleanup(suppression.Dispose);
                if (failures.Count != 0)
                {
                    if (primaryFailure != null) failures.Insert(0, primaryFailure);
                    throw new AggregateException("Owned FX preparation cleanup unfinished", failures);
                }
            }
        });
        var parsed = JsonUtility.FromJson<TestResult>(result);
        if (parsed.status == "passed") parsed.message += "; " + Path.Combine(Root, phase + ".json");
        return parsed.ToJson();
    }
    static int Count(IEnumerable values) { int count = 0; foreach (var v in values) count++; return count; }

    public static string Regression() => TestContext.Run("Mutable FX bindings, fingerprints, input ownership and Undo/Redo", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        var fx = ScriptableObject.CreateInstance<ShaderFX>();
        var oldActive = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var input = new RenderTexture(13, 7, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        try
        {
            doc.hideFlags = fx.hideFlags = HideFlags.HideAndDontSave; doc.width = 26; doc.height = 14;
            input.Create();
            object context = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true),
                F, null, new object[] { doc, input, 13, 7, 2f, true, true, null }, null);
            const string code = "// @param float _A = 1\n// @param float _B = 2\n// @param texture2D _Map = self\n// @param texture2D _Other = self\nfloat4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return float4(_A,_B,_Map_TexelSize.z/13,1);}";
            Set(fx, "code", code); Call(fx, "ApplyAgentDraft");
            var valuesField = typeof(ShaderFX).GetField("parameters", F);
            List<ShaderFXParameter> Values() => (List<ShaderFXParameter>)valuesField.GetValue(fx);
            Material Prepare() => (Material)Call(fx, "GetMaterial", context);
            ulong Stamp() => (ulong)Call(fx, "RenderCacheStamp");
            var material = Prepare(); var values = Values();
            t.Near(1, material.GetFloat("_A"), 0, "Initial scalar");
            values[0].floatValue = .375f; Prepare();
            t.Near(.375, material.GetFloat("_A"), 0, "Direct edits do not require notification");
            values.Reverse(); Prepare(); t.Near(.375, material.GetFloat("_A"), 0, "Reorder rebuilds mapping"); values.Reverse();
            values[0] = new ShaderFXParameter { name = "_A", type = ShaderFXParameterType.Float, floatValue = .625f };
            Prepare(); t.Near(.625, material.GetFloat("_A"), 0, "Equal-length replacement rebuilds mapping");
            values.Insert(0, new ShaderFXParameter { name = "_A", type = ShaderFXParameterType.Float, floatValue = .125f });
            Prepare(); t.Near(.125, material.GetFloat("_A"), 0, "First duplicate wins"); values.RemoveAt(0);
            values[0].name = "_Draft"; Prepare(); t.Near(1, material.GetFloat("_A"), 0, "Rename falls back to applied declaration");
            values[0].name = "_A"; values[0].type = ShaderFXParameterType.Color; Prepare();
            t.Near(1, material.GetFloat("_A"), 0, "Type mismatch falls back to applied declaration"); values[0].type = ShaderFXParameterType.Float;
            Prepare(); t.Near(.625, material.GetFloat("_A"), 0, "Returning draft type restores matching");

            ulong oldStamp = Stamp(); string oldName = values[0].name;
            values[0].name = new string(oldName.ToCharArray()); t.Equal(oldStamp, Stamp(), "Equal immutable strings preserve fingerprint");
            string oldId = values[0].id; values[0].id = "changed"; t.True(oldStamp != Stamp(), "ID edits invalidate"); values[0].id = oldId;
            t.Equal(oldStamp, Stamp(), "Restoring ID restores content stamp");
            var extra = new ShaderFXParameter { name = "_Extra", type = ShaderFXParameterType.Gradient, gradientValue = new WhimTexGradient() }; values.Add(extra);
            void Changed(Action action, string label) { ulong previous = Stamp(); action(); t.True(previous != Stamp(), label); t.Equal(Stamp(), Stamp(), label + " settles"); }
            Changed(() => extra.gradientValue.SetMidpoint(false, 0, .3f), "Gradient midpoint");
            Changed(() => extra.gradientValue.WrapMode = WhimTexGradientWrapMode.Mirror, "Gradient wrap");
            Changed(() => extra.gradientValue.Smoothness = .25f, "Gradient smoothness");
            Changed(() => extra.gradientValue.ColorSpace = ColorSpace.Linear, "Gradient space");
            extra.type = ShaderFXParameterType.Curve; extra.curveValue = AnimationCurve.Linear(0, 0, 1, 1);
            Changed(() => { var k = extra.curveValue[1]; k.inTangent = .3f; extra.curveValue.MoveKey(1, k); }, "Curve tangent");
            Changed(() => { var k = extra.curveValue[1]; k.weightedMode = WeightedMode.In; k.inWeight = .2f; extra.curveValue.MoveKey(1, k); }, "Weighted key");
            Changed(() => extra.curveValue.postWrapMode = WrapMode.PingPong, "Curve wrap");
            extra.type = ShaderFXParameterType.Transform2D;
            Changed(() => { var tr = extra.transformValue; tr.rotation += .0000000001; extra.transformValue = tr; }, "Double-precision transform");
            Changed(() => { var tr = extra.transformValue; tr.matrix.m21 += .002; extra.transformValue = tr; }, "Stored projective component");
            values.Remove(extra);

            Layer consumer = new ColorFillLayerBehaviour(); doc.layers.Add(consumer); consumer.fx.Add(fx); Call(doc, "NormalizeModel");
            var bind = typeof(WhimTexDocument).GetMethod("BindShaderTextureLayers", F);
            object[] args = { fx, consumer, context, input };
            var first = (IDisposable)bind.Invoke(doc, args); var second = (IDisposable)bind.Invoke(doc, args);
            try
            {
                t.True(!ReferenceEquals(first, second), "Nested inputs have independent leases");
                Call(first, "Apply", material); t.Equal(input, material.GetTexture("_Map"), "Self binds borrowed input");
                second.Dispose(); t.Equal(input, material.GetTexture("_Map"), "Unapplied nested lease cannot clear outer material");
            }
            finally { first.Dispose(); second.Dispose(); }
            t.True(material.GetTexture("_Map") == null && input.IsCreated(), "Dispose clears material but does not release Self");
            foreach (var p in values) if (p.type == ShaderFXParameterType.Texture2D) p.textureSource = ShaderFXTextureSource.None;
            Prepare();
            var none = (IDisposable)bind.Invoke(doc, args);
            try
            {
                Call(none, "Apply", material); var clear = material.GetTexture("_Map");
                t.True(clear != null && clear.width == 13 && clear.height == 7, "None preserves render-sized input and texel size");
                t.Equal(clear, material.GetTexture("_Other"), "Repeated clear input shares one owned RT");
            }
            finally { none.Dispose(); }
            t.True(material.GetTexture("_Map") == null && material.GetTexture("_Other") == null, "All temporary bindings cleared");
            foreach (var p in values) if (p.type == ShaderFXParameterType.Texture2D) p.textureSource = ShaderFXTextureSource.Texture;
            t.True(bind.Invoke(doc, args) == null, "Asset texture path allocates no lease");
            var changedDocSize = doc.width = 52; Prepare();
            t.Near(changedDocSize, material.GetVector("_CanvasSize").x, 0, "Context dimensions are never cached as parameter values");

            Undo.IncrementCurrentGroup();
            Undo.RecordObject(fx, "Owned FX preparation test"); values[0].floatValue = .875f; Undo.FlushUndoRecordObjects(); Prepare();
            t.Near(.875, material.GetFloat("_A"), 0, "Prepared before Undo");
            Undo.PerformUndo(); material = Prepare(); t.Near(.625, material.GetFloat("_A"), 0, "Undo rematches deserialized parameter objects");
            Undo.PerformRedo(); material = Prepare(); t.Near(.875, material.GetFloat("_A"), 0, "Redo rematches parameter objects");
            t.True(!JsonUtility.ToJson(fx).Contains("parameterPlan"), "Runtime plans are not serialized");
            t.Equal(oldActive, RenderTexture.active, "Binding preserves caller RT"); t.Equal(srgb, GL.sRGBWrite, "Binding preserves write state");
        }
        finally
        {
            Undo.ClearUndo(fx); RenderTexture.active = oldActive; GL.sRGBWrite = srgb;
            Object.DestroyImmediate(fx); Object.DestroyImmediate(doc); input.Release(); Object.DestroyImmediate(input);
        }
    });
    static Timing Measure(string name, Action action, int batch)
    {
        for (int i = 0; i < 16; i++) action();
        var samples = new double[11];
        for (int r = 0; r < samples.Length; r++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < batch; i++) action();
            samples[r] = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency / batch;
        }
        Array.Sort(samples);
        // Same public GC.Alloc recorder and immediate main-thread counting semantics as the product profiler.
        // Separate from timing: counting overhead cannot change the reported stage timings.
        using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 16384, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        recorder.Reset(); recorder.Start();
        for (int i = 0; i < 64; i++) action();
        double allocations = recorder.Valid && recorder.Count < 16384 ? recorder.Count / 64.0 : -1;
        return new Timing { name = name, medianUs = samples[5], p95Us = samples[10], allocations = allocations };
    }
    static string Pixels(Material material, RenderTexture input)
    {
        var rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var texture = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true); var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(input, rt, material); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 32, 32), 0, 0, false); texture.Apply(false, false);
            return Convert.ToBase64String(texture.GetRawTextureData());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); RenderTexture.ReleaseTemporary(rt); }
    }
}
