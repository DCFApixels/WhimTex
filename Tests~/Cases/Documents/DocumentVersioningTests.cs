// Pipeline run_script entries: RunRegistry, RunUpgrades, RunExports, RunJson, RunTiff.
// Append Tests~/Framework/TestApi.cs. Requires the loaded WhimTex and Newtonsoft.Json assemblies.
// No Default registrations. Synthetic contracts use existing concrete behaviours because
// LayerBehaviour.Render is internal abstract and cannot be implemented by an external test assembly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class DocumentVersioningTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // The ephemeral compiler also sees Unity.Localization's Newtonsoft copy. Resolve every
    // JSON type from the production contract signature so callbacks have the exact required type.
    static object Invoke(MethodInfo method, object owner, params object[] arguments)
    {
        try { return method.Invoke(owner, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    sealed class Json
    {
        internal static readonly Type ObjectType = typeof(WhimTexDocumentContract).GetMethod("Upgrade").GetParameters()[0].ParameterType;
        static readonly Type TokenType = ObjectType.Assembly.GetType("Newtonsoft.Json.Linq.JToken", true);
        static readonly Type ValueType = ObjectType.Assembly.GetType("Newtonsoft.Json.Linq.JValue", true);
        static readonly PropertyInfo ItemProperty = TokenType.GetProperty("Item", new[] { typeof(object) });
        internal readonly object Raw;
        internal Json(object raw) { Raw = raw; }
        internal static Json Parse(string text) => new Json(Invoke(ObjectType.GetMethod("Parse", new[] { typeof(string) }), null, text));
        internal static Json Scalar(object value) => new Json(ValueType.GetConstructor(new[] { typeof(object) }).Invoke(new[] { value }));
        internal static bool DeepEquals(Json left, Json right) => (bool)Invoke(TokenType.GetMethod("DeepEquals", new[] { TokenType, TokenType }), null, left?.Raw, right?.Raw);
        internal Json DeepClone() => new Json(Invoke(TokenType.GetMethod("DeepClone", System.Type.EmptyTypes), Raw));
        internal bool Remove(string name) => (bool)Invoke(ObjectType.GetMethod("Remove", new[] { typeof(string) }), Raw, name);
        internal void RemoveAll() => Invoke(ObjectType.GetMethod("RemoveAll", System.Type.EmptyTypes), Raw);
        internal string Type => TokenType.GetProperty("Type").GetValue(Raw).ToString();
        internal int Count => (int)Raw.GetType().GetProperty("Count").GetValue(Raw);
        internal IEnumerable<string> PropertyNames()
        {
            var properties = (System.Collections.IEnumerable)Invoke(ObjectType.GetMethod("Properties", System.Type.EmptyTypes), Raw);
            foreach (var property in properties)
                yield return (string)property.GetType().GetProperty("Name").GetValue(property);
        }
        public Json this[object key]
        {
            get { var value = ItemProperty.GetValue(Raw, new[] { key }); return value == null ? null : new Json(value); }
            set => ItemProperty.SetValue(Raw, value?.Raw, new[] { key });
        }
        public override string ToString() => Raw.ToString();
        public static implicit operator Json(string value) => Scalar(value);
        public static implicit operator Json(int value) => Scalar(value);
        public static implicit operator Json(double value) => Scalar(value);
        public static implicit operator Json(bool value) => Scalar(value);
        public static explicit operator int(Json value) => Convert.ToInt32(ValueType.GetProperty("Value").GetValue(value.Raw));
        public static explicit operator double(Json value) => Convert.ToDouble(ValueType.GetProperty("Value").GetValue(value.Raw));
        public static explicit operator string(Json value) => value == null || value.Type == "Null" ? null : (string)ValueType.GetProperty("Value").GetValue(value.Raw);
    }

    sealed class Types
    {
        readonly WhimTexDocumentTypes registry;
        readonly Dictionary<WhimTexDocumentContract, VersionContract> wrappers = new Dictionary<WhimTexDocumentContract, VersionContract>();
        internal static readonly Types Default = new Types(WhimTexDocumentTypes.Default);
        internal Types() : this(new WhimTexDocumentTypes()) { }
        Types(WhimTexDocumentTypes value) { registry = value; }
        VersionContract Wrap(WhimTexDocumentContract value)
        {
            if (value == null) return null;
            if (!wrappers.TryGetValue(value, out var wrapper)) wrappers.Add(value, wrapper = new VersionContract(value));
            return wrapper;
        }
        internal VersionContract Document => Wrap(registry.Document);
        internal VersionContract Layer => Wrap(registry.Layer);
        internal VersionContract Register(Type type, string id, int currentVersion = 1, bool builtIn = false)
            => Wrap(registry.Register(type, id, currentVersion, builtIn));
        internal VersionContract Resolve(string id) => Wrap(registry.Resolve(id));
        internal VersionContract ForType(Type type) => Wrap(registry.ForType(type));
    }

    sealed class VersionContract
    {
        internal readonly WhimTexDocumentContract Raw;
        internal VersionContract(WhimTexDocumentContract raw) { Raw = raw; }
        internal Type Type => Raw.Type;
        internal string Id => Raw.Id;
        internal int CurrentVersion => Raw.CurrentVersion;
        static Delegate UpgradeDelegate<T>(Action<Json> callback) => new Action<T>(value => callback(new Json(value)));
        static Delegate ExportDelegate<T>(Func<Json, bool> callback) => new Func<T, bool>(value => callback(new Json(value)));
        static Delegate Callback(string name, object callback) => callback == null ? null :
            (Delegate)Invoke(typeof(VersionContract).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(Json.ObjectType), null, callback);
        internal VersionContract UpgradeFrom(int version, Action<Json> callback)
        { Invoke(typeof(WhimTexDocumentContract).GetMethod("UpgradeFrom"), Raw, version, Callback("UpgradeDelegate", callback)); return this; }
        internal VersionContract ExportTo(int version, Func<Json, bool> callback)
        { Invoke(typeof(WhimTexDocumentContract).GetMethod("ExportTo"), Raw, version, Callback("ExportDelegate", callback)); return this; }
        internal Json Upgrade(Json fields, int version) => new Json(Invoke(typeof(WhimTexDocumentContract).GetMethod("Upgrade"), Raw, fields?.Raw, version));
        internal Json SelectVersion(Json fields, out int version)
        {
            object[] arguments = { fields?.Raw, 0 };
            var value = Invoke(typeof(WhimTexDocumentContract).GetMethod("SelectVersion"), Raw, arguments);
            version = (int)arguments[1]; return new Json(value);
        }
    }

    static bool SameReference(object left, object right)
        => ReferenceEquals(left is Json l ? l.Raw : left, right is Json r ? r.Raw : right);

    static T Throws<T>(TestContext t, Action action, string label) where T : Exception
    {
        Exception caught = null;
        try { action(); } catch (Exception error) { caught = error; }
        t.True(caught is T, label + ": expected " + typeof(T).Name + ", got " + (caught?.ToString() ?? "success"));
        t.True(!string.IsNullOrWhiteSpace(caught.Message), label + ": diagnostic is not empty");
        return (T)caught;
    }

    static void SameJson(TestContext t, Json expected, Json actual, string label)
    {
        bool equal = Json.DeepEquals(expected, actual);
        t.True(equal, equal ? label : label + "\n" + JsonDifferences(expected, actual));
    }

    static string JsonDifferences(Json expected, Json actual)
    {
        const int limit = 8;
        var differences = new List<string>();
        string Brief(Json value)
        {
            if (value == null) return "<missing>";
            if (value.Type == "Object") return "Object(" + value.Count + " properties)";
            if (value.Type == "Array") return "Array(" + value.Count + " items)";
            string text = value.ToString().Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            if (text.Length > 160) text = text.Substring(0, 160) + "...";
            return value.Type + ": " + (value.Type == "Null" ? "null" : text);
        }
        void Difference(string path, string left, string right)
        {
            if (differences.Count < limit) differences.Add(path + ": expected " + left + "; actual " + right);
        }
        void Visit(Json left, Json right, string path)
        {
            if (differences.Count >= limit || Json.DeepEquals(left, right)) return;
            if (left == null || right == null || left.Type != right.Type)
            { Difference(path, Brief(left), Brief(right)); return; }
            if (left.Type == "Object")
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in left.PropertyNames())
                {
                    names.Add(name); Visit(left[name], right[name], path + "." + name);
                    if (differences.Count >= limit) return;
                }
                foreach (string name in right.PropertyNames())
                {
                    if (!names.Contains(name)) Visit(null, right[name], path + "." + name);
                    if (differences.Count >= limit) return;
                }
                return;
            }
            if (left.Type == "Array")
            {
                if (left.Count != right.Count) Difference(path + ".length", left.Count.ToString(), right.Count.ToString());
                for (int i = 0; i < Math.Max(left.Count, right.Count) && differences.Count < limit; i++)
                    Visit(i < left.Count ? left[i] : null, i < right.Count ? right[i] : null, path + "[" + i + "]");
                return;
            }
            Difference(path, Brief(left), Brief(right));
        }
        Visit(expected, actual, "$");
        return "First differences (up to " + limit + "):\n" + string.Join("\n", differences);
    }

    static Json Clone(Json value) => (Json)value.DeepClone();
    static Json Fields() => Json.Parse("{\"threshold\":0.12345678901234566,\"count\":7,\"optional\":null," +
        "\"nested\":{\"samples\":[1,2,3],\"mode\":\"normal\"},\"gain\":1}");

    static VersionContract Contract(int version = 3)
        => new Types().Register(typeof(NoiseLayerBehaviour), "tests.versioned_noise", version);

    static VersionContract Migrations()
        => Contract().UpgradeFrom(1, data =>
        {
            data["threshold"] = data["oldThreshold"].DeepClone();
            data.Remove("oldThreshold");
            data["nested"]["mode"] = "normal";
        }).UpgradeFrom(2, data => data["gain"] = 1);

    static bool ExportV1(Json data)
    {
        data["oldThreshold"] = data["threshold"].DeepClone();
        data.Remove("threshold"); data.Remove("gain");
        ((Json)data["nested"]).Remove("mode");
        return true;
    }

    static bool ExportV2(Json data) { data.Remove("gain"); return true; }

    public static string RunRegistry() => TestContext.Run("Document v1 registry identity and validation", t =>
    {
        var defaults = Types.Default;
        t.Equal(typeof(WhimTexDocument), defaults.Document.Type, "Default document type");
        t.Equal("whimtex.document", defaults.Document.Id, "Default document permanent ID");
        t.Equal(typeof(Layer), defaults.Layer.Type, "Default layer type");
        t.Equal("whimtex.layer", defaults.Layer.Id, "Default layer permanent ID");
        t.Equal(1, defaults.Document.CurrentVersion, "Document starts at v1");
        t.Equal(1, defaults.Layer.CurrentVersion, "Layer starts at v1");
        t.True(SameReference(defaults.Document, defaults.ForType(typeof(WhimTexDocument))), "Document contract identity");
        t.True(SameReference(defaults.Layer, defaults.Resolve("layer")), "Built-in layer alias");
        foreach (Type type in new[] { typeof(GroupLayerBehaviour), typeof(DrawingLayerBehaviour),
            typeof(GradientLayerBehaviour), typeof(NoiseLayerBehaviour), typeof(ShaderLayerBehaviour) })
        {
            var builtin = defaults.ForType(type);
            t.Equal(type, builtin.Type, "Builtin type " + type.Name);
            t.Equal(1, builtin.CurrentVersion, "Builtin starts at v1 " + type.Name);
            t.True(SameReference(builtin, defaults.Resolve(builtin.Id)), "Builtin permanent identity " + type.Name);
        }

        var registry = new Types();
        Throws<WhimTexDocumentException>(t, () => registry.Resolve("whimtex.document"), "Independent registry starts empty");
        Throws<WhimTexDocumentException>(t, () => registry.ForType(typeof(NoiseLayerBehaviour)), "Unregistered type");
        var external = registry.Register(typeof(NoiseLayerBehaviour), "vendor.noise", 4);
        var builtinNoise = registry.Register(typeof(GradientLayerBehaviour), "whimtex.noise", builtIn: true);
        t.Equal(4, external.CurrentVersion, "Explicit current version");
        t.True(SameReference(builtinNoise, registry.Resolve("noise")), "Builtin alias wins when registered second");
        t.True(SameReference(external, registry.Resolve("vendor.noise")), "Permanent external ID remains exact");
        t.True(SameReference(external, registry.ForType(typeof(NoiseLayerBehaviour))), "Type lookup keeps external identity");
        registry.Register(typeof(ColorFillLayerBehaviour), "other.noise");
        t.True(SameReference(builtinNoise, registry.Resolve("noise")), "Builtin remains preferred after later external registration");
        Throws<ArgumentException>(t, () => registry.Register(typeof(GroupLayerBehaviour), "vendor.noise"), "Duplicate permanent ID");
        Throws<ArgumentException>(t, () => registry.Register(typeof(NoiseLayerBehaviour), "vendor.other"), "Duplicate type");
        Throws<ArgumentException>(t, () => registry.Register(typeof(GroupLayerBehaviour), "other_builtin.noise", builtIn: true), "Duplicate builtin alias");
        var recovered = registry.Register(typeof(GroupLayerBehaviour), "vendor.group");
        t.True(SameReference(recovered, registry.Resolve("vendor.group")), "Rejected registrations do not reserve the type");
        Throws<WhimTexDocumentException>(t, () => registry.Resolve("vendor.missing"), "Unknown permanent ID");
        Throws<WhimTexDocumentException>(t, () => registry.Resolve("missing"), "Unknown alias");
        Throws<WhimTexDocumentException>(t, () => registry.Resolve("missing.noise"), "Unknown permanent ID does not fall back to its suffix");

        var ambiguous = new Types();
        var left = ambiguous.Register(typeof(NoiseLayerBehaviour), "left.shared");
        var right = ambiguous.Register(typeof(GradientLayerBehaviour), "right.shared");
        Throws<WhimTexDocumentException>(t, () => ambiguous.Resolve("shared"), "Ambiguous external alias");
        t.True(SameReference(left, ambiguous.Resolve("left.shared")) &&
            SameReference(right, ambiguous.Resolve("right.shared")), "Ambiguity does not damage permanent IDs");

        var aliases = new Types();
        var alias = aliases.Register(typeof(NoiseLayerBehaviour), "alias_only");
        t.True(SameReference(alias, aliases.Resolve("alias_only")) &&
            SameReference(alias, aliases.ForType(typeof(NoiseLayerBehaviour))), "Unique alias-only contract roundtrips");
        aliases.Register(typeof(GradientLayerBehaviour), "vendor.alias_only");
        Throws<WhimTexDocumentException>(t, () => aliases.Resolve("alias_only"), "Alias-only ID cannot hide ambiguity");
        Throws<WhimTexDocumentException>(t, () => aliases.ForType(typeof(NoiseLayerBehaviour)), "Ambiguous alias-only type cannot be written");
        var shadowed = new Types();
        shadowed.Register(typeof(NoiseLayerBehaviour), "noise");
        var preferred = shadowed.Register(typeof(GradientLayerBehaviour), "whimtex.noise", builtIn: true);
        t.True(SameReference(preferred, shadowed.Resolve("noise")), "Builtin beats alias-only external ID");
        Throws<WhimTexDocumentException>(t, () => shadowed.ForType(typeof(NoiseLayerBehaviour)), "Shadowed alias cannot roundtrip");

        foreach (Type type in new[] { typeof(object), typeof(string), typeof(int), typeof(ShaderFX),
            typeof(LayerBehaviour), typeof(TargetedLayerBehaviour), typeof(List<>) })
            Throws<ArgumentException>(t, () => new Types().Register(type, "tests.invalid"), "Invalid contract type " + type);
        Throws<ArgumentNullException>(t, () => registry.Register(null, "tests.null"), "Null contract type");
        foreach (string id in new[] { null, "", " ", ".noise", "noise.", "a..b", "Upper.noise", "a/b", "a-b" })
            Throws<ArgumentException>(t, () => new Types().Register(typeof(NoiseLayerBehaviour), id), "Invalid ID " + id);
        foreach (int version in new[] { 0, -1, int.MinValue })
            Throws<ArgumentOutOfRangeException>(t, () => new Types().Register(typeof(NoiseLayerBehaviour), "tests.noise", version), "Nonpositive current version");
        t.Equal(1, defaults.ForType(typeof(NoiseLayerBehaviour)).CurrentVersion, "Private registry tests leave Default unchanged");
    });

    public static string RunUpgrades() => TestContext.Run("Document v1 ordered migrations and clone isolation", t =>
    {
        var source = Json.Parse("{\"oldThreshold\":0.12345678901234566,\"count\":7,\"optional\":null,\"nested\":{\"samples\":[1,2,3]}}");
        var original = Clone(source);
        var calls = new List<int>();
        Json first = null, second = null;
        var contract = Contract().UpgradeFrom(1, data =>
        {
            first = data; calls.Add(1);
            data["threshold"] = data["oldThreshold"].DeepClone(); data.Remove("oldThreshold");
            data["nested"]["mode"] = "normal";
        }).UpgradeFrom(2, data =>
        {
            second = data; calls.Add(2);
            t.True(data["oldThreshold"] == null && data["threshold"] != null &&
                (string)data["nested"]["mode"] == "normal" && data["gain"] == null,
                "Later migration sees the preceding version's state");
            data["gain"] = 1;
        });
        Json result = contract.Upgrade(source, 1);
        SameJson(t, Fields(), result, "Ordered migration produces exact current state");
        t.True(calls.Count == 2 && calls[0] == 1 && calls[1] == 2, "Each migration runs once in version order");
        SameJson(t, original, source, "Migration leaves caller fields unchanged");
        t.True(!SameReference(first, source) && !SameReference(first["nested"], source["nested"]), "First callback receives a deep clone");
        t.True(SameReference(first, second) && SameReference(first["nested"], second["nested"]),
            "Upgrade steps evolve the same isolated clone sequentially");
        result["nested"]["samples"][0] = -123;
        SameJson(t, original, source, "Mutating the upgraded nested result cannot mutate caller input");

        calls.Clear();
        var v2 = Fields(); v2.Remove("gain");
        SameJson(t, Fields(), contract.Upgrade(v2, 2), "Starting at v2 skips v1");
        t.True(calls.Count == 1 && calls[0] == 2, "Only applicable upgrade runs");
        calls.Clear();
        var current = Fields();
        var copy = contract.Upgrade(current, 3);
        t.Equal(0, calls.Count, "Current version invokes no migration");
        copy["nested"]["samples"][0] = 99;
        SameJson(t, Fields(), current, "Current-version return also owns its nested fields");
        foreach (int version in new[] { 0, -1, 4, int.MaxValue })
        {
            Throws<WhimTexDocumentException>(t, () => contract.Upgrade(source, version), "Unsupported incoming version " + version);
            SameJson(t, original, source, "Rejected version preserves input");
        }
        var incomplete = Contract().UpgradeFrom(1, data => data["nested"]["samples"][0] = 999);
        var gap = Throws<WhimTexDocumentException>(t, () => incomplete.Upgrade(source, 1), "Missing intermediate migration");
        t.True(gap.Message.Contains("2"), "Missing migration identifies the missing step");
        SameJson(t, original, source, "Partially upgraded failure preserves input");
        var failing = Contract().UpgradeFrom(1, data =>
        { data["nested"]["samples"][0] = 999; throw new InvalidOperationException("test-upgrade-failure"); });
        Throws<InvalidOperationException>(t, () => failing.Upgrade(source, 1), "Callback failure propagates");
        SameJson(t, original, source, "Throwing callback preserves input");
        foreach (int version in new[] { -1, 0, 3, 4 })
        {
            Throws<ArgumentOutOfRangeException>(t, () => Contract().UpgradeFrom(version, _ => { }), "Invalid upgrade source " + version);
            Throws<ArgumentOutOfRangeException>(t, () => Contract().ExportTo(version, _ => false), "Invalid export target " + version);
        }
        Throws<ArgumentNullException>(t, () => Contract().UpgradeFrom(1, null), "Null upgrade callback");
        Throws<ArgumentNullException>(t, () => Contract().ExportTo(1, null), "Null export callback");
        Throws<ArgumentException>(t, () => contract.UpgradeFrom(1, _ => { }), "Duplicate upgrade registration");
        var duplicate = Contract().ExportTo(1, _ => false);
        Throws<ArgumentException>(t, () => duplicate.ExportTo(1, _ => true), "Duplicate export registration");
        Throws<ArgumentNullException>(t, () => contract.Upgrade(null, 3), "Null migration fields");
        Throws<ArgumentNullException>(t, () => contract.SelectVersion(null, out _), "Null export fields");
    });

    public static string RunExports() => TestContext.Run("Document v1 minimum lossless versions and isolated exporters", t =>
    {
        var current = Fields(); var snapshot = Clone(current);
        var order = new List<int>();
        var contract = Migrations().ExportTo(2, data => { order.Add(2); return ExportV2(data); })
            .ExportTo(1, data => { order.Add(1); return ExportV1(data); });
        var selected = contract.SelectVersion(current, out int version);
        t.Equal(1, version, "Lowest representable version wins, independently of registration order");
        t.True(order.Count == 1 && order[0] == 1, "A supported earliest target stops probing");
        t.True(selected["oldThreshold"] != null && selected["threshold"] == null && selected["gain"] == null,
            "Selected data retains historical shape after validation");
        SameJson(t, snapshot, contract.Upgrade(selected, version), "Historical export upgrades to exact current fields");
        SameJson(t, snapshot, current, "Successful export leaves input unchanged");
        selected["nested"]["samples"][0] = 99;
        SameJson(t, snapshot, current, "Returned export owns nested arrays");

        Json rejected = null, accepted = null;
        var fallback = Migrations().ExportTo(1, data =>
        {
            rejected = data; data["nested"]["samples"][0] = -999;
            data.Remove("optional"); data["poison"] = true; return false;
        }).ExportTo(2, data =>
        {
            accepted = data;
            SameJson(t, snapshot, data, "Later exporter starts with fresh current fields after false");
            return ExportV2(data);
        });
        var fallbackResult = fallback.SelectVersion(current, out int fallbackVersion);
        t.Equal(2, fallbackVersion, "False means unsupported and permits next candidate");
        t.True(!SameReference(rejected, accepted) && !SameReference(rejected["nested"], accepted["nested"]), "Export candidates share no nested objects");
        rejected["nested"]["samples"][1] = -888;
        SameJson(t, snapshot, fallback.Upgrade(fallbackResult, fallbackVersion), "Mutating rejected candidate cannot corrupt accepted candidate");
        SameJson(t, snapshot, current, "False exporter cannot mutate current data");

        var unsupported = Contract().ExportTo(1, data => { data.RemoveAll(); return false; });
        var unchanged = unsupported.SelectVersion(current, out int latest);
        t.Equal(3, latest, "No supported historical candidate selects current version");
        SameJson(t, snapshot, unchanged, "False candidate mutations do not enter current fallback");
        unchanged["nested"]["samples"][0] = 15;
        SameJson(t, snapshot, current, "Current fallback is a deep clone");

        var damages = new Action<Json>[] {
            data => data.Remove("optional"),
            data => data["count"] = 7.0,
            data => data["oldThreshold"] = (float)(double)data["oldThreshold"],
            data => data["nested"]["samples"] = Json.Parse("{\"values\":[3,2,1]}")["values"],
            data => data["unexpected"] = true,
            data => ((Json)data["nested"]).Remove("samples")
        };
        for (int i = 0; i < damages.Length; i++)
        {
            int index = i;
            bool laterCalled = false;
            var lossy = Migrations().ExportTo(1, data => { ExportV1(data); damages[index](data); return true; })
                .ExportTo(2, data => { laterCalled = true; return ExportV2(data); });
            var error = Throws<WhimTexDocumentException>(t, () => lossy.SelectVersion(current, out _), "Successful but lossy exporter " + i);
            t.True(error.Message.Contains("tests.versioned_noise"), "Lossy export identifies contract " + i);
            t.True(!laterCalled, "Lossy true result is an error, not an unsupported candidate " + i);
            SameJson(t, snapshot, current, "Lossy exporter preserves original " + i);
        }
        var noUpgrade = Contract().ExportTo(1, data => true);
        Throws<WhimTexDocumentException>(t, () => noUpgrade.SelectVersion(current, out _), "Successful export without complete reverse migration is rejected");
        var throws = Contract().ExportTo(1, data =>
        { data["nested"]["samples"][0] = 1000; throw new InvalidOperationException("test-export-failure"); });
        Throws<InvalidOperationException>(t, () => throws.SelectVersion(current, out _), "Throwing exporter propagates");
        SameJson(t, snapshot, current, "Throwing exporter leaves caller fields unchanged");

        var independent = new Types();
        var document = independent.Register(typeof(WhimTexDocument), "tests.document", 2);
        var layer = independent.Register(typeof(Layer), "tests.layer", 4).UpgradeFrom(2, data => data["v3"] = false)
            .UpgradeFrom(3, data => data["v4"] = 1).ExportTo(1, _ => false)
            .ExportTo(2, data => { data.Remove("v3"); data.Remove("v4"); return true; });
        var behaviour = independent.Register(typeof(NoiseLayerBehaviour), "tests.noise", 3)
            .UpgradeFrom(1, data => data["seed"] = 1337).UpgradeFrom(2, data => data["inactive"] = 73)
            .ExportTo(1, data => { data.Remove("seed"); data.Remove("inactive"); return true; });
        var documentFields = Json.Parse("{\"width\":16}");
        var layerFields = Json.Parse("{\"opacity\":0.5,\"v3\":false,\"v4\":1}");
        var behaviourFields = Json.Parse("{\"scale\":8,\"seed\":1337,\"inactive\":73}");
        var oldDocument = document.SelectVersion(documentFields, out int dv);
        var oldLayer = layer.SelectVersion(layerFields, out int lv);
        var oldBehaviour = behaviour.SelectVersion(behaviourFields, out int bv);
        t.Equal(2, dv, "Independent document minimum"); t.Equal(2, lv, "Independent layer minimum");
        t.Equal(1, bv, "Independent behaviour minimum");
        SameJson(t, documentFields, document.Upgrade(oldDocument, dv), "Document reverse validation");
        SameJson(t, layerFields, layer.Upgrade(oldLayer, lv), "Layer reverse validation");
        SameJson(t, behaviourFields, behaviour.Upgrade(oldBehaviour, bv), "Behaviour reverse validation including inactive field");
    });

    static Json Full(WhimTexDocument document, bool omitDrawing = false) => Json.Parse(WhimTexDocumentJson.Write(document,
        new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full, AllowDrawingOmission = omitDrawing }).Json);

    static void ReadRejected(TestContext t, Json root, string label)
        => Throws<WhimTexDocumentException>(t, () => { using var read = WhimTexDocumentJson.Read(root.ToString(), false); }, label);

    static void Envelopes(TestContext t, Json root)
    {
        t.Equal("whimtex.document", (string)root["format"], "Current document format");
        t.Equal(1, (int)root["documentVersion"], "Root uses documentVersion v1");
        t.True(root["version"] == null, "Old root version is absent");
        void Visit(Json layers)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i]; var behaviour = layer["behaviour"];
                t.Equal(1, (int)layer["layerVersion"], "Layer v1 " + i);
                t.Equal(1, (int)behaviour["behaviourVersion"], "Behaviour v1 " + i);
                t.True(behaviour.Count == 3 && behaviour["data"]?.Type == "Object" && behaviour["$type"] == null,
                    "Behaviour has exactly type/behaviourVersion/data");
                t.True(((string)behaviour["type"]).StartsWith("whimtex.", StringComparison.Ordinal), "Builtin writer emits a permanent type ID");
                t.True(layer["rendering"]?.Type == "Object" && layer["opacity"] == null && layer["blendMode"] == null,
                    "Layer rendering fields have their own wrapper");
                if (layer["children"]?.Type == "Array") Visit(layer["children"]);
            }
        }
        Visit(root["layers"]);
    }

    public static string RunJson() => TestContext.Run("Document v1 envelopes, defaults, optional IDs and inactive fields", t =>
    {
        const string minimal = "{\"format\":\"whimtex.document\",\"document\":{\"width\":16,\"height\":12}," +
            "\"layers\":[{\"behaviour\":{\"type\":\"noise\",\"data\":{}}}]}";
        using var initial = WhimTexDocumentJson.Read(minimal, false);
        t.Equal(0, initial.Warnings.Count, "Minimal current document has no warning");
        var document = initial.Document;
        var noise = (NoiseLayerBehaviour)document.layers[0].Behaviour;
        var defaults = new NoiseLayerBehaviour();
        t.Equal(defaults.seed, noise.seed, "Missing noise seed uses current default");
        t.Equal(defaults.scale, noise.scale, "Missing scale uses current default");
        t.Equal(defaults.warpStrength, noise.warpStrength, "Missing inactive field uses current default");
        t.Equal(1f, document.layers[0].opacity, "Missing rendering wrapper uses current layer defaults");
        t.True(!string.IsNullOrEmpty(document.layers[0].Id), "Missing layer ID is generated");
        var canonical = Full(document);
        Envelopes(t, canonical);
        for (int mask = 0; mask < 8; mask++)
        {
            var missing = Clone(canonical);
            if ((mask & 1) != 0) missing.Remove("documentVersion");
            if ((mask & 2) != 0) missing["layers"][0].Remove("layerVersion");
            if ((mask & 4) != 0) missing["layers"][0]["behaviour"].Remove("behaviourVersion");
            using var read = WhimTexDocumentJson.Read(missing.ToString(), false);
            SameJson(t, canonical, Full(read.Document), "Every missing-version combination defaults independently: " + mask);
        }
        var badValues = new[] { Json.Scalar(0), Json.Scalar(-1), Json.Scalar(2), Json.Scalar(int.MaxValue),
            Json.Scalar(1.5), Json.Scalar("1"), Json.Scalar(null), Json.Scalar(true) };
        foreach (var bad in badValues)
        {
            var root = Clone(canonical); root["documentVersion"] = bad.DeepClone();
            ReadRejected(t, root, "Invalid documentVersion " + bad);
            root = Clone(canonical); root["layers"][0]["layerVersion"] = bad.DeepClone();
            ReadRejected(t, root, "Invalid layerVersion " + bad);
            root = Clone(canonical); root["layers"][0]["behaviour"]["behaviourVersion"] = bad.DeepClone();
            ReadRejected(t, root, "Invalid behaviourVersion " + bad);
        }
        var legacy = Clone(canonical); legacy.Remove("documentVersion"); legacy["version"] = 3;
        ReadRejected(t, legacy, "Explicit old document format is not current missing-version shorthand");
        legacy["documentVersion"] = 1;
        ReadRejected(t, legacy, "Mixing old and new version fields is rejected");
        var legacyBehaviour = Clone(canonical);
        legacyBehaviour["layers"][0]["behaviour"] = Json.Parse("{\"$type\":\"NoiseLayerBehaviour\"}");
        ReadRejected(t, legacyBehaviour, "Old flattened behaviour format is rejected");
        foreach (string type in new[] { "missing", "vendor.missing", "whimtex.document", "whimtex.layer" })
        {
            var root = Clone(canonical); root["layers"][0]["behaviour"]["type"] = type;
            ReadRejected(t, root, "Unknown or non-behaviour type " + type);
        }
        var flatRendering = Clone(canonical); flatRendering["layers"][0]["opacity"] = .25;
        ReadRejected(t, flatRendering, "Flattened rendering field is rejected");
        var unknownRendering = Clone(canonical); unknownRendering["layers"][0]["rendering"]["unknown"] = 1;
        ReadRejected(t, unknownRendering, "Unknown rendering field is rejected");

        noise.warp = NoiseLayerBehaviour.WarpType.None; noise.warpStrength = 73;
        noise.linkScale = false; noise.scale = 3.25f; noise.scaleY = 6.75f; noise.scaleZ = 19.5f;
        noise.seed = -412; document.layers[0].enabled = false; document.layers[0].opacity = .375f;
        var transform = document.layers[0].transform;
        transform.rotation = 37.123456789012344;
        transform.position = new Double2(0.12345678901234566, -12.987654321098764);
        transform.scale = new Double2(1.0000000000000002, .9876543210987654);
        document.layers[0].transform = transform;
        var expected = Full(document);
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            var written = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode });
            using var read = WhimTexDocumentJson.Read(written.Json, false);
            SameJson(t, expected, Full(read.Document), "Full current state survives " + mode);
            var actual = read.Document.layers[0]; var loaded = (NoiseLayerBehaviour)actual.Behaviour;
            t.Equal(73f, loaded.warpStrength, "Inactive warp strength survives " + mode);
            t.Equal(19.5f, loaded.scaleZ, "Inactive third dimension survives " + mode);
            t.True(!actual.enabled, "Disabled layer survives " + mode);
            t.Equal(BitConverter.DoubleToInt64Bits(transform.rotation), BitConverter.DoubleToInt64Bits(actual.transform.rotation), "Rotation retains every double bit " + mode);
            t.Equal(BitConverter.DoubleToInt64Bits(transform.position.x), BitConverter.DoubleToInt64Bits(actual.transform.position.x), "Position retains every double bit " + mode);
            t.Equal(BitConverter.DoubleToInt64Bits(transform.scale.x), BitConverter.DoubleToInt64Bits(actual.transform.scale.x), "Scale retains every double bit " + mode);
            using var again = WhimTexDocumentJson.Read(Full(read.Document).ToString(), false);
            SameJson(t, expected, Full(again.Document), "Repeated full roundtrip is stable " + mode);
            SameJson(t, expected, Full(document), "Writer does not change source " + mode);
        }
        using var nested = WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"layers\":[" +
            "{\"behaviour\":{\"type\":\"group\",\"data\":{}},\"children\":[" +
            "{\"behaviour\":{\"type\":\"noise\",\"data\":{}}}," +
            "{\"behaviour\":{\"type\":\"gradient\",\"data\":{\"gradientType\":\"Linear\"}}}]}]}", false);
        var ids = new HashSet<string>();
        var group = nested.Document.layers[0];
        t.True(ids.Add(group.Id) && ids.Add(group.children[0].Id) && ids.Add(group.children[1].Id), "Generated optional IDs are unique through children");
        var nestedFull = Full(nested.Document); Envelopes(t, nestedFull);
        using var nestedAgain = WhimTexDocumentJson.Read(nestedFull.ToString(), false);
        SameJson(t, nestedFull, Full(nestedAgain.Document), "Nested optional IDs become stable persisted IDs");
    });

    static object Call(object owner, string name, params object[] arguments)
        => Invoke(owner.GetType().GetMethod(name, Instance) ?? throw new MissingMethodException(owner.GetType().FullName, name), owner, arguments);

    sealed class Owned : IDisposable
    {
        readonly List<Object> objects = new List<Object>();
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        string folder;
        internal T Track<T>(T value) where T : Object { objects.Add(value); return value; }
        internal string Folder()
        {
            if (folder != null) return folder;
            string name = "__WhimTexDocumentV1_" + Guid.NewGuid().ToString("N");
            string candidate = "Assets/" + name;
            if (Directory.Exists(candidate) || File.Exists(candidate + ".meta")) throw new IOException("Owned fixture path already exists.");
            string guid = AssetDatabase.CreateFolder("Assets", name);
            if (string.IsNullOrEmpty(guid)) throw new IOException("Cannot create owned TIFF fixture folder.");
            folder = candidate;
            return folder;
        }
        public void Dispose()
        {
            try
            {
                foreach (var obj in objects) if (obj != null) Undo.ClearUndo(obj);
                // Documents own their Drawing textures and embedded FX; fallback entries handle partial setup.
                foreach (var obj in objects) if (obj is WhimTexDocument && obj != null) Object.DestroyImmediate(obj);
                foreach (var obj in objects) if (obj != null) Object.DestroyImmediate(obj);
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (folder != null && !AssetDatabase.DeleteAsset(folder) && Directory.Exists(folder))
                    throw new IOException("Failed to remove owned TIFF fixture: " + folder);
            }
        }
    }

    static Layer Add(List<Layer> layers, LayerBehaviour behaviour, string name)
    {
        var layer = new Layer(behaviour) { layerName = name };
        Call(layer, "AssignNewId"); layers.Add(layer); return layer;
    }

    static Color32[] DrawingPixels()
    {
        var pixels = new Color32[12];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32((byte)(i * 19 + 2), (byte)(251 - i * 17), (byte)(i * 7 + 31), (byte)(i * 23));
        return pixels;
    }

    static void VerifyDrawing(TestContext t, DrawingLayerBehaviour drawing, Color32[] expected)
    {
        // This is owned by the behaviour, not a caller-owned preview; do not destroy it.
        var pixels = drawing.GetPreviewTexture(4);
        t.True(pixels != null && pixels.width == 4 && pixels.height == 3, "Original Drawing dimensions survive canvas fitting");
        var actual = pixels.GetPixels32();
        t.Equal(expected.Length, actual.Length, "Drawing pixel count");
        for (int i = 0; i < expected.Length; i++) t.Equal(expected[i], actual[i], "Exact raw Drawing RGBA including transparent RGB " + i);
    }

    static DrawingLayerBehaviour FixtureDrawing(WhimTexDocument document)
        => (DrawingLayerBehaviour)document.layers[0].children[0].children[0].Behaviour;

    static bool Deferred(DrawingLayerBehaviour drawing)
        => (bool)typeof(DrawingLayerBehaviour).GetProperty("HasDeferredTexture", Instance).GetValue(drawing);

    public static string RunTiff() => TestContext.Run("Document v1 normal TIFF save, deferred reopen and graph/code persistence", t =>
    {
        using var owned = new Owned();
        var document = owned.Track(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = 16; document.height = 12; document.outputSrgb = false; document.outputFilter = FilterMode.Point;
        var outer = Add(document.layers, new GroupLayerBehaviour(), "Outer v1");
        outer.children ??= new List<Layer>(); outer.opacity = .625f;
        outer.transform.position = new Double2(.12345678901234566, -.9876543210987654);
        outer.transform.rotation = 17.123456789012344;
        var inner = Add(outer.children, new GroupLayerBehaviour(), "Inner v1");
        inner.children ??= new List<Layer>();
        var drawing = new DrawingLayerBehaviour();
        var drawingLayer = Add(inner.children, drawing, "Owned pixels");
        var raw = owned.Track(new Texture2D(4, 3, TextureFormat.RGBA32, false, true));
        var expectedPixels = DrawingPixels(); raw.SetPixels32(expectedPixels); raw.Apply(false, false);
        Call(drawing, "AdoptStoredTexture", raw);
        var gradientLayer = Add(inner.children, new GradientLayerBehaviour
        { gradientType = GradientLayerBehaviour.GradientType.Linear, circularRepetitions = 3.25f, circularStartAngle = 123.75f }, "Gradient dormant angular settings");
        var noiseLayer = Add(outer.children, new NoiseLayerBehaviour
        { seed = -701, scale = 3.5f, scaleY = 7.25f, scaleZ = 19.5f, linkScale = false,
            warp = NoiseLayerBehaviour.WarpType.None, warpStrength = 73f }, "Noise dormant warp");
        noiseLayer.opacity = .25f;
        var shader = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = ShaderGraph.CreateDefault() };
        shader.draftGraph.parameters[0].colorValue = new Color(.25f, .5f, .75f, .625f);
        shader.draftGraph.notes.Add(new ShaderGraphNote { title = "Stored graph", contents = "Version one", position = new Vector2(23, 41) });
        // CreateDefault leaves parameterLayout empty. Author the normal layout before
        // compilation captures workingGraph, so reader normalization is not mistaken for data loss.
        var layout = typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphParameterLayout", true);
        Invoke(layout.GetMethod("Normalize", BindingFlags.Static | BindingFlags.NonPublic), null, shader.draftGraph);
        t.Equal(1, shader.draftGraph.parameterLayout.Count, "Authored graph explicitly lays out its parameter");
        t.Equal(shader.draftGraph.parameters[0].id, shader.draftGraph.parameterLayout[0].parameterId, "Layout references the authored parameter");
        shader.draftCode = "float4 ApplyFX(PinnedUV uv, float4 color) { return float4(0.125,0.25,0.5,1); }";
        shader.workingCode = shader.draftCode;
        var graphLayer = Add(document.layers, shader, "Graph with inactive Code"); graphLayer.opacity = .375f;
        Call(document, "NormalizeModel");
        t.True((bool)Call(shader, "CompileGraph", document), "Owned graph compiles for normal TIFF saving");
        t.Equal(1, shader.program.workingGraph.parameterLayout.Count, "Working graph captures the normalized authored layout");
        var comment = document.AddComment(new Vector2(.25f, .75f), "Document v1 / 评论\nOwned Drawing", "Fixture author",
            attachments: new List<WhimTexCommentAttachment> { WhimTexCommentAttachment.ForLayer(drawingLayer) });
        document.AddCommentMessage(comment.id, "Retain nested pixels and inactive settings", "Fixture reviewer");
        comment.messages[0].createdUtcTicks = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks;
        comment.messages[1].createdUtcTicks = comment.messages[0].createdUtcTicks + TimeSpan.TicksPerMinute;
        document.SetCommentStatus(comment.id, WhimTexCommentStatus.AwaitingReview);
        var effect = owned.Track((ShaderFX)Call(document, "AddEmbeddedShaderFX", gradientLayer));
        Call(effect, "SetDraftCode", "float4 ApplyFX(PinnedUV uv, float4 color) { return color * float4(0.5,1,0.75,1); }");
        Call(effect, "CompileAgentDraft");
        t.True((bool)typeof(ShaderFX).GetProperty("HasWorkingShader", Instance).GetValue(effect), "Owned FX has an executable program");
        effect.Active = false;
        noiseLayer.fx.Add(effect);
        var beforeComposite = owned.Track(document.ComposeCanvas());
        var expectedComposite = beforeComposite.GetPixels();
        var before = Full(document, true); Envelopes(t, before);
        string path = WhimTexDocumentFile.Save(document, owned.Folder() + "/document-v1.tiff");
        t.True(File.Exists(path) && WhimTexDocumentFile.IsDocument(path), "Normal API saves a recognized TIFF");
        t.True(AssetImporter.GetAtPath(path) is TextureImporter, "Native texture importer owns the saved carrier");

        var carrier = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexTiffCarrier", true);
        using (var container = (WhimTexDocumentContainer)Invoke(carrier.GetMethod("OpenContainer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), null, path))
        {
            byte[] bytes = container.Get(WhimTexDocumentContainer.DocumentBlock);
            byte[] signature = System.Text.Encoding.ASCII.GetBytes("WTDocument\0");
            t.True(bytes.Length > signature.Length, "TIFF model includes signature and JSON");
            for (int i = 0; i < signature.Length; i++) t.Equal(signature[i], bytes[i], "Canonical model signature byte " + i);
            var model = Json.Parse(new System.Text.UTF8Encoding(false, true).GetString(bytes, signature.Length, bytes.Length - signature.Length));
            Envelopes(t, model);
        }
        var loaded = owned.Track(WhimTexDocumentFile.Load(path));
        t.True(loaded != document && !AssetDatabase.Contains(loaded), "Load reconstructs an independent transient document");
        var reopenedDrawing = FixtureDrawing(loaded);
        t.True(Deferred(reopenedDrawing),
            "Normal TIFF reopen defers raw Drawing decoding until first access");
        SameJson(t, before, Full(loaded, true), "TIFF reconstructs full JSON model including inactive state and IDs");
        t.Equal(1, loaded.comments.Count, "TIFF restores the separate comments block");
        t.Equal(comment.id, loaded.comments[0].id, "Comment ID survives TIFF");
        t.Equal(WhimTexCommentStatus.AwaitingReview, loaded.comments[0].Status, "Comment workflow status survives TIFF");
        t.Equal(2, loaded.comments[0].messages.Count, "Comment replies survive TIFF");
        t.Equal(comment.messages[0].text, loaded.comments[0].messages[0].text, "Unicode and multiline comment text survives TIFF");
        t.Equal(comment.messages[0].createdUtcTicks, loaded.comments[0].messages[0].createdUtcTicks, "Comment timestamp survives TIFF");
        t.Equal(1, loaded.comments[0].messages[0].attachments.Count, "Comment layer attachment survives TIFF");
        t.Equal(drawingLayer.Id, loaded.layers[0].children[0].children[0].Id, "Deeply nested Drawing ID survives");
        t.True(ReferenceEquals(loaded.layers[0].children[0].children[1].fx[0], loaded.layers[0].children[1].fx[0]), "Shared FX identity survives TIFF");
        t.True(!loaded.layers[0].children[1].fx[0].Active, "Disabled FX retains its full program and disabled state");

        // Save through the ordinary API before any explicit Drawing pixel access. Saving may
        // materialize its source; the reopened file must still have independent deferred pixels.
        t.True(Deferred(reopenedDrawing), "Metadata inspection leaves Drawing deferred before resave");
        string deferredPath = WhimTexDocumentFile.Save(loaded, owned.Folder() + "/deferred-resave.tiff");
        var deferredAgain = owned.Track(WhimTexDocumentFile.Load(deferredPath));
        t.True(Deferred(FixtureDrawing(deferredAgain)), "Resaving a deferred source produces a lazily opened Drawing");
        SameJson(t, before, Full(deferredAgain, true), "Resave before pixel access preserves complete state and comments");
        VerifyDrawing(t, FixtureDrawing(deferredAgain), expectedPixels);
        t.True(!Deferred(FixtureDrawing(deferredAgain)), "First pixel access materializes the resaved deferred Drawing");

        var cloneSource = owned.Track(WhimTexDocumentFile.Load(path));
        t.True(Deferred(FixtureDrawing(cloneSource)), "Clone fixture starts with deferred pixels");
        var clone = owned.Track((WhimTexDocument)Invoke(typeof(WhimTexDocumentFile).GetMethod("CreateEditableCopy",
            BindingFlags.Static | BindingFlags.NonPublic), null, cloneSource));
        t.True(clone != cloneSource && !AssetDatabase.Contains(clone), "Editable copy is an independent transient document");
        SameJson(t, before, Full(clone, true), "Editable copy retains complete graph, comments, dormant settings and IDs");
        var cloneDrawing = FixtureDrawing(clone);
        VerifyDrawing(t, cloneDrawing, expectedPixels);
        var clonePixels = cloneDrawing.GetPreviewTexture(4);
        var sourcePixels = FixtureDrawing(cloneSource).GetPreviewTexture(4);
        t.True(!ReferenceEquals(clonePixels, sourcePixels), "Editable copy owns distinct Drawing pixels");
        var cloneFx = clone.layers[0].children[1].fx[0];
        var sourceFx = cloneSource.layers[0].children[1].fx[0];
        t.True(!ReferenceEquals(cloneFx, sourceFx) && ReferenceEquals(cloneFx, clone.layers[0].children[0].children[1].fx[0]),
            "Editable copy owns a distinct FX while preserving internal sharing");
        t.True(!ReferenceEquals(clone.comments[0], cloneSource.comments[0]) &&
            !ReferenceEquals(clone.comments[0].messages[0], cloneSource.comments[0].messages[0]), "Editable copy owns comment objects");
        clonePixels.SetPixel(1, 0, Color.magenta); clonePixels.Apply(false, false);
        cloneFx.Active = true;
        clone.comments[0].messages[0].text = "Changed only in the owned copy";
        SameJson(t, before, Full(cloneSource, true), "Editing copy FX and comments cannot change its source model");
        VerifyDrawing(t, FixtureDrawing(cloneSource), expectedPixels);
        Undo.ClearUndo(clone); Object.DestroyImmediate(clone);
        t.True(sourceFx != null && !sourceFx.Active, "Destroying the editable copy preserves source FX");
        VerifyDrawing(t, FixtureDrawing(cloneSource), expectedPixels);

        VerifyDrawing(t, reopenedDrawing, expectedPixels);
        t.True(!Deferred(reopenedDrawing), "Drawing access materializes deferred pixels");
        var afterComposite = owned.Track(loaded.ComposeCanvas()); var actualComposite = afterComposite.GetPixels();
        t.Equal(expectedComposite.Length, actualComposite.Length, "Composite dimensions survive");
        for (int i = 0; i < expectedComposite.Length; i++)
            for (int channel = 0; channel < 4; channel++)
                t.Near(expectedComposite[i][channel], actualComposite[i][channel], .00001, "TIFF rendered pixel " + i + " channel " + channel);
        var reopenedShader = (ShaderLayerBehaviour)loaded.layers[1].Behaviour;
        t.Equal(shader.draftCode, reopenedShader.draftCode, "Inactive Code survives active Graph");
        t.Equal(shader.draftGraph.edges.Count, reopenedShader.draftGraph.edges.Count, "Graph wiring survives");
        t.Equal("Version one", reopenedShader.draftGraph.notes[0].contents, "Graph authoring state survives");
        SameJson(t, before, Full(document, true), "Save/reopen does not mutate source model");
        // A second ordinary save/reopen also exercises materialized texture storage.
        WhimTexDocumentFile.Save(loaded, deferredPath);
        var again = owned.Track(WhimTexDocumentFile.Load(deferredPath));
        SameJson(t, Full(loaded, true), Full(again, true), "Resaved TIFF retains the same complete model");
        VerifyDrawing(t, (DrawingLayerBehaviour)again.layers[0].children[0].children[0].Behaviour, expectedPixels);
    });
}
