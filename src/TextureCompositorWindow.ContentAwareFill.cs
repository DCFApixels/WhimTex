using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private void OpenContentAwareFill()
        {
            FinishPaintingStroke();
            FinishPreviewTransform();
            CanvasSelection selection = GetAreaSelection();
            if (selection == null || !selection.Active || selection.Bounds.width == 0 || selection.Bounds.height == 0) return;
            foreach (var existing in Resources.FindObjectsOfTypeAll<ContentFillWindow>())
                if (existing.owner == this) { existing.Focus(); return; }
            var window = CreateInstance<ContentFillWindow>();
            window.owner = this;
            window.document = compositor;
            window.canvasWidth = compositor.width;
            window.canvasHeight = compositor.height;
            window.selection = (byte[])selection.Coverage.Clone();
            window.selectionBounds = selection.Bounds;
            window.sourceLayerId = GetSelectedLayer()?.Id;
            window.titleContent = new GUIContent("Content-Aware Fill");
            window.minSize = new Vector2(400, 540);
            window.ShowUtility();
        }

        // The window owns only snapshots and transient preview textures, never the source pixels.
        private sealed class ContentFillWindow : EditorWindow
        {
            private enum Source { VisibleComposition, SelectedLayer }
            private enum Sampling { Nearby, WholeImage, CustomSelection }
            private enum FillArea { EntireSelection, InnerBorder }
            private enum Quality { Fast, Balanced, High }
            internal TextureCompositorWindow owner;
            internal TextureCompositor document;
            internal int canvasWidth, canvasHeight;
            internal byte[] selection;
            internal RectInt selectionBounds;
            internal string sourceLayerId;
            private Source source;
            private Sampling sampling;
            private FillArea area;
            private Quality quality = Quality.Balanced;
            private int borderWidth = 16, samplingDistance = 128, seed = 1;
            private bool transparentOnly, invertSelection, showBefore, applying;
            private byte[] customSelection;
            private RectInt customBounds, workingBounds, fillBounds;
            private ContentAwareFill.Result result;
            private Texture2D beforeTexture, afterTexture;
            private Color[] original;
            private VisualElement settings;
            private IntegerField widthField, distanceField;
            private Button captureButton, generateButton, variationButton, applyButton, cancelButton;
            private Label message;
            private Image image;
            private Toggle beforeToggle;
            private ProgressBar progress;
            private Task<ContentAwareFill.Result> task;
            private CancellationTokenSource cancellation;
            private int progressValue;
            private static Task activeTask;
            private bool Valid => owner != null && document != null && owner.compositor == document &&
                document.width == canvasWidth && document.height == canvasHeight;

            private void OnEnable()
            {
                TextureCompositor.Changed += OnDocumentChanged;
                TextureCompositor.OutputTextureChanged += OnOutputChanged;
                Undo.undoRedoPerformed += InvalidateSource;
                EditorApplication.projectChanged += InvalidateSource;
                AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            }
            private void OnDisable()
            {
                TextureCompositor.Changed -= OnDocumentChanged;
                TextureCompositor.OutputTextureChanged -= OnOutputChanged;
                Undo.undoRedoPerformed -= InvalidateSource;
                EditorApplication.projectChanged -= InvalidateSource;
                AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
                Cancel();
                // Observe faults and dispose only after the worker has stopped using its token.
                var pending = task; var tokenOwner = cancellation;
                task = null; cancellation = null;
                if (pending != null) pending.ContinueWith(t =>
                {
                    _ = t.Exception; tokenOwner?.Dispose();
                    Interlocked.CompareExchange(ref activeTask, null, pending);
                }, TaskScheduler.Default);
                else tokenOwner?.Dispose();
                ReleasePreview();
            }
            private void OnDocumentChanged(TextureCompositor changed) { if (changed == document) InvalidateSource(); }
            private void OnOutputChanged(CompositorOutputChange change)
            {
                if (document != null && change.ShouldRefresh(document)) InvalidateSource();
            }
            private void InvalidateSource()
            {
                if (applying) return;
                Cancel(); ReleasePreview();
                if (message != null) message.text = "Source changed. Generate a new preview before applying.";
                UpdateControls();
            }
            private void Cancel() => cancellation?.Cancel();
            private void ReleasePreview()
            {
                result = null; original = null;
                if (image != null) image.image = null;
                if (beforeTexture != null) DestroyImmediate(beforeTexture);
                if (afterTexture != null) DestroyImmediate(afterTexture);
                beforeTexture = afterTexture = null;
            }
            public void CreateGUI()
            {
                var root = rootVisualElement;
                root.Clear();
                WhimTexUI.ApplyWindowStyles(root);
                root.AddToClassList("whimtex-content-fill");
                var description = new Label("Rebuild the selected area from existing texture details. The result is added as a new Drawing Layer.");
                description.AddToClassList("whimtex-content-fill-message"); root.Add(description);
                settings = new VisualElement(); root.Add(settings);
                AddEnum("Source", source, value => source = value);
                AddEnum("Fill Area", area, value => area = value);
                var invert = new Toggle("Invert Selection") { value = invertSelection, name = "invertSelection" };
                invert.tooltip = "Fill outside the captured selection instead of inside it. Applied before Inner Border; does not change the canvas selection or custom sampling area.";
                invert.RegisterValueChangedCallback(e => { invertSelection = e.newValue; ChangedSettings(); }); settings.Add(invert);
                widthField = new IntegerField("Border Width (px)") { value = borderWidth };
                widthField.tooltip = "Width of the filled strip INSIDE the selection. The center is untouched.";
                widthField.RegisterValueChangedCallback(e => { borderWidth = Math.Max(1, Math.Min(16384, e.newValue)); widthField.SetValueWithoutNotify(borderWidth); ChangedSettings(); });
                settings.Add(widthField);
                var transparent = new Toggle("Transparent Only") { value = transparentOnly };
                transparent.tooltip = "Fill only empty pixels (alpha at most 0.1%). Preserve visible source pixels.";
                transparent.RegisterValueChangedCallback(e => { transparentOnly = e.newValue; ChangedSettings(); }); settings.Add(transparent);
                AddEnum("Sampling Area", sampling, value => sampling = value);
                distanceField = new IntegerField("Sampling Distance (px)") { value = samplingDistance };
                distanceField.tooltip = "How far outside the selection to look for source details. This does not expand the filled area.";
                distanceField.RegisterValueChangedCallback(e => { samplingDistance = Math.Max(1, Math.Min(16384, e.newValue)); distanceField.SetValueWithoutNotify(samplingDistance); ChangedSettings(); }); settings.Add(distanceField);
                captureButton = new Button(CaptureSourceSelection) { text = "Use Current Selection as Sampling Area" };
                captureButton.tooltip = "The fill selection is fixed when this window opens. Select suitable source pixels in the main window, then click here.";
                settings.Add(captureButton);
                AddEnum("Quality", quality, value => quality = value);
                var seedField = new IntegerField("Variation") { value = seed, name = "variation" };
                seedField.RegisterValueChangedCallback(e => { seed = e.newValue; ChangedSettings(); }); settings.Add(seedField);
                foreach (var field in settings.Children()) field.AddToClassList("whimtex-content-fill-field");
                image = new Image { scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("whimtex-content-fill-preview"); root.Add(image);
                beforeToggle = new Toggle("Show Before");
                beforeToggle.RegisterValueChangedCallback(e => { showBefore = e.newValue; RefreshImage(); }); root.Add(beforeToggle);
                progress = new ProgressBar { title = "", lowValue = 0, highValue = 100 }; root.Add(progress);
                message = new Label("Selection captured. Choose settings, then click Preview.");
                message.AddToClassList("whimtex-content-fill-message"); root.Add(message);
                var buttons = new VisualElement(); buttons.AddToClassList("whimtex-content-fill-actions"); root.Add(buttons);
                generateButton = new Button(Generate) { text = "Preview" }; buttons.Add(generateButton);
                variationButton = new Button(() => { seed = unchecked(seed + 1); settings.Q<IntegerField>("variation").SetValueWithoutNotify(seed); Generate(); }) { text = "New Variation" }; buttons.Add(variationButton);
                applyButton = new Button(Apply) { text = "Apply" }; buttons.Add(applyButton);
                cancelButton = new Button(() => { if (task != null) Cancel(); else Close(); }) { text = "Close" }; buttons.Add(cancelButton);
                UpdateControls();
            }
            private void AddEnum<T>(string label, T value, Action<T> set) where T : Enum
            {
                var field = new EnumField(label, value);
                TwoChoiceDropdown.Attach(field);
                field.RegisterValueChangedCallback(e => { set((T)e.newValue); ChangedSettings(); }); settings.Add(field);
            }
            private void ChangedSettings()
            {
                Cancel(); ReleasePreview(); UpdateControls();
                if (message != null) message.text = "Settings changed. Click Preview to see the result.";
            }
            private void UpdateControls()
            {
                if (generateButton == null) return;
                bool busy = task != null;
                settings.SetEnabled(!busy && Valid);
                widthField.SetEnabled(area == FillArea.InnerBorder);
                distanceField.SetEnabled(sampling == Sampling.Nearby);
                captureButton.SetEnabled(sampling == Sampling.CustomSelection);
                bool canRun = Valid && !busy && (activeTask == null || activeTask.IsCompleted);
                generateButton.SetEnabled(canRun); variationButton.SetEnabled(canRun);
                applyButton.SetEnabled(Valid && !busy && result != null);
                beforeToggle.SetEnabled(result != null);
                cancelButton.text = busy ? "Cancel" : "Close";
            }
            private void CaptureSourceSelection()
            {
                if (!Valid) return;
                var current = owner.GetAreaSelection();
                if (current == null || !current.Active || current.Bounds.width == 0 || current.Bounds.height == 0)
                { message.text = "Select source pixels in the main window first."; return; }
                customSelection = (byte[])current.Coverage.Clone(); customBounds = current.Bounds;
                ChangedSettings(); message.text = "Sampling selection captured. The original fill selection is unchanged.";
            }
            private void Generate()
            {
                if (!Valid || task != null || (activeTask != null && !activeTask.IsCompleted)) return;
                ReleasePreview();
                try
                {
                    owner.FinishPaintingStroke(); owner.FinishPreviewTransform();
                    if (!Valid) throw new InvalidOperationException("The source document changed. Reopen Content-Aware Fill.");
                    Layer layer = source == Source.SelectedLayer ? document.FindLayer(sourceLayerId) : null;
                    if (source == Source.SelectedLayer && (layer == null || layer.Behaviour == null))
                        throw new InvalidOperationException("The originally selected layer is unavailable. Choose Visible Composition or reopen this window with a layer selected.");
                    if (sampling == Sampling.CustomSelection && customSelection == null)
                        throw new InvalidOperationException("Select source pixels in the main window and capture the sampling area first.");
                    fillBounds = GetFillBounds();
                    if (fillBounds.width == 0 || fillBounds.height == 0)
                        throw new InvalidOperationException("No pixels to fill. Turn off Invert Selection or reopen with a smaller selection.");
                    RectInt sampleBounds = sampling == Sampling.WholeImage ? new RectInt(0, 0, canvasWidth, canvasHeight) :
                        sampling == Sampling.CustomSelection ? customBounds : Bounds(
                            Math.Max(0, fillBounds.xMin - samplingDistance), Math.Max(0, fillBounds.yMin - samplingDistance),
                            Math.Min(canvasWidth, fillBounds.xMax + samplingDistance), Math.Min(canvasHeight, fillBounds.yMax + samplingDistance));
                    workingBounds = Bounds(Math.Min(fillBounds.xMin, sampleBounds.xMin), Math.Min(fillBounds.yMin, sampleBounds.yMin),
                        Math.Max(fillBounds.xMax, sampleBounds.xMax), Math.Max(fillBounds.yMax, sampleBounds.yMax));
                    int w = workingBounds.width, h = workingBounds.height;
                    if ((long)w * h > ContentAwareFill.MaximumWorkingPixels)
                        throw new InvalidOperationException("The selection and sampling area together exceed 4 million pixels. Use a smaller selection or sampling distance.");
                    Texture2D rendered = document.RenderAreaSelectionSource(layer);
                    try { original = rendered.GetPixels(workingBounds.x, workingBounds.y, w, h); }
                    finally { if (rendered != null) DestroyImmediate(rendered); }
                    var selected = new byte[w * h]; var donors = new byte[w * h];
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x, sx = x + workingBounds.x, sy = y + workingBounds.y, si = sy * canvasWidth + sx;
                        selected[i] = invertSelection ? (byte)(255 - selection[si]) : selection[si];
                        donors[i] = sampling == Sampling.CustomSelection ? customSelection[si] : sampleBounds.Contains(new Vector2Int(sx, sy)) ? (byte)255 : (byte)0;
                    }
                    var input = new ContentAwareFill.Input { width = w, height = h, pixels = original, donors = donors, seed = seed, quality = (int)quality };
                    bool border = area == FillArea.InnerBorder, empty = transparentOnly;
                    int width = borderWidth;
                    cancellation = new CancellationTokenSource(); var token = cancellation.Token;
                    progressValue = 0;
                    task = Task.Run(() =>
                    {
                        input.target = ContentAwareFill.TargetMask(selected, input.pixels, w, h, border, width, empty, token);
                        return ContentAwareFill.Run(input, token, p => Volatile.Write(ref progressValue, (int)(p * 100)));
                    }, token);
                    activeTask = task;
                    message.text = "Calculating… You can cancel without changing the document.";
                }
                catch (Exception e) { ReleasePreview(); message.text = e.Message; }
                UpdateControls();
            }
            private void Update()
            {
                if (!Valid) { Cancel(); if (message != null) message.text = "The source window or canvas changed. Close this window and start again."; }
                if (task != null)
                {
                    if (progress != null) { progress.value = Volatile.Read(ref progressValue); progress.title = $"{progress.value:0}%"; }
                    if (task.IsCompleted)
                    {
                        try
                        {
                            var completed = task.GetAwaiter().GetResult();
                            if (!cancellation.IsCancellationRequested && Valid)
                            {
                                result = completed;
                                beforeTexture = MakePreview(original, null);
                                afterTexture = MakePreview(original, result);
                                RefreshImage();
                                message.text = "Preview ready. Apply adds the filled pixels on a new Drawing Layer.";
                            }
                            else ReleasePreview();
                        }
                        catch (OperationCanceledException) { ReleasePreview(); if (message != null) message.text = "Cancelled. The document was not changed."; }
                        catch (Exception e) { ReleasePreview(); if (message != null) message.text = e.Message; }
                        finally
                        {
                            Interlocked.CompareExchange(ref activeTask, null, task);
                            task = null; cancellation.Dispose(); cancellation = null;
                        }
                    }
                }
                UpdateControls();
            }
            // A small display-only sRGB preview. Final pixels stay linear HDR at native resolution.
            private Texture2D MakePreview(Color[] sourcePixels, ContentAwareFill.Result fill)
            {
                int w = workingBounds.width, h = workingBounds.height;
                float ratio = Math.Min(1f, 768f / Math.Max(w, h));
                int pw = Math.Max(1, (int)(w * ratio)), ph = Math.Max(1, (int)(h * ratio));
                var colors = new Color32[pw * ph];
                for (int y = 0; y < ph; y++) for (int x = 0; x < pw; x++)
                {
                    int i = Math.Min(h - 1, y * h / ph) * w + Math.Min(w - 1, x * w / pw);
                    Color color = HdrUtility.Safe(sourcePixels[i]);
                    if (fill != null && fill.target[i] != 0)
                    {
                        Color over = fill.pixels[i]; over.a *= fill.target[i] / 255f;
                        float alpha = over.a + color.a * (1 - over.a);
                        color = alpha > 0 ? new Color((over.r * over.a + color.r * color.a * (1 - over.a)) / alpha,
                            (over.g * over.a + color.g * color.a * (1 - over.a)) / alpha,
                            (over.b * over.a + color.b * color.a * (1 - over.a)) / alpha, alpha) : Color.clear;
                    }
                    color = color.gamma;
                    float checker = ((x / 8 + y / 8) & 1) == 0 ? .25f : .33f;
                    colors[y * pw + x] = new Color(Mathf.Lerp(checker, Mathf.Clamp01(color.r), color.a),
                        Mathf.Lerp(checker, Mathf.Clamp01(color.g), color.a), Mathf.Lerp(checker, Mathf.Clamp01(color.b), color.a), 1);
                }
                var texture = new Texture2D(pw, ph, TextureFormat.RGBA32, false)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                try { texture.SetPixels32(colors); texture.Apply(false, false); return texture; }
                catch { DestroyImmediate(texture); throw; }
            }
            private void RefreshImage() { if (image != null) image.image = showBefore ? beforeTexture : afterTexture; }
            private static RectInt Bounds(int x0, int y0, int x1, int y1) => new RectInt(x0, y0, x1 - x0, y1 - y0);
            private RectInt GetFillBounds()
            {
                if (!invertSelection) return selectionBounds;
                int left = canvasWidth, bottom = canvasHeight, right = 0, top = 0;
                for (int y = 0; y < canvasHeight; y++) for (int x = 0; x < canvasWidth; x++)
                {
                    if (selection[y * canvasWidth + x] == 255) continue;
                    left = Math.Min(left, x); bottom = Math.Min(bottom, y);
                    right = Math.Max(right, x + 1); top = Math.Max(top, y + 1);
                }
                return right > left && top > bottom ? Bounds(left, bottom, right, top) : default;
            }
            private void Apply()
            {
                if (!Valid || task != null || result == null) return;
                owner.FinishPaintingStroke(); owner.FinishPreviewTransform();
                if (!Valid || result == null) return;
                Texture2D texture = null;
                try
                {
                    // Store only the selection's bounding rectangle, not the wider sampling region.
                    int w = fillBounds.width, h = fillBounds.height;
                    var pixels = new Color[w * h];
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    {
                        int i = (y + fillBounds.y - workingBounds.y) * result.width + x + fillBounds.x - workingBounds.x;
                        if (result.target[i] == 0) continue;
                        Color color = result.pixels[i]; color.a *= result.target[i] / 255f; pixels[y * w + x] = color;
                    }
                    texture = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true)
                        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    texture.SetPixels(pixels); texture.Apply(false, false);
                    Texture2D output = texture; bool inserted = false;
                    applying = true;
                    owner.ExecuteContextChange("Content-Aware Fill", () =>
                    {
                        var layer = DrawingLayerBehaviour.FromMergedTexture(output);
                        layer.layerName = "Content-Aware Fill";
                        layer.colorRange = LayerColorRange.HDR; layer.blendRange = LayerBlendRange.HDR;
                        var placement = TextureTransform.Default;
                        placement.scale = new Vector2((float)w / canvasWidth, (float)h / canvasHeight);
                        placement.position = new Vector2(fillBounds.x + w * .5f - canvasWidth * .5f, fillBounds.y + h * .5f - canvasHeight * .5f);
                        layer.transform = placement;
                        layer.MakeTexturePersistent(document);
                        Undo.RegisterCreatedObjectUndo(output, "Content-Aware Fill");
                        // Creating a Unity object flushes pending snapshots; record the model after it.
                        Undo.RegisterCompleteObjectUndo(document, "Content-Aware Fill");
                        document.layers.Insert(0, layer); owner.SelectOnlyLayer(layer.Id); inserted = true;
                    });
                    if (inserted && document.layers.Exists(l => l?.Behaviour is DrawingLayerBehaviour d && d.StoredTexture == output))
                    { texture = null; Close(); }
                }
                catch (Exception e) { message.text = e.Message; }
                finally { applying = false; if (texture != null) DestroyImmediate(texture, true); }
            }
        }
    }
}
