# Make Seamless release verification — 2026-09-28

## Changes in this pass

- Consolidated Unreleased notes into final behavior, defaults and explicit upgrade notes. Published version sections were left intact.
- Removed the obsolete numeric transition reference; clarified the ordinary 512-pixel preview limit, full-resolution Pencil preview and resolution-sensitive patch selection in UI and EN/RU/ZH guides.
- Corrected a misleading guide sentence: compensation adds work, it does not make enabling the option faster than disabling it.
- Guarded deferred leaf-thumbnail refresh after window/document teardown. The release test captures the actual callbacks and invokes them after closing the window.
- Fixed the integration fixture so group/clipping/channel checks run with quilting edges enabled, rather than accidentally testing bypass.
- No rendering algorithm, quality budget, saved parameter or default changed in this pass.

## Reproduction

Use the connected Editor only, one command at a time:

```sh
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/SeamlessReleaseSmoke.cs" --entry SeamlessReleaseSmoke.Workflow --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/SeamlessReleaseSmoke.cs" --entry SeamlessReleaseSmoke.Preview --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/SeamlessReleaseSmoke.cs" --entry SeamlessReleaseSmoke.Stress --args '[2048,4]' --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/SeamlessReleaseSmoke.cs" --entry SeamlessReleaseSmoke.Visuals --project-path "D:/DCFA/Projects/Test6.6" --format json
```

Stress's second argument is the stored mode ID: Mirror 0, Screened Poisson 2, Offset Blend 3, Patch Quilting 4. Run modes separately: the combined 2K batch exceeded the CLI's 30-second timeout, including exhaustive CPU comparisons. The Editor subsequently responded normally; separate runs all completed. This was not a 30-second single render. Stress measures the render plus synchronous readback only, excluding the expensive pixel assertions between samples. `Status` reads the last completed stress report from SessionState.

Workflow creates uniquely named `Assets/WhimTexSeamRelease_<guid>` test TIFFs, then deletes only that owned folder in finally. No existing user documents are opened or changed. UI tests restore focus; Workflow isolates its Undo group and restores selection. Visuals writes generated PNGs only under `output/seamless-release`.

## Results

| Test | Result |
| --- | --- |
| Workflow | 393600 checks: all four modes, real TIFF save/destroy/load, declared settings, target IDs, window edit Undo/Redo, transfer copy backend, rendered equality, PNG encoder/decoder and sRGB quantization |
| Preview | 1572870 checks: actual Pencil window output at 768×512 vs full composite, nonempty cache, zero cache bytes after close, safe deferred thumbnail callbacks |
| PatchQuiltingSmoke.IntegrationShifted | 294954 checks, now with active group/clipping input |
| PatchQuiltingSmoke.Workspace | 36933 checks: allocation reuse, budget, nested rent, expiry and native/texture disposal |
| SeamlessEmptyEdgesSmoke.Main | 43211 checks: five selectors, all-off bypass, four modes and render paths |
| SeamlessDefaultsSmoke.Main | 64 checks: factory, retained saved values, ordered UI, percentages and highlights |
| SeamlessChannelsSmoke.Main | 2362650 checks: RGBA masks, directions, render paths, group, clipboard and Undo/Redo |
| QuiltingSeedLayoutSmoke | 320/620-pixel panels: input 110/410 pixels, Random stays 64, long values do not shrink the input |
| LayerTransferSmoke | Real UI Toolkit drag events: top/before/after/group/end/footer passed with Quilting + source group; settings, target remapping, copy independence and transfer Undo/Redo checked |
| MultiWindow | Two 1024×768 High/Independent/compensated/Poisson windows; result caches 18.8 MiB each, independent output and complete cache release on close |
| Node contracts | MakeSeamless (485916 numerical checks), AgentDocumentation, ProceduralClipboard, schema and localized documentation source checks passed |

Workflow calls the capture/paste backend; LayerTransferSmoke additionally sends drag events through actual laid-out UI elements. Neither is a manual pointer-gesture test. The release workflow tests one nondefault parameter combination per mode, not every possible serialization combination. Existing targeted tests cover additional combinations.

## Local performance observations

2048×1536 HDR packed source, all edges, compensation and Poisson enabled for blend/quilting modes. Quilting: High, Independent, Patch Width 45%, Along-Seam Search 25%, Feather 73%. First evaluation is a cache miss, not a guaranteed cold Burst compilation. Times include GPU readback and are individual samples, not benchmark distributions.

| Mode | First, ms | Cached repeats, ms |
| --- | ---: | ---: |
| Offset Blend | 875.63 | 43.17 / 97.83 |
| Mirror | 800.54 | 43.31 / 97.63 |
| Screened Poisson | 745.83 | 39.91 / 42.24 |
| Patch Quilting | 885.16 | 43.12 / 104.60 |

All repeated pixels matched within the test tolerance and were finite. This does not guarantee an interactive frame budget. Full-resolution global correction remains expensive. Multi-window verification used two 1024×768 windows, not two 2K windows. Cache accounting is not a measurement of total peak GPU/CPU memory. 4K+ and other GPUs/backends were not stress-tested here.

## Visual artifact and limitations

`output/seamless-release/visual-matrix.png`: columns source / Offset Blend / Mirror / Screened Poisson / Patch Quilting; rows smooth scalar / sharp pattern / color / transparency over checker / packed R. Joins are shifted to the center. Color and transparency use Linked quilting; the packed row uses Independent. These are nondefault test configurations with correction enabled, not a ranking of default modes.

Inspected the generated matrix: sharp structures can deform or ghost in blending modes; Poisson can retain visibly discontinuous detail even when boundary values improve; quilting can alter shapes near cuts. No algorithmic retuning was done to hide those limitations. The matrix is a visual baseline, not proof of invisible seams for arbitrary inputs.

Remaining release gates: visual confirmation on representative artist documents; supported backend coverage; 4K+ peak memory/latency if advertised; actual drag gestures with different DPI/docking layouts. No package version bump, player build, commit, push or publication was performed.
