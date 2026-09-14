using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    internal static class MissingLayerRecovery
    {
        internal sealed class Record
        {
            internal string Error;
            internal JObject Data;
        }

        internal sealed class Report
        {
            internal int Copied;
            internal readonly List<string> Skipped = new List<string>();
            internal string Summary => $"Transferred {Copied} values." + (Skipped.Count == 0 ? "" :
                " Kept defaults for unavailable or incompatible fields: " + string.Join(", ", Skipped) + ".");
        }

        internal static Record FindRecord(TextureCompositor document, string behaviourId)
        {
            if (string.IsNullOrEmpty(behaviourId)) return null;
            Record found = null;
            foreach (var missing in UnityEditor.SerializationUtility.GetManagedReferencesWithMissingTypes(document))
            {
                try
                {
                    var data = MissingLayerData.Parse(missing.serializedData);
                    string id = data["recoveryId"]?.Annotation<MissingLayerData.ScalarText>()?.Text ?? (string)data["recoveryId"];
                    if (id != behaviourId) continue;
                    if (found != null) return new Record { Error = "Saved behaviour data is ambiguous. Common settings are safe; select a new behaviour without transferring specific settings." };
                    found = new Record { Data = data };
                }
                catch (Exception e) when (e is FormatException || e is Newtonsoft.Json.JsonException)
                {
                    // An unreadable unrelated missing reference must not affect this layer.
                }
            }
            return found;
        }

        internal static Report Copy(Record record, LayerBehaviour destination, TextureCompositor document)
        {
            var report = new Report();
            if (record == null) return report;
            if (record.Data == null) throw new InvalidOperationException(record.Error);
            CopyFields(record.Data, destination, document, report, "", 0);
            return report;
        }

        private static Dictionary<string, FieldInfo> Fields(Type type)
        {
            var fields = new Dictionary<string, FieldInfo>();
            for (; type != null && type != typeof(object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsStatic && !field.IsInitOnly && !field.IsDefined(typeof(NonSerializedAttribute)) &&
                        (field.IsPublic || field.IsDefined(typeof(SerializeField)) || field.IsDefined(typeof(SerializeReference))))
                        if (!fields.ContainsKey(field.Name)) fields.Add(field.Name, field);
            return fields;
        }

        private static void CopyFields(JObject source, object destination, TextureCompositor document, Report report, string path, int depth)
        {
            var fields = Fields(destination.GetType());
            foreach (var property in source.Properties())
            {
                if (path.Length == 0 && property.Name == "recoveryId") continue;
                string childPath = path.Length == 0 ? property.Name : path + "." + property.Name;
                if (!fields.TryGetValue(property.Name, out var field) ||
                    !TryValue(property.Value, field.FieldType, field.GetValue(destination), document, report, childPath, depth + 1, out object value))
                {
                    report.Skipped.Add(childPath);
                    continue;
                }
                field.SetValue(destination, value);
            }
        }

        private static bool TryValue(JToken token, Type type, object original, TextureCompositor document,
            Report report, string path, int depth, out object value)
        {
            value = original;
            if (depth > 64) return false;
            if (token is JObject unavailable && unavailable["$unavailable"] != null) return false;
            if (type == typeof(string) && token.Annotation<MissingLayerData.ScalarText>() is MissingLayerData.ScalarText scalar)
            { value = scalar.Text; report.Copied++; return true; }
            if (token.Type == JTokenType.Null && !type.IsValueType) { value = null; report.Copied++; return true; }
            if (typeof(Object).IsAssignableFrom(type))
            {
                if (!(token is JObject reference)) return false;
                if (reference["fileID"]?.Type != JTokenType.Integer) return false;
                long fileId = (long)reference["fileID"];
                if (fileId == 0) { value = null; report.Copied++; return true; }
                string guid = reference["guid"]?.Annotation<MissingLayerData.ScalarText>()?.Text ?? (string)reference["guid"];
                string assetPath = string.IsNullOrEmpty(guid) ? AssetDatabase.GetAssetPath(document) : AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) return false;
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                    if (asset != null && type.IsInstanceOfType(asset) &&
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId) && localId == fileId)
                    { value = asset; report.Copied++; return true; }
                return false;
            }
            // A reference to another managed object is not inline field data. Do not steal
            // children from an existing group or fabricate unresolved managed references.
            if (token is JObject managed && managed["rid"] != null) return false;
            if (type == typeof(Gradient))
            {
                if (!(token is JObject gradientData) || !TryGradient(gradientData, out Gradient gradient)) return false;
                value = gradient;
                report.Copied++;
                return true;
            }
            if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!(token is JArray array) || array.Count > 65536) return false;
                Type elementType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                IList list = type.IsArray ? (IList)Array.CreateInstance(elementType, array.Count) : (IList)Activator.CreateInstance(type);
                int before = report.Copied;
                for (int i = 0; i < array.Count; i++)
                {
                    object fallback = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                    if (!TryValue(array[i], elementType, fallback, document, report, path + "[" + i + "]", depth + 1, out object item))
                    { report.Copied = before; return false; }
                    if (type.IsArray) list[i] = item; else list.Add(item);
                }
                value = list;
                return true;
            }
            if (token is JObject map)
            {
                if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsAbstract || Fields(type).Count == 0) return false;
                object nested = original ?? (type.IsValueType || type.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(type) : null);
                if (nested == null) return false;
                CopyFields(map, nested, document, report, path, depth);
                value = nested;
                return true;
            }
            try
            {
                if (type == typeof(string) && token.Type == JTokenType.String) value = (string)token;
                else if (type == typeof(bool) && (token.Type == JTokenType.Boolean || token.Type == JTokenType.Integer && ((long)token == 0 || (long)token == 1)))
                    value = token.Type == JTokenType.Boolean ? (bool)token : (long)token != 0;
                else if (type.IsEnum && token.Type == JTokenType.Integer)
                {
                    object number = Convert.ChangeType(((JValue)token).Value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
                    value = Enum.ToObject(type, number);
                    if (!Enum.IsDefined(type, value)) return false;
                }
                else if ((type == typeof(float) || type == typeof(double)) && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer))
                {
                    double number = (double)token;
                    if (double.IsNaN(number) || double.IsInfinity(number) || type == typeof(float) && Math.Abs(number) > float.MaxValue) return false;
                    value = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                }
                else if (type.IsPrimitive && type != typeof(bool) && type != typeof(char) && token.Type == JTokenType.Integer)
                    value = Convert.ChangeType(((JValue)token).Value, type, CultureInfo.InvariantCulture);
                else return false;
            }
            catch (Exception e) when (e is OverflowException || e is InvalidCastException || e is FormatException) { return false; }
            report.Copied++;
            return true;
        }

        private static bool TryGradient(JObject data, out Gradient gradient)
        {
            gradient = null;
            if (data["m_NumColorKeys"]?.Type != JTokenType.Integer || data["m_NumAlphaKeys"]?.Type != JTokenType.Integer) return false;
            long colorCount = (long)data["m_NumColorKeys"], alphaCount = (long)data["m_NumAlphaKeys"];
            if (colorCount < 1 || colorCount > 8 || alphaCount < 1 || alphaCount > 8) return false;
            var colors = new GradientColorKey[(int)colorCount];
            var alphas = new GradientAlphaKey[(int)alphaCount];
            for (int i = 0; i < Math.Max(colorCount, alphaCount); i++)
            {
                if (!(data["key" + i] is JObject key)) return false;
                var channels = new float[4];
                string[] names = { "r", "g", "b", "a" };
                for (int c = 0; c < 4; c++)
                {
                    var number = key[names[c]];
                    if (number == null || number.Type != JTokenType.Float && number.Type != JTokenType.Integer) return false;
                    double component = (double)number;
                    if (double.IsNaN(component) || double.IsInfinity(component) || Math.Abs(component) > float.MaxValue) return false;
                    channels[c] = (float)component;
                }
                if (i < colorCount)
                {
                    if (!GradientTime(data["ctime" + i], out float time)) return false;
                    colors[i] = new GradientColorKey(new Color(channels[0], channels[1], channels[2], channels[3]), time);
                }
                if (i < alphaCount)
                {
                    if (!GradientTime(data["atime" + i], out float time)) return false;
                    alphas[i] = new GradientAlphaKey(channels[3], time);
                }
            }
            if (data["m_Mode"]?.Type != JTokenType.Integer || !Enum.IsDefined(typeof(GradientMode), (int)data["m_Mode"])) return false;
            gradient = new Gradient { mode = (GradientMode)(int)data["m_Mode"] };
            gradient.SetKeys(colors, alphas);
            if (data["m_ColorSpace"] != null)
            {
                var property = typeof(Gradient).GetProperty("colorSpace");
                if (property == null || !property.CanWrite || data["m_ColorSpace"].Type != JTokenType.Integer ||
                    !Enum.IsDefined(typeof(ColorSpace), (int)data["m_ColorSpace"])) return false;
                property.SetValue(gradient, (ColorSpace)(int)data["m_ColorSpace"]);
            }
            return true;
        }

        private static bool GradientTime(JToken token, out float time)
        {
            time = 0;
            if (token?.Type != JTokenType.Integer || (long)token < 0 || (long)token > 65535) return false;
            time = (long)token / 65535f;
            return true;
        }
    }
}
