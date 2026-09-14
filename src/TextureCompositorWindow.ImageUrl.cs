using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace DCFApixels.WhimTex
{
    public partial class TextureCompositorWindow
    {
        private UnityWebRequest imageUrlRequest;
        private TextureCompositor imageUrlDocument;
        private double imageUrlStarted;

        private bool TryPasteImageUrl(string text)
        {
            if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) return false;
            if (imageUrlRequest != null)
            {
                ShowNotification(new GUIContent("An image is already downloading."));
                return true;
            }
            try
            {
                imageUrlDocument = compositor;
                imageUrlStarted = EditorApplication.timeSinceStartup;
                imageUrlRequest = new UnityWebRequest(uri.AbsoluteUri, "GET", new ImageUrlDownload(), null)
                { timeout = 30, redirectLimit = 5 };
                imageUrlRequest.SendWebRequest();
                EditorApplication.update += PollImageUrlPaste;
                ShowNotification(new GUIContent("Downloading image…"), 30);
            }
            catch
            {
                CancelImageUrlPaste();
                ShowNotification(new GUIContent("Could not start the image download."));
            }
            return true;
        }

        private void CancelImageUrlPaste()
        {
            EditorApplication.update -= PollImageUrlPaste;
            if (imageUrlRequest != null)
            {
                imageUrlRequest.Abort();
                imageUrlRequest.Dispose();
                imageUrlRequest = null;
            }
            imageUrlDocument = null;
        }

        private void PollImageUrlPaste()
        {
            if (this == null || compositor == null || compositor != imageUrlDocument)
            {
                CancelImageUrlPaste();
                if (this != null) RemoveNotification();
                return;
            }
            if (!imageUrlRequest.isDone && EditorApplication.timeSinceStartup - imageUrlStarted <= 30) return;
            Texture2D texture = null;
            try
            {
                if (EditorApplication.timeSinceStartup - imageUrlStarted > 30)
                    throw new InvalidOperationException("Image download timed out.");
                var download = (ImageUrlDownload)imageUrlRequest.downloadHandler;
                if (download.TooLarge) throw new InvalidOperationException("Image download exceeds 64 MB.");
                if (imageUrlRequest.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Image download failed. Check the link and connection.");
                texture = ImageClipboard.DecodeWebImage(download.Bytes);
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
                    var placement = TextureTransform.Default;
                    // Fit the visible bounds only; keep the full-resolution source texture untouched.
                    float fit = Mathf.Min((float)compositor.width / source.width, (float)compositor.height / source.height);
                    placement.scale = new Vector2(source.width * fit / compositor.width, source.height * fit / compositor.height);
                    layer.transform = placement;
                    layer.MakeTexturePersistent(compositor);
                    Undo.RegisterCreatedObjectUndo(source, "Paste Image URL");
                    compositor.layers.Insert(0, layer);
                    SelectOnlyLayer(layer.Id);
                });
                if (compositor.layers.Exists(layer => layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture == source))
                {
                    texture = null;
                    ShowNotification(new GUIContent("Image pasted as a Drawing layer."));
                }
                else ShowNotification(new GUIContent("Could not insert the downloaded image."));
            }
            catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); }
            finally
            {
                if (texture != null) DestroyImmediate(texture, true);
                CancelImageUrlPaste();
            }
        }

        private sealed class ImageUrlDownload : DownloadHandlerScript
        {
            private const int MaximumBytes = 64 * 1024 * 1024;
            private readonly MemoryStream bytes = new MemoryStream();
            public bool TooLarge { get; private set; }
            public byte[] Bytes => bytes.ToArray();
            public ImageUrlDownload() : base(new byte[64 * 1024]) { }
            protected override void ReceiveContentLengthHeader(ulong length) => TooLarge = length > MaximumBytes;
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (TooLarge || bytes.Length + length > MaximumBytes) { TooLarge = true; return false; }
                if (data != null && length > 0) bytes.Write(data, 0, length);
                return true;
            }
            public override void Dispose() { bytes.Dispose(); base.Dispose(); }
        }
    }
}
