using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class HlslEffectDropTests
{
    static string ExecuteMain()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(TextureCompositorWindow);
        var catalog = type.Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
        var inspect = catalog.GetMethod("InspectDroppedHlsl", BindingFlags.NonPublic | BindingFlags.Static);
        string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/Gain.hlsl";
        var entry = inspect.Invoke(null, new object[] { path });
        UnityBRun.Check(!(entry == null), "Effect not recognized");
        UnityBRun.Check(!(inspect.Invoke(null, new object[] { "Assets/NotAnEffect.png" }) != null), "Unrelated file accepted");
        var window = UnityBRun.Create<TextureCompositorWindow>();
        try
        {
            var document = UnityBRun.Track((TextureCompositor)type.GetField("compositor", flags).GetValue(window));
            var drop = type.GetMethod("ApplyDroppedHlsl", flags);
            drop.Invoke(window, new object[] { entry, null });
            UnityBRun.Check(!(document.layers.Count != 1 || !(document.layers[0].Behaviour is ShaderProcessorLayerBehaviour)), "Processor missing");
            var layer = document.layers[0];
            UnityBRun.Check(!(layer.fx.Count != 1), "Unexpected default FX");
            drop.Invoke(window, new object[] { entry, layer });
            UnityBRun.Check(!(document.layers.Count != 1 || layer.fx.Count != 2 || layer.fx[0] == layer.fx[1]), "Append did not create independent FX");
            Undo.PerformUndo();
            UnityBRun.Check(!(document.layers.Count != 1 || document.layers[0].fx.Count != 1), "Append Undo failed");
            Undo.PerformUndo();
            UnityBRun.Check(!(document.layers.Count != 0), "Processor Undo failed");
            return "";
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
    }
    public static string Main() => UnityBRun.Run("HlslEffectDropSmoke.Main", () => ExecuteMain());
}

