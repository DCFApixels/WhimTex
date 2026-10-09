// Pipeline run_script entry DocumentJsonSchema.Run. Regenerates the shared-format schema from model fields.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class DocumentJsonSchema
{
    static Dictionary<string, object> D(params (string key, object value)[] fields)
    { var d = new Dictionary<string, object>(); foreach (var p in fields) d[p.key] = p.value; return d; }
    public static string Run()
    {
        var assembly = typeof(WhimTexDocument).Assembly;
        var fields = typeof(WhimTexDocumentJson).GetMethod("Fields", BindingFlags.NonPublic | BindingFlags.Static);
        var definitions = new Dictionary<string, object>();
        var asset = D(("type", "object"), ("additionalProperties", false), ("required", new[] { "$asset" }),
            ("properties", D(("$asset", D(("type", "object"), ("additionalProperties", false),
                ("properties", D(("guid", D(("type", "string"))), ("path", D(("type", "string"))),
                    ("localId", D(("type", "string"))), ("type", D(("type", "string"))))), ("required", new[] { "guid", "path", "localId", "type" }))))));
        var reference = D(("type", "object"), ("additionalProperties", false), ("required", new[] { "$ref" }), ("properties", D(("$ref", D(("type", "string"))))));
        object Number(Type type) => type == typeof(float) ? D(("type", "number"), ("minimum", -float.MaxValue), ("maximum", float.MaxValue)) :
            type == typeof(int) ? D(("type", "integer"), ("minimum", int.MinValue), ("maximum", int.MaxValue)) :
            type == typeof(byte) ? D(("type", "integer"), ("minimum", 0), ("maximum", 255)) :
            D(("type", type == typeof(double) || type == typeof(decimal) ? "number" : "integer"));
        object Tuple(int count, Type component = null, int? minimum = null) => D(("type", "array"), ("minItems", minimum ?? count), ("maxItems", count), ("items", Number(component ?? typeof(float))));
        string Name(Type t) => t.FullName.Substring("DCFApixels.WhimTex.".Length);
        object Schema(Type type)
        {
            if (type.IsEnum) return D(("type", "string"), ("enum", Enum.GetNames(type)));
            if (type == typeof(bool)) return D(("type", "boolean"));
            if (type == typeof(string) || type == typeof(char)) return D(("type", new[] { "string", "null" }));
            if (type.IsPrimitive || type == typeof(decimal)) return Number(type);
            if (type == typeof(Vector2Int)) return Tuple(2, typeof(int));
            if (type == typeof(Vector3Int)) return Tuple(3, typeof(int));
            if (type == typeof(RectInt)) return Tuple(4, typeof(int));
            if (type == typeof(Color32)) return Tuple(4, typeof(byte));
            if (type == typeof(Vector2)) return Tuple(2);
            if (type == typeof(Vector3)) return Tuple(3, minimum: 2);
            if (type == typeof(Vector4)) return Tuple(4, minimum: 2);
            if (type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect)) return Tuple(4);
            if (type == typeof(Bounds)) return Tuple(6);
            if (type == typeof(AnimationCurve))
            {
                object tangent = D(("anyOf", new[] { Number(typeof(float)), D(("enum", new[] { "Infinity", "-Infinity" })) }));
                object key = D(("type", "array"), ("minItems", 7), ("maxItems", 7),
                    ("prefixItems", new object[] { Number(typeof(float)), Number(typeof(float)), tangent, tangent,
                        Number(typeof(float)), Number(typeof(float)), Schema(typeof(WeightedMode)) }));
                return D(("type", new[] { "object", "null" }), ("additionalProperties", false),
                    ("required", new[] { "preWrap", "postWrap", "keys" }), ("properties", D(
                        ("preWrap", Schema(typeof(WrapMode))), ("postWrap", Schema(typeof(WrapMode))),
                        ("keys", D(("type", "array"), ("maxItems", 65536), ("items", key))))));
            }
            if (typeof(IList).IsAssignableFrom(type)) return D(("type", new[] { "array", "null" }),
                ("items", Schema(type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0])));
            if (typeof(UnityEngine.Object).IsAssignableFrom(type) && type != typeof(WhimTexDocument) && type != typeof(ShaderFX))
            {
                var variants = new List<object> { D(("type", "null")), asset };
                if (type.IsAssignableFrom(typeof(ShaderFX))) { variants.Add(Schema(typeof(ShaderFX))); variants.Add(reference); }
                return D(("anyOf", variants));
            }
            if (type.IsAbstract && typeof(LayerBehaviour).IsAssignableFrom(type))
                return D(("anyOf", assembly.GetTypes().Where(t => !t.IsAbstract && type.IsAssignableFrom(t)).Select(t =>
                    D(("allOf", new[] { Schema(t), D(("type", "object"), ("required", new[] { "$type" })) }))).ToArray()));
            if (type.Assembly != assembly) throw new Exception("Unsupported schema type: " + type);
            string name = Name(type);
            if (!definitions.ContainsKey(name))
            {
                var properties = new Dictionary<string, object>();
                definitions[name] = D(("type", "object"), ("additionalProperties", false), ("properties", properties));
                if (type == typeof(Layer)) ((Dictionary<string, object>)definitions[name])["required"] = new[] { "id", "behaviour" };
                properties["$type"] = D(("const", name));
                if (type == typeof(ShaderFX))
                { properties["$id"] = D(("type", "string")); properties["$name"] = D(("type", "string")); }
                if (type == typeof(DrawingLayerBehaviour)) properties["contentOmitted"] = D(("const", true));
                foreach (FieldInfo f in (FieldInfo[])fields.Invoke(null, new object[] { type }))
                    if (!(type == typeof(WhimTexDocument) && f.Name == "layers") && !(type == typeof(DrawingLayerBehaviour) && f.Name == "pixels"))
                        properties[f.Name] = type == typeof(ShapeLayerBehaviour) && (f.Name == "rectangleCorners" || f.Name == "polygonCorners")
                            ? D(("type", "array"), ("minItems", f.Name == "rectangleCorners" ? 4 : 3), ("maxItems", f.Name == "rectangleCorners" ? 4 : 32), ("items", Schema(typeof(ShapeLayerBehaviour.Corner)))) :
                            type == typeof(ShapeLayerBehaviour.Corner) && f.Name == "amount" ? D(("type", "number"), ("minimum", 0), ("maximum", 1)) :
                            type == typeof(WhimTexDocument) && (f.Name == "width" || f.Name == "height")
                            ? D(("type", "integer"), ("minimum", 1), ("maximum", 16384)) :
                            type == typeof(Layer) && f.Name == "id" ? D(("type", "string"), ("minLength", 1)) : Schema(f.FieldType);
            }
            object modelRef = D(("$ref", "#/$defs/" + name));
            return !type.IsValueType && type != typeof(WhimTexDocument) && type != typeof(Layer)
                ? D(("anyOf", new[] { modelRef, D(("type", "null")) })) : modelRef;
        }
        object document = Schema(typeof(WhimTexDocument)), layers = D(("type", "array"), ("maxItems", 1024), ("items", Schema(typeof(Layer))));
        var schema = D(("$schema", "https://json-schema.org/draft/2020-12/schema"), ("title", "WhimTex document v" + WhimTexDocumentJson.Version),
            ("type", "object"), ("additionalProperties", false), ("required", new[] { "format", "version", "layers" }),
            ("properties", D(("format", D(("const", WhimTexDocumentJson.Format))), ("version", D(("const", WhimTexDocumentJson.Version))),
                ("writeMode", Schema(typeof(WhimTexJsonWriteMode))),
                ("document", document), ("layers", layers))), ("$defs", definitions));
        var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var formatting = Type.GetType("Newtonsoft.Json.Formatting, Newtonsoft.Json", true);
        string text = (string)json.GetMethod("SerializeObject", new[] { typeof(object), formatting }).Invoke(null, new[] { schema, Enum.ToObject(formatting, 1) });
        File.WriteAllText("Packages/com.dcfapixels.whimtex/Documentation~/AI/document.schema.json", text + "\n");
        return "Generated shared JSON schema: " + definitions.Count + " model types.";
    }
}
