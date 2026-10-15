using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class ImageClipboardWriteTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type Clipboard => typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.ImageClipboard", true);
    static object Call(object target, string name, params object[] args)
    {
        Type type = target as Type ?? target.GetType();
        MethodInfo method = Array.Find(type.GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return method.Invoke(method.IsStatic ? null : target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static object Read(object target, string name) => (target as Type ?? target.GetType()).GetField(name, Flags).GetValue(target is Type ? null : target);
    static void Set(object target, string name, object value) => (target as Type ?? target.GetType()).GetField(name, Flags).SetValue(target is Type ? null : target, value);
    static Texture2D Stored(Layer layer) => (Texture2D)layer.Behaviour.GetType().GetProperty("StoredTexture", Flags).GetValue(layer.Behaviour);
    static void ColorNear(TestContext t, Color expected, Color actual, float tolerance, string label)
    {
        t.Near(expected.r, actual.r, tolerance, label + " R"); t.Near(expected.g, actual.g, tolerance, label + " G");
        t.Near(expected.b, actual.b, tolerance, label + " B"); t.Near(expected.a, actual.a, tolerance, label + " A");
    }
    static Texture2D Texture(int width, int height, Color32[] pixels)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        texture.SetPixels32(pixels); texture.Apply(false, false); return texture;
    }
    static readonly Color32[] Rgba =
    {
        new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 128), new Color32(0, 0, 255, 0),
        new Color32(200, 100, 50, 64), new Color32(20, 210, 130, 200), new Color32(82, 64, 239, 255)
    };

    public static string RunManaged() => TestContext.Run("Image clipboard encoding and internal HDR placement", t =>
    {
        var original = (Color32[])Rgba.Clone();
        byte[] dib = (byte[])Call(Clipboard, "EncodeDib", original, 3, 2);
        t.True(original.SequenceEqual(Rgba), "DIB encoding leaves its source pixels unchanged");
        t.Equal(124u, BitConverter.ToUInt32(dib, 0), "DIB uses the V5 header");
        t.Equal(3, BitConverter.ToInt32(dib, 4), "DIB width");
        t.Equal(-2, BitConverter.ToInt32(dib, 8), "DIB has top-down rows");
        t.Equal((ushort)32, BitConverter.ToUInt16(dib, 14), "DIB stores 32-bit pixels");
        t.Equal(3u, BitConverter.ToUInt32(dib, 16), "DIB declares explicit channel masks");
        foreach (var mask in new[] { (40, 0x00FF0000u), (44, 0x0000FF00u), (48, 0x000000FFu), (52, 0xFF000000u) })
            t.Equal(mask.Item2, BitConverter.ToUInt32(dib, mask.Item1), "DIB channel mask");
        t.Equal(0x73524742u, BitConverter.ToUInt32(dib, 56), "DIB declares sRGB");
        var decoded = (Texture2D)Call(Clipboard, "DecodeDib", dib);
        try
        {
            t.True(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(decoded.graphicsFormat), "DIB decodes into an sRGB texture");
            var pixels = decoded.GetPixels32();
            for (int i = 0; i < original.Length; i++)
            {
                Color32 p = original[i];
                // DIB bytes use premultiplied BGRA; the existing decoder exposes those stored channels.
                var expected = new Color32((byte)((p.r * p.a + 127) / 255), (byte)((p.g * p.a + 127) / 255),
                    (byte)((p.b * p.a + 127) / 255), p.a);
                t.Equal(expected, pixels[i], "DIB preserves row orientation, channel order and alpha at pixel " + i);
            }
        }
        finally { Object.DestroyImmediate(decoded); }

        Type areaType = typeof(WhimTexWindow).GetNestedType("AreaClipboard", Flags);
        object copy = Activator.CreateInstance(areaType, true);
        var hdr = new[] { new Color(3, .25f, .125f, .5f), new Color(2, .5f, .25f, 1) };
        Set(copy, "pixels", hdr); Set(copy, "region", new RectInt(1, 1, 2, 1)); Set(copy, "canvas", new Vector2Int(4, 3));
        foreach (var size in new[] { new Vector2Int(4, 3), new Vector2Int(6, 5) })
        {
            var pasted = (Texture2D)Call(typeof(WhimTexWindow), "BuildAreaClipboardTexture", copy, size.x, size.y);
            try
            {
                int x = size.x == 4 ? 1 : 2, y = size.y == 3 ? 1 : 2;
                t.True(pasted.format == TextureFormat.RGBAHalf || pasted.format == TextureFormat.RGBAFloat, "Internal clipboard keeps floating-point storage");
                ColorNear(t, hdr[0], pasted.GetPixel(x, y), .002f, "HDR clipboard retains value and placement");
                ColorNear(t, hdr[1], pasted.GetPixel(x + 1, y), .002f, "HDR clipboard retains its second pixel");
                t.Equal(0f, pasted.GetPixel(0, 0).a, "Pixels outside the copied region remain transparent");
                hdr[0] = Color.green;
                t.True(pasted.GetPixel(x, y).r > 1, "Pasted texture is independent of the clipboard array");
                hdr[0] = new Color(3, .25f, .125f, .5f);
            }
            finally { Object.DestroyImmediate(pasted); }
        }
    });

    [Serializable] sealed class NativeReport
    {
        public string status, message;
        public int checks;
        public string[] failures;
        public bool recoveryRequired;
    }
    static NativeClipboard pendingClipboard;
    static LocalState pendingState;
    public static string RunNative()
    {
        var t = new TestContext();
        if (Application.platform != RuntimePlatform.WindowsEditor)
            return TestContext.Result("skipped", 0, "Native image publication requires the Windows Editor.").ToJson();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type previous = assembly.GetType(typeof(ImageClipboardWriteTests).FullName, false);
            if (previous?.GetField("pendingClipboard", Flags)?.GetValue(null) != null)
                return JsonUtility.ToJson(new NativeReport { status = "skipped", checks = 0,
                    message = "An earlier image clipboard fixture needs recovery before another native run.",
                    failures = Array.Empty<string>(), recoveryRequired = true });
        }
        NativeClipboard clipboard = null;
        LocalState state = null;
        Exception failure = null;
        string skipped = null;
        bool restored = false;
        try
        {
            if (!NativeClipboard.TryCapture(out clipboard, out skipped))
                return TestContext.Result("skipped", 0, skipped).ToJson();
            state = new LocalState(clipboard.OriginalRevision);
            NativeChecks(t, clipboard, state);
        }
        catch (ClipboardChanged) { skipped = "Clipboard changed outside the fixture; external contents were left untouched."; }
        catch (Exception error) { failure = error; }
        finally
        {
            try { state?.DisposeOwned(); } catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            try
            {
                if (clipboard != null) restored = clipboard.Restore(t);
                if (clipboard != null && !restored && failure == null) skipped = "Clipboard changed outside the fixture; original clipboard restoration was not applied.";
            }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            try { state?.RestoreBorrowed(restored ? NativeClipboard.Sequence : (uint?)null); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            if (clipboard != null && clipboard.RecoveryRequired)
            {
                pendingClipboard = clipboard; pendingState = state;
            }
            else clipboard?.Dispose();
        }
        return JsonUtility.ToJson(new NativeReport { status = failure != null ? "failed" : skipped != null ? "skipped" : "passed",
            checks = t.Checks, message = skipped ?? "PNG/DIBV5 publication, HDR paste and all-format clipboard restoration",
            failures = failure == null ? Array.Empty<string>() : new[] { failure.ToString() },
            recoveryRequired = clipboard?.RecoveryRequired == true });
    }
    // Recovery uses only retained fixture memory and refuses to overwrite a subsequent external clipboard change.
    public static string RecoverNative()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(typeof(ImageClipboardWriteTests).FullName, false);
            if (type == null || type.GetField("pendingClipboard", Flags)?.GetValue(null) == null) continue;
            return (string)type.GetMethod("RecoverOwned", Flags).Invoke(null, null);
        }
        return TestContext.Result("skipped", 0, "No pending image clipboard fixture recovery.").ToJson();
    }
    static string RecoverOwned()
    {
        var t = new TestContext();
        try
        {
            if (!pendingClipboard.Restore(t))
                return JsonUtility.ToJson(new NativeReport { status = "skipped", checks = t.Checks,
                    message = "Clipboard ownership changed; the captured original remains in fixture memory and external contents were left untouched.",
                    failures = Array.Empty<string>(), recoveryRequired = true });
            pendingState?.RestoreBorrowed(NativeClipboard.Sequence);
            pendingClipboard.Dispose(); pendingClipboard = null; pendingState = null;
            return TestContext.Result("passed", t.Checks, "All original clipboard formats and bytes restored.").ToJson();
        }
        catch (Exception error)
        {
            return JsonUtility.ToJson(new NativeReport { status = "failed", checks = t.Checks,
                message = "Image clipboard fixture recovery is still pending.", failures = new[] { error.ToString() }, recoveryRequired = true });
        }
    }
    static void Publish(NativeClipboard clipboard, Texture2D texture, Guid token)
    {
        clipboard.EnsureCurrent(); clipboard.PublicationAttempted = true;
        object[] args = { texture, token, 0u };
        bool written = (bool)Call(Clipboard, "TryPublish", args);
        if (!written) throw new InvalidOperationException("The Windows writer unexpectedly reported an unsupported platform.");
        clipboard.Track(token, (uint)args[2]);
    }
    static void NativeChecks(TestContext t, NativeClipboard clipboard, LocalState state)
    {
        var input = (Color32[])Rgba.Clone(); input[2] = new Color32(0, 0, 0, 0);
        var image = state.Own(Texture(3, 2, input));
        t.True((bool)Clipboard.GetProperty("SupportsImageWrite", Flags).GetValue(null), "Windows reports support for native image publication");
        Guid token = Guid.NewGuid(); Publish(clipboard, image, token);
        t.True((bool)Call(Clipboard, "IsCurrentImage", token, NativeClipboard.Sequence), "Published image token and revision identify the current clipboard");
        t.True(clipboard.HasFormat("PNG") && clipboard.HasFormat(17) && clipboard.HasFormat("WhimTex.Selection"), "Publication includes PNG, DIBV5 and the private ownership token");
        var read = state.Own((Texture2D)Call(Clipboard, "ReadImage"));
        t.Equal(3, read.width, "Published PNG width"); t.Equal(2, read.height, "Published PNG height");
        t.True(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(read.graphicsFormat), "Published PNG reads as an sRGB image");
        t.True(input.SequenceEqual(read.GetPixels32()), "Preferred PNG readback preserves straight RGBA and row orientation");

        var source = state.Window(4, 3); var target = state.Window(4, 3);
        var sourceDoc = (WhimTexDocument)Read(source, "activeDocument"); var targetDoc = (WhimTexDocument)Read(target, "activeDocument");
        var linear = state.Own(new Texture2D(4, 3, TextureFormat.RGBAHalf, false, true)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point });
        var colors = Enumerable.Repeat(new Color(2, .25f, .125f, .75f), 12).ToArray(); colors[6].r = 3;
        linear.SetPixels(colors); linear.Apply(false, false);
        var drawing = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", linear);
        Layer selected = drawing.Owner;
        selected.colorRange = LayerColorRange.HDR; selected.blendRange = LayerBlendRange.HDR;
        sourceDoc.layers.Add(selected); Call(sourceDoc, "NormalizeModel"); Call(source, "SelectOnlyLayer", selected.Id);
        var selection = Call(source, "GetAreaSelection"); var coverage = new byte[12]; coverage[5] = 128; coverage[6] = 255;
        Type combine = selection.GetType().Assembly.GetType("DCFApixels.WhimTex.SelectionCombine", true);
        Call(selection, "Set", coverage, Enum.Parse(combine, "Replace"));
        clipboard.EnsureCurrent(); clipboard.PublicationAttempted = true; Call(source, "CopyAreaSelection", false);
        object area = Read(typeof(WhimTexWindow), "areaClipboard");
        t.True(area != null, "Copy stores an independent internal snapshot");
        clipboard.Track((Guid)Read(area, "token"), (uint)Read(area, "systemRevision"));
        var copied = (Color[])Read(area, "pixels");
        t.Equal(new RectInt(1, 1, 2, 1), (RectInt)Read(area, "region"), "Copy crops to selection bounds");
        ColorNear(t, new Color(2, .25f, .125f, .75f * 128 / 255), copied[0], .003f, "Selection coverage changes alpha without clamping HDR RGB");
        t.True(copied[1].r > 2.9f, "Internal copied pixels retain HDR above one");
        var external = state.Own((Texture2D)Call(Clipboard, "ReadImage"));
        t.Equal(2, external.width, "External image is cropped to the selected width"); t.Equal(1, external.height, "External image is cropped to the selected height");
        Color32 externalPixel = external.GetPixels32()[0];
        t.Equal((byte)255, externalPixel.r, "External PNG clamps HDR red into SDR");
        t.Near(Mathf.LinearToGammaSpace(.25f) * 255, externalPixel.g, 2, "External PNG encodes linear green to sRGB once");
        t.Near(.75f * 128 / 255 * 255, externalPixel.a, 1, "External PNG keeps partial selection alpha");

        // Offer owned valid JSON alongside the image to verify image-snapshot priority.
        var jsonDoc = state.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); jsonDoc.width = 4; jsonDoc.height = 3;
        jsonDoc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.green })); Call(jsonDoc, "NormalizeModel");
        clipboard.AppendText(WhimTexDocumentJson.Write(jsonDoc).Json);
        Set(area, "systemRevision", NativeClipboard.Sequence);
        Call(target, "PasteAreaSelection");
        t.Equal(1, targetDoc.layers.Count, "Valid internal image takes priority over simultaneous clipboard JSON");
        t.True(targetDoc.layers[0].Behaviour is DrawingLayerBehaviour, "Internal image pastes as Drawing rather than JSON layers");
        var pasted = Stored(targetDoc.layers[0]);
        ColorNear(t, copied[0], pasted.GetPixel(1, 1), .003f, "Paste retains original canvas placement, alpha and HDR");
        t.Equal(0f, pasted.GetPixel(0, 0).a, "Paste does not fill unselected pixels");
        linear.SetPixels(Enumerable.Repeat(Color.clear, 12).ToArray()); linear.Apply(false, false);
        t.True(pasted.GetPixel(1, 1).r > 1 && copied[0].r > 1, "Copy and paste are independent of subsequent source edits");

        linear.SetPixels(colors); linear.Apply(false, false); Call(drawing, "InvalidatePaintSurface");
        sourceDoc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.blue })); Call(sourceDoc, "NormalizeModel");
        clipboard.EnsureCurrent(); clipboard.PublicationAttempted = true; Call(source, "CopyAreaSelection", true);
        area = Read(typeof(WhimTexWindow), "areaClipboard"); clipboard.Track((Guid)Read(area, "token"), (uint)Read(area, "systemRevision"));
        copied = (Color[])Read(area, "pixels");
        t.Near(1.5f, copied[0].r, .01, "Copy Merged composites the selected source over lower layers");
        t.Near(.34375f, copied[0].b, .01, "Copy Merged includes lower-layer blue");
        state.Close(source);
        var centered = state.Window(6, 5); var centeredDoc = (WhimTexDocument)Read(centered, "activeDocument");
        Call(centered, "PasteAreaSelection");
        ColorNear(t, copied[0], Stored(centeredDoc.layers[0]).GetPixel(2, 2), .01f, "Clipboard survives source closure and centers on another canvas size");

        // Another application's image has no WhimTex ownership token.
        clipboard.PublishExternal(image);
        int before = targetDoc.layers.Count; Call(target, "PasteAreaSelection");
        t.Equal(before + 1, targetDoc.layers.Count, "External clipboard change invalidates the internal HDR snapshot");
        t.True(Read(typeof(WhimTexWindow), "areaClipboard") == null, "External clipboard change drops stale area pixels");
        t.Equal(LayerColorRange.Standard, targetDoc.layers[0].colorRange, "External image uses SDR color range");
        t.True(Stored(targetDoc.layers[0]).GetPixels32().SequenceEqual(input), "External replacement image is pasted instead of stale HDR pixels");
    }

    sealed class LocalState
    {
        readonly List<Object> owned = new List<Object>();
        readonly object area, layerSnapshot, layerMarker, layerRevision;
        readonly Type layers = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.LayerClipboard", true);
        readonly bool areaWasCurrent, layersWereCurrent;
        readonly RenderTexture active = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        readonly EditorWindow focus = EditorWindow.focusedWindow;
        readonly Object[] selected = Selection.objects;
        readonly Dictionary<string, (bool exists, string value)> prefs = new Dictionary<string, (bool, string)>();
        internal LocalState(uint revision)
        {
            area = Read(typeof(WhimTexWindow), "areaClipboard");
            layerSnapshot = Read(layers, "snapshot"); layerMarker = Read(layers, "marker"); layerRevision = Read(layers, "revision");
            areaWasCurrent = area != null && (uint)Read(area, "systemRevision") == revision &&
                (bool)Call(Clipboard, "IsCurrentImage", (Guid)Read(area, "token"), revision);
            layersWereCurrent = layerSnapshot != null && (uint)layerRevision == revision && (string)layerMarker == GUIUtility.systemCopyBuffer;
            foreach (string key in new[] { "DCFApixels.WhimTex.Canvas.PaintToolSettings", "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool" })
                prefs[key] = (EditorPrefs.HasKey(key), EditorPrefs.GetString(key));
            Set(typeof(WhimTexWindow), "areaClipboard", null); Set(layers, "snapshot", null); Set(layers, "marker", null);
        }
        internal T Own<T>(T value) where T : Object { if (value != null) value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        internal WhimTexWindow Window(int width, int height)
        {
            var window = Own(ScriptableObject.CreateInstance<WhimTexWindow>());
            var doc = (WhimTexDocument)Read(window, "activeDocument"); doc.width = width; doc.height = height; doc.outputFilter = FilterMode.Point;
            return window;
        }
        internal void Close(WhimTexWindow window)
        {
            var doc = (WhimTexDocument)Read(window, "activeDocument");
            foreach (Layer layer in doc.layers)
                if (layer?.Behaviour is DrawingLayerBehaviour) { var texture = Stored(layer); if (texture != null) Undo.ClearUndo(texture); }
            Undo.ClearUndo(doc); Undo.ClearUndo(window); window.DiscardChanges(); Object.DestroyImmediate(window); owned.Remove(window);
        }
        internal void DisposeOwned()
        {
            var errors = new List<Exception>();
            foreach (Object obj in owned.ToArray().Reverse())
                try { if (obj is WhimTexWindow window && window != null) Close(window); else if (obj != null) { Undo.ClearUndo(obj); Object.DestroyImmediate(obj); } }
                catch (Exception error) { errors.Add(error); }
            owned.Clear();
            try { Call(layers, "Clear"); } catch (Exception error) { errors.Add(error); }
            RenderTexture.active = active; GL.sRGBWrite = srgb; Selection.objects = selected;
            foreach (var item in prefs) { if (item.Value.exists) EditorPrefs.SetString(item.Key, item.Value.value); else EditorPrefs.DeleteKey(item.Key); }
            if (focus != null && EditorWindow.focusedWindow != focus) focus.Focus();
            if (errors.Count > 0) throw new AggregateException("Owned clipboard fixture cleanup failed", errors);
        }
        internal void RestoreBorrowed(uint? restoredRevision)
        {
            Set(typeof(WhimTexWindow), "areaClipboard", area);
            Set(layers, "snapshot", layerSnapshot); Set(layers, "marker", layerMarker); Set(layers, "revision", layerRevision);
            if (!restoredRevision.HasValue) return;
            if (areaWasCurrent && (bool)Call(Clipboard, "IsCurrentImage", (Guid)Read(area, "token"), restoredRevision.Value))
                Set(area, "systemRevision", restoredRevision.Value);
            if (layersWereCurrent) Set(layers, "revision", restoredRevision.Value);
        }
    }
    sealed class ClipboardChanged : Exception { }

    // All clipboard bytes stay in memory. Unknown handle types and oversized captures skip before mutation.
    sealed class NativeClipboard : IDisposable
    {
        const int MaximumCaptureBytes = 4 * 1024 * 1024, MaximumFormats = 64;
        readonly List<(uint format, byte[] bytes)> original = new List<(uint, byte[])>();
        IntPtr owner;
        uint ownedRevision, ownedFormat;
        byte[] ownedPayload;
        bool restoring;
        readonly List<uint> restoredFormats = new List<uint>();
        internal uint OriginalRevision;
        bool publicationAttempted, unconfirmedPublication;
        internal bool PublicationAttempted { get => publicationAttempted; set { publicationAttempted = value; if (value) unconfirmedPublication = true; } }
        internal bool RecoveryRequired;
        internal static uint Sequence => GetClipboardSequenceNumber();
        internal static bool TryCapture(out NativeClipboard value, out string reason)
        {
            value = new NativeClipboard(); reason = null;
            try
            {
                value.owner = CreateWindowEx(0, "STATIC", "WhimTex Clipboard Fixture", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (value.owner == IntPtr.Zero) throw new InvalidOperationException("A temporary clipboard owner could not be created.");
                value.Open();
                try
                {
                    value.OriginalRevision = Sequence; var formats = Formats(); int total = 0;
                    foreach (uint format in formats)
                    {
                        if (format == 2 || format == 3 || format == 9 || format == 10 || format == 14 ||
                            (format >= 0x80 && format <= 0x8E) || (format >= 0x200 && format <= 0x3FF) || OleFormat(format))
                            throw new InvalidOperationException("An existing clipboard format has an unsupported non-memory handle.");
                        byte[] bytes = Bytes(format, MaximumCaptureBytes - total); total += bytes.Length;
                        value.original.Add((format, bytes));
                    }
                    if (Sequence != value.OriginalRevision || !formats.SequenceEqual(Formats()))
                        throw new InvalidOperationException("Clipboard formats or revision changed while capturing them.");
                }
                finally { CloseClipboard(); }
                return true;
            }
            catch (Exception error)
            {
                reason = "Native clipboard checks skipped before mutation: " + error.Message;
                value.Dispose(); value = null; return false;
            }
        }
        internal void EnsureCurrent()
        {
            if (Sequence != (ownedPayload == null ? OriginalRevision : ownedRevision)) throw new ClipboardChanged();
        }
        internal void Track(Guid token, uint revision)
        {
            ownedFormat = RegisterClipboardFormat("WhimTex.Selection");
            ownedPayload = new byte[24]; Array.Copy(BitConverter.GetBytes(0x534D4857u), ownedPayload, 4);
            Array.Copy(BitConverter.GetBytes(1u), 0, ownedPayload, 4, 4); Array.Copy(token.ToByteArray(), 0, ownedPayload, 8, 16);
            ownedRevision = revision;
            Open(); try { if (!CurrentInside()) throw new ClipboardChanged(); unconfirmedPublication = false; } finally { CloseClipboard(); }
        }
        bool CurrentInside()
        {
            if (Sequence != ownedRevision) return false;
            if (restoring)
                return GetClipboardOwner() == owner && original.Take(restoredFormats.Count)
                    .All(item => item.bytes.SequenceEqual(Bytes(item.format, MaximumCaptureBytes)));
            if (!IsClipboardFormatAvailable(ownedFormat)) return false;
            byte[] actual = Bytes(ownedFormat, 256);
            return actual.Length >= ownedPayload.Length && actual.Take(ownedPayload.Length).SequenceEqual(ownedPayload);
        }
        internal bool HasFormat(string name) => HasFormat(RegisterClipboardFormat(name));
        internal bool HasFormat(uint format) { EnsureCurrent(); return IsClipboardFormatAvailable(format); }
        internal void AppendText(string text)
        {
            EnsureCurrent(); Open(); bool written = false;
            try
            {
                if (!CurrentInside()) throw new ClipboardChanged();
                Write(13, Encoding.Unicode.GetBytes(text + "\0")); written = true;
            }
            finally { CloseClipboard(); if (written) ownedRevision = Sequence; }
        }
        internal void PublishExternal(Texture2D image)
        {
            EnsureCurrent(); byte[] png = image.EncodeToPNG(), payload = Guid.NewGuid().ToByteArray();
            uint fixtureFormat = RegisterClipboardFormat("WhimTex.Tests.ImageClipboard");
            Open(); bool written = false;
            try
            {
                if (!CurrentInside()) throw new ClipboardChanged();
                unconfirmedPublication = true;
                if (!EmptyClipboard()) throw new InvalidOperationException("Cannot replace the owned clipboard.");
                Write(RegisterClipboardFormat("PNG"), png); Write(fixtureFormat, payload);
                ownedFormat = fixtureFormat; ownedPayload = payload; written = true;
            }
            finally { CloseClipboard(); if (written) { ownedRevision = Sequence; unconfirmedPublication = false; } }
        }
        internal bool Restore(TestContext t)
        {
            if (ownedPayload == null && !PublicationAttempted)
            {
                bool unchanged = Sequence == OriginalRevision;
                if (unchanged) t.True(true, "An untouched clipboard retains its original revision");
                return unchanged;
            }
            RecoveryRequired = true;
            Open();
            var handles = new List<IntPtr>();
            try
            {
                if (ownedPayload == null)
                {
                    if (Sequence == OriginalRevision)
                    {
                        t.True(true, "A failed publication left the original clipboard revision unchanged");
                        RecoveryRequired = false; return true;
                    }
                    throw new InvalidOperationException("Clipboard publication failed before ownership could be confirmed; original data remains captured in fixture memory.");
                }
                if (!CurrentInside())
                {
                    // A failed write may have replaced the old token. Keep its original capture for explicit recovery.
                    RecoveryRequired = unconfirmedPublication || restoring;
                    return false;
                }
                foreach (var item in original) handles.Add(Allocate(item.bytes));
                if (!EmptyClipboard()) throw new InvalidOperationException("Cannot restore the captured clipboard.");
                restoring = true; restoredFormats.Clear(); ownedRevision = Sequence;
                for (int i = 0; i < original.Count; i++)
                {
                    if (SetClipboardData(original[i].format, handles[i]) == IntPtr.Zero)
                        throw new InvalidOperationException("Clipboard fixture restoration transfer failed.");
                    handles[i] = IntPtr.Zero; restoredFormats.Add(original[i].format); ownedRevision = Sequence;
                }
                t.True(original.Select(item => item.format).OrderBy(format => format).SequenceEqual(Formats().OrderBy(format => format)), "Clipboard restoration preserves every original format");
                foreach (var item in original) t.True(item.bytes.SequenceEqual(Bytes(item.format, MaximumCaptureBytes)), "Clipboard restoration preserves an original format's bytes");
                RecoveryRequired = false; return true;
            }
            finally
            {
                foreach (IntPtr handle in handles) if (handle != IntPtr.Zero) GlobalFree(handle);
                CloseClipboard(); if (restoring) ownedRevision = Sequence;
            }
        }
        void Open() { if (!OpenClipboard(owner)) throw new InvalidOperationException("Clipboard is busy during fixture ownership checks."); }
        static List<uint> Formats()
        {
            var formats = new List<uint>(); uint previous = 0;
            while (true)
            {
                SetLastError(0); uint next = EnumClipboardFormats(previous);
                if (next == 0) { if (Marshal.GetLastWin32Error() != 0) throw new InvalidOperationException("Cannot enumerate clipboard formats."); break; }
                if (formats.Count == MaximumFormats || formats.Contains(next)) throw new InvalidOperationException("Clipboard format enumeration exceeded its limit.");
                formats.Add(next); previous = next;
            }
            return formats;
        }
        static byte[] Bytes(uint format, int limit)
        {
            IntPtr handle = GetClipboardData(format); if (handle == IntPtr.Zero) throw new InvalidOperationException("Clipboard data is unavailable.");
            ulong size = GlobalSize(handle).ToUInt64(); if (size == 0 || size > (ulong)Math.Max(0, limit)) throw new InvalidOperationException("Clipboard data is not a bounded memory block.");
            IntPtr memory = GlobalLock(handle); if (memory == IntPtr.Zero) throw new InvalidOperationException("Clipboard memory cannot be accessed.");
            try { var bytes = new byte[(int)size]; Marshal.Copy(memory, bytes, 0, bytes.Length); return bytes; }
            finally { GlobalUnlock(handle); }
        }
        static void Write(uint format, byte[] bytes)
        {
            IntPtr handle = Allocate(bytes);
            try
            {
                if (SetClipboardData(format, handle) == IntPtr.Zero) throw new InvalidOperationException("Clipboard fixture data transfer failed.");
                handle = IntPtr.Zero;
            }
            finally { if (handle != IntPtr.Zero) GlobalFree(handle); }
        }
        static IntPtr Allocate(byte[] bytes)
        {
            IntPtr handle = GlobalAlloc(0x42u, (UIntPtr)(uint)bytes.Length);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Clipboard fixture memory allocation failed.");
            try
            {
                IntPtr memory = GlobalLock(handle); if (memory == IntPtr.Zero) throw new InvalidOperationException("Clipboard fixture memory lock failed.");
                try { Marshal.Copy(bytes, 0, memory, bytes.Length); } finally { GlobalUnlock(handle); }
                return handle;
            }
            catch { GlobalFree(handle); throw; }
        }
        static bool OleFormat(uint format)
        {
            if (format < 0xC000) return false;
            var name = new StringBuilder(256); GetClipboardFormatName(format, name, name.Capacity);
            string value = name.ToString();
            return value.StartsWith("Ole ", StringComparison.OrdinalIgnoreCase) ||
                new[] { "DataObject", "Link Source", "Embed Source", "Embedded Object", "Object Descriptor", "Link Source Descriptor", "OwnerLink", "ObjectLink", "FileContents", "FileGroupDescriptor", "FileGroupDescriptorW" }
                    .Any(item => value.Equals(item, StringComparison.OrdinalIgnoreCase));
        }
        public void Dispose() { if (owner != IntPtr.Zero) { DestroyWindow(owner); owner = IntPtr.Zero; } }
        [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll")] static extern bool EmptyClipboard();
        [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", SetLastError = true)] static extern uint EnumClipboardFormats(uint format);
        [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClipboardFormatName(uint format, StringBuilder name, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint extended, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr owner);
        [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
        [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
    }
}
