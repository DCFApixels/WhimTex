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
            "outputTexture", "outputSprite", "sliceOutputs", "documentLoadWarning", "documentBinding"
        };

        private static readonly Dictionary<string, Type> KnownTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, FieldInfo[]> CachedFields = new Dictionary<Type, FieldInfo[]>();

        public static byte[] Serialize(object root, WhimTexDocumentContainer container)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(FormatVersion);
                var context = new Writer(writer, container, root as TextureCompositor);
                context.Write(root, root == null ? typeof(object) : root.GetType());
            }
            if (stream.Length > 64L * 1024 * 1024) throw new WhimTexDocumentException("The document model exceeds the 64 MiB budget.");
            return stream.ToArray();
        }

        public static object Deserialize(byte[] bytes, WhimTexDocumentContainer container, Type expectedType,
            string sourcePath = null, bool deferDrawingTextures = false)
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
            _unresolvedReferences.Clear();
            var context = new Reader(reader, container, sourcePath, deferDrawingTextures);
            try
            {
                object result = context.Read(expectedType);
                if (result != null && expectedType != null && !expectedType.IsInstanceOfType(result))
                    throw new WhimTexDocumentException("Unexpected document root type.");
                if (stream.Position != stream.Length) throw new WhimTexDocumentException("Unexpected trailing model data.");
                return result;
            }
            catch (OperationCanceledException)
            {
                context.ReleaseCreatedObjects();
                throw;
            }
            catch (Exception error)
            {
                context.ReleaseCreatedObjects();
                throw new WhimTexDocumentException("Cannot read the document model: " + error.Message, error);
            }
        }

        // --- fields ---

        private static FieldInfo[] Fields(Type type)
        {
            lock (CachedFields) if (CachedFields.TryGetValue(type, out FieldInfo[] cached)) return cached;
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
            FieldInfo[] result = fields.ToArray();
            lock (CachedFields) CachedFields[type] = result;
            return result;
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

        /// <summary>How many fields the automatic pass writes for a type. Used to check generated code.</summary>
        internal static int ReflectedFieldCount(Type type) => Fields(type).Length;

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
            foreach (Assembly assembly in GetLoadedAssemblies())
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
            foreach (CustomAttributeData attribute in type.GetCustomAttributesData())
            {
                if (attribute.AttributeType != typeof(UnityEngine.Scripting.APIUpdating.MovedFromAttribute)) continue;
                var args = attribute.ConstructorArguments;
                string oldName = args.Count == 4 ? args[3].Value as string : null;
                string oldSpace = args.Count == 4 ? args[1].Value as string : args.Count == 1 ? args[0].Value as string : null;
                if (string.IsNullOrEmpty(oldName)) oldName = type.Name;
                if (oldSpace == null) oldSpace = type.Namespace;
                if ((string.IsNullOrEmpty(oldSpace) ? oldName : oldSpace + "." + oldName) == name) return true;
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
        public static IReadOnlyList<string> LastUnresolvedReferences => _unresolvedReferences;
        private static readonly List<string> _unresolvedReferences = new List<string>();

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
                foreach (Assembly assembly in GetLoadedAssemblies())
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

        // UnityEngine.Assemblies was added after the first Unity 6 releases. Keep the
        // compatibility path isolated here so the serializer compiles on 6000.0/6000.3
        // while newer editors avoid the AppDomain API (which Unity warns about).
        private static IEnumerable<Assembly> GetLoadedAssemblies()
        {
#if UNITY_6000_7_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        // --- writer ---

        private sealed class Writer : IWhimTexDocumentWriter
        {
            private readonly BinaryWriter _writer;
            private readonly WhimTexDocumentContainer _container;
            private readonly Dictionary<object, int> _ids = new Dictionary<object, int>(ReferenceComparer.Instance);
            private readonly List<object> _objects = new List<object>();

            private int _textureIndex;
            private long _textureBytes;
            private int _depth, _values;

            private readonly HashSet<Texture2D> _ownedTextures = new();
            private readonly TextureCompositor _owner;

            public Writer(BinaryWriter writer, WhimTexDocumentContainer container, TextureCompositor owner)
            {
                _writer = writer;
                _container = container;
                _owner = owner;
                if (owner != null) CollectOwned(owner.layers);
            }

            private void CollectOwned(List<Layer> layers)
            {
                if (layers == null) return;
                foreach (var layer in layers)
                {
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture != null)
                        _ownedTextures.Add(drawing.StoredTexture);
                    if (layer != null) CollectOwned(layer.children);
                }
            }

            public void Write(object value, Type declared)
            {
                if (++_values > 1000000 || ++_depth > 128) throw new WhimTexDocumentException("Document graph exceeds safety limits.");
                try { WriteValue(value, declared); }
                finally { _depth--; }
            }

            private void WriteValue(object value, Type declared)
            {
                if (value == null) { _writer.Write(TagNull); return; }
                if (value is ShaderFX) _container.HasExternalInputs = true;
                if (value is string text && System.Text.Encoding.UTF8.GetByteCount(text) > 1024 * 1024)
                    throw new WhimTexDocumentException("Document string exceeds the 1 MiB budget.");
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
                if (keys.Length > 65536) throw new WhimTexDocumentException("Too many curve keys.");
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
                if (ReferenceEquals(value, _owner) || !UnityEditor.AssetDatabase.Contains(value) || value is ShaderFX fx && fx.EmbeddedOwner != null)
                {
                    // A document-owned object must not be lost: it is stored inline as an object graph.
                    WriteObject(value, value.GetType());
                    return;
                }
                _container.HasExternalInputs = true;
                _writer.Write(TagReference);
                UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId);
                _writer.Write(guid ?? string.Empty);
                _writer.Write(localId);
            }


            private void WriteTexture(Texture2D texture)
            {
                if (texture == null) { _writer.Write(TagNull); return; }
                if (UnityEditor.AssetDatabase.Contains(texture) && !_ownedTextures.Contains(texture))
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
                using (var settings = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(settings, System.Text.Encoding.UTF8, true))
                    {
                        writer.Write((int)texture.filterMode);
                        writer.Write((int)texture.wrapModeU); writer.Write((int)texture.wrapModeV); writer.Write((int)texture.wrapModeW);
                        writer.Write(texture.anisoLevel); writer.Write(texture.mipMapBias);
                    }
                    _container.Set(block + ":sampling", settings.ToArray());
                }
                // Pixels stay in native memory: copying every layer into the managed heap would hand the
                // garbage collector hundreds of megabytes per save, and the container frees what it owns.
                Unity.Collections.NativeArray<byte> view = texture.GetRawTextureData<byte>();
                WhimTexDocumentLimits.CheckTexture(view.Length, ref _textureBytes, "Drawing texture '" + texture.name + "'");
                WhimTexDocumentOperation.Report("Preparing Drawing pixels", .1f);
                // Native fingerprint is only a fast lookup: the container compares every raw byte
                // before accepting a cached block. File integrity still uses SHA-256, once per new block.
                string cacheKey = _container.ReusePixelCache ? view.Length + ":hash128:" + Hash128.Compute(view) : null;
                if (cacheKey != null && _container.TryReusePixels(block, cacheKey, view)) return;
                var pixels = new Unity.Collections.NativeArray<byte>(view.Length, Unity.Collections.Allocator.Persistent);
                Unity.Collections.NativeArray<byte>.Copy(view, pixels);
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


            private void WriteList(IList list, Type type)
            {
                if (list.Count > 1000000) throw new WhimTexDocumentException("Too many list entries.");
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

            /// <summary>Counts a value and writes its name. Keeping the value out of an object parameter is
            /// what stops a value-type field from being boxed on its way out.</summary>
            private void Before(string name)
            {
                if (_manualRemaining < 0)
                    throw new WhimTexDocumentException("A manually serialized type wrote a value before Begin.");
                _manualRemaining--;
                _writer.Write(name);
            }

            private void Named(string name, object value, Type type)
            {
                Before(name);
                Write(value, type);
            }

            // Value types are written straight to the stream: the tag and the payload are exactly what the
            // automatic pass writes, so both passes stay interchangeable byte for byte.
            public void Write(string name, int value) { Before(name); _writer.Write(TagInt); _writer.Write(value); }
            public void Write(string name, long value) { Before(name); _writer.Write(TagLong); _writer.Write(value); }
            public void Write(string name, float value) { Before(name); _writer.Write(TagFloat); _writer.Write(value); }
            public void Write(string name, double value) { Before(name); _writer.Write(TagDouble); _writer.Write(value); }
            public void Write(string name, bool value) { Before(name); _writer.Write(TagBool); _writer.Write(value); }
            public void Write(string name, char value) { Before(name); _writer.Write(TagChar); _writer.Write(value); }
            public void Write(string name, string value)
            {
                Before(name);
                if (value == null) { _writer.Write(TagNull); return; }
                _writer.Write(TagString);
                _writer.Write(value);
            }
            public void Write(string name, Vector2 value)
            {
                Before(name); _writer.Write(TagVector2);
                _writer.Write(value.x); _writer.Write(value.y);
            }
            public void Write(string name, Vector3 value)
            {
                Before(name); _writer.Write(TagVector3);
                _writer.Write(value.x); _writer.Write(value.y); _writer.Write(value.z);
            }
            public void Write(string name, Vector4 value)
            {
                Before(name); _writer.Write(TagVector4);
                _writer.Write(value.x); _writer.Write(value.y); _writer.Write(value.z); _writer.Write(value.w);
            }
            public void Write(string name, Vector2Int value)
            {
                Before(name); _writer.Write(TagVector2Int);
                _writer.Write(value.x); _writer.Write(value.y);
            }
            public void Write(string name, Vector3Int value)
            {
                Before(name); _writer.Write(TagVector3Int);
                _writer.Write(value.x); _writer.Write(value.y); _writer.Write(value.z);
            }
            public void Write(string name, Quaternion value)
            {
                Before(name); _writer.Write(TagQuaternion);
                _writer.Write(value.x); _writer.Write(value.y); _writer.Write(value.z); _writer.Write(value.w);
            }
            public void Write(string name, Color value)
            {
                Before(name); _writer.Write(TagColor);
                _writer.Write(value.r); _writer.Write(value.g); _writer.Write(value.b); _writer.Write(value.a);
            }
            public void Write(string name, Color32 value)
            {
                Before(name); _writer.Write(TagColor32);
                _writer.Write(value.r); _writer.Write(value.g); _writer.Write(value.b); _writer.Write(value.a);
            }
            public void Write(string name, Rect value)
            {
                Before(name); _writer.Write(TagRect);
                _writer.Write(value.x); _writer.Write(value.y); _writer.Write(value.width); _writer.Write(value.height);
            }
            public void Write(string name, RectInt value)
            {
                Before(name); _writer.Write(TagRectInt);
                _writer.Write(value.x); _writer.Write(value.y); _writer.Write(value.width); _writer.Write(value.height);
            }
            public void Write(string name, Bounds value)
            {
                Before(name); _writer.Write(TagBounds);
                _writer.Write(value.center.x); _writer.Write(value.center.y); _writer.Write(value.center.z);
                _writer.Write(value.size.x); _writer.Write(value.size.y); _writer.Write(value.size.z);
            }

            // An enum carries its own type name, curves, objects, lists and asset references are reference
            // types, so those keep the shared path.
            public void WriteEnum<T>(string name, T value) where T : struct, Enum => Named(name, value, typeof(T));
            public void Write(string name, AnimationCurve value) => Named(name, value, typeof(AnimationCurve));
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
                    // wire: the count it declares is checked against what it actually wrote. A nested manual
                    // object must not disturb the count of the object that contains it.
                    int outer = _manualRemaining;
                    _manualRemaining = -1;
                    manual.WriteDocument(this);
                    if (_manualRemaining != 0)
                        throw new WhimTexDocumentException("A manually serialized type wrote a value count that does not match its fields: " + type.FullName + ".");
                    _manualRemaining = outer;
                    return;
                }
                WriteAutomaticFields(value, type);
            }

            public void WriteAutomaticFields(object value) => WriteAutomaticFields(value, value.GetType());

            private void WriteAutomaticFields(object value, Type type)
            {
                FieldInfo[] fields = Fields(type);
                _writer.Write(fields.Length);
                foreach (FieldInfo field in fields)
                {
                    _writer.Write(field.Name);
                    object fieldValue = field.GetValue(value);
                    if (value is ShaderFX effect && field.Name == "code")
                        fieldValue = ShaderFXSourceBuilder.DocumentCode((string)fieldValue, effect.SourcePath);
                    Write(fieldValue, field.FieldType);
                }
            }
        }

        internal readonly struct DeferredTextureInfo
        {
            internal readonly string sourcePath, block;
            internal readonly long sourceLength, sourceWriteTicksUtc;
            internal readonly int width, height, mipCount;
            internal readonly TextureFormat format;
            internal readonly bool linear;

            internal DeferredTextureInfo(string sourcePath, string block, int width, int height,
                TextureFormat format, int mipCount, bool linear, long sourceLength, long sourceWriteTicksUtc)
            {
                this.sourcePath = sourcePath;
                this.block = block;
                this.sourceLength = sourceLength;
                this.sourceWriteTicksUtc = sourceWriteTicksUtc;
                this.width = width;
                this.height = height;
                this.format = format;
                this.mipCount = mipCount;
                this.linear = linear;
            }
        }

        internal static Texture2D MaterializeDeferredTexture(DeferredTextureInfo info)
        {
            var file = new FileInfo(info.sourcePath);
            if (!file.Exists || file.Length != info.sourceLength || file.LastWriteTimeUtc.Ticks != info.sourceWriteTicksUtc)
                throw new WhimTexDocumentException("The TIFF changed after opening; reopen the document before using its deferred Drawing pixels.");
            using var container = WhimTexTiffCarrier.OpenContainer(info.sourcePath);
            long length = container.LengthOf(info.block);
            long total = 0;
            WhimTexDocumentLimits.CheckTexture(length, ref total, "Deferred Drawing block '" + info.block + "'");
            if (WhimTexDocumentLimits.ExpectedBytes(info.width, info.height, info.format, info.mipCount, info.linear) != length)
                throw new WhimTexDocumentException("Embedded texture byte count does not match its dimensions.");
            byte[] raw = container.Get(info.block);
            var texture = new Texture2D(info.width, info.height, info.format, info.mipCount, info.linear)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                if (texture.GetRawTextureData<byte>().Length != raw.Length)
                    throw new WhimTexDocumentException("Embedded texture byte count does not match its dimensions.");
                texture.LoadRawTextureData(raw);
                texture.Apply(false, false);
                string sampling = info.block + ":sampling";
                if (container.Contains(sampling))
                {
                    if (container.LengthOf(sampling) != 24)
                        throw new WhimTexDocumentException("Invalid texture sampling settings.");
                    using var stream = new MemoryStream(container.Get(sampling), false);
                    using var settings = new BinaryReader(stream);
                    texture.filterMode = (FilterMode)settings.ReadInt32();
                    texture.wrapModeU = (TextureWrapMode)settings.ReadInt32();
                    texture.wrapModeV = (TextureWrapMode)settings.ReadInt32();
                    texture.wrapModeW = (TextureWrapMode)settings.ReadInt32();
                    texture.anisoLevel = settings.ReadInt32();
                    texture.mipMapBias = settings.ReadSingle();
                }
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        // --- reader ---

        private sealed class Reader : IWhimTexDocumentReader
        {
            private readonly BinaryReader _reader;
            private readonly WhimTexDocumentContainer _container;
            private readonly string _sourcePath;
            private readonly bool _deferDrawingTextures;
            private readonly List<object> _objects = new List<object>();
            private int _depth, _values;
            private long _textureBytes;
            private const int MaxValues = 1000000;

            private sealed class DeferredTextureReference
            {
                private readonly DrawingLayerBehaviour _owner;
                internal DeferredTextureReference(DrawingLayerBehaviour owner) { _owner = owner; }
                internal Texture2D Resolve() => _owner.StoredTexture;
            }

            public void ReleaseCreatedObjects()
            {
                for (int i = _objects.Count - 1; i >= 0; i--)
                    if (_objects[i] is UnityEngine.Object value && value != null && !UnityEditor.AssetDatabase.Contains(value))
                        UnityEngine.Object.DestroyImmediate(value);
            }

            private string ReadText()
            {
                uint length = 0;
                for (int shift = 0; shift <= 28; shift += 7)
                {
                    byte part = _reader.ReadByte();
                    if (shift == 28 && part > 7) throw new WhimTexDocumentException("Invalid string length.");
                    length |= (uint)(part & 127) << shift;
                    if ((part & 128) != 0) continue;
                    if (length > 1024 * 1024 || length > _reader.BaseStream.Length - _reader.BaseStream.Position)
                        throw new WhimTexDocumentException("Document string exceeds safety limits.");
                    return System.Text.Encoding.UTF8.GetString(_reader.ReadBytes((int)length));
                }
                throw new WhimTexDocumentException("Invalid string length.");
            }

            public Reader(BinaryReader reader, WhimTexDocumentContainer container, string sourcePath, bool deferDrawingTextures)
            {
                _reader = reader;
                _container = container;
                _sourcePath = sourcePath;
                _deferDrawingTextures = deferDrawingTextures && !string.IsNullOrEmpty(sourcePath);
            }

            public object Read(Type declared)
            {
                if (++_values > MaxValues || ++_depth > 128) throw new WhimTexDocumentException("Document graph exceeds safety limits.");
                try { return ReadTagged(_reader.ReadByte(), declared); }
                finally { _depth--; }
            }

            private int ReadCount(int maximum, int minimumBytes)
            {
                int count = _reader.ReadInt32();
                if (count < 0 || count > maximum || (long)count * minimumBytes > _reader.BaseStream.Length - _reader.BaseStream.Position)
                    throw new WhimTexDocumentException("Invalid collection length in the document.");
                return count;
            }

            private object ReadTagged(byte tag, Type declared)
            {
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
                    case TagString: return ReadText();
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
                    case TagBounds: return new Bounds(new Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle()),
                        new Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle()));
                    case TagCurve: return ReadCurve();
                    case TagReference: return ReadReference();
                    case TagTexture: return ReadTexture();
                    case TagEnum: return ReadEnum();
                    case TagList: return ReadList(declared);
                    case TagObject: return ReadObjectValue(declared);
                    case TagObjectRef:
                        int id = _reader.ReadInt32();
                        if (id < 0 || id >= _objects.Count) throw new WhimTexDocumentException("Invalid object reference " + id + " in the document.");
                        return _objects[id] is DeferredTextureReference deferred ? deferred.Resolve() : _objects[id];
                    default: throw new WhimTexDocumentException("Unknown value tag " + tag + " in the document.");
                }
            }

            private object ReadCurve()
            {
                var curve = new AnimationCurve
                {
                    preWrapMode = ParseEnum<WrapMode>(ReadText()),
                    postWrapMode = ParseEnum<WrapMode>(ReadText())
                };
                int count = ReadCount(65536, 28);
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
                string typeName = ReadText();
                string name = ReadText();
                long number = _reader.ReadInt64();
                Type type = ResolveType(typeName);
                if (type == null) return null;
                if (!type.IsEnum) throw new WhimTexDocumentException("Invalid enum type: " + typeName);
                return Enum.IsDefined(type, name) ? Enum.Parse(type, name) : Enum.ToObject(type, number);
            }

            private object ReadReference()
            {
                string guid = ReadText();
                long localId = _reader.ReadInt64();
                if (string.IsNullOrEmpty(guid) && localId == 0) return null;
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) { _unresolvedReferences.Add(guid + ":" + localId); return null; }
                if (localId == 0)
                {
                    var main = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
                    if (main != null) return main;
                    foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)) return asset;
                    _unresolvedReferences.Add(guid + ":" + localId);
                    return null;
                }
                foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id);
                    if (id == localId) return asset;
                }
                _unresolvedReferences.Add(guid + ":" + localId);
                return null;
            }

            private object ReadTexture()
            {
                int width = _reader.ReadInt32();
                int height = _reader.ReadInt32();
                string formatName = ReadText();
                int mipCount = _reader.ReadInt32();
                bool linear = _reader.ReadBoolean();
                string block = ReadText();
                if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || mipCount < 1 ||
                    mipCount > 1 + (int)Math.Floor(Math.Log(Math.Max(width, height), 2)))
                    throw new WhimTexDocumentException("Invalid embedded texture dimensions.");
                long length = _container.LengthOf(block);
                WhimTexDocumentLimits.CheckTexture(length, ref _textureBytes, "Drawing block '" + block + "'");
                if (!Enum.TryParse(formatName, out TextureFormat format) || !Enum.IsDefined(typeof(TextureFormat), format))
                    throw new WhimTexDocumentException("Invalid embedded texture format.");
                if (WhimTexDocumentLimits.ExpectedBytes(width, height, format, mipCount, linear) != length)
                    throw new WhimTexDocumentException("Embedded texture byte count does not match its dimensions.");
                // Do not prefetch neighbouring Drawing blocks here. Opening a document only needs
                // the block currently being materialized; prefetching four blocks keeps all of their
                // decompressed byte arrays alive next to the Texture2D objects and can briefly double
                // memory for large documents. The container remains lazy, so reading one block at a
                // time lets each temporary byte array be released immediately after Apply().
                WhimTexDocumentOperation.Report("Reading and verifying Drawing pixels", .25f);
                byte[] raw = _container.Get(block);
                var texture = new Texture2D(width, height, format, mipCount, linear) { hideFlags = HideFlags.HideAndDontSave };
                _objects.Add(texture);
                if (texture.GetRawTextureData<byte>().Length != raw.Length)
                    throw new WhimTexDocumentException("Embedded texture byte count does not match its dimensions.");
                texture.LoadRawTextureData(raw);
                texture.Apply(false, false);
                string sampling = block + ":sampling";
                if (_container.Contains(sampling))
                {
                    if (_container.LengthOf(sampling) != 24) throw new WhimTexDocumentException("Invalid texture sampling settings.");
                    using var stream = new MemoryStream(_container.Get(sampling), false);
                    using var settings = new BinaryReader(stream);
                    texture.filterMode = (FilterMode)settings.ReadInt32();
                    texture.wrapModeU = (TextureWrapMode)settings.ReadInt32();
                    texture.wrapModeV = (TextureWrapMode)settings.ReadInt32();
                    texture.wrapModeW = (TextureWrapMode)settings.ReadInt32();
                    texture.anisoLevel = settings.ReadInt32(); texture.mipMapBias = settings.ReadSingle();
                }
                // The texture owns the pixels now, so the inflated copy can be collected.
                _container.Remove(block);
                return texture;
            }

            private object ReadDeferredDrawingTexture(DrawingLayerBehaviour drawing)
            {
                byte tag = _reader.ReadByte();
                if (tag == TagNull) return null;
                if (tag != TagTexture)
                    return ReadTagged(tag, typeof(Texture2D));

                int width = _reader.ReadInt32();
                int height = _reader.ReadInt32();
                string formatName = ReadText();
                int mipCount = _reader.ReadInt32();
                bool linear = _reader.ReadBoolean();
                string block = ReadText();
                if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || mipCount < 1 ||
                    mipCount > 1 + (int)Math.Floor(Math.Log(Math.Max(width, height), 2)))
                    throw new WhimTexDocumentException("Invalid embedded texture dimensions.");
                long length = _container.LengthOf(block);
                WhimTexDocumentLimits.CheckTexture(length, ref _textureBytes, "Drawing block '" + block + "'");
                if (!Enum.TryParse(formatName, out TextureFormat format) || !Enum.IsDefined(typeof(TextureFormat), format))
                    throw new WhimTexDocumentException("Invalid embedded texture format.");
                if (WhimTexDocumentLimits.ExpectedBytes(width, height, format, mipCount, linear) != length)
                    throw new WhimTexDocumentException("Embedded texture byte count does not match its dimensions.");
                var file = new FileInfo(_sourcePath);
                if (!file.Exists)
                    throw new WhimTexDocumentException("The TIFF disappeared while loading its Drawing metadata.");
                drawing.SetDeferredTexture(new DeferredTextureInfo(_sourcePath, block, width, height, format, mipCount, linear,
                    file.Length, file.LastWriteTimeUtc.Ticks));
                // The writer registered the texture before emitting TagTexture. Keep the same object-table
                // slot so a later back-reference can materialize it instead of desynchronizing the graph.
                _objects.Add(new DeferredTextureReference(drawing));
                return null;
            }

            private object ReadList(Type declared)
            {
                int count = ReadCount(MaxValues, 1);
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
                    object item = Read(element);
                    list.Add(item);
                }
                return list;
            }

            private object ReadObjectValue(Type declared)
            {
                Type type = ResolveType(ReadText());
                if (type == null || declared == null)
                {
                    // Keep the object numbering in step with the writer, then consume the values.
                    int missing = ReadCount(65536, 2);
                    _objects.Add(null);
                    for (int i = 0; i < missing; i++)
                    {
                        ReadText();
                        Read(null);
                    }
                    return null;
                }
                if (type.IsAbstract || type.ContainsGenericParameters || IsUnityType(type) ||
                    declared != null && !declared.IsAssignableFrom(type) ||
                    type.Assembly != typeof(TextureCompositor).Assembly && !typeof(LayerBehaviour).IsAssignableFrom(type) &&
                    (declared == typeof(object) || declared == null || declared.Assembly != type.Assembly) ||
                    typeof(UnityEngine.Object).IsAssignableFrom(type) && type != typeof(TextureCompositor) && type != typeof(ShaderFX) ||
                    !typeof(UnityEngine.Object).IsAssignableFrom(type) && !type.IsSerializable)
                    throw new WhimTexDocumentException("Unsupported or incompatible object type: " + type.FullName);
                object instance = typeof(ScriptableObject).IsAssignableFrom(type)
                    ? ScriptableObject.CreateInstance(type)
                    : Activator.CreateInstance(type, true);
                _objects.Add(instance);
                if (instance is UnityEngine.Object owned) owned.hideFlags = HideFlags.HideAndDontSave;
                if (instance is IWhimTexDocumentSerializable manual)
                {
                    // A manual type consumes its own values. Generated code falls back to this same
                    // automatic pass when it does not cover every field, so neither side can lose values.
                    int outer = _manualCount;
                    Type outerType = _manualType;
                    _manualCount = -1;
                    _manualType = type;
                    manual.ReadDocument(this);
                    _manualCount = outer;
                    _manualType = outerType;
                }
                else
                {
                    ReadAutomaticFields(instance);
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
            private Type _manualType;
            private string _manualName;

            public int Count
            {
                get
                {
                    if (_manualCount < 0) _manualCount = ReadCount(65536, 2);
                    return _manualCount;
                }
            }

            public string NextName() => _manualName = ReadText();
            // A value is read straight from its tag, so nothing is boxed on the way in. A tag that does not
            // match the field (a field whose type changed between versions, or a null) falls back to the
            // shared pass, which is exactly how the automatic path treats such a value.
            public int ReadInt() { byte tag = _reader.ReadByte(); if (tag == TagInt) return _reader.ReadInt32(); return ReadTagged(tag, typeof(int)) is int value ? value : default; }
            public long ReadLong() { byte tag = _reader.ReadByte(); if (tag == TagLong) return _reader.ReadInt64(); return ReadTagged(tag, typeof(long)) is long value ? value : default; }
            public float ReadFloat() { byte tag = _reader.ReadByte(); if (tag == TagFloat) return _reader.ReadSingle(); return ReadTagged(tag, typeof(float)) is float value ? value : default; }
            public double ReadDouble() { byte tag = _reader.ReadByte(); if (tag == TagDouble) return _reader.ReadDouble(); return ReadTagged(tag, typeof(double)) is double value ? value : default; }
            public bool ReadBool() { byte tag = _reader.ReadByte(); if (tag == TagBool) return _reader.ReadBoolean(); return ReadTagged(tag, typeof(bool)) is bool value && value; }
            public char ReadChar() { byte tag = _reader.ReadByte(); if (tag == TagChar) return _reader.ReadChar(); return ReadTagged(tag, typeof(char)) is char value ? value : default; }
            public string ReadString()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagString) return ReadText();
                return ReadTagged(tag, typeof(string)) as string;
            }
            public Vector2 ReadVector2()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagVector2) return new Vector2(_reader.ReadSingle(), _reader.ReadSingle());
                return ReadTagged(tag, typeof(Vector2)) is Vector2 value ? value : default;
            }
            public Vector3 ReadVector3()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagVector3) return new Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                return ReadTagged(tag, typeof(Vector3)) is Vector3 value ? value : default;
            }
            public Vector4 ReadVector4()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagVector4) return new Vector4(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                return ReadTagged(tag, typeof(Vector4)) is Vector4 value ? value : default;
            }
            public Vector2Int ReadVector2Int()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagVector2Int) return new Vector2Int(_reader.ReadInt32(), _reader.ReadInt32());
                return ReadTagged(tag, typeof(Vector2Int)) is Vector2Int value ? value : default;
            }
            public Vector3Int ReadVector3Int()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagVector3Int) return new Vector3Int(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32());
                return ReadTagged(tag, typeof(Vector3Int)) is Vector3Int value ? value : default;
            }
            public Quaternion ReadQuaternion()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagQuaternion) return new Quaternion(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                return ReadTagged(tag, typeof(Quaternion)) is Quaternion value ? value : default;
            }
            public Color ReadColor()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagColor) return new Color(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                return ReadTagged(tag, typeof(Color)) is Color value ? value : default;
            }
            public Color32 ReadColor32()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagColor32) return new Color32(_reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte());
                return ReadTagged(tag, typeof(Color32)) is Color32 value ? value : default;
            }
            public Rect ReadRect()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagRect) return new Rect(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
                return ReadTagged(tag, typeof(Rect)) is Rect value ? value : default;
            }
            public RectInt ReadRectInt()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagRectInt) return new RectInt(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32());
                return ReadTagged(tag, typeof(RectInt)) is RectInt value ? value : default;
            }
            public Bounds ReadBounds()
            {
                byte tag = _reader.ReadByte();
                if (tag == TagBounds)
                    return new Bounds(new Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle()),
                        new Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle()));
                return ReadTagged(tag, typeof(Bounds)) is Bounds value ? value : default;
            }
            AnimationCurve IWhimTexDocumentReader.ReadCurve() => (AnimationCurve)Read(typeof(AnimationCurve));
            // An enum carries its own type name, so it keeps the shared pass.
            public T ReadEnum<T>() where T : struct, Enum => (T)Read(typeof(T));
            public object ReadObject(Type type) => Read(type);
            // These names already exist on the reader's own value dispatch, so they are implemented
            // explicitly: through the interface they mean "read the next value as this type".
            IList IWhimTexDocumentReader.ReadList(Type type) => (IList)Read(type);
            UnityEngine.Object IWhimTexDocumentReader.ReadReference() => Read(null) as UnityEngine.Object;
            Texture2D IWhimTexDocumentReader.ReadTexture() => Read(typeof(Texture2D)) as Texture2D;
            public void Skip()
            {
                if (_manualType != null) RecordSkippedField(_manualType, _manualName ?? "unknown");
                Read(null);
            }

            /// <summary>The automatic pass: reads a value count and assigns every value to its field by name.</summary>
            public void ReadAutomaticFields(object value)
            {
                Type type = value.GetType();
                int count = ReadCount(65536, 2);
                for (int i = 0; i < count; i++)
                {
                    string fieldName = ReadText();
                    FieldInfo field = FindField(type, fieldName);
                    object fieldValue = type == typeof(DrawingLayerBehaviour) && field?.FieldType == typeof(Texture2D) &&
                        string.Equals(field.Name, "pixels", StringComparison.Ordinal) && _deferDrawingTextures
                        ? ReadDeferredDrawingTexture((DrawingLayerBehaviour)value)
                        : Read(field?.FieldType);
                    if (field == null) continue;
                    try
                    {
                        if (fieldValue != null || !field.FieldType.IsValueType) field.SetValue(value, fieldValue);
                    }
                    catch (ArgumentException) { RecordSkippedField(type, fieldName); }
                    catch (InvalidCastException) { RecordSkippedField(type, fieldName); }
                }
            }
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
