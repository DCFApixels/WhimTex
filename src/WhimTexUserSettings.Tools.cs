using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexUserSettings
    {
        internal const string ToolSettingsKey = "DCFApixels.WhimTex.Canvas.ToolSettings";
        internal const string PaintSettingsKey = "DCFApixels.WhimTex.Canvas.PaintToolSettings";
        internal const string ActiveToolKey = "DCFApixels.WhimTex.Canvas.Tool";
        internal const string ReturnToolKey = "DCFApixels.WhimTex.Canvas.TransformReturnTool";
        private static readonly List<ToolPreferenceSession> toolSessions = new();
        private static readonly ToolPreferenceStore editorTools = new();

        internal interface IToolPreferenceStore
        {
            string Read(string name);
            void Write(string name, string value);
        }

        private sealed class ToolPreferenceStore : IToolPreferenceStore
        {
            private string cached;
            private JObject values;
            private static string ExistingKey(string name) => name == "paint" ? PaintSettingsKey :
                name == "activeTool" ? ActiveToolKey : name == "returnTool" ? ReturnToolKey : null;
            private JObject Values
            {
                get
                {
                    string json = EditorPrefs.GetString(ToolSettingsKey, "{}");
                    if (values != null && json == cached) return values;
                    cached = json;
                    try { values = JObject.Parse(json); }
                    catch (JsonException) { values = new JObject(); Debug.LogWarning("Could not read WhimTex tool settings. Defaults will be used."); }
                    return values;
                }
            }
            public string Read(string name)
            {
                string key = ExistingKey(name);
                return key != null ? EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null :
                    Values[name] is JValue value && value.Type == JTokenType.String ? value.Value<string>() : null;
            }
            public void Write(string name, string value)
            {
                string key = ExistingKey(name);
                if (key != null) { EditorPrefs.SetString(key, value); return; }
                var data = Values; data[name] = value;
                cached = data.ToString(Formatting.None); EditorPrefs.SetString(ToolSettingsKey, cached);
            }
            internal void Reset()
            {
                EditorPrefs.DeleteKey(ToolSettingsKey); EditorPrefs.DeleteKey(PaintSettingsKey);
                EditorPrefs.DeleteKey(ActiveToolKey); EditorPrefs.DeleteKey(ReturnToolKey);
                cached = null; values = null;
            }
        }

        // JsonUtility ignores unknown fields; reject retired gradient data before it can do so.
        internal static void ValidateGradientPreferences(JToken token)
        {
            if (token is JObject value)
            {
                if (value["colors"] is JArray && value["alphas"] is JArray)
                {
                    if (value["mode"] != null || value["colorMode"] == null || value["interpolation"] == null)
                        throw new FormatException("Unsupported saved gradient at " + value.Path + ": colorMode and interpolation are required; the former mode is not supported.");
                    if (value["customKeys"] is JArray custom && custom.Count != 0)
                    {
                        if (value["customColorMode"] == null)
                            throw new FormatException("Saved Custom gradient requires customColorMode at " + value.Path + ".");
                        foreach (var item in custom)
                            AgentJson.Keys(AgentJson.Obj(item, "Custom gradient key"), "id", "channel", "time", "value", "inTangent", "outTangent", "tangentMode");
                    }
                    if (value["customKeys"] != null && value["customKeys"].Type != JTokenType.Null && !(value["customKeys"] is JArray))
                        throw new FormatException("Saved customKeys must be an array at " + value.Path + ".");
                    WhimTexDocumentJson.ValidateGradientStorage(JsonUtility.FromJson<WhimTexGradient>(value.ToString(Formatting.None)));
                }
                foreach (var property in value.Properties()) ValidateGradientPreferences(property.Value);
            }
            else if (token is JArray array)
                foreach (var item in array) ValidateGradientPreferences(item);
        }

        internal static void ResetToolSettings()
        {
            editorTools.Reset();
            foreach (var session in toolSessions.ToArray())
                if (ReferenceEquals(session.Store, editorTools)) session.Reset();
        }

        // Each window owns values/resources. Only confirmed settings cross this boundary;
        // neither window serialization nor the document is the preferences store.
        internal sealed class ToolPreferenceSession : IDisposable
        {
            private sealed class DelegateStore : IToolPreferenceStore
            {
                private readonly Func<string, string> read;
                private readonly Action<string, string> write;
                internal DelegateStore(Func<string, string> read, Action<string, string> write) { this.read = read; this.write = write; }
                public string Read(string name) => read(name);
                public void Write(string name, string value) => write(name, value);
            }
            private readonly List<IEntry> entries = new();
            private bool queued, disposed;
            internal IToolPreferenceStore Store { get; }
            internal Action Restored;

            internal ToolPreferenceSession(IToolPreferenceStore store = null)
            {
                Store = store ?? editorTools;
                toolSessions.Add(this);
                AssemblyReloadEvents.beforeAssemblyReload += Flush;
                EditorApplication.quitting += Flush;
            }
            internal ToolPreferenceSession(Func<string, string> read, Action<string, string> write)
                : this(new DelegateStore(read, write)) { }

            internal void Register<T>(string name, Func<T> get, Action<T> set, Func<T> defaults,
                Func<T, T> normalize = null, Func<T, string> encode = null, Func<string, T> decode = null, bool refresh = true)
            {
                foreach (var entry in entries) if (entry.Name == name) throw new ArgumentException("Duplicate tool preference: " + name);
                bool scalar = typeof(T).IsPrimitive || typeof(T).IsEnum || typeof(T) == typeof(string);
                encode ??= value => new JObject
                {
                    ["value"] = value is null ? JValue.CreateNull() : scalar ? JToken.FromObject(value) : JToken.Parse(JsonUtility.ToJson(value))
                }.ToString(Formatting.None);
                decode ??= json =>
                {
                    if (JToken.Parse(json) is not JObject box || box["value"] == null) throw new FormatException("Expected a settings object.");
                    JToken value = box["value"];
                    if (value.Type == JTokenType.Null) return defaults();
                    if (scalar) return value.ToObject<T>();
                    if (value is not JObject) throw new FormatException("Expected a settings value object.");
                    ValidateGradientPreferences(value);
                    object result = defaults(); JsonUtility.FromJsonOverwrite(value.ToString(Formatting.None), result); return (T)result;
                };
                var added = new Entry<T>(name, get, set, defaults, normalize, encode, decode, refresh);
                entries.Add(added); added.Load(Store);
            }

            internal void ScheduleSave()
            {
                if (disposed || queued) return;
                queued = true; EditorApplication.delayCall += Flush;
            }
            internal void Flush()
            {
                if (disposed) return;
                queued = false; EditorApplication.delayCall -= Flush;
                foreach (var entry in entries) entry.Save(Store);
            }
            internal void Refresh()
            {
                if (disposed) return;
                Flush(); bool changed = false;
                foreach (var entry in entries) changed |= entry.Refresh(Store);
                if (changed) Restored?.Invoke();
            }
            internal void Reset()
            {
                queued = false; EditorApplication.delayCall -= Flush;
                foreach (var entry in entries) entry.Reset();
                Restored?.Invoke();
            }
            public void Dispose()
            {
                if (disposed) return;
                Flush(); disposed = true; Restored = null;
                EditorApplication.delayCall -= Flush;
                AssemblyReloadEvents.beforeAssemblyReload -= Flush; EditorApplication.quitting -= Flush;
                toolSessions.Remove(this);
            }

            private interface IEntry
            {
                string Name { get; }
                void Load(IToolPreferenceStore store);
                void Save(IToolPreferenceStore store);
                bool Refresh(IToolPreferenceStore store);
                void Reset();
            }
            private sealed class Entry<T> : IEntry
            {
                public string Name { get; }
                private readonly Func<T> get, defaults;
                private readonly Action<T> set;
                private readonly Func<T, T> normalize;
                private readonly Func<T, string> encode;
                private readonly Func<string, T> decode;
                private readonly bool refresh;
                private string baseline, loaded;
                private bool seedUnstored, pendingMerged;
                internal Entry(string name, Func<T> get, Action<T> set, Func<T> defaults, Func<T, T> normalize,
                    Func<T, string> encode, Func<string, T> decode, bool refresh)
                { Name = name; this.get = get; this.set = set; this.defaults = defaults; this.normalize = normalize; this.encode = encode; this.decode = decode; this.refresh = refresh; }
                private T Read(string raw)
                {
                    try { T value = raw == null ? defaults() : decode(raw); return normalize != null ? normalize(value) : value; }
                    catch (Exception error) when (error is ArgumentException || error is FormatException || error is JsonException || error is OverflowException)
                    { Debug.LogWarning("Could not restore WhimTex tool setting '" + Name + "'. Using defaults. " + error.Message); return defaults(); }
                }
                public void Load(IToolPreferenceStore store)
                {
                    loaded = store.Read(Name);
                    T initial = get();
                    T value = loaded == null && initial is not null ? initial : Read(loaded);
                    set(normalize != null ? normalize(value) : value); baseline = encode(get());
                    seedUnstored = loaded == null && baseline != encode(defaults());
                }
                public void Save(IToolPreferenceStore store)
                {
                    string current = encode(get());
                    if (current == baseline && !seedUnstored) return;
                    string remote = store.Read(Name);
                    string merged = remote == null || remote == baseline ? current : Merge(baseline, current, remote);
                    if (merged != remote) store.Write(Name, merged);
                    // Keep the local baseline separate from remote data until the next refresh.
                    // Closing a stale window therefore cannot re-publish unchanged fields.
                    baseline = current; loaded = merged; seedUnstored = false; pendingMerged = current != merged;
                }
                public bool Refresh(IToolPreferenceStore store)
                {
                    string raw = store.Read(Name);
                    if (raw == loaded && !pendingMerged) return false;
                    loaded = raw;
                    if (refresh) set(Read(raw));
                    baseline = encode(get()); pendingMerged = false; return refresh;
                }
                public void Reset() { set(defaults()); loaded = null; baseline = encode(get()); seedUnstored = pendingMerged = false; }
            }

            private static string Merge(string before, string after, string remote)
            {
                try { return Patch(JToken.Parse(before), JToken.Parse(after), JToken.Parse(remote)).ToString(Formatting.None); }
                catch (JsonException) { return after; } // Scalar tool names and malformed previous preferences.
            }
            private static JToken Patch(JToken before, JToken after, JToken remote)
            {
                if (JToken.DeepEquals(before, after)) return remote?.DeepClone();
                if (before is JObject oldObject && after is JObject newObject && remote is JObject remoteObject)
                {
                    var result = (JObject)remoteObject.DeepClone();
                    foreach (var property in newObject.Properties())
                        if (!JToken.DeepEquals(oldObject[property.Name], property.Value))
                            result[property.Name] = Patch(oldObject[property.Name], property.Value, remoteObject[property.Name]);
                    return result;
                }
                return after.DeepClone(); // A gradient's key array is one user value, not index-merged.
            }
        }
    }
}
