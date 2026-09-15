using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed class BrushPresetAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] internal BrushPresetLibrary.Preset preset;
    }
}
