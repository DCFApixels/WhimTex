using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed class PsdExportReport
    {
        public int layerCount { get; internal set; }
        public int groupCount { get; internal set; }
        public int editableFillCount { get; internal set; }
        public int editableOutlineCount { get; internal set; }
        private readonly List<string> messages = new List<string>();
        public IReadOnlyList<string> notes => messages;
        internal void Note(Layer layer, string message) => messages.Add((layer.layerName ?? "Unnamed") + ": " + message);
    }

    public static class WhimTexPsdExporter
    {
        // Returns conversion notes; does not import the output or modify the source document.
        // progress may throw OperationCanceledException. The destination is replaced only on success.
        public static PsdExportReport Export(TextureCompositor document, string path, bool overwrite = false,
            Action<string, float> progress = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".psd", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a .psd destination.", nameof(path));
            if (document.width < 1 || document.height < 1 || document.width > 30000 || document.height > 30000)
                throw new InvalidOperationException("PSD supports canvas dimensions from 1 to 30000 pixels.");
            string destination = Path.GetFullPath(path);
            if (File.Exists(destination) && !overwrite) throw new IOException("The PSD destination already exists.");
            if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new DirectoryNotFoundException("The destination folder does not exist.");

            document.RefreshTransformHierarchy();
            var report = new PsdExportReport();
            var records = new List<PsdWriter.LayerRecord>();
            Collect(document, document.layers, records, report, new HashSet<Layer>(), new HashSet<uint>());
            Layer processor = FindProcessor(document.layers);
            if (processor != null)
            {
                // A stack processor is not an independent source-over layer in this format.
                // Retain editable sources in a hidden folder and show one faithful composite.
                records.Insert(0, new PsdWriter.LayerRecord { name = "</Group>", section = 3, visible = false });
                records.Add(new PsdWriter.LayerRecord { name = "Source Layers", section = 1, visible = false,
                    opacity = 255, blend = "norm", sectionBlend = "norm" });
                records.Add(new PsdWriter.LayerRecord { name = "Processed Result", visible = true, opacity = 255,
                    blend = "norm", openPixels = () => new Pixels(document.RenderPsdPixels(null), document.width, document.height) });
                report.groupCount++;
                report.layerCount++;
                report.Note(processor, "Stack processing is baked into Processed Result. Original layers and folders are preserved in the hidden Source Layers folder.");
            }
            if (records.Count > 32767) throw new InvalidOperationException("There are too many layers and folder dividers for PSD.");
            // A transparency-bearing layer record also identifies the merged alpha in an empty document.
            if (records.Count == 0)
                records.Add(new PsdWriter.LayerRecord { name = "Canvas", openPixels = () => new Pixels(null, document.width, document.height) });
            for (int i = 0; i < records.Count; i++)
            {
                PsdWriter.LayerRecord record = records[i];
                if (record.section != 0) continue;
                Func<PsdWriter.IPixels> open = record.openPixels;
                float fraction = 0.1f + 0.85f * i / records.Count;
                record.openPixels = () => { progress?.Invoke(record.name, fraction); return open(); };
            }

            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                progress?.Invoke("Rendering composite", 0f);
                using (var merged = new Pixels(document.RenderPsdPixels(null), document.width, document.height))
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    PsdWriter.Write(stream, document.width, document.height, records, merged);
                progress?.Invoke("Finishing PSD", 1f);
                if (File.Exists(destination))
                {
                    if (!overwrite) throw new IOException("The PSD destination was created during export.");
                    File.Replace(temporary, destination, null);
                }
                else File.Move(temporary, destination);
                return report;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static Layer FindProcessor(List<Layer> layers)
        {
            if (layers == null) return null;
            foreach (Layer layer in layers)
            {
                if (layer?.Behaviour is ShaderProcessorLayerBehaviour) return layer;
                if (layer?.AsGroup() is Layer group)
                {
                    Layer found = FindProcessor(group.layers);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static void Collect(TextureCompositor document, List<Layer> layers, List<PsdWriter.LayerRecord> records,
            PsdExportReport report, HashSet<Layer> visited, HashSet<uint> ids)
        {
            if (layers == null) return;
            // PSD records are bottom-to-top, with each folder bounded by two section records.
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                Layer layer = layers[i];
                if (layer == null || layer?.Behaviour is PendingLayerBehaviour) continue;
                if (!visited.Add(layer)) throw new InvalidOperationException("The layer tree contains a cycle or shared layer instance.");
                bool orphanClipping = layer.clippingMask && document.GetClippingBase(layer) == null;
                bool missingBehaviour = layer.Behaviour == null;
                if (missingBehaviour) report.Note(layer, "Layer behaviour is unavailable; exported hidden with its structure preserved.");
                if (orphanClipping) report.Note(layer, "Clipping has no base in this group; exported hidden to preserve its invisible result.");
                if (layer?.AsGroup() is Layer group)
                {
                    report.groupCount++;
                    records.Add(new PsdWriter.LayerRecord { name = "</Group>", id = LayerId(group, ids, ":end"), section = 3, visible = false });
                    bool bakedSwizzle = !missingBehaviour && (!group.swizzle.IsIdentity || group.HasModifiers);
                    if (bakedSwizzle)
                        records.Add(new PsdWriter.LayerRecord { name = "</Group>", id = LayerId(group, ids, ":source-end"), section = 3, visible = false });
                    Collect(document, group.layers, records, report, visited, ids);
                    if (bakedSwizzle)
                    {
                        records.Add(new PsdWriter.LayerRecord { name = "Source Layers", id = LayerId(group, ids, ":sources"),
                            section = 1, visible = false, opacity = 255, blend = "norm", sectionBlend = "norm" });
                        records.Add(new PsdWriter.LayerRecord { name = group.HasModifiers ? "FX Result" : "Swizzle Result", id = LayerId(group, ids, ":swizzle"),
                            visible = true, opacity = 255, blend = "norm",
                            openPixels = () => new Pixels(document.RenderPsdGroupContent(group), document.width, document.height) });
                        report.Note(group, "Group FX and Swizzle are baked into a child layer. Original children are preserved in the hidden Source Layers folder.");
                    }
                    bool isolated = !group.IsPassThrough || document.IsGroupIsolatedByClipping(group);
                    string groupBlend = isolated ? BlendKey(group.EffectiveBlendMode, out _) : "pass";
                    records.Add(new PsdWriter.LayerRecord { name = group.layerName, id = LayerId(group, ids), section = 1,
                        visible = group.enabled && !missingBehaviour && !orphanClipping && (!isolated || group.EffectiveBlendMode != BlendMode.None), opacity = ToByte(group.opacity),
                        blend = isolated ? groupBlend : "norm", sectionBlend = groupBlend, clipping = group.clippingMask });
                    if (isolated && group.blendRange == LayerBlendRange.HDR)
                        report.Note(group, "HDR group blending is approximated in the 8-bit layer stack; merged pixels are clamped.");
                    continue;
                }
                report.layerCount++;
                if (layer.colorRange == LayerColorRange.HDR || layer.blendRange == LayerBlendRange.HDR)
                    report.Note(layer, "HDR values are clamped for this 8-bit export. Extended blending may differ in the editable stack.");
                var record = new PsdWriter.LayerRecord
                {
                    name = layer.layerName,
                    id = LayerId(layer, ids),
                    opacity = ToByte(layer.opacity),
                    clipping = layer.clippingMask,
                    visible = layer.enabled && !missingBehaviour && !orphanClipping && layer.blendMode != BlendMode.None,
                    blend = BlendKey(layer.blendMode, out bool approximate),
                    openPixels = () => new Pixels(document.RenderPsdPixels(layer), document.width, document.height)
                };
                if (approximate) report.Note(layer, layer.blendMode + " is approximated by " + record.blend + "; the merged image retains the original result.");
                if (layer.blendMode == BlendMode.None) report.Note(layer, "No-op blend is represented by a hidden layer.");

                bool modifiers = HasModifiers(layer) || !layer.swizzle.IsIdentity;
                if (!layer.swizzle.IsIdentity) report.Note(layer, "Swizzle is baked into the layer pixels.");
                if (layer?.Behaviour is ColorFillLayerBehaviour fill && fill.mode == ColorFillLayerBehaviour.FillMode.Color && !modifiers)
                {
                    record.adjustment = true;
                    record.mask = true;
                    Add(record, "SoCo", new PsdWriter.Descriptor().Object("Clr ", Rgb(fill.color)));
                    record.openPixels = () => new Pixels(document.RenderPsdPixels(layer), document.width, document.height, alphaAsMask: true);
                    report.editableFillCount++;
                }
                else if (layer?.Behaviour is GradientLayerBehaviour gradient && CanExportGradient(gradient, modifiers))
                {
                    record.adjustment = true;
                    record.mask = gradient.transform.tiling == TransformTilingMode.Clip;
                    Add(record, "GdFl", Gradient(gradient, document.width, document.height));
                    record.openPixels = () => GradientPixels(document, gradient, record.mask);
                    report.editableFillCount++;
                    if (!gradient.Owner.CanvasTransform.IsIdentity() ||
                        (gradient.gradientType != GradientLayerBehaviour.GradientType.Horizontal && gradient.gradientType != GradientLayerBehaviour.GradientType.Vertical))
                        report.Note(layer, "Gradient geometry/interpolation is editable but may differ, especially on a non-square canvas.");
                }
                else if (layer?.Behaviour is OutlineLayerBehaviour outline && CanExportOutline(outline, modifiers))
                {
                    record.fillOpacity = 0;
                    Add(record, "lfx2", Stroke(outline));
                    record.openPixels = () => new Pixels(document.RenderPsdPixels(outline, effectInput: true), document.width, document.height);
                    report.editableOutlineCount++;
                    report.Note(layer, "Editable stroke on a snapshot of the target alpha, with Fill 0%. Target linkage, distance metric and softness are not retained.");
                }
                else if (!(layer?.Behaviour is DrawingLayerBehaviour) && !(layer?.Behaviour is FileLayerBehaviour))
                    report.Note(layer, "Rasterized with its transform and FX; no compatible editable representation for these settings.");
                else if (HasModifiers(layer))
                    report.Note(layer, "Shader/Material FX are baked into the layer pixels.");
                records.Add(record);
            }
        }

        private static bool HasModifiers(Layer layer)
        {
            if (layer.modifiers == null) return false;
            foreach (UnityEngine.Object modifier in layer.modifiers) if (modifier != null) return true;
            return false;
        }

        private static uint LayerId(Layer layer, HashSet<uint> used, string suffix = "")
        {
            string key = (string.IsNullOrEmpty(layer.Id) ? layer.layerName ?? "" : layer.Id) + suffix;
            uint id = 2166136261;
            unchecked { foreach (char c in key) id = (id ^ c) * 16777619; }
            id &= 0x7fffffff;
            while (id == 0 || !used.Add(id)) id = (id + 1) & 0x7fffffff;
            return id;
        }

        private static bool CanExportOutline(OutlineLayerBehaviour layer, bool modifiers) =>
            !modifiers && !layer.fillCenter && layer.outlineOffset == 0f &&
            layer.Owner.CanvasTransform.IsIdentity() && layer.outlineWidth > 0f && layer.outlineWidth <= 250f &&
            (layer.metric == DistanceMetric.EuclideanExact || layer.metric == DistanceMetric.EuclideanApproximate);

        private static bool CanExportGradient(GradientLayerBehaviour layer, bool modifiers)
        {
            if (modifiers || layer.Owner.CanvasTransform.storage == TransformStorage.Projective || layer.gradient == null || layer.gradient.Mode != WhimTexGradientMode.Classic ||
                layer.gradient.ColorSpace != ColorSpace.Gamma || layer.gradient.Smoothness != 0f ||
                (layer.Owner.CanvasTransform.tiling != TransformTilingMode.Clip && layer.Owner.CanvasTransform.tiling != TransformTilingMode.Source)) return false;
            bool linear = layer.gradientType == GradientLayerBehaviour.GradientType.Horizontal || layer.gradientType == GradientLayerBehaviour.GradientType.Vertical;
            Vector2 scale = layer.Owner.CanvasTransform.scaleF;
            if (Mathf.Abs(scale.x) < 0.00001f || Mathf.Abs(scale.y) < 0.00001f) return false;
            if (!linear && (scale.x <= 0 || !Mathf.Approximately(scale.x, scale.y))) return false;
            return layer.gradientType != GradientLayerBehaviour.GradientType.Circular || Mathf.Approximately(layer.circularRepetitions, 1f);
        }

        private static void Add(PsdWriter.LayerRecord layer, string key, PsdWriter.Descriptor value) =>
            layer.descriptors.Add(new KeyValuePair<string, PsdWriter.Descriptor>(key, value));

        private static PsdWriter.Descriptor Rgb(Color color) => new PsdWriter.Descriptor("RGBC")
            .Number("Rd  ", Mathf.Clamp01(color.r) * 255d)
            .Number("Grn ", Mathf.Clamp01(color.g) * 255d)
            .Number("Bl  ", Mathf.Clamp01(color.b) * 255d);

        private static PsdWriter.Descriptor Stroke(OutlineLayerBehaviour layer)
        {
            string position = layer.outlinePosition == OutlineLayerBehaviour.OutlinePosition.Inside ? "InsF" :
                layer.outlinePosition == OutlineLayerBehaviour.OutlinePosition.Center ? "CtrF" : "OutF";
            var stroke = new PsdWriter.Descriptor("FrFX").Bool("enab", true).Bool("present", true).Bool("showInDialog", true)
                .Enum("Styl", "FStl", position).Enum("PntT", "FrFl", "SClr").Enum("Md  ", "BlnM", "Nrml")
                .Unit("Opct", "#Prc", Mathf.Clamp01(layer.outlineColor.a) * 100d)
                .Unit("Sz  ", "#Pxl", layer.outlineWidth).Object("Clr ", Rgb(layer.outlineColor));
            return new PsdWriter.Descriptor().Unit("Scl ", "#Prc", 100).Bool("masterFXSwitch", true).Object("FrFX", stroke);
        }

        private static PsdWriter.Descriptor Gradient(GradientLayerBehaviour layer, int width, int height)
        {
            var colors = new List<PsdWriter.Descriptor>();
            int index = 0;
            foreach (GradientColorKey key in layer.gradient.ColorKeys)
                colors.Add(new PsdWriter.Descriptor("Clrt").Object("Clr ", Rgb(key.color)).Enum("Type", "Clry", "UsrS")
                    .Int("Lctn", Mathf.RoundToInt(key.time * 4096f)).Int("Mdpn", Mathf.RoundToInt(layer.gradient.GetMidpoint(false, index++) * 100f)));
            var alpha = new List<PsdWriter.Descriptor>();
            index = 0;
            foreach (GradientAlphaKey key in layer.gradient.AlphaKeys)
                alpha.Add(new PsdWriter.Descriptor("TrnS").Unit("Opct", "#Prc", key.alpha * 100d)
                    .Int("Lctn", Mathf.RoundToInt(key.time * 4096f)).Int("Mdpn", Mathf.RoundToInt(layer.gradient.GetMidpoint(true, index++) * 100f)));
            var gradient = new PsdWriter.Descriptor("Grdn").Text("Nm  ", layer.layerName).Enum("GrdF", "GrdF", "CstS")
                .Int("Intr", 0).Objects("Clrs", colors).Objects("Trns", alpha);
            string type;
            double angle = 0, scale = 100;
            switch (layer.gradientType)
            {
                case GradientLayerBehaviour.GradientType.Horizontal: type = "Lnr "; break;
                case GradientLayerBehaviour.GradientType.Vertical: type = "Lnr "; angle = 90; break;
                case GradientLayerBehaviour.GradientType.Circular: type = "Angl"; angle = 180; break;
                case GradientLayerBehaviour.GradientType.Diamond: type = "Dmnd"; break;
                case GradientLayerBehaviour.GradientType.Square: type = "Dmnd"; angle = 45; break;
                default: type = "Rdl "; break;
            }
            Vector2 center = GradientLayerBehaviour.BaseCenter;
            TextureTransform transform = layer.Owner.CanvasTransform;
            Vector2 pivot = Vector2.Scale(transform.pivotF, new Vector2(width, height));
            Vector2 local = Vector2.Scale(Vector2.Scale(center, new Vector2(width, height)) - pivot, transform.scaleF);
            float radians = transform.rotationF * Mathf.Deg2Rad;
            center = pivot + transform.positionF + new Vector2(Mathf.Cos(radians) * local.x - Mathf.Sin(radians) * local.y,
                Mathf.Sin(radians) * local.x + Mathf.Cos(radians) * local.y);
            double x = (center.x / width - 0.5) * 100, y = (0.5 - center.y / height) * 100;
            angle += transform.rotationF;
            if (type == "Lnr ")
            {
                bool horizontal = layer.gradientType == GradientLayerBehaviour.GradientType.Horizontal;
                float axisScale = horizontal ? transform.scaleF.x : transform.scaleF.y;
                if (axisScale < 0) angle += 180;
                double projectedCanvas = Math.Abs(width * Math.Cos(angle * Math.PI / 180)) + Math.Abs(height * Math.Sin(angle * Math.PI / 180));
                scale = (horizontal ? width : height) * Math.Abs(axisScale) / projectedCanvas * 100;
            }
            else
            {
                scale = GradientLayerBehaviour.BaseRadius * transform.scaleF.x * 200;
                if (layer.gradientType == GradientLayerBehaviour.GradientType.Square) scale *= Math.Sqrt(2);
            }
            return new PsdWriter.Descriptor().Unit("Angl", "#Ang", angle).Enum("Type", "GrdT", type)
                .Unit("Scl ", "#Prc", scale).Bool("Rvrs", false).Bool("Dthr", false).Bool("Algn", true)
                .Object("Ofst", new PsdWriter.Descriptor("Pnt ").Unit("Hrzn", "#Prc", x).Unit("Vrtc", "#Prc", y))
                .Object("Grad", gradient);
        }

        internal static string BlendKey(BlendMode mode, out bool approximate)
        {
            approximate = false;
            switch (mode)
            {
                case BlendMode.Normal: case BlendMode.None: return "norm";
                case BlendMode.Multiply: return "mul ";
                case BlendMode.Add: case BlendMode.LinearDodge: return "lddg";
                case BlendMode.Subtract: return "fsub";
                case BlendMode.Divide: return "fdiv";
                case BlendMode.Screen: return "scrn";
                case BlendMode.Overlay: return "over";
                case BlendMode.Darken: return "dark";
                case BlendMode.Lighten: return "lite";
                case BlendMode.Dodge: return "div ";
                case BlendMode.Burn: return "idiv";
                case BlendMode.LinearBurn: return "lbrn";
                case BlendMode.LinearLight: return "lLit";
                case BlendMode.VividLight: return "vLit";
                case BlendMode.PinLight: return "pLit";
                case BlendMode.HardMix: return "hMix";
                case BlendMode.HardLight: return "hLit";
                case BlendMode.SoftLight: return "sLit";
                case BlendMode.Difference: return "diff";
                case BlendMode.Exclusion: return "smud";
                case BlendMode.LinearLightAddSub: approximate = true; return "lLit";
                case BlendMode.Negation: approximate = true; return "diff";
                default: approximate = true; return "norm";
            }
        }

        private static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        private static Pixels GradientPixels(TextureCompositor document, GradientLayerBehaviour layer, bool mask)
        {
            Texture2D texture = document.RenderPsdPixels(layer);
            try
            {
                Texture2D coverage = mask ? document.RenderPsdCoverage(layer) : null;
                return new Pixels(texture, document.width, document.height, coverage: coverage);
            }
            catch
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        private sealed class Pixels : PsdWriter.IPixels
        {
            private readonly Texture2D texture;
            private readonly int width, height;
            private readonly bool alphaAsMask;
            private readonly Texture2D coverage;
            public Pixels(Texture2D texture, int width, int height, bool alphaAsMask = false, Texture2D coverage = null)
            { this.texture = texture; this.width = width; this.height = height; this.alphaAsMask = alphaAsMask; this.coverage = coverage; }
            public void ReadRow(int channel, int y, byte[] destination)
            {
                if (texture == null) { Array.Clear(destination, 0, width); return; }
                var pixels = channel == -2 && coverage != null ? coverage.GetRawTextureData<Color32>() : texture.GetRawTextureData<Color32>();
                int offset = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 color = pixels[offset + x];
                    destination[x] = channel == 0 ? color.r : channel == 1 ? color.g : channel == 2 ? color.b :
                        channel == -1 && alphaAsMask ? (byte)255 : color.a;
                }
            }
            public void Dispose()
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (coverage != null) UnityEngine.Object.DestroyImmediate(coverage);
            }
        }
    }
}
