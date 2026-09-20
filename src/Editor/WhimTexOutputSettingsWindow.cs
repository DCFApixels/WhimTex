using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexOutputSettingsWindow : EditorWindow
    {
        [SerializeField] private TextureCompositor document;
        [SerializeField] private float previewHeight = 220;
        [SerializeField] private bool previewCollapsed;
        [SerializeField] private int previewChannel;
        [SerializeField] private int previewMip;
        private const float PreviewHeaderHeight = 20;
        private const float PreviewMinimumHeight = 120;
        private const float PreviewCollapseTravel = 24;
        private SerializedObject data;
        private Button applyButton;
        private Button revertButton;
        private Label previewStatus;
        private PopupField<int> mipField;
        private WhimTexOutputPreview previewRenderer;
        private int lastDirtyCount = int.MinValue;
        private bool settingsChanged;
        private string validationError;
        private bool wasPersistent;
        private Image outputPreview;
        private Label outputInfo;
        private Label outputInfoShadow;
        private VisualElement previewPane;
        private VisualElement previewSurface;
        private Vector2Int checkerTextureSize;
        private bool fileSizeDirty = true;
        private string savedFileSize = "Not saved";

        private void OnEnable()
        {
            EditorApplication.projectChanged += InvalidateFileSize;
            TextureCompositor.Changed += OnDocumentChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }
        private void OnDocumentChanged(TextureCompositor changed)
        {
            if (changed != document) return;
            lastDirtyCount = int.MinValue;
            RefreshApplyState();
        }
        private void OnUndoRedo() { lastDirtyCount = int.MinValue; RefreshApplyState(); }
        private void InvalidateFileSize()
        {
            fileSizeDirty = true;
            lastDirtyCount = int.MinValue;
            previewRenderer?.ReleaseTexture();
        }

        private void OnInspectorUpdate()
        {
            if (data != null && (document == null || AssetDatabase.Contains(document) != wasPersistent))
                CreateGUI();
            RefreshOutputInfo();
            RefreshApplyState();
        }

        private void RefreshApplyState()
        {
            if (applyButton == null || document == null) return;
            int dirtyCount = EditorUtility.GetDirtyCount(document);
            if (lastDirtyCount != dirtyCount)
            {
                lastDirtyCount = dirtyCount;
                settingsChanged = document.HasOutputSettingsChanges;
                validationError = document.ValidateOutputSettings(out _);
            }
            bool pending = EditorUtility.IsDirty(document) || (data != null && data.hasModifiedProperties);
            pending |= settingsChanged;
            bool legacy = WhimTexLegacyMigration.IsLegacyAsset(document);
            applyButton.SetEnabled((AssetDatabase.Contains(document) || legacy) && validationError == null);
            applyButton.EnableInClassList("whimtex-output-apply--pending", pending);
            revertButton?.SetEnabled(settingsChanged);
            if (previewStatus != null)
                previewStatus.text = document.OutputTexture == null ? "No saved output" : pending ? "Preview requires Apply" : "";
            applyButton.tooltip = legacy ? "Create a new TIFF copy; the legacy .asset remains unchanged."
                : !AssetDatabase.Contains(document) ? "Save the document in WhimTex first."
                : validationError != null ? validationError
                : pending ? "There are unsaved changes. Apply settings, rebuild the output and save the document."
                : "Rebuild the output and save the document.";
        }

        private void RefreshOutputInfo()
        {
            if (document == null || outputInfo == null || previewCollapsed) return;
            var size = document.SpriteOutputSettings.GetSize(document.width, document.height);
            var texture = document.OutputTexture;
            int mipCount = texture == null ? 1 : texture.mipmapCount;
            previewMip = Mathf.Clamp(previewMip, 0, mipCount - 1);
            if (mipField != null)
            {
                if (mipField.choices.Count != mipCount)
                {
                    var choices = new System.Collections.Generic.List<int>(mipCount);
                    for (int i = 0; i < mipCount; i++) choices.Add(i);
                    mipField.choices = choices;
                }
                mipField.SetValueWithoutNotify(previewMip);
                mipField.SetEnabled(mipCount > 1);
            }
            previewRenderer ??= new WhimTexOutputPreview();
            outputPreview.image = previewRenderer.Get(texture, previewChannel, previewMip);
            var shown = outputPreview.image;
            var textureSize = shown == null ? Vector2Int.zero : new Vector2Int(shown.width, shown.height);
            if (checkerTextureSize != textureSize)
            {
                checkerTextureSize = textureSize;
                outputPreview.parent.MarkDirtyRepaint();
            }
            if (fileSizeDirty)
            {
                fileSizeDirty = false;
                string path = AssetDatabase.GetAssetPath(document);
                try
                {
                    savedFileSize = string.IsNullOrEmpty(path) || !System.IO.File.Exists(path) ? "Not saved"
                        : EditorUtility.FormatBytes(new System.IO.FileInfo(path).Length);
                }
                catch (System.IO.IOException) { savedFileSize = "Unavailable"; }
                catch (System.UnauthorizedAccessException) { savedFileSize = "Unavailable"; }
            }
            string info = texture == null || texture.width != size.x || texture.height != size.y ? $"Next save: {size.x} × {size.y}\n" : "";
            if (texture == null) info += "No saved output yet.";
            else
            {
                var format = texture.graphicsFormat;
                uint bw = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockWidth(format);
                uint bh = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockHeight(format);
                uint bytes = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(format);
                ulong total = 0;
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    uint w = (uint)Mathf.Max(1, texture.width >> mip), h = (uint)Mathf.Max(1, texture.height >> mip);
                    total += (ulong)((w + bw - 1) / bw) * ((h + bh - 1) / bh) * bytes;
                }
                info += $"Saved: {texture.width} × {texture.height} · {texture.format} · {(texture.isDataSRGB ? "sRGB" : "Linear")}\n" +
                    $"Mip levels: {texture.mipmapCount} · GPU data ≈ {EditorUtility.FormatBytes((long)total)}" +
                    (texture.isReadable ? $" · CPU copy ≈ {EditorUtility.FormatBytes((long)total)}" : " · CPU copy: off");
                info += $"\nAsset file: {savedFileSize} (includes layers)";
                if (previewMip > 0) info += $"\nViewing mip {previewMip}: {Mathf.Max(1, texture.width >> previewMip)} × {Mathf.Max(1, texture.height >> previewMip)}";
            }
            if (outputInfo.text != info) outputInfo.text = info;
            if (outputInfoShadow.text != info) outputInfoShadow.text = info;
        }

        internal static void Open(TextureCompositor document)
        {
            if (document == null) return;
            foreach (var existing in Resources.FindObjectsOfTypeAll<WhimTexOutputSettingsWindow>())
                if (existing.document == document) { existing.Show(); existing.Focus(); return; }
            var window = CreateInstance<WhimTexOutputSettingsWindow>();
            window.document = document;
            window.titleContent = new GUIContent("Output Settings");
            window.minSize = new Vector2(420, 380);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            ReleaseView();
            if (document == null)
            {
                rootVisualElement.Add(new HelpBox("The document is no longer available. Reopen settings from WhimTex or its asset.", HelpBoxMessageType.Info));
                return;
            }
            WhimTexUI.ApplyWindowStyles(rootVisualElement);
            rootVisualElement.focusable = true;
            wasPersistent = AssetDatabase.Contains(document);
            data = new SerializedObject(document);
            document.EnsureOutputSettingsBaseline();
            var reference = new ObjectField("Document") { objectType = typeof(TextureCompositor), value = document };
            reference.SetEnabled(false);
            rootVisualElement.Add(reference);
            var split = new VisualElement();
            split.AddToClassList("whimtex-output-split");
            var settingsPane = new VisualElement();
            settingsPane.AddToClassList("whimtex-output-settings-pane");
            previewPane = new VisualElement { name = "output-preview-footer" };
            previewPane.AddToClassList("whimtex-output-preview-footer");
            split.Add(settingsPane);
            split.Add(previewPane);
            rootVisualElement.Add(split);
            split.RegisterCallback<GeometryChangedEvent>(_ => UpdatePreviewLayout());
            var scroll = new ScrollView();
            scroll.AddToClassList("whimtex-output-settings-scroll");
            scroll.Add(TextureCompositorEditor.BuildOutputSettings(data));
            settingsPane.Add(scroll);
            var previewTitle = new VisualElement { tooltip = "Drag to resize. Continue downward past the minimum height to hide; drag upward to show." };
            previewTitle.AddToClassList("whimtex-output-preview-title");
            previewTitle.Add(new Label("Preview") { pickingMode = PickingMode.Ignore });
            previewTitle.AddManipulator(new PreviewResizeManipulator(this));
            var previewGrip = new VisualElement { pickingMode = PickingMode.Ignore };
            previewGrip.AddToClassList("whimtex-output-preview-grip");
            previewTitle.Add(previewGrip);
            var channels = new PopupField<string>(new System.Collections.Generic.List<string> { "RGBA", "RGB", "Alpha" }, Mathf.Clamp(previewChannel, 0, 2));
            channels.AddToClassList("whimtex-output-preview-channel");
            channels.tooltip = "Preview channels only; does not change the saved texture.";
            channels.RegisterValueChangedCallback(_ => { previewChannel = channels.index; RefreshOutputInfo(); });
            previewTitle.Add(channels);
            mipField = new PopupField<int>(new System.Collections.Generic.List<int> { 0 }, 0,
                value => "Mip " + value, value => "Mip " + value);
            mipField.AddToClassList("whimtex-output-preview-mip");
            mipField.RegisterValueChangedCallback(evt => { previewMip = evt.newValue; RefreshOutputInfo(); });
            previewTitle.Add(mipField);
            previewPane.Add(previewTitle);
            previewSurface = new VisualElement();
            previewSurface.AddToClassList("whimtex-output-preview-surface");
            previewSurface.generateVisualContent += DrawPreviewCheckerboard;
            previewSurface.RegisterCallback<GeometryChangedEvent>(_ => previewSurface.MarkDirtyRepaint());
            previewPane.Add(previewSurface);
            outputPreview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            outputPreview.AddToClassList("whimtex-output-preview-image");
            outputInfo = new Label { name = "output-preview-info", pickingMode = PickingMode.Ignore, tooltip = "GPU/CPU: estimated pixel storage including mipmaps, excluding driver overhead. Asset file: actual saved document size, including layers; excludes .meta." };
            outputInfo.AddToClassList("whimtex-output-preview-overlay");
            outputInfoShadow = new Label { pickingMode = PickingMode.Ignore };
            outputInfoShadow.AddToClassList("whimtex-output-preview-overlay");
            outputInfoShadow.AddToClassList("whimtex-output-preview-shadow");
            previewSurface.Add(outputPreview);
            previewSurface.Add(outputInfoShadow);
            previewSurface.Add(outputInfo);
            previewStatus = new Label { pickingMode = PickingMode.Ignore };
            previewStatus.AddToClassList("whimtex-output-preview-status");
            previewSurface.Add(previewStatus);
            UpdatePreviewLayout();
            RefreshOutputInfo();
            bool legacy = WhimTexLegacyMigration.IsLegacyAsset(document);
            var note = new Label(legacy
                ? "Legacy .asset is read-only. Saving here creates a new TIFF and leaves the source unchanged."
                : AssetDatabase.Contains(document)
                ? "Apply saves the document and rebuilds its output."
                : "Save the document in WhimTex to create its output.");
            note.AddToClassList("whimtex-output-info");
            settingsPane.Add(note);
            applyButton = new Button(() =>
            {
                if (document == null) return;
                rootVisualElement.Focus();
                data.ApplyModifiedProperties();
                if (WhimTexLegacyMigration.IsLegacyAsset(document))
                {
                    TextureCompositorWindow.SaveDocumentAsTiff(document);
                    InvalidateFileSize();
                    lastDirtyCount = int.MinValue;
                    RefreshApplyState();
                    return;
                }
                if (!AssetDatabase.Contains(document))
                { ShowNotification(new GUIContent("Save the document in WhimTex first.")); return; }
                ShowNotification(new GUIContent("Only TIFF documents can be saved."));
            }) { text = WhimTexLegacyMigration.IsLegacyAsset(document) ? "Save As TIFF…" : "Apply & Save Output" };
            applyButton.AddToClassList("whimtex-output-apply");
            applyButton.SetEnabled(AssetDatabase.Contains(document) || WhimTexLegacyMigration.IsLegacyAsset(document));
            var actions = new VisualElement();
            actions.AddToClassList("whimtex-output-actions");
            revertButton = new Button(() =>
            {
                rootVisualElement.Focus();
                data.ApplyModifiedProperties();
                document.RevertOutputSettings();
                data.Update();
                CreateGUI();
            }) { text = "Revert", tooltip = "Restore the last applied output settings and filter. Layers and sprite slices are not changed." };
            revertButton.AddToClassList("whimtex-output-revert");
            actions.Add(revertButton);
            actions.Add(applyButton);
            settingsPane.Add(actions);
            rootVisualElement.Bind(data);
            scroll.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                if (document != null) document.MarkChanged();
                lastDirtyCount = int.MinValue;
                RefreshApplyState();
            });
            RefreshApplyState();
        }

        private void ResizePreview(float requestedHeight, bool startedCollapsed)
        {
            float threshold = startedCollapsed ? PreviewHeaderHeight + PreviewCollapseTravel
                : PreviewMinimumHeight - PreviewCollapseTravel;
            bool wasCollapsed = previewCollapsed;
            previewCollapsed = requestedHeight < threshold;
            if (!previewCollapsed) previewHeight = Mathf.Clamp(requestedHeight, PreviewMinimumHeight, MaximumPreviewHeight());
            UpdatePreviewLayout();
            if (wasCollapsed && !previewCollapsed) RefreshOutputInfo();
        }

        private float MaximumPreviewHeight()
        {
            float available = previewPane?.parent?.contentRect.height ?? 0;
            return Mathf.Max(PreviewMinimumHeight, available - 120);
        }

        private void UpdatePreviewLayout()
        {
            if (previewPane == null || previewSurface == null) return;
            previewPane.style.height = previewCollapsed ? PreviewHeaderHeight
                : Mathf.Clamp(previewHeight, PreviewMinimumHeight, MaximumPreviewHeight());
            previewSurface.EnableInClassList("whimtex-output-preview-surface--hidden", previewCollapsed);
            if (previewCollapsed) { outputPreview.image = null; previewRenderer?.ReleaseTexture(); }
        }

        private sealed class PreviewResizeManipulator : PointerManipulator
        {
            private readonly WhimTexOutputSettingsWindow window;
            private int pointer = -1;
            private float startY, startHeight;
            private bool startedCollapsed;

            internal PreviewResizeManipulator(WhimTexOutputSettingsWindow window) => this.window = window;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                ReleasePointer();
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            private void OnDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || pointer >= 0) return;
                if (evt.target is VisualElement hit && (hit is PopupField<int> || hit.GetFirstAncestorOfType<PopupField<int>>() != null ||
                    hit is PopupField<string> || hit.GetFirstAncestorOfType<PopupField<string>>() != null)) return;
                pointer = evt.pointerId;
                startY = evt.position.y;
                startHeight = window.previewPane.resolvedStyle.height;
                startedCollapsed = window.previewCollapsed;
                target.CapturePointer(pointer);
                evt.StopPropagation();
            }

            private void OnMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer || !target.HasPointerCapture(pointer)) return;
                window.ResizePreview(startHeight + startY - evt.position.y, startedCollapsed);
                evt.StopPropagation();
            }

            private void OnUp(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                ReleasePointer();
                evt.StopPropagation();
            }

            private void OnCaptureOut(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) pointer = -1; }
            private void OnDetach(DetachFromPanelEvent evt) => ReleasePointer();
            private void ReleasePointer()
            {
                int captured = pointer;
                pointer = -1;
                if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
            }
        }

        private void DrawPreviewCheckerboard(MeshGenerationContext context)
        {
            if (previewCollapsed) return;
            var texture = outputPreview?.image;
            Rect area = context.visualElement.contentRect;
            if (texture == null || area.width <= 0 || area.height <= 0) return;
            float scale = Mathf.Min(area.width / texture.width, area.height / texture.height);
            var size = new Vector2(texture.width * scale, texture.height * scale);
            Vector2 origin = area.center - size * 0.5f;
            var painter = context.painter2D;
            const int cell = 16;
            for (int y = 0; y < size.y; y += cell)
            for (int x = 0; x < size.x; x += cell)
            {
                float shade = ((x / cell + y / cell) & 1) == 0 ? .28f : .42f;
                painter.fillColor = new Color(shade, shade, shade, 1);
                float left = origin.x + x, top = origin.y + y;
                float right = origin.x + Mathf.Min(x + cell, size.x), bottom = origin.y + Mathf.Min(y + cell, size.y);
                painter.BeginPath();
                painter.MoveTo(new Vector2(left, top));
                painter.LineTo(new Vector2(right, top));
                painter.LineTo(new Vector2(right, bottom));
                painter.LineTo(new Vector2(left, bottom));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= InvalidateFileSize;
            TextureCompositor.Changed -= OnDocumentChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            ReleaseView();
        }
        private void ReleaseView()
        {
            rootVisualElement.Unbind();
            rootVisualElement.Clear();
            data?.Dispose();
            data = null;
            applyButton = null;
            revertButton = null;
            previewStatus = null;
            mipField = null;
            previewRenderer?.Dispose();
            previewRenderer = null;
            lastDirtyCount = int.MinValue;
            outputPreview = null;
            outputInfo = null;
            outputInfoShadow = null;
            previewPane = null;
            previewSurface = null;
            checkerTextureSize = Vector2Int.zero;
        }
    }

    [InitializeOnLoad]
    internal static class WhimTexOutputSettingsHeader
    {
        private sealed class Entry
        {
            internal Object target;
            internal TextureCompositor document;
        }
        private static ConditionalWeakTable<Editor, Entry> cache = new ConditionalWeakTable<Editor, Entry>();

        static WhimTexOutputSettingsHeader()
        {
            Editor.finishedDefaultHeaderGUI += Draw;
            EditorApplication.projectChanged += () => cache = new ConditionalWeakTable<Editor, Entry>();
        }

        private static void Draw(Editor editor)
        {
            if (editor == null || editor.targets.Length != 1 || !(editor.target is Texture2D || editor.target is Sprite)) return;
            var entry = cache.GetValue(editor, _ => new Entry());
            if (entry.target != editor.target)
            {
                entry.target = editor.target;
                entry.document = TextureCompositor.FindDocument(editor.target);
            }
            if (entry.document == null || (editor.target != entry.document.OutputTexture && !entry.document.IsOutputSprite(editor.target as Sprite))) return;
            if (GUILayout.Button("WhimTex Output Settings…")) WhimTexOutputSettingsWindow.Open(entry.document);
        }
    }
}
