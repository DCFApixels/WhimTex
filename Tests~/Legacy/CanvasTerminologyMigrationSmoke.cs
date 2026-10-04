using System;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class CanvasTerminologyMigrationSmoke
{
    private static void SetState(TextureCompositorWindow window)
    {
        var data = new SerializedObject(window);
        data.FindProperty("canvasChannels").intValue = 5;
        data.FindProperty("canvasDebug").boolValue = true;
        data.FindProperty("canvasGuidesHidden").boolValue = true;
        data.FindProperty("canvasGuidesLocked").boolValue = true;
        data.FindProperty("canvasGuidesSnap").boolValue = false;
        data.FindProperty("tiledCanvas").boolValue = true;
        var guides = data.FindProperty("canvasGuides");
        guides.arraySize = 1;
        var guide = guides.GetArrayElementAtIndex(0);
        guide.FindPropertyRelative("normal").vector2Value = Vector2.right;
        guide.FindPropertyRelative("position").floatValue = 37.5f;
        var preview = data.FindProperty("layerPreviewState");
        preview.FindPropertyRelative("height").floatValue = 193f;
        preview.FindPropertyRelative("collapsed").boolValue = false;
        preview.FindPropertyRelative("channelMask").intValue = 9;
        data.ApplyModifiedPropertiesWithoutUndo();
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
            SetState(window);
            void Verify(TextureCompositorWindow target)
            {
                var data = new SerializedObject(target);
                Check(data.FindProperty("canvasChannels").intValue == 5, "Channel selection retained");
                Check(data.FindProperty("canvasDebug").boolValue, "Diagnostics retained");
                Check(data.FindProperty("canvasGuidesHidden").boolValue, "Hidden guides retained");
                Check(data.FindProperty("canvasGuidesLocked").boolValue, "Guide lock retained independently");
                Check(!data.FindProperty("canvasGuidesSnap").boolValue, "Guide snapping retained");
                Check(data.FindProperty("tiledCanvas").boolValue, "Tiled view retained");
                var guides = data.FindProperty("canvasGuides");
                Check(guides.arraySize == 1, "Guide list retained");
                var guide = guides.GetArrayElementAtIndex(0);
                Check(guide.FindPropertyRelative("normal").vector2Value == Vector2.right, "Guide normal retained");
                Check(guide.FindPropertyRelative("position").floatValue == 37.5f, "Guide position retained");
                var preview = data.FindProperty("layerPreviewState");
                Check(preview.FindPropertyRelative("height").floatValue == 193, "Layer Preview height retained");
                Check(!preview.FindPropertyRelative("collapsed").boolValue, "Layer Preview foldout retained");
                Check(preview.FindPropertyRelative("channelMask").intValue == 9, "Layer Preview channels retained");
            }
            Verify(window);
            string saved = EditorJsonUtility.ToJson(window);
            Check(saved.Contains("\"canvasChannels\"") && !saved.Contains("\"previewChannels\""), "Writes canonical field names");
            copy = ScriptableObject.CreateInstance<TextureCompositorWindow>();
            EditorJsonUtility.FromJsonOverwrite(saved, copy);
            Verify(copy);

            var color = new WhimTexColorField();
            color.UseCanvasChannels = true;
            Check(color.UseCanvasChannels, "Canonical color-field setter");
            color.UseCanvasChannels = false;
            Check(!color.UseCanvasChannels, "Canonical color-field getter");
            return "Passed " + checks + " checks: canonical window fields, nested guides, state roundtrip and canonical color-field API.";
        }
        finally
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            UnityEngine.Object.DestroyImmediate(window);
        }
    }
}
