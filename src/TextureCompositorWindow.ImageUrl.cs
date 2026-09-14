using System;
using System.Collections.Generic;
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

        // Each entry fetches one link. apply() takes ownership of the decoded texture by returning true.
        private readonly List<(string url, Func<Texture2D, bool> apply)> imageUrlJobs = new List<(string, Func<Texture2D, bool>)>();
        private UnityWebRequest imageUrlRequest;
        private TextureCompositor imageUrlDocument;
        private double imageUrlStarted;
        private int imageUrlIndex;
        private Action<bool> imageUrlFinished;
        private WhimTexApi.ProceduralClipboard clipboardPasteData;
        private bool clipboardPasteResize;

        private bool HasPendingImageUrl => imageUrlRequest != null || imageUrlFinished != null;

        private bool TryPasteImageUrl(string text)
        {
            if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) return false;
            if (HasPendingImageUrl)
            {
                ShowNotification(new GUIContent("An image is already downloading."));
                return true;
            }
            BeginImageUrlBatch(compositor,
                new List<(string, Func<Texture2D, bool>)> { (uri.AbsoluteUri, InsertDownloadedImage) }, null);
            return true;
        }

        // Applies a parsed clipboard tree. Returns true when the batch owns the data: image layers are
        // downloaded first, then the whole tree is pasted in one Undo step once every image has arrived.
        private bool PasteProceduralClipboard(WhimTexApi.ProceduralClipboard data, bool resize)
        {
            if (data.Images.Count == 0)
            {
                PasteCopiedLayers(data.Document, resize);
                return false;
            }
            if (HasPendingImageUrl)
            {
                ShowNotification(new GUIContent("Wait for the current image download."));
                return false;
            }
            if (!EditorUtility.DisplayDialog("Paste images from links",
                "This JSON downloads " + data.Images.Count + " image(s) from:\n\n" + DescribeImageHosts(data) +
                "\n\nOnly paste links you trust. Each download is limited to 64 MB, and the layers are added as one Undo step after every image arrives.",
                "Download and Paste", "Cancel"))
                return false;
            var jobs = new List<(string, Func<Texture2D, bool>)>(data.Images.Count);
            foreach (var image in data.Images) jobs.Add((image.url, texture => AdoptClipboardImage(image.layer, texture)));
            clipboardPasteData = data;
            clipboardPasteResize = resize;
            BeginImageUrlBatch(compositor, jobs, FinishClipboardPaste);
            return true;
        }

        private static string DescribeImageHosts(WhimTexApi.ProceduralClipboard data)
        {
            var hosts = new List<string>();
            foreach (var image in data.Images)
            {
                string host = Uri.TryCreate(image.url, UriKind.Absolute, out Uri link) ? link.Host : image.url;
                if (!hosts.Contains(host)) hosts.Add(host);
            }
            return string.Join(", ", hosts);
        }

        private bool AdoptClipboardImage(Layer layer, Texture2D texture)
        {
            WhimTexApi.ProceduralClipboard data = clipboardPasteData;
            if (data == null || !(layer?.Behaviour is DrawingLayerBehaviour drawing)) return false;
            drawing.AdoptStoredTexture(texture);
            // Keep every source pixel: fit the transform to the canvas instead of resampling the image.
            if (layer.TryGetOriginalAspectTransform(data.Document, out TextureTransform fitted)) layer.transform = fitted;
            return true;
        }

        private void FinishClipboardPaste(bool completed)
        {
            WhimTexApi.ProceduralClipboard data = clipboardPasteData;
            clipboardPasteData = null;
            if (data == null) return;
            try
            {
                if (completed) PasteCopiedLayers(data.Document, clipboardPasteResize);
            }
            finally { data.Dispose(); }
        }

        private void CancelClipboardPaste()
        {
            WhimTexApi.ProceduralClipboard data = clipboardPasteData;
            clipboardPasteData = null;
            data?.Dispose();
        }

        private void BeginImageUrlBatch(TextureCompositor document,
            List<(string url, Func<Texture2D, bool> apply)> jobs, Action<bool> finished)
        {
            imageUrlDocument = document;
            imageUrlJobs.Clear();
            imageUrlJobs.AddRange(jobs);
            imageUrlIndex = 0;
            imageUrlFinished = finished;
            EditorApplication.update += PollImageUrl;
            StartNextImageUrl();
        }

        private void StartNextImageUrl()
        {
            if (imageUrlIndex >= imageUrlJobs.Count)
            {
                FinishImageUrlBatch(true);
                return;
            }
            try
            {
                imageUrlStarted = EditorApplication.timeSinceStartup;
                imageUrlRequest = new UnityWebRequest(imageUrlJobs[imageUrlIndex].url, "GET", new ImageUrlDownload(), null)
                { timeout = ImageUrlTimeout, redirectLimit = 5 };
                imageUrlRequest.SendWebRequest();
                ShowNotification(new GUIContent(imageUrlJobs.Count == 1
                    ? "Downloading image…"
                    : $"Downloading image {imageUrlIndex + 1} of {imageUrlJobs.Count}…"), ImageUrlTimeout + 30);
            }
            catch (Exception exception) { FailImageUrlBatch(exception.Message); }
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
            string failure = null;
            try
            {
                if (EditorApplication.timeSinceStartup - imageUrlStarted > ImageUrlTimeout)
                    throw new InvalidOperationException("Image download timed out.");
                var download = (ImageUrlDownload)imageUrlRequest.downloadHandler;
                if (download.TooLarge) throw new InvalidOperationException("Image download exceeds 64 MB.");
                if (imageUrlRequest.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Image download failed. Check the link and connection.");
                texture = ImageClipboard.DecodeWebImage(download.Bytes);
                if (imageUrlJobs[imageUrlIndex].apply(texture)) texture = null;
            }
            catch (Exception exception) { failure = exception.Message; }
            finally { if (texture != null) DestroyImmediate(texture, true); }
            if (failure != null)
            {
                FailImageUrlBatch(failure);
                return;
            }
            ReleaseImageUrlRequest();
            imageUrlIndex++;
            StartNextImageUrl();
        }

        private void FinishImageUrlBatch(bool completed)
        {
            Action<bool> finished = imageUrlFinished;
            ReleaseImageUrlRequest();
            EditorApplication.update -= PollImageUrl;
            imageUrlJobs.Clear();
            imageUrlIndex = 0;
            imageUrlDocument = null;
            imageUrlFinished = null;
            RemoveNotification();
            try { finished?.Invoke(completed); }
            catch (Exception exception) { ShowNotification(new GUIContent("Paste failed: " + exception.Message)); }
        }

        private void FailImageUrlBatch(string message)
        {
            FinishImageUrlBatch(false);
            ShowNotification(new GUIContent(message ?? "Image download failed."));
        }

        private void ReleaseImageUrlRequest()
        {
            if (imageUrlRequest == null) return;
            imageUrlRequest.Abort();
            imageUrlRequest.Dispose();
            imageUrlRequest = null;
        }

        private void CancelImageUrlPaste()
        {
            if (HasPendingImageUrl || imageUrlJobs.Count > 0) FinishImageUrlBatch(false);
            CancelClipboardPaste();
        }

        private bool InsertDownloadedImage(Texture2D texture)
        {
            FinishPaintingStroke();
            FinishPreviewTransform();
            var source = texture;
            ExecuteContextChange("Paste Image URL", () =>
            {
                if (!HasPreviewLayers) { compositor.width = source.width; compositor.height = source.height; }
                var layer = DrawingLayerBehaviour.FromMergedTexture(source);
                layer.colorRange = LayerColorRange.Standard;
                layer.blendRange = LayerBlendRange.Standard;
                layer.layerName = compositor.AllocateLayerName(layer);
                // Keep every source pixel: fit the transform to the canvas instead of resampling the image.
                if (layer.TryGetOriginalAspectTransform(compositor, out TextureTransform fitted)) layer.transform = fitted;
                layer.MakeTexturePersistent(compositor);
                Undo.RegisterCreatedObjectUndo(source, "Paste Image URL");
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
