# Make Seamless agent contract — 2026-09-29

## Scope

- AgentAPI now documents all 42 settings across Offset Blend, Mirror, Screened Poisson and Patch Quilting, including UI names, valid ranges and registry-created defaults.
- Live/clipboard entry points distinguish settings envelopes, source references, fractional percentages and Feather's 0–100 units. EN/RU/ZH guides link the same contract and clarify that changing Transition Start does not toggle correction.
- Describe adds Quilting quality and channel-matching enum lists. The generated clipboard schema derives enum spellings from C#, annotates new-layer defaults and explains independent passes/units.
- Numeric validation uses double bounds before conversion to stored floats. The original float-bound check rejected exact valid JSON endpoints, reproduced with `blendWidth: 0.001`; Patch Width 0.45 and Offset Transition Start 0.95 had the same risk. Rendering algorithms, field names, saved settings and defaults are unchanged.

## Commands

From the package directory:

```sh
node Tests~/MakeSeamlessContract.test.mjs
node Tests~/AgentDocumentation.test.mjs
node Tests~/AgentCommandInventory.test.mjs
node Tests~/ProceduralClipboard.test.mjs
node Tests~/MakeSeamless.test.mjs
node Documentation~/scripts/build-agent-fields-schema.mjs --check
node Documentation~/scripts/check-docs.mjs source
```

From the Unity project, after a normal Editor compilation:

```powershell
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/MakeSeamlessContractSmoke.cs --entry MakeSeamlessContractSmoke.Main --project-path D:/DCFA/Projects/Test6.6 --format json
```

## Results

- Unity compilation: completed, no errors.
- Runtime contract smoke: **4407 checks passed**. Checks schema defaults against Describe; every enum/bool/numeric endpoint; partial-update isolation; serialized snapshot reapplication; invalid fields/types/ranges; the actual add/target/set examples extracted from AgentAPI; clipboard parsing and a 64×64 render of the new recipe; render-state restoration.
- Static contract: 42 parser/schema/snapshot/documented fields, numeric bounds and 10 enum fields matched.
- Existing numerical suite: 485916 checks plus integration/source contracts passed.
- Agent documentation: 21 commands and 45 JSON examples passed. Command inventory and procedural clipboard/schema checks passed.
- Documentation source: 79 pages, reciprocal translations and local Markdown links passed.

The test runner resolves the API's JSON assembly through reflection to avoid conflicting JSON types in the Editor's loaded assemblies. Tests use temporary models only, do not open windows, save assets or modify user documents. This is contract/parser/render-availability coverage, not a new visual-quality audit, full-resolution benchmark or published-site build. Static contract and clipboard checks are included in the documentation workflow.
