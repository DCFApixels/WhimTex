using System;
using System.Collections.Generic;
using System.IO;
using DCFApixels.WhimTex;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static void Main(string[] args)
    {
        var random = new Random(42);
        foreach (int width in new[] { 1, 2, 3, 127, 128, 129, 255, 512, 30000 })
            for (int mode = 0; mode < 5; mode++)
            {
                byte[] row = new byte[width];
                for (int i = 0; i < width; i++)
                    row[i] = mode == 0 ? (byte)77 : mode == 1 ? (byte)i : mode == 2 ? (byte)(i / 3) :
                        mode == 3 ? (byte)random.Next(256) : (byte)(i % 129 == 0 ? 1 : 0);
                byte[] packed = new byte[width + (width + 127) / 128 + 2];
                int length = PsdWriter.PackBits(row, packed);
                byte[] restored = Unpack(packed, length, width);
                Check(Convert.ToBase64String(row) == Convert.ToBase64String(restored), "PackBits round trip");
            }

        var layers = new List<PsdWriter.LayerRecord>
        {
            new PsdWriter.LayerRecord { name = "Bottom", opacity = 128, blend = "mul ", openPixels = () => new Pixels() },
            new PsdWriter.LayerRecord { name = "</Group>", section = 3 },
            new PsdWriter.LayerRecord { name = "</Group>", section = 3 },
            new PsdWriter.LayerRecord { name = "Hidden", visible = false, openPixels = () => new Pixels() },
            new PsdWriter.LayerRecord { name = "Nested", section = 1, blend = "mul ", sectionBlend = "mul ", opacity = 153 },
            new PsdWriter.LayerRecord { name = "Группа 💗", section = 1, clipping = true },
        };
        var fill = new PsdWriter.LayerRecord { name = "Fill", adjustment = true, mask = true, openPixels = () => new Pixels() };
        fill.descriptors.Add(new KeyValuePair<string, PsdWriter.Descriptor>("SoCo", new PsdWriter.Descriptor()
            .Object("Clr ", new PsdWriter.Descriptor("RGBC").Number("Rd  ", 255).Number("Grn ", 64).Number("Bl  ", 32))));
        layers.Add(fill);
        var outline = new PsdWriter.LayerRecord { name = "Stroke", clipping = true, fillOpacity = 0, openPixels = () => new Pixels() };
        outline.descriptors.Add(new KeyValuePair<string, PsdWriter.Descriptor>("lfx2", new PsdWriter.Descriptor()
            .Bool("masterFXSwitch", true).Unit("Scl ", "#Prc", 100).Object("FrFX", new PsdWriter.Descriptor("FrFX")
                .Bool("enab", true).Enum("Styl", "FStl", "OutF").Enum("PntT", "FrFl", "SClr")
                .Enum("Md  ", "BlnM", "Nrml").Unit("Opct", "#Prc", 75).Unit("Sz  ", "#Pxl", 4)
                .Object("Clr ", new PsdWriter.Descriptor("RGBC").Number("Rd  ", 255).Number("Grn ", 0).Number("Bl  ", 0)))));
        layers.Add(outline);
        var gradient = new PsdWriter.LayerRecord { name = "Gradient", adjustment = true, openPixels = () => new Pixels() };
        var stops = new List<PsdWriter.Descriptor>();
        var alphas = new List<PsdWriter.Descriptor>();
        for (int i = 0; i < 2; i++)
        {
            stops.Add(new PsdWriter.Descriptor("Clrt").Object("Clr ", new PsdWriter.Descriptor("RGBC")
                .Number("Rd  ", i * 255).Number("Grn ", 0).Number("Bl  ", 255 - i * 255))
                .Enum("Type", "Clry", "UsrS").Int("Lctn", i * 4096).Int("Mdpn", 50));
            alphas.Add(new PsdWriter.Descriptor("TrnS").Unit("Opct", "#Prc", i * 100).Int("Lctn", i * 4096).Int("Mdpn", 50));
        }
        gradient.descriptors.Add(new KeyValuePair<string, PsdWriter.Descriptor>("GdFl", new PsdWriter.Descriptor()
            .Enum("Type", "GrdT", "Lnr ").Unit("Angl", "#Ang", 90).Unit("Scl ", "#Prc", 100).Bool("Algn", true)
            .Object("Grad", new PsdWriter.Descriptor("Grdn").Text("Nm  ", "Gradient").Enum("GrdF", "GrdF", "CstS")
                .Int("Intr", 4096).Objects("Clrs", stops).Objects("Trns", alphas))));
        layers.Add(gradient);
        using (var stream = new MemoryStream())
        using (var pixels = new Pixels())
        {
            PsdWriter.Write(stream, 3, 2, layers, pixels);
            byte[] bytes = stream.ToArray();
            Check(System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "8BPS", "PSD signature");
            Check(bytes[5] == 1 && bytes[13] == 4 && bytes[23] == 8 && bytes[25] == 3, "Version, RGBA, 8-bit RGB");
            Check(Pixels.disposed == 5, "Each layer pixel source is disposed after encoding");
            int cursor = 26;
            cursor += 4 + (int)Read32(bytes, cursor);
            cursor += 4 + (int)Read32(bytes, cursor);
            int layerEnd = cursor + 4 + (int)Read32(bytes, cursor);
            int layerInfoEnd = cursor + 8 + (int)Read32(bytes, cursor + 4);
            int layerCursor = cursor + 8;
            Check(unchecked((short)Read16(bytes, layerCursor)) == -9, "Merged alpha indicated by negative layer count");
            int recordCursor = layerCursor + 2;
            for (int recordIndex = 0; recordIndex < layers.Count; recordIndex++)
            {
                int channelCount = Read16(bytes, recordCursor + 16);
                int blendStart = recordCursor + 18 + channelCount * 6;
                Check(bytes[blendStart + 9] == (layers[recordIndex].clipping ? 1 : 0), "Native clipping flag");
                recordCursor = blendStart + 16 + (int)Read32(bytes, blendStart + 12);
            }
            Check(layerInfoEnd + 4 == layerEnd, "Global mask and layer section lengths");
            Check(Read16(bytes, layerEnd) == 1, "Merged image RLE");
            int scanline = layerEnd + 2 + 4 * 2 * 2;
            for (int channel = 0; channel < 4; channel++)
                for (int y = 0; y < 2; y++)
                {
                    int length = Read16(bytes, layerEnd + 2 + (channel * 2 + y) * 2);
                    byte[] data = new byte[length]; Array.Copy(bytes, scanline, data, 0, length); scanline += length;
                    byte[] restored = Unpack(data, length, 3);
                    byte[] expected = new byte[3]; pixels.ReadRow(channel == 3 ? -1 : channel, y, expected);
                    if (channel < 3)
                    {
                        byte[] alpha = new byte[3]; pixels.ReadRow(-1, y, alpha);
                        for (int x = 0; x < 3; x++) expected[x] = (byte)((expected[x] * alpha[x] + 255 * (255 - alpha[x]) + 127) / 255);
                    }
                    Check(Convert.ToBase64String(restored) == Convert.ToBase64String(expected), "Merged channel orientation/data");
                }
            Check(scanline == bytes.Length, "No trailing data");
            if (args.Length != 0) File.WriteAllBytes(args[0], bytes);
        }
        bool rejected = false;
        try { PsdWriter.Write(new MemoryStream(), 30001, 2, layers, new Pixels()); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Oversized canvas rejected");
        Console.WriteLine($"PSD writer: {checks} checks passed.");
    }

    static int Read16(byte[] bytes, int p) => bytes[p] * 256 + bytes[p + 1];
    static uint Read32(byte[] bytes, int p) => (uint)Read16(bytes, p) * 65536 + (uint)Read16(bytes, p + 2);
    static byte[] Unpack(byte[] source, int length, int width)
    {
        byte[] output = new byte[width];
        int read = 0, write = 0;
        while (read < length)
        {
            int n = (sbyte)source[read++];
            if (n >= 0) for (int i = 0; i <= n; i++) output[write++] = source[read++];
            else if (n != -128) { byte b = source[read++]; for (int i = 0; i < 1 - n; i++) output[write++] = b; }
        }
        if (write != width) throw new Exception("Wrong decoded row length");
        return output;
    }

    sealed class Pixels : PsdWriter.IPixels
    {
        public static int disposed;
        public void ReadRow(int channel, int y, byte[] row)
        {
            for (int x = 0; x < row.Length; x++) row[x] = (byte)(20 * (channel + 3) + 7 * y + x);
        }
        public void Dispose() { disposed++; }
    }
}
