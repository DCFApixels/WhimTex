using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientWindow
    {
        private VisualElement historyGrid;
        private ScrollView historyScroll;
        private Texture2D historyCursor;
        private TextureCompositor historyDocument;
        private readonly List<Color> historySnapshot = new List<Color>();
        private int historyMask = -2;
        private bool historyHdr;
        private int HistoryChannelMask => WhimTexColorChannels.Enabled ? channelSource?.Invoke() ?? -1 : -1;

        private void BuildColorHistory()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/WhimTexColorPicker.uss");
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            var heading = BuildLibrarySection("History", "gradientColorHistory", HistoryExpandedKey);
            heading.tooltip = "Apply a history color to the selected color key. Opacity keys are unchanged. Drag to reorder; drag outside to remove.";
            heading.AddToClassList("whimtex-gradient-history");
            historyScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            historyScroll.AddToClassList("whimtex-picker-history-scroll");
            historyGrid = new VisualElement(); historyGrid.AddToClassList("whimtex-picker-history");
            historyScroll.Add(historyGrid); heading.Add(historyScroll);
            historyDocument = null; historySnapshot.Clear(); historyMask = -2;
            heading.schedule.Execute(RefreshColorHistory).Every(100);
            RefreshColorHistory(true);
        }

        private void RefreshColorHistory() => RefreshColorHistory(false);
        private void RefreshColorHistory(bool force)
        {
            if (historyGrid == null) return;
            var document = WhimTexColorPicker.DocumentFor(owner);
            var entries = document != null ? document.ColorHistory : null;
            bool hdr = color != null && color.hdr;
            bool rebuild = force || hdr != historyHdr || document != historyDocument || (entries?.Count ?? 0) != historySnapshot.Count;
            if (!rebuild && entries != null)
                for (int i = 0; i < entries.Count; i++) if (!entries[i].Equals(historySnapshot[i])) { rebuild = true; break; }
            historyGrid.SetEnabled(document != null && !alphaTrack && !midpointSelected);
            if (rebuild)
            {
                historyGrid.Clear(); historySnapshot.Clear(); historyDocument = document; historyHdr = hdr;
                if (entries == null || entries.Count == 0)
                    historyGrid.Add(new Label(entries == null ? "No document history for this input." : "No colors in History yet."));
                else for (int i = 0; i < entries.Count; i++)
                {
                    historySnapshot.Add(entries[i]);
                    if (!WhimTexColorPicker.IsHistoryColorVisible(entries[i], hdr)) continue;
                    WhimTexColorPicker.AddHistoryChip(document, i, entries[i], historyGrid, historyScroll,
                        () => HistoryChannelMask, ref historyCursor, () => RefreshColorHistory(true), SelectHistoryColor);
                }
                if (historyGrid.childCount == 0) historyGrid.Add(new Label("No standard colors in History."));
            }
            int mask = HistoryChannelMask;
            if (mask != historyMask)
            {
                historyMask = mask;
                foreach (var chip in historyGrid.Children()) chip.MarkDirtyRepaint();
            }
        }

        private void SelectHistoryColor(int index, Color value)
        {
            var document = WhimTexColorPicker.DocumentFor(owner);
            if (document == null || document != historyDocument || alphaTrack || midpointSelected ||
                !WhimTexColorPicker.IsHistoryColorVisible(value, color != null && color.hdr) ||
                selected < 0 || selected >= colors.Length || index < 0 || index >= document.ColorHistory.Count ||
                !document.ColorHistory[index].Equals(value)) return;
            Edit(() =>
            {
                float time = colors[selected].time;
                intensityPreferences.Remove(time); hdrPreferences.Remove(time);
                StoreDisplayColor(value);
            });
            document.MoveHistoryColor(index, 0);
            RefreshColorHistory(true);
        }
    }
}
