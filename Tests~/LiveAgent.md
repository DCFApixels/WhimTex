# Live editing checks

Run `node Tests~/LiveAgent.test.mjs` for extracted scalar/control-flow and contract checks without
Unity. This is not a substitute for compiling or exercising the Editor.

`AgentEditingSmoke.cs` runs with `run_script`, entry `AgentEditingSmoke.Main`, in the explicitly
selected connected project after compilation. It covers shared FX/structure/repair operations,
all built-in preset metadata and dry-run insertion, diagnostic pixels, revision/lock conflicts,
Undo/Redo, runtime-error rollback, headless replay and TIFF round-trip. It owns an unshown
temporary window and a uniquely named `Assets/agent-edit-*` folder, removed in `finally`.
Allow test asset creation before running it; diagnostic PNGs remain under `Temp/WhimTex/Agent`.
It also checks bound TIFF paths in Assistant responses, group transforms, original/imported File
dimensions, discarded `save:false` edits, cleanup after rejected revisions, malformed Headless
requests and save-failure recovery guidance.

Run `node Tests~/AgentDocumentation.test.mjs` for the documented command inventory, FX parameter
limit/types and JSON example checks. These are static checks; they do not replace Unity smoke tests.

After the user manually compiles, `LiveAgentSmoke.cs` is an opt-in main-thread C# eval script for the
intended project. It creates a separate unsaved window and temporary PNG files only. It tests live
discovery, fast begin, shared-context forks, begin/completion retries, user rename/visibility/reordering, trial isolation, completion
Undo/Redo, permanent cancellation after delete/Undo, image insertion and color, frozen soft selection,
strict/guide completion and fork policy,
inline Shader FX preview/completion, code inspection, invalid HLSL recovery, edit locks and FX Undo/Redo,
target conflicts, canvas resize and close. It does not trigger compilation or import/save assets.

Additional manual checks:

- While a named reservation is pending, rename it, toggle the eye, drag it into/out of a group.
  Its opacity, blend, transform and content remain unavailable. Other layers remain editable.
  Leave a delayed name edit uncommitted and attempt completion: it must wait with document_busy,
  not discard the typed name. Commit the text, then repeat the same completion request.
- Lock a Drawing layer, then try brush/pencil, fill, Transform, Clear, opacity digits/multi-edit,
  clipping shortcut and FX/settings edits (including separately open property/FX windows). These must
  not change locked content. Name, eye and tree movement remain available. Cancel Agent Edit restores
  editing without removing the layer. Other layers stay editable; a group lock covers its own settings.
- Check active locks release after Undo/Redo, close/switch, deletion and reload; redo must not revive a
  lock. Repeated lock/unlock requests must be safe. A competing lock or pixel replacement must fail.
- Add an inline Shader Processor and FX on a normal layer; try an existing #include, parameters and
  a deliberate syntax error. Save/reopen only an authorized fixture to verify embedded code/shader
  persistence. Replacing a shared external FX reference must not modify the referenced asset.
- Insert a reservation between clipped layers and their base, or between a Previous effect and its
  source. The rendered image must not change. Completing it then uses ordinary layer semantics.
- Use a parameter candidate preview repeatedly; the live Layers list must not grow and trial renders
  must not enter Undo. Check an unsaved Drawing's pixels survive trial rendering.
- Generate a region repair with an arbitrary selection. Changing the live selection afterwards must
  not change delivery. Inspect soft boundaries and verify untouched pixels outside the mask.
- Generate an object with `selectionMode:guide` and enough padding for its shadow. The shadow outside
  the selection must survive, including partial alpha; pixels beyond the captured crop stay unchanged.
  Compare with strict mode. Guide pixel replacement replaces the entire crop, not just the white mask.
- Replace pixels in a rotated/scaled Drawing. Verify the mask maps into its stored pixels; repeating
  transforms must report a clear error and permit a new-layer workflow instead.
- Save As while pending: the active job follows the new copy. Cancel an old copied reservation in a
  different document and confirm the active copy's task is unaffected.
- Recompile/reopen with saved reservations: they show as interrupted and can be cancelled. A late
  completion must not recreate them in a different document.

Do not use an existing artist document as a disposable fixture. Test saving/subassets in a separate
fixture only when the user authorizes it. Generated test PNGs are under Temp/WhimTex and may be
removed after inspection; the script does not delete user files or clear global Undo history.
