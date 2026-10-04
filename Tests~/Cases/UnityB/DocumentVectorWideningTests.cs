using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

// run_script, entry DocumentVectorWideningTests.Run. In-memory only; no assets or windows.
public static class DocumentVectorWideningTests
{
    [Serializable]
    public class Automatic
    {
        public Vector3 offset = new Vector3(9, 8, 7);
        public int tail;
    }

    [Serializable]
    public class Manual : IWhimTexDocumentSerializable
    {
        public Vector3 offset;
        public int tail;
        public void WriteDocument(IWhimTexDocumentWriter writer)
        {
            writer.Begin(2);
            writer.Write("offset", offset);
            writer.Write("tail", tail);
        }
        public void ReadDocument(IWhimTexDocumentReader reader)
        {
            int count = reader.Count;
            for (int i = 0; i < count; i++)
                switch (reader.NextName())
                {
                    case "offset": offset = reader.ReadVector3(); break;
                    case "tail": tail = reader.ReadInt(); break;
                    default: reader.Skip(); break;
                }
        }
    }

    [Serializable]
    public class VectorFields
    {
        public Vector2 v2;
        public Vector3 v3;
        public Vector4 v4;
        public Vector2Int i2;
        public Vector3Int i3;
        public int tail;
    }

    [Serializable]
    public class ManualVectorFields : VectorFields, IWhimTexDocumentSerializable
    {
        public void WriteDocument(IWhimTexDocumentWriter writer)
        {
            writer.Begin(6);
            writer.Write("v2", v2); writer.Write("v3", v3); writer.Write("v4", v4);
            writer.Write("i2", i2); writer.Write("i3", i3); writer.Write("tail", tail);
        }
        public void ReadDocument(IWhimTexDocumentReader reader)
        {
            int count = reader.Count;
            for (int i = 0; i < count; i++)
                switch (reader.NextName())
                {
                    case "v2": v2 = reader.ReadVector2(); break;
                    case "v3": v3 = reader.ReadVector3(); break;
                    case "v4": v4 = reader.ReadVector4(); break;
                    case "i2": i2 = reader.ReadVector2Int(); break;
                    case "i3": i3 = reader.ReadVector3Int(); break;
                    case "tail": tail = reader.ReadInt(); break;
                    default: reader.Skip(); break;
                }
        }
    }

    static readonly Type Serializer = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int assertions;

    static void Check(bool condition, string message)
    {
        assertions++;
        UnityBRun.Check(!(!condition), message);
    }

    static byte[] Payload(Type type, byte tag, params float[] components)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1); // Existing format version.
        writer.Write((byte)29); // Object, ordinary named-field encoding.
        writer.Write(type.FullName);
        writer.Write(2);
        writer.Write("offset");
        writer.Write(tag);
        foreach (float component in components) writer.Write(component);
        writer.Write("tail");
        writer.Write((byte)6);
        writer.Write(1234);
        return stream.ToArray();
    }

    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object readResult;
    static object Read(byte[] bytes, Type type, WhimTexDocumentContainer container)
    {
        readResult = Serializer.GetMethod("Deserialize", Flags).Invoke(null, new object[] { bytes, container, type, null, false });
        return readResult.GetType().GetProperty("Model", Instance).GetValue(readResult);
    }
    static IReadOnlyList<string> Skipped => (IReadOnlyList<string>)readResult.GetType().GetProperty("SkippedFields", Instance).GetValue(readResult);
    static Vector3 Offset(object value) => (Vector3)value.GetType().GetField("offset").GetValue(value);

    static byte[] Write(object value, WhimTexDocumentContainer container)
        => (byte[])Serializer.GetMethod("Serialize", Flags).Invoke(null, new[] { value, container });

    static void FieldCase(Type type, string name, object source, object expected, WhimTexDocumentContainer container)
    {
        if (source.GetType() != type.GetField(name).FieldType) expected = null;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write((byte)29);
        writer.Write(type.FullName);
        writer.Write(2);
        writer.Write(name);
        byte[] tagged = Write(source, container);
        writer.Write(tagged, 4, tagged.Length - 4); // Strip only the root format version.
        writer.Write("tail"); writer.Write((byte)6); writer.Write(1234);
        object result = Read(stream.ToArray(), type, container);
        Check((int)type.GetField("tail").GetValue(result) == 1234, "Field alignment: " + name);
        if (expected == null)
        {
            Check(Skipped.Count == 1 && Skipped[0] == type.Name + "." + name, "Incompatible field guard: " + name);
            return;
        }
        Check(Skipped.Count == 0, "Widening must not skip: " + name);
        Check(type.GetField(name).GetValue(result).Equals(expected), "Widening components: " + name);
        object restored = Read(Write(result, container), type, container);
        Check(Skipped.Count == 0 && type.GetField(name).GetValue(restored).Equals(expected), "Typed roundtrip: " + name);
    }

    static string ExecuteRun()
    {
        assertions = 0;
        // run_script loads a new transient assembly each run. Bind only these test fixtures
        // to this run instead of an earlier loaded assembly with identical full names.
        var knownTypes = (Dictionary<string, Type>)Serializer.GetField("KnownTypes", Flags).GetValue(null);
        foreach (Type fixture in new[] { typeof(Automatic), typeof(Manual), typeof(VectorFields), typeof(ManualVectorFields) })
            knownTypes[fixture.FullName] = fixture;
        using var container = new WhimTexDocumentContainer();
        try
        {
            foreach (Type type in new[] { typeof(Automatic), typeof(Manual) })
            {
                var values = new[] { new[] { -2.25f }, new[] { -2.25f, 17.5f }, new[] { -2.25f, 17.5f, 3.75f } };
                byte[] tags = { 10, 15, 16 };
                for (int i = 0; i < tags.Length; i++)
                {
                    object value = Read(Payload(type, tags[i], values[i]), type, container);
                    if (tags[i] != 16)
                    {
                        Check(Skipped.Count == 1 && Skipped[0] == type.Name + ".offset", "Historical coercion rejected");
                        Check((int)type.GetField("tail").GetValue(value) == 1234, "Rejected value stream alignment");
                        continue;
                    }
                    var expected = new Vector3(values[i][0], i > 0 ? values[i][1] : 0, i > 1 ? values[i][2] : 0);
                    Check(Offset(value).Equals(expected), type.Name + " component order / zero filling");
                    Check(Skipped.Count == 0, "Compatible field must not block Save");
                    Check((int)type.GetField("tail").GetValue(value) == 1234, "Stream alignment");
                    type.GetField("offset").SetValue(value, new Vector3(expected.x, expected.y, -6.5f));
                    byte[] saved = (byte[])Serializer.GetMethod("Serialize", Flags).Invoke(null, new[] { value, container });
                    using (var reader = new BinaryReader(new MemoryStream(saved)))
                    {
                        Check(reader.ReadInt32() == 1 && reader.ReadByte() == 29, "Unchanged format");
                        reader.ReadString(); reader.ReadInt32();
                        Check(reader.ReadString() == "offset" && reader.ReadByte() == 16, "Writer persists Vector3 tag");
                    }
                    object restored = Read(saved, type, container);
                    Check(Offset(restored).Equals(new Vector3(expected.x, expected.y, -6.5f)), "Roundtrip preserves edited Z");
                }
                object incompatible = Read(Payload(type, 17, 1, 2, 3, 4), type, container);
                Check(Skipped.Count == 1 && Skipped[0] == type.Name + ".offset", "Vector4 narrowing must remain protected");
                Check((int)type.GetField("tail").GetValue(incompatible) == 1234, "Rejected value still consumed completely");
            }

            var noise = (NoiseLayerBehaviour)Read(Payload(typeof(NoiseLayerBehaviour), 15, 4.5f, -7.25f), typeof(NoiseLayerBehaviour), container);
            Check(noise.offset == Vector3.zero, "Noise does not expand an obsolete Vector2 offset");
            Check(Skipped.Count == 2 && System.Linq.Enumerable.Contains(Skipped, "NoiseLayerBehaviour.offset") && System.Linq.Enumerable.Contains(Skipped, "NoiseLayerBehaviour.tail"), "Both obsolete and unknown fields protected");
            foreach (Type type in new[] { typeof(VectorFields), typeof(ManualVectorFields) })
            {
                FieldCase(type, "v2", -1.25f, new Vector2(-1.25f, 0), container);
                FieldCase(type, "v3", -1.25f, new Vector3(-1.25f, 0, 0), container);
                FieldCase(type, "v4", -1.25f, new Vector4(-1.25f, 0, 0, 0), container);
                FieldCase(type, "v3", new Vector2(-1.25f, 2.5f), new Vector3(-1.25f, 2.5f, 0), container);
                FieldCase(type, "v4", new Vector2(-1.25f, 2.5f), new Vector4(-1.25f, 2.5f, 0, 0), container);
                FieldCase(type, "v4", new Vector3(-1.25f, 2.5f, 7.75f), new Vector4(-1.25f, 2.5f, 7.75f, 0), container);
                FieldCase(type, "i2", int.MinValue, new Vector2Int(int.MinValue, 0), container);
                FieldCase(type, "i3", int.MaxValue, new Vector3Int(int.MaxValue, 0, 0), container);
                FieldCase(type, "i3", new Vector2Int(int.MinValue, int.MaxValue), new Vector3Int(int.MinValue, int.MaxValue, 0), container);
                FieldCase(type, "v2", new Vector2(3, 4), new Vector2(3, 4), container);
                FieldCase(type, "v3", new Vector3(3, 4, 5), new Vector3(3, 4, 5), container);
                FieldCase(type, "v4", new Vector4(3, 4, 5, 6), new Vector4(3, 4, 5, 6), container);
                FieldCase(type, "i2", new Vector2Int(int.MaxValue, int.MinValue), new Vector2Int(int.MaxValue, int.MinValue), container);
                FieldCase(type, "i3", new Vector3Int(int.MaxValue, int.MinValue, -7), new Vector3Int(int.MaxValue, int.MinValue, -7), container);
                FieldCase(type, "v2", new Vector3(1, 2, 3), null, container);
                FieldCase(type, "v2", new Vector4(1, 2, 3, 4), null, container);
                FieldCase(type, "v3", new Vector4(1, 2, 3, 4), null, container);
                FieldCase(type, "i2", new Vector3Int(1, 2, 3), null, container);
                FieldCase(type, "v2", new Vector2Int(-123, 456), new Vector2(-123, 456), container);
                FieldCase(type, "v3", new Vector2Int(-123, 456), new Vector3(-123, 456, 0), container);
                FieldCase(type, "v4", new Vector2Int(-123, 456), new Vector4(-123, 456, 0, 0), container);
                FieldCase(type, "v3", new Vector3Int(-123, 456, 789), new Vector3(-123, 456, 789), container);
                FieldCase(type, "v4", new Vector3Int(-123, 456, 789), new Vector4(-123, 456, 789, 0), container);
                FieldCase(type, "v3", new Vector2Int(int.MinValue, 16777218), new Vector3(int.MinValue, 16777218, 0), container);
                FieldCase(type, "v2", new Vector3Int(1, 2, 3), null, container);
                FieldCase(type, "v4", new Vector2Int(int.MaxValue, 1), null, container);
                FieldCase(type, "v4", new Vector2Int(1, -16777217), null, container);
                FieldCase(type, "v3", new Vector3Int(1, 2, 16777217), null, container);
                FieldCase(type, "i3", new Vector2(1.25f, 2.5f), null, container);
                FieldCase(type, "i3", 2.5f, null, container);
                FieldCase(type, "v3", 2, null, container);
                FieldCase(type, "v4", new Color(1, 2, 3, 4), null, container);
                FieldCase(type, "v4", new Quaternion(1, 2, 3, 4), null, container);
            }
            return "";
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    public static string Run() => UnityBRun.Run("DocumentVectorWideningSmoke.Run", () => ExecuteRun());
}

