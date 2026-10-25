using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed partial class GradientCanvasManipulator
        {
            private int selectedCustomKey;
            private WhimTexGradient customMarkerSource;
            private uint customMarkerRevision;
            private WhimTexGradientCurveKey[] customMarkers = Array.Empty<WhimTexGradientCurveKey>();
            private WhimTexGradientCurveKey[] customKeys = Array.Empty<WhimTexGradientCurveKey>();
            private Vector2[] customMarkerPoints = Array.Empty<Vector2>();
            private readonly List<int> customHitCandidates = new List<int>();
            private int customColorPositionCount;
            private Vector2 customDragOffset;
            private float customAngularGrabAngle;
            private bool customCanRemove;
            private bool IsCustom => GradientCustomMarkers.IsCustom(owner.gradientCanvasLayer.gradient);

            private void RefreshCustomMarkers()
            {
                var gradient = owner.gradientCanvasLayer.gradient;
                if (ReferenceEquals(customMarkerSource, gradient) && customMarkerRevision == gradient.Revision) return;
                customMarkerSource = gradient; customMarkerRevision = gradient.Revision;
                customKeys = gradient.CustomKeys;
                customMarkers = gradient.GetCustomMarkers();
                customColorPositionCount = gradient.ColorKeys.Length;
                if (customMarkerPoints.Length != customMarkers.Length)
                    customMarkerPoints = new Vector2[customMarkers.Length];
            }

            private Vector2 CustomBasePoint(float time, Vector2 a, Vector2 b) => KeyPosition(a, b, time);

            private void RefreshCustomGeometry(Vector2 a, Vector2 b)
            {
                for (int i = 0; i < customMarkers.Length; i++)
                    customMarkerPoints[i] = CustomBasePoint(customMarkers[i].time, a, b);
            }

            private int HitCustomMarker(Vector2 point, bool cycle = false)
            {
                var candidates = customHitCandidates; candidates.Clear(); float nearest = 100;
                for (int i = 0; i < customMarkers.Length; i++)
                {
                    if (IsAngular && !AngularVisible(customMarkers[i].time)) continue;
                    float distance = (point - customMarkerPoints[i]).sqrMagnitude;
                    if (distance > nearest + .1f) continue;
                    if (distance < nearest - .1f) { candidates.Clear(); nearest = distance; }
                    candidates.Add(customMarkers[i].id);
                }
                if (candidates.Count == 0) return -1;
                int current = candidates.IndexOf(selectedCustomKey);
                return current < 0 ? candidates[0] : candidates[cycle ? (current + 1) % candidates.Count : current];
            }

            private bool HitCustomCanvas(Vector2 p, out int hit, out float time, bool cycle = false)
            {
                Points(out var a, out var b);
                RefreshCustomMarkers(); RefreshCustomGeometry(a, b);
                hit = -1;
                time = Projection(p, a, b);
                if (IsAngular)
                {
                    Vector2 first = AngularPoint(0), center = AngularCenterPoint;
                    if (!float.IsFinite(AngularAngle(p))) return false;
                    if ((p - center).sqrMagnitude <= 81) hit = AngularCenter;
                    else if ((p - first).sqrMagnitude <= 121) hit = AngularClosed ? AngularJoined : AngularStart;
                    else if (AngularSpan < 360 && (p - AngularPoint(1)).sqrMagnitude <= 121) hit = AngularEnd;
                }
                else
                {
                    Vector2 offset = Offset(a, b);
                    if ((p - a - offset).sqrMagnitude <= 64) hit = -2;
                    else if ((p - b - offset).sqrMagnitude <= 64) hit = -3;
                }
                if (hit != -1) return true;
                hit = HitCustomMarker(p, cycle);
                if (hit >= 0) return true;
                if (IsAngular)
                {
                    Vector2 center = AngularCenterPoint, ray = AngularPoint(0) - center;
                    float along = Vector2.Dot(p - center, ray) / Mathf.Max(.0001f, ray.sqrMagnitude);
                    if (along > 0 && along < 1 && (p - center - along * ray).sqrMagnitude <= 36)
                    { hit = AngularRotate; return true; }
                }
                Vector2 onLine = CustomBasePoint(time, a, b);
                return (p - onLine).sqrMagnitude <= 36;
            }

            private int CustomEndpointKey(bool first)
            {
                float time = first ? 0 : 1;
                foreach (var marker in customMarkers)
                    if (Mathf.Abs(marker.time - time) < .00001f) return marker.id;
                return 0;
            }

            private bool CustomEndpointSelected(bool first) => selectionLayer == owner.gradientCanvasLayer &&
                owner.gradientCanvasLayer.gradient.TryGetCustomKey(selectedCustomKey, out var key) &&
                Mathf.Abs(key.time - (first ? 0 : 1)) < .00001f;

            private bool CanAddCustomCanvasKey(float time)
            {
                if (!float.IsFinite(time)) return false;
                RefreshCustomMarkers();
                bool existing = false;
                foreach (var key in customKeys)
                    if (key.channel != 3 && Mathf.Abs(key.time - time) < .000001f) { existing = true; break; }
                if (!existing && customColorPositionCount >= 64) return false;
                // Adding on an existing color position supplies any missing channels there.
                return true;
            }

            private void CustomCanvasDown(PointerDownEvent e)
            {
                Vector2 p = e.localPosition;
                if (!HitCustomCanvas(p, out int hit, out float time, e.clickCount == 1))
                { if (e.button == 0) SelectTextCharacter(e); return; }
                bool endpoint = IsAngular && hit <= AngularStart;
                bool firstEndpoint = hit == AngularStart || hit == AngularJoined && AngularFirstHalf(p);
                int keyId = hit > 0 ? hit : endpoint ? CustomEndpointKey(firstEndpoint) : 0;
                if (e.button == 1)
                {
                    if (keyId <= 0) return;
                    selectedCustomKey = keyId; selected = -1; selectionLayer = owner.gradientCanvasLayer;
                    ShowCustomCanvasMenu(keyId); owner.gradientCanvasOverlay.MarkDirtyRepaint();
                    e.StopImmediatePropagation(); return;
                }
                if (e.button != 0) return;
                owner.Focus(); target.Focus(); midpointSelected = false;
                selectionLayer = owner.gradientCanvasLayer; selected = -1;
                var g = selectionLayer.gradient;
                selectedCustomKey = keyId;
                if (e.clickCount == 2 && (hit > 0 || endpoint))
                {
                    if (selectedCustomKey == 0 && !AddCustomCanvasKey(firstEndpoint ? 0 : 1))
                    { e.StopImmediatePropagation(); return; }
                    OpenCustomCanvasCurves(selectedCustomKey);
                    owner.gradientCanvasOverlay.MarkDirtyRepaint(); e.StopImmediatePropagation(); return;
                }
                if (hit == -1)
                {
                    AddCustomCanvasKey(time); e.StopImmediatePropagation(); return;
                }
                layer = selectionLayer; originalGradient = g.Clone(); CaptureGeometry();
                layer.UvEndpoints(out var startUv, out var endUv);
                start = GradientCanvasGeometry.Point(startUv, originalTransform, Size);
                end = GradientCanvasGeometry.Point(endUv, originalTransform, Size);
                Points(out var a, out var b);
                int markerIndex = Array.FindIndex(customMarkers, k => k.id == hit);
                customDragOffset = markerIndex >= 0 ? p - CustomBasePoint(customMarkers[markerIndex].time, a, b) : Vector2.zero;
                customCanRemove = markerIndex >= 0 && GradientCustomMarkers.CanRemove(originalGradient, hit, alpha: false);
                if (IsAngular)
                {
                    originalAngularAngle = layer.circularStartAngle; originalAngularRepetitions = layer.circularRepetitions;
                    lastAngularAngle = AngularAngle(p); angularDelta = 0; angularPointerPosition = p;
                    customAngularGrabAngle = markerIndex < 0 ? 0 : Mathf.DeltaAngle(
                        originalAngularAngle + AngularSpan * customMarkers[markerIndex].time, lastAngularAngle);
                }
                pointerStart = Document(p); handle = hit; pendingRemoval = false; angularGesture = IsAngular;
                pointer = e.pointerId; target.CapturePointer(pointer);
                owner.UpdateCanvasCursor(p, e.altKey); owner.gradientCanvasOverlay.MarkDirtyRepaint(); e.StopImmediatePropagation();
            }

            private bool AddCustomCanvasKey(float time)
            {
                if (!CanAddCustomCanvasKey(time)) return false;
                try
                {
                    var next = owner.gradientCanvasLayer.gradient.Clone();
                    int id = next.AddCustomColorKeys(time);
                    BeginUndo(); selectionLayer = owner.gradientCanvasLayer; selectionLayer.gradient = next;
                    selectedCustomKey = id; selected = -1; Commit(); return true;
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
                { owner.ShowNotification(new GUIContent(e.Message)); return false; }
            }

            private void MoveCustomCanvasKey(Vector2 point)
            {
                Points(out var a, out var b);
                Vector2 projected = IsAngular ? point : point - customDragOffset;
                originalGradient.TryGetCustomKey(handle, out var moving);
                float time = IsAngular ? AngularGradientGeometry.Project(AngularAngle(point) -
                    owner.gradientCanvasLayer.circularStartAngle - customAngularGrabAngle, AngularSpan, moving.time) : Projection(projected, a, b);
                Vector2 onLine = CustomBasePoint(time, a, b);
                float distance = Vector2.Distance(projected, onLine);
                pendingRemoval = customCanRemove && distance > 32;
                if (pendingRemoval) return;
                var next = originalGradient.Clone();
                try { GradientCustomMarkers.Move(next, handle, time, alpha: false); }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { return; }
                layer.gradient = next;
                selectedCustomKey = handle;
            }

            private void OpenCustomCanvasCurves(int id)
            {
                var edited = owner.gradientCanvasLayer; var document = owner.activeDocument;
                bool Valid() => owner != null && owner.activeDocument == document && owner.gradientCanvasLayer == edited &&
                    owner.IsGradientCanvasAvailable && GradientCustomMarkers.IsCustom(edited.gradient) &&
                    !WhimTexApi.IsLayerContentLocked(document, edited.Owner);
                WhimTexGradientCurvesWindow.ShowFor(() => Valid() ? edited.gradient : null, value =>
                {
                    if (!Valid()) return;
                    Undo.RegisterCompleteObjectUndo(document, "Edit Gradient Curves");
                    edited.gradient = value.Clone();
                    owner.applyingToolkitChange = true;
                    try { owner.CommitModelChange(); } finally { owner.applyingToolkitChange = false; }
                    owner.RequestCanvasRender(true); owner.toolkitRefreshRequested = true;
                    owner.gradientCanvasOverlay?.MarkDirtyRepaint();
                }, document, id);
            }

            private void ShowCustomCanvasMenu(int id)
            {
                var edited = owner.gradientCanvasLayer; var document = owner.activeDocument;
                var g = edited.gradient; g.TryGetCustomKey(id, out var anchor);
                var menu = new GenericMenu();
                void Change(Action<WhimTexGradient> change)
                {
                    if (owner == null || owner.activeDocument != document || owner.gradientCanvasLayer != edited ||
                        !owner.IsGradientCanvasAvailable || edited.gradient != g) return;
                    try
                    {
                        var next = g.Clone(); change(next);
                        BeginUndo(); edited.gradient = next; Commit();
                    }
                    catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
                    { owner.ShowNotification(new GUIContent(e.Message)); }
                }
                bool Valid() => owner != null && owner.activeDocument == document && owner.gradientCanvasLayer == edited &&
                    owner.IsGradientCanvasAvailable && edited.gradient == g;
                menu.AddItem(new GUIContent("Edit Curves"), false, () => { if (Valid()) OpenCustomCanvasCurves(id); });
                var group = GradientCustomMarkers.Group(g, id, alpha: false);
                foreach (var key in g.CustomKeys)
                {
                    if (key.id == id || Array.IndexOf(group, key.id) < 0) continue;
                    int otherId = key.id;
                    string name = g.CustomChannelName(key.channel) + " #" + otherId;
                    menu.AddItem(new GUIContent("Select/" + name), false, () =>
                    { if (!Valid()) return; selectedCustomKey = otherId; owner.gradientCanvasOverlay.MarkDirtyRepaint(); OpenCustomCanvasCurves(otherId); });
                }
                if (GradientCustomMarkers.CanRemove(g, id, alpha: false)) menu.AddItem(new GUIContent("Delete Keys"), false,
                    () => Change(next => GradientCustomMarkers.Remove(next, id, alpha: false)));
                else menu.AddDisabledItem(new GUIContent("Delete Keys"));
                menu.ShowAsContext();
            }

            private bool DeleteCustomCanvasKey(KeyDownEvent e)
            {
                var element = e.target as VisualElement;
                if (e.keyCode != KeyCode.Delete || pointer >= 0 || selectionLayer != owner.gradientCanvasLayer ||
                    selectedCustomKey <= 0 || element != target && (element == null || !target.Contains(element))) return false;
                if (GradientCustomMarkers.CanRemove(selectionLayer.gradient, selectedCustomKey, alpha: false))
                {
                    var next = selectionLayer.gradient.Clone(); GradientCustomMarkers.Remove(next, selectedCustomKey, alpha: false);
                    BeginUndo(); selectionLayer.gradient = next; selectedCustomKey = 0; Commit();
                }
                e.StopImmediatePropagation(); return true;
            }

            private void DrawCustomCanvas(Painter2D p, Vector2 a, Vector2 b)
            {
                RefreshCustomMarkers(); RefreshCustomGeometry(a, b);
                var g = owner.gradientCanvasLayer.gradient;
                var selectedGroup = GradientCustomMarkers.Group(g, selectedCustomKey, alpha: false);
                for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < customMarkers.Length; i++)
                {
                    var key = customMarkers[i];
                    if (IsAngular && (key.time <= .00001f || key.time >= .99999f || !AngularVisible(key.time))) continue;
                    bool selectedKey = selectionLayer == owner.gradientCanvasLayer && Array.IndexOf(selectedGroup, key.id) >= 0;
                    if (selectedKey != (pass == 1) || selectedKey && pendingRemoval) continue;
                    AngularDisc(p, customMarkerPoints[i], GradientCustomMarkers.Swatch(g, key), selectedKey);
                }
            }
        }
    }
}
