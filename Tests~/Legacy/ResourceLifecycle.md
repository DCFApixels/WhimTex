# Resource lifecycle regression checks

Run only after manually compiling the package in Unity. The opt-in
`ResourceLifecycleSmoke.cs` eval script checks temporary Drawing deletion through
both UI paths, nested groups, repeated Undo/Redo, shared embedded FX, owned shader
restoration, external FX preservation and the window's unsaved-state flag.
It creates temporary objects and touches Editor Undo; do not run during an edit.

`DrawingReload.test.mjs` checks the reload/destruction split and extracted lifecycle
logic with fake graphics services. `DrawingReloadSmoke.cs`, opt-in after manual
compilation, calls Disable/Enable on temporary documents and checks native texture
identity, nested groups, Standard/HDR pixels and final destruction. Neither test
triggers a domain reload or validates an unfinished GPU stroke.

The following checks require interaction and are not covered by that script:

1. Draw in a new unsaved document. Close its tab, then repeat with a floating
   window. Cancel the close prompt: the document and pixels must remain.
2. Choose Save, then cancel the Save As file dialog. The editor window must stay
   open with its unsaved-state indicator. Retry with a valid path: verify the
   document can be reopened with its pixels and layers.
3. Choose Discard when closing an unsaved document. Reopen WhimTex: it must
   start with a fresh document. Closing an untouched new document must not prompt.
4. Start another unsaved document and try New or switching to an existing asset.
   Cancel must retain the current document. Save As cancellation must do the same.
5. Repeat single-layer and group deletion/Undo/Redo in a saved fixture document.
   Save and reopen it after Undo; the recovered pixels must still be present.
6. In a saved fixture, remove the last reference to an embedded Shader FX, then
   Undo/Redo/Undo. Verify its code, parameters and applied shader are restored.
   Save and reopen after final removal: its owned FX/shader subassets must not
   remain. A reference still used by another layer must not be removed.
7. Draw in an unsaved document, including a nested group and an HDR Drawing layer.
   Manually recompile Unity scripts, repeatedly: pixels, layer settings and the
   unsaved indicator must survive. Repeat with unsaved edits in a saved document.
   Also recompile while a stroke is active; its latest visible pixels must remain,
   regardless of the order in which document and window receive OnDisable.

Exception cleanup in the composition render paths is checked by source inspection;
the smoke script does not inject graphics failures or verify GPU allocation counts.
