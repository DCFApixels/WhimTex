# Local Editor hang capture

This opt-in diagnostic collects evidence; it neither fixes nor reproduces a hang.
No test, compilation, Editor restart, document edit or capture is performed by
preparing these files. The Windows helper is an explicitly approved diagnostic
exception; no native Windows dependency was added to the package runtime.

## Preparation

Use a Microsoft-signed ProcDump executable from the
[official download](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump).
Keep its supplied license. Place `procdump64.exe` in the project's
`Temp/WhimTex/HangDiagnostics/tools/`, or pass `-ProcDumpPath` explicitly.
The helper verifies the product identity and signature before running it.
It does not download/install tools or silently accept a license.

Enable breadcrumbs, without starting a capture:

```powershell
& 'D:\DCFA\Projects\Test6.6\Packages\com.dcfapixels.whimtex\Tools~\Capture-EditorHang.ps1' -ProjectPath 'D:\DCFA\Projects\Test6.6' -Mode Arm
```

After the Editor has loaded the changed scripts normally, finish a vector-brush
stroke (or change Original Mask settings). This starts a local JSONL journal.
It records the main thread and contour worker, phase-entry C# stacks, nested phase
IDs, leave events, dimensions/tolerances, one-second Editor heartbeats and assembly
identity. Existing profiler scopes are included even when profiler recording is
off. It also covers rendering/inspector work after the geometry commit.

Each line is flushed before the next operation, independently of the Editor
console. Files go to `Temp/WhimTex/HangDiagnostics/trace-*.jsonl`. This is deliberately
slower diagnostic instrumentation, not a performance benchmark. One session lasts
at most ten minutes, 20,000 records or 32 MiB; the first limit stops recording.
The flag is observed once per second. To rearm after a limit, disarm, allow one
Editor update, then arm again; a script reload also starts a fresh session.
Old files are retained. A reload/exit/disarm closes the current journal.

## When the Editor hangs

Do not close/restart Unity before capture. Run the helper from an external
PowerShell, not through Unity's blocked command queue. If reproducing with an
agent, report the hang and leave the Editor running so the agent can capture it.

After reading/accepting the Sysinternals license:

```powershell
& 'D:\DCFA\Projects\Test6.6\Packages\com.dcfapixels.whimtex\Tools~\Capture-EditorHang.ps1' -ProjectPath 'D:\DCFA\Projects\Test6.6' -AcceptEula
```

`-AcceptEula` is an explicit agreement to the tool's license; omit it if already
accepted. The helper selects exactly one non-worker Editor with the matching
`-projectPath`. If ambiguous, specify its `-UnityProcessId`; the ID is still checked
against the project. PID and creation time are rechecked before each dump, so a
restarted Editor is not substituted. The raw process command line is not logged.

The first capture is full memory (useful for Mono/JIT inspection); a second mini
dump follows five seconds after it completes. Comparing stacks distinguishes
progress from a repeated wait/hot loop. No `Responding`/hung-window trigger is used:
the busy dialog can process Windows messages while the Editor's main thread is stuck.
The capture may briefly pause Unity and requires substantial disk space. There is
no memory-heavy process clone. `-MiniOnly` takes two smaller dumps if memory/disk
pressure prevents full capture, but can omit data needed for managed/JIT analysis.
Collection has a 45-second ProcDump timeout. On failure, partial files remain;
they are not reported as a successful capture. Do not force-kill a running capture;
Ctrl+C lets ProcDump detach/resume safely.

A unique `capture-*` folder retains dumps, before/after journals, a log tail when
the project's log is discoverable, the package assembly/debug symbols, metadata
and SHA-256 hashes. Nothing is uploaded, and no global debugger/WER configuration
is installed. Dumps/logs can contain document contents, paths and session secrets:
keep the folder private and inspect/redact before sharing anything.

## Reading the evidence

An unmatched `enter` identifies an unfinished instrumented scope, **not proof of
which instruction is currently executing**. `entryStack` is the cooperative C#
stack from the start of that scope; it is never presented as a sampled hang stack.
`leave` means the scope exited, including exception unwinding, not successful work.
Continuing heartbeats with an open worker scope indicate that the Editor update
loop still runs. Missing heartbeats alone do not diagnose a deadlock.

Open each dump in WinDbg/Visual Studio with the matching Unity symbols, as described
in [Unity's Windows debugging guide](https://docs.unity3d.com/6000.0/Documentation/Manual/WindowsDebugging.html).
`EditorHang.windbg.txt` configures the Microsoft/Unity symbol servers and prints all
native thread stacks, thread CPU times and loaded modules. For an installed CDB:

```powershell
& 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe' -z '<capture-folder>\threads-1.dmp' -logo '<capture-folder>\stacks-1.txt' -cf '<capture-folder>\EditorHang.windbg.txt'
```

Repeat for `threads-2.dmp` and compare the main-thread/worker stacks. This opens a
dump, not the live process. A debugger is not installed or launched by the capture
helper. Resolving symbols downloads symbols, not the dump; keep captured data local.
Do not apply .NET CLR/SOS assumptions to Unity's Mono runtime. Native symbols,
matching Editor version, retained managed assembly/PDB and entry breadcrumbs must
be correlated. Mono/JIT frames may remain unresolved; a dump is evidence, not a
guarantee of source-line resolution. If needed, attach a supported managed debugger
in a separately agreed reproduction rather than inventing a managed stack.

## Disable

```powershell
& 'D:\DCFA\Projects\Test6.6\Packages\com.dcfapixels.whimtex\Tools~\Capture-EditorHang.ps1' -ProjectPath 'D:\DCFA\Projects\Test6.6' -Mode Disarm
```

This removes only the opt-in flag, retaining all journals/dumps. Runtime capture and
the vector-brush reproduction remain untested until the user's next manual run.
