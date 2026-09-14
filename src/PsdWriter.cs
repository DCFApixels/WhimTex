using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DCFApixels.WhimTex
{
    // The format writer has no dependency on Unity or on the compositor's layer model.
    internal static class PsdWriter
    {
        internal interface IPixels : IDisposable
        {
            // Channels: 0/1/2 RGB, -1 transparency, -2 user mask. Rows are top-to-bottom.
            void ReadRow(int channel, int y, byte[] destination);
        }

        internal sealed class LayerRecord
        {
            public string name = "";
            public string blend = "norm";
            public string sectionBlend = "pass";
            public uint id;
            public byte opacity = 255;
            public bool visible = true;
            public bool clipping;
            public int section;
            public bool mask;
            public bool adjustment;
            public byte fillOpacity = 255;
            public readonly List<KeyValuePair<string, Descriptor>> descriptors = new List<KeyValuePair<string, Descriptor>>();
            public Func<IPixels> openPixels;
        }

        internal sealed class Descriptor
        {
            private readonly string classId;
            private readonly List<Action<BigEndian>> entries = new List<Action<BigEndian>>();
            public Descriptor(string classId = "null") { this.classId = classId; }
            private Descriptor Add(string key, string type, Action<BigEndian> value)
            {
                entries.Add(w => { w.Id(key); w.Code(type); value(w); });
                return this;
            }
            public Descriptor Bool(string key, bool value) => Add(key, "bool", w => w.Byte(value ? 1 : 0));
            public Descriptor Int(string key, int value) => Add(key, "long", w => w.U32(unchecked((uint)value)));
            public Descriptor Number(string key, double value) => Add(key, "doub", w => w.Double(value));
            public Descriptor Text(string key, string value) => Add(key, "TEXT", w => w.Unicode(value));
            public Descriptor Unit(string key, string unit, double value) => Add(key, "UntF", w => { w.Code(unit); w.Double(value); });
            public Descriptor Enum(string key, string type, string value) => Add(key, "enum", w => { w.Id(type); w.Id(value); });
            public Descriptor Object(string key, Descriptor value) => Add(key, "Objc", value.Write);
            public Descriptor Objects(string key, List<Descriptor> values) => Add(key, "VlLs", w =>
            {
                w.U32((uint)values.Count);
                foreach (Descriptor value in values) { w.Code("Objc"); value.Write(w); }
            });
            internal void Write(BigEndian w)
            {
                w.Unicode(""); w.Id(classId); w.U32((uint)entries.Count);
                foreach (Action<BigEndian> entry in entries) entry(w);
            }
        }

        internal static void Write(Stream stream, int width, int height, IList<LayerRecord> layers, IPixels merged)
        {
            if (stream == null || !stream.CanWrite || !stream.CanSeek)
                throw new ArgumentException("PSD output must be a writable, seekable stream.");
            if (width < 1 || height < 1 || width > 30000 || height > 30000)
                throw new ArgumentOutOfRangeException(nameof(width), "PSD supports canvas dimensions from 1 to 30000 pixels.");
            if (layers.Count > 32767)
                throw new ArgumentException("PSD supports at most 32767 layer records, including folder dividers.");

            var w = new BigEndian(stream);
            w.Code("8BPS"); w.U16(1); w.Zeros(6); w.U16(4);
            w.U32((uint)height); w.U32((uint)width); w.U16(8); w.U16(3);
            w.U32(0); // RGB has no color-mode payload.
            w.Section(() =>
            {
                w.Code("8BIM"); w.U16(1057); w.U16(0);
                w.Section(() =>
                {
                    w.U32(1); w.Byte(1);
                    w.Unicode("WhimTex"); w.Unicode("WhimTex"); w.U32(1);
                }, 2);
            });

            byte[] row = new byte[width];
            byte[] encoded = new byte[width + (width + 127) / 128 + 2];
            w.Section(() =>
            {
                w.Section(() =>
                {
                    w.U16(unchecked((ushort)-layers.Count));
                    var channelOffsets = new List<long[]>();
                    for (int i = 0; i < layers.Count; i++)
                    {
                        LayerRecord layer = layers[i];
                        bool folder = layer.section != 0;
                        int channels = folder ? 4 : layer.mask ? 5 : 4;
                        w.U32(0); w.U32(0); w.U32(folder ? 0u : (uint)height); w.U32(folder ? 0u : (uint)width);
                        w.U16((ushort)channels);
                        var offsets = new long[channels];
                        for (int c = 0; c < channels; c++)
                        {
                            w.U16(unchecked((ushort)ChannelId(c)));
                            offsets[c] = w.Position; w.U32(0);
                        }
                        channelOffsets.Add(offsets);
                        w.Code("8BIM"); w.Code(layer.blend);
                        w.Byte(layer.opacity); w.Byte(layer.clipping ? 1 : 0);
                        w.Byte(8 | (layer.visible ? 0 : 2) | (folder || layer.adjustment ? 16 : 0)); w.Byte(0);
                        w.Section(() =>
                        {
                            w.Section(() =>
                            {
                                if (!layer.mask) return;
                                w.U32(0); w.U32(0); w.U32((uint)height); w.U32((uint)width);
                                w.Byte(0); w.Byte(0); w.U16(0);
                            });
                            w.U32(0);
                            w.Pascal(layer.name);
                            w.Block("luni", () => w.Unicode(layer.name), 4);
                            w.Block("lyid", () => w.U32(layer.id == 0 ? (uint)i + 1 : layer.id));
                            if (folder) w.Block("lsct", () =>
                            {
                                w.U32((uint)layer.section);
                                if (layer.section != 3) { w.Code("8BIM"); w.Code(layer.sectionBlend); w.U32(0); }
                            });
                            if (layer.fillOpacity != 255)
                                w.Block("iOpa", () => w.Byte(layer.fillOpacity));
                            foreach (var descriptor in layer.descriptors)
                                w.Block(descriptor.Key, () =>
                                {
                                    if (descriptor.Key == "lfx2") w.U32(0);
                                    w.U32(16); descriptor.Value.Write(w);
                                }, 4);
                        });
                    }

                    for (int i = 0; i < layers.Count; i++)
                    {
                        LayerRecord layer = layers[i];
                        using (IPixels pixels = layer.section == 0 ? layer.openPixels() : null)
                        {
                            for (int c = 0; c < channelOffsets[i].Length; c++)
                            {
                                long start = w.Position;
                                if (layer.section != 0) w.U16(0);
                                else WriteChannel(w, pixels, ChannelId(c), height, row, encoded);
                                w.Patch32(channelOffsets[i][c], w.Position - start);
                            }
                        }
                    }
                }, 2);
                w.U32(0);
            });

            // Merged image uses the actual compositor result, including unsupported blend modes.
            w.U16(1);
            long table = w.Position;
            w.Zeros(checked(height * 4 * 2));
            byte[] alpha = new byte[width];
            ushort[] mergedCounts = new ushort[height * 4];
            for (int channel = 0; channel < 4; channel++)
                for (int y = 0; y < height; y++)
                {
                    merged.ReadRow(channel == 3 ? -1 : channel, y, row);
                    if (channel < 3)
                    {
                        // Merged transparency in PSD uses RGB matted against white; layer RGB does not.
                        merged.ReadRow(-1, y, alpha);
                        for (int x = 0; x < width; x++)
                            row[x] = (byte)((row[x] * alpha[x] + 255 * (255 - alpha[x]) + 127) / 255);
                    }
                    int count = PackBits(row, encoded);
                    w.Bytes(encoded, count);
                    mergedCounts[channel * height + y] = checked((ushort)count);
                }
            w.PatchCounts(table, mergedCounts);
            if (w.Position > int.MaxValue)
                throw new InvalidOperationException("The PSD exceeds the supported 2 GB file limit.");
        }

        private static int ChannelId(int index) => index == 3 ? -1 : index == 4 ? -2 : index;

        private static void WriteChannel(BigEndian w, IPixels pixels, int channel, int height, byte[] row, byte[] encoded)
        {
            w.U16(1);
            long table = w.Position; w.Zeros(height * 2);
            ushort[] counts = new ushort[height];
            for (int y = 0; y < height; y++)
            {
                pixels.ReadRow(channel, y, row);
                int count = PackBits(row, encoded);
                w.Bytes(encoded, count);
                counts[y] = checked((ushort)count);
            }
            w.PatchCounts(table, counts);
        }

        internal static int PackBits(byte[] input, byte[] output)
        {
            int read = 0, write = 0;
            while (read < input.Length)
            {
                int run = 1;
                while (run < 128 && read + run < input.Length && input[read + run] == input[read]) run++;
                if (run >= 3)
                {
                    output[write++] = (byte)(257 - run); output[write++] = input[read]; read += run;
                    continue;
                }
                int start = read;
                do
                {
                    read++;
                    if (read + 2 < input.Length && input[read] == input[read + 1] && input[read] == input[read + 2]) break;
                } while (read < input.Length && read - start < 128);
                int length = read - start;
                output[write++] = (byte)(length - 1);
                Buffer.BlockCopy(input, start, output, write, length); write += length;
            }
            return write;
        }

        internal sealed class BigEndian
        {
            private readonly Stream stream;
            public long Position => stream.Position;
            public BigEndian(Stream stream) { this.stream = stream; }
            public void Byte(int value) => stream.WriteByte((byte)value);
            public void U16(ushort value) { Byte(value >> 8); Byte(value); }
            public void U32(uint value) { U16((ushort)(value >> 16)); U16((ushort)value); }
            public void Double(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("PSD numeric settings must be finite.");
                ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
                U32((uint)(bits >> 32)); U32((uint)bits);
            }
            public void Bytes(byte[] data, int count) => stream.Write(data, 0, count);
            public void Zeros(int count) { for (int i = 0; i < count; i++) Byte(0); }
            public void Code(string value)
            {
                if (value.Length != 4) throw new ArgumentException("PSD signature must contain four characters.");
                for (int i = 0; i < 4; i++) Byte(value[i]);
            }
            public void Id(string value)
            {
                U32(value.Length == 4 ? 0u : (uint)value.Length);
                foreach (char c in value) Byte(c);
            }
            public void Unicode(string value)
            {
                value = value ?? ""; U32((uint)value.Length);
                foreach (char c in value) U16(c);
            }
            public void Pascal(string value)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(value ?? "");
                int count = Math.Min(255, bytes.Length); Byte(count); Bytes(bytes, count);
                Zeros((4 - (count + 1) % 4) % 4);
            }
            public void Patch32(long position, long value)
            {
                if (value < 0 || value > int.MaxValue) throw new InvalidOperationException("PSD section exceeds 2 GB.");
                long end = Position; stream.Position = position; U32((uint)value); stream.Position = end;
            }
            public void PatchCounts(long position, ushort[] values)
            {
                long end = Position; stream.Position = position;
                foreach (ushort value in values) U16(value);
                stream.Position = end;
            }
            public void Section(Action write, int alignment = 1)
            {
                long length = Position; U32(0); long start = Position;
                write(); Zeros((int)((alignment - (Position - start) % alignment) % alignment));
                Patch32(length, Position - start);
            }
            public void Block(string key, Action write, int alignment = 2)
            {
                Code("8BIM"); Code(key); Section(write, alignment);
            }
        }
    }
}
