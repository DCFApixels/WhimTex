using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Independent port: complete original body, assertion inputs and finally cleanup retained.
public static class LayerBehaviourSwapTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("LayerBehaviourSwapTests", Body);
    static void Body()
    {
        // Pipeline eval_file after manual compilation. Transient document, balanced own Undo group.
        WhimTex.Tests.UnityC.FixtureContext.Context.True(!(GUIUtility.hotControl != 0), "Finish the current UI gesture before running Undo checks.");
        const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var doc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>());
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 8;
        Texture2D owned = null;
        int checks = 0;
        void Check(bool value, string label) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, label); checks++; }
        object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Hidden).Invoke(obj, args);
        try
        {
            var paint = new DCFApixels.WhimTex.DrawingLayerBehaviour { brushColor = Color.red, brushSize = 8, brushHardness = 1 };
            var layer = new DCFApixels.WhimTex.Layer(paint) { layerName = "Keep identity" };
            doc.layers.Add(layer);
            Call(doc, "NormalizeModel");
            var stroke = Call(paint, "GetStrokeParameters", false);
            Call(paint, "PaintPoint", new Vector2(.5f, .5f), 8, 8, stroke);
            // Structural Undo records the completed stroke, not an unfinished GPU-only snapshot.
            Call(paint, "SyncSurfaceToTexture");
            owned = (Texture2D)typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetField("pixels", Hidden).GetValue(paint);
            Check(owned != null && owned.GetPixel(4,4).r > .99f, "Fixture has real painted pixels");
            string id = layer.Id;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(doc, "Verify behaviour swap");
            layer.SetBehaviour(new DCFApixels.WhimTex.NoiseLayerBehaviour { seed = 137 });
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Check(owned != null && owned.GetPixel(4,4).r > .99f, "Detaching Drawing does not destroy owned pixels");
            Check(layer.Id == id && layer.layerName == "Keep identity", "Swap retains common identity");
            Undo.PerformUndo();
            var restored = doc.layers[0].Behaviour as DCFApixels.WhimTex.DrawingLayerBehaviour;
            Check(restored != null && restored.GetPreviewTexture(8).GetPixel(4,4).r > .99f, "Undo restores readable Drawing pixels");
            Undo.PerformRedo();
            Check(doc.layers[0].Behaviour is DCFApixels.WhimTex.NoiseLayerBehaviour noise && noise.seed == 137, "Redo restores replacement");
            Check(owned != null, "Redo does not delete a texture still owned by Undo");
            doc.layers[0].SetBehaviour(paint);
            Check(paint.GetPreviewTexture(8).GetPixel(4,4).r > .99f, "Detached behaviour can be reattached");
            return;
        }
        finally
        {
            Undo.ClearUndo(doc);
            if (owned != null) Undo.ClearUndo(owned);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);
            if (owned != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(owned);
        }
        
    }
}
