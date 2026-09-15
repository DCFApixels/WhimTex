using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexGradientBinding
    {
        internal static event Action<Object, string> Changed;
        internal static WhimTexGradient Read(Object target, string path)
        {
            if (target == null) throw new ArgumentException("Gradient owner no longer exists.");
            using var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(path);
            if (property == null || !(property.boxedValue is WhimTexGradient value))
                throw new ArgumentException("Gradient property no longer exists: " + path);
            return JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(value));
        }
        internal static void Write(Object target, string path, WhimTexGradient value)
        {
            if (target == null) return;
            using var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(path);
            if (property == null) throw new ArgumentException("Gradient property no longer exists: " + path);
            property.boxedValue = JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(value));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            if (target is WhimTexGradientSession session) session.changed?.Invoke(value);
            Changed?.Invoke(target, path);
        }
    }
}
