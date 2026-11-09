using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        // Owns preparation, executables and the rendered source. Image/Height presentation stays in the view.
        private sealed class NodePreviewSource : IDisposable
        {
            private ShaderProgramRuntime program;
            private ShaderGraphPreviewVM svm;
            private bool svmAttempted;
            private int svmRevision;
            private bool svmRendered;
            private ulong svmCode, svmValues;
            private string svmPort;
            private ShaderProgramRuntime.CompilationRequest pending;
            private ulong programFingerprint;
            private string programPort;
            private double renderAt;
            private RenderTexture texture;
            private readonly string nodeId;
            private readonly ShaderGraphWindow window;
            private readonly Canvas canvas;
            private readonly Func<bool> wanted;
            private string compiledPort;
            private ulong fingerprint;
            private ulong renderedFrame;
            private bool dirty = true, attempted, disposed;
            private bool renderError;
            private bool errorSVM;
            private int errorSize;
            private int attemptedGeneration;
            private ulong renderedValues;
            internal RenderTexture Texture => texture;
            internal bool HasImage { get; private set; }
            internal uint ImageRevision { get; private set; }
            internal bool DisplayedSVM { get; private set; }
            internal bool IsUpdating { get; private set; }
            internal IReadOnlyList<ShaderGraphMessage> Messages { get; private set; } = Array.Empty<ShaderGraphMessage>();
            internal uint MessagesRevision { get; private set; }
            internal float CompilationPriority { get; private set; }

            internal NodePreviewSource(ShaderGraphWindow window, Canvas canvas, string nodeId, Func<bool> wanted)
            { this.window = window; this.canvas = canvas; this.nodeId = nodeId; this.wanted = wanted; }

            internal void UpdatePriority(float priority, bool visible)
            {
                CompilationPriority = priority;
                pending?.SetPreviewPriority(priority);
                svm?.SetHybridPriority(priority, wanted);
                if (!visible) { CancelPending(); IsUpdating = false; }
            }

            private void PreviewSourceChanged() { HasImage = true; ImageRevision++; }
            private void ClearImage() { HasImage = false; ImageRevision++; }
            private void SetMessages(IReadOnlyList<ShaderGraphMessage> messages)
            { Messages = messages; MessagesRevision++; }

            internal void CancelPending()
            {
                if (svm?.IsHybridPending == true) { svm.Dispose(); svm = null; svmAttempted = false; }
                if (pending == null) return;
                // The shared executable cache owns native work that has already started.
                // Releasing this consumer cancels queued work without stopping another consumer.
                pending.Dispose(); pending = null; attempted = false;
            }
            internal void InvalidateCode(ulong next)
            {
                if (pending != null && fingerprint != next) CancelPending();
                Invalidate();
            }
            internal void Invalidate(bool imageChanged = true)
            {
                dirty |= imageChanged;
                IsUpdating = true;
            }
            internal void Suspend() { Release(); dirty = true; }
            internal bool Tick(ShaderGraphPreviewState state, string port, int size, bool visible)
            {
                if (disposed) return false;
                ulong next = state.PreviewFingerprint(nodeId, window.accurateLighting);
                ulong values = state.ValueFingerprint(nodeId);
                int generation = canvas.PreparationGeneration, failureGeneration = generation;
                if (renderError && !dirty && attemptedGeneration == generation && fingerprint == next &&
                    compiledPort == port && renderedValues == values && errorSVM == window.svmPreviews && errorSize == size)
                { IsUpdating = false; return false; }
                if (renderError) { renderError = false; attempted = false; dirty = true; }
                IsUpdating = visible &&
                    (dirty || texture == null || pending != null || programFingerprint != next || programPort != port);
                bool completed = false;
                try
                {
                    if (visible && window.svmPreviews && TickVM(state, next, port, size, out bool vmChanged)) return vmChanged;
                    if (!window.svmPreviews && svm != null) { svm.Dispose(); svm = null; svmAttempted = false; dirty = true; }
                    if (pending != null &&
                        (fingerprint != next || compiledPort != port || !visible))
                        CancelPending();
                    if (pending != null)
                    {
                        failureGeneration = attemptedGeneration;
                        if (!pending.TryComplete(out var compiled)) return false;
                        failureGeneration = generation;
                        pending.Dispose(); pending = null;
                        if (fingerprint == next && compiledPort == port)
                        {
                            program?.Dispose(); program = compiled;
                            programFingerprint = fingerprint; programPort = compiledPort;
                            dirty = completed = true;
                            SetMessages(program.Messages);
                        }
                        else { compiled.Dispose(); attempted = false; }
                    }
                    if (!visible) return false;
                    dirty |= values != renderedValues;
                    if (dirty) IsUpdating = true;
                    // Returning to the retained output does not need a new preparation job.
                    if (pending == null && program != null && !renderError && programFingerprint == next && programPort == port)
                    { attempted = true; fingerprint = next; compiledPort = port; }
                    bool compile = !attempted || next != fingerprint || port != compiledPort ||
                        renderError && attemptedGeneration != canvas.PreparationGeneration;
                    if (compile)
                    {
                        var current = window.Graph?.FindNode(nodeId);
                        if (current == null) { Release(); return false; }
                        attempted = true; fingerprint = next; compiledPort = port;
                        attemptedGeneration = canvas.PreparationGeneration;
                        using var profile = WhimTexProfiler.Measure("shadergraph.preview.compile", window.document);
                        pending = new ShaderProgramRuntime.CompilationRequest(canvas.Preparation(), nodeId, port,
                            simplifiedLighting: !window.accurateLighting).WithLighting(window.document);
                        pending.SetPreviewPriority(CompilationPriority);
                        pending.SetPreviewDemand(wanted);
                        return true;
                    }
                    if (program == null || programFingerprint != next || programPort != port) { dirty = false; return false; }
                    bool resize = texture == null || texture.width != size;
                    bool animated = program.UsesTime || window.Target.IsEffect && window.Target.Layer.UsesTime;
                    double now = EditorApplication.timeSinceStartup;
                    if (!dirty && !resize && (!animated || now < renderAt || renderedFrame == ShaderGraphClock.Frame)) return completed;
                    var previous = RenderTexture.active;
                    bool srgb = GL.sRGBWrite;
                    try
                    {
                        using var profile = WhimTexProfiler.Measure("shadergraph.preview.render", window.document);
                        if (resize)
                        {
                            if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                            texture = CreateTexture(size);
                        }
                        var host = window.Target;
                        var context = new LayerRenderContext(window.document, null, size, size,
                            (float)Mathf.Max(window.document.width, window.document.height) / size, applyTransform: false, applyFx: false,
                            transformFxCoordinates: host.IsEffect && !host.Layer.IsGroup)
                            .WithShaderGraphPreviewTime(ShaderGraphClock.Seconds);
                        if (host.IsEffect) program.BindEffect(window.Graph, context, state.bindings, branchCurrent: true);
                        else
                        {
                            program.Bind(window.Graph, context, state.bindings, branchCurrent: true);
                            program.PrepareCoordinates(host.Layer, context);
                        }
                        using var input = host.IsEffect
                            ? new ShaderLayerInput(canvas.BorrowFXPreviewInput(context), context, owned: false)
                            : window.document.ResolveShaderInput(host.Behaviour, context);
                        if (host.IsEffect) host.Layer.BindShaderFXCoordinates(program.Material, context);
                        var inputTexture = input.Bind(program.Material, program.EmptyInput);
                        using var bindings = window.document.BindShaderTextureLayers(program.Parameters, host.Layer, context,
                            host.IsEffect ? input.Texture as RenderTexture : null);
                        bindings?.Apply(program.Material);
                        GL.sRGBWrite = false;
                        program.Render(inputTexture, texture, context);
                        PreviewSourceChanged();
                        DisplayedSVM = false;

                        if (!ReferenceEquals(Messages, program.Messages)) SetMessages(program.Messages);
                        renderError = false; dirty = false;
                        IsUpdating = false;
                        renderedValues = values;
                        renderedFrame = ShaderGraphClock.Frame;
                        renderAt = now + (EditorWindow.focusedWindow == window ? 1d / 30 : .1d);
                    }
                    finally { program.Material.SetTexture("_MainTex", null); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
                    return true;
                }
                catch (Exception error)
                {
                    pending?.Dispose(); pending = null;
                    if (failureGeneration != canvas.PreparationGeneration)
                    { attempted = false; return true; }
                    renderError = true; dirty = false; renderedValues = values;
                    attemptedGeneration = generation; fingerprint = next; compiledPort = port;
                    errorSVM = window.svmPreviews; errorSize = size;
                    IsUpdating = false;
                    ClearImage();
                    DisplayedSVM = false;
                    SetMessages((error as ShaderGraphException)?.messages ??
                        new[] { new ShaderGraphMessage(nodeId, error.Message, ShaderGraphMessageLevel.Error) });
                    return true;
                }
            }
            private bool TickVM(ShaderGraphPreviewState state, ulong next, string port, int size, out bool changed)
            {
                changed = false;
                ulong values = state.ValueFingerprint(nodeId);
                if (!svmAttempted || svmRevision != ShaderProgramSourceRevision.Current || svmCode != next || svmValues != values || svmPort != port)
                {
                    bool updated = svm != null && svmAttempted && svmRevision == ShaderProgramSourceRevision.Current &&
                        svmCode == next && svmPort == port && svm.TryUpdateValues(window.Graph, state.bindings);
                    svmAttempted = true; svmRevision = ShaderProgramSourceRevision.Current; svmCode = next; svmValues = values; svmPort = port;
                    if (!updated)
                    {
                        var prepared = ShaderGraphPreviewVM.BeginHybrid(window.Graph, nodeId, port, !window.accurateLighting, effect: window.Target.IsEffect);
                        if (svm != null && prepared != null) { svm.ReplaceCode(prepared); prepared.Dispose(); }
                        else { svm?.Dispose(); svm = prepared; }
                    }
                    dirty = true; svmRendered = false;
                    IsUpdating = true;
                }
                if (svm == null) return false;
                if (svm.HasBoundaries && window.Target.IsEffect) { svm.Dispose(); svm = null; return false; }
                svm.ConfigureHybrid(window.document, window.Target.Layer, effect:window.Target.IsEffect);
                svm.SetHybridPriority(CompilationPriority, wanted);
                if (!svm.UpdatePreparation())
                {
                    // A supported program waiting for its shared executor does not
                    // also need a per-node native shader compilation.
                    if (!svm.Failed) return true;
                    svm.Dispose(); svm = null; return false;
                }
                pending?.Dispose(); pending = null;
                program?.Dispose(); program = null; attempted = false;
                dirty |= !svmRendered;
                bool resize = texture == null || texture.width != size;
                double now = EditorApplication.timeSinceStartup;
                bool animated=svm.UsesTime || window.Target.IsEffect && window.Target.Layer.UsesTime;
                if (!dirty && !resize && (!animated || now < renderAt || renderedFrame == ShaderGraphClock.Frame))
                { IsUpdating = false; return true; }
                if (resize)
                {
                    if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                    texture = CreateTexture(size);
                }
                var vmContext = new LayerRenderContext(window.document, null, size, size,
                    (float)Mathf.Max(window.document.width, window.document.height) / size, applyTransform: false, applyFx: false,
                    transformFxCoordinates: window.Target.IsEffect && !window.Target.Layer.IsGroup)
                    .WithShaderGraphPreviewTime(ShaderGraphClock.Seconds);
                bool rendered=window.Target.IsEffect
                    ? svm.RenderEffectPreview(texture,vmContext,svm.NeedsInput?canvas.BorrowFXPreviewInput(vmContext):null)
                    : svm.RenderBoundaries(texture,vmContext);
                if (!rendered) return true;
                PreviewSourceChanged();
                DisplayedSVM = true;
                SetMessages(Array.Empty<ShaderGraphMessage>());
                programFingerprint = next; programPort = port;
                renderedValues = values; renderedFrame = ShaderGraphClock.Frame;
                dirty = renderError = false;
                IsUpdating = false;
                svmRendered = true;
                renderAt = now + (EditorWindow.focusedWindow == window ? 1d / 30 : .1d);
                changed = true;
                return true;
            }
            private static RenderTexture CreateTexture(int size)
            {
                var result = ProfiledRenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                result.filterMode = FilterMode.Bilinear; result.wrapMode = TextureWrapMode.Clamp;
                return result;
            }
            private void Release()
            {
                ClearImage();
                DisplayedSVM = false;
                IsUpdating = false;
                svm?.Dispose(); svm = null; svmAttempted = svmRendered = false;
                pending?.Dispose(); pending = null; program?.Dispose(); program = null;
                if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                texture = null; attempted = renderError = false;
            }
            public void Dispose() { if (disposed) return; disposed = true; Release(); }
        }
    }
}
