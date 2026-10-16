using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace WhimTex.Tests
{
    // Pipeline's ephemeral compiler sees multiple assemblies with Newtonsoft types.
    // Resolve the canonical public API without taking an ambiguous compile reference.
    public sealed class ApiJson : IEnumerable<ApiJson>
    {
        static readonly Assembly Assembly = Assembly.Load("Newtonsoft.Json");
        static readonly Type ObjectType = Assembly.GetType("Newtonsoft.Json.Linq.JObject", true);
        static readonly Type TokenType = Assembly.GetType("Newtonsoft.Json.Linq.JToken", true);
        readonly object token;
        public ApiJson() { token = Activator.CreateInstance(ObjectType); }
        ApiJson(object value) { token = value; }
        public static ApiJson Parse(string json) => new ApiJson(ObjectType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json }));
        public static ApiJson FromObject(object value) => value is ApiJson json ? json :
            new ApiJson(TokenType.GetMethod("FromObject", new[] { typeof(object) }).Invoke(null, new[] { value }));
        public ApiJson this[string name]
        {
            get { object value = token.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(token, new object[] { name }); return value == null ? null : new ApiJson(value); }
            set => token.GetType().GetProperty("Item", new[] { typeof(string) }).SetValue(token, value.token, new object[] { name });
        }
        public ApiJson this[int index] => this.ElementAt(index);
        public string Type => token.GetType().GetProperty("Type").GetValue(token).ToString();
        public int Count => (int)token.GetType().GetProperty("Count").GetValue(token);
        public override string ToString() => token.ToString();
        public static implicit operator ApiJson(string value) => FromObject(value);
        public static implicit operator ApiJson(int value) => FromObject(value);
        public static implicit operator ApiJson(bool value) => FromObject(value);
        public static explicit operator string(ApiJson value) => value == null || value.Type == "Null" ? null : value.ToString();
        public static explicit operator int(ApiJson value) => int.Parse(value.ToString(), CultureInfo.InvariantCulture);
        public static explicit operator bool(ApiJson value) => bool.Parse(value.ToString());
        public IEnumerator<ApiJson> GetEnumerator() { foreach (object value in (IEnumerable)token) yield return new ApiJson(value); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
