using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class AlphaPresenceTests
{
    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private sealed class Detector : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Type type = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexAlphaPresence", true);
        private readonly object instance;
        internal Detector() { instance = Activator.CreateInstance(type, true); }
        internal bool Pending => (bool)type.GetProperty("isPending", Hidden).GetValue(instance);
        internal bool Updating => (bool)type.GetField("updating", Hidden).GetValue(instance);
        internal bool Begin(RenderTexture source, Action<bool?> completed)
            => (bool)type.GetMethod("Begin", Hidden).Invoke(instance, new object[] { source, completed });
        internal void Cancel() => type.GetMethod("Cancel", Hidden).Invoke(instance, null);
        internal void PollRequest() => type.GetMethod("Update", Hidden).Invoke(instance, null);
        internal bool ReadPixel(RenderTexture pixel)
            => (bool)type.GetMethod("ReadSynchronously", Hidden).Invoke(instance, new object[] { pixel });
        internal Material OwnedMaterial => (Material)type.GetField("material", Hidden).GetValue(instance);
        internal Texture2D OwnedReadPixel => (Texture2D)type.GetField("readbackPixel", Hidden).GetValue(instance);
        internal RenderTexture PendingPixel
        {
            get
            {
                object operation = type.GetField("pending", Hidden).GetValue(instance);
                return operation == null ? null : (RenderTexture)operation.GetType().GetField("pixel", Hidden).GetValue(operation);
            }
        }
        public void Dispose() => ((IDisposable)instance).Dispose();
    }

    private sealed class Source : IDisposable
    {
        internal RenderTexture texture;
        private Texture2D upload;

        internal Source(int width, int height, int changedIndex = -1, float alpha = 0f)
        {
            try
            {
                upload = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
                var colors = new Color[width * height];
                for (int index = 0; index < colors.Length; index++) colors[index] = new Color(7f, .4f, -3f, 0f);
                if (changedIndex >= 0) colors[changedIndex].a = alpha;
                upload.SetPixels(colors);
                upload.Apply(false, false);
                texture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Repeat;
                if (upload.graphicsFormat != texture.graphicsFormat) throw new InvalidOperationException("Fixture requires matching float graphics formats.");
                // Copy the actual float bits; the fixture must not quantize tiny or NaN alpha through a blit shader.
                Graphics.CopyTexture(upload, 0, 0, texture, 0, 0);
            }
            catch { Dispose(); throw; }
        }

        internal void ReleaseBorrowed()
        {
            if (texture != null) RenderTexture.ReleaseTemporary(texture);
            texture = null;
        }

        public void Dispose()
        {
            ReleaseBorrowed();
            if (upload != null) UnityEngine.Object.DestroyImmediate(upload);
            upload = null;
        }
    }

    private static async Task Update(CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>();
        EditorApplication.CallbackFunction next = () => completion.TrySetResult(true);
        EditorApplication.update += next;
        using (token.Register(() => completion.TrySetCanceled()))
            try { await completion.Task; }
            finally { EditorApplication.update -= next; }
    }

    private static async Task Drain(Detector detector, CancellationToken token)
    {
        double deadline = EditorApplication.timeSinceStartup + 10d;
        while (detector.Pending)
        {
            token.ThrowIfCancellationRequested();
            if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException("Owned alpha-presence readback did not complete.");
            await Update(token);
        }
    }

    private static async Task Detect(TestContext context, Detector detector, Source source,
        RenderTexture sentinel, bool expected, string label, CancellationToken token)
    {
        bool completed = false;
        bool? result = null;
        RenderTexture previousActive = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        try
        {
            RenderTexture.active = sentinel;
            GL.sRGBWrite = true;
            context.True(detector.Begin(source.texture, value => { completed = true; result = value; }), label + ": accepted");
            context.True(RenderTexture.active == sentinel && GL.sRGBWrite, label + ": caller render state restored immediately");
            context.True(source.texture.IsCreated(), label + ": borrowed source remains created");
            context.Equal(FilterMode.Bilinear, source.texture.filterMode, label + ": source filtering is unchanged");
            context.Equal(TextureWrapMode.Repeat, source.texture.wrapMode, label + ": source wrapping is unchanged");
            if (detector.Pending)
            {
                context.True(detector.Updating, label + ": pending request owns an Editor update subscription");
                bool unexpected = false;
                context.True(!detector.Begin(source.texture, _ => unexpected = true), label + ": second pending request rejected");
                context.True(!unexpected, label + ": rejected request has no callback");
            }
        }
        finally { RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgb; }
        await Drain(detector, token);
        context.True(!detector.Updating, label + ": completion removes the Editor update subscription");
        context.True(completed && result.HasValue, label + ": supported GPU yields a known result");
        context.Equal((bool?)expected, result, label + ": exact alpha presence");
        context.True(source.texture.IsCreated(), label + ": completed readback does not release borrowed source");
    }

    private static async Task Execute(TestContext context, CancellationToken token)
    {
        Shader shader = Shader.Find("Hidden/WhimTex/AlphaPresenceReduce");
        context.True(shader != null && shader.isSupported, "Alpha-presence shader must be supported for the GPU regression");
        context.True(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) &&
            SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat), "Float GPU fixtures are supported");
        context.True((SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) != 0,
            "Exact float texture-to-render-target copies are supported");
        var sentinel = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave };
        var detector = new Detector();
        RenderTexture previousActive = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        try
        {
            context.True(sentinel.Create(), "Owned render-state sentinel is created");
            using (var source = new Source(7, 5))
                await Detect(context, detector, source, sentinel, true, "RGB-bearing zero-alpha image", token);
            using (var source = new Source(257, 129, 257 * 129 - 1, 1e-5f))
                await Detect(context, detector, source, sentinel, false, "Full-size odd image with one tiny-alpha last-corner pixel", token);
            foreach (int index in new[] { 0, 6, 28, 34, 17 })
            {
                using var source = new Source(7, 5, index, 1f);
                await Detect(context, detector, source, sentinel, false, "Single alpha pixel at index " + index, token);
            }
            foreach (float alpha in new[] { 1e-20f, -1e-20f, -.5f, float.NaN, float.PositiveInfinity })
            {
                using var source = new Source(7, 5, 34, alpha);
                await Detect(context, detector, source, sentinel, false, "Odd corner alpha " + alpha, token);
            }
            foreach (bool present in new[] { false, true })
            {
                using var source = new Source(1, 1, present ? 0 : -1, .25f);
                await Detect(context, detector, source, sentinel, !present, "One-pixel source " + present, token);
            }
            using (var source = new Source(1, 9, 8, .1f))
                await Detect(context, detector, source, sentinel, false, "One-column odd image", token);
            using (var source = new Source(9, 1, 8, .1f))
                await Detect(context, detector, source, sentinel, false, "One-row odd image", token);

            bool unknownCompleted = false;
            bool? unknown = true;
            context.True(detector.Begin(null, value => { unknownCompleted = true; unknown = value; }), "Invalid source request is accepted for a diagnostic result");
            context.True(unknownCompleted && !unknown.HasValue && !detector.Pending, "Invalid source reports Unknown without a stale pending request");

            using (var borrowed = new Source(9, 7))
            {
                bool completed = false;
                bool? result = null;
                context.True(detector.Begin(borrowed.texture, value => { completed = true; result = value; }), "Borrowed-release request accepted");
                borrowed.ReleaseBorrowed();
                using (var replacement = new Source(9, 7, 62, 1f))
                {
                    await Drain(detector, token);
                    context.True(completed && result == true, "Immediate source release/pool reuse cannot replace the owned reduction result");
                }
            }

            using (var source = new Source(7, 5, 34, .5f))
            {
                int callbacks = 0;
                context.True(detector.Begin(source.texture, _ => callbacks++), "Cancel fixture request accepted");
                if (detector.Pending)
                {
                    RenderTexture pixel = detector.PendingPixel;
                    detector.Cancel();
                    context.True(detector.Pending && pixel != null && pixel.IsCreated(), "Cancel holds the owned pixel until GPU completion");
                    await Drain(detector, token);
                    context.Equal(0, callbacks, "Cancelled readback suppresses its callback");
                }
                else context.Equal(1, callbacks, "Synchronous fallback completes before cancellation can intervene");
                await Detect(context, detector, source, sentinel, false, "Reuse after cancellation", token);
            }

            using (var source = new Source(7, 5))
            {
                bool completed = false;
                bool? result = null;
                context.True(detector.Begin(source.texture, value => { completed = true; result = value; }),
                    "Delayed readback fixture accepted");
                if (detector.Pending) detector.PollRequest();
                await Drain(detector, token);
                context.True(completed && result == true && !detector.Pending && !detector.Updating,
                    "An asynchronously polled request completes with the actual pixel and releases its update subscription");
                context.True(source.texture.IsCreated(), "Delayed completion preserves borrowed source ownership");
            }

            var fallback = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                foreach (bool present in new[] { false, true })
                {
                    RenderTexture.active = fallback;
                    GL.Clear(false, true, present ? Color.white : Color.clear);
                    RenderTexture.active = sentinel;
                    GL.sRGBWrite = false;
                    context.Equal(!present, detector.ReadPixel(fallback), "Single-pixel synchronous fallback " + present);
                    context.True(RenderTexture.active == sentinel && !GL.sRGBWrite, "Synchronous fallback preserves caller render state");
                }
            }
            finally { RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(fallback); }

            Material ownedMaterial = detector.OwnedMaterial;
            Texture2D ownedReadPixel = detector.OwnedReadPixel;
            using (var source = new Source(7, 5, 34, 1f))
            {
                int callbacks = 0;
                context.True(detector.Begin(source.texture, _ => callbacks++), "Dispose fixture request accepted");
                bool wasPending = detector.Pending;
                detector.Dispose();
                context.True(!detector.Pending && !detector.Updating, "Dispose drains its own pending request and removes its update subscription");
                context.Equal(wasPending ? 0 : 1, callbacks, "Dispose suppresses any in-flight callback");
                context.True(source.texture.IsCreated(), "Dispose leaves borrowed source ownership with the caller");
                context.True(ownedMaterial == null && ownedReadPixel == null, "Dispose destroys only owned material and CPU readback texture");
                bool rejectedCallback = false;
                context.True(!detector.Begin(source.texture, _ => rejectedCallback = true) && !rejectedCallback,
                    "Disposed helper rejects reuse without invoking a callback");
                await Update(token);
                context.Equal(wasPending ? 0 : 1, callbacks, "Late GPU completion after disposal is inert");
            }
        }
        finally
        {
            AsyncD.CleanupOwned(detector.Dispose,
                () => RenderTexture.active = previousActive,
                () => GL.sRGBWrite = previousSrgb,
                () => UnityEngine.Object.DestroyImmediate(sentinel));
        }
    }
}
