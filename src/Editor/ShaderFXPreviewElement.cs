using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXPreviewThumb : VisualElement
    {
        internal ShaderFXPreviewThumb(int intervalSide = 0)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-fx-preview-thumb");
            if (intervalSide != 0) AddToClassList("whimtex-fx-preview-thumb--half");
            generateVisualContent += context =>
            {
                // Fixed logical-pixel geometry; the native dragger still owns input and positioning.
                var pen = context.painter2D;
                pen.fillColor = resolvedStyle.color;
                pen.strokeColor = new Color32(30, 30, 30, 255);
                pen.lineWidth = 1f;
                pen.BeginPath();
                if (intervalSide != 0)
                {
                    // Half triangles extend outside the interval, with their vertical edges at the endpoints.
                    float Edge(float x) => intervalSide < 0 ? 8f - x : x;
                    pen.MoveTo(new Vector2(Edge(.5f), 1.14f));
                    pen.BezierCurveTo(new Vector2(Edge(.86f), 1.14f), new Vector2(Edge(1.22f), 1.32f), new Vector2(Edge(1.46f), 1.68f));
                    pen.LineTo(new Vector2(Edge(6.14f), 8.4f));
                    pen.BezierCurveTo(new Vector2(Edge(6.98f), 9.6f), new Vector2(Edge(6.26f), 11.4f), new Vector2(Edge(4.82f), 11.4f));
                    pen.LineTo(new Vector2(Edge(.5f), 11.4f));
                    pen.ClosePath();
                    pen.Fill(); pen.Stroke();
                    return;
                }
                pen.MoveTo(new Vector2(5.2f, 1.4f));
                pen.BezierCurveTo(new Vector2(5.6f, .8f), new Vector2(6.4f, .8f), new Vector2(6.8f, 1.4f));
                pen.LineTo(new Vector2(10.7f, 7f));
                pen.BezierCurveTo(new Vector2(11.4f, 8f), new Vector2(10.8f, 9.5f), new Vector2(9.6f, 9.5f));
                pen.LineTo(new Vector2(2.4f, 9.5f));
                pen.BezierCurveTo(new Vector2(1.2f, 9.5f), new Vector2(.6f, 8f), new Vector2(1.3f, 7f));
                pen.ClosePath();
                pen.Fill(); pen.Stroke();
            };
        }
    }

    // Inspector-local lifetime, shared by all surfaces of one FX. No user assets or live input RTs are retained.
    internal sealed class ShaderFXPreviewCache : IDisposable
    {
        internal sealed class Program
        {
            internal Shader shader;
            internal Material material;
            internal string error;
        }

        private static readonly Dictionary<UnityObjectID, ShaderFXPreviewCache> entries = new();
        private static List<ShaderFXPreviewCache> pending = new(), processing = new();
        private static int epoch;
        internal static int Epoch => epoch;
        private static readonly Regex Input = new(@"\b(SampleInput|_MainTex|_MainTex_TexelSize|LayerToLocal|_InputSize|_RenderScale)\b");
        static ShaderFXPreviewCache()
        {
            EditorApplication.update += Update;
            ShaderFX.Changed += RequestUpdate;
            Undo.undoRedoPerformed += RequestAll;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }
        private readonly ShaderFX effect;
        private readonly UnityObjectID id;
        private readonly Dictionary<string, Program> programs = new(StringComparer.Ordinal);
        private readonly Dictionary<ShaderFXParameterControl, Program> controls = new();
        private readonly List<ShaderFXPreviewElement> surfaces = new(), refreshing = new();
        private bool queued;
        private string code, source, path, expanded, sourceError;
        private Shader compiled;
        private int references, stampEpoch = -1;
        private ulong stamp, boundStamp, inputStamp;
        private bool sourceUsesInput, hasTextureParameters, bound, inputChecked, needsInput;
        private Vector2 boundDimensions;
        private float boundPixelScale;
        private Material valuesMaterial;
        private ShaderParameterBindings bindings;
        internal Texture2D Empty { get; private set; }
        internal int FingerprintCount { get; private set; }
        internal int BindingCount { get; private set; }
        internal int ProgramCount => programs.Count;
        private ShaderFXPreviewCache(ShaderFX effect) { this.effect = effect; id = UnityObjectID.FromObject(effect); }

        private static void RequestUpdate(ShaderFX effect)
        {
            if (effect != null && entries.TryGetValue(UnityObjectID.FromObject(effect), out var entry)) entry.RequestUpdate();
        }
        private static void RequestAll()
        {
            foreach (var entry in entries.Values) entry.RequestUpdate();
        }
        internal void RequestUpdate()
        {
            if (queued) return;
            queued = true;
            pending.Add(this);
        }
        internal void Attach(ShaderFXPreviewElement surface)
        {
            surfaces.Add(surface);
            RequestUpdate();
        }
        internal void Detach(ShaderFXPreviewElement surface) => surfaces.Remove(surface);
        private static void Update()
        {
            unchecked { epoch++; }
            var next = processing; processing = pending; pending = next;
            try
            {
                for (int i = 0; i < processing.Count; i++)
                {
                    var entry = processing[i];
                    entry.queued = false;
                    if (entry.references == 0 || entry.effect == null) continue;
                    entry.refreshing.AddRange(entry.surfaces);
                    try
                    {
                        foreach (var surface in entry.refreshing)
                            try { surface.UpdatePreview(); }
                            catch (Exception error) { Debug.LogException(error); }
                    }
                    finally { entry.refreshing.Clear(); }
                }
            }
            finally { processing.Clear(); }
        }

        internal static ShaderFXPreviewCache Acquire(ShaderFX effect)
        {
            var id = UnityObjectID.FromObject(effect);
            if (!entries.TryGetValue(id, out var entry)) entries.Add(id, entry = new ShaderFXPreviewCache(effect));
            entry.references++;
            return entry;
        }
        internal void Release()
        {
            if (--references != 0) return;
            if (entries.TryGetValue(id, out var current) && current == this) entries.Remove(id);
            Dispose();
        }
        private static void Clear()
        {
            foreach (var entry in entries.Values) entry.Dispose();
            entries.Clear();
            pending.Clear(); processing.Clear();
        }
        internal ulong Stamp
        {
            get
            {
                if (stampEpoch != epoch)
                { stamp = effect.RenderCacheStamp(); stampEpoch = epoch; FingerprintCount++; }
                return stamp;
            }
        }
        internal bool NeedsInput
        {
            get
            {
                if (sourceUsesInput) return true;
                if (!hasTextureParameters) return false;
                ulong currentStamp = Stamp;
                if (inputChecked && inputStamp == currentStamp) return needsInput;
                needsInput = sourceUsesInput;
                if (!needsInput)
                    foreach (var value in effect.Parameters)
                        if (value.type == ShaderFXParameterType.Texture2D &&
                            (value.textureSource == ShaderFXTextureSource.Self || value.textureSource == ShaderFXTextureSource.Layer))
                        { needsInput = true; break; }
                inputChecked = true; inputStamp = currentStamp;
                return needsInput;
            }
        }
        internal Program GetProgram(ShaderFXParameterControl control, ShaderFXParameter parameter)
        {
            if (code != effect.Code || source != effect.PreviewCompiledSource || compiled != effect.PreviewCompiledShader || path != effect.SourcePath)
            {
                Dispose();
                code = effect.Code; source = effect.PreviewCompiledSource; compiled = effect.PreviewCompiledShader; path = effect.SourcePath;
                try
                {
                    expanded = ShaderFXSourceBuilder.DocumentCode(code, path) ?? string.Empty;
                    sourceUsesInput = Input.IsMatch(expanded);
                    hasTextureParameters = false;
                    foreach (var value in effect.Parameters)
                        if (value.type == ShaderFXParameterType.Texture2D) { hasTextureParameters = true; break; }
                }
                catch (Exception error) { sourceError = error.Message; }
            }
            if (controls.TryGetValue(control, out var result)) return result;
            string call;
            try
            {
                if (sourceError != null) throw new InvalidOperationException(sourceError);
                call = ShaderFXPreviewFunction.Call(expanded, control.previewFunction, parameter, control.type, control.previewParameters, effect.Parameters);
            }
            catch (Exception error)
            { result = new Program { error = error.Message }; controls.Add(control, result); return result; }
            if (!programs.TryGetValue(call, out result))
            {
                programs.Add(call, result = new Program());
                try
                {
                    string shaderSource = ShaderFXSourceBuilder.BuildResolvedPreview(effect, control, expanded, call);
                    result.shader = ShaderUtil.CreateShaderAsset(shaderSource, false);
                    if (result.shader == null) throw new InvalidOperationException("Unity could not create the preview shader.");
                    result.shader.hideFlags = HideFlags.HideAndDontSave;
                    result.material = new Material(result.shader) { hideFlags = HideFlags.HideAndDontSave };
                    if (result.material.passCount > 0) ShaderUtil.CompilePass(result.material, 0, true);
                    foreach (var message in ShaderUtil.GetShaderMessages(result.shader))
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                            throw new InvalidOperationException(message.message);
                    if (!result.shader.isSupported || result.material.passCount == 0) throw new InvalidOperationException("Unsupported preview shader.");
                }
                catch (Exception error)
                {
                    if (result.material != null) UnityEngine.Object.DestroyImmediate(result.material);
                    if (result.shader != null) UnityEngine.Object.DestroyImmediate(result.shader);
                    result.material = null; result.shader = null; result.error = error.Message;
                }
            }
            controls.Add(control, result);
            return result;
        }
        internal void Bind(Material target, Vector2 dimensions, float pixelScale, ulong currentStamp)
        {
            if (valuesMaterial == null)
            {
                valuesMaterial = new Material(target.shader) { hideFlags = HideFlags.HideAndDontSave };
                bindings = new ShaderParameterBindings();
                Empty = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                Empty.SetPixel(0, 0, Color.clear); Empty.Apply(false, true);
            }
            if (!bound || boundStamp != currentStamp || boundDimensions != dimensions || boundPixelScale != pixelScale)
            {
                foreach (var value in effect.Parameters) bindings.Apply(valuesMaterial, value, value, dimensions, pixelScale, textureViews: true);
                valuesMaterial.SetVector("_InputSize", new Vector4(1, 1, 1, 1));
                valuesMaterial.SetVector("_CanvasSize", new Vector4(dimensions.x, dimensions.y, 1f / dimensions.x, 1f / dimensions.y));
                valuesMaterial.SetFloat("_RenderScale", 1f); valuesMaterial.SetFloat("_PixelScale", pixelScale);
                ProjectiveMatrix.Identity.SetShader(valuesMaterial, "_WhimTex_LayerToLocalRow");
                foreach (var value in effect.Parameters)
                    if (value.type == ShaderFXParameterType.Texture2D && value.textureSource != ShaderFXTextureSource.Texture)
                        valuesMaterial.SetTexture(value.name, Empty);
                bound = true; boundStamp = currentStamp; boundDimensions = dimensions; boundPixelScale = pixelScale; BindingCount++;
            }
            target.CopyPropertiesFromMaterial(valuesMaterial);
        }
        public void Dispose()
        {
            foreach (var program in programs.Values)
            {
                if (program.material != null) UnityEngine.Object.DestroyImmediate(program.material);
                if (program.shader != null) UnityEngine.Object.DestroyImmediate(program.shader);
            }
            programs.Clear(); controls.Clear();
            if (valuesMaterial != null) UnityEngine.Object.DestroyImmediate(valuesMaterial);
            if (Empty != null) UnityEngine.Object.DestroyImmediate(Empty);
            valuesMaterial = null; Empty = null; bindings?.Dispose(); bindings = null;
            bound = false; inputChecked = false; stampEpoch = -1; sourceError = null;
        }
    }

    internal static class ShaderFXPreviewRendering
    {
        internal delegate void FrameHandler(ShaderFX effect, Layer layer, int index, LayerRenderContext context,
            Material material, RenderTexture input);
        internal static event FrameHandler Frame;
        internal static void Capture(ShaderFX effect, Layer layer, int index, in LayerRenderContext context,
            Material material, RenderTexture input) => Frame?.Invoke(effect, layer, index, context, material, input);
    }

    internal sealed class ShaderFXPreviewElement : Image
    {
        private readonly ShaderFX effect;
        private readonly ShaderFXParameter parameter;
        private readonly ShaderFXParameterControl control;
        private readonly bool slider;
        private readonly WhimTexDocument document;
        private readonly Layer layer;
        private readonly int fxIndex;
        private ShaderFXPreviewCache cache;
        private Material material;
        private RenderTexture result;
        private bool hasFrame, needsInput, inputDirty;
        private double lastInputDraw;
        private ulong values;
        private int drawWidth, drawHeight;
        private int lastDrawEpoch = int.MinValue;
        internal int DrawCount { get; private set; }
        private readonly IVisualElementScheduledItem update;

        internal ShaderFXPreviewElement(ShaderFX effect, ShaderFXParameter parameter, ShaderFXParameterControl control,
            bool slider, WhimTexDocument document, Layer layer, int fxIndex)
        {
            this.effect = effect; this.parameter = parameter; this.control = control; this.slider = slider;
            this.document = document; this.layer = layer; this.fxIndex = fxIndex;
            name = "preview-" + control.previewFunction;
            pickingMode = PickingMode.Ignore;
            scaleMode = ScaleMode.StretchToFill;
            AddToClassList("whimtex-fx-preview");
            AddToClassList(slider ? "whimtex-fx-preview--slider" : parameter == null
                ? "whimtex-fx-preview--standalone" : "whimtex-fx-preview--field");
            update = schedule.Execute(UpdatePreview).Every(100);
            update.Pause();
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (effect != null)
                {
                    cache = ShaderFXPreviewCache.Acquire(effect);
                    cache.Attach(this);
                }
                ShaderFXPreviewRendering.Frame += Capture;
                AssemblyReloadEvents.beforeAssemblyReload += Release;
                EditorApplication.quitting += Release;
                update.Resume();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ShaderFXPreviewRendering.Frame -= Capture;
                AssemblyReloadEvents.beforeAssemblyReload -= Release;
                EditorApplication.quitting -= Release;
                update.Pause(); Release(); cache?.Detach(this); cache?.Release(); cache = null;
            });
            RegisterCallback<GeometryChangedEvent>(_ => cache?.RequestUpdate());
        }

        private bool Visible()
        {
            if (panel == null || !visible || contentRect.width < 1 || contentRect.height < 1) return false;
            for (VisualElement ancestor = this; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden ||
                    ancestor is ScrollView scroll && !scroll.contentViewport.worldBound.Overlaps(worldBound)) return false;
            return true;
        }

        private bool Prepare()
        {
            if (effect == null || cache == null || !Visible()) return false;
            var program = cache.GetProgram(control, parameter);
            if (material != program.material) { material = program.material; hasFrame = false; }
            needsInput = cache.NeedsInput;
            string message = program.error == null ? control.previewFunction : "Preview " + control.previewFunction + ": " + program.error;
            if (tooltip != message) tooltip = message;
            EnableInClassList("whimtex-fx-preview--error", program.error != null);
            return material != null;
        }

        internal void UpdatePreview()
        {
            if (lastDrawEpoch == ShaderFXPreviewCache.Epoch) return;
            if (!Prepare()) return;
            int width = Mathf.Clamp(Mathf.CeilToInt(contentRect.width * EditorGUIUtility.pixelsPerPoint), 1, 1024);
            int height = Mathf.Clamp(Mathf.CeilToInt(contentRect.height * EditorGUIUtility.pixelsPerPoint), 1, 256);
            ulong stamp = cache.Stamp;
            bool unchanged = hasFrame && values == stamp && width == drawWidth && height == drawHeight && !effect.UsesUnityTimeInputs;
            if (unchanged && (!inputDirty || EditorApplication.timeSinceStartup - lastInputDraw < .1)) return;
            if (needsInput && document != null && layer != null && effect.Active && !effect.IsUnavailable &&
                fxIndex >= 0 && fxIndex < layer.fx.Count && layer.fx[fxIndex] == effect && document.TryFindLayer(layer, out _, out _))
            {
                // One prefix render refreshes all visible previews at this exact stack position.
                try { document.CaptureInspectorFXPrefix(layer, fxIndex + 1); }
                catch (Exception error) { tooltip = "Preview " + control.previewFunction + ": " + error.Message; }
                return;
            }
            var dimensions = document != null ? new Vector2(document.width, document.height) : Vector2.one;
            float pixelScale = document != null ? document.GetPixelScale() : 1f;
            cache.Bind(material, dimensions, pixelScale, stamp);
            Draw(cache.Empty, pixelScale, stamp);
        }

        private void Capture(ShaderFX captured, Layer owner, int index, LayerRenderContext context, Material bound, RenderTexture input)
        {
            if (captured != effect || document == null || context.activeDocument != document ||
                owner != layer || index != fxIndex) return;
            if (!document.CapturingInspectorFX)
            {
                // A canvas frame only invalidates the preview. Its input can be a crop,
                // whereas a preview callback is free to sample anywhere in the image.
                // Remember changes while hidden too, without compiling/drawing in Canvas.
                if (cache == null || !cache.NeedsInput) return;
                inputDirty = true;
                cache.RequestUpdate();
                return;
            }
            if (lastDrawEpoch == ShaderFXPreviewCache.Epoch || !Prepare() || !needsInput) return;
            try
            {
                material.CopyPropertiesFromMaterial(bound);
                Draw(input, context.pixelScale, cache.Stamp);
                inputDirty = false;
                lastInputDraw = EditorApplication.timeSinceStartup;
            }
            catch (Exception error) { tooltip = "Preview " + control.previewFunction + ": " + error.Message; }
            finally
            {
                material?.SetTexture("_MainTex", null);
                foreach (var value in effect.Parameters)
                    if (value.type == ShaderFXParameterType.Texture2D && value.textureSource != ShaderFXTextureSource.Texture)
                        material?.SetTexture(value.name, null);
            }
        }

        private void Draw(Texture input, float pixelScale, ulong stamp)
        {
            if (lastDrawEpoch == ShaderFXPreviewCache.Epoch) return;
            int width = Mathf.Clamp(Mathf.CeilToInt(contentRect.width * EditorGUIUtility.pixelsPerPoint), 1, 1024);
            int height = Mathf.Clamp(Mathf.CeilToInt(contentRect.height * EditorGUIUtility.pixelsPerPoint), 1, 256);
            if (result == null || result.width != width || result.height != height)
            {
                if (result != null) { result.Release(); UnityEngine.Object.DestroyImmediate(result); }
                result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                result.Create(); image = result;
            }
            float units = parameter != null && parameter.pixelUnits ? pixelScale : 1f;
            material.SetVector("_WhimTex_PreviewRange", new Vector4(control.minimum * units, control.maximum * units, slider ? 1f : 0f, 0f));
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try { GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(input, result, material); }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
            drawWidth = width; drawHeight = height; values = stamp; hasFrame = true;
            lastDrawEpoch = ShaderFXPreviewCache.Epoch;
            DrawCount++;
            // Graphics.Blit binds its source to the material. Do not retain a temporary layer RT.
            material.SetTexture("_MainTex", null);
            MarkDirtyRepaint();
        }

        private void Release()
        {
            image = null;
            if (result != null) { result.Release(); UnityEngine.Object.DestroyImmediate(result); }
            result = null; material = null; hasFrame = false; lastDrawEpoch = int.MinValue;
        }
    }
}
