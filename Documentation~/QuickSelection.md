# Quick Selection runtime contract

Quick Selection produces the window-local `CanvasSelection`. It changes no document pixels,
assets or document Undo. Existing painting, copying, comment-mask and assistant consumers
read the same native-resolution coverage. Pending work reports `document_busy` to live
assistant requests. There is no separate agent command for painting these hints.

## Persistent hints

`W` selects the tool. Add marks foreground; Subtract, right mouse or Alt at pointer-down marks background,
including before a selection exists. Size, mode, Strength and Tolerance are frozen per stroke.
The last stamp replaces hints under its footprint; unpainted hints remain throughout a session.

Hints use sparse native 256 × 256 byte tiles. Absent tiles read as 128; neutral-only tiles are
released. 255 is hard foreground, 0 hard background, 128 unknown. Strength `s` maps to
`128 + round(127*s)` for Add and `128 - round(128*s)` for Subtract. Intermediate bytes are
finite preferences, not output opacity. Strength zero erases hints, and clearing all hints
restores the selection that existed before the session. Deselect discards both hints and output.

## Boundary solver

The solver minimizes a binary cut energy: positive/negative hint costs, a cost for selected
area, and contrast-sensitive links between four adjacent pixels. A soft image-rectangle
perimeter cost prevents an Add-only flat image from becoming entirely selected. It is not a
hard background constraint on edge pixels. Repeated canvases link opposing edges instead;
a constant repeated source uses a stronger normalized area prior because it has no outer border.
Hard 0/255 hints dominate all incident links. Finite soft hints can yield even at their own
pixel; stronger positive evidence cannot shrink the optimum on an unchanged graph.

Source features retain signed finite HDR RGB through `x/(1+abs(x))`, weighted by alpha,
with alpha as an additional feature. Invisible hidden RGB does not introduce visible edges.
The source pixels are never changed or converted into an SDR texture.

The first graph covers an image of at most 512 × 512 samples. Each subsequent level doubles
resolution and cuts a band around the previous boundary, including contradictory native
hints. A band expands when it constrains a continuation. Strongly connected narrow ridge
candidates also expand it so an artificial band boundary does not cut a thin branch at its
root. Final refinement samples native pixels; output is not a bilinear enlargement of a
coarse selection. Small details absent from the coarse image and far from both the current
boundary and the hints can still require an additional Add/Subtract stroke.

The portable C# maximum-preflow solver uses FIFO push/relabel and reverse breadth-first
relabeling. Exhaustive independent small-graph label enumeration checks the returned energy.
Refinement bands exceeding the graph node budget use bounded block-coordinate cuts with
neighbouring labels, repeating sweeps until stable. They do not solve isolated texture tiles.
Coarse-to-fine bands and block relaxation are approximations to a full native-image cut;
they are not a guarantee of its global optimum or semantic object recognition. This solver
also does not estimate translucent hair/edge alpha. New output is binary; unrelated baseline
fractional coverage is preserved for additive refinement.

## Scheduling and memory

- The interactive path first publishes a contour snapshot from a graph of at most 128²
  nodes. This snapshot is display-only: it does not change native coverage, its revision,
  or the GPU selection mask. A bounded preview is allowed to finish during continuous
  input; queued hints produce its successor, and displayed preview revisions never decrease.
- Native refinement starts after 180 ms without new input, or immediately after release
  and ingestion of the queued stamps. It retains the 512² coarse graph and native boundary
  solver above, so preview resolution does not constrain the final boundary. Both stages
  reuse the captured source. New input interrupts graph building/solving; obsolete final
  results are rejected by revision before publication. A pending bounded GPU read is retired
  normally rather than synchronously drained.
- Tool selection and Copy wait asynchronously for refinement; Stop cancels those pending
  actions, removes the provisional contour and retains only already committed native output.
  A pause can finish refinement with the mouse still held; subsequent movement starts a new
  preview from the accumulated hints. Source/context changes cancel as before.
- Input only queues stamps. One background worker stamps hints, plans reads, solves cuts,
  and prepares publication; no max-flow or full-mask walk runs in an input callback.
- GPU source requests are asynchronous when available. The initial read is at most 512²
  RGBAFloat pixels (4 MiB); sparse refinement reads at most 258² samples including a halo.
  A compact contour is packed into one region of at most 528² samples (about 4.26 MiB),
  avoiding a separate editor/GPU round trip for each quadrant. The
  fallback reads only that bounded region. Spatial FX may still require their existing
  full-source branch; this feature does not change the FX contract.
- Hints cost at most 64 MiB at 8192², with no separate GPU hint image. Native output costs
  64 MiB; the optional GPU R8 selection costs another 64 MiB. A pre-existing selection may
  require a 64 MiB baseline. Refinement uses a byte label pyramid, a bitset band, bounded
  graph arrays and regional features, not full-resolution RGBA source copies.
- Bulk label expansion and changed-tile classification use portable Burst with arrays pinned
  only for the synchronous worker job. They do not copy to native arrays and back. Cancellation
  is checked between these bounded jobs and regularly inside graph solving.
- Only changed 256² output tiles reach the UI and GPU. Contours and combined bounds are
  prepared on the worker. Dense contours use one shared sampling step across every tile
  and its seams; exceeding the segment budget rebuilds the complete contour on a coarser
  grid. Individual segments are never decimated out of a boundary. Native coverage is
  unchanged. Stop keeps already published output and retained hints. Reset
  discards the session. Late readbacks are guarded by generation; teardown drains only
  owned work. Source samples are released on completion/cancellation to avoid stale input.
  Pending work requests editor updates and repaints; idle-editor throttling can still make
  end-to-end GPU integration slower than the separately reported CPU solve.

## Validation

`quick-selection-quality` records analytical boundary error/IoU, worker and total elapsed
CPU time, source request counts and estimated solver-array peaks on identical inputs. Cases
include flat Add-only input, weak boundaries, textured areas, one/two-pixel branches, HDR
holes and 8K weak-boundary/frame images. Generated reports belong in `Temp/WhimTex`.

`quick-selection-refinement` checks hard/soft hints, overwrite, neutral erasure, fractional
baseline, wrap and the independent min-cut energy oracle. `quick-selection-8k` also exercises
regional GPU sampling, native R8 publication, queued corrections, cancellation and teardown
through an owned detached tool window. No mouse/keyboard automation or user document is used.
Progressive checks record time to the first CPU snapshot and the first tool-visible contour,
verify pause/release scheduling, sustained-input preview delivery, in-flight supersession,
and exact native HDR Add/Subtract/Add output on 8K. CPU-only time excludes GPU/editor cadence.
Contour checks cover curved rings, many small components, odd canvas dimensions and 8K:
complete and intermediate publications must have no dangling endpoints, fit the display
budget, and leave every native mask byte unchanged. Restored contour caches are checked too.
