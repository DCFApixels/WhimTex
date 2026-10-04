# All Below effect input validation

Validated in the connected Unity Editor on 2026-09-29.

## Contract

- `Previous` and `Specific` retain their existing values and behavior. The default remains `Previous`.
- `All Below` combines visible lower siblings in the current container on transparency, including opacity, blending, clipping and effects.
- Inside groups, including Pass Through groups, the input is local to the group and excludes the external backdrop. An empty lower stack is transparent.
- Ordinary full-stack rendering reuses the existing accumulator. Standalone previews, clipped inputs, partial renders and Pass Through scopes may reconstruct the local lower stack.
- Copies retain stack-relative `All Below` input. API and portable clipboard serialization support the new mode without an explicit target.

## Results

- Unity script compilation: completed without errors.
- `AllBelowInputSmoke.cs`: 181441 assertions passed, including pixel comparisons for Outline, SDF, Normal Map, Blur, Sharpen and Make Seamless against a precomposited Specific input; main/mini preview, thumbnail and export paths; lower Shader Processor output; Patch Quilting cache invalidation; groups, clipping, cycles, serialization, clipboard and shared settings UI.
- The cold-cache normal rendering check observed exactly two source publications for two lower source layers: the lower stack was not generated again for the effect input.
- `EffectCacheSmoke.cs`: 278538 assertions passed.
- `HiddenEffectInputSmoke.cs`: 22560 assertions passed, covering existing Previous/Specific behavior.
- `ShaderProcessorSmoke.cs`: 57 assertions passed.
- Clipboard schema/fixture checks, agent documentation examples and source documentation validation passed.

The checks use temporary in-memory documents and textures, not saved user assets. Pixel assertions are not independent test cases or a performance benchmark. Large-document timing and interactive visual acceptance remain separate checks.

## Run

```powershell
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/AllBelowInputSmoke.cs --entry AllBelowInputSmoke.Main --project-path D:/DCFA/Projects/Test6.6 --format json
```

The existing regression snippets use `unity command eval_file --file <path>` with the same explicit project path.
