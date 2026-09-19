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
            _missingTypes.Clear();
            _missingTypeNames.Clear();
            _skippedFieldNames.Clear();
            _skippedFields.Clear();
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

        // --- migrations: the payload stores names, so names are what keeps old documents loadable ---

        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> FieldNames =
            new Dictionary<Type, Dictionary<string, FieldInfo>>();
        private static readonly Dictionary<string, Type> MovedTypeNames = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly List<string> _skippedFields = new List<string>();
        private static readonly HashSet<string> _skippedFieldNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Fields the last loaded document carried but this build no longer declares.</summary>
        public static IReadOnlyList<string> LastSkippedFields => _skippedFields;

        /// <summary>
        /// Every name a field was ever serialized under. Field order never mattered because the value
        /// carries its name, and a renamed field keeps loading because [FormerlySerializedAs] names are
        /// part of the map.
        /// </summary>
        private static Dictionary<string, FieldInfo> FieldNameMap(Type type)
        {
            lock (FieldNames)
            {
                if (FieldNames.TryGetValue(type, out Dictionary<string, FieldInfo> cached)) return cached;
            }
            var map = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (FieldInfo field in Fields(type))
            {
                if (!map.ContainsKey(field.Name)) map[field.Name] = field;
                foreach (object old in AttributesNamed(field, "FormerlySerializedAsAttribute"))
                {
                    string former = StringMember(old, "name");
                    if (string.IsNullOrEmpty(former) || map.ContainsKey(former)) continue;
                    map[former] = field;
                }
            }
            lock (FieldNames) FieldNames[type] = map;
            return map;
        }

        private static void RecordSkippedField(Type type, string name)
        {
            string label = type.Name + "." + name;
            if (_skippedFieldNames.Add(label)) _skippedFields.Add(label);
        }

        /// <summary>
        /// A type is stored by name, so a renamed or moved type is found through its [MovedFrom] markers.
        /// This runs only when a plain lookup failed, which is exactly the migration case.
        /// </summary>
        private static Type FindMovedType(string name)
        {
            lock (MovedTypeNames)
            {
                if (MovedTypeNames.TryGetValue(name, out Type cached)) return cached;
            }
            string simple = name;
            string space = null;
            int dot = name.LastIndexOf('.');
            if (dot > 0)
            {
                space = name.Substring(0, dot);
                simple = name.Substring(dot + 1);
            }
            Type found = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (Exception) { continue; }
                foreach (Type type in types)
                {
                    if (!MatchesMovedName(type, name, simple, space)) continue;
                    found = type;
                    break;
                }
                if (found != null) break;
            }
            lock (MovedTypeNames) MovedTypeNames[name] = found;
            return found;
        }

        /// <summary>
        /// MovedFrom lives in a namespace that moved between Unity versions and its members differ too, so
        /// the attribute and its strings are read by name instead of by type.
        /// </summary>
        private static bool MatchesMovedName(Type type, string name, string simple, string space)
        {
            foreach (object attribute in AttributesNamed(type, "MovedFromAttribute"))
            {
                string oldName = StringMember(attribute, "name");
                if (string.IsNullOrEmpty(oldName)) oldName = type.Name;
                string oldSpace = StringMember(attribute, "namespace");
                if (oldName == simple && (oldSpace == null || space == null || oldSpace == space)) return true;
                string full = oldSpace == null ? oldName : oldSpace + "." + oldName;
                if (full == name) return true;
            }
            return false;
        }

        /// <summary>Attributes are matched by type name: a rename or a namespace move must not break a load.</summary>
        private static IEnumerable<object> AttributesNamed(MemberInfo member, string attributeName)
        {
            object[] attributes;
            try { attributes = member.GetCustomAttributes(false); }
            catch (Exception) { yield break; }
            foreach (object attribute in attributes)
                if (attribute != null && attribute.GetType().Name == attributeName) yield return attribute;
        }

        private static string StringMember(object attribute, string namePart)
        {
            foreach (PropertyInfo property in attribute.GetType().GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.PropertyType != typeof(string)) continue;
                if (property.Name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (property.GetValue(attribute) is string value && !string.IsNullOrEmpty(value)) return value;
            }
            return null;
        }

        /// <summary>Types the last loaded document referenced but this build does not have.</summary>
        public static IReadOnlyList<string> LastMissingTypes => _missingTypes;

        private static readonly List<string> _missingTypes = new List<string>();
        private static readonly HashSet<string> _missingTypeNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Returns null for a type this build does not know. A document written by another version of
        /// the package must stay readable: an unknown layer behaviour is dropped with a report instead
        /// of failing the whole load.
        /// </summary>
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
            if (resolved == null) resolved = FindMovedType(name);
            if (resolved == null)
            {
                if (_missingTypeNames.Add(name)) _missingTypes.Add(name);
                return null;
            }
            KnownTypes[name] = resolved;
            return resolved;
        }

        private static bool IsEightBit(TextureFormat format) =>
            format == TextureFormat.RGBA32 || format == TextureFormat.ARGB32 || format == TextureFormat.RGB24 ||
            format == TextureFormat.Alpha8 || format == TextureFormat.R8;

        // --- writer ---

        private sealed class Writer : IWhimTexDocumentWriter
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

            /// <summary>FNV-1a in four independent streams, straight over native pixels: no managed copy is made.</summary>
            private static ulong Hash64(Unity.Collections.NativeArray<byte> data)
            {
                const ulong prime = 1099511628211UL;
                ulong h1 = 14695981039346656037UL, h2 = h1 ^ 0x9E3779B97F4A7C15UL;
                ulong h3 = h1 ^ 0xBF58476D1CE4E5B9UL, h4 = h1 ^ 0x94D049BB133111EBUL;
                System.ReadOnlySpan<byte> span = data.AsSpan();
                int i = 0;
                int limit = span.Length - 31;
                for (; i < limit; i += 32)
                {
                    h1 = (h1 ^ BitConverter.ToUInt64(span.Slice(i))) * prime;
                    h2 = (h2 ^ BitConverter.ToUInt64(span.Slice(i + 8))) * prime;
                    h3 = (h3 ^ BitConverter.ToUInt64(span.Slice(i + 16))) * prime;
                    h4 = (h4 ^ BitConverter.ToUInt64(span.Slice(i + 24))) * prime;
                }
                for (; i < span.Length - 7; i += 8) h1 = (h1 ^ BitConverter.ToUInt64(span.Slice(i))) * prime;
                for (; i < span.Length; i++) h1 = (h1 ^ span[i]) * prime;
                return ((h1 ^ h2) * prime ^ h3) * prime ^ h4;
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
                // Pixels stay in native memory: copying every layer into the managed heap would hand the
                // garbage collector hundreds of megabytes per save, and the container frees what it owns.
                Unity.Collections.NativeArray<byte> view = texture.GetRawTextureData<byte>();
                var pixels = new Unity.Collections.NativeArray<byte>(view.Length, Unity.Collections.Allocator.Persistent);
                Unity.Collections.NativeArray<byte>.Copy(view, pixels);
                // Layer pixels dominate save time, and a save only ever changes a few layers. The key is a
                // content identity, so an unchanged layer is not deflated again and identical pixels share
                // one block even across layers or documents.
                string cacheKey = pixels.Length + ":" + Hash64(pixels).ToString("x16");
                _container.SetNative(block, cacheKey, pixels, System.IO.Compression.CompressionLevel.Fastest);
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

            // --- manual mode: a type writes its own fields, with the automatic encoding ---

            private int _manualRemaining = -1;

            public void Begin(int count)
            {
                if (_manualRemaining >= 0)
                    throw new WhimTexDocumentException("A manually serialized type called Begin twice.");
                _manualRemaining = count;
                _writer.Write(count);
            }

            private void Named(string name, object value, Type type)
            {
                if (_manualRemaining < 0)
                    throw new WhimTexDocumentException("A manually serialized type wrote a value before Begin.");
                _manualRemaining--;
                _writer.Write(name);
                Write(value, type);
            }

            public void Write(string name, int value) => Named(name, value, typeof(int));
            public void Write(string name, long value) => Named(name, value, typeof(long));
            public void Write(string name, float value) => Named(name, value, typeof(float));
            public void Write(string name, double value) => Named(name, value, typeof(double));
            public void Write(string name, bool value) => Named(name, value, typeof(bool));
            public void Write(string name, char value) => Named(name, value, typeof(char));
            public void Write(string name, string value) => Named(name, value, typeof(string));
            public void Write(string name, Vector2 value) => Named(name, value, typeof(Vector2));
            public void Write(string name, Vector3 value) => Named(name, value, typeof(Vector3));
            public void Write(string name, Vector4 value) => Named(name, value, typeof(Vector4));
            public void Write(string name, Vector2Int value) => Named(name, value, typeof(Vector2Int));
            public void Write(string name, Vector3Int value) => Named(name, value, typeof(Vector3Int));
            public void Write(string name, Quaternion value) => Named(name, value, typeof(Quaternion));
            public void Write(string name, Color value) => Named(name, value, typeof(Color));
            public void Write(string name, Color32 value) => Named(name, value, typeof(Color32));
            public void Write(string name, Rect value) => Named(name, value, typeof(Rect));
            public void Write(string name, RectInt value) => Named(name, value, typeof(RectInt));
            public void Write(string name, Bounds value) => Named(name, value, typeof(Bounds));
            public void Write(string name, AnimationCurve value) => Named(name, value, typeof(AnimationCurve));
            public void WriteEnum<T>(string name, T value) where T : struct, Enum => Named(name, value, typeof(T));
            public void WriteObject(string name, object value, Type type) => Named(name, value, type);
            public void WriteList(string name, IList value, Type type) => Named(name, value, type);
            public void WriteReference(string name, UnityEngine.Object value) => Named(name, value, typeof(UnityEngine.Object));
            public void WriteTexture(string name, Texture2D value) => Named(name, value, typeof(Texture2D));

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
                if (value is IWhimTexDocumentSerializable manual)
                {
                    // A manual type writes its own field names and values, but takes the same shape on the
                    // wire: the count it declares is checked against what it actually wrote.
                    _manualRemaining = -1;
                    manual.WriteDocument(this);
                    if (_manualRemaining != 0)
                        throw new WhimTexDocumentException("A manually serialized type wrote a value count that does not match its fields: " + type.FullName + ".");
                    return;
                }
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

        private sealed class Reader : IWhimTexDocumentReader
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
                if (type == null) return null;
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
                // The texture owns the pixels now, so the inflated copy can be collected.
                _container.Remove(block);
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
                for (int i = 0; i < count; i++)
                {
                    // A dropped unknown object must not leave nulls in the document graph.
                    object item = Read(element);
                    if (item != null) list.Add(item);
                }
                return list;
            }

            private object ReadObject()
            {
                Type type = ResolveType(_reader.ReadString());
                int fieldCount = _reader.ReadInt32();
                if (type == null)
                {
                    // Keep the object numbering in step with the writer, then consume the fields.
                    _objects.Add(null);
                    for (int i = 0; i < fieldCount; i++)
                    {
                        _reader.ReadString();
                        Read(null);
                    }
                    return null;
                }
                object instance = typeof(ScriptableObject).IsAssignableFrom(type)
                    ? ScriptableObject.CreateInstance(type)
                    : Activator.CreateInstance(type, true);
                _objects.Add(instance);
                if (instance is IWhimTexDocumentSerializable manual)
                {
                    // A manual type consumes its own values and must read every one of them: a value left
                    // behind would silently shift everything that follows in the stream.
                    _manualCount = -1;
                    manual.ReadDocument(this);
                    if (_manualCount < 0)
                        throw new WhimTexDocumentException("A manually deserialized type did not read its value count: " + type.FullName + ".");
                }
                else
                {
                    for (int i = 0; i < fieldCount; i++)
                    {
                        string fieldName = _reader.ReadString();
                        FieldInfo field = FindField(type, fieldName);
                        object value = Read(field?.FieldType);
                        if (field == null) continue;
                        try
                        {
                            if (value != null || !field.FieldType.IsValueType) field.SetValue(instance, value);
                        }
                        catch (ArgumentException) { }
                        catch (InvalidCastException) { }
                    }
                }
                if (instance is ISerializationCallbackReceiver receiver) receiver.OnAfterDeserialize();
                return instance;
            }

            private static FieldInfo FindField(Type type, string name)
            {
                // Index and base type are irrelevant: the value in the payload carries its field name, and
                // the map answers to former names too. An unknown name is reported, never dropped quietly.
                if (FieldNameMap(type).TryGetValue(name, out FieldInfo known)) return known;
                RecordSkippedField(type, name);
                return null;
            }

            private static T ParseEnum<T>(string name) where T : struct => (T)Enum.Parse(typeof(T), name);

            // --- manual mode: a type reads its own fields, in the automatic encoding ---

            private int _manualCount = -1;

            public int Count
            {
                get
                {
                    if (_manualCount < 0) _manualCount = _reader.ReadInt32();
                    return _manualCount;
                }
            }

            public string NextName() => _reader.ReadString();
            public int ReadInt() => (int)Read(typeof(int));
            public long ReadLong() => (long)Read(typeof(long));
            public float ReadFloat() => (float)Read(typeof(float));
            public double ReadDouble() => (double)Read(typeof(double));
            public bool ReadBool() => (bool)Read(typeof(bool));
            public char ReadChar() => (char)Read(typeof(char));
            public string ReadString() => (string)Read(typeof(string));
            public Vector2 ReadVector2() => (Vector2)Read(typeof(Vector2));
            public Vector3 ReadVector3() => (Vector3)Read(typeof(Vector3));
            public Vector4 ReadVector4() => (Vector4)Read(typeof(Vector4));
            public Vector2Int ReadVector2Int() => (Vector2Int)Read(typeof(Vector2Int));
            public Vector3Int ReadVector3Int() => (Vector3Int)Read(typeof(Vector3Int));
            public Quaternion ReadQuaternion() => (Quaternion)Read(typeof(Quaternion));
            public Color ReadColor() => (Color)Read(typeof(Color));
            public Color32 ReadColor32() => (Color32)Read(typeof(Color32));
            public Rect ReadRect() => (Rect)Read(typeof(Rect));
            public RectInt ReadRectInt() => (RectInt)Read(typeof(RectInt));
            public Bounds ReadBounds() => (Bounds)Read(typeof(Bounds));
            AnimationCurve IWhimTexDocumentReader.ReadCurve() => (AnimationCurve)Read(typeof(AnimationCurve));
            public T ReadEnum<T>() where T : struct, Enum => (T)Read(typeof(T));
            public object ReadObject(Type type) => Read(type);
            // These names already exist on the reader's own value dispatch, so they are implemented
            // explicitly: through the interface they mean "read the next value as this type".
            IList IWhimTexDocumentReader.ReadList(Type type) => (IList)Read(type);
            UnityEngine.Object IWhimTexDocumentReader.ReadReference() => Read(null) as UnityEngine.Object;
            Texture2D IWhimTexDocumentReader.ReadTexture() => Read(typeof(Texture2D)) as Texture2D;
            public void Skip() => Read(null);
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
