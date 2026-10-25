using System;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexUserSettings
    {
        internal static int UndoDiskGiB => PaintUndoDisk.Current.ReadLimits().diskGiB;
        internal static int PaintUndoSteps => PaintUndoDisk.Current.ReadLimits().steps;
        internal static void SetUndoDiskGiB(int value) { PaintUndoDisk.Current.SetLimits(value, null); Changed?.Invoke(); }
        internal static void SetPaintUndoSteps(int value) { PaintUndoDisk.Current.SetLimits(null, value); Changed?.Invoke(); }
        private static double nextPreferencePoll;
        private static string preferenceSignature;
        private static string limitsSignature;
        [InitializeOnLoadMethod]
        private static void SubscribePreferenceRefresh() => EditorApplication.update += RefreshSharedPreferences;
        private static void RefreshSharedPreferences()
        {
            if (EditorApplication.timeSinceStartup < nextPreferencePoll) return;
            nextPreferencePoll = EditorApplication.timeSinceStartup + 2;
            bool limitsChanged = false;
            try
            {
                var limits = PaintUndoDisk.Current.ReadLimits();
                string value = limits.diskGiB + ":" + limits.steps;
                limitsChanged = value != limitsSignature; limitsSignature = value;
            }
            catch (Exception) { /* The settings window and paint reservation report storage errors. */ }
            string signature = string.Join("|", EditorPrefs.GetString(PressureCurveKey, "linear"),
                ColorHistoryLimit,
                EditorPrefs.GetString(LightKey), EditorPrefs.GetString(DarkKey), EditorPrefs.GetString(ErrorKey),
                EditorPrefs.GetString(GuideAlignedKey), EditorPrefs.GetString(GuideAngledKey), EditorPrefs.GetString(GuideActiveKey),
                EditorPrefs.GetString(HealingStrokeColorKey), EditorPrefs.GetString(PostFxBackgroundKey),
                EditorPrefs.GetString(CommentAuthorKey), EditorPrefs.GetString(CommentAuthorColorKey), EditorPrefs.GetString(CommentAuthorIdKey),
                EditorPrefs.GetString(PresetsFolderKey), EditorPrefs.GetString(VsCodeExecutableKey),
                EditorPrefs.GetBool(ShowMantaKey, true), EditorPrefs.GetInt(SizeKey, DefaultCheckerSize),
                EditorPrefs.GetInt(PostFxBackgroundModeKey), EditorPrefs.GetInt(ImageOpenModeKey, 1), EditorPrefs.GetInt(ImageOpenLayerKey),
                EditorPrefs.GetFloat(LayerPickAlphaKey, DefaultLayerPickAlphaThreshold), EditorPrefs.GetFloat(SnapRadiusKey, DefaultSnapRadius));
            if (signature == preferenceSignature) { if (limitsChanged) Changed?.Invoke(); return; }
            preferenceSignature = signature;
            tabletPressureCurve = null;
            checkerLight = Load(LightKey); checkerDark = Load(DarkKey); invalidPixels = Load(ErrorKey);
            guideAlignedColor = Load(GuideAlignedKey); guideAngledColor = Load(GuideAngledKey); guideActiveColor = Load(GuideActiveKey);
            healingStrokeColor = Load(HealingStrokeColorKey, true); postFxBackground = Load(PostFxBackgroundKey);
            showManta = EditorPrefs.GetBool(ShowMantaKey, true);
            checkerSize = Mathf.Clamp(EditorPrefs.GetInt(SizeKey, DefaultCheckerSize), MinimumCheckerSize, MaximumCheckerSize);
            postFxBackgroundMode = NormalizePostFxBackgroundMode((PostFxBackground)EditorPrefs.GetInt(PostFxBackgroundModeKey));
            layerPickAlphaThreshold = NormalizePickAlpha(EditorPrefs.GetFloat(LayerPickAlphaKey, DefaultLayerPickAlphaThreshold));
            snapRadius = NormalizeSnapRadius(EditorPrefs.GetFloat(SnapRadiusKey, DefaultSnapRadius));
            Changed?.Invoke();
        }
    }
}
