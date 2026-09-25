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
        private DrawingLayerBehaviour.HealingStrokeBuffer healingStroke;
        private int healingPointer = -1;
        private TextureCompositor healingDocument;
        private DrawingLayerBehaviour healingLayer;
        private Vector2Int healingCanvasSize;
        private int healingSelectionRevision;
        private ProjectiveMatrix healingTransform;
        private HealingJob healingJob;
        private static Task healingWorker;
        private VisualElement healingOverlay;
        private Label healingStatus;
        private Button healingCancel, previewHealingButton;

        private sealed class HealingJob
        {
            internal readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            internal Task<ContentAwareFill.Result> task;
            internal RectInt bounds;
            internal bool tiled;
            internal int progress;
        }

        private void AddHealingSettings()
        {
            var row = CreatePreviewSettingsRow();
            BindPreviewSettingsRow(row, PreviewTool.HealingBrush);
            var size = CompactField(new FloatField("Size"), 88f);
            size.AddToClassList("whimtex-blur-size");
            toolkitHeaderBindings.Track(size, () => paintSettings.healingSize);
            size.RegisterValueChangedCallback(e => ApplyPaintToolChange(() =>
                paintSettings.healingSize = float.IsFinite(e.newValue) ? Mathf.Clamp(e.newValue, 1, 512) : 32));
            row.Add(size);
            var hardness = CompactField(new Slider("Hardness", 0, 100)
            { showInputField = true, tooltip = "Softness of the repair mask edge." }, 160f);
            hardness.AddToClassList("whimtex-blur-hardness");
            toolkitHeaderBindings.Track(hardness, () => paintSettings.healingHardness * 100f);
            hardness.RegisterValueChangedCallback(e => ApplyPaintToolChange(() =>
                paintSettings.healingHardness = float.IsFinite(e.newValue) ? Mathf.Clamp01(e.newValue * .01f) : .8f));
            row.Add(hardness);
            var search = CompactField(new IntegerField("Search"), 110f);
            search.AddToClassList("whimtex-healing-search");
            search.tooltip = "Source search margin around the whole stroke, in canvas pixels.";
            toolkitHeaderBindings.Track(search, () => paintSettings.healingSearch);
            search.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.healingSearch = Mathf.Clamp(e.newValue, 8, 512)));
            row.Add(search);
            var source = CompactField(new EnumField(paintSettings.healingSample), 150f);
            source.tooltip = "Current Layer: raw pixels before FX. Current & Below: visible stack from this layer down within its group. Writes only this Drawing layer.";
            toolkitHeaderBindings.Track(source, () => (Enum)paintSettings.healingSample);
            source.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.healingSample = (HealingSampleMode)e.newValue));
            row.Add(source);
            var quality = CompactField(new EnumField(paintSettings.healingQuality), 90f);
            quality.tooltip = "More iterations improve matching but take longer.";
            toolkitHeaderBindings.Track(quality, () => (Enum)paintSettings.healingQuality);
            quality.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.healingQuality = (HealingQuality)e.newValue));
            row.Add(quality);
            var empty = new Toggle("Transparent Only") { tooltip = "Only fill empty pixels in the sampled source; preserve visible pixels." };
            toolkitHeaderBindings.Track(empty, () => paintSettings.healingTransparentOnly);
            empty.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.healingTransparentOnly = e.newValue));
            row.Add(empty);
            healingStatus = new Label();
            row.Add(healingStatus);
            healingCancel = new Button(CancelHealing) { text = "Cancel", tooltip = "Cancel the stroke or pending calculation without changing pixels." };
            row.Add(healingCancel);
            toolkitPreviewHeader.Add(row);
            RefreshHealingStatus();
        }

        private void BuildHealingOverlay()
        {
            healingOverlay = new VisualElement { name = "healingOverlay", pickingMode = PickingMode.Ignore };
            healingOverlay.AddToClassList("whimtex-healing-overlay");
            healingOverlay.generateVisualContent += DrawHealingOverlay;
            toolkitPreviewCanvas.Add(healingOverlay);
            toolkitPreviewCanvas.RegisterCallback<PointerCancelEvent>(_ => CancelHealing());
            toolkitPreviewCanvas.RegisterCallback<DetachFromPanelEvent>(_ => CancelHealing());
        }

        private bool HandleHealingDown(PointerDownEvent evt)
        {
            if (previewTool != PreviewTool.HealingBrush || evt.button != 0 || evt.altKey) return false;
            WhimTexUI.ConsumeEvent(evt);
            if (healingJob != null || healingWorker != null && !healingWorker.IsCompleted)
            { ShowNotification(new GUIContent("Healing is still running. Wait or cancel it first.")); return true; }
            var layer = GetSelectedLayer()?.Behaviour as DrawingLayerBehaviour;
            if (layer == null || compositor == null || WhimTexApi.IsLayerContentLocked(compositor, layer) ||
                !PreviewContainsPaintPoint(evt.localPosition)) return true;
            var transform = compositor.GetCanvasTransform(layer).ToMatrix(compositor.width, compositor.height);
            if (!transform.TryInverse(out _))
            { ShowNotification(new GUIContent("Healing needs an invertible layer transform.")); return true; }
            FinishPaintingStroke();
            FinishPreviewTransform();
            healingDocument = compositor;
            healingLayer = layer;
            healingTransform = transform;
            healingCanvasSize = new Vector2Int(compositor.width, compositor.height);
            healingSelectionRevision = GetAreaSelection().Revision;
            try { BeginHealingStroke(HealingCanvasPoint(evt.localPosition)); }
            catch (Exception e) { CancelHealing(); ShowNotification(new GUIContent("Healing: " + e.Message)); return true; }
            healingPointer = evt.pointerId;
            Focus(); toolkitPreviewCanvas.Focus();
            toolkitPreviewCanvas.CapturePointer(evt.pointerId);
            healingOverlay?.MarkDirtyRepaint();
            RefreshHealingStatus();
            return true;
        }

        private Vector2 HealingCanvasPoint(Vector2 position)
        {
            var rect = toolkitPreviewCanvas.ImageRect;
            position = toolkitPreviewCanvas.ToCanvas(position);
            return new Vector2((position.x - rect.x) / rect.width * compositor.width,
                (1 - (position.y - rect.y) / rect.height) * compositor.height);
        }

        private void BeginHealingStroke(Vector2 point)
        {
            healingStroke?.Dispose();
            healingTransform.TryInverse(out var inverse);
            healingStroke = new DrawingLayerBehaviour.HealingStrokeBuffer(healingCanvasSize.x, healingCanvasSize.y,
                Mathf.Clamp(paintSettings.healingSize, 1, 512), Mathf.Clamp01(paintSettings.healingHardness),
                tiledPreview, inverse, GetAreaSelectionTexture());
            healingStroke.Add(point);
        }

        private bool HandleHealingMove(PointerMoveEvent evt)
        {
            if (healingPointer != evt.pointerId) return false;
            if ((evt.pressedButtons & 1) == 0) { WhimTexUI.ConsumeEvent(evt); return true; }
            AddHealingPoint(evt.localPosition);
            UpdatePreviewCursor(evt.localPosition, evt.altKey);
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private void AddHealingPoint(Vector2 position)
        {
            try { healingStroke.Add(HealingCanvasPoint(position)); }
            catch (Exception e) { CancelHealing(); ShowNotification(new GUIContent("Healing: " + e.Message)); }
            healingOverlay?.MarkDirtyRepaint();
        }

        private bool HandleHealingUp(PointerUpEvent evt)
        {
            if (healingPointer != evt.pointerId || evt.button != 0) return false;
            AddHealingPoint(evt.localPosition);
            int pointer = healingPointer;
            healingPointer = -1;
            if (pointer >= 0 && toolkitPreviewCanvas.HasPointerCapture(pointer)) toolkitPreviewCanvas.ReleasePointer(pointer);
            if (healingLayer != null) StartHealing();
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private bool HealingContextValid => healingDocument != null && healingDocument == compositor &&
            previewTool == PreviewTool.HealingBrush && healingLayer != null &&
            ReferenceEquals(GetSelectedLayer()?.Behaviour, healingLayer) &&
            compositor.width == healingCanvasSize.x && compositor.height == healingCanvasSize.y &&
            GetAreaSelection().Revision == healingSelectionRevision &&
            healingStroke != null && healingStroke.tiled == tiledPreview &&
            compositor.GetCanvasTransform(healingLayer).ToMatrix(compositor.width, compositor.height).Equals(healingTransform) &&
            !WhimTexApi.IsLayerContentLocked(compositor, healingLayer);

        private void StartHealing()
        {
            RenderTexture rendered = null;
            Texture2D readback = null;
            RenderTexture cropped = null;
            var previous = RenderTexture.active;
            try
            {
                if (!HealingContextValid) { CancelHealing(); return; }
                int width = compositor.width, height = compositor.height;
                RectInt bounds = healingStroke.Bounds(Mathf.Clamp(paintSettings.healingSearch, 8, 512));
                if (bounds.width == 0 || bounds.height == 0) throw new InvalidOperationException("Draw inside the canvas.");
                readback = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBAFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
                cropped = RenderTexture.GetTemporary(bounds.width, bounds.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Color[] ReadRegion(RenderTexture source)
                {
                    var wrap = source.wrapMode; var filter = source.filterMode;
                    try
                    {
                        source.wrapMode = tiledPreview ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                        source.filterMode = FilterMode.Point;
                        Graphics.Blit(source, cropped, new Vector2(bounds.width / (float)width, bounds.height / (float)height),
                            new Vector2(bounds.x / (float)width, bounds.y / (float)height));
                        RenderTexture.active = cropped;
                        readback.ReadPixels(new Rect(0, 0, bounds.width, bounds.height), 0, 0, false);
                        return readback.GetPixels();
                    }
                    finally { source.wrapMode = wrap; source.filterMode = filter; }
                }
                var raster = ReadRegion(healingStroke.Texture);
                var coverage = new byte[raster.Length];
                int targets = 0;
                for (int i = 0; i < coverage.Length; i++)
                {
                    coverage[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(raster[i].a) * 255);
                    if (coverage[i] != 0) targets++;
                }
                if (targets == 0) throw new InvalidOperationException("The stroke does not cover editable pixels. Check the selection and layer frame.");
                if (tiledPreview) bounds = HealingBrushUtility.RecenterTiledRegion(bounds, ref coverage, width, height);
                rendered = paintSettings.healingSample == HealingSampleMode.CurrentLayer
                    ? healingLayer.Render(new LayerRenderContext(compositor, null, width, height, 1, applyModifiers: false))
                    : compositor.RenderLayerAndBelow(healingLayer, width, height);
                if (rendered == null) throw new InvalidOperationException("No source image available.");
                var pixels = ReadRegion(rendered);
                var job = new HealingJob { bounds = bounds, tiled = tiledPreview };
                bool empty = paintSettings.healingTransparentOnly;
                int quality = (int)paintSettings.healingQuality;
                var token = job.cancellation.Token;
                healingJob = job;
                job.task = Task.Run(() => HealingBrushUtility.Heal(pixels, coverage, bounds.width, bounds.height,
                    empty, quality, 1, token, value => Volatile.Write(ref job.progress, (int)(value * 100))), token);
                healingWorker = job.task;
            }
            catch (Exception e) { CancelHealing(); ShowNotification(new GUIContent("Healing: " + e.Message)); }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null) DestroyImmediate(readback);
                if (cropped != null) RenderTexture.ReleaseTemporary(cropped);
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
                RefreshHealingStatus();
            }
        }

        private void UpdateHealing()
        {
            if (healingLayer == null) return;
            if (healingPointer >= 0 && (toolkitPreviewCanvas == null || !toolkitPreviewCanvas.HasPointerCapture(healingPointer)))
            { CancelHealing(); return; }
            healingOverlay?.MarkDirtyRepaint();
            if (!HealingContextValid) { CancelHealing(); return; }
            if (healingJob == null) return;
            var job = healingJob;
            RefreshHealingStatus();
            if (!job.task.IsCompleted) return;
            try
            {
                var result = job.task.GetAwaiter().GetResult();
                if (!job.cancellation.IsCancellationRequested && HealingContextValid)
                {
                    // Clear pending state before MarkChanged/Undo notifications from our own commit.
                    var layer = healingLayer;
                    var transform = healingTransform;
                    CancelHealing();
                    CommitHealing(layer, transform, job.bounds, result, job.tiled);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { ShowNotification(new GUIContent("Healing: " + e.Message)); }
            finally { if (healingJob == job) CancelHealing(); }
        }

        private void CommitHealing(DrawingLayerBehaviour layer, ProjectiveMatrix transform, RectInt bounds, ContentAwareFill.Result result, bool tiled)
        {
            Texture2D patch = null, mask = null;
            int group = -1;
            try
            {
                patch = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBAHalf, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                patch.SetPixels(result.pixels); patch.Apply(false, false);
                mask = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var bytes = new Color32[result.target.Length];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = new Color32(result.target[i], 0, 0, 255);
                mask.SetPixels32(bytes); mask.Apply(false, false);
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Healing Brush");
                Undo.RegisterCompleteObjectUndo(compositor, "Healing Brush");
                bool hadPixels = layer.StoredTexture != null;
                layer.PrepareStroke(compositor.width, compositor.height, "Healing Brush");
                if (!hadPixels) Undo.RegisterCreatedObjectUndo(layer.StoredTexture, "Healing Brush");
                layer.ApplyHealingPatch(patch, mask, bounds, compositor.width, compositor.height, transform, tiled);
                compositor.MarkChanged();
                temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
                effectInteractiveUntil = 0;
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                RequestPreview(true);
            }
            catch
            {
                if (group >= 0)
                {
                    Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group);
                    compositor.InvalidateDrawingLayerSurfaces(); RequestPreview(true);
                }
                throw;
            }
            finally
            {
                if (patch != null) DestroyImmediate(patch);
                if (mask != null) DestroyImmediate(mask);
                if (group >= 0) Undo.IncrementCurrentGroup();
            }
        }

        private void CancelHealing()
        {
            var job = healingJob;
            healingJob = null;
            healingLayer = null; healingDocument = null;
            healingStroke?.Dispose(); healingStroke = null;
            int pointer = healingPointer; healingPointer = -1;
            if (pointer >= 0 && toolkitPreviewCanvas != null && toolkitPreviewCanvas.HasPointerCapture(pointer))
                toolkitPreviewCanvas.ReleasePointer(pointer);
            if (job != null)
            {
                job.cancellation.Cancel();
                if (job.task != null) job.task.ContinueWith(t =>
                {
                    _ = t.Exception;
                    job.cancellation.Dispose();
                    Interlocked.CompareExchange(ref healingWorker, null, t);
                }, TaskScheduler.Default);
                else job.cancellation.Dispose();
            }
            healingOverlay?.MarkDirtyRepaint();
            RefreshHealingStatus();
        }

        private void RefreshHealingStatus()
        {
            if (healingStatus != null) healingStatus.text = healingJob != null
                ? $"Healing {Volatile.Read(ref healingJob.progress)}%" : healingPointer >= 0 ? "Release to heal" : "";
            healingCancel?.SetEnabled(healingLayer != null);
        }

        private void DrawHealingOverlay(MeshGenerationContext context)
        {
            if (healingStroke?.Texture == null || compositor != healingDocument || toolkitPreviewCanvas == null) return;
            Rect image = toolkitPreviewCanvas.ImageRect, viewport = healingOverlay.contentRect;
            if (image.width <= 0 || image.height <= 0 || viewport.width <= 0 || viewport.height <= 0) return;
            Vector3 Position(float x, float y)
            {
                Vector2 point = healingStroke.tiled ? new Vector2(x, y) : toolkitPreviewCanvas.ToView(new Vector2(x, y));
                return new Vector3(point.x, point.y, Vertex.nearZ);
            }
            Vector2 Uv(Vector2 position)
            {
                if (healingStroke.tiled) position = toolkitPreviewCanvas.ToCanvas(position);
                return new Vector2((position.x - image.x) / image.width, 1 - (position.y - image.y) / image.height);
            }
            var rect = healingStroke.tiled ? viewport : image;
            Vector2 origin = Uv(rect.min);
            Vector2 offset = healingStroke.tiled ? new Vector2(Mathf.Floor(origin.x), Mathf.Floor(origin.y)) : Vector2.zero;
            var tint = new Color(.2f, .75f, 1f, .4f);
            context.AllocateTempMesh(4, 6, out var vertices, out var indices);
            vertices[0] = new Vertex { position = Position(rect.xMin, rect.yMin), tint = tint, uv = origin - offset };
            vertices[1] = new Vertex { position = Position(rect.xMax, rect.yMin), tint = tint, uv = Uv(new Vector2(rect.xMax, rect.yMin)) - offset };
            vertices[2] = new Vertex { position = Position(rect.xMax, rect.yMax), tint = tint, uv = Uv(rect.max) - offset };
            vertices[3] = new Vertex { position = Position(rect.xMin, rect.yMax), tint = tint, uv = Uv(new Vector2(rect.xMin, rect.yMax)) - offset };
            indices[0] = 0; indices[1] = 1; indices[2] = 2; indices[3] = 0; indices[4] = 2; indices[5] = 3;
#if UNITY_6000_3_OR_NEWER
            context.DrawMesh(vertices, indices, healingStroke.Texture, TextureOptions.SkipDynamicAtlas);
#else
            context.DrawMesh(vertices, indices, healingStroke.Texture);
#endif
        }
    }
}
