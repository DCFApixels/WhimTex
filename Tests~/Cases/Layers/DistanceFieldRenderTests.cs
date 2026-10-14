using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class DistanceFieldRenderTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Size = 128;

    public static string RunReconstructedCapture() => TestContext.Run("Captured false-ray raster: verified reconstruction modes", t =>
    {
        const int size = 512, crop = 320;
        var objects = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { objects.Add(value); return value; }
        RenderTexture previous = RenderTexture.active, input = null, output = null;
        bool srgb = GL.sRGBWrite;
        try
        {
            byte[] bytes = File.ReadAllBytes("Packages/com.dcfapixels.whimtex/Tests~/Fixtures/DistanceRaster/star-13-inner-0364-phase3-known-failure.png");
            using (var hash = System.Security.Cryptography.SHA256.Create())
                t.Equal("98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC",
                    BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), "Authentic original raster");
            var source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            t.True(source.LoadImage(bytes), "Load captured source");
            source.filterMode = FilterMode.Point;
            var sourcePixels = source.GetPixels32();
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = doc.height = size;
            input = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            GL.sRGBWrite = false; Graphics.Blit(source, input);
            var read = Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var sheet = Own(new Texture2D(crop * 3, crop * 3, TextureFormat.RGBA32, false, true));
            var contextType = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            object context = Activator.CreateInstance(contextType, doc, input, size, size, 1f, false, false, null);
            var modes = new[] { DistanceMetric.Contour, DistanceMetric.CornerFit, DistanceMetric.Spring };
            var records = new List<string> { "threshold,mode,renderMs,formerFalseRayNormalized" };
            Color[] rawContour = null;
            for (int column = 0; column < 3; column++) for (int row = 0; row < 3; row++)
            {
                byte threshold = new byte[] { 77, 128, 192 }[column];
                var sdf = new SDFLayerBehaviour { metric = modes[row], threshold = threshold, miterLimit = 34.49f,
                    maxDistanceNormalization = 16.3f, encoding = SDFLayerBehaviour.OutputEncoding.LinearData };
                doc.layers.Add(sdf.Owner);
                RenderTexture.active = input; GL.sRGBWrite = false;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                output = (RenderTexture)typeof(SDFLayerBehaviour).GetMethod("Render", Hidden).Invoke(sdf, new[] { context });
                clock.Stop();
                t.True(RenderTexture.active == input && !GL.sRGBWrite, "Reconstruction preserves GPU state");
                RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                var pixels = read.GetPixels();
                int invalid = 0, badSigns = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color p = pixels[i];
                    if (!float.IsFinite(p.r) || p.r < 0 || p.r > 1 || Math.Abs(p.a - 1) > .001) invalid++;
                    // Reconstruction may legitimately move the zero contour. Its
                    // membership must still agree outside the bounded recovery band.
                    if (row == 0 || Math.Abs(rawContour[i].r - .5f) * 32.6f > 2.25f)
                        if (sourcePixels[i].a > threshold ? p.r > .5001f : p.r < .4999f) badSigns++;
                }
                t.Equal(0, invalid, "Finite normalized reconstruction " + modes[row] + "/" + threshold);
                t.Equal(0, badSigns, "Signs outside the recovery band " + modes[row] + "/" + threshold);
                if (row == 0) rawContour = pixels;
                t.Near(1, pixels[238 * size + 20].r, .002, "No former unrelated horizontal ray " + modes[row] + "/" + threshold);
                var tile = new Color[crop * crop];
                for (int y = 0; y < crop; y++) for (int x = 0; x < crop; x++) tile[y * crop + x] = pixels[(y + 92) * size + x + 85];
                sheet.SetPixels(column * crop, (2 - row) * crop, crop, crop, tile);
                records.Add(threshold + "," + modes[row] + "," + clock.Elapsed.TotalMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + pixels[238 * size + 20].r);
                RenderTexture.active = input; RenderTexture.ReleaseTemporary(output); output = null;
            }
            sheet.Apply();
            string directory = "Temp/WhimTex/distance-fields/reconstruction";
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "captured-contour-cornerfit-spring.png"), sheet.EncodeToPNG());
            File.WriteAllLines(Path.Combine(directory, "captured.csv"), records);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            if (input != null) RenderTexture.ReleaseTemporary(input);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    });

    // Support: Tests~/Framework/TestApi.cs. Six native renders; no borrowed document.
    // Columns: thresholds 77/128/192. Top: Round AA. Bottom: AAEDT + 8SED.
    // The authenticated PNG is input; the generated sheet is a diagnostic, not a golden.
    public static string RunAaedt() => TestContext.Run("Frozen fifth input: Round AA versus AAEDT + 8SED at thresholds 77/128/192", t =>
    {
        const int size = 512, crop = 320;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Budget() => t.True(clock.Elapsed.TotalSeconds < 30, "Six-render AAEDT comparison stays below 30 seconds");
        byte[] bytes = File.ReadAllBytes(Path.GetFullPath(
            "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/DistanceRaster/star-13-inner-0364-phase3-known-failure.png"));
        using (var hash = System.Security.Cryptography.SHA256.Create())
            t.Equal("98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC",
                BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), "Authentic frozen fifth input");
        var objects = new List<UnityEngine.Object>();
        T Own<T>(T item) where T : UnityEngine.Object { objects.Add(item); return item; }
        RenderTexture previous = RenderTexture.active, input = null, output = null;
        bool srgb = GL.sRGBWrite;
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            doc.width = doc.height = size;
            var source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            t.True(source.LoadImage(bytes), "Decode captured input");
            t.Equal(size, source.width, "Frozen width"); t.Equal(size, source.height, "Frozen height");
            source.filterMode = FilterMode.Point;
            var sourcePixels = source.GetPixels32();
            input = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            GL.sRGBWrite = false; Graphics.Blit(source, input);
            var read = Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var sheet = Own(new Texture2D(crop * 3, crop * 2, TextureFormat.RGBA32, false, true));
            var contextType = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            object context = Activator.CreateInstance(contextType, doc, input, size, size, 1f, false, false, null);
            var metrics = new List<string> { "threshold,topMode,bottomMode,changedPixels,maxNormalizedDelta" };
            int column = 0, calls = 0;
            foreach (byte threshold in new byte[] { 77, 128, 192 })
            {
                Color[] baseline = null;
                int row = 0;
                foreach (var mode in new[] { DistanceMetric.EuclideanAntialiased, DistanceMetric.AAEDT })
                {
                    Budget();
                    var sdf = new SDFLayerBehaviour { metric = mode, threshold = threshold,
                        maxDistanceNormalization = 16.3f, encoding = SDFLayerBehaviour.OutputEncoding.LinearData };
                    doc.layers.Add(sdf.Owner);
                    RenderTexture.active = input; GL.sRGBWrite = false;
                    output = (RenderTexture)typeof(SDFLayerBehaviour).GetMethod("Render", Hidden).Invoke(sdf, new[] { context });
                    calls++;
                    t.True(output != null && output.width == size && output.height == size, "Captured field renders: " + mode);
                    t.True(RenderTexture.active == input && !GL.sRGBWrite, "Captured render preserves caller GPU state");
                    RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    var pixels = read.GetPixels();
                    int invalid = 0, badSigns = 0, changed = 0;
                    float maximum = 0;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color p = pixels[i];
                        if (!float.IsFinite(p.r) || !float.IsFinite(p.g) || !float.IsFinite(p.b) || !float.IsFinite(p.a)
                            || p.r < 0 || p.r > 1 || Math.Abs(p.r - p.g) > .001f || Math.Abs(p.r - p.b) > .001f || Math.Abs(1 - p.a) > .001f) invalid++;
                        if (sourcePixels[i].a > threshold ? p.r > .5001f : p.r < .4999f) badSigns++;
                        if (baseline != null)
                        {
                            float delta = Math.Abs(p.r - baseline[i].r);
                            maximum = Math.Max(maximum, delta);
                            if (delta > .00001f) changed++;
                        }
                    }
                    t.Equal(0, invalid, "Finite opaque scalar render: " + mode + "/" + threshold);
                    t.Equal(0, badSigns, "Rendered sign agrees with frozen threshold mask: " + mode + "/" + threshold);
                    t.Near(1, pixels[238 * size + 20].r, .002, "Former false ray is outside the finite range: " + mode + "/" + threshold);
                    t.True(pixels[252 * size + 245].r < .5f, "Captured star retains its interior");
                    if (baseline == null) baseline = pixels;
                    else
                    {
                        t.True(changed > 0, "AAEDT is not an alias of Round AA on captured coverage");
                        metrics.Add(threshold + ",Round AA,AAEDT + 8SED," + changed + "," + maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    var tile = new Color[crop * crop];
                    for (int y = 0; y < crop; y++) for (int x = 0; x < crop; x++) tile[y * crop + x] = pixels[(y + 92) * size + x + 85];
                    sheet.SetPixels(column * crop, (1 - row) * crop, crop, crop, tile);
                    RenderTexture.active = input; RenderTexture.ReleaseTemporary(output); output = null;
                    row++;
                    Budget();
                }
                column++;
            }
            t.Equal(6, calls, "Exactly six native comparison renders");
            sheet.Apply();
            string directory = Path.GetFullPath("Temp/WhimTex/distance-fields/aaedt");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "captured-round-aaedt-77-128-192.png"), sheet.EncodeToPNG());
            File.WriteAllLines(Path.Combine(directory, "captured-round-aaedt-77-128-192.csv"), metrics);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            if (input != null) RenderTexture.ReleaseTemporary(input);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
        Budget();
    });

    public static string RunMerge() => TestContext.Run("Captured SDF native merge comparison: Sharp/MTSDF, 0/1/2 pixels, thresholds 77/128/192", t =>
    {
        const int size=512, crop=320;
        const string sourcePath="Packages/com.dcfapixels.whimtex/Tests~/Fixtures/DistanceRaster/star-13-inner-0364-phase3-known-failure.png";
        byte[] bytes=File.ReadAllBytes(Path.GetFullPath(sourcePath));
        using (var hash=System.Security.Cryptography.SHA256.Create())
            t.Equal("98BFDEDC59C261EDBAD7888C35500A3FAFC143A476C0118D7599274879BD1ACC",
                BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), "Frozen source is authentic, not an output golden");
        var objects=new List<UnityEngine.Object>();
        T Own<T>(T o) where T:UnityEngine.Object { objects.Add(o); return o; }
        RenderTexture previous=RenderTexture.active, output=null, input=null;
        bool srgb=GL.sRGBWrite;
        try
        {
            var doc=Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width=doc.height=size;
            var source=Own(new Texture2D(2,2,TextureFormat.RGBA32,false,true));
            t.True(source.LoadImage(bytes),"Decode frozen source");
            t.Equal(size,source.width,"Captured width"); t.Equal(size,source.height,"Captured height");
            source.filterMode=FilterMode.Point;
            input=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            GL.sRGBWrite=false; Graphics.Blit(source,input);
            var read=Own(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var sheet=Own(new Texture2D(crop*3,crop*2,TextureFormat.RGBA32,false,true));
            var contextType=typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext",true);
            object context=Activator.CreateInstance(contextType,doc,input,size,size,1f,false,false,null);
            var metrics=new List<string>{"threshold,mode,mergePixels,changedPixels,maxNormalizedDelta"};
            string directory=Path.GetFullPath("Temp/WhimTex/distance-fields/merge"); Directory.CreateDirectory(directory);
            foreach(byte threshold in new byte[]{77,128,192})
            {
            int row=0;
            foreach (var mode in new[]{DistanceMetric.Sharp,DistanceMetric.MTSDF})
            {
                Color[] baseline=null;
                for(int merge=0;merge<=2;merge++)
                {
                    var sdf=new SDFLayerBehaviour { metric=mode,contourMergeDistance=merge,miterLimit=34.49f,
                        threshold=threshold,maxDistanceNormalization=16.3f,encoding=SDFLayerBehaviour.OutputEncoding.LinearData };
                    doc.layers.Add(sdf.Owner);
                    RenderTexture.active=input; GL.sRGBWrite=false;
                    output=(RenderTexture)typeof(SDFLayerBehaviour).GetMethod("Render",Hidden).Invoke(sdf,new[]{context});
                    t.True(RenderTexture.active==input && !GL.sRGBWrite,"Merge render preserves caller state");
                    RenderTexture.active=output; read.ReadPixels(new Rect(0,0,size,size),0,0);
                    var pixels=read.GetPixels();
                    int changed=0; float maximum=0;
                    for(int i=0;i<pixels.Length;i++)
                    {
                        t.True(float.IsFinite(pixels[i].r) && pixels[i].r>=0 && pixels[i].r<=1,"Finite rendered merge field");
                        if(baseline!=null) { float delta=Math.Abs(pixels[i].r-baseline[i].r); maximum=Math.Max(maximum,delta); if(delta>.00001f) changed++; }
                    }
                    t.Near(1,pixels[238*size+20].r,.002,"Former false horizontal ray stays absent: "+mode+"/"+merge+"/"+threshold);
                    if(merge==0) baseline=pixels;
                    var tile=new Color[crop*crop];
                    for(int y=0;y<crop;y++) for(int x=0;x<crop;x++) tile[y*crop+x]=pixels[(y+92)*size+x+85];
                    sheet.SetPixels(merge*crop,(1-row)*crop,crop,crop,tile);
                    metrics.Add(threshold+","+mode+","+merge+","+changed+","+maximum.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    RenderTexture.active=input; RenderTexture.ReleaseTemporary(output); output=null;
                }
                row++;
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(directory,"captured-merge-0-1-2"+(threshold==128?"":"-threshold-"+threshold)+".png"),sheet.EncodeToPNG());
            }
            File.WriteAllLines(Path.Combine(directory,"captured-merge-0-1-2.csv"),metrics);
        }
        finally
        {
            RenderTexture.active=previous; GL.sRGBWrite=srgb;
            if(output!=null) RenderTexture.ReleaseTemporary(output);
            if(input!=null) RenderTexture.ReleaseTemporary(input);
            for(int i=objects.Count-1;i>=0;i--) if(objects[i]!=null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    });

    public static string Run() => TestContext.Run("Ten distance modes: native render and data output", t =>
    {
        var objects = new List<UnityEngine.Object>();
        T Own<T>(T item) where T : UnityEngine.Object { objects.Add(item); return item; }
        var previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        var input = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        RenderTexture rendered = null;
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            doc.width = doc.height = Size;
            var source = Own(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var read = Own(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true));
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                float alpha = 0;
                // Rotated polygon, ring, acute triangle, and concave cut. Supersampling is
                // an independent coverage source, not an expected distance implementation.
                for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                {
                    float px = x + (sx + .5f) / 4 - .5f, py = y + (sy + .5f) / 4 - .5f;
                    float dx = px - 37, dy = py - 37;
                    float u = .8660254f * dx + .5f * dy, v = -.5f * dx + .8660254f * dy;
                    float r = Mathf.Sqrt((px - 94) * (px - 94) + (py - 36) * (py - 36));
                    bool square = Mathf.Abs(u) < 17 && Mathf.Abs(v) < 17;
                    bool ring = r < 18 && r > 9;
                    bool triangle = py > 80 && py < 116 && Mathf.Abs(px - 35) < (116 - py) * .4f;
                    bool concave = px > 76 && px < 110 && py > 78 && py < 110 && !(px > 88 && py > 90);
                    if (square || ring || triangle || concave) alpha += 1f / 16;
                }
                pixels[y * Size + x] = new Color(1, 1, 1, alpha);
            }
            source.SetPixels(pixels); source.Apply();
            GL.sRGBWrite = false; Graphics.Blit(source, input);
            var contextType = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            object context = Activator.CreateInstance(contextType, doc, input, Size, Size, 1f, false, false, null);
            var sheet = Own(new Texture2D(Size * 10, Size * 2, TextureFormat.RGBA32, false, true));
            int column = 0;
            foreach (DistanceMetric mode in Enum.GetValues(typeof(DistanceMetric)))
            {
                var outline = new OutlineLayerBehaviour { metric = mode, outlineWidth = 7, outlineSoftness = 0,
                    fillCenter = true, outlineColor = new Color(.1f, .65f, 1, 1), fillColor = new Color(.7f, .7f, .7f, 1) };
                doc.layers.Add(outline.Owner);
                RenderTexture.active = input; GL.sRGBWrite = false;
                rendered = (RenderTexture)typeof(OutlineLayerBehaviour).GetMethod("Render", Hidden).Invoke(outline, new[] { context });
                t.True(rendered != null && rendered.width == Size, "Outline renders: " + mode);
                t.True(RenderTexture.active == input && !GL.sRGBWrite, "Outline preserves caller GPU state: " + mode);
                RenderTexture.active = rendered; read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                var actual = read.GetPixels();
                int partial = 0;
                for (int i = 0; i < actual.Length; i++)
                {
                    for (int c = 0; c < 4; c++) t.True(float.IsFinite(actual[i][c]), "Finite Outline channel: " + mode);
                    if (actual[i].a > .01f && actual[i].a < .99f) partial++;
                    actual[i] = Color.Lerp(new Color(.025f, .025f, .025f, 1), new Color(actual[i].r, actual[i].g, actual[i].b, 1), actual[i].a);
                }
                t.True(partial > 40, "Crisp Outline still has subpixel coverage: " + mode);
                sheet.SetPixels(column * Size, Size, Size, Size, actual);
                RenderTexture.active = input; RenderTexture.ReleaseTemporary(rendered); rendered = null;

                var sdf = new SDFLayerBehaviour { metric = mode, encoding = SDFLayerBehaviour.OutputEncoding.LinearData,
                    maxDistanceNormalization = 12 };
                doc.layers.Add(sdf.Owner);
                rendered = (RenderTexture)typeof(SDFLayerBehaviour).GetMethod("Render", Hidden).Invoke(sdf, new[] { context });
                t.True(RenderTexture.active == input && !GL.sRGBWrite, "SDF preserves caller GPU state: " + mode);
                RenderTexture.active = rendered; read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                actual = read.GetPixels();
                foreach (Color color in actual)
                {
                    t.True(float.IsFinite(color.r) && color.r >= 0 && color.r <= 1, "Finite remapped SDF: " + mode);
                    t.Near(1, color.a, .001, "Scalar SDF output is opaque: " + mode);
                }
                sheet.SetPixels(column * Size, 0, Size, Size, actual);
                RenderTexture.active = input; RenderTexture.ReleaseTemporary(rendered); rendered = null;
                column++;
            }
            t.Equal(10, column, "Exactly ten artist modes");
            sheet.Apply();
            string directory = Path.Combine(Application.dataPath, "../Temp/WhimTex/distance-fields");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "ten-modes.png"), sheet.EncodeToPNG());

            var mtsdf = new SDFLayerBehaviour { metric = DistanceMetric.MTSDF, encoding = SDFLayerBehaviour.OutputEncoding.MultiChannelData,
                maxDistanceNormalization = 12, inverted = true, contourOffset = 5,
                distancePosition = SDFLayerBehaviour.DistancePosition.Inside, profile = AnimationCurve.Linear(0, 0, 1, 0) };
            doc.layers.Add(mtsdf.Owner);
            RenderTexture.active = input;
            rendered = (RenderTexture)typeof(SDFLayerBehaviour).GetMethod("Render", Hidden).Invoke(mtsdf, new[] { context });
            RenderTexture.active = rendered; read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            var channels = read.GetPixels();
            int different = 0, distanceAlpha = 0;
            foreach (Color color in channels)
            {
                if (Mathf.Abs(color.r - color.g) > .002f || Mathf.Abs(color.g - color.b) > .002f) different++;
                if (color.a > .01f && color.a < .99f) distanceAlpha++;
                for (int c = 0; c < 4; c++) t.True(float.IsFinite(color[c]) && color[c] >= 0 && color[c] <= 1, "Normalized linear MTSDF channel");
            }
            t.True(different > 100, "MTSDF has genuinely different RGB channels, not replicated grayscale");
            t.True(distanceAlpha > 100, "MTSDF alpha stores signed distance, not opacity");
            t.True(channels[37 * Size + 37].a < .5f, "MTSDF bypasses artistic inversion/profile/position");
            CheckComposition(t, source, objects);
            CheckMtsdfStarFalloff(t, objects);
            var shader = Shader.Find("Hidden/WhimTex/SdfGradient");
            t.True(shader != null && !ShaderUtil.ShaderHasError(shader), "SDF shader compiles");
            CheckControls(t, doc);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            RenderTexture.ReleaseTemporary(input);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        }
    });

    static void CheckMtsdfStarFalloff(TestContext t, List<UnityEngine.Object> owned)
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        owned.Add(doc); doc.width = doc.height = 512;
        var star = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Star,
            sides = 5, innerRadius = .5f, fill = true, edgeMode = ShapeLayerBehaviour.EdgeMode.Antialiased };
        star.transform.positionF = new Vector2(-105.23746f, 41.058594f);
        star.transform.scaleF = Vector2.one * .35042733f;
        doc.layers.Add(star.Owner);
        var initialized = doc.ComposeCanvas();
        UnityEngine.Object.DestroyImmediate(initialized);
        var sdf = new SDFLayerBehaviour { metric = DistanceMetric.MTSDF,
            encoding = SDFLayerBehaviour.OutputEncoding.LinearData, maxDistanceNormalization = 32.6f,
            inputMode = EffectInputMode.Specific, TargetLayerId = star.Owner.Id };
        doc.layers.Insert(0, sdf.Owner);
        foreach (DistanceMetric mode in Enum.GetValues(typeof(DistanceMetric)))
        foreach (byte threshold in new byte[] { 64, 96, 117, 118, 119, 128, 143, 160, 192, 224, 228 })
        {
            sdf.metric = mode;
            sdf.threshold = threshold;
            var canvas = doc.ComposeCanvas();
            try
            {
                var pixels = canvas.GetPixels();
                double farError = 0;
                for (int y = 0; y < 512; y++) for (int x = 400; x < 512; x++)
                    farError = Math.Max(farError, Math.Abs(1 - pixels[y * 512 + x].r));
                t.Near(0, farError, .002, $"Native {mode} star has no distant false gradient; threshold={threshold}");
                t.True(canvas.EncodeToPNG().Length > 200, "Native star regression remains exportable: " + mode);
                if (mode == DistanceMetric.Bevel && threshold >= 117 && threshold <= 119)
                    t.Near(1, pixels[213 * 512 + 216].r, .002,
                        "Native Bevel facet exceeds the 32.6px range at the former overlapping-loop bulge; threshold=" + threshold);
                if ((mode == DistanceMetric.MTSDF || mode == DistanceMetric.Bevel) && threshold >= 128)
                {
                    // The native unrotated star is symmetric about x~=150.25. A diagonal
                    // cap at the upper tip previously made one side's exterior ramp much
                    // steeper. Check native composition, not a captured output golden.
                    for (int y = 395; y <= 405; y += 2)
                    for (int halfWidth = 5; halfWidth <= 10; halfWidth += 5)
                        t.Near(pixels[y * 512 + 150 - halfWidth].r, pixels[y * 512 + 150 + halfWidth].r, .018,
                            $"{mode} native star tip remains balanced at y={y}, halfWidth={halfWidth}, threshold={threshold}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }
    }

    static void CheckComposition(TestContext t, Texture2D source, List<UnityEngine.Object> owned)
    {
        foreach (DistanceMetric mode in Enum.GetValues(typeof(DistanceMetric)))
        {
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            owned.Add(doc); doc.width = doc.height = Size;
            var group = new GroupLayerBehaviour().Owner;
            group.enabled = false;
            group.children.Add(new FileLayerBehaviour { sourceTexture = source }.Owner);
            doc.layers.Add(group);
            var initialized = doc.ComposeCanvas();
            UnityEngine.Object.DestroyImmediate(initialized);
            t.True(!string.IsNullOrEmpty(group.Id), "Source IDs exist before assigning an explicit Target");
            var outline = new OutlineLayerBehaviour { metric = mode, outlineWidth = 6, fillCenter = true,
                inputMode = EffectInputMode.Specific, TargetLayerId = group.Id };
            var sdf = new SDFLayerBehaviour { metric = mode, encoding = SDFLayerBehaviour.OutputEncoding.LinearData,
                maxDistanceNormalization = 12, inputMode = EffectInputMode.Specific, TargetLayerId = group.Id };
            foreach (TargetedLayerBehaviour effect in new TargetedLayerBehaviour[] { outline, sdf })
            {
                doc.layers.Clear(); doc.layers.Add(effect.Owner); doc.layers.Add(group);
                Texture2D canvas = doc.ComposeCanvas();
                try
                {
                    int visible = 0;
                    foreach (Color p in canvas.GetPixels())
                    {
                        t.True(float.IsFinite(p.r) && float.IsFinite(p.a), "Finite group Target composition: " + mode);
                        if (p.a > .1f) visible++;
                    }
                    t.True(visible > 100, "Disabled group remains a usable explicit effect source: " + mode);
                    t.True(canvas.EncodeToPNG().Length > 200, "Composed effect is exportable: " + mode);
                }
                finally { UnityEngine.Object.DestroyImmediate(canvas); }
                var preview = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerPreview", Hidden)
                    .Invoke(doc, new object[] { effect.Owner, 64 });
                try { t.True(preview != null && preview.width == 64 && preview.height == 64, "Effect preview at reduced resolution: " + mode); }
                finally { if (preview != null) RenderTexture.ReleaseTemporary(preview); }
                effect.clippingMask = true;
                doc.layers.Insert(1, new ColorFillLayerBehaviour { color = new Color(1, 1, 1, .5f) }.Owner);
                canvas = doc.ComposeCanvas();
                try
                {
                    foreach (Color p in canvas.GetPixels())
                        t.Near(.5, p.a, .006, "Clipping retains soft base coverage: " + mode);
                }
                finally { UnityEngine.Object.DestroyImmediate(canvas); }
                effect.clippingMask = false;
                // Both behaviors were rendered even though only one is in the final list.
                typeof(SDFLayerBehaviour).GetMethod("ReleaseTransientResources", Hidden).Invoke(sdf, null);
            }
        }
    }

    static void CheckControls(TestContext t, WhimTexDocument doc)
    {
        var assembly = typeof(SDFLayerBehaviour).Assembly;
        var bindingsType = assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings", true);
        var previousFocus = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<DistanceFieldControlWindow>();
        window.hideFlags = HideFlags.HideAndDontSave;
        window.titleContent = new GUIContent("SDF Controls Test");
        try
        {
            window.Show();
            var root = window.rootVisualElement;
            object bindings = Activator.CreateInstance(bindingsType, true);
            int changes = 0;
            Action<string, Action> apply = (label, action) => { changes++; action(); };
            Action<VisualElement, TargetedLayerBehaviour> target = (element, behavior) => { };
            var sdf = new SDFLayerBehaviour();
            typeof(SDFLayerEditorWindow).GetMethod("BuildFields", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { root, sdf, doc, apply, bindings, target });
            var algorithm = root.Query<EnumField>().ToList().Find(field => field.label == "Distance Algorithm");
            var miter = root.Query<FloatField>().ToList().Find(field => field.label == "Miter Limit");
            var merge = root.Query<FloatField>().ToList().Find(field => field.label == "Contour Merge (px)");
            var output = root.Query<PopupField<SDFLayerBehaviour.OutputEncoding>>().ToList().Find(field => field.label == "Output");
            t.True(algorithm != null && miter != null && output != null, "Artist controls are built without a window");
            t.True(miter.ClassListContains("whimtex-hidden"), "Miter Limit is hidden for Round");
            t.True(merge != null && merge.ClassListContains("whimtex-hidden"), "Contour Merge is hidden for Round");
            t.Near(0, sdf.contourMergeDistance, 0, "Merge is disabled by default");
            t.Equal(2, output.choices.Count, "Round has only scalar and gradient outputs");
            Edit(algorithm, (Enum)DistanceMetric.Sharp);
            t.Equal(DistanceMetric.Sharp, sdf.metric, "Algorithm field edits the actual model");
            t.True(!miter.ClassListContains("whimtex-hidden"), "Miter Limit appears for Sharp");
            t.True(!merge.ClassListContains("whimtex-hidden"), "Contour Merge appears for Sharp");
            int mergeChanges = changes;
            Edit(merge, 1f);
            t.Near(1, sdf.contourMergeDistance, 0, "One-pixel merge edits model");
            t.Equal(mergeChanges + 1, changes, "Merge uses one change transaction");
            Edit(merge, 8f);
            t.Near(2, sdf.contourMergeDistance, 0, "Merge clamps at two pixels");
            Edit(merge, -1f);
            t.Near(0, sdf.contourMergeDistance, 0, "Negative merge clamps off");
            Edit(merge, 1.5f);
            Edit(miter, 8f);
            t.Near(8, sdf.miterLimit, 0, "Miter field edits the model");
            Edit(algorithm, (Enum)DistanceMetric.MTSDF);
            t.Equal(3, output.choices.Count, "MTSDF exposes Multi Channel Data");
            t.True(!merge.ClassListContains("whimtex-hidden"), "Contour Merge remains available for MTSDF");
            t.Near(1.5, sdf.contourMergeDistance, 0, "Algorithm switching retains merge value");
            Edit(output, SDFLayerBehaviour.OutputEncoding.MultiChannelData);
            t.Equal(SDFLayerBehaviour.OutputEncoding.MultiChannelData, sdf.encoding, "Data output edits the model");
            t.True(!root.Query<CurveField>().ToList().Find(field => field.label == "Profile").enabledSelf,
                "Data output disables artistic Profile");
            int before = changes;
            Edit(algorithm, (Enum)DistanceMetric.EuclideanAntialiased);
            t.Equal(SDFLayerBehaviour.OutputEncoding.LinearData, sdf.encoding, "Leaving MTSDF restores a valid scalar output");
            t.Equal(before + 1, changes, "Algorithm and output transition is one change transaction");
            t.Equal(2, output.choices.Count, "Non-MTSDF cannot select data encoding");
            t.True(merge.ClassListContains("whimtex-hidden"), "Round hides inactive merge");
            t.Near(1.5, sdf.contourMergeDistance, 0, "Inactive mode does not discard saved merge");
            before = changes;
            Edit(algorithm, (Enum)DistanceMetric.AAEDT);
            t.Equal(DistanceMetric.AAEDT, sdf.metric, "Algorithm control selects AAEDT");
            t.Equal(before + 1, changes, "AAEDT selection uses one change transaction");
            var aaedtLabel = typeof(DistanceMetric).GetField(nameof(DistanceMetric.AAEDT)).GetCustomAttribute<InspectorNameAttribute>();
            t.True(aaedtLabel != null && aaedtLabel.displayName == "AAEDT + 8SED", "AAEDT has the requested inspector label");
            t.True(miter.ClassListContains("whimtex-hidden") && merge.ClassListContains("whimtex-hidden"), "AAEDT hides inactive miter and merge");
            t.Near(8, sdf.miterLimit, 0, "AAEDT retains saved miter");
            t.Near(1.5, sdf.contourMergeDistance, 0, "AAEDT retains saved merge");
            t.Equal(2, output.choices.Count, "AAEDT offers only gradient and scalar output");
            Edit(algorithm, (Enum)DistanceMetric.MTSDF);
            Edit(output, SDFLayerBehaviour.OutputEncoding.MultiChannelData);
            before = changes;
            Edit(algorithm, (Enum)DistanceMetric.AAEDT);
            t.Equal(SDFLayerBehaviour.OutputEncoding.LinearData, sdf.encoding, "MTSDF to AAEDT restores valid scalar encoding");
            t.Equal(before + 1, changes, "MTSDF to AAEDT encoding transition is one transaction");
            t.True(root.Query<CurveField>().ToList().Find(field => field.label == "Profile").enabledSelf,
                "AAEDT scalar output re-enables Profile");
            Edit(algorithm, (Enum)DistanceMetric.MTSDF);
            Edit(output, SDFLayerBehaviour.OutputEncoding.MultiChannelData);
            before = changes;
            Edit(algorithm, (Enum)DistanceMetric.Contour);
            t.Equal(DistanceMetric.Contour, sdf.metric, "Algorithm control selects Contour AA");
            t.Equal(before + 1, changes, "Contour selection and encoding use one transaction");
            t.Equal(SDFLayerBehaviour.OutputEncoding.LinearData, sdf.encoding, "Contour restores scalar output");
            t.Equal(2, output.choices.Count, "Contour has no multichannel output");
            t.True(miter.ClassListContains("whimtex-hidden") && merge.ClassListContains("whimtex-hidden"), "Contour hides inactive miter and merge");
            t.Near(8, sdf.miterLimit, 0, "Contour preserves inactive miter");
            t.Near(1.5, sdf.contourMergeDistance, 0, "Contour preserves inactive merge");
            var contourLabel = typeof(DistanceMetric).GetField(nameof(DistanceMetric.Contour)).GetCustomAttribute<InspectorNameAttribute>();
            t.Equal("Contour AA", contourLabel.displayName, "Contour inspector label");
            foreach (var mode in new[] { DistanceMetric.CornerFit, DistanceMetric.Spring })
            {
                Edit(algorithm, (Enum)DistanceMetric.MTSDF);
                Edit(output, SDFLayerBehaviour.OutputEncoding.MultiChannelData);
                before = changes;
                Edit(algorithm, (Enum)mode);
                t.Equal(mode, sdf.metric, "New reconstruction mode selection");
                t.Equal(before + 1, changes, "Reconstruction mode change is one transaction");
                t.Equal(SDFLayerBehaviour.OutputEncoding.LinearData, sdf.encoding, "Reconstruction uses scalar output");
                t.True(!miter.ClassListContains("whimtex-hidden") && merge.ClassListContains("whimtex-hidden"), "Verified joins expose miter, not legacy merge");
                t.Equal(2, output.choices.Count, "Reconstruction has no multichannel encoding");
            }
        }
        finally
        {
            if (window != null)
            {
                window.DiscardChanges();
                window.Close();
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
            }
            if (previousFocus != null && EditorWindow.focusedWindow != previousFocus) previousFocus.Focus();
        }
    }

    static void Edit<T>(BaseField<T> field, T value)
    {
        // Use the public value-change event, without simulating physical input.
        using var change = ChangeEvent<T>.GetPooled(field.value, value);
        field.SetValueWithoutNotify(value);
        change.target = field;
        field.SendEvent(change);
    }
}

public sealed class DistanceFieldControlWindow : EditorWindow { }
