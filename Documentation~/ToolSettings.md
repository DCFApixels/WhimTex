# Tool preferences

Tool defaults are per-user editor preferences, not document content. Registration lives in
`WhimTexWindow.ToolPreferences.cs`; persistence and lifecycle belong to
`WhimTexUserSettings.ToolPreferenceSession` in `src/WhimTexUserSettings.Tools.cs`.

## Ownership and storage

- Each window keeps its own values and owned brush resources. Register each section with
  typed getters/setters, a fresh-default factory and optional normalization/codec.
- Reuse the existing Canvas PaintToolSettings, Tool and TransformReturnTool EditorPrefs
  keys. Other registered sections share the Canvas ToolSettings preference. Do not create
  a second brush, font, gradient or asset-reference preference system.
- Register actual user options only. Document dimensions, selected IDs, UV mesh/channel,
  transform values, crop frames, path vertices, masks, pointer capture, editing sessions,
  temporary FX tools and viewport positions remain outside preferences. Zoom/Transform/
  gradient handles edit document/view state and have no additional persistent defaults.
  Layer Select's threshold, comment author, pressure curve and guide radius/colors already
  belong to user settings and are reused unchanged.
- The Comment tool's status filter is shared; search text, author/date queries, list
  layout and comment drafts remain view/document-local rather than tool defaults.
- Brush parameters use their existing GUID/local-file-ID references. File presets and
  clipboard tips keep their existing path/cache identity. Serialized tool preferences strip
  runtime Unity object references; missing inputs retain identity for later restoration.
  No texture pixels, materials or compiled programs are stored in EditorPrefs.

## Changes and multiple windows

`BindCanvasSettingsRow` installs the common typed ChangeEvent observer in trickle-down.
After control callbacks finish, one deferred Editor callback captures the final values.
Actions outside those rows, subtype pickers and keyboard paths call `SaveToolPreferences`.
No polling, serialization or disk writes run on cursor motion or idle editor frames.
Unchanged values do not write. Dispose, beforeAssemblyReload and quitting flush pending
changes before releasing owned brush resources.

Snapshots use JSON field diffs against each window's last local baseline. A stale window
changes only the fields it edited, preserving newer unrelated fields from another window.
Arrays such as gradient stops/curves are single values rather than index-merged sequences.
The same-field conflict policy is last committed edit wins. On focus an idle window first
flushes its local edits, then imports newer shared values and refreshes its controls.
Active creation/painting gestures defer that refresh. Merely closing an unchanged stale
window never rewrites shared settings. Existing window values without a stored preference
seed the registry, so installing this path does not discard in-memory tool choices.
The remembered basic tool/Transform return choice is startup-only: another window's tool
choice does not switch an existing window or terminate its editing context on focus.

Reset clears the shared tool store and existing tool keys, cancels pending writes and
restores fresh defaults in open sessions. It never deletes presets or edits documents.
Malformed sections fall back independently; valid other sections remain intact.

## Verification

`tool-preferences-v1` uses GUID-isolated EditorPrefs and unshown owned windows to check
close/recreate with exact snapshots of all 26 registered sections, cold session reads,
typed values, gradients/HDR/alpha, asset identity and missing external preset/cache inputs,
coalescing, nested multi-window merging, stale-close behavior, keyboard size changes,
session reset, malformed sections and exclusion of document/gesture state.
`user-settings-cleanup-v2` guards central registration and the common control observer.
The case does not restart the Editor. A real user-performed restart was separately
confirmed for [F26](../Context~/Backlog/TOOL_SETTINGS_PERSISTENCE.md#выполнено), with
restored windows checked against durable settings before running the cases again.
Physical pointer/tablet input, the Reset dialog and reload during active gestures
remain in [D02](../Context~/Backlog/VERIFICATION.md#настройки-инструментов-f26);
recreating a window is not presented as a real process restart.
