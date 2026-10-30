using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class LayerGroupButtonTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string field) => value.GetType().GetField(field, F).GetValue(value);
    static void Write(object value, string field, object data) => value.GetType().GetField(field, F).SetValue(value, data);
    static object Call(object value, string method, params object[] args) =>
        Array.Find(value.GetType().GetMethods(F), m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(value, args);

    public static string Run() => TestContext.Run("Folder click creates an empty group; drop grouping retains hierarchy, placement and Undo", t =>
    {
        WhimTexWindow window = null;
        WhimTexDocument document = null;
        try
        {
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            document = (WhimTexDocument)Read(window, "activeDocument");
            t.Equal(0, document.layers.Count, "Owned unshown window starts with an empty document");
            ((IDisposable)Read(window, "toolPreferences")).Dispose();
            Write(window, "toolPreferences", null);
            document.width = document.height = 64;
            void Normalize() => Call(document, "NormalizeModel");
            Layer Selected() => (Layer)Call(window, "GetSelectedLayer");
            void Click()
            {
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                Call(window, "AddGroupForSelection");
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            }
            var footer = new VisualElement(); Write(window, "toolkitLayerFooter", footer);
            Call(window, "BuildToolkitLayerFooter");
            Call(Read(window, "toolkitSettingsBindings"), "Refresh", true);
            t.True(footer.Q<Button>("whimTexAddGroup").enabledInHierarchy, "Folder remains enabled without a selection");
            Layer a = new ColorFillLayerBehaviour { layerName = "A" };
            Layer b = new ColorFillLayerBehaviour { layerName = "B" };
            Layer c = new ColorFillLayerBehaviour { layerName = "C" };
            document.layers.AddRange(new[] { a, b, c }); Normalize();
            string aId = a.Id, bId = b.Id, cId = c.Id;
            Call(window, "SelectContextLayers", new List<Layer> { a, b }, b.Id);
            Click();
            var empty = Selected(); string emptyId = empty.Id;
            t.True(empty.Behaviour is GroupLayerBehaviour && empty.children.Count == 0, "Click creates an empty ordinary group");
            t.True(document.layers.Count == 4 && document.layers[0] == a && document.layers[1] == empty &&
                document.layers[2] == b && document.layers[3] == c, "Empty group is before active row; multiselection stays outside");
            t.True(((Dictionary<string, bool>)Read(window, "groupExpansion"))[emptyId], "Created group is expanded");
            Undo.PerformUndo(); Normalize();
            t.True(document.layers.Count == 3 && document.layers[0].Id == aId && document.layers[1].Id == bId && document.layers[2].Id == cId,
                "Undo removes only the new empty group");
            Undo.PerformRedo(); Normalize();
            t.True(document.layers.Count == 4 && document.layers[1].Id == emptyId && document.layers[1].children.Count == 0,
                "Redo restores the same empty group");
            Call(window, "SelectOnlyLayer", (object)null); Click();
            t.True(document.layers[0] == Selected() && Selected().children.Count == 0, "No selection inserts at the root top");

            Undo.ClearUndo(document);
            document.layers.Clear();
            Layer parent = new GroupLayerBehaviour { layerName = "Parent" };
            Layer child = new ColorFillLayerBehaviour { layerName = "Child" };
            parent.children.Add(child); document.layers.Add(parent); Normalize();
            Call(window, "SelectOnlyLayer", child.Id); Click();
            t.True(document.layers.Count == 1 && parent.children.Count == 2 && parent.children[0] == Selected() && parent.children[1] == child,
                "Nested selection inserts a sibling group without moving the child");
            t.Equal(0, Selected().children.Count, "Nested inserted group is empty");

            Undo.ClearUndo(document);
            parent.children.Clear(); parent.children.Add(child);
            Layer other = new ColorFillLayerBehaviour { layerName = "Other" }; document.layers.Add(other);
            var transform = parent.transform; transform.position = new Vector2(.2f, .7f); transform.rotation = 27;
            transform.scale = new Vector2(.7f, 1.2f); parent.transform = transform;
            Normalize();
            var before = (TextureTransform)Call(document, "GetCanvasTransform", child);
            var otherBefore = (TextureTransform)Call(document, "GetCanvasTransform", other);
            string parentId = parent.Id, childId = child.Id, otherId = other.Id;
            Call(window, "SelectOnlyLayer", (object)null);
            Call(window, "GroupLayers", new List<Layer> { child, other });
            var grouped = Selected(); string groupedId = grouped.Id;
            t.True(grouped.Behaviour is GroupLayerBehaviour && grouped.children.Count == 2 && grouped.children[0] == child && grouped.children[1] == other,
                "Drop handler groups the supplied layers even without an active selection");
            t.True(document.layers.Count == 2 && parent.children.Count == 0, "Dropped layers are moved, not copied, without an extra empty group");
            foreach (Vector2 point in new[] { Vector2.zero, Vector2.one, new Vector2(.3f, .6f) })
            {
                foreach (var pair in new[] { (child, before), (other, otherBefore) })
                {
                    var after = (TextureTransform)Call(document, "GetCanvasTransform", pair.Item1);
                    var expected = pair.Item2.Map(point, new Vector2(64, 64)); var actual = after.Map(point, new Vector2(64, 64));
                    t.Near(expected.x, actual.x, .0001, "Grouping preserves canvas X");
                    t.Near(expected.y, actual.y, .0001, "Grouping preserves canvas Y");
                }
            }
            Undo.PerformUndo(); Normalize();
            t.True(document.layers.Count == 2 && document.layers[0].Id == parentId && document.layers[0].children.Count == 1 &&
                document.layers[0].children[0].Id == childId && document.layers[1].Id == otherId, "Undo restores the original nested hierarchy");
            Undo.PerformRedo(); Normalize();
            t.True(((Layer)Call(document, "FindLayer", groupedId))?.children.Count == 2, "Redo restores drop grouping");
        }
        finally
        {
            Undo.FlushUndoRecordObjects();
            if (document != null) Undo.ClearUndo(document);
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            if (document != null) UnityEngine.Object.DestroyImmediate(document);
            Undo.IncrementCurrentGroup();
        }
    });
}
