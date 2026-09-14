using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXCodeField : TextField
    {
        private const string UndoName = "Edit Shader FX Code";
        private const double TypingPause = 0.8;
        private enum EditKind { Insert, Backspace, Delete, Atomic }

        private sealed class SelectionStep
        {
            internal int beforeCursor, beforeSelect, beforeLength;
            internal int afterCursor, afterSelect, afterLength;
        }

        private sealed class CaretHistory
        {
            internal readonly Dictionary<int, SelectionStep> steps = new Dictionary<int, SelectionStep>();
            internal readonly Queue<int> order = new Queue<int>();

            internal void Add(int group, SelectionStep step)
            {
                if (!steps.ContainsKey(group))
                    order.Enqueue(group);
                steps[group] = step;
                while (order.Count > 128)
                    steps.Remove(order.Dequeue());
            }
        }

        private static readonly ConditionalWeakTable<ShaderFX, CaretHistory> Carets = new ConditionalWeakTable<ShaderFX, CaretHistory>();
        private readonly ShaderFX effect;
        private readonly CaretHistory history;
        private int editGroup = -1;
        private EditKind editKind;
        private KeyCode inputKey;
        private bool atomicInput;
        private double lastEditTime;
        private int beforeCursor, beforeSelect, beforeLength;
        private SelectionStep activeStep;

        internal ShaderFXCodeField(ShaderFX effect)
        {
            this.effect = effect;
            history = Carets.GetValue(effect, _ => new CaretHistory());
            name = "shaderFXCode";
            multiline = true;
            verticalScrollerVisibility = ScrollerVisibility.Auto;
            textSelection.selectAllOnFocus = false;
            textSelection.selectAllOnMouseUp = false;
            AddToClassList("whimtex-shader-fx-code");
            SetValueWithoutNotify(effect.Code ?? string.Empty);
            CaptureBefore();
            RegisterCallback<ChangeEvent<string>>(OnChanged);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<ValidateCommandEvent>(OnValidateCommand, TrickleDown.TrickleDown);
            RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand, TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(_ => EndEditGroup(), TrickleDown.TrickleDown);
            RegisterCallback<FocusInEvent>(_ => CaptureBefore());
            RegisterCallback<FocusOutEvent>(_ => EndEditGroup());
            RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target != this)
                    return;
                Undo.undoRedoEvent += OnUndoRedo;
                SyncFromModel();
            });
            RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target != this)
                    return;
                EndEditGroup();
                Undo.undoRedoEvent -= OnUndoRedo;
            });
        }

        internal void SyncFromModel()
        {
            if (effect == null || value == effect.Code)
                return;
            string previous = value ?? string.Empty;
            string next = effect.Code ?? string.Empty;
            int cursor = 0;
            while (cursor < previous.Length && cursor < next.Length && previous[cursor] == next[cursor])
                cursor++;
            editGroup = -1;
            activeStep = null;
            SetValueWithoutNotify(next);
            textSelection.SelectRange(cursor, cursor);
            CaptureBefore();
        }

        private void CaptureBefore()
        {
            beforeLength = (value ?? string.Empty).Length;
            beforeCursor = Mathf.Clamp(textSelection.cursorIndex, 0, beforeLength);
            beforeSelect = Mathf.Clamp(textSelection.selectIndex, 0, beforeLength);
        }

        private void OnChanged(ChangeEvent<string> evt)
        {
            if (evt.target != this || effect == null || evt.newValue == effect.Code)
                return;
            if (evt.previousValue != effect.Code)
            {
                SyncFromModel();
                return;
            }
            string previous = evt.previousValue ?? string.Empty;
            string next = evt.newValue ?? string.Empty;
            if (beforeLength != previous.Length)
                beforeCursor = beforeSelect = Mathf.Min(textSelection.cursorIndex, previous.Length);
            EditKind kind = EditKind.Atomic;
            if (!atomicInput && beforeCursor == beforeSelect)
            {
                if (next.Length == previous.Length + 1 && inputKey != KeyCode.Return && inputKey != KeyCode.KeypadEnter)
                    kind = EditKind.Insert;
                else if (next.Length == previous.Length - 1)
                    kind = inputKey == KeyCode.Backspace ? EditKind.Backspace : EditKind.Delete;
            }
            double now = EditorApplication.timeSinceStartup;
            bool merge = activeStep != null && editGroup == Undo.GetCurrentGroup() && kind != EditKind.Atomic &&
                kind == editKind && now - lastEditTime < TypingPause &&
                beforeCursor == activeStep.afterCursor && beforeSelect == activeStep.afterSelect;
            if (!merge)
            {
                EndEditGroup();
                Undo.IncrementCurrentGroup();
                editGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                activeStep = new SelectionStep
                {
                    beforeCursor = beforeCursor, beforeSelect = beforeSelect, beforeLength = previous.Length
                };
                history.Add(editGroup, activeStep);
            }
            Undo.RecordObject(effect, UndoName);
            effect.SetDraftCode(next);
            Undo.FlushUndoRecordObjects();
            editKind = kind;
            lastEditTime = now;
            SelectionStep step = activeStep;
            step.afterLength = next.Length;
            step.afterCursor = step.afterSelect = Mathf.Clamp(inputKey == KeyCode.Delete && beforeCursor == beforeSelect ? beforeCursor :
                Mathf.Min(beforeCursor, beforeSelect) + next.Length - previous.Length + Mathf.Abs(beforeCursor - beforeSelect), 0, next.Length);
            schedule.Execute(() =>
            {
                if (value != next || step != activeStep)
                    return;
                step.afterCursor = Mathf.Clamp(textSelection.cursorIndex, 0, next.Length);
                step.afterSelect = Mathf.Clamp(textSelection.selectIndex, 0, next.Length);
            });
            if (kind == EditKind.Atomic)
                EndEditGroup();
            atomicInput = false;
        }

        private void EndEditGroup()
        {
            if (editGroup >= 0 && editGroup == Undo.GetCurrentGroup())
            {
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }
            editGroup = -1;
            activeStep = null;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            bool action = evt.ctrlKey || evt.commandKey;
            bool undo = action && !evt.altKey && evt.keyCode == KeyCode.Z && !evt.shiftKey;
            bool redo = action && !evt.altKey &&
                ((evt.keyCode == KeyCode.Z && evt.shiftKey) || (evt.keyCode == KeyCode.Y && !evt.shiftKey));
            if (undo || redo)
            {
                WhimTexUI.ConsumeEvent(evt);
                PerformHistory(redo);
                return;
            }
            if (action || evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow ||
                evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.Home ||
                evt.keyCode == KeyCode.End || evt.keyCode == KeyCode.PageUp || evt.keyCode == KeyCode.PageDown)
                EndEditGroup();
            inputKey = evt.keyCode;
            atomicInput = action;
            CaptureBefore();
        }

        private void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (evt.commandName != "Undo" && evt.commandName != "Redo")
                return;
            WhimTexUI.ConsumeEvent(evt);
        }

        private void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if (evt.commandName == "Undo" || evt.commandName == "Redo")
            {
                WhimTexUI.ConsumeEvent(evt);
                PerformHistory(evt.commandName == "Redo");
                return;
            }
            EndEditGroup();
            inputKey = KeyCode.None;
            atomicInput = true;
            CaptureBefore();
        }

        private void PerformHistory(bool redo)
        {
            EndEditGroup();
            Undo.FlushUndoRecordObjects();
            if (redo)
                Undo.PerformRedo();
            else
                Undo.PerformUndo();
        }

        private void OnUndoRedo(in UndoRedoInfo info)
        {
            if (effect == null)
                return;
            editGroup = -1;
            activeStep = null;
            SyncFromModel();
            if (info.undoName == UndoName && history.steps.TryGetValue(info.undoGroup, out SelectionStep step) &&
                value.Length == (info.isRedo ? step.afterLength : step.beforeLength))
            {
                int length = value.Length;
                int cursor = info.isRedo ? step.afterCursor : step.beforeCursor;
                int selection = info.isRedo ? step.afterSelect : step.beforeSelect;
                textSelection.SelectRange(Mathf.Clamp(cursor, 0, length), Mathf.Clamp(selection, 0, length));
                CaptureBefore();
            }
        }
    }
}
