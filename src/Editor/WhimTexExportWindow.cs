using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Format = DCFApixels.WhimTex.TextureCompositorWindow.TextureExportFormat;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal sealed class WhimTexExportOptions
    {
        internal enum ExrCompression { ZIP, RLE, PIZ, None }
        public Format format = Format.Png;
        public WhimTexJsonWriteMode jsonMode = WhimTexJsonWriteMode.FullOptimized;
        public int jpegQuality = 95;
        public bool exrFloat32;
        public ExrCompression exrCompression = ExrCompression.ZIP;

        internal Texture2D.EXRFlags ExrFlags => (exrFloat32 ? Texture2D.EXRFlags.OutputAsFloat : Texture2D.EXRFlags.None) |
            (exrCompression == ExrCompression.ZIP ? Texture2D.EXRFlags.CompressZIP :
             exrCompression == ExrCompression.RLE ? Texture2D.EXRFlags.CompressRLE :
             exrCompression == ExrCompression.PIZ ? Texture2D.EXRFlags.CompressPIZ : Texture2D.EXRFlags.None);

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(Format), format)) throw new InvalidOperationException("Choose a supported export format.");
            if (format == Format.Json && !Enum.IsDefined(typeof(WhimTexJsonWriteMode), jsonMode)) throw new InvalidOperationException("Choose a supported JSON mode.");
            if (format == Format.Jpeg && (jpegQuality < 1 || jpegQuality > 100)) throw new InvalidOperationException("JPEG quality must be between 1 and 100.");
            if (format == Format.Exr && !Enum.IsDefined(typeof(ExrCompression), exrCompression)) throw new InvalidOperationException("Choose supported EXR compression.");
        }
    }

    internal sealed class WhimTexExportWindow : EditorWindow
    {
        private static readonly List<string> Formats = new() { "PNG (.png)", "JPEG (.jpg)", "TGA (.tga)", "OpenEXR (.exr)",
            "Unity Texture2D (.asset)", "Layered PSD (.psd)", "WhimTex JSON (.json)" };
        private static readonly List<string> JsonModes = new() { "Full Optimized (Default)", "Full", "Compact" };
        private static readonly WhimTexJsonWriteMode[] JsonValues = { WhimTexJsonWriteMode.FullOptimized, WhimTexJsonWriteMode.Full, WhimTexJsonWriteMode.Compact };
        [SerializeField] private TextureCompositorWindow owner;
        [SerializeField] private TextureCompositor source;
        [SerializeField] private WhimTexExportOptions options = new();
        private VisualElement settings;
        private HelpBox drawingWarning, sourceWarning, errorBox;
        private Button exportButton, cancelButton;
        private bool exporting;

        internal static void Open(TextureCompositorWindow owner, TextureCompositor source)
        {
            foreach (var existing in Resources.FindObjectsOfTypeAll<WhimTexExportWindow>())
                if (existing.owner == owner && existing.source == source)
                { existing.ShowUtility(); existing.Focus(); return; }
            var window = CreateInstance<WhimTexExportWindow>();
            window.owner = owner;
            window.source = source;
            window.titleContent = new GUIContent("WhimTex Export");
            window.minSize = new Vector2(360, 240);
            window.position = new Rect(owner.position.center - new Vector2(210, 140), new Vector2(420, 280));
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            options ??= new WhimTexExportOptions();
            var root = rootVisualElement;
            root.Clear();
            WhimTexUI.ApplyWindowStyles(root);
            root.AddToClassList("whimtex-export-window");
            var body = new ScrollView { name = "exportBody" };
            body.AddToClassList("whimtex-export-body");
            root.Add(body);
            var format = new DropdownField("Format", Formats, (int)options.format) { name = "format" };
            format.AddToClassList("whimtex-export-field");
            format.RegisterValueChangedCallback(_ =>
            {
                options.format = (Format)format.index;
                errorBox.AddToClassList("whimtex-hidden");
                BuildSettings();
            });
            body.Add(format);
            settings = new VisualElement { name = "formatSettings" };
            body.Add(settings);
            drawingWarning = new HelpBox("", HelpBoxMessageType.Warning) { name = "drawingWarning" };
            sourceWarning = new HelpBox("The source document is no longer open here. Reopen Export from the document you want to export.", HelpBoxMessageType.Warning) { name = "sourceWarning" };
            errorBox = new HelpBox("", HelpBoxMessageType.Error) { name = "exportError" };
            errorBox.AddToClassList("whimtex-hidden");
            body.Add(drawingWarning);
            body.Add(sourceWarning);
            body.Add(errorBox);
            var actions = new VisualElement();
            actions.AddToClassList("whimtex-export-actions");
            cancelButton = new Button(Close) { name = "cancel", text = "Cancel" };
            exportButton = new Button(Export) { name = "export", text = "Export…" };
            actions.Add(cancelButton);
            actions.Add(exportButton);
            root.Add(actions);
            BuildSettings();
        }

        private void BuildSettings()
        {
            settings.Clear();
            string description;
            switch (options.format)
            {
                case Format.Json:
                    var mode = new DropdownField("Mode", JsonModes, Array.IndexOf(JsonValues, options.jsonMode)) { name = "jsonMode" };
                    mode.AddToClassList("whimtex-export-field");
                    var explanation = new Label(ModeDescription()) { name = "modeDescription" };
                    explanation.AddToClassList("whimtex-export-description");
                    mode.RegisterValueChangedCallback(_ => { options.jsonMode = JsonValues[mode.index]; explanation.text = ModeDescription(); });
                    settings.Add(mode);
                    settings.Add(explanation);
                    description = "Editable document settings and layers. Export creates a copy and does not change the open document.";
                    break;
                case Format.Jpeg:
                    var quality = new SliderInt("Quality", 1, 100) { name = "jpegQuality", value = options.jpegQuality, showInputField = true };
                    quality.AddToClassList("whimtex-export-field");
                    quality.RegisterValueChangedCallback(e => options.jpegQuality = e.newValue);
                    settings.Add(quality);
                    description = "Flattened color image with lossy compression. Transparent areas become white; HDR values use the ordinary color range.";
                    break;
                case Format.Exr:
                    var precision = new DropdownField("Precision", new List<string> { "16-bit Half", "32-bit Float" }, options.exrFloat32 ? 1 : 0) { name = "exrPrecision" };
                    precision.AddToClassList("whimtex-export-field");
                    precision.RegisterValueChangedCallback(_ => options.exrFloat32 = precision.index == 1);
                    var compression = new EnumField("Compression", options.exrCompression) { name = "exrCompression" };
                    compression.AddToClassList("whimtex-export-field");
                    compression.RegisterValueChangedCallback(e => options.exrCompression = (WhimTexExportOptions.ExrCompression)e.newValue);
                    settings.Add(precision);
                    settings.Add(compression);
                    description = "Flattened HDR image with alpha. Compression is lossless; precision controls the stored channel format.";
                    break;
                case Format.Psd:
                    description = "Layered document with groups and supported editable effects. Other effects are rasterized or approximated. Export details appear in the Console.";
                    break;
                case Format.Asset:
                    description = "Flattened Unity texture with HDR values and alpha, saved inside Assets. Editable layers are not included.";
                    break;
                default:
                    description = "Flattened color image with transparency. HDR values use the ordinary color range.";
                    break;
            }
            var note = new Label(description);
            note.AddToClassList("whimtex-export-description");
            settings.Add(note);
            RefreshState();
        }

        private string ModeDescription() => options.jsonMode == WhimTexJsonWriteMode.Full ? "Includes all persistent settings, including inactive ones." :
            options.jsonMode == WhimTexJsonWriteMode.Compact ? "Omits inactive settings and default values. Disabled layers and FX remain." :
            "Omits inactive settings; keeps active defaults. Disabled layers and FX remain.";

        private void OnInspectorUpdate() => RefreshState();

        private void RefreshState()
        {
            if (exportButton == null) return;
            bool available = owner != null && source != null && owner.AgentDocument == source;
            exportButton.SetEnabled(available && !exporting);
            cancelButton.SetEnabled(!exporting);
            sourceWarning.EnableInClassList("whimtex-hidden", available);
            int drawingCount = available && options.format == Format.Json ? CountDrawing(source.layers) : 0;
            drawingWarning.EnableInClassList("whimtex-hidden", drawingCount == 0);
            if (drawingCount > 0) drawingWarning.text = $"{drawingCount} Drawing layer(s) contain pixels that JSON cannot store. They will be exported as empty layers with settings and FX. The open document stays unchanged; confirmation is required.";
        }

        private static int CountDrawing(List<Layer> layers)
        {
            int count = 0;
            if (layers == null) return count;
            foreach (var layer in layers)
                if (layer != null)
                {
                    if (layer.Behaviour is DrawingLayerBehaviour drawing && drawing.HasJsonOmittedPixels) count++;
                    count += CountDrawing(layer.children);
                }
            return count;
        }

        private void Export()
        {
            exporting = true;
            RefreshState();
            errorBox.AddToClassList("whimtex-hidden");
            try
            {
                if (owner == null) throw new InvalidOperationException("The source window was closed.");
                if (owner.ExportDocumentFromDialog(source, options)) Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                errorBox.text = error.Message;
                errorBox.RemoveFromClassList("whimtex-hidden");
                Debug.LogException(error);
            }
            finally
            {
                exporting = false;
                if (this != null) { RefreshState(); Focus(); }
            }
        }
    }
}
