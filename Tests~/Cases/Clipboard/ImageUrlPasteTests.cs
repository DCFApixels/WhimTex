// Pipeline run_script entry ImageUrlPasteTests.Run. Loopback only; no assets or clipboard writes.
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ImageUrlPasteTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static object Field(object obj, string name) => obj.GetType().GetField(name, F).GetValue(obj);
    static bool Pending(WhimTexWindow window) => (bool)window.GetType().GetProperty("HasPendingImageUrl", F).GetValue(window);
    static int checks;
    static void Check(bool yes, string message) { checks++; UnityBRun.Check(!(!yes), message); }
    sealed class ImageServer : IDisposable
    {
        readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        readonly CancellationTokenSource cancellation = new CancellationTokenSource(8000);
        public readonly Task Served;
        public readonly string Url;
        public ImageServer(byte[] png)
        {
            listener.Start(1);
            cancellation.Token.Register(() => listener.Stop());
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/image.png";
            Served = Task.Run(async () =>
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    var buffer = new byte[1024];
                    string request = "";
                    while (!request.Contains("\r\n\r\n"))
                    {
                        int length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation.Token);
                        UnityBRun.Check(!(length == 0 || request.Length > 16384), "Invalid loopback request.");
                        request += Encoding.ASCII.GetString(buffer, 0, length);
                    }
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: " + png.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length, cancellation.Token);
                    await stream.WriteAsync(png, 0, png.Length, cancellation.Token);
                }
                catch (Exception) when (cancellation.IsCancellationRequested) { }
            });
        }
        bool disposed;
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { cancellation.Cancel(); listener.Stop(); Served.GetAwaiter().GetResult(); }
            finally { cancellation.Dispose(); }
        }
    }
    static async Task WaitDownload(WhimTexWindow window)
    {
        for (int i = 0; i < 300 && Pending(window); i++) await UnityBRun.Delay(20);
        Check(!Pending(window), "Loopback download did not finish.");
    }
    static async Task<string> ExecuteRun()
    {
        checks = 0;
        Undo.IncrementCurrentGroup();
        var window = UnityBRun.Create<WhimTexWindow>();
        var doc = UnityBRun.Track((WhimTexDocument)Field(window, "activeDocument"));
        var source = UnityBRun.Track(new Texture2D(32, 16, TextureFormat.RGBA32, false, false));
        try
        {
            var colors = new Color32[32 * 16];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32((byte)i, 31, 127, (byte)(i % 255));
            source.SetPixels32(colors); source.Apply();
            byte[] png = source.EncodeToPNG();
            Check(!(bool)Call(window, "TryPasteImageUrl", "ftp://example.com/a.png"), "Non-HTTP URL accepted.");
            Check(!(bool)Call(window, "TryPasteImageUrl", "not a URL"), "Plain text accepted.");
            using (var server = new ImageServer(png))
            {
                Check((bool)Call(window, "TryPasteImageUrl", server.Url), "HTTP image URL not handled.");
                Check(Pending(window), "Download not tracked.");
                Check((bool)Call(window, "TryPasteImageUrl", server.Url), "Repeated paste not handled.");
                await WaitDownload(window);
                await server.Served;
            }
            Check(doc.width == 32 && doc.height == 16 && doc.layers.Count == 1, "Empty canvas did not adopt image dimensions.");
            var drawing = (DrawingLayerBehaviour)doc.layers[0].Behaviour;
            var stored = (Texture2D)drawing.GetType().GetProperty("StoredTexture", F).GetValue(drawing);
            Check(stored.width == 32 && stored.height == 16, "Source pixels resampled.");
            var restored = stored.GetPixels32();
            for (int i = 0; i < colors.Length; i++) Check(restored[i].Equals(colors[i]), "Pixel/alpha changed at " + i);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Check(doc.layers.Count == 0, "Image URL paste Undo failed.");
            Undo.PerformRedo();
            Check(doc.layers.Count == 1, "Image URL paste Redo failed; layers=" + doc.layers.Count + ", documentAlive=" + (doc != null));
            doc.width = doc.height = 64;
            using (var server = new ImageServer(png))
            {
                Call(window, "TryPasteImageUrl", server.Url);
                await WaitDownload(window);
                await server.Served;
            }
            Check(doc.width == 64 && doc.height == 64 && doc.layers.Count == 2, "Nonempty canvas resized.");
            var fitted = doc.layers[0];
            Check(fitted.transform.scale == new Double2(1, .5), "Source aspect ratio not fitted to canvas.");
            var fittedPixels = (Texture2D)fitted.Behaviour.GetType().GetProperty("StoredTexture", F).GetValue(fitted.Behaviour);
            Check(fittedPixels.width == 32 && fittedPixels.height == 16, "Fitting resampled source image.");
            using (var server = new ImageServer(png))
            {
                Texture2D declined = null;
                bool? completed = null;
                Call(window, "BeginImageUrlDownload", doc, server.Url,
                    (Func<Texture2D, bool>)(image => { declined = image; return false; }),
                    (Action<bool>)(ok => completed = ok));
                await WaitDownload(window);
                await server.Served;
                Check(completed == true, "Single-image consumer callback did not finish.");
                Check(declined == null, "Declined decoded texture was not destroyed.");
                Check(doc.layers.Count == 2, "A non-layer consumer inserted layers.");
            }
            using (var server = new ImageServer(png))
            {
                bool? completed = null;
                int applies = 0;
                Call(window, "BeginImageUrlDownload", doc, server.Url,
                    (Func<Texture2D, bool>)(image => { applies++; return false; }),
                    (Action<bool>)(ok => completed = ok));
                Call(window, "CancelImageUrlPaste");
                Check(!Pending(window) && completed == false && applies == 0, "Cancellation applied image or lost cleanup.");
                Check(Field(window, "imageUrlApply") == null && Field(window, "imageUrlFinished") == null, "Canceled callbacks retained.");
                Call(window, "CancelImageUrlPaste");
                server.Dispose();
                await server.Served;
            }
            using (var server = new ImageServer(png))
            {
                bool? completed = null;
                Call(window, "BeginImageUrlDownload", doc, server.Url,
                    (Func<Texture2D, bool>)(image => { throw new Exception("Applied to switched document."); }),
                    (Action<bool>)(ok => completed = ok));
                Undo.ClearUndo(doc);
                var next = UnityBRun.Create<WhimTexDocument>();
                Call(window, "SetDocument", next);
                doc = next;
                Check(!Pending(window) && completed == false && doc.layers.Count == 0, "Document switch did not cancel download.");
                server.Dispose();
                await server.Served;
            }
            var downloadType = window.GetType().GetNestedType("ImageUrlDownload", F);
            using var download = (UnityEngine.Networking.DownloadHandler)Activator.CreateInstance(downloadType, true);
            Call(download, "ReceiveContentLengthHeader", (ulong)(64 * 1024 * 1024 + 1));
            Check((bool)downloadType.GetProperty("TooLarge").GetValue(download), "64 MB header limit removed.");
            Check(!(bool)Call(download, "ReceiveData", new byte[1], 1), "Oversized download still accepts data.");
            return "";
        }
        finally
        {
            Call(window, "CancelImageUrlPaste");
            Undo.ClearUndo(doc);
            Object.DestroyImmediate(window);
            Object.DestroyImmediate(source);
        }
    }
    public static string Run(string runId) => UnityBRun.Start(runId, "ImageUrlPasteSmoke.Run", async () => { await ExecuteRun(); });
    public static string Poll(string runId) => UnityBRun.Poll(runId);
    public static System.Threading.Tasks.Task<string> Cancel(string runId) => UnityBRun.Cancel(runId);
    public static string Cleanup(string runId) => UnityBRun.Cleanup(runId);
}

