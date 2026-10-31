using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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
        private struct CodeContext
        {
            internal int lastToken, opener;
            internal bool canIndent, canClose;
        }

        private sealed class SelectionStep
        {
            internal int beforeCursor, beforeSelect, beforeLength;
            internal int afterCursor, afterSelect, afterLength;
        }

        private sealed class CaretHistory
        {
            internal readonly Dictionary<int, SelectionStep> steps = new Dictionary<int, SelectionStep>();
            internal readonly Queue<int> order = new Queue<int>();
            internal string source;
            internal int cursor, select;

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
        private static readonly Regex Definition = new Regex(@"\b\w+\s+ApplyFX\s*\(([^()]*)\)(?=\s*(?::[^;{}]+)?\{)", RegexOptions.CultureInvariant);
        private static readonly Regex Argument = new Regex(@"\b(float4|float2|PinnedUV)\s+(\w+)\s*$", RegexOptions.CultureInvariant);
        private readonly ShaderFX effect;
        private readonly CaretHistory history;
        private readonly ShaderFXCodeSyntax syntax;
#if UNITY_6000_7_OR_NEWER
        private readonly ShaderFXCodeLineNumbers lineNumbers;
#endif
        private int editGroup = -1;
        private EditKind editKind;
        private KeyCode inputKey;
        private bool atomicInput;
        private bool suppressEnterCharacter;
        private double lastEditTime;
        private int beforeCursor, beforeSelect, beforeLength;
        private SelectionStep activeStep;

        internal ShaderFXCodeField(ShaderFX effect = null)
        {
            this.effect = effect;
            name = "shaderFXCode";
            multiline = true;
            isReadOnly = effect == null;
            verticalScrollerVisibility = ScrollerVisibility.Auto;
            textSelection.selectAllOnFocus = false;
            textSelection.selectAllOnMouseUp = false;
            AddToClassList("whimtex-shader-fx-code");
            SetValueWithoutNotify(effect?.Code ?? string.Empty);
            syntax = new ShaderFXCodeSyntax(this, effect);
#if UNITY_6000_7_OR_NEWER
            lineNumbers = new ShaderFXCodeLineNumbers(this, effect);
#endif
            RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target != this) return;
                var scroll = this.Q<ScrollView>();
                if (scroll == null) return;
                scroll.mode = ScrollViewMode.VerticalAndHorizontal;
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            });
            if (effect == null) return;
            history = Carets.GetValue(effect, _ => new CaretHistory());
            if (history.source == value)
                textSelection.SelectRange(Mathf.Clamp(history.cursor, 0, value.Length), Mathf.Clamp(history.select, 0, value.Length));
            CaptureBefore();
            RegisterCallback<ChangeEvent<string>>(OnChanged);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<KeyUpEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) suppressEnterCharacter = false;
            }, TrickleDown.TrickleDown);
            RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (CanUseReference && (evt.direction == NavigationMoveEvent.Direction.Next || evt.direction == NavigationMoveEvent.Direction.Previous))
                    WhimTexUI.ConsumeEvent(evt);
            }, TrickleDown.TrickleDown);
            RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (CanUseReference) WhimTexUI.ConsumeEvent(evt);
            }, TrickleDown.TrickleDown);
            RegisterCallback<ValidateCommandEvent>(OnValidateCommand, TrickleDown.TrickleDown);
            RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand, TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(_ => EndEditGroup(), TrickleDown.TrickleDown);
            RegisterCallback<FocusInEvent>(_ => CaptureBefore());
            RegisterCallback<FocusOutEvent>(_ => { suppressEnterCharacter = false; RememberSelection(true); EndEditGroup(); }, TrickleDown.TrickleDown);
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

        public override void SetValueWithoutNotify(string newValue)
        {
            base.SetValueWithoutNotify(newValue);
#if UNITY_6000_7_OR_NEWER
            lineNumbers?.Refresh(newValue);
#endif
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
            RememberSelection(true);
        }

        // Reference labels must not replace the last code selection with their own focus state.
        internal void RememberSelection(bool force = false)
        {
            if (history == null) return;
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (!force && panel != null && focused != this && (focused == null || !Contains(focused)))
                return;
            history.source = value ?? string.Empty;
            history.cursor = Mathf.Clamp(textSelection.cursorIndex, 0, history.source.Length);
            history.select = Mathf.Clamp(textSelection.selectIndex, 0, history.source.Length);
        }

        internal bool CanUseReference => effect != null && enabledInHierarchy && !isReadOnly && !effect.IsCatalogLinked &&
            !WhimTexApi.IsShaderFXContentLocked(effect);

        internal bool UseReference(string snippet)
        {
            if (!CanUseReference || string.IsNullOrEmpty(snippet))
                return false;
            SyncFromModel();
            RememberSelection();
            string previous = value ?? string.Empty;
            int cursor = history.source == previous ? history.cursor : 0;
            int select = history.source == previous ? history.select : cursor;
            int originalCursor = cursor, originalSelect = select;
            int start = Mathf.Min(cursor, select), length = Mathf.Abs(cursor - select);
            string replacement = snippet;
            bool signature = snippet.StartsWith("float4 ApplyFX(", StringComparison.Ordinal);
            if (signature)
            {
                var definitions = Definition.Matches(ShaderFXEntryPoint.Clean(previous));
                if (definitions.Count > 1)
                    return false;
                if (definitions.Count == 1)
                {
                    var definition = definitions[0];
                    string uvName = "uv", colorName = "color";
                    foreach (string argument in definition.Groups[1].Value.Split(','))
                    {
                        var match = Argument.Match(argument);
                        if (!match.Success) continue;
                        if (match.Groups[1].Value == "float4") colorName = match.Groups[2].Value;
                        else uvName = match.Groups[2].Value;
                    }
                    replacement = snippet.Contains("PinnedUV") ? "float4 ApplyFX(PinnedUV " + uvName + ", float4 " + colorName + ")" :
                        "float4 ApplyFX(float4 " + colorName + ")";
                    start = definition.Index;
                    length = definition.Length;
                    cursor = MapSelection(cursor, start, length, replacement.Length);
                    select = MapSelection(select, start, length, replacement.Length);
                }
                else signature = false;
            }
            string next = previous.Remove(start, length).Insert(start, replacement);
            if (!signature) cursor = select = start + replacement.Length;
            CommitEdit(previous, next, originalCursor, originalSelect, cursor, select);
            return true;
        }

        private void Indent(bool outdent)
        {
            SyncFromModel();
            string previous = value ?? string.Empty;
            int cursor = Mathf.Clamp(textSelection.cursorIndex, 0, previous.Length);
            int select = Mathf.Clamp(textSelection.selectIndex, 0, previous.Length);
            int start = Mathf.Min(cursor, select), end = Mathf.Max(cursor, select);
            int firstLine = LineStart(previous, start);
            if (!outdent && start == end)
            {
                int column = 0;
                for (int i = firstLine; i < start; i++)
                    column += previous[i] == '\t' ? ShaderFX.CodeIndent.Length - column % ShaderFX.CodeIndent.Length : 1;
                string spaces = ShaderFX.CodeIndent.Substring(column % ShaderFX.CodeIndent.Length);
                CommitEdit(previous, previous.Insert(start, spaces), cursor, select, start + spaces.Length, start + spaces.Length);
                return;
            }
            int lastLine = LineStart(previous, end > start ? end - 1 : end);
            var result = new StringBuilder(previous.Length);
            int consumed = 0, delta = 0, afterCursor = cursor, afterSelect = select;
            for (int line = firstLine; line <= lastLine;)
            {
                int remove = 0;
                string insert = outdent ? string.Empty : ShaderFX.CodeIndent;
                if (outdent)
                {
                    if (line < previous.Length && previous[line] == '\t') remove = 1;
                    else while (remove < ShaderFX.CodeIndent.Length && line + remove < previous.Length && previous[line + remove] == ' ')
                        remove++;
                }
                result.Append(previous, consumed, line - consumed).Append(insert);
                consumed = line + remove;
                afterCursor = MapSelection(afterCursor, line + delta, remove, insert.Length);
                afterSelect = MapSelection(afterSelect, line + delta, remove, insert.Length);
                delta += insert.Length - remove;
                int newline = previous.IndexOf('\n', line);
                if (newline < 0) break;
                line = newline + 1;
            }
            result.Append(previous, consumed, previous.Length - consumed);
            CommitEdit(previous, result.ToString(), cursor, select, afterCursor, afterSelect);
        }

        private static int LineStart(string text, int position) => position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

        private static int IndentEnd(string text, int line)
        {
            while (line < text.Length && (text[line] == ' ' || text[line] == '\t')) line++;
            return line;
        }

        private static string LineIndent(string text, int position)
        {
            int line = LineStart(text, position);
            return text.Substring(line, IndentEnd(text, line) - line);
        }

        private static CodeContext ContextBefore(string text, int end)
        {
            var braces = new Stack<int>();
            var context = new CodeContext { lastToken = -1, opener = -1 };
            bool lineComment = false, blockComment = false;
            char quote = '\0';
            for (int i = 0; i < end; i++)
            {
                char current = text[i], next = i + 1 < end ? text[i + 1] : '\0';
                if (current == '\n') { context.lastToken = -1; lineComment = false; }
                if (lineComment) continue;
                if (blockComment)
                {
                    if (current == '*' && next == '/') { blockComment = false; i++; }
                    continue;
                }
                if (quote != '\0')
                {
                    if (current == '\\' && next != '\0') { if (next == '\n') context.lastToken = -1; i++; }
                    else if (current == quote) { quote = '\0'; context.lastToken = i; }
                    continue;
                }
                if (current == '/' && next == '/') { lineComment = true; i++; continue; }
                if (current == '/' && next == '*') { blockComment = true; i++; continue; }
                if (char.IsWhiteSpace(current)) continue;
                context.lastToken = i;
                if (current == '"' || current == '\'') quote = current;
                else if (current == '{') braces.Push(i);
                else if (current == '}' && braces.Count > 0) braces.Pop();
            }
            if (braces.Count > 0) context.opener = braces.Peek();
            context.canIndent = !blockComment && quote == '\0';
            context.canClose = context.canIndent && !lineComment;
            return context;
        }

        private void InsertNewline()
        {
            SyncFromModel();
            string previous = value ?? string.Empty;
            int cursor = Mathf.Clamp(textSelection.cursorIndex, 0, previous.Length);
            int select = Mathf.Clamp(textSelection.selectIndex, 0, previous.Length);
            int start = Mathf.Min(cursor, select), end = Mathf.Max(cursor, select);
            int line = LineStart(previous, start);
            string indent = previous.Substring(line, Mathf.Min(start, IndentEnd(previous, line)) - line);
            int newlineAt = previous.IndexOf('\n', line);
            if (newlineAt < 0 && line > 0) newlineAt = previous.LastIndexOf('\n', line - 1);
            string newline = newlineAt > 0 && previous[newlineAt - 1] == '\r' ? "\r\n" : "\n";
            var context = ContextBefore(previous, start);
            bool opens = context.canIndent && context.lastToken >= line && previous[context.lastToken] == '{';
            string innerIndent = opens ? indent + ShaderFX.CodeIndent : indent;
            int right = IndentEnd(previous, end);
            bool closes = context.canClose && right < previous.Length && previous[right] == '}' && context.opener >= 0;
            string insertion = newline + innerIndent;
            int after = start + insertion.Length;
            if (closes)
            {
                string closingIndent = LineIndent(previous, context.opener);
                insertion = opens ? insertion + newline + closingIndent : newline + closingIndent;
                if (!opens) after = start + insertion.Length;
                end = right;
            }
            string next = previous.Remove(start, end - start).Insert(start, insertion);
            CommitEdit(previous, next, cursor, select, after, after);
        }

        private void InsertClosingBrace()
        {
            SyncFromModel();
            string previous = value ?? string.Empty;
            int cursor = Mathf.Clamp(textSelection.cursorIndex, 0, previous.Length);
            int select = Mathf.Clamp(textSelection.selectIndex, 0, previous.Length);
            int start = Mathf.Min(cursor, select), end = Mathf.Max(cursor, select);
            int line = LineStart(previous, start);
            string insertion = "}";
            if (start <= IndentEnd(previous, line))
            {
                var context = ContextBefore(previous, start);
                if (context.canClose)
                {
                    string indent = previous.Substring(line, start - line);
                    int remove = 0;
                    if (indent.StartsWith("\t", StringComparison.Ordinal)) remove = 1;
                    else while (remove < ShaderFX.CodeIndent.Length && remove < indent.Length && indent[remove] == ' ') remove++;
                    insertion = (context.opener >= 0 ? LineIndent(previous, context.opener) : indent.Substring(remove)) + insertion;
                    start = line;
                }
            }
            string next = previous.Remove(start, end - start).Insert(start, insertion);
            int after = start + insertion.Length;
            CommitEdit(previous, next, cursor, select, after, after);
        }

        private void CommitEdit(string previous, string next, int originalCursor, int originalSelect, int cursor, int select)
        {
            using var timing = WhimTexProfiler.Measure("ui.code.edit", effect != null ? effect.EmbeddedOwner : null, detail: "HLSL");
            EndEditGroup();
            if (next != previous)
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                history.Add(group, new SelectionStep
                {
                    beforeCursor = originalCursor, beforeSelect = originalSelect, beforeLength = previous.Length,
                    afterCursor = cursor, afterSelect = select, afterLength = next.Length
                });
                Undo.RecordObject(effect, UndoName);
                effect.SetDraftCode(next);
                SetValueWithoutNotify(next);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                using (var change = ChangeEvent<string>.GetPooled(previous, next))
                {
                    change.target = this;
                    SendEvent(change);
                }
            }
            Focus();
            textSelection.SelectRange(cursor, select);
            CaptureBefore();
            RememberSelection(true);
        }

        private static int MapSelection(int position, int start, int length, int replacementLength) =>
            position <= start ? position : position >= start + length ? position + replacementLength - length :
            start + Mathf.Min(position - start, replacementLength);

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
            using var timing = WhimTexProfiler.Measure("ui.code.change", effect.EmbeddedOwner, detail: "HLSL");
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
            bool enter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
            bool newlineCharacter = evt.keyCode == KeyCode.None && (evt.character == '\r' || evt.character == '\n');
            if (CanUseReference && !action && !evt.altKey && (enter || newlineCharacter))
            {
                WhimTexUI.ConsumeEvent(evt);
                if (enter)
                {
                    suppressEnterCharacter = true;
                    InsertNewline();
                }
                else if (!suppressEnterCharacter) InsertNewline();
                return;
            }
            if (evt.keyCode != KeyCode.None || evt.character != '\0') suppressEnterCharacter = false;
            if (CanUseReference && evt.character == '}' && !evt.commandKey && (!evt.ctrlKey || evt.altKey))
            {
                WhimTexUI.ConsumeEvent(evt);
                InsertClosingBrace();
                return;
            }
            if (evt.keyCode == KeyCode.Tab && !action && !evt.altKey && CanUseReference)
            {
                WhimTexUI.ConsumeEvent(evt);
                Indent(evt.shiftKey);
                return;
            }
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
                RememberSelection(true);
            }
        }
    }
}
