using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private enum QuickSelectionMode { Add, Subtract }
        private enum QuickSelectionSource { VisibleLayers, CurrentLayer }
        [SerializeField] private float quickSelectionSize = 64;
        [SerializeField] private float quickSelectionStrength = 1;
        [SerializeField] private float quickSelectionTolerance = .18f;
        [SerializeField] private QuickSelectionMode quickSelectionMode;
        [SerializeField] private QuickSelectionSource quickSelectionSource;
        private Button canvasQuickSelectionButton;
        private Label quickSelectionStatus;
        private QuickSelectionManipulator quickSelectionManipulator;
        private WhimTexDocument quickSelectionDocument;
        private Layer quickSelectionLayer;
        private CanvasSelection quickSelectionTarget;
        private int quickSelectionRevision, quickSelectionGeneration;
        private bool quickSelectionTiled;
        private readonly Queue<QuickSelectionStroke> quickSelectionStrokes = new Queue<QuickSelectionStroke>();
        private QuickSelectionStroke quickSelectionStroke;
        private QuickSelection quickSelection;
        private QuickSelection quickSelectionCompleted;
        private CancellationTokenSource quickSelectionCancellation;
        private Task quickSelectionInitialize;
        private Task<QuickSelection.Result> quickSelectionTask;
        private Task quickSelectionRetirement;
        private readonly List<QuickSelectionReadback> quickSelectionReadbacks = new List<QuickSelectionReadback>();
        private QuickSelection.Request quickSelectionRequest;
        private bool quickSelectionReadback;
        private Color[] quickSelectionPixels;
        private EffectRenderCache quickSelectionEffects;
        private RenderTexture quickSelectionMaskTexture;
        private Texture2D quickSelectionUpload;
        private int quickSelectionMaskRevision = -1;
        private Vector4[] quickSelectionPreviewEdges;
        private int quickSelectionPreviewRevision=-1;
        private double quickSelectionLastInput;
        private const double QuickSelectionPauseSeconds=.18;
        private CanvasTool? quickSelectionNextTool;
        private Action quickSelectionAfterRefine;
        private bool HasPendingQuickSelection=>quickSelection!=null||quickSelectionStrokes.Count!=0;
        private void NotifyQuickSelectionInput()
        {quickSelectionLastInput=EditorApplication.timeSinceStartup;quickSelection?.Interrupt();}
        private bool DeferUntilQuickSelectionReady(Action action)
        {
            if(!HasPendingQuickSelection)return false;
            quickSelectionAfterRefine=action;quickSelectionManipulator?.End();
            if(quickSelectionStroke!=null)quickSelectionStroke.ended=true;
            foreach(var stroke in quickSelectionStrokes)stroke.ended=true;
            return true;
        }
        private sealed class QuickSelectionReadback
        {
            internal AsyncGPUReadbackRequest request;
            internal RenderTexture texture;
        }

        private sealed class QuickSelectionStroke
        {
            internal bool subtract, ended, tiled;
            internal float radius, tolerance, strength;
            internal readonly Queue<Vector2> points = new Queue<Vector2>();
            internal Vector2 last;
            internal bool started;
        }

        private void AddQuickSelectionSettings()
        {
            var row = CreateCanvasSettingsRow(); BindCanvasSettingsRow(row, CanvasTool.QuickSelection);
            var mode = new EnumField(quickSelectionMode) { tooltip = "Keep foreground/background brush hints. The latest stroke replaces hints under the brush. Right mouse or Alt temporarily subtracts; Deselect clears hints." };
            TwoChoiceDropdown.Attach(mode); toolkitHeaderBindings.Track(mode, () => (Enum)quickSelectionMode);
            mode.RegisterValueChangedCallback(e => quickSelectionMode = (QuickSelectionMode)e.newValue); row.Add(mode);
            var size = new FloatField("Size") { value = quickSelectionSize };
            size.style.width = 130; toolkitHeaderBindings.Track(size, () => quickSelectionSize);
            size.RegisterValueChangedCallback(e => quickSelectionSize = ShapeLayerBehaviour.Limit(e.newValue, 1, 2048, 64)); row.Add(size);
            var strength = new Slider("Strength", 0, 100) { value = quickSelectionStrength * 100, showInputField = true,
                tooltip = "100% is a fixed Add/Subtract hint. Lower values can yield to opposing hints. 0% clears hints under the brush; it does not directly erase the selection." };
            strength.style.width = 190; toolkitHeaderBindings.Track(strength, () => quickSelectionStrength * 100);
            strength.RegisterValueChangedCallback(e => quickSelectionStrength = ShapeLayerBehaviour.Limit(e.newValue / 100, 0, 1, 1)); row.Add(strength);
            var tolerance = new Slider("Tolerance", .01f, .6f) { value = quickSelectionTolerance, showInputField = true,
                tooltip = "Higher values include larger colour differences. Lower values stop at subtler boundaries." };
            tolerance.style.width = 230;
            toolkitHeaderBindings.Track(tolerance, () => quickSelectionTolerance);
            tolerance.RegisterValueChangedCallback(e =>
            {
                float value=ShapeLayerBehaviour.Limit(e.newValue,.01f,.6f,.18f);
                if(value!=quickSelectionTolerance)CancelQuickSelection();
                quickSelectionTolerance=value;
            }); row.Add(tolerance);
            var source = new EnumField(quickSelectionSource) { tooltip = "Find boundaries in all visible layers or the selected layer, including its effects." };
            TwoChoiceDropdown.Attach(source); toolkitHeaderBindings.Track(source, () => (Enum)quickSelectionSource);
            source.RegisterValueChangedCallback(e => { ResetQuickSelection(); quickSelectionSource = (QuickSelectionSource)e.newValue; }); row.Add(source);
            row.Add(WhimTexUI.CreateButton("Deselect", () => ChangeAreaSelection(s => s.Clear())));
            var cancel = WhimTexUI.CreateButton("Stop", CancelQuickSelection);
            cancel.tooltip = "Stop refinement and pending actions; discard the preview and keep committed selection. Esc also stops.";
            toolkitHeaderBindings.Add(() => cancel.SetEnabled(quickSelection != null || quickSelectionStrokes.Count != 0)); row.Add(cancel);
            quickSelectionStatus = new Label(); quickSelectionStatus.AddToClassList("whimtex-area-status"); row.Add(quickSelectionStatus);
            toolkitCanvasViewHeader.Add(row);
        }

        private QuickSelectionStroke BeginQuickSelection(bool subtract, Vector2 point)
        {
            if (activeDocument == null || !HasCanvasLayers) return null;
            var target = GetAreaSelection(); target.ValidateSize();
            Layer source = quickSelectionSource == QuickSelectionSource.CurrentLayer ? GetSelectedLayer() : null;
            if (quickSelectionSource == QuickSelectionSource.CurrentLayer && source == null) return null;
            if (quickSelectionDocument != activeDocument || quickSelectionTarget != target || quickSelectionLayer != source ||
                quickSelectionRevision != target.Revision || quickSelectionTiled != tiledCanvas) ResetQuickSelection();
            quickSelectionDocument = activeDocument; quickSelectionTarget = target; quickSelectionLayer = source;
            quickSelectionRevision = target.Revision;
            quickSelectionTiled = tiledCanvas;
            var stroke = new QuickSelectionStroke { subtract = subtract, radius = quickSelectionSize * .5f,
                tolerance = quickSelectionTolerance, strength = quickSelectionStrength, tiled = tiledCanvas };
            stroke.points.Enqueue(point); quickSelectionStrokes.Enqueue(stroke);
            NotifyQuickSelectionInput();
            EditorApplication.update -= UpdateQuickSelection; EditorApplication.update += UpdateQuickSelection;
            return stroke;
        }

        private bool QuickSelectionContextValid => quickSelectionDocument != null && quickSelectionDocument == activeDocument &&
            canvasTool == CanvasTool.QuickSelection && quickSelectionTarget == areaSelection &&
            quickSelectionRevision == areaSelection.Revision && activeDocument.width == areaSelection.Width &&
            activeDocument.height == areaSelection.Height && quickSelectionTiled == tiledCanvas &&
            (quickSelectionSource != QuickSelectionSource.CurrentLayer || ReferenceEquals(quickSelectionLayer, GetSelectedLayer()));

        private void UpdateQuickSelection()
        {
            try
            {
                if (!QuickSelectionContextValid) { ResetQuickSelection(); return; }
                if (quickSelection == null)
                {
                    if (quickSelectionStrokes.Count == 0) { EditorApplication.update -= UpdateQuickSelection; RefreshQuickSelectionStatus(); return; }
                    quickSelectionStroke = quickSelectionStrokes.Dequeue();
                    quickSelection = new QuickSelection(areaSelection.Width, areaSelection.Height,
                        quickSelectionStroke.tolerance, quickSelectionStroke.subtract);
                    quickSelection.Wrap = quickSelectionStroke.tiled;
                    quickSelection.Progressive=true;
                    quickSelectionPreviewRevision=-1;
                    quickSelection.ReuseLayout(quickSelectionCompleted); quickSelectionCompleted = null;
                    quickSelectionCancellation = new CancellationTokenSource();
                    var engine = quickSelection; var existing = areaSelection.Coverage; var token = quickSelectionCancellation.Token;
                    var retirement=quickSelectionRetirement;
                    quickSelectionInitialize = Task.Run(async () =>
                    { if(retirement!=null)await retirement; token.ThrowIfCancellationRequested(); engine.Initialize(existing, token); }, token);
                }
                if (quickSelectionInitialize != null)
                {
                    if (!quickSelectionInitialize.IsCompleted) { RefreshQuickSelectionStatus(); return; }
                    quickSelectionInitialize.GetAwaiter().GetResult(); quickSelectionInitialize = null;
                }
                // Bound work done by an input/update callback even after a large pointer jump.
                for (int n = 0; n < 8; n++)
                {
                    if(quickSelectionStroke.points.Count==0)
                    {
                        if(!quickSelectionStroke.ended || quickSelectionStrokes.Count==0)break;
                        quickSelectionStroke=quickSelectionStrokes.Dequeue();
                        if(quickSelectionStroke.points.Count==0)continue;
                    }
                    Vector2 next = quickSelectionStroke.points.Peek();
                    float spacing = Mathf.Max(1, quickSelectionStroke.radius * .5f);
                    if (!quickSelectionStroke.started)
                    { quickSelectionStroke.last = next; quickSelectionStroke.started = true; quickSelectionStroke.points.Dequeue(); }
                    else
                    {
                        Vector2 delta = next - quickSelectionStroke.last;
                        if (delta.magnitude <= spacing) { quickSelectionStroke.last = next; quickSelectionStroke.points.Dequeue(); }
                        else quickSelectionStroke.last += delta.normalized * spacing;
                    }
                    quickSelection.AddWeightedBrush(quickSelectionStroke.last, quickSelectionStroke.radius,
                        quickSelectionStroke.subtract,quickSelectionStroke.tiled,quickSelectionStroke.strength);
                }
                quickSelection.RefinementAllowed=quickSelectionStroke.points.Count==0&&quickSelectionStrokes.Count==0&&
                    (quickSelectionStroke.ended||EditorApplication.timeSinceStartup-quickSelectionLastInput>=QuickSelectionPauseSeconds);
                if (quickSelectionTask != null)
                {
                    if (!quickSelectionTask.IsCompleted) { RefreshQuickSelectionStatus(); return; }
                    var result = quickSelectionTask.GetAwaiter().GetResult(); quickSelectionTask = null;
                    if(result.preview&&result.revision>=quickSelectionPreviewRevision)
                    {
                        quickSelectionPreviewRevision=result.revision;
                        quickSelectionPreviewEdges=result.edges;
                        areaSelectionOverlay?.MarkDirtyRepaint();
                    }
                    if(quickSelection.IsCurrent(result))
                    {
                        using (WhimTexProfiler.Measure("selection.quick.publish", activeDocument))
                        {
                            bool emptyStorage = areaSelection.Coverage == null;
                            int beforeRevision = areaSelection.Revision;
                            areaSelection.ApplyQuickSelection(result, quickSelection.InitialCoverage);
                            quickSelectionRevision = areaSelection.Revision;
                            if (result.changed)
                            {
                                UploadQuickSelectionTile(result, beforeRevision, emptyStorage);
                                areaSelectionOverlay?.SetQuickSelectionEdges(result.edges);
                            }
                        }
                    }
                    quickSelection.Accept(result); quickSelectionPixels = null; quickSelectionRequest = null;
                    toolkitHeaderBindings.Refresh();
                }
                if (quickSelectionReadback) { RefreshQuickSelectionStatus(); return; }
                if (quickSelectionRequest != null && quickSelectionPixels != null)
                {
                    RunQuickSelectionRequest();
                    RefreshQuickSelectionStatus(); return;
                }
                if (quickSelectionRequest == null)
                {
                    quickSelectionRequest = quickSelection.Take();
                    if (quickSelectionRequest != null)
                    {
                        CaptureQuickSelectionTile();
                        if(!quickSelectionReadback&&quickSelectionPixels!=null)RunQuickSelectionRequest();
                    }
                    else if(quickSelection.IsRefined&&quickSelectionPreviewEdges!=null)
                    {quickSelectionPreviewEdges=null;areaSelectionOverlay?.MarkDirtyRepaint();}
                    if (quickSelectionRequest==null&&quickSelectionStroke.ended && quickSelectionStroke.points.Count == 0)
                    {
                        if(!quickSelection.IsFinalized)
                        {
                            var engine=quickSelection;var token=quickSelectionCancellation.Token;
                            quickSelectionInitialize=Task.Run(()=>engine.FinishInput(token),token);
                            return;
                        }
                        quickSelectionCompleted = quickSelection;
                        quickSelection.Dispose();
                        quickSelection = null; quickSelectionStroke = null;
                        quickSelectionCancellation.Dispose(); quickSelectionCancellation = null;
                        quickSelectionPreviewEdges=null;areaSelectionOverlay?.MarkDirtyRepaint();
                        var nextTool=quickSelectionNextTool;var after=quickSelectionAfterRefine;
                        quickSelectionNextTool=null;quickSelectionAfterRefine=null;
                        if(nextTool.HasValue)SetCanvasTool(nextTool.Value);
                        after?.Invoke();
                    }
                }
                RefreshQuickSelectionStatus();
            }
            catch (OperationCanceledException) { CancelQuickSelection(); }
            catch (Exception e) { ResetQuickSelection(); ShowNotification(new GUIContent("Quick Selection: " + e.Message)); }
        }

        private void RunQuickSelectionRequest()
        {
            var engine=quickSelection;var request=quickSelectionRequest;var pixels=quickSelectionPixels;
            var existing=areaSelection.Coverage;var token=quickSelectionCancellation.Token;
            quickSelectionTask=Task.Run(()=>engine.GrowRaw(request,pixels,existing,token),token);
        }

        private void CaptureQuickSelectionTile()
        {
            if(quickSelectionRequest.cleanup) { quickSelectionPixels=Array.Empty<Color>(); return; }
            RenderTexture rendered = null;
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                using var profile = WhimTexProfiler.Measure("selection.quick.capture", activeDocument);
                quickSelectionEffects ??= new EffectRenderCache { BudgetBytes = 32L * 1024 * 1024 };
                rendered = activeDocument.RenderSelectionRegion(new CanvasRenderRegion(quickSelectionRequest.source, quickSelectionRequest.step),
                    quickSelectionEffects, quickSelectionLayer);
                int generation = quickSelectionGeneration;
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    quickSelectionReadback = true;
                    var owned = new QuickSelectionReadback { texture = rendered };
                    quickSelectionReadbacks.Add(owned);
                    try { owned.request = AsyncGPUReadback.Request(rendered, 0, TextureFormat.RGBAFloat, request =>
                    {
                        try
                        {
                            if (this == null || generation != quickSelectionGeneration) return;
                            if (request.hasError) { CancelQuickSelection(); ShowNotification(new GUIContent("Quick Selection: GPU readback failed. Please retry.")); return; }
                            using var copy = WhimTexProfiler.Measure("selection.quick.readback", activeDocument);
                            var data = request.GetData<Color>();
                            quickSelectionPixels = data.ToArray(); quickSelectionReadback = false;
                            RunQuickSelectionRequest();
                        }
                        finally { ReleaseQuickSelectionReadback(owned); }
                    });
                    }
                    catch { quickSelectionReadbacks.Remove(owned); owned.texture = null; throw; }
                    owned.request.forcePlayerLoopUpdate = true;
                    GL.Flush();
                    rendered = null;
                }
                else
                {
                    // At most 528 squared for compact contours, 258 squared for sparse
                    // regions. Never synchronously read the full native document.
                    var read = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBAFloat, false, true)
                        { hideFlags = HideFlags.HideAndDontSave };
                    try { RenderTexture.active = rendered; read.ReadPixels(new Rect(0,0,rendered.width,rendered.height),0,0,false); quickSelectionPixels = read.GetPixels(); }
                    finally { DestroyImmediate(read); }
                }
            }
            finally { if (rendered != null) ProfiledRenderTexture.ReleaseTemporary(rendered); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        private void RefreshQuickSelectionStatus()
        {
            if(quickSelection!=null)
            {
                // Keep asynchronous source transfers moving after the pointer stops.
                // Otherwise the idle editor can add a long tick delay to every region.
                EditorApplication.QueuePlayerLoopUpdate();Repaint();
            }
            if (quickSelectionStatus != null) quickSelectionStatus.text = quickSelection != null
                ? (quickSelection.IsRefined?"Ready":quickSelectionPreviewEdges==null?"Preview…":quickSelection.RefinementAllowed?"Refining…":"Preview · refining after pause")+" · Esc to stop"
                : quickSelectionStrokes.Count > 0 ? "Queued…" : "";
        }

        private void UploadQuickSelectionTile(QuickSelection.Result result, int beforeRevision, bool emptyStorage)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.R8) ||
                (SystemInfo.copyTextureSupport & CopyTextureSupport.TextureToRT) == 0) return;
            if (!emptyStorage && (quickSelectionMaskTexture == null || quickSelectionMaskRevision != beforeRevision)) return;
            if (!emptyStorage && !quickSelectionMaskTexture.IsCreated()) return;
            using var profile = WhimTexProfiler.Measure("selection.quick.upload", activeDocument);
            if (quickSelectionMaskTexture == null || !quickSelectionMaskTexture.IsCreated())
            {
                ReleaseQuickSelectionMask();
                quickSelectionMaskTexture = new RenderTexture(areaSelection.Width, areaSelection.Height, 0,
                    RenderTextureFormat.R8, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave,
                    name = "WhimTex Quick Selection", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var previous = RenderTexture.active;
                try { quickSelectionMaskTexture.Create(); RenderTexture.active = quickSelectionMaskTexture; GL.Clear(false, true, Color.clear); }
                finally { RenderTexture.active = previous; }
            }
            RectInt r = result.region;
            if (quickSelectionUpload == null || quickSelectionUpload.width != r.width || quickSelectionUpload.height != r.height)
            {
                if (quickSelectionUpload != null) DestroyImmediate(quickSelectionUpload);
                quickSelectionUpload = new Texture2D(r.width, r.height, TextureFormat.R8, false, true)
                    { hideFlags = HideFlags.HideAndDontSave, name = "WhimTex selection tile" };
            }
            quickSelectionUpload.LoadRawTextureData(result.coverage); quickSelectionUpload.Apply(false, false);
            Graphics.CopyTexture(quickSelectionUpload, 0, 0, 0, 0, r.width, r.height, quickSelectionMaskTexture, 0, 0, r.x, r.y);
            quickSelectionMaskRevision = areaSelection.Revision;
        }
        private void ReleaseQuickSelectionMask()
        {
            if (quickSelectionMaskTexture != null) { quickSelectionMaskTexture.Release(); DestroyImmediate(quickSelectionMaskTexture); }
            if (quickSelectionUpload != null) DestroyImmediate(quickSelectionUpload);
            quickSelectionMaskTexture = null; quickSelectionUpload = null; quickSelectionMaskRevision = -1;
        }
        private void ReleaseQuickSelectionReadback(QuickSelectionReadback operation)
        {
            if (operation.texture != null) ProfiledRenderTexture.ReleaseTemporary(operation.texture);
            operation.texture = null; quickSelectionReadbacks.Remove(operation);
        }
        private void DrainQuickSelection()
        {
            CancelQuickSelection();
            // Only window teardown/reload drains owned work. Pointer input and Stop never wait.
            try { quickSelectionRetirement?.GetAwaiter().GetResult(); }
            catch (Exception e) { Debug.LogException(e); }
            quickSelectionRetirement = null;
            foreach (var operation in quickSelectionReadbacks.ToArray())
            {
                if (operation.texture == null) continue;
                operation.request.WaitForCompletion();
                ReleaseQuickSelectionReadback(operation);
            }
        }

        private void CancelQuickSelection()
        {
            quickSelectionPreviewEdges=null;quickSelectionNextTool=null;quickSelectionAfterRefine=null;
            areaSelectionOverlay?.MarkDirtyRepaint();
            quickSelectionGeneration++;
            quickSelectionManipulator?.End();
            EditorApplication.update -= UpdateQuickSelection;
            quickSelectionCancellation?.Cancel(); quickSelectionCancellation?.Dispose(); quickSelectionCancellation = null;
            var owned = quickSelection;
            Task running = (Task)quickSelectionTask ?? quickSelectionInitialize;
            if (owned != null)
            {
                if (running != null && !running.IsCompleted)
                {
                    var disposal = running.ContinueWith(_ => owned.Dispose(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    quickSelectionRetirement = quickSelectionRetirement == null || quickSelectionRetirement.IsCompleted
                        ? disposal : Task.WhenAll(quickSelectionRetirement, disposal);
                    ObserveQuickSelectionTask(quickSelectionRetirement);
                }
                else owned.Dispose();
            }
            if(owned!=null)quickSelectionCompleted=owned;
            ObserveQuickSelectionTask(quickSelectionTask); ObserveQuickSelectionTask(quickSelectionInitialize);
            quickSelectionTask = null; quickSelectionInitialize = null;
            quickSelectionStrokes.Clear(); quickSelectionStroke = null; quickSelection = null;
            quickSelectionRequest = null; quickSelectionPixels = null; quickSelectionReadback = false;
            quickSelectionEffects?.Dispose(); quickSelectionEffects = null;
            RefreshQuickSelectionStatus();
        }
        private void ResetQuickSelection()
        {
            CancelQuickSelection();
            quickSelectionCompleted=null;
            quickSelectionDocument=null;quickSelectionLayer=null;quickSelectionTarget=null;
        }
        private static void ObserveQuickSelectionTask(Task task)
        {
            if (task == null) return;
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private sealed class QuickSelectionManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private readonly VisualElement overlay;
            private int pointer = -1, button = -1;
            private Vector2 hover;
            private bool inside, alt;
            private QuickSelectionStroke stroke;
            internal QuickSelectionManipulator(WhimTexWindow owner)
            {
                this.owner = owner;
                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.AddToClassList("whimtex-area-overlay"); overlay.generateVisualContent += Draw;
                owner.toolkitCanvas.Add(overlay);
                owner.toolkitCanvas.ViewChanged += overlay.MarkDirtyRepaint;
            }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Cancel);
                target.RegisterCallback<PointerLeaveEvent>(Leave);
                target.RegisterCallback<DetachFromPanelEvent>(Detach);
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                target.RegisterCallback<KeyUpEvent>(KeyUp);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                End();
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Cancel);
                target.UnregisterCallback<PointerLeaveEvent>(Leave);
                target.UnregisterCallback<DetachFromPanelEvent>(Detach);
                target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                target.UnregisterCallback<KeyUpEvent>(KeyUp);
                owner.toolkitCanvas.ViewChanged -= overlay.MarkDirtyRepaint; overlay.RemoveFromHierarchy();
            }
            private Vector2 Point(Vector2 local)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                Vector2 p = owner.toolkitCanvas.ToCanvas(local);
                Vector2 pixel = new Vector2((p.x-image.x)/image.width*owner.activeDocument.width,
                    (image.yMax-p.y)/image.height*owner.activeDocument.height);
                return pixel;
            }
            private void Down(PointerDownEvent e)
            {
                if (owner.canvasTool != CanvasTool.QuickSelection || owner.activeDocument == null ||
                    (e.button != 0 && e.button != 1) || pointer >= 0) return;
                Vector2 p = Point(e.localPosition);
                if (!owner.tiledCanvas && (p.x<0 || p.y<0 || p.x>=owner.activeDocument.width || p.y>=owner.activeDocument.height)) return;
                try { stroke = owner.BeginQuickSelection(e.button == 1 || e.altKey || owner.quickSelectionMode == QuickSelectionMode.Subtract, p); }
                catch(Exception ex) { owner.ShowNotification(new GUIContent(ex.Message)); }
                if (stroke == null) return;
                pointer=e.pointerId; button=e.button; hover=e.localPosition; inside=true; alt=e.altKey;
                target.CapturePointer(pointer); target.Focus(); overlay.MarkDirtyRepaint(); WhimTexUI.ConsumeEvent(e);
            }
            private void Move(PointerMoveEvent e)
            {
                hover=e.localPosition; inside=true; alt=e.altKey; overlay.MarkDirtyRepaint();
                if(pointer!=e.pointerId || stroke==null)return;
                if((e.pressedButtons & (1 << button))==0) { End(); WhimTexUI.ConsumeEvent(e); return; }
                stroke.points.Enqueue(Point(e.localPosition));owner.NotifyQuickSelectionInput();WhimTexUI.ConsumeEvent(e);
            }
            private void Up(PointerUpEvent e)
            { if(pointer!=e.pointerId || e.button!=button)return; stroke?.points.Enqueue(Point(e.localPosition)); End(); WhimTexUI.ConsumeEvent(e); }
            internal void End()
            {
                if(stroke!=null)stroke.ended=true;
                stroke=null; int id=pointer;pointer=-1;button=-1;
                if(id>=0 && target?.HasPointerCapture(id)==true)target.ReleasePointer(id);
                overlay.MarkDirtyRepaint();
            }
            private void Lost(PointerCaptureOutEvent e) { if(pointer==e.pointerId)End(); }
            private void Cancel(PointerCancelEvent e) => End();
            private void Leave(PointerLeaveEvent e) { inside=false;overlay.MarkDirtyRepaint(); }
            private void Detach(DetachFromPanelEvent e) => owner.CancelQuickSelection();
            private void Key(KeyDownEvent e)
            {
                if(owner.canvasTool!=CanvasTool.QuickSelection || IsTextInputTarget(e.target as VisualElement))return;
                alt=e.altKey;
                if(e.keyCode==KeyCode.Escape)owner.CancelQuickSelection();
                else if(e.keyCode==KeyCode.LeftBracket)owner.quickSelectionSize=Mathf.Max(1,owner.quickSelectionSize/1.2f);
                else if(e.keyCode==KeyCode.RightBracket)owner.quickSelectionSize=Mathf.Min(2048,owner.quickSelectionSize*1.2f);
                else { overlay.MarkDirtyRepaint();return; }
                owner.SaveToolPreferences();owner.toolkitHeaderBindings.Refresh();overlay.MarkDirtyRepaint();WhimTexUI.ConsumeEvent(e);
            }
            private void KeyUp(KeyUpEvent e) { alt=e.altKey;overlay.MarkDirtyRepaint(); }
            private void Draw(MeshGenerationContext context)
            {
                if(!inside || owner.canvasTool!=CanvasTool.QuickSelection || owner.activeDocument==null ||
                    owner.canvasZoomManipulator?.IsNavigating==true)return;
                var p=context.painter2D;
                float radius=owner.quickSelectionSize*.5f*owner.toolkitCanvas.PixelScale;
                for(int pass=0;pass<2;pass++)
                {
                    p.lineWidth=pass==0?3:1;p.strokeColor=pass==0?new Color(0,0,0,.8f):Color.white;
                    p.BeginPath();p.Arc(hover,Mathf.Max(2,radius),0,360);p.Stroke();
                    p.BeginPath();p.MoveTo(hover+new Vector2(-3,0));p.LineTo(hover+new Vector2(3,0));
                    if(!(stroke?.subtract ?? (alt || owner.quickSelectionMode==QuickSelectionMode.Subtract)))
                    {p.MoveTo(hover+new Vector2(0,-3));p.LineTo(hover+new Vector2(0,3));}p.Stroke();
                }
            }
        }
    }
}
