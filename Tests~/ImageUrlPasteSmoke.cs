// Unity Pipeline run_script, entry ImageUrlPasteSmoke.Main. No persistent assets or network requests.
using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.SpriteEditor;

public static class ImageUrlPasteSmoke
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    public static string Main()
    {
        var type = typeof(TextureCompositor).Assembly.GetType("DCFApixels.SpriteEditor.ImageClipboard");
        var decode = type.GetMethod("DecodeWebImage", BindingFlags.NonPublic | BindingFlags.Static);
        Texture2D Read(byte[] bytes) => (Texture2D)decode.Invoke(null, new object[] { bytes });
        void Reject(byte[] bytes)
        {
            try { var unexpected = Read(bytes); UnityEngine.Object.DestroyImmediate(unexpected); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { checks++; return; }
            throw new Exception("Invalid image accepted.");
        }
        var source = new Texture2D(8, 4, TextureFormat.RGBA32, false);
        try
        {
            var pixels = new Color32[32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 40, 80, 128);
            source.SetPixels32(pixels); source.Apply();
            byte[] png = source.EncodeToPNG();
            var image = Read(png);
            try
            {
                Check(image.width == 8 && image.height == 4, "PNG dimensions lost.");
                Check(image.isReadable && image.GetPixels32()[0].a == 128, "PNG alpha/readability lost.");
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            image = Read(source.EncodeToJPG());
            try { Check(image.width == 8 && image.height == 4 && image.isReadable, "JPEG decoding failed."); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            Reject(null); Reject(new byte[0]); Reject(System.Text.Encoding.UTF8.GetBytes("<html>not an image</html>"));
            Reject(new byte[] { 255, 216, 255, 192, 0, 8 });
            png[16] = 127; // Oversize IHDR must be rejected before decoding (and before checking its CRC).
            Reject(png);
            var handlerType = typeof(TextureCompositorWindow).GetNestedType("ImageUrlDownload", BindingFlags.NonPublic);
            var handler = (IDisposable)Activator.CreateInstance(handlerType);
            try
            {
                var receive = handlerType.GetMethod("ReceiveData", BindingFlags.NonPublic | BindingFlags.Instance);
                Check((bool)receive.Invoke(handler, new object[] { new byte[] { 1, 2 }, 2 }), "Download rejected bytes.");
                Check(((byte[])handlerType.GetProperty("Bytes").GetValue(handler)).Length == 2, "Download lost bytes.");
                handlerType.GetMethod("ReceiveContentLengthHeader", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(handler, new object[] { 65UL * 1024 * 1024 });
                Check(!(bool)receive.Invoke(handler, new object[] { new byte[1], 1 }), "Download size cap ignored.");
            }
            finally { handler.Dispose(); }
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        return checks + " image URL checks passed";
    }
}
