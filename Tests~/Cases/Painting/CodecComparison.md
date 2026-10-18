# Opt-in lossless codec and spill-policy diagnostics

These cases do not change production drawing, compression, history scheduling or user settings.
The corpus is produced by the actual Brush path in detached documents. `paintSurface` remains
Half, compute remains enabled, and production history pages provide before/after Half and
before/after stored bytes. No full-canvas GPU `ReadPixels` is used.

## Prerequisites and lifecycle

Native LZ4 use requires separate user approval under package rules. The tested library is the
already installed Entities `Unity.Core.Editor/Compression/lz4-1.9.1/liblz4` (Windows/macOS/Linux
variants); no package installation, vendored binary or production dependency is added.
Temporary fixture assets also require explicit approval. Missing libraries are a prerequisite
failure, not permission to download one.

1. Review `paint-codec-fixture-stage`, its support sources and declared effects; run with
   `assets,temp-files`. It copies canonical sources into the owned
   `Assets/WhimTexCodecDiagnostics_Temporary` Editor assembly with `allowUnsafeCode:true`.
2. Request `AssetDatabase.Refresh` through the connected Editor and wait for compilation.
   Run `paint-codec-preflight`. It requires actual Burst execution, LZ4 version 10901 and
   byte-exact recovery for zero, repeated, random and hidden-color/arbitrary-Half patterns.
3. Run the `paint-codec-smoke` profile, then individual `paint-codec-corpus-8192-*` cases.
   Modes are `thin`, `wide`, `detail`, `hdr`. They need an idle graphical Editor, 7 GB VRAM
   and 8 GiB free disk. Capture copies/hashes one completed page per Editor update.
4. Run corresponding `paint-codec-measure-8192-*` cases. For spill replay, use
   `paint-spill-measure-8192-{wide|hdr}-{short|paced|burst}` sequentially. Splitting schedules
   keeps each case within the runner time budget. Each tests all four policies twice, except
   the largest HDR burst: run both `paint-spill-measure-8192-hdr-burst` and its `-repeat` case
   to cover the first and second repetition separately.
   The unsuffixed spill cases run all schedules together and are useful for small corpora.
   Once the authenticated corpora exist, `paint-codec-8k` and `paint-spill-8k` select the
   bounded comparison cases, including both HDR burst repetitions.
5. Run `paint-codec-fixture-remove`, then refresh and wait for compilation. Removal verifies
   the ownership marker, exact canonical source copies and absence of unknown files or active
   workers. It never removes user assets. Generated corpus/results remain in `Temp/WhimTex`.

No UI/input automation is used. Test source compilation uses Unity Editor only. A raw Pipeline
`run_script` compilation does not permit unsafe code; an `AssemblyBuilder`-loaded assembly was
not registered as Burst entry points in the tested Editor. The imported temporary assembly
avoids measuring a managed fallback against native LZ4. Canonical executable test sources remain
under `Cases` / `Framework`.

CPU cases temporarily defer domain reload until their owned worker is drained. This is released
by the registered cleanup. Do not run another benchmark concurrently. Setting the AppDomain
key `WhimTex.Tests.CodecPause` to `true` skips *new* codec measurements for coordination; it does
not interrupt the active case. Clear the key to resume. The ordinary runner lock and review
receipt still apply.

## Codec measurement

`latest-{side}-{mode}.txt` identifies a generated corpus. Every page is authenticated against
its SHA-256 manifest before replay. `Visit` validates format, rectangles, blob lengths, raw
byte totals and end-of-file. The diagnostic can also measure the fully captured first `detail`
stroke from a cancelled two-stroke capture, only when all 64 pages belong to stroke zero;
that capture failure remains in its immutable manifest and must be disclosed in results.

Variants: the current production Burst codec (called through project-owned reflection),
installed native LZ4 default fast compression, and a portable Burst byte-PackBits baseline;
each runs with and without byte planes (4 planes for RGBA32, 8 for RGBAHalf). This is one RLE
implementation, not a claim about every possible RLE codec. All use raw fallback when packed
data is not smaller. Raw fallback retains original byte order. Packed sizes include per-blob
length prefixes, excluding the small common page/rectangle envelope.

One page warms the kernels. Three complete passes rotate variant order per block. Encode timing
includes shuffling and raw-fallback handling; decode timing includes unshuffling/restoration.
Allocation, source-file decoding, input loading, authentication and exact comparison are outside
these intervals. Every candidate output is compared byte-for-byte through the portable Unity
`UnsafeUtility.MemCmp` API, never by approximate pixel error or hash alone. The preflight also
checks mismatch detection at the start, middle and end. Plane-level rows distinguish before,
after, Half and stored data. Workspaces are bounded and reused. Production reflection wrapper
overhead remains in its timings. These are single-worker kernel measurements, not brush FPS.

## Spill-policy replay

The prototype preloads immutable pages from the second real stroke, then replays their bytes
through actual current-codec compression, the production page checksum and file writes with
four background workers (worker startup and workspaces are prepared before timing):

- immediate spill, 128 MiB hard resident cap;
- immediate spill, 768 MiB cap (equal-cap control);
- 512 MiB soft / 768 MiB hard, oldest history first, drain toward 384 MiB;
- the same thresholds with emergency compression by the producer at the hard limit.

The active image is never a candidate for eviction. This is already consistent with the current
painting path; the experiment tests when immutable *history* leaves RAM. Short traces enqueue
eight pages; paced traces replay three complete strokes with 750 ms gaps; burst traces replay
six without intentional gaps. Two passes rotate policy order. Payloads are a deterministic
replay of captured data, not six newly painted GPU strokes.

Reports include resident high-water mark, separate frozen-fixture RAM, a separate idle page pool
(at most 64 MiB), producer copy/wait/foreground time, drain time, bytes written before forced
flush, retained history and complete-file recovery. All output pages must recover exactly and
dispatch in oldest-first order; completion may be out of order because page identities preserve
logical order. Verified output pages are removed between policy runs. File writes use ordinary
OS buffering, not a forced physical-disk durability benchmark. No claims about presented frame
rate, GPU readback, Unity Undo latency or process-wide peak memory follow from this CPU replay.

## Production LZ4 verification

`paint-codec-preflight` additionally cross-decodes production and native LZ4 in both directions,
including odd lengths, tiny blocks, long/overlapping offsets and arbitrary Half bits. Guard bytes
check the production decoder's output capacity on malformed input. `paint-history-codec` covers
public block I/O, truncated streams, raw fallback, native row gathering and exclusive workspace reuse.

`paint-lz4-measure-{512-wide|8192-thin|8192-wide|8192-detail|8192-hdr}` measures only production
and native LZ4 without shuffling. Each real corpus block is cross-decoded after the timed interval.
Three rotated passes write `lz4-results.json`, leaving prior `codec-results.json` intact. Preserve
pre-change reports before editing the implementation; the label `current` means the code at run time.
Use `paint-history-burst-8192-saturation` and `-hdr` for six newly drawn dense masked GPU strokes,
actual background page writes and ordered Undo/Redo. These include readback and queue pressure;
the codec microbenchmark does not. `paint-tile-history-queued` compares every stored and Half byte
across six overlapping revisions, near-transparent paint and erasure, in both SDR and HDR.

Corpora, run reports and per-case JSON stay in project `Temp/WhimTex/codec-comparison`.
