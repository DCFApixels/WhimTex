using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal enum ShaderGraphColorMode { None, Category, Heatmap, Precision, UserDefined }

    internal static partial class WhimTexUserSettings
    {
        private const string GraphColorModeKey = "DCFApixels.WhimTex.ShaderGraph.ColorMode";
        internal static ShaderGraphColorMode GraphColorMode
        {
            get
            {
                int value = EditorPrefs.GetInt(GraphColorModeKey, 0);
                return value >= 0 && value <= (int)ShaderGraphColorMode.UserDefined ? (ShaderGraphColorMode)value : ShaderGraphColorMode.None;
            }
            set
            {
                if (GraphColorMode == value) return;
                EditorPrefs.SetInt(GraphColorModeKey, (int)value);
                Changed?.Invoke();
            }
        }
    }
}
