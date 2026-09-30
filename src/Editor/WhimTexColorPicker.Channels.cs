using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexColorPicker
    {
        private Func<int> channelSource;
        private Toggle channelControl;
        private int displayedChannels = -2;
        private bool displayedChannelMode;
        private int PickerChannelMask => WhimTexColorChannels.Enabled ? channelSource?.Invoke() ?? -1 : -1;

        internal void SetChannelSource(Func<int> source)
        {
            channelSource = source;
            displayedChannels = -2;
            UpdateColorChannels();
        }
        private void BuildChannelControl(VisualElement toolbar)
        {
            displayedChannels = -2;
            channelControl = new Toggle("Channels") { tooltip = "Preview the originating window's channels. Swatches compare original (upper left) and channel view (lower right). Stored RGBA, numeric fields and the alpha bar remain unchanged." };
            toolbar.Add(channelControl);
            channelControl.RegisterValueChangedCallback(e => { WhimTexColorChannels.Enabled = e.newValue; UpdateColorChannels(); });
            UpdateColorChannels();
        }
        private void UpdateColorChannels()
        {
            int mask = channelSource?.Invoke() ?? -1;
            bool enabled = WhimTexColorChannels.Enabled;
            if (mask == displayedChannels && enabled == displayedChannelMode) return;
            displayedChannels = mask; displayedChannelMode = enabled;
            channelControl?.EnableInClassList("whimtex-picker-hidden", mask < 0);
            channelControl?.SetValueWithoutNotify(enabled);
            hueRing?.MarkDirtyRepaint(); plane?.MarkDirtyRepaint(); before?.MarkDirtyRepaint(); after?.MarkDirtyRepaint();
            foreach (var ramp in ramps) ramp.MarkDirtyRepaint();
            if (history != null) foreach (var chip in history.Children()) chip.MarkDirtyRepaint();
        }
    }
}
