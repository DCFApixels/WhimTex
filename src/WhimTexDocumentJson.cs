using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public enum WhimTexJsonWriteMode { Full, FullOptimized, Compact }

    public sealed class WhimTexJsonWriteOptions
    {
        public WhimTexJsonWriteMode Mode = WhimTexJsonWriteMode.FullOptimized;
        public bool AllowDrawingOmission;
        public bool AllowDataLoss;
    }

    public sealed class WhimTexJsonWriteResult
    {
        public string Json { get; internal set; }
        public IReadOnlyList<string> Warnings { get; internal set; }
    }

    /// <summary>Owns a detached document until TakeDocument transfers it to its caller.</summary>
    public sealed class WhimTexJsonReadResult : IDisposable
    {
        public WhimTexDocument Document { get; internal set; }
        public IReadOnlyList<string> Warnings { get; internal set; }
        internal List<ShaderFX> Effects = new();
        internal bool HasCanvasSize;
        public WhimTexDocument TakeDocument()
        {
            var result = Document;
            Document = null;
            Effects.Clear();
            return result;
        }
        public void Dispose()
        {
            if (Document != null) Object.DestroyImmediate(Document);
            foreach (var effect in Effects) if (effect != null) Object.DestroyImmediate(effect);
            Effects.Clear();
            Document = null;
        }
    }

    /// <summary>The shared storage format for files, layer fragments, clipboard and automation.</summary>
    public static partial class WhimTexDocumentJson
    {
        public const string Format = "whimtex.document";
        public const int Version = 2;
        public const string Extension = ".json";
        private const int MaxCharacters = 64 * 1024 * 1024;
        private static readonly HashSet<string> TransientFields = new(StringComparer.Ordinal)
        {
            "compiledShader", "appliedCode", "appliedSource", "appliedParameters", "diagnostics",
            "lastApplyFailed", "shaderCreationRecorded", "embeddedOwner", "transformCache", "jsonWriteMode",
            "outputTexture", "documentLoadWarning", "documentBinding",
            "embeddedShaderFX", "jsonMissingAssets",
            "catalogGuid", "catalogSourcePath", "catalogDependencyHash", "documentIncludeBasePath"
        };
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();
        private static readonly ConditionalWeakTable<object, Dictionary<string, JToken>> MissingAssets = new();
        private static readonly Dictionary<string, Type> Types = BuildTypes();

        public static bool IsJsonPath(string path) => path != null && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        public static bool IsDocumentFile(string path)
        {
            if (!IsJsonPath(path) || !File.Exists(path)) return false;
            try
            {
                if (new FileInfo(path).Length > MaxCharacters * 4L) return false;
                return (string)Parse(File.ReadAllText(path))["format"] == Format;
            }
            catch { return false; }
        }

        public static WhimTexJsonWriteResult Write(WhimTexDocument document, WhimTexJsonWriteOptions options = null)
            => WriteCore(document, null, options);

        public static WhimTexJsonWriteResult WriteLayers(WhimTexDocument document, IEnumerable<Layer> layers,
            WhimTexJsonWriteOptions options = null) => WriteCore(document, layers?.ToList() ?? throw new ArgumentNullException(nameof(layers)), options);

        private static WhimTexJsonWriteResult WriteCore(WhimTexDocument document, List<Layer> selection, WhimTexJsonWriteOptions options)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            options ??= new WhimTexJsonWriteOptions();
            if (!options.AllowDataLoss && !string.IsNullOrEmpty(document.documentLoadWarning))
                throw new WhimTexDocumentException("Cannot export an incompletely loaded document: " + document.documentLoadWarning);
            if (!Enum.IsDefined(typeof(WhimTexJsonWriteMode), options.Mode))
                throw new ArgumentOutOfRangeException(nameof(options.Mode));
            RestoreMissingAssets(document);
            if (selection != null) selection = FragmentRoots(document, selection);
            var context = new Writer(options);
            var settings = context.ObjectFields(document);
            settings.Remove("layers");
            // Even Compact retains the source canvas: omission means the destination axis
            // when this same JSON is pasted, rather than the format's default axis.
            settings["width"] = document.width;
            settings["height"] = document.height;
            var root = new JObject { ["format"] = Format, ["version"] = Version,
                ["document"] = settings,
                ["layers"] = context.Value(selection ?? document.layers, typeof(List<Layer>)) };
            if (options.Mode != WhimTexJsonWriteMode.FullOptimized) root["writeMode"] = options.Mode.ToString();
            if (selection != null)
                for (int i = 0; i < selection.Count; i++)
                    root["layers"][i]["transform"] = context.Value(document.GetCanvasTransform(selection[i]), typeof(TextureTransform));
            ValidateLayerReferences(root);
            string json = root.ToString(Formatting.Indented);
            if (json.Length > MaxCharacters) throw new WhimTexDocumentException("JSON exceeds the 64 MiB character budget.");
            return new WhimTexJsonWriteResult { Json = json, Warnings = context.Warnings.ToArray() };
        }

        public static WhimTexJsonReadResult Read(string json, bool prepareEffects = true)
            => ReadCore(json, prepareEffects, null);

        internal static WhimTexJsonReadResult ReadForInsertion(string json, int width, int height, bool prepareEffects)
            => ReadCore(json, prepareEffects, new Vector2Int(width, height));

        private static WhimTexJsonReadResult ReadCore(string json, bool prepareEffects, Vector2Int? fallbackCanvas)
        {
            JObject root = Parse(json);
            CheckKeys(root, "format", "version", "document", "layers", "writeMode");
            var mode = WhimTexJsonWriteMode.FullOptimized;
            if (root.TryGetValue("writeMode", out var modeToken) &&
                (modeToken.Type != JTokenType.String || !Enum.TryParse((string)modeToken, out mode) ||
                 !Enum.IsDefined(typeof(WhimTexJsonWriteMode), mode) || mode.ToString() != (string)modeToken))
                throw new WhimTexDocumentException("writeMode must be Full, FullOptimized or Compact.");
            if ((string)ReadScalar(root["format"], typeof(string)) != Format || (int)ReadScalar(root["version"], typeof(int)) != Version)
                throw new WhimTexDocumentException("Expected " + Format + " version " + Version + ". Older document versions are not supported.");
            // The caller selects open, insert or replace; file content never selects an action.
            ValidateLayerReferences(root);
            var context = new Reader();
            var result = new WhimTexJsonReadResult();
            try
            {
                var settings = root.TryGetValue("document", out var settingsToken)
                    ? settingsToken as JObject ?? throw new WhimTexDocumentException("document must be an object when present.")
                    : new JObject();
                if (settings.ContainsKey("layers")) throw JsonError(settings["layers"], "Put layers at the root, not inside document.");
                bool hasWidth = settings.ContainsKey("width"), hasHeight = settings.ContainsKey("height");
                result.HasCanvasSize = hasWidth || hasHeight;
                var model = (JObject)settings.DeepClone();
                if (fallbackCanvas.HasValue)
                {
                    if (!hasWidth) model["width"] = fallbackCanvas.Value.x;
                    if (!hasHeight) model["height"] = fallbackCanvas.Value.y;
                }
                model["layers"] = root["layers"]?.DeepClone() ?? throw new WhimTexDocumentException("layers is required.");
                // Retain source-style paths after combining settings and layers for the model reader.
                model.AddAnnotation(new ModelJsonPaths());
                _ = new JObject { ["document"] = model };
                result.Document = (WhimTexDocument)context.Value(model, typeof(WhimTexDocument));
                result.Document.jsonWriteMode = mode;
                result.Effects = context.Effects;
                if (result.Document.width < 1 || result.Document.height < 1 || result.Document.width > 16384 || result.Document.height > 16384 ||
                    (long)result.Document.width * result.Document.height > 16777216)
                    throw new WhimTexDocumentException("document.width / document.height: canvas must be 1..16384 per axis, at most 16,777,216 pixels.");
                result.Document.NormalizeModel();
                CaptureMissingAssets(result.Document);
                foreach (var effect in result.Effects)
                {
                    effect.RestoreDocumentOwner(result.Document);
                    result.Document.AdoptAgentShaderFX(effect, null);
                    effect.SuspendDocumentCatalogReload();
                    if (prepareEffects)
                    {
                        effect.TryPrepareDocumentEffect(out string warning);
                        if (warning != null) context.Warnings.Add(warning);
                    }
                }
                ValidateModelInputs(result.Document);
                result.Warnings = context.Warnings.ToArray();
                return result;
            }
            catch
            {
                foreach (var obj in context.Created) if (obj != null) Object.DestroyImmediate(obj);
                result.Document = null;
                result.Effects.Clear();
                throw;
            }
        }

        internal static JObject Parse(string json)
        {
            if (json == null || json.Length > MaxCharacters) throw new WhimTexDocumentException("JSON exceeds the 64 MiB character budget.");
            json = json.Trim().TrimStart('\uFEFF');
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = json.IndexOf('\n');
                if (newline < 0 || !json.EndsWith("```", StringComparison.Ordinal)) throw new WhimTexDocumentException("Incomplete JSON block.");
                json = json.Substring(newline + 1, json.Length - newline - 4);
            }
            using var text = new StringReader(json);
            using var reader = new JsonTextReader(text) { MaxDepth = 128, DateParseHandling = DateParseHandling.None };
            var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new WhimTexDocumentException("Unexpected trailing JSON data.");
            return root;
        }

        private static void CheckKeys(JObject node, params string[] names)
        {
            foreach (var field in node.Properties()) if (!names.Contains(field.Name))
                throw new WhimTexDocumentException("Unknown field: " + field.Path);
        }

        internal static FieldInfo[] Fields(Type type)
        {
            if (FieldCache.TryGetValue(type, out var result)) return result;
            var fields = new List<FieldInfo>();
            for (var current = type; current != null && current.Assembly == typeof(WhimTexDocument).Assembly; current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized || TransientFields.Contains(field.Name)) continue;
                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField)) && !field.IsDefined(typeof(SerializeReference))) continue;
                    fields.Add(field);
                }
            return FieldCache[type] = fields.ToArray();
        }

        private static Dictionary<string, Type> BuildTypes()
        {
            var types = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var type in typeof(WhimTexDocument).Assembly.GetTypes())
                if (type.FullName != null && type.FullName.StartsWith("DCFApixels.WhimTex.", StringComparison.Ordinal) &&
                    !type.IsAbstract && !type.IsGenericType && (type.IsSerializable || type == typeof(WhimTexDocument) || type == typeof(ShaderFX)) &&
                    !typeof(Delegate).IsAssignableFrom(type) && !typeof(Component).IsAssignableFrom(type))
                    types[type.FullName.Substring("DCFApixels.WhimTex.".Length)] = type;
            return types;
        }

        private static string TypeName(Type type) => type.FullName.Substring("DCFApixels.WhimTex.".Length);
        private static Type ElementType(Type type) => type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
        private static bool IsNull(object value) => value == null || value is Object obj && obj == null;

        private static void ValidateLayerReferences(JObject root)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targets = new List<string>();
            if (!(root["layers"] is JArray roots)) throw new WhimTexDocumentException("layers must be an array.");
            int count = 0;
            void CheckTree(JArray layers)
            {
                foreach (var item in layers)
                {
                    if (++count > 1024) throw new WhimTexDocumentException("At most 1024 layers are supported.");
                    if (!(item is JObject layer)) throw JsonError(item, "Expected a layer object.");
                    if (!(layer["behaviour"] is JObject)) throw JsonError(layer["behaviour"] ?? layer, "Every layer requires a behaviour object.");
                    if (layer["id"]?.Type != JTokenType.String || string.IsNullOrEmpty((string)layer["id"]))
                        throw JsonError(layer["id"] ?? layer, "Every layer requires a nonempty string id.");
                    if (layer["children"] is JArray children) CheckTree(children);
                }
            }
            CheckTree(roots);
            void Walk(JToken token)
            {
                if (token is JObject node)
                {
                    if (node["behaviour"] != null)
                    {
                        string id = (string)node["id"];
                        if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw JsonError(node["id"] ?? node, "Layer IDs must be present and unique.");
                    }
                    if (node["inputMode"]?.Type == JTokenType.String && (string)node["inputMode"] == "Specific" && node["targetLayerId"]?.Type == JTokenType.String)
                        targets.Add((string)node["targetLayerId"]);
                    if (node["textureSource"]?.Type == JTokenType.String && (string)node["textureSource"] == "Layer" && node["textureLayerId"]?.Type == JTokenType.String)
                        targets.Add((string)node["textureLayerId"]);
                    foreach (var field in node.Properties()) Walk(field.Value);
                }
                else if (token is JArray array) foreach (var item in array) Walk(item);
            }
            Walk(root["layers"]);
            foreach (string target in targets) if (!ids.Contains(target))
                throw new WhimTexDocumentException("Include the referenced layer in the fragment: " + target);
        }

        internal static void ValidateModelInputs(WhimTexDocument document)
        {
            void Visit(List<Layer> layers)
            {
                foreach (var layer in layers)
                {
                    if (layer?.Behaviour is TargetedLayerBehaviour target &&
                        (target.inputMode == EffectInputMode.Specific && !document.IsUsableEffectTarget(target, target.TargetLayerId) ||
                         target.inputMode == EffectInputMode.AllBelow && document.TryFindLayer(layer, out var siblings, out int index) && !document.HasUsableEffectInput(target, siblings, index)))
                        throw new WhimTexDocumentException("Invalid or cyclic layer input: " + layer.Id);
                    if (layer?.fx != null)
                        foreach (var fxEntry in layer.fx)
                            if (fxEntry is ShaderFX fx)
                                foreach (var parameter in fx.TextureLayerParameters())
                                    if (!document.IsUsableShaderTexture(layer, parameter.textureLayerId))
                                        throw new WhimTexDocumentException("Invalid or cyclic FX input: " + parameter.name);
                    if (layer?.children != null) Visit(layer.children);
                }
            }
            Visit(document.layers);
        }

        private static List<Layer> FragmentRoots(WhimTexDocument document, List<Layer> selection)
        {
            var wanted = new HashSet<Layer>(selection);
            var included = new HashSet<Layer>();
            var roots = new List<Layer>();
            void Collect(List<Layer> layers, bool inside)
            {
                if (layers == null) return;
                foreach (var layer in layers)
                {
                    bool take = inside || wanted.Contains(layer);
                    if (take) included.Add(layer);
                    if (take && !inside) roots.Add(layer);
                    Collect(layer.children, take);
                }
            }
            Collect(document.layers, false);
            if (roots.Count == 0 || selection.Any(layer => !included.Contains(layer)))
                throw new WhimTexDocumentException("Select existing layers to write a fragment.");
            foreach (var layer in included)
            {
                if (layer.clippingMask && document.GetClippingBase(layer) is Layer clip && !included.Contains(clip))
                    throw new WhimTexDocumentException("Include the clipping base: " + clip.Id);
                if (layer.Behaviour is TargetedLayerBehaviour targeted && document.TryFindLayer(layer, out var siblings, out int index))
                {
                    if (targeted.inputMode == EffectInputMode.Previous && index + 1 < siblings.Count && !included.Contains(siblings[index + 1]))
                        throw new WhimTexDocumentException("Include the Previous input: " + siblings[index + 1].Id);
                    if (targeted.inputMode == EffectInputMode.AllBelow)
                        for (int i = index + 1; i < siblings.Count; i++) if (!included.Contains(siblings[i]))
                            throw new WhimTexDocumentException("Include every All Below input: " + siblings[i].Id);
                }
            }
            return roots;
        }

        private sealed class Writer
        {
            internal readonly List<string> Warnings = new();
            private readonly WhimTexJsonWriteOptions options;
            private readonly Dictionary<ShaderFX, string> effects = new();
            private int depth, count;
            internal Writer(WhimTexJsonWriteOptions options) { this.options = options; }

            internal JObject ObjectFields(object value)
            {
                var node = new JObject();
                foreach (var field in Fields(value.GetType()))
                {
                    if (value is ShaderFXParameter parameter && parameter.controls.Count > 0 &&
                        (field.Name == "controls" || field.Name == "hasMinimum" || field.Name == "hasMaximum" ||
                         field.Name == "softMinimum" || field.Name == "softMaximum" || field.Name == "minimum" || field.Name == "maximum")) continue;
                    if (value is WhimTexDocument && field.Name == "layers") continue;
                    if (value is DrawingLayerBehaviour && field.Name == "pixels")
                    {
                        if (((DrawingLayerBehaviour)value).HasJsonOmittedPixels && !options.AllowDrawingOmission)
                            throw new WhimTexDocumentException("JSON cannot store Drawing pixels. Set AllowDrawingOmission explicitly to retain empty Drawing nodes.");
                        node["contentOmitted"] = true;
                        if (((DrawingLayerBehaviour)value).HasJsonOmittedPixels)
                            Warnings.Add(((DrawingLayerBehaviour)value).Owner?.layerName + ": Drawing pixels omitted.");
                        continue;
                    }
                    if (options.Mode != WhimTexJsonWriteMode.Full && !IsActive(value, field.Name)) continue;
                    object item = field.GetValue(value);
                    if (value is ShaderFX effect && field.Name == "parameters")
                    {
                        // Store the valid draft's declarations without mutating its applied state.
                        try
                        {
                            var declared = ShaderFXMetadata.Parse(effect.Code, false, out _);
                            ShaderFXMetadata.PreserveValues(declared, effect.Parameters);
                            item = declared;
                        }
                        catch (FormatException) { /* Broken drafts keep their stored values. */ }
                    }
                    JToken encoded;
                    if (IsNull(item) && MissingAssets.TryGetValue(value, out var missing) && missing.TryGetValue(field.Name, out var identity))
                        encoded = identity.DeepClone();
                    else if (value is ShaderFX fx && field.Name == "code")
                    {
                        try { encoded = new JValue(ShaderFXSourceBuilder.ExportIncludes(fx.Code, fx.SourcePath)); }
                        catch (Exception error) when (error is IOException || error is InvalidOperationException)
                        { encoded = new JValue(fx.Code); }
                    }
                    else encoded = Value(item, field.FieldType);
                    if (options.Mode == WhimTexJsonWriteMode.Compact && IsDefault(value.GetType(), field.Name, encoded)) continue;
                    node[field.Name] = encoded;
                }
                return node;
            }

            internal JToken Value(object value, Type declared)
            {
                if (++count > 1000000 || ++depth > 128) throw new WhimTexDocumentException("JSON graph exceeds safety limits.");
                try
                {
                    if (IsNull(value)) return JValue.CreateNull();
                    Type type = value.GetType();
                    if (type.IsEnum) return new JValue(value.ToString());
                    if (type.IsPrimitive || value is string || value is decimal) return new JValue(value);
                    JToken primitive = WriteUnityValue(value);
                    if (primitive != null) return primitive;
                    if (value is ShaderFX fx)
                    {
                        if (effects.TryGetValue(fx, out string reference)) return new JObject { ["$ref"] = reference };
                        reference = "fx" + effects.Count;
                        effects.Add(fx, reference);
                        var data = ObjectFields(fx);
                        data.AddFirst(new JProperty("$id", reference));
                        data.AddFirst(new JProperty("$type", TypeName(type)));
                        data["$name"] = fx.name;
                        return data;
                    }
                    if (value is Object asset && !(value is WhimTexDocument))
                    {
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                            throw new WhimTexDocumentException("Unstored asset cannot be represented in JSON: " + asset.name);
                        return new JObject { ["$asset"] = new JObject { ["guid"] = guid, ["path"] = AssetDatabase.GetAssetPath(asset),
                            ["localId"] = localId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["type"] = type.FullName } };
                    }
                    if (value is IList list)
                    {
                        var array = new JArray();
                        Type element = ElementType(type);
                        for (int i = 0; i < list.Count; i++)
                            if (IsNull(list[i]) && MissingAssets.TryGetValue(list, out var missing) && missing.TryGetValue(i.ToString(), out var identity)) array.Add(identity.DeepClone());
                            else array.Add(Value(list[i], element));
                        return array;
                    }
                    if (type.Assembly != typeof(WhimTexDocument).Assembly) throw new WhimTexDocumentException("Unsupported JSON type: " + type.FullName);
                    var result = ObjectFields(value);
                    if (type != declared) result.AddFirst(new JProperty("$type", TypeName(type)));
                    return result;
                }
                finally { depth--; }
            }
        }

        private sealed class Reader
        {
            internal readonly List<string> Warnings = new();
            internal readonly List<Object> Created = new();
            internal readonly List<ShaderFX> Effects = new();
            private readonly Dictionary<string, ShaderFX> references = new();
            private int count;

            internal object Value(JToken token, Type declared, object parent = null, string key = null)
            {
                try { return ReadValue(token, declared, parent, key); }
                catch (Exception error) when ((error is WhimTexDocumentException || error is ArgumentException ||
                    error is InvalidCastException || error is FormatException || error is OverflowException || error is JsonException) &&
                    !error.Data.Contains("WhimTexJsonPath"))
                { throw JsonError(token, error.Message); }
            }

            private object ReadValue(JToken token, Type declared, object parent, string key)
            {
                if (++count > 1000000) throw new WhimTexDocumentException("JSON graph exceeds safety limits.");
                if (token == null || token.Type == JTokenType.Null)
                {
                    if (declared.IsValueType || declared == typeof(Layer) || declared == typeof(WhimTexDocument) || declared == typeof(LayerBehaviour))
                        throw JsonError(token, "Null is not allowed for " + declared.Name + ".");
                    return null;
                }
                if (declared.IsEnum || declared.IsPrimitive || declared == typeof(string) || declared == typeof(decimal)) return ReadScalar(token, declared);
                if (TryReadUnityValue(token, declared, out object primitive)) return primitive;
                if (token is JArray array)
                {
                    if (!typeof(IList).IsAssignableFrom(declared)) throw new WhimTexDocumentException("Unexpected array: " + token.Path);
                    Type element = ElementType(declared);
                    IList list = declared.IsArray ? Array.CreateInstance(element, array.Count) : (IList)Activator.CreateInstance(declared);
                    for (int i = 0; i < array.Count; i++)
                    {
                        object item = Value(array[i], element, list, i.ToString());
                        if (declared.IsArray) list[i] = item; else list.Add(item);
                    }
                    return list;
                }
                var node = token as JObject ?? throw new WhimTexDocumentException("Expected object: " + token.Path);
                foreach (string metadata in new[] { "$type", "$id", "$name", "$ref" })
                    if (node.TryGetValue(metadata, out var metadataValue) && metadataValue.Type != JTokenType.String)
                        throw JsonError(metadataValue, "Expected a string.");
                if (node["$asset"] is JObject asset)
                {
                    CheckKeys(node, "$asset");
                    if (!typeof(Object).IsAssignableFrom(declared)) throw new WhimTexDocumentException("Unexpected asset reference.");
                    Object resolved = ResolveAsset(asset, declared);
                    if (resolved == null)
                    {
                        Warnings.Add("Missing asset: " + (string)asset["path"] + " (" + (string)asset["guid"] + ")");
                        if (parent != null) MissingAssets.GetOrCreateValue(parent)[key] = node.DeepClone();
                    }
                    return resolved;
                }
                if (node["$ref"] != null)
                {
                    CheckKeys(node, "$ref");
                    if (!references.TryGetValue((string)node["$ref"], out var fx) || !declared.IsInstanceOfType(fx))
                        throw new WhimTexDocumentException("Invalid FX reference: " + token.Path);
                    return fx;
                }
                Type type = declared;
                if (node["$type"] != null && (!Types.TryGetValue((string)node["$type"], out type) || !declared.IsAssignableFrom(type)))
                    throw new WhimTexDocumentException("Unknown or incompatible type: " + node["$type"]);
                if (type.IsAbstract || type.Assembly != typeof(WhimTexDocument).Assembly)
                    throw new WhimTexDocumentException("Unsupported JSON type: " + type.FullName);
                if (type != typeof(ShaderFX) && (node.ContainsKey("$id") || node.ContainsKey("$name")))
                    throw JsonError(node["$id"] ?? node["$name"], "Only ShaderFX supports this metadata.");
                object result;
                if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    if (type != typeof(WhimTexDocument) && type != typeof(ShaderFX)) throw new WhimTexDocumentException("Unsupported owned object: " + type.Name);
                    var obj = ScriptableObject.CreateInstance(type);
                    obj.hideFlags = HideFlags.HideAndDontSave;
                    Created.Add(obj);
                    result = obj;
                    if (obj is ShaderFX shader)
                    {
                        Effects.Add(shader);
                        shader.name = (string)node["$name"] ?? "Shader FX";
                        if (node["$id"] != null) references.Add((string)node["$id"], shader);
                    }
                }
                else result = Activator.CreateInstance(type, true);
                var fields = Fields(type).ToDictionary(f => f.Name, StringComparer.Ordinal);
                foreach (var property in node.Properties())
                {
                    if (property.Name == "$type" || property.Name == "$id" || property.Name == "$name") continue;
                    if (property.Name == "contentOmitted" && result is DrawingLayerBehaviour)
                    {
                        if (property.Value.Type != JTokenType.Boolean || !(bool)property.Value)
                            throw JsonError(property.Value, "contentOmitted must be true.");
                        Warnings.Add("Drawing node has no saved pixels."); continue;
                    }
                    if (property.Name == "pixels" && result is DrawingLayerBehaviour)
                        throw JsonError(property.Value, "Drawing pixels are not supported in JSON.");
                    if (!fields.ContainsKey(property.Name)) throw new WhimTexDocumentException("Unknown field: " + property.Path);
                }
                JObject defaults = DefaultsFor(type);
                foreach (var field in fields.Values)
                {
                    JToken value = node[field.Name] ?? defaults?[field.Name];
                    if (value != null) field.SetValue(result, Value(value, field.FieldType, result, field.Name));
                    else if (defaults != null && field.Name != "id" && field.Name != "recoveryId" && field.Name != "shaderKey")
                        field.SetValue(result, field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null);
                }
                return result;
            }
        }

        private static Object ResolveAsset(JObject asset, Type expected)
        {
            CheckKeys(asset, "guid", "path", "localId", "type");
            foreach (string field in new[] { "guid", "path", "localId", "type" })
                if (asset[field]?.Type != JTokenType.String) throw JsonError(asset[field] ?? asset, field + " must be a string.");
            string guid = (string)asset["guid"], fallback = (string)asset["path"];
            string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) path = fallback;
            if (!long.TryParse((string)asset["localId"], out long localId)) throw new WhimTexDocumentException("Invalid asset localId.");
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(path))
                if (candidate != null && expected.IsInstanceOfType(candidate) &&
                    (string.IsNullOrEmpty((string)asset["type"]) || candidate.GetType().FullName == (string)asset["type"]) &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long candidateId) && candidateId == localId) return candidate;
            return null;
        }
    }
}
