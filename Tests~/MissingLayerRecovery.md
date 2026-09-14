# Missing layer recovery

No project compilation is initiated by the tests. Recompile manually in the intended Unity Editor first.

- Static checks: `node --test --experimental-test-isolation=none "Tests~/*.test.mjs"`.
- Optional runtime check: execute `MissingLayerRecoverySmoke.cs` through Pipeline `eval_file` in
  the intended Unity project after recompilation, explicitly passing its absolute path through
  `--project-path`. It uses transient objects and does not save assets.

Manual checks on a disposable copy of a document containing missing layer types:

1. Click each missing row, including one inside a group. Check column alignment, selection,
   the Warning HelpBox and absence of the normal four inspector sections. Up/down navigation
   should visit missing rows and skip end-of-group drop markers.
2. Choose the saved record by name if Unity cannot identify the original record automatically.
   Choose Blur for an old Gaussian/Motion layer, inspect the transfer report and replace it.
   Verify name, GUID, stack position, opacity, Transform, Target and shared settings.
   Enum values are copied numerically, not migrated between old/new meanings: check Mode.
3. Check that unsupported fields are reported; choosing another type must retain its defaults
   for those fields. The explicit defaults-only option must not copy an arbitrary record.
4. Undo and redo the replacement. Check the surrounding layers and repeat recovery on the
   next missing row. After save/reopen, verify the recovered layer and asset references.
5. Select a missing row, then change its container through Undo or another window. The old
   inspector must not replace a different row. Already recovered GUIDs must be rejected.
6. Check Remove from both the context menu and footer, with Undo/Redo. Try a replacement
   Drawing Layer, paint on it, then undo/redo and save/reopen to check its owned pixels.

Recovery intentionally does not invent unresolved child layers or managed-reference graphs.
Such fields are listed as unavailable; it never clears all missing references or rewrites asset YAML.
