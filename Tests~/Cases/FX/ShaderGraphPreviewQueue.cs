using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests.UnityD;

public static partial class ShaderGraphEditingBench
{
    public static string PreviewQueueChecks(string runId) => AsyncD.Start(runId, async (t, token) =>
    {
        string Source() => "Shader \"Hidden/WhimTex/QueueCheck_" + Guid.NewGuid().ToString("N") + "\" { SubShader { Pass { HLSLPROGRAM\n" +
            "#pragma vertex vert\n#pragma fragment frag\n#pragma multi_compile_local _ QUEUE_A\n#pragma multi_compile_local _ QUEUE_B\n" +
            "#include \"UnityCG.cginc\"\nfloat4 vert(float4 p:POSITION):SV_POSITION{return UnityObjectToClipPos(p);}\n" +
            "float4 frag():SV_Target {\n#ifdef QUEUE_A\nreturn float4(.25,.5,.75,1);\n#else\nreturn float4(.75,.5,.25,1);\n#endif\n}\nENDHLSL\n} } }";
        var priorities = new List<object>(); var held = new List<object>(); var materials = new List<Material>();
        Shader shader = null;
        object Priority(bool main, float rank, Func<bool> wanted = null)
        {
            var p = Activator.CreateInstance(TypeOf("ShaderGraphPreparation").GetNestedType("Priority", Flags),
                Flags, null, new object[] { main }, null);
            Set(p, "previewRank", rank); Set(p, "wanted", wanted); return p;
        }
        object Own(Material material, object priority)
        {
            var entry = Static("ShaderExecutableCache", "AcquireVariant", material, priority);
            held.Add(entry); priorities.Add(priority); return entry;
        }
        void Release(int index)
        {
            var entry = held[index]; if (entry == null) return;
            ((IList)Get(entry, "requests")).Remove(priorities[index]); Call(entry, "Release"); held[index] = null;
        }
        try
        {
            var clock = Stopwatch.StartNew();
            while (Counter("ActiveCount") != 0)
            { if (clock.Elapsed.TotalSeconds > 60) throw new TimeoutException("Queue drain"); await NextUpdate(token); }
            shader = ShaderUtil.CreateShaderAsset(Source(), false); shader.hideFlags = HideFlags.HideAndDontSave;
            Material Material(params string[] keywords)
            {
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, shaderKeywords = keywords };
                materials.Add(material); return material;
            }
            bool wanted = false;
            var low = Own(Material("QUEUE_A", "QUEUE_B"), Priority(false, 100, () => wanted));
            var high = Own(Material("QUEUE_B", "QUEUE_A"), Priority(true, 0));
            t.True(ReferenceEquals(low, high), "Keyword order shares an executable");
            t.Equal(-1f, (float)Get(low, "Rank"), "Final demand promotes shared preview executable");
            Release(1);
            t.True(float.IsPositiveInfinity((float)Get(low, "Rank")), "Removed final consumer leaves no stale priority");
            wanted = true;
            t.Equal(100f, (float)Get(low, "Rank"), "Live demand resumes shared request");
            var highAgain = Own(Material("QUEUE_A", "QUEUE_B"), Priority(true, 0));
            t.True(ReferenceEquals(low, highAgain), "Promoted consumer reuses pending entry");
            var other = Own(Material("QUEUE_A"), Priority(false, 20));
            t.True(!ReferenceEquals(low, other), "Keywords distinguish variants");
            var cancelled = Own(Material("QUEUE_B"), Priority(false, 5)); Release(4);
            t.True(!((IList)Get(TypeOf("ShaderExecutableCache"), "entries")).Contains(cancelled), "Last queued owner cancels request");
            var native = Static("ShaderExecutableCache", "Acquire", Source(), false, false, Priority(false, 50), 0, false);
            held.Add(native); priorities.Add(((IList)Get(native, "requests"))[0]);
            long requests = Counter("VariantPassCompilationRequests");
            Static("ShaderExecutableCache", "Update");
            t.True((bool)Get(low, "started"), "Final variant starts before native/node work");
            t.True(!(bool)Get(other, "started") && !(bool)Get(native, "started"), "One unified submission per update");
            Release(0); UnityEngine.Object.DestroyImmediate(materials[0]);
            clock.Restart();
            while (!(bool)Static("ShaderExecutableCache", "IsReady", highAgain) ||
                   !(bool)Static("ShaderExecutableCache", "IsReady", other) || !(bool)Static("ShaderExecutableCache", "IsReady", native))
            {
                t.True(Counter("ActiveCount") <= 2, "SVM and HLSL share two slots");
                if (clock.Elapsed.TotalSeconds > 60) throw new TimeoutException("Shared compile queue");
                await NextUpdate(token);
            }
            t.Equal(2L, Counter("VariantPassCompilationRequests") - requests, "Two variants, two requests despite duplicate consumers");
            var reused = Own(Material("QUEUE_A", "QUEUE_B"), Priority(false, 0));
            t.True(ReferenceEquals(reused, highAgain), "Completed variant is cached");
            t.True((bool)Static("ShaderExecutableCache", "IsReady", reused), "Cached variant is immediately ready");
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true); var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(Texture2D.whiteTexture, target, materials[2]); RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); read.Apply(); var color = read.GetPixel(0, 0);
                t.True(Mathf.Abs(color.r - .25f) < .001f && Mathf.Abs(color.b - .75f) < .001f, "Shared variant renders its keywords");
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(read); }
        }
        finally
        {
            for (int i = 0; i < held.Count; i++) Release(i);
            foreach (var material in materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            t.True(shader != null, "Consumer cleanup preserves borrowed shader");
            if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
        }
    });
}
