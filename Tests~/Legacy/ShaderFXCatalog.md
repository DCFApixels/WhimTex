# HLSL catalog and FX Transform 2D checks

Run `ShaderFXCatalogSmoke.cs` with Unity Pipeline `eval_file` in the intended editor after recompilation.
It uses transient objects only: no asset saves, imports, scene changes or edits to open documents.
It covers first-line/BOM rules, metadata validation, all parameter types, defaults and ranges,
rectangular/mirrored transform round trips, no-pivot hit testing, GPU live values without shader replacement,
independent clones, serialization, inspector construction and package catalog discovery/application.

Manual interaction checks:

1. Select a populated layer. Add `FX → + Preset → Transform → UV Transform`.
2. Use Edit on Canvas: frame/handles are green; center moves it and no pivot is drawn.
3. Move, rotate, resize, use Shift constraints and Ctrl to disable snapping; test non-square documents.
4. Escape during a drag restores the starting value; Undo/Redo restores the parameter and image.
5. Change layer/document/tool, delete the FX, switch windows and recompile. No stale frame or stuck capture.
6. Add Color/Gain twice. Changing either instance must not change the other. Test HDR/Standard color inputs.
7. Save/reopen a test document: parameter values and IDs survive. Do not use the user's working document for this check.
8. In a test HLSL, edit code/defaults/ranges, move with its meta, remove the marker, inject a syntax error,
   then restore it. Existing values should survive compatible declarations; failed Apply retains the last good shader.
9. Edit a relative include: only dependent live FX reload. Unrelated HLSL edits do not recompile these instances.
10. Lock a layer through the agent API, edit its linked source, then unlock: reload is deferred until unlock.

Unity 6.0 compatibility is an API/source target. A successful run in a newer Editor does not establish
runtime compatibility on 6.0; repeat there before publishing a compatibility claim.

## Opening without false unsaved changes

Run `ShaderFXDocumentDirtySmoke.cs` with Pipeline `run_script`, entry
`ShaderFXDocumentDirtySmoke.Run`, after recompilation. It uses transient model/container
round trips through the document serializer and the production FX preparation path, plus
hidden test windows; it does not write/import assets or change existing windows.

- No FX, inline FX, linked FX, and a linked FX with includes stay clean after restoration.
- Owner dirty count and Undo group do not change just for compilation.
- Render-only notifications refresh previews without marking the document/window dirty.
- Explicit Apply, FX edits, layer edits, changed catalog revisions and missing-source fallback
  still mark changes. Catalog-revision cases alter only a transient saved hash, not preset files.
- Coalescing preserves real edits in either notification order; later refreshes never clear dirty.
- File deserialization establishes an Undo baseline; actual FX Undo callbacks remain detectable.
- Restored GPU output matches the original; existing user-window references/dirty flags remain intact.

The test delivers the queued callback explicitly after attaching the document, so it is deterministic.
It is not a disk-import or physical-click timing test. `DocumentTitle.test.mjs` and
`EmptyDocumentSave.test.mjs` additionally cover the window save-prompt contracts.
