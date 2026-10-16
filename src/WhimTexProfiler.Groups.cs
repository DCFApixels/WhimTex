using System;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexProfiler
    {
        [Flags]
        internal enum Group
        {
            None = 0, UI = 1, Editing = 2, Rendering = 4, Resources = 8, Storage = 16, Diagnostics = 32,
            All = UI | Editing | Rendering | Resources | Storage | Diagnostics
        }

        internal static readonly Group[] Groups = { Group.UI, Group.Editing, Group.Rendering,
            Group.Resources, Group.Storage, Group.Diagnostics };

        internal static string GroupId(Group group) => group switch
        {
            Group.UI => "ui", Group.Editing => "editing", Group.Rendering => "rendering",
            Group.Resources => "resources", Group.Storage => "storage", _ => "diagnostics"
        };

        internal static string GroupName(Group group) => group switch
        {
            Group.UI => "UI & Input", Group.Editing => "Tools & Editing", Group.Rendering => "Rendering",
            Group.Resources => "Resources & Memory", Group.Storage => "Load & Save", _ => "Diagnostics"
        };

        internal static Group GroupFor(string phase, string kind)
        {
            phase ??= "";
            bool Starts(string prefix) => phase.StartsWith(prefix, StringComparison.Ordinal);
            if (kind == "error") return Group.Diagnostics;
            if (kind == "cache" || Starts("cache.") || Starts("resource.") || Starts("memory.") ||
                Starts("transfer.") || Starts("effect.cache") || Starts("sdf.cache") || Starts("sdf.geometry.cache") ||
                phase == "input.readback" || phase == "document.compose_readback" || phase == "output.upload" ||
                phase == "reconstruction.match" || phase == "reconstruction.copy") return Group.Resources;
            if (kind == "change" || Starts("tool.") || Starts("edit.") || Starts("brush.") ||
                Starts("selection.") || Starts("undo.") || Starts("redo.")) return Group.Editing;
            if (Starts("ui.") || Starts("input.") || Starts("handle.")) return Group.UI;
            if (Starts("storage.") || Starts("load.") || Starts("save.") || Starts("import.") ||
                Starts("export.") || Starts("serialize.")) return Group.Storage;
            if (kind == "counter") return Group.Diagnostics;
            if (Starts("render.") || Starts("layer.") || Starts("vector.") || Starts("fx.") || Starts("sdf.") ||
                Starts("outline.") || Starts("field.") || Starts("contour.") || Starts("geometry.") ||
                Starts("distance.") || Starts("reconstruction.") || Starts("mtsdf.") || Starts("rbf.") ||
                Starts("source.") || Starts("output.") || phase == "document.canvas_update" ||
                phase == "document.render" || phase == "document.layer_and_below" || phase == "document.layer_preview")
                return Group.Rendering;
            return Group.Diagnostics;
        }
    }
}
