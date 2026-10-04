# Scoped Undo and tool settings

After manual Unity compilation, run `ScopedUndoSmoke.cs` as an opt-in eval body.
It uses temporary objects and Editor Undo: run only when no edits are in progress.
It restores tool preferences, destroys its fixtures and saves no assets.

Coverage: one notification per changed document, unrelated Undo, texture-only
Undo/Redo without a document change, FX consumers, settings changes inserting no
Undo step, and tool settings surviving document and window Undo snapshots.
Also rerun `ResourceLifecycleSmoke.cs` for deleted layers, groups and embedded FX.

Manual checks:

- Draw a line, change brush size with both the field and bracket keys, swap colors,
  change hardness/spacing and fill options, then Undo. The line should disappear
  immediately; tool settings should remain as configured. Redo restores the line.
- Change tool settings after Undo; Redo must remain available.
- Open two different documents plus Properties windows. Undo an edit in one:
  only its previews/settings should refresh. Repeat with an unrelated Unity edit.
- Verify drawing, fill, clear, transforms, group deletion and repeated Undo/Redo.
- Verify tool preferences survive window close/reopen and script reload.

Only common tool preferences are outside Undo. Drawing-layer repetition, layer
transforms and pixel operations remain document edits with Undo support. Existing
history entries created before this change are not cleared or rewritten.

Undo routing uses deserialization flags and lightweight native-object dirty
counts, never pixel readback. Dirty counts are conservative: a save/import that
resets a tracked texture/material count can cause one extra owner refresh on the
next Undo. It does not trigger a refresh of every compositor.
