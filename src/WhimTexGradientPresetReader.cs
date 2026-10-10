using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Descriptor = DCFApixels.WhimTex.PsdDescriptorReader.Descriptor;
using EnumValue = DCFApixels.WhimTex.PsdDescriptorReader.EnumValue;
using UnitValue = DCFApixels.WhimTex.PsdDescriptorReader.UnitValue;

namespace DCFApixels.WhimTex
{
    public sealed class GradientPreset
    {
        public string name { get; internal set; }
        public WhimTexGradient gradient { get; internal set; }
    }
    public sealed class GradientPresetReadResult
    {
        internal readonly List<GradientPreset> entries = new List<GradientPreset>();
        internal readonly List<string> messages = new List<string>();
        public IReadOnlyList<GradientPreset> presets => entries;
        public IReadOnlyList<string> warnings => messages;
    }
    public static class WhimTexGradientPresetReader
    {
        public const int MaxFileBytes = 32 * 1024 * 1024;
        public static GradientPresetReadResult Read(string path, Color? foreground = null, Color? background = null)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes) throw new InvalidDataException("GRD file exceeds 32 MiB.");
            byte[] bytes = new byte[checked((int)stream.Length)];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new InvalidDataException("GRD file changed during reading.");
                offset += read;
            }
            if (stream.ReadByte() != -1) throw new InvalidDataException("GRD file changed during reading.");
            return Read(bytes, foreground, background);
        }
        public static GradientPresetReadResult Read(byte[] bytes, Color? foreground = null, Color? background = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length > MaxFileBytes) throw new InvalidDataException("GRD file exceeds 32 MiB.");
            var reader = new PsdDescriptorReader(bytes);
            if (reader.Code() != "8BGR") throw new InvalidDataException("Not a GRD gradient library.");
            int version = reader.U16();
            var result = new GradientPresetReadResult();
            Color front = foreground ?? Color.black, back = background ?? Color.white;
            if (version == 5) ReadDescriptors(reader, result, front, back);
            else if (version == 3) ReadStops(reader, result, front, back);
            else throw new InvalidDataException("Unsupported GRD version: " + version);
            if (result.entries.Count == 0) throw new InvalidDataException("The GRD library contains no usable gradients. " + string.Join(" ", result.messages));
            return result;
        }
        private static void ReadDescriptors(PsdDescriptorReader reader, GradientPresetReadResult result, Color front, Color back)
        {
            if (reader.U32() != 16) throw new InvalidDataException("Unsupported GRD descriptor version.");
            Descriptor root = reader.ReadDescriptor();
            if (!(root.Get("GrdL") is List<object> entries)) throw new InvalidDataException("GRD gradient list is missing.");
            int index = 0;
            foreach (object entry in entries)
            {
                string name = "Gradient " + ++index;
                try
                {
                    Descriptor wrapper = Object(entry);
                    Descriptor gradient = wrapper.Get("Grad") == null ? wrapper : Object(wrapper.Get("Grad"));
                    if (gradient.Get("Nm  ") is string title && !string.IsNullOrWhiteSpace(title)) name = DisplayName(title);
                    if (gradient.Get("GrdF") is EnumValue form && form.Value != "CstS")
                        throw new InvalidDataException("Noise or unknown gradient forms are not supported.");
                    var colors = new List<ColorStop>();
                    var alphas = new List<AlphaStop>();
                    bool dynamic = false;
                    foreach (object item in Track(gradient.Get("Clrs"), false))
                    {
                        Descriptor stop = Object(item);
                        string type = stop.Get("Type") is EnumValue kind ? kind.Value : "UsrS";
                        Color color;
                        if (type == "FrgC" || type == "BckC") { color = type == "FrgC" ? front : back; dynamic = true; }
                        else if (type == "UsrS") color = ReadColor(Object(stop.Get("Clr ")));
                        else throw new InvalidDataException("Unknown color stop type: " + type);
                        colors.Add(new ColorStop { color = color, time = Position(stop), midpoint = Midpoint(stop) });
                    }
                    foreach (object item in Track(gradient.Get("Trns"), true))
                    {
                        Descriptor stop = Object(item);
                        double opacity = Number(stop.Get("Opct"));
                        if (stop.Get("Opct") is UnitValue unit && unit.Unit != "#Prc") throw new InvalidDataException("Opacity must use percent units.");
                        alphas.Add(new AlphaStop { alpha = Range(opacity / 100, 0, 1), time = Position(stop), midpoint = Midpoint(stop) });
                    }
                    var value = Create(colors, alphas);
                    object method = wrapper.Get("gradientsInterpolationMethod") ?? gradient.Get("gradientsInterpolationMethod");
                    string methodName = method is EnumValue enumMethod ? enumMethod.Value : method as string;
                    value.Mode = methodName == "Gcls" || methodName == "classic" ? WhimTexGradientMode.Classic :
                        methodName == "Lnr " || methodName == "linear" ? WhimTexGradientMode.Linear : WhimTexGradientMode.Perceptual;
                    if (method != null && methodName != "Gcls" && methodName != "classic" && methodName != "Lnr " && methodName != "linear" && methodName != "Perc" && methodName != "perceptual")
                        result.messages.Add(name + ": unknown interpolation uses Perceptual.");
                    object smoothness = gradient.Get("Intr");
                    double smooth = OptionalNumber(smoothness, 4096);
                    if (smooth < 0 || smooth > 4096 || smoothness != null && !(smoothness is double) && !(smoothness is int))
                    { smooth = 4096; result.messages.Add(name + ": unknown Smoothness uses 100%."); }
                    value.Smoothness = (float)(smooth / 4096);
                    result.entries.Add(new GradientPreset { name = name, gradient = value });
                    if (dynamic) result.messages.Add(name + ": foreground/background stops were resolved to the supplied colors.");
                }
                catch (Exception error) when (error is InvalidDataException || error is ArgumentException)
                { result.messages.Add(name + ": skipped. " + error.Message); }
            }
            if (reader.Remaining > 0) result.messages.Add("GRD folder metadata or trailing data was not imported.");
        }
        private static void ReadStops(PsdDescriptorReader reader, GradientPresetReadResult result, Color front, Color back)
        {
            int count = reader.U16();
            if (count > 4096) throw new InvalidDataException("GRD library exceeds 4096 gradients.");
            for (int i = 0; i < count; i++)
            {
                string name = reader.Pascal();
                var colors = new List<ColorStop>();
                var alphas = new List<AlphaStop>();
                bool unsupported = false, dynamic = false;
                int colorCount = reader.U16();
                if (colorCount > 4096) throw new InvalidDataException("GRD color stop limit exceeded.");
                for (int c = 0; c < colorCount; c++)
                {
                    float time = Range(reader.U32() / 4096d, 0, 1), midpoint = Range(reader.U32() / 100d, 0, 1);
                    int model = reader.U16();
                    int r = reader.U16(), g = reader.U16(), b = reader.U16(); reader.U16();
                    int type = reader.U16();
                    Color color = Color.black;
                    if (type == 1 || type == 2) { color = type == 1 ? front : back; dynamic = true; }
                    else if (type != 0) unsupported = true;
                    else if (model == 0) color = new Color(r / 65535f, g / 65535f, b / 65535f, 1);
                    else if (model == 1) color = Color.HSVToRGB(r / 65535f, g / 65535f, b / 65535f);
                    else if (model == 8) color = Color.white * Range(r / 10000d, 0, 1);
                    else unsupported = true;
                    colors.Add(new ColorStop { color = color, time = time, midpoint = Mathf.Clamp(midpoint, .01f, .99f) });
                }
                int alphaCount = reader.U16();
                if (alphaCount > 4096) throw new InvalidDataException("GRD alpha stop limit exceeded.");
                for (int a = 0; a < alphaCount; a++)
                    alphas.Add(new AlphaStop { time = Range(reader.U32() / 4096d, 0, 1), midpoint = Mathf.Clamp(Range(reader.U32() / 100d, 0, 1), .01f, .99f), alpha = Range(reader.U16() / 255d, 0, 1) });
                reader.Skip(6);
                try
                {
                    if (unsupported) throw new InvalidDataException("Unsupported color model or stop type.");
                    result.entries.Add(new GradientPreset { name = name, gradient = Create(colors, alphas) });
                    if (dynamic) result.messages.Add(name + ": foreground/background stops were resolved to the supplied colors.");
                }
                catch (ArgumentException error) { result.messages.Add(name + ": skipped. " + error.Message); }
                catch (InvalidDataException error) { result.messages.Add(name + ": skipped. " + error.Message); }
            }
            if (reader.Remaining != 0) throw new InvalidDataException("Unexpected data after the GRD gradient list.");
        }
        private struct ColorStop { internal Color color; internal float time, midpoint; }
        private struct AlphaStop { internal float alpha, time, midpoint; }
        private static WhimTexGradient Create(List<ColorStop> colors, List<AlphaStop> alphas)
        {
            if (colors.Count == 0 || colors.Count > 64 || alphas.Count > 64) throw new InvalidDataException("Each gradient track supports 1..64 stops.");
            colors.Sort((a, b) => a.time.CompareTo(b.time)); alphas.Sort((a, b) => a.time.CompareTo(b.time));
            if (alphas.Count == 0) { alphas.Add(new AlphaStop { alpha = 1, time = 0, midpoint = .5f }); alphas.Add(new AlphaStop { alpha = 1, time = 1, midpoint = .5f }); }
            var c = new GradientColorKey[colors.Count]; var a = new GradientAlphaKey[alphas.Count];
            for (int i = 0; i < c.Length; i++) c[i] = new GradientColorKey(colors[i].color, colors[i].time);
            for (int i = 0; i < a.Length; i++) a[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);
            var gradient = new WhimTexGradient(); gradient.SetKeys(c, a);
            for (int i = 0; i < c.Length; i++) gradient.SetMidpoint(false, i, colors[i].midpoint);
            for (int i = 0; i < a.Length; i++) gradient.SetMidpoint(true, i, alphas[i].midpoint);
            return gradient;
        }
        private static Descriptor Object(object value) => value as Descriptor ?? throw new InvalidDataException("Expected a GRD object.");
        private static List<object> Track(object value, bool optional)
        {
            if (value == null && optional) return new List<object>();
            var list = value as List<object> ?? throw new InvalidDataException("Expected a GRD stop list.");
            if (list.Count > 64) throw new InvalidDataException("Gradient track exceeds 64 stops.");
            return list;
        }
        private static double Number(object value) => value is UnitValue unit ? unit.Value : value is double d ? d :
            value is int i ? i : throw new InvalidDataException("Expected a GRD number.");
        private static double OptionalNumber(object value, double fallback) => value is double || value is int ? Number(value) : fallback;
        private static float Range(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum) throw new InvalidDataException("GRD value is outside its supported range.");
            return (float)value;
        }
        private static float Position(Descriptor value) => Range(Number(value.Get("Lctn")) / 4096, 0, 1);
        private static float Midpoint(Descriptor value) => Mathf.Clamp(Range(OptionalNumber(value.Get("Mdpn"), 50) / 100, 0, 1), .01f, .99f);
        private static string DisplayName(string name) => name.StartsWith("$$$/", StringComparison.Ordinal) && name.Contains("=") ? name.Substring(name.LastIndexOf('=') + 1) : name;
        private static Color ReadColor(Descriptor color)
        {
            if (color.ClassId == "RGBC") return new Color(Range(Number(color.Get("Rd  ")) / 255, 0, 1), Range(Number(color.Get("Grn ")) / 255, 0, 1), Range(Number(color.Get("Bl  ")) / 255, 0, 1), 1);
            if (color.ClassId == "HSBC")
            {
                object hue = color.Get("H   ");
                if (hue is UnitValue unit && unit.Unit != "#Ang") throw new InvalidDataException("Hue must use angle units.");
                return Color.HSVToRGB(Range(Number(hue) / 360, 0, 1), Range(Number(color.Get("Strt")) / 100, 0, 1), Range(Number(color.Get("Brgh")) / 100, 0, 1));
            }
            if (color.ClassId == "Grsc") { float gray = 1 - Range(Number(color.Get("Gry ")) / 100, 0, 1); return new Color(gray, gray, gray, 1); }
            throw new InvalidDataException("Color model requires an unavailable color profile or color book: " + color.ClassId);
        }
    }
}
