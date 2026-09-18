using System;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        [Serializable]
        private sealed class OutputSettingsState
        {
            public OutputSettings settings;
            public FilterMode filter = FilterMode.Bilinear;
        }

        [SerializeField, HideInInspector] private string savedOutputSettings;

        internal string CaptureOutputSettings() => JsonUtility.ToJson(new OutputSettingsState
        { settings = SpriteOutputSettings, filter = outputFilter });

        internal void EnsureOutputSettingsBaseline()
        {
            if (string.IsNullOrEmpty(savedOutputSettings)) savedOutputSettings = CaptureOutputSettings();
        }

        internal bool HasOutputSettingsChanges => CaptureOutputSettings() != savedOutputSettings;

        internal void RevertOutputSettings()
        {
            EnsureOutputSettingsBaseline();
            var state = JsonUtility.FromJson<OutputSettingsState>(savedOutputSettings);
            if (state?.settings == null) throw new InvalidOperationException("Saved output settings are unavailable.");
            Undo.RecordObject(this, "Revert Output Settings");
            outputSettings = state.settings;
            outputFilter = state.filter;
            MarkChanged();
        }

        internal sealed class OutputSettingsError : InvalidOperationException
        {
            internal readonly string[] Fields;
            internal OutputSettingsError(string message, params string[] fields) : base(message) => Fields = fields;
        }

        internal string ValidateOutputSettings(out string[] fields)
        {
            fields = Array.Empty<string>();
            try
            {
                GetLinkedTexturePath(SpriteOutputSettings.linkedTextureGuid);
                SpriteOutputSettings.Validate(width, height);
                if (!Enum.IsDefined(typeof(FilterMode), outputFilter))
                    throw new OutputSettingsError("Choose a valid texture filter.", "outputFilter");
                if (SpriteOutputSettings.outputType == OutputType.Sprite && SpriteOutputSettings.spriteMode == OutputSpriteMode.Multiple)
                    ValidateSpriteSlices(GetSpriteSlices(), width, height);
                return null;
            }
            catch (OutputSettingsError error) { fields = error.Fields; return error.Message; }
            catch (System.IO.IOException error) { fields = new[] { "linkedTextureGuid" }; return error.Message; }
            catch (UnauthorizedAccessException error) { fields = new[] { "linkedTextureGuid" }; return error.Message; }
            catch (InvalidOperationException error) { fields = new[] { "spriteMode" }; return error.Message; }
        }
    }
}
