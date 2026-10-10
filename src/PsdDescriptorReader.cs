using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed class PsdDescriptorReader
    {
        internal sealed class Descriptor : Dictionary<string, object>
        {
            internal string ClassId;
            internal object Get(string key) => TryGetValue(key, out object value) ? value : null;
        }
        internal readonly struct EnumValue
        {
            internal readonly string Type, Value;
            internal EnumValue(string type, string value) { Type = type; Value = value; }
        }
        internal readonly struct UnitValue
        {
            internal readonly string Unit;
            internal readonly double Value;
            internal UnitValue(string unit, double value) { Unit = unit; Value = value; }
        }

        private readonly byte[] data;
        private int position, valuesRead;
        internal int Remaining => data.Length - position;
        internal PsdDescriptorReader(byte[] data) { this.data = data ?? throw new ArgumentNullException(nameof(data)); }
        internal int Byte() { Need(1); return data[position++]; }
        internal int U16() => Byte() * 256 + Byte();
        internal uint U32() => (uint)U16() * 65536 + (uint)U16();
        internal int Count(int maximum)
        {
            uint count = U32();
            if (count > maximum) throw new InvalidDataException("GRD count exceeds its supported limit.");
            return (int)count;
        }
        internal void Skip(int count) { Need(count); position += count; }
        private void Need(int count)
        {
            if (count < 0 || count > Remaining) throw new InvalidDataException("GRD data is truncated.");
        }
        internal string Code() { Need(4); string value = Encoding.ASCII.GetString(data, position, 4); position += 4; return value; }
        private string Id()
        {
            int count = Count(256);
            if (count == 0) count = 4;
            Need(count); string value = Encoding.ASCII.GetString(data, position, count); position += count; return value;
        }
        internal string Pascal()
        {
            int count = Byte(); Need(count);
            string value = Encoding.GetEncoding(28591).GetString(data, position, count); position += count; return value;
        }
        private string Unicode()
        {
            int count = Count(65536); Need(count * 2);
            string value = Encoding.BigEndianUnicode.GetString(data, position, count * 2); position += count * 2;
            return value.TrimEnd('\0');
        }
        private double Double()
        {
            long bits = unchecked((long)((ulong)U32() << 32 | U32()));
            double value = BitConverter.Int64BitsToDouble(bits);
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("GRD numbers must be finite.");
            return value;
        }
        internal Descriptor ReadDescriptor(int depth = 0)
        {
            Guard(depth);
            Unicode();
            var result = new Descriptor { ClassId = Id() };
            int count = Count(4096);
            for (int i = 0; i < count; i++)
            {
                string key = Id();
                if (result.ContainsKey(key)) throw new InvalidDataException("Duplicate GRD descriptor key: " + key);
                result.Add(key, ReadValue(Code(), depth + 1));
            }
            return result;
        }
        private object ReadValue(string type, int depth)
        {
            Guard(depth);
            switch (type)
            {
                case "Objc": case "GlbO": return ReadDescriptor(depth + 1);
                case "VlLs":
                    int count = Count(4096);
                    var list = new List<object>(count);
                    for (int i = 0; i < count; i++) list.Add(ReadValue(Code(), depth + 1));
                    return list;
                case "enum": return new EnumValue(Id(), Id());
                case "UntF": return new UnitValue(Code(), Double());
                case "doub": return Double();
                case "long": return unchecked((int)U32());
                case "TEXT": return Unicode();
                case "bool": return Byte() != 0;
                case "type": case "GlbC": Unicode(); return Id();
                case "comp": U32(); U32(); return null;
                case "tdta": case "alis": case "Pth ": Skip(Count(data.Length)); return null;
                default: throw new InvalidDataException("Unsupported GRD descriptor value type: " + type);
            }
        }
        private void Guard(int depth)
        {
            if (depth > 32 || ++valuesRead > 2000000) throw new InvalidDataException("GRD descriptor nesting or value limit exceeded.");
        }
    }
}
