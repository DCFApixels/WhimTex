using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace DCFApixels.WhimTex
{
    public partial class TextureCompositorWindow
    {
        private const int ImageUrlTimeout = 30;
        private const int ImageUrlMaximumBytes = 64 * 1024 * 1024;

        private UnityWebRequest imageUrlRequest;
        private TextureCompositor imageUrlDocument;
        private Func<Texture2D, bool> imageUrlApply;
        private Action<bool> imageUrlFinished;
        private double imageUrlStarted;

        private bool HasPendingImageUrl => imageUrlRequest != null;

        private bool TryPasteImageUrl(string text)
        {
            if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) return false;
            if (HasPendingImageUrl)
            {
                ShowNotification(new GUIContent("An image is already downloading."));
                return true;
            }
            BeginImageUrlDownload(compositor, uri.AbsoluteUri, InsertDownloadedImage);
            return true;
        }

        private void BeginImageUrlDownload(TextureCompositor document, string url,
            Func<Texture2D, bool> apply, Action<bool> finished = null)
        {
            imageUrlDocument = document;
            imageUrlApply = apply;
            imageUrlFinished = finished;
            try
            {
                imageUrlStarted = EditorApplication.timeSinceStartup;
                imageUrlRequest = new UnityWebRequest(url, "GET", new ImageUrlDownload(), null)
                { timeout = ImageUrlTimeout, redirectLimit = 5 };
                imageUrlRequest.SendWebRequest();
                EditorApplication.update += PollImageUrl;
                ShowNotification(new GUIContent("Downloading image…"), ImageUrlTimeout + 30);
            }
            catch (Exception exception) { FailImageUrlPaste(exception); }
        }

        private void PasteProceduralClipboard(WhimTexApi.ProceduralClipboard data, bool resize)
        {
            WhimTexApi.PreparePortableDestination(data, compositor);
            if (data.Warnings.Count > 0 && !EditorUtility.DisplayDialog("Paste with warnings",
                string.Join("\n\n", data.Warnings), "Paste", "Cancel")) return;
            PasteCopiedLayers(data.Document, resize);
        }

        private void PollImageUrl()
        {
            if (this == null || compositor == null || compositor != imageUrlDocument || imageUrlRequest == null)
            {
                CancelImageUrlPaste();
                return;
            }
            if (!imageUrlRequest.isDone && EditorApplication.timeSinceStartup - imageUrlStarted <= ImageUrlTimeout) return;
            Texture2D texture = null;
            Exception failure = null;
            try
            {
                if (EditorApplication.timeSinceStartup - imageUrlStarted > ImageUrlTimeout)
                    throw new InvalidOperationException("Image download timed out.");
                var download = (ImageUrlDownload)imageUrlRequest.downloadHandler;
                if (download.TooLarge) throw new InvalidOperationException("Image download exceeds 64 MB.");
                if (imageUrlRequest.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Image download failed. Check the link and connection.");
                texture = ImageClipboard.DecodeWebImage(download.Bytes);
                if (imageUrlApply(texture)) texture = null;
            }
            catch (Exception exception) { failure = exception; }
            finally { if (texture != null) DestroyImmediate(texture, true); }
            FinishImageUrlDownload(failure == null);
            if (failure != null) ReportClipboardPasteError("Image paste failed", failure);
        }

        private void FailImageUrlPaste(Exception exception)
        {
            CancelImageUrlPaste();
            ReportClipboardPasteError("Image paste failed", exception);
        }

        private void CancelImageUrlPaste() => FinishImageUrlDownload(false);

        private void FinishImageUrlDownload(bool completed)
        {
            Action<bool> finished = imageUrlFinished;
            imageUrlFinished = null;
            if (imageUrlRequest != null)
            {
                imageUrlRequest.Abort();
                imageUrlRequest.Dispose();
                imageUrlRequest = null;
            }
            EditorApplication.update -= PollImageUrl;
            imageUrlDocument = null;
            imageUrlApply = null;
            RemoveNotification();
            try { finished?.Invoke(completed); }
            catch (Exception exception) { ReportClipboardPasteError("Paste failed", exception); }
        }

        private bool InsertDownloadedImage(Texture2D texture)
        {
            FinishPaintingStroke();
            FinishCanvasTransform();
            var source = texture;
            ExecuteContextChange("Paste Image URL", () =>
            {
                Undo.RegisterCreatedObjectUndo(source, "Paste Image URL");
                Undo.RegisterCompleteObjectUndo(compositor, "Paste Image URL");
                if (!HasCanvasLayers) { compositor.width = source.width; compositor.height = source.height; }
                var layer = DrawingLayerBehaviour.FromMergedTexture(source);
                layer.colorRange = LayerColorRange.Standard;
                layer.blendRange = LayerBlendRange.Standard;
                layer.layerName = compositor.AllocateLayerName(layer);
                // Keep every source pixel: fit the transform to the canvas instead of resampling the image.
                if (layer.TryGetOriginalAspectTransform(compositor, out TextureTransform fitted)) layer.transform = fitted;
                compositor.layers.Insert(0, layer);
                SelectOnlyLayer(layer.Id);
            });
            bool adopted = compositor.layers.Exists(layer => layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture == source);
            ShowNotification(new GUIContent(adopted ? "Image pasted as a Drawing layer." : "Could not insert the downloaded image."));
            return adopted;
        }

        private sealed class ImageUrlDownload : DownloadHandlerScript
        {
            private readonly MemoryStream bytes = new MemoryStream();
            public bool TooLarge { get; private set; }
            public byte[] Bytes => bytes.ToArray();
            public ImageUrlDownload() : base(new byte[64 * 1024]) { }
            protected override void ReceiveContentLengthHeader(ulong length) => TooLarge = length > ImageUrlMaximumBytes;
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (TooLarge || bytes.Length + length > ImageUrlMaximumBytes) { TooLarge = true; return false; }
                if (data != null && length > 0) bytes.Write(data, 0, length);
                return true;
            }
            public override void Dispose() { bytes.Dispose(); base.Dispose(); }
        }
    }
}
