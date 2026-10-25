using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed partial class GradientCanvasManipulator
        {
            private bool IsMesh => owner.gradientCanvasLayer.gradientType == GradientLayerBehaviour.GradientType.Mesh;
            private bool meshGesture, meshEdgeGesture;
            private MeshGradient originalMesh;
            private int meshNode, meshHandle = -1, lastMeshSelection = -1;
            private Vector2 meshPointerStart;
            private float meshEdgeWeight;
            private GradientLayerBehaviour MeshLayer => owner.gradientCanvasLayer.GradientLayer;

            private void MeshHit(Vector2 view, out int node, out int direction)
            {
                var data = MeshLayer.mesh; node = -1; direction = -1; float nearest = 100;
                int selectedNode = Mathf.Clamp(MeshLayer.selectedMeshNode, 0, data.NodeCount - 1);
                for (int d = 0; d < 4; d++)
                {
                    int i = data.HandleIndex(selectedNode, d); if (i < 0) continue;
                    float distance = (FreeformView(data.positions[i]) - view).sqrMagnitude;
                    if (distance < Mathf.Min(64, nearest)) { node = selectedNode; direction = d; nearest = distance; }
                }
                if (direction >= 0) return;
                for (int i = 0; i < data.NodeCount; i++)
                {
                    float distance = (FreeformView(data.NodePosition(i)) - view).sqrMagnitude;
                    if (distance <= nearest) { nearest = distance; node = i; }
                }
            }
            private bool MeshEdgeHit(Vector2 view, out int node, out int direction, out int cell, out float time)
            {
                var data = MeshLayer.mesh; float nearest = 36;
                bool found = false; node = -1; direction = -1; cell = 0; time = .5f;
                for (int axis = 0; axis < 2; axis++)
                {
                    int lines = axis == 0 ? data.rows + 1 : data.columns + 1;
                    int cells = axis == 0 ? data.columns : data.rows;
                    for (int line = 0; line < lines; line++) for (int part = 0; part < cells; part++)
                    {
                        Vector2 a = MeshEdgeView(data, axis == 0, line, part, 0);
                        for (int step = 1; step <= 32; step++)
                        {
                            Vector2 b = MeshEdgeView(data, axis == 0, line, part, step / 32f), delta = b - a;
                            float t = Mathf.Clamp01(Vector2.Dot(view - a, delta) / Mathf.Max(1e-9f, delta.sqrMagnitude));
                            float distance = (view - a - delta * t).sqrMagnitude;
                            if (distance < nearest)
                            {
                                nearest = distance; found = true; direction = axis == 0 ? 1 : 3;
                                node = axis == 0 ? line * (data.columns + 1) + part : part * (data.columns + 1) + line;
                                cell = part; time = (step - 1 + t) / 32f;
                            }
                            a = b;
                        }
                    }
                }
                return found && time > .001f && time < .999f;
            }
            private Vector2 MeshEdgeView(MeshGradient data, bool horizontal, int line, int cell, float t)
            {
                int row = horizontal ? Mathf.Min(line, data.rows - 1) : cell;
                int col = horizontal ? cell : Mathf.Min(line, data.columns - 1);
                return FreeformView(data.EvaluatePosition(col, row,
                    horizontal ? t : line == data.columns ? 1 : 0,
                    horizontal ? line == data.rows ? 1 : 0 : t));
            }
            private MouseCursor GetMeshCursor(Vector2 view)
            {
                MeshHit(view, out int node, out _);
                return node >= 0 || MeshEdgeHit(view, out _, out _, out _, out _) ? MouseCursor.Pan : MouseCursor.Arrow;
            }
            private void MeshDown(PointerDownEvent e)
            {
                if (!target.contentRect.Contains(e.localPosition)) return;
                var gradient = MeshLayer; var data = gradient.mesh;
                MeshHit(e.localPosition, out int node, out int direction);
                if (e.button == 1)
                {
                    if (node >= 0) gradient.selectedMeshNode = node;
                    owner.toolkitRefreshRequested = true; owner.gradientCanvasOverlay.MarkDirtyRepaint();
                    int selectedNode = Mathf.Clamp(gradient.selectedMeshNode, 0, data.NodeCount - 1);
                    bool Valid() => owner.IsGradientCanvasEnabled && IsMesh && MeshLayer == gradient && gradient.mesh == data;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Edit Color"), false, () => { if (Valid()) OpenMeshColor(selectedNode); });
                    foreach (var mode in new[] { MeshGradientNodeMode.Smooth, MeshGradientNodeMode.Corner })
                        menu.AddItem(new GUIContent(mode.ToString()), data.NodeMode(selectedNode) == mode, () =>
                        {
                            if (!Valid() || data.NodeMode(selectedNode) == mode) return;
                            var next = data.Clone(); next.SetNodeMode(selectedNode, mode);
                            BeginUndo(); gradient.mesh = next; Commit();
                        });
                    menu.AddSeparator("");
                    if (data.columns < MeshGradient.MaxCells) menu.AddItem(new GUIContent("Add Column"), false, () => { if (Valid()) SplitMesh(true, Mathf.Min(selectedNode % (data.columns + 1), data.columns - 1), .5f); });
                    if (data.rows < MeshGradient.MaxCells) menu.AddItem(new GUIContent("Add Row"), false, () => { if (Valid()) SplitMesh(false, Mathf.Min(selectedNode / (data.columns + 1), data.rows - 1), .5f); });
                    menu.ShowAsContext(); e.StopImmediatePropagation(); return;
                }
                meshEdgeGesture = false;
                if (node < 0)
                {
                    if (!MeshEdgeHit(e.localPosition, out node, out direction, out int cell, out float t))
                    { e.StopImmediatePropagation(); return; }
                    if (e.clickCount == 2)
                    { SplitMesh(direction == 1, cell, t); e.StopImmediatePropagation(); return; }
                    meshEdgeGesture = true; meshEdgeWeight = 3 * t * (1 - t);
                }
                gradient.selectedMeshNode = node; owner.toolkitRefreshRequested = true;
                owner.gradientCanvasOverlay.MarkDirtyRepaint(); target.Focus();
                if (e.clickCount == 2 && direction < 0) { OpenMeshColor(node); e.StopImmediatePropagation(); return; }
                layer = owner.gradientCanvasLayer; selectionLayer = layer; CaptureGeometry();
                originalMesh = data.Clone(); meshPointerStart = FreeformUv(e.localPosition);
                meshNode = node; meshHandle = direction; meshGesture = true; pointer = e.pointerId;
                target.CapturePointer(pointer); owner.UpdateCanvasCursor(e.localPosition, e.altKey); e.StopImmediatePropagation();
            }
            private void MeshMove(PointerMoveEvent e)
            {
                Vector2 delta = FreeformUv(e.localPosition) - meshPointerStart;
                if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y) || delta.sqrMagnitude < 1e-12f && undo < 0) return;
                var next = originalMesh.Clone();
                if (meshEdgeGesture) next.MoveEdge(meshNode, meshHandle, delta / meshEdgeWeight);
                else if (meshHandle < 0) next.SetNodePosition(meshNode, originalMesh.NodePosition(meshNode) + delta);
                else next.SetHandlePosition(meshNode, meshHandle, originalMesh.positions[originalMesh.HandleIndex(meshNode, meshHandle)] + delta);
                BeginUndo(); layer.GradientLayer.mesh = next;
                owner.RequestTransformCanvas(); owner.toolkitRefreshRequested = true;
                owner.gradientCanvasOverlay.MarkDirtyRepaint(); e.StopImmediatePropagation();
            }
            private void SplitMesh(bool column, int cell, float time)
            {
                if (!owner.IsGradientCanvasEnabled || !IsMesh || pointer >= 0) return;
                var gradient = MeshLayer; var next = gradient.mesh.Clone();
                if ((column ? next.columns : next.rows) >= MeshGradient.MaxCells) return;
                if (column) next.SplitColumn(cell, time); else next.SplitRow(cell, time);
                BeginUndo(); gradient.mesh = next;
                gradient.selectedMeshNode = column ? cell + 1 : (cell + 1) * (next.columns + 1);
                Commit();
            }
            private void OpenMeshColor(int index)
            {
                var edited = MeshLayer; var document = owner.activeDocument; var expected = edited.mesh;
                Color initial = expected.NodeColor(index);
                bool Valid() => owner != null && owner.activeDocument == document && owner.gradientCanvasLayer.GradientLayer == edited &&
                    owner.IsGradientCanvasAvailable && edited.gradientType == GradientLayerBehaviour.GradientType.Mesh && edited.mesh == expected &&
                    index < expected.NodeCount && edited.selectedMeshNode == index;
                var picker = WhimTexColorPicker.Open(initial, WhimTexColorInputs.Hdr || initial.maxColorComponent > 1 || initial.r < 0 || initial.g < 0 || initial.b < 0,
                    true, WhimTexColorRange.Switchable, document, value =>
                    {
                        if (!Valid() || expected.NodeColor(index).Equals(value)) return;
                        Undo.RegisterCompleteObjectUndo(document, "Change Mesh Color");
                        var next = expected.Clone(); next.SetNodeColor(index, value); edited.mesh = expected = next;
                        owner.applyingToolkitChange = true;
                        try { owner.CommitModelChange(); } finally { owner.applyingToolkitChange = false; }
                        owner.RequestCanvasRender(true); owner.toolkitRefreshRequested = true; owner.gradientCanvasOverlay?.MarkDirtyRepaint();
                    }, valid: Valid);
                picker.SetChannelSource(() => owner != null && owner.activeDocument == document ? owner.canvasChannels : -1);
            }
            private void ValidateMeshSelection()
            {
                if (!IsMesh) return;
                int current = Mathf.Clamp(MeshLayer.selectedMeshNode, 0, MeshLayer.mesh.NodeCount - 1);
                if (current == lastMeshSelection) return;
                lastMeshSelection = current; owner.gradientCanvasOverlay?.MarkDirtyRepaint();
            }
            private void DrawMeshGradient(Painter2D painter)
            {
                var data = MeshLayer.mesh; int selectedNode = Mathf.Clamp(MeshLayer.selectedMeshNode, 0, data.NodeCount - 1);
                owner.activeDocument.RefreshTransformHierarchy();
                for (int pass = 0; pass < 2; pass++)
                {
                    painter.lineWidth = pass == 0 ? 3 : 1; painter.strokeColor = pass == 0 ? new Color(0, 0, 0, .45f) : new Color(.4f, .85f, 1, .8f);
                    painter.BeginPath();
                    for (int axis = 0; axis < 2; axis++)
                    {
                        int lines = axis == 0 ? data.rows + 1 : data.columns + 1, cells = axis == 0 ? data.columns : data.rows;
                        for (int line = 0; line < lines; line++) for (int cell = 0; cell < cells; cell++)
                        {
                            int origin = axis == 0 ? line * 3 * data.Width + cell * 3 : cell * 3 * data.Width + line * 3;
                            int step = axis == 0 ? 1 : data.Width;
                            painter.MoveTo(FreeformView(data.positions[origin]));
                            painter.BezierCurveTo(FreeformView(data.positions[origin + step]),
                                FreeformView(data.positions[origin + step * 2]), FreeformView(data.positions[origin + step * 3]));
                        }
                    }
                    painter.Stroke();
                }
                Vector2 node = FreeformView(data.NodePosition(selectedNode));
                painter.strokeColor = Color.white; painter.lineWidth = 1;
                for (int d = 0; d < 4; d++)
                {
                    int index = data.HandleIndex(selectedNode, d); if (index < 0) continue;
                    Vector2 p = FreeformView(data.positions[index]); painter.BeginPath(); painter.MoveTo(node); painter.LineTo(p); painter.Stroke(); Square(painter, p);
                }
                for (int i = 0; i < data.NodeCount; i++) if (i != selectedNode) DrawFreeformPoint(painter, FreeformView(data.NodePosition(i)), data.NodeColor(i));
                DrawFreeformPoint(painter, node, data.NodeColor(selectedNode), true);
            }
        }
    }
}
