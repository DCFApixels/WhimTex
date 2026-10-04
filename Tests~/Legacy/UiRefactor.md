# UI refactor checks

Run `node --test --experimental-test-isolation=none Tests~/*.test.mjs` without opening or compiling Unity.

`UssCascadeBaseline.json` records the reviewed current cascade for the WhimTex editor styling. Refresh it only when a deliberate UI style change is reviewed, so accidental selector or property drift remains detectable.
`UssCascadeSnapshot.mjs` expands the new shared palette and groups consecutive identical values;
grouping identical rules is allowed, but values, selectors and conflicting order are protected.
The two direct Target classes are mapped back to their former ancestor selectors; source checks
require that those classes are attached to the same popup children, with unchanged specificity.
This is a conservative source check, not a Unity style resolver or a pixel comparison.
When intentionally changing the design, review the expected change before updating the baseline.

After the user manually compiles, check both editor skins, the main window and standalone
Properties windows. Compare normal/hover/pressed/disabled states, channel buttons, foldouts,
Target drop highlighting, and compact preview footer. Colors and dimensions should be unchanged.

Check Target options after rename, insert, delete, reorder, group/ungroup, effect input changes,
clipping changes, agent edits and Undo/Redo, including edits from a second window. Cyclic targets
must remain unavailable. Ordinary inspector refreshes should reuse the option list; document
changes, hierarchy changes and forced refreshes invalidate it. Dropdown and text focus are preserved.

Profile in Unity before making frame-time claims. This pass removes repeated option-list work,
per-candidate visited-set allocations and redundant HelpBox visibility writes; it does not change
rendering, Undo storage, or the UI tree.
