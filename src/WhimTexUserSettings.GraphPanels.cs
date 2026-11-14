using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexUserSettings
    {
        private const string GraphPanelLayoutKey = "DCFApixels.WhimTex.ShaderGraph.PanelLayout";
        internal static ShaderGraphWorkspaceLayout LoadGraphPanelLayout()
        {
            try
            {
                string json = EditorPrefs.GetString(GraphPanelLayoutKey, string.Empty);
                var layout = string.IsNullOrEmpty(json) ? new ShaderGraphWorkspaceLayout() : JsonUtility.FromJson<ShaderGraphWorkspaceLayout>(json);
                layout ??= new ShaderGraphWorkspaceLayout(); layout.Validate(); return layout;
            }
            catch (System.ArgumentException) { return new ShaderGraphWorkspaceLayout(); }
        }
        internal static void SaveGraphPanelLayout(ShaderGraphWorkspaceLayout layout)
        {
            if (layout != null) EditorPrefs.SetString(GraphPanelLayoutKey, JsonUtility.ToJson(layout));
        }
    }
}
