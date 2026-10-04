# Layer composition verification

Historical verification of the `598b104` layer refactor (package 0.9.0 at that checkpoint).
Current file compatibility starts at 0.12.5; compositor `.asset` documents are not supported.

## Automated checks

Run source contracts from the package directory:

```text
node --test --experimental-test-isolation=none Tests~/*.test.mjs
node Documentation~/scripts/check-docs.mjs source
```

Unity smoke scripts use transient documents, textures and, where needed, separate
unsaved windows. They must run through the connected Editor's Pipeline, never a
standalone MSBuild process. Undo tests must not run during a user edit or paint gesture.
For example:

```text
unity command eval_file --file "<package>/Tests~/LayerCompositionSmoke.cs" --project-path "<project>" --format json
```

After source edits, compile through the connected Unity Editor/Pipeline under the current project rules.
Check both the transport result and the nested command result when executing smoke scripts.

## Baseline verification before the follow-up

- C# compilation: completed with no errors.
- Source contracts: 40 tests passed; documentation validation: 51 pages passed.
- LayerCompositionSmoke: 22 checks, including native clone, JSON wrapper round-trip,
  exclusive behaviour ownership, missing-group traversal, compatible field transfer
  and in-place content adoption.
- LayerCompositionWindowSmoke: 8 checks, including multiple-layer/group conversion,
  wrapper identity and native Drawing pixels across Undo/Redo.
- LiveAgentSmoke: 113 checks, including image and procedural completion, retained
  placeholder identity, inline FX, selection, conflicts and Undo/Redo.
- MergeLayersSmoke: 20 checks; HdrGroupSmoke passed.
- SwizzleSmoke: 287 checks; ColorPipelineSmoke: 59 checks in a Linear project.
- GaussianBlurSmoke: 18,369 checks; MotionBlurSmoke: 24,799 checks.
- GaussianBlurApiSmoke, NoiseSmoke, NormalMapSmoke, MakeSeamlessSmoke,
  HiddenEffectInputSmoke, EffectCacheSmoke and DrawingPatternSmoke passed.

Two issues found during validation were corrected: integer addressing at Repeat/Mirror
seams in Blur, and registering newly created native objects before the document's
structural Undo snapshot in merge/conversion/completion workflows.

## Follow-up verification

- Source contracts: 41 passed; documentation: 51 pages passed.
- LayerBehaviourSwapSmoke: 7 passed against the loaded Editor assembly. Replacing Drawing
  preserves its pixels, Undo/Redo works, and the detached behaviour can be reattached.
- LayerScrollViewSmoke reproduced the actual short-list bounds: viewport 276, content 34,
  low 0, high -242. The previous clamp returned -242; the fixed clamp returns 0.
  Source/extracted arithmetic checks cover both short and overflowing lists.
- A real temporary behaviour type was removed between domain reloads. Missing leaf/group
  wrappers retained names, GUIDs, common settings and descendants. Persisted Drawing pixels,
  effect target IDs and embedded FX subassets survived. The missing group stayed hidden.
- The disposable unsaved Drawing window also retained its ID and red pixels across reloads.
- The native recovery test exposed Unity's typed diagnostic payload (`seed 613 (int)`),
  which the old YAML/JSON parser did not recognize. Support was added, but its runtime
  verification and the recovery/save/reopen portion still require manual compilation.
- The disposable asset, window and compiled-source fixture were removed. User assets were
  not modified. An ignored fixture template remains under `Tests~/Fixtures` for repeatability.

## Remaining manual/native checks

After manual compilation, run `MissingLayerDataSmoke.cs` for the captured native scalar
payload and representative nested/vector/list/string forms. The latter are parser fixtures,
not proof of Unity's native format for every type. Unsupported object-reference formats must
keep defaults, not fabricate or clear texture references.

The old LayerPersistence setup/verify/cleanup scripts and native compositor `.asset` route
were retired during legacy cleanup. They are not current test instructions.
Current coverage uses `DocumentMissingTypeSmoke` and `Compatibility0125ReaderSmoke` for bounded
unknown-type decoding and save protection, `DocumentRoundTripSmoke` for TIFF pixels/IDs/FX,
`DocumentReloadSmoke.Prepare` → real domain reload → `Verify` for file binding and unsaved state,
and `MissingLayerDataSmoke` for the native recovery parser.
The historical outcomes above are retained; they do not claim that old assets are supported.
Finally, manually drag a layer near both list edges with short/long lists and expanded groups.
The bounds were verified in UI Toolkit; a full interactive drag gesture remains a visual check.
