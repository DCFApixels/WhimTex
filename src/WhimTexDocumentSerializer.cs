using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Serializes a WhimTex document model into the document block of a container.
    ///
    /// The walk is driven by the serializable fields of each type, so every layer behaviour and every
    /// future field is covered without a per-type writer. Rules that matter:
    ///   * Unity's own serialization rule is reproduced: public or [SerializeField], never static,
    ///     readonly or [NonSerialized];
    ///   * walking stops at UnityEngine/UnityEditor types, which are written by explicit handlers;
    ///   * the concrete type name is written for every object, so [SerializeReference] polymorphism
    ///     and nested lists survive without reflection into Unity's managed references;
    ///   * enums are written by name, because several enums in the model have non-sequential values;
    ///   * doubles are written as doubles: layer transforms and projective matrices must not lose precision;
    ///   * textures owned by the document go into container blocks, external assets are referenced by
    ///     `guid:localId`.
    /// </summary>
    internal static class WhimTexDocumentSerializer
    {
        private const int FormatVersion = 1;

        private const byte TagNull = 0;
        private const byte TagBool = 1;
        private const byte TagByte = 2;
        private const byte TagSByte = 3;
        private const byte TagShort = 4;
        private const byte TagUShort = 5;
        private const byte TagInt = 6;
        private const byte TagUInt = 7;
        private const byte TagLong = 8;
        private const byte TagULong = 9;
        private const byte TagFloat = 10;
        private const byte TagDouble = 11;
        private const byte TagChar = 12;
        private const byte TagString = 13;
        private const byte TagEnum = 14;
        private const byte TagVector2 = 15;
        private const byte TagVector3 = 16;
        private const byte TagVector4 = 17;
        private const byte TagVector2Int = 18;
        private const byte TagVector3Int = 19;
        private const byte TagQuaternion = 20;
        private const byte TagColor = 21;
        private const byte TagColor32 = 22;
        private const byte TagRect = 23;
        private const byte TagRectInt = 24;
        private const byte TagBounds = 25;
        private const byte TagCurve = 26;
        private const byte TagReference = 27;
        private const byte TagTexture = 28;
        private const byte TagObject = 29;
        private const byte TagList = 30;
        private const byte TagObjectRef = 31;

        /// <summary>Derived or transient state: rebuilt from the rest of the document, never stored.</summary>
        private static readonly HashSet<string> SkippedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "compiledShader", "appliedCode", "appliedSource", "appliedParameters", "diagnostics",
            "lastApplyFailed", "shaderCreationRecorded", "embeddedOwner", "transformCache",
            "outputTexture", "outputSprite", "sliceOutputs"
        };

        private static readonly Dictionary<string, Type> KnownTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        public static byte[] Serialize(object root, WhimTexDocumentContainer container)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(FormatVersion);
                var context = new Writer(writer, container);
                context.Write(root, root == null ? typeof(object) : root.GetType());
            }
            return stream.ToArray();
        }

        public static object Deserialize(byte[] bytes, WhimTexDocumentContainer container, Type expectedType)
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new WhimTexDocumentException("Unsupported document payload version: " + version + ".");
            var context = new Reader(reader, container);
            return context.Read(expectedType);
        }

        // --- fields ---

        private static FieldInfo[] Fields(Type type)
        {
            var fields = new List<FieldInfo>();
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                string space = current.Namespace ?? string.Empty;
                if (space.StartsWith("UnityEngine", StringComparison.Ordinal) || space.StartsWith("UnityEditor", StringComparison.Ordinal))
                    break;
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized) continue;
                    // [SerializeReference] alone is enough for a private field: Unity serializes those too,
                    // and the whole layer behaviour polymorphism relies on it.
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null &&
                        field.GetCustomAttribute<SerializeReference>() == null) continue;
                    if (SkippedFields.Contains(field.Name)) continue;
                    fields.Add(field);
                }
            }
            return fields.ToArray();
        }

        /// <summary>Unity types are never walked by reflection: each one needs an explicit handler.</summary>
        private static bool IsUnityType(Type type)
        {
            string space = type.Namespace ?? string.Empty;
            return space.StartsWith("UnityEngine", StringComparison.Ordinal) || space.StartsWith("UnityEditor", StringComparison.Ordinal);
        }

        private static Type ResolveType(string name)
        {
            if (KnownTypes.TryGetValue(name, out Type known)) return known;
            Type resolved = typeof(WhimTexDocumentSerializer).Assembly.GetType(name);
            if (resolved == null)
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    resolved = assembly.GetType(name);
                    if (resolved != null) break;
                }
            if (resolved == null) throw new WhimTexDocumentException("Unknown type in the document: " + name + ".");
            KnownTypes[name] = resolved;
            return resolved;
        }

        private static bool IsEightBit(TextureFormat format) =>
            format == TextureFormat.RGBA32 || format == TextureFormat.ARGB32 || format == TextureFormat.RGB24 ||
            format == TextureFormat.Alpha8 || format == TextureFormat.R8;

        // --- writer ---

        private sealed class Writer
        {
            private readonly BinaryWriter _writer;
            private readonly WhimTexDocumentContainer _container;
            private readonly Dictionary<object, int> _ids = new Dictionary<object, int>(ReferenceComparer.Instance);
            private readonly List<object> _objects = new List<object>();
            private int _textureIndex;

            public Writer(BinaryWriter writer, WhimTexDocumentContainer container)
            {
                _writer = writer;
                _container = container;
            }

            public void Write(object value, Type declared)
            {
                if (value == null) { _writer.Write(TagNull); return; }
                Type type = value.GetType();
                if (type.IsEnum)
                {
                    _writer.Write(TagEnum);
                    _writer.Write(type.FullName);
                    _writer.Write(value.ToString());
                    _writer.Write(Convert.ToInt64(value));
                    return;
                }
                switch (value)
                {
                    case bool v: _writer.Write(TagBool); _writer.Write(v); return;
                    case byte v: _writer.Write(TagByte); _writer.Write(v); return;
                    case sbyte v: _writer.Write(TagSByte); _writer.Write(v); return;
                    case short v: _writer.Write(TagShort); _writer.Write(v); return;
                    case ushort v: _writer.Write(TagUShort); _writer.Write(v); return;
                    case int v: _writer.Write(TagInt); _writer.Write(v); return;
                    case uint v: _writer.Write(TagUInt); _writer.Write(v); return;
                    case long v: _writer.Write(TagLong); _writer.Write(v); return;
                    case ulong v: _writer.Write(TagULong); _writer.Write(v); return;
                    case float v: _writer.Write(TagFloat); _writer.Write(v); return;
                    case double v: _writer.Write(TagDouble); _writer.Write(v); return;
                    case char v: _writer.Write(TagChar); _writer.Write(v); return;
                    case string v: _writer.Write(TagString); _writer.Write(v); return;
                    case Vector2 v: _writer.Write(TagVector2); _writer.Write(v.x); _writer.Write(v.y); return;
                    case Vector3 v: _writer.Write(TagVector3); _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.z); return;
                    case Vector4 v: _writer.Write(TagVector4); _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.z); _writer.Write(v.w); return;
                    case Vector2Int v: _writer.Write(TagVector2Int); _writer.Write(v.x); _writer.Write(v.y); return;
                    case Vector3Int v: _writer.Write(TagVector3Int); _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.z); return;
                    case Quaternion v: _writer.Write(TagQuaternion); _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.z); _writer.Write(v.w); return;
                    case Color v: _writer.Write(TagColor); _writer.Write(v.r); _writer.Write(v.g); _writer.Write(v.b); _writer.Write(v.a); return;
                    case Color32 v: _writer.Write(TagColor32); _writer.Write(v.r); _writer.Write(v.g); _writer.Write(v.b); _writer.Write(v.a); return;
                    case Rect v: _writer.Write(TagRect); _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.width); _writer.Write(v.height); return;
                    case RectInt v: _writer.Write(TagRectInt); _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.width); _writer.Write(v.height); return;
                    case Bounds v:
                        _writer.Write(TagBounds);
                        _writer.Write(v.center.x); _writer.Write(v.center.y); _writer.Write(v.center.z);
                        _writer.Write(v.size.x); _writer.Write(v.size.y); _writer.Write(v.size.z);
                        return;
                    case AnimationCurve v: WriteCurve(v); return;
                    case Texture2D v: WriteTexture(v); return;
                }
                if (value is UnityEngine.Object unityObject) { WriteReference(unityObject); return; }
                if (value is IList list) { WriteList(list, type); return; }
                WriteObject(value, type);
            }

            private void WriteCurve(AnimationCurve curve)
            {
                Keyframe[] keys = curve.keys;
                _writer.Write(TagCurve);
                _writer.Write(curve.preWrapMode.ToString());
                _writer.Write(curve.postWrapMode.ToString());
                _writer.Write(keys.Length);
                foreach (Keyframe key in keys)
                {
                    _writer.Write(key.time); _writer.Write(key.value);
                    _writer.Write(key.inTangent); _writer.Write(key.outTangent);
                    _writer.Write(key.inWeight); _writer.Write(key.outWeight);
                    _writer.Write((int)key.weightedMode);
                }
            }

            private void WriteReference(UnityEngine.Object value)
            {
                if (value == null) { _writer.Write(TagNull); return; }
                if (!UnityEditor.AssetDatabase.Contains(value))
                {
                    // A document-owned object must not be lost: it is stored inline as an object graph.
                    WriteObject(value, value.GetType());
                    return;
                }
                _writer.Write(TagReference);
                UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId);
                _writer.Write(guid ?? string.Empty);
                _writer.Write(localId);
            }

            private void WriteTexture(Texture2D texture)
            {
                if (texture == null) { _writer.Write(TagNull); return; }
                if (UnityEditor.AssetDatabase.Contains(texture) && !IsDocumentOwned(texture))
                {
                    WriteReference(texture);
                    return;
                }
                if (TryWriteBackReference(texture)) return;
                if (!texture.isReadable)
                    throw new WhimTexDocumentException("Texture '" + texture.name + "' cannot be stored because it is not readable.");
                // Register before writing the tag: the reader registers the restored texture at the same
                // position, and back-reference ids must stay in sync on both sides.
                _ids[texture] = _objects.Count;
                _objects.Add(texture);
                _writer.Write(TagTexture);
                string block = "texture:" + _textureIndex++;
                _writer.Write(texture.width);
                _writer.Write(texture.height);
                _writer.Write(texture.format.ToString());
                _writer.Write(texture.mipmapCount);
                // isDataSRGB and the constructor's linear flag are opposites; store the constructor flag.
                _writer.Write(!texture.isDataSRGB);
                _writer.Write(block);
                byte[] raw = texture.GetRawTextureData<byte>().ToArray();
                _container.Set(block, raw, System.IO.Compression.CompressionLevel.Fastest);
            }

            /// <summary>A shared object is written once: the document must keep object identity across the graph.</summary>
            private bool TryWriteBackReference(object value)
            {
                if (!_ids.TryGetValue(value, out int id)) return false;
                _writer.Write(TagObjectRef);
                _writer.Write(id);
                return true;
            }

            private static bool IsDocumentOwned(Texture2D texture)
            {
                // Sub-assets of a legacy document asset are document data, not external references.
                string path = UnityEditor.AssetDatabase.GetAssetPath(texture);
                return !string.IsNullOrEmpty(path) && UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != texture
                       && !(UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) is Texture2D);
            }

            private void WriteList(IList list, Type type)
            {
                _writer.Write(TagList);
                _writer.Write(list.Count);
                Type element = type.IsArray ? type.GetElementType() : ElementType(type);
                foreach (object item in list) Write(item, element);
            }

            private void WriteObject(object value, Type type)
            {
                if (IsUnityType(type))
                    throw new WhimTexDocumentException("Unsupported Unity field type: " + type.FullName + ".");
                if (TryWriteBackReference(value)) return;
                _ids[value] = _objects.Count;
                _objects.Add(value);
                _writer.Write(TagObject);
                _writer.Write(type.FullName);
                if (value is ISerializationCallbackReceiver receiver) receiver.OnBeforeSerialize();
                FieldInfo[] fields = Fields(type);
                _writer.Write(fields.Length);
                foreach (FieldInfo field in fields)
                {
                    _writer.Write(field.Name);
                    Write(field.GetValue(value), field.FieldType);
                }
            }
        }

        // --- reader ---

        private sealed class Reader
        {
            private readonly BinaryReader _reader;
            private readonly WhimTexDocumentContainer _container;
            private readonly List<object> _objects = new List<object>();

            public Reader(BinaryReader reader, WhimTexDocumentContainer container)
            {
                _reader = reader;
                _container = container;
            }

            public object Read(Type declared)
            {
                byte tag = _reader.ReadByte();
                switch (tag)
                {
                    case TagNull: return null;
                    case TagBool: return _reader.ReadBoolean();
                    case TagByte: return _reader.ReadByte();
                    case TagSByte: return _reader.ReadSByte();
                    case TagShort: return _reader.ReadInt16();
                    case TagUShort: return _reader.ReadUInt16();
                    case TagInt: return _reader.ReadInt32();
                    case TagUInt: return _reader.ReadUInt32();
                    case TagLong: return _reader.ReadInt64();
                    case TagULong: return _reader.ReadUInt64();
                    case TagFloat: return _reader.ReadSingle();
                    case TagDouble: return _reader.ReadDouble();
                    case TagChar: return _reader.ReadChar();
                    case TagString: return _reader.ReadString();
                    case TagVector2: return new Vector2(_reader.ReadSingle(), _reader.ReadSingle());
                    case TagVector3: return new Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                    case TagVector4: return new Vector4(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                    case TagVector2Int: return new Vector2Int(_reader.ReadInt32(), _reader.ReadInt32());
                    case TagVector3Int: return new Vector3Int(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32());
                    case TagQuaternion: return new Quaternion(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                    case TagColor: return new Color(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                    case TagColor32: return new Color32(_reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte());
                    case TagRect: return new Rect(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                    case TagRectInt: return new RectInt(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32());
                    case TagBounds: return new Bounds((Vector3)Read(declared), (Vector3)Read(declared));
                    case TagCurve: return ReadCurve();
                    case TagReference: return ReadReference();
                    case TagTexture: return ReadTexture();
                    case TagEnum: return ReadEnum();
                    case TagList: return ReadList(declared);
                    case TagObject: return ReadObject();
                    case TagObjectRef:
                        int id = _reader.ReadInt32();
                        if (id < 0 || id >= _objects.Count) throw new WhimTexDocumentException("Invalid object reference " + id + " in the document.");
                        return _objects[id];
                    default: throw new WhimTexDocumentException("Unknown value tag " + tag + " in the document.");
                }
            }

            private object ReadCurve()
            {
                var curve = new AnimationCurve
                {
                    preWrapMode = ParseEnum<WrapMode>(_reader.ReadString()),
                    postWrapMode = ParseEnum<WrapMode>(_reader.ReadString())
                };
                int count = _reader.ReadInt32();
                var keys = new Keyframe[count];
                for (int i = 0; i < count; i++)
                    keys[i] = new Keyframe(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(),
                        _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle())
                    { weightedMode = (WeightedMode)_reader.ReadInt32() };
                curve.keys = keys;
                return curve;
            }

            private object ReadEnum()
            {
                string typeName = _reader.ReadString();
                string name = _reader.ReadString();
                long number = _reader.ReadInt64();
                Type type = ResolveType(typeName);
                return Enum.IsDefined(type, name) ? Enum.Parse(type, name) : Enum.ToObject(type, number);
            }

            private object ReadReference()
            {
                string guid = _reader.ReadString();
                long localId = _reader.ReadInt64();
                if (string.IsNullOrEmpty(guid) && localId == 0) return null;
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return null;
                if (localId == 0)
                {
                    var main = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
                    if (main != null) return main;
                    foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)) return asset;
                    return null;
                }
                foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id);
                    if (id == localId) return asset;
                }
                return UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
            }

            private object ReadTexture()
            {
                int width = _reader.ReadInt32();
                int height = _reader.ReadInt32();
                string formatName = _reader.ReadString();
                int mipCount = _reader.ReadInt32();
                bool linear = _reader.ReadBoolean();
                string block = _reader.ReadString();
                if (!_container.TryGet(block, out byte[] raw))
                    throw new WhimTexDocumentException("Texture block '" + block + "' is missing from the document.");
                var format = (TextureFormat)Enum.Parse(typeof(TextureFormat), formatName);
                var texture = new Texture2D(width, height, format, mipCount > 1, linear);
                _objects.Add(texture);
                texture.LoadRawTextureData(raw);
                texture.Apply(false, false);
                return texture;
            }

            private object ReadList(Type declared)
            {
                int count = _reader.ReadInt32();
                if (count < 0) throw new WhimTexDocumentException("Negative list length in the document.");
                Type element = declared != null && declared.IsArray ? declared.GetElementType() : ElementType(declared);
                if (declared != null && declared.IsArray)
                {
                    var array = Array.CreateInstance(element ?? typeof(object), count);
                    for (int i = 0; i < count; i++) array.SetValue(Read(element), i);
                    return array;
                }
                var listType = typeof(List<>).MakeGenericType(element ?? typeof(object));
                var list = (IList)Activator.CreateInstance(listType);
                for (int i = 0; i < count; i++) list.Add(Read(element));
                return list;
            }

            private object ReadObject()
            {
                Type type = ResolveType(_reader.ReadString());
                object instance = typeof(ScriptableObject).IsAssignableFrom(type)
                    ? ScriptableObject.CreateInstance(type)
                    : Activator.CreateInstance(type, true);
                _objects.Add(instance);
                int fieldCount = _reader.ReadInt32();
                for (int i = 0; i < fieldCount; i++)
                {
                    string fieldName = _reader.ReadString();
                    FieldInfo field = FindField(type, fieldName);
                    object value = Read(field?.FieldType);
                    if (field != null && value != null) field.SetValue(instance, value);
                    else if (field != null && !field.FieldType.IsValueType) field.SetValue(instance, null);
                }
                if (instance is ISerializationCallbackReceiver receiver) receiver.OnAfterDeserialize();
                return instance;
            }

            private static FieldInfo FindField(Type type, string name)
            {
                for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                {
                    string space = current.Namespace ?? string.Empty;
                    if (space.StartsWith("UnityEngine", StringComparison.Ordinal) || space.StartsWith("UnityEditor", StringComparison.Ordinal))
                        break;
                    FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                                                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field != null) return field;
                }
                return null;
            }

            private static T ParseEnum<T>(string name) where T : struct => (T)Enum.Parse(typeof(T), name);
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        private static Type ElementType(Type type)
        {
            if (type == null) return null;
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType) return type.GetGenericArguments()[0];
            return null;
        }
    }
}
