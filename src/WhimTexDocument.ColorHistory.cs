using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [SerializeField, HideInInspector] private List<Color> colorHistory = new List<Color>();
        internal IReadOnlyList<Color> ColorHistory => colorHistory ??= new List<Color>();

        internal void RememberColor(Color color)
        {
            if (!WhimTexColorPicker.IsFinite(color)) return;
            colorHistory ??= new List<Color>();
            int existing = colorHistory.IndexOf(color);
            if (existing >= 0) { MoveHistoryColor(existing, 0); return; }
            Undo.RecordObject(this, "Remember Color");
            colorHistory.Insert(0, color);
            MarkChanged();
        }

        internal void MoveHistoryColor(int from, int to)
        {
            if (colorHistory == null || from < 0 || to < 0 || from >= colorHistory.Count || to >= colorHistory.Count || from == to) return;
            Undo.RecordObject(this, "Reorder Color History");
            Color color = colorHistory[from];
            colorHistory.RemoveAt(from); colorHistory.Insert(to, color);
            MarkChanged();
        }

        internal void RemoveHistoryColor(int index)
        {
            if (colorHistory == null || index < 0 || index >= colorHistory.Count) return;
            Undo.RecordObject(this, "Remove History Color");
            colorHistory.RemoveAt(index);
            MarkChanged();
        }
    }
}
