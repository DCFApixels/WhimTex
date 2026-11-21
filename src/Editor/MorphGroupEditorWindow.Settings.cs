using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        private HelpBox unknownMode;
        private Foldout motion;
        private EnumField trajectory;
        private PopupField<MorphMaskBlend?> blending;
        private Toggle preserveGeometry, preserveStationaryDetails;

        private void AddGroupSettings(VisualElement root)
        {
            unknownMode = WhimTexUI.AddHelpBox(root, string.Empty, HelpBoxMessageType.Warning);
            unknownMode.name = "unknownMorphMode";
            motion = new Foldout { name = "morphMotion", text = "Motion", value = false };
            root.Add(motion);
            trajectory = WhimTexUI.ConfigureField(new EnumField("Trajectory", MorphInterpolation.Linear)
                { name = "morphInterpolation" });
            trajectory.RegisterValueChangedCallback(evt =>
            {
                if (!IsRaster || !GroupWritable)
                    trajectory.SetValueWithoutNotify(morph?.interpolation ?? MorphInterpolation.Linear);
                else if ((MorphInterpolation)evt.newValue != morph.interpolation)
                    EditGroup("Change Morph Trajectory", () => morph.interpolation = (MorphInterpolation)evt.newValue);
            });
            motion.Add(trajectory);
            preserveGeometry = AddGroupToggle(motion, "preserveGeometry", "Preserve Geometry",
                m => m.preserveGeometry, (m, value) => m.preserveGeometry = value);
            preserveStationaryDetails = AddGroupToggle(motion, "preserveStationaryDetails", "Preserve Stationary Details",
                m => m.preserveStationaryDetails, (m, value) => m.preserveStationaryDetails = value);

            // Crossfade switches off visibility without overwriting the group's saved mask blend.
            var blends = new List<MorphMaskBlend?> { null, MorphMaskBlend.Original, MorphMaskBlend.SeparateTone, MorphMaskBlend.NormalizeDetail };
            blending = WhimTexUI.ConfigureField(new PopupField<MorphMaskBlend?>("Blending", blends, 1, BlendName, BlendName)
                { name = "morphBlending" });
            blending.RegisterValueChangedCallback(evt =>
            {
                var next = evt.newValue;
                if (!IsRaster || !GroupWritable || morph.classicLiao || !blends.Contains(next))
                    blending.SetValueWithoutNotify(CurrentBlending);
                else if (next != CurrentBlending)
                    EditGroup("Change Morph Blending", () =>
                    {
                        morph.visibilityMask = next.HasValue;
                        if (next.HasValue) morph.maskBlend = next.Value;
                    });
            });
            motion.Add(blending);
            AddMatchingControls(root);
        }

        private MorphMaskBlend? CurrentBlending => morph?.visibilityMask == false ? (MorphMaskBlend?)null :
            morph?.maskBlend ?? MorphMaskBlend.Original;

        private static string BlendName(MorphMaskBlend? mode) => mode switch
        {
            null => "Crossfade",
            MorphMaskBlend.Original => "Visibility",
            MorphMaskBlend.SeparateTone => "Separate Tone",
            MorphMaskBlend.NormalizeDetail => "Normalize Detail",
            _ => $"Unknown ({(int)mode.Value})"
        };

        private Toggle AddGroupToggle(VisualElement parent, string name, string label,
            Func<MorphGroupLayerBehaviour, bool> read, Action<MorphGroupLayerBehaviour, bool> write)
        {
            var toggle = new Toggle { name = name, text = label };
            toggle.RegisterValueChangedCallback(evt =>
            {
                bool current = morph != null && read(morph);
                if (!IsRaster || !GroupWritable) toggle.SetValueWithoutNotify(current);
                else if (evt.newValue != current) EditGroup("Change " + label, () => write(morph, evt.newValue));
            });
            parent.Add(toggle);
            return toggle;
        }

        private void RefreshGroupSettings()
        {
            bool unknown = morph != null && morph.mode is not (MorphMode.Raster or MorphMode.Vector);
            SetVisible(unknownMode, unknown);
            if (unknown) unknownMode.text = $"Unknown morph mode ({(int)morph.mode}). Choose Raster or Vector in Layer Settings.";
            SetVisible(motion, IsRaster);
            motion.contentContainer.SetEnabled(GroupWritable);
            var path = morph?.interpolation ?? MorphInterpolation.Linear;
            trajectory.label = path is MorphInterpolation.Linear or MorphInterpolation.AutomaticCurve ?
                "Trajectory" : $"Trajectory · Unknown ({(int)path})";
            trajectory.SetValueWithoutNotify(path);
            preserveGeometry.SetValueWithoutNotify(morph?.preserveGeometry ?? false);
            preserveStationaryDetails.SetValueWithoutNotify(morph?.preserveStationaryDetails ?? false);
            blending.SetValueWithoutNotify(CurrentBlending);
            blending.SetEnabled(GroupWritable && !morph.classicLiao);
            RefreshMatchingControls();
        }
    }
}
