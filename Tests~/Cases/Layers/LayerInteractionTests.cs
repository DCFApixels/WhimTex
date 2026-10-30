using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class LayerInteractionTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object obj, string name) => obj.GetType().GetField(name, F).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, F).SetValue(obj, value);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("Layer interaction gestures", () =>
    {
        var t = WhimTex.Tests.UnityC.FixtureContext.Context;
        var scope = WhimTex.Tests.UnityC.FixtureContext.Scope;
        var doc = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        var window = scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
        doc.hideFlags = window.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 16;
        Set(window, "activeDocument", doc);
        Set(window, "toolkitSettingsScroll", new ScrollView());
        Set(window, "toolkitLayerFooter", new VisualElement());
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        try
        {
            for (int i = 0; i < 70; i++) doc.layers.Add(new ColorFillLayerBehaviour { layerName = "Layer " + i });
            Call(doc, "NormalizeModel");
            Call(window, "BuildToolkitSettings");
            foreach (var layer in doc.layers) Call(window, "CreateLayerVisibilityButton", layer);
            var buttons = (IList)Get(window, "layerVisibilityButtons");
            var drag = Get(window, "layerVisibilityDrag");
            string active = doc.layers[5].Id;
            Call(window, "SelectOnlyLayer", active);

            // Drive the package-owned gesture state without showing a window or moving the OS pointer.
            void Begin(bool value) { ((Action)Get(drag, "begin"))(); Set(drag, "value", value); }
            void Pass(params int[] indices)
            {
                bool changed = false;
                foreach (int index in indices) changed |= (bool)Call(drag, "Apply", index);
                if (changed) ((Action)Get(drag, "changed"))();
            }
            void End() { ((Action)Get(drag, "end"))(); Undo.FlushUndoRecordObjects(); }
            bool Enabled(int i) => doc.layers[i].enabled;
            Begin(false); Pass(0, 1, 2, 63, 69); Pass(2, 1, 0); End();
            t.True(!Enabled(0) && !Enabled(1) && !Enabled(2) && !Enabled(63) && !Enabled(69), "Sweep covers more than 64 layers and return pass does not invert");
            t.True(Enabled(3) && Enabled(68), "Unvisited layers stay unchanged");
            t.Equal(active, (string)Get(window, "selectedLayerId"), "Visibility gesture preserves layer selection");
            Undo.PerformUndo(); Call(doc, "NormalizeModel");
            t.True(Enabled(0) && Enabled(1) && Enabled(2) && Enabled(63) && Enabled(69), "One Undo restores the whole gesture");
            Undo.PerformRedo(); Call(doc, "NormalizeModel");
            t.True(!Enabled(0) && !Enabled(1) && !Enabled(2) && !Enabled(63) && !Enabled(69), "One Redo restores the whole gesture");

            buttons.Clear(); foreach (var layer in doc.layers) Call(window, "CreateLayerVisibilityButton", layer);
            Begin(true); Pass(0, 1, 3, 63); End();
            t.True(Enabled(0) && Enabled(1) && Enabled(3) && Enabled(63) && !Enabled(2), "Mixed states receive the first button's target value");
            Undo.PerformUndo(); Call(doc, "NormalizeModel");
            t.True(!Enabled(0) && !Enabled(1) && Enabled(3) && !Enabled(63), "Separate gesture is a separate Undo operation");

            var crosses = drag.GetType().GetMethod("Crosses", F);
            bool Cross(Rect rect, Vector2 a, Vector2 b) => (bool)crosses.Invoke(null, new object[] { rect, a, b });
            t.True(Cross(new Rect(0, 20, 20, 20), new Vector2(10, 0), new Vector2(10, 100)), "Fast sweep crosses intermediate eye rectangles");
            t.True(!Cross(new Rect(0, 20, 20, 20), new Vector2(30, 0), new Vector2(30, 100)), "Movement outside eye column changes nothing");
            var row = new VisualElement(); var eye = (Button)buttons[0]; row.Add(eye);
            t.True(!(bool)Call(window, "IsLayerDragArea", row, eye), "Eye no longer starts layer reordering");

            var selected = doc.layers[3]; Call(window, "SelectOnlyLayer", selected.Id);
            Set(window, "lastPickedLayer", selected); Set(window, "lastLayerPickDocument", doc);
            Set(window, "lastLayerPickPosition", new Vector2(100, 100)); Set(window, "lastLayerPickInsideGroups", false);
            object Continue(Vector2 p, bool deep = false, bool shift = false) => Call(window, "GetLayerPickContinuation", p, deep, shift);
            t.True(Continue(new Vector2(102, 101)) == selected, "Repeated click tolerates small pointer movement");
            t.True(Continue(new Vector2(140, 100)) == null, "Click in a new location restarts at the top");
            t.True(Continue(new Vector2(100, 100), true) == null && Continue(new Vector2(100, 100), false, true) == null,
                "Changed group modifier and Shift selection restart the cycle");
            Call(window, "SelectOnlyLayer", doc.layers[4].Id);
            t.True(Continue(new Vector2(100, 100)) == null, "Selection changed elsewhere resets the cycle");
        }
        finally
        {
            Call(window, "EndLayerVisibilityDrag");
            Undo.FlushUndoRecordObjects(); Undo.ClearUndo(doc); Undo.IncrementCurrentGroup();
        }
    });
}
