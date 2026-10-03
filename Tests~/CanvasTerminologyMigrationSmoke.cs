using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class CanvasTerminologyMigrationSmoke
{
    private static object LegacyState()
    {
        // Only WhimTex's fields are reflected; no internal Unity APIs.
        var names = new[] { "previewChannels", "previewDebug", "previewGuidesHidden", "previewGuidesLocked",
            "previewGuidesSnap", "tiledPreview", "previewGuides", "inspectorPreviewState" };
        var current = new[] { "canvasChannels", "canvasDebug", "canvasGuidesHidden", "canvasGuidesLocked",
            "canvasGuidesSnap", "tiledCanvas", "canvasGuides", "layerPreviewState" };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("WhimTexLegacyWindowFixture" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("Fixture").DefineType("LegacyWindowState", TypeAttributes.Public | TypeAttributes.Serializable);
        for (int i = 0; i < names.Length; i++)
            builder.DefineField(names[i], typeof(TextureCompositorWindow).GetField(current[i], BindingFlags.Instance | BindingFlags.NonPublic).FieldType, FieldAttributes.Public);
        Type type = builder.CreateType();
        object state = Activator.CreateInstance(type);
        object[] values = { 5, true, true, true, false, true };
        for (int i = 0; i < values.Length; i++) type.GetField(names[i]).SetValue(state, values[i]);
        var guideField = type.GetField("previewGuides");
        var guides = (IList)Activator.CreateInstance(guideField.FieldType);
        var guide = Activator.CreateInstance(guideField.FieldType.GenericTypeArguments[0]);
        guide.GetType().GetField("normal").SetValue(guide, Vector2.right);
        guide.GetType().GetField("position").SetValue(guide, 37.5f);
        guides.Add(guide); guideField.SetValue(state, guides);
        var previewField = type.GetField("inspectorPreviewState");
        var preview = Activator.CreateInstance(previewField.FieldType, true);
        preview.GetType().GetField("height").SetValue(preview, 193f);
        preview.GetType().GetField("collapsed").SetValue(preview, false);
        preview.GetType().GetField("channelMask").SetValue(preview, 9);
        previewField.SetValue(state, preview);
        return state;
    }
    public static string Run()
    {
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        TextureCompositorWindow copy = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        try
        {
            // Exercise Unity's managed-field migration, including FormerlySerializedAs.
            // EditorJsonUtility.FromJsonOverwrite does not resolve old-name aliases.
            EditorUtility.CopySerializedManagedFieldsOnly(LegacyState(), window);
            void Verify(TextureCompositorWindow target)
            {
                var data = new SerializedObject(target);
                Check(data.FindProperty("canvasChannels").intValue == 5, "Channel selection migrated");
                Check(data.FindProperty("canvasDebug").boolValue, "Diagnostics migrated");
                Check(data.FindProperty("canvasGuidesHidden").boolValue, "Hidden guides migrated");
                Check(data.FindProperty("canvasGuidesLocked").boolValue, "Guide lock migrated independently");
                Check(!data.FindProperty("canvasGuidesSnap").boolValue, "Guide snapping migrated");
                Check(data.FindProperty("tiledCanvas").boolValue, "Tiled view migrated");
                var guides = data.FindProperty("canvasGuides");
                Check(guides.arraySize == 1, "Guide list migrated");
                var guide = guides.GetArrayElementAtIndex(0);
                Check(guide.FindPropertyRelative("normal").vector2Value == Vector2.right, "Guide normal retained");
                Check(guide.FindPropertyRelative("position").floatValue == 37.5f, "Guide position retained");
                var preview = data.FindProperty("layerPreviewState");
                Check(preview.FindPropertyRelative("height").floatValue == 193, "Layer Preview height migrated");
                Check(!preview.FindPropertyRelative("collapsed").boolValue, "Layer Preview foldout migrated");
                Check(preview.FindPropertyRelative("channelMask").intValue == 9, "Layer Preview channels migrated");
            }
            Verify(window);
            string saved = EditorJsonUtility.ToJson(window);
            Check(saved.Contains("\"canvasChannels\"") && !saved.Contains("\"previewChannels\""), "Writes canonical field names");
            copy = ScriptableObject.CreateInstance<TextureCompositorWindow>();
            EditorJsonUtility.FromJsonOverwrite(saved, copy);
            Verify(copy);

            var color = new WhimTexColorField();
#pragma warning disable CS0618
            color.UsePreviewChannels = true;
            Check(color.UseCanvasChannels, "Legacy color-field setter forwards");
            color.UseCanvasChannels = false;
            Check(!color.UsePreviewChannels, "Legacy color-field getter forwards");
#pragma warning restore CS0618
            return "Passed " + checks + " checks: old window fields, nested guides, new-name roundtrip and public color-field alias.";
        }
        finally
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            UnityEngine.Object.DestroyImmediate(window);
        }
    }
}
