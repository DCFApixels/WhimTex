using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerRingSmoke
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static WhimTexColorPicker Picker() => Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x => x.name == "Color picker layout test");

    public static string Verify()
    {
        var picker = Picker();
        var ring = picker.rootVisualElement.Q(className: "whimtex-picker-ring");
        if (!(ring is ImmediateModeElement)) throw new Exception("Ring is not an ImmediateModeElement");
        var field = ring.GetType().GetField("material", Fields);
        var material = (Material)field.GetValue(ring);
        if (material == null || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Ring shader unavailable");
        var parent = ring.parent;
        int index = parent.IndexOf(ring);
        ring.RemoveFromHierarchy();
        if (material != null || field.GetValue(ring) != null) throw new Exception("Detached ring retained its material");
        parent.Insert(index, ring);
        if ((Material)field.GetValue(ring) == null) throw new Exception("Reattached ring did not recreate its material");
        return "PASS: immediate renderer, supported shader, detach cleanup and reattach recreation.";
    }

    public static string Capture()
    {
        var picker = Picker();
        picker.rootVisualElement.Q("ring-capture")?.RemoveFromHierarchy();
        var probe = new CaptureProbe(picker) { name = "ring-capture", pickingMode = PickingMode.Ignore };
        probe.style.position = Position.Absolute;
        probe.style.left = 0; probe.style.top = 0; probe.style.width = 1; probe.style.height = 1;
        picker.rootVisualElement.Add(probe);
        picker.Repaint();
        return "Capture queued for the temporary picker repaint.";
    }

    public static string CaptureResult() => (string)Picker().rootVisualElement.Q("ring-capture").userData ?? "Pending repaint";

    public static string VerifyPlusCenter()
    {
        var tile = Picker().rootVisualElement.Q(className: "whimtex-picker-add-color");
        float scale = EditorGUIUtility.pixelsPerPoint;
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            image.LoadImage(File.ReadAllBytes("Temp/WhimTex/color-picker-ring.png"));
            var bounds = tile.worldBound;
            int x0 = Mathf.RoundToInt(bounds.xMin * scale), y0 = Mathf.RoundToInt(bounds.yMin * scale);
            int width = Mathf.RoundToInt(bounds.width * scale), height = Mathf.RoundToInt(bounds.height * scale);
            Vector2 grayMin = Vector2.one * float.PositiveInfinity, grayMax = Vector2.one * float.NegativeInfinity;
            Vector2 plusMin = grayMin, plusMax = grayMax;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                Color c = image.GetPixel(x0 + x, image.height - 1 - y0 - y);
                var p = new Vector2(x + .5f, y + .5f);
                if (c.r > .4f) { grayMin = Vector2.Min(grayMin, p); grayMax = Vector2.Max(grayMax, p); }
                if (c.r > .7f) { plusMin = Vector2.Min(plusMin, p); plusMax = Vector2.Max(plusMax, p); }
            }
            Vector2 delta = (plusMin + plusMax - grayMin - grayMax) * .5f;
            if (float.IsNaN(delta.x) || float.IsNaN(delta.y) || delta.magnitude > .51f)
                throw new Exception("Plus is not centered in the rendered gray fill: " + delta);
            return "PASS: rendered plus center minus gray-fill center = " + delta + " physical pixels.";
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }

    public static string ClipAndScale()
    {
        var picker = Picker();
        var ring = picker.rootVisualElement.Q(className: "whimtex-picker-ring");
        var parent = ring.parent;
        ring.RemoveFromHierarchy();
        var clip = new VisualElement { name = "ring-test-clip" };
        clip.style.position = Position.Absolute;
        clip.style.left = 0; clip.style.top = 0;
        clip.style.width = 220; clip.style.height = 110;
        clip.style.overflow = Overflow.Hidden;
        parent.Insert(0, clip);
        clip.Add(ring);
        ring.style.width = 220; ring.style.height = 220;
        ring.style.scale = new Scale(new Vector3(.75f, .75f, 1));
        picker.Repaint();
        return "Temporary ring scaled to 75% and clipped to its upper half; capture after layout. Cleanup closes this test window.";
    }

    sealed class CaptureProbe : ImmediateModeElement
    {
        readonly WhimTexColorPicker picker;
        bool captured;
        internal CaptureProbe(WhimTexColorPicker picker) { this.picker = picker; }
        protected override void ImmediateRepaint()
        {
            if (captured) return;
            captured = true;
            Texture2D image = null;
            try
            {
                var target = RenderTexture.active;
                int width = target != null ? target.width : Mathf.RoundToInt(picker.position.width * EditorGUIUtility.pixelsPerPoint);
                int height = target != null ? target.height : Mathf.RoundToInt(picker.position.height * EditorGUIUtility.pixelsPerPoint);
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                image.Apply(false);
                string path = Path.GetFullPath("Temp/WhimTex/color-picker-ring.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
                userData = path + " (" + width + "x" + height + ", target=" + (target != null ? target.name : "backbuffer") + ")";
            }
            catch (Exception e) { userData = e.ToString(); }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
