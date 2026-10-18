using UnityEngine;

namespace DCFApixels.WhimTex
{
    // One native record per pixel operation lets retention remove exactly that operation.
    internal sealed class DrawingPixelUndoRecord : ScriptableObject
    {
        [SerializeField] internal string before, after;
        [SerializeField] internal bool active;
        [System.NonSerialized] internal bool observed;
        private void OnEnable() => observed = active;
    }
}
