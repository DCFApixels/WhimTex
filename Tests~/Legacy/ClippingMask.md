# Clipping masks: verification

`ClippingMaskSmoke.cs` is an opt-in live-Editor evaluation script, to run only after the user
has compiled manually and authorized transient test objects. It does not save/import assets.

It covers shared-base resolution, soft alpha, chain opacity, all blend modes, clipped Overwrite,
hidden/transparent/missing bases, group boundaries and isolation, effect input coverage,
Swizzle/Transform coverage, Drawing conversion, serialization defaults, HDR, agent setting
validation and partial/full merge rendering. `PsdWriter/Program.cs` checks the native clipping
byte on each exported layer record in its standalone writer test.

Manual UI checks:

1. Create a Drawing base with a soft edge; put two colored layers above it. Select both and
   toggle **Clipping Mask** in the context menu. Both get `↳` markers and use the Drawing base.
2. Hide the base, then undo: the complete chain disappears and returns. Toggle clipping via
   `Alt`-click at a row boundary; selection must stay unchanged and no drag should start.
3. Move the base or put the chain into a folder. Check nearest-sibling linking and group boundaries.
4. Use a group as the base and as a clipped member. Check the isolation hint and active range fields.
5. Convert a clipped layer/group to Drawing in both modes, then undo. The flag and visible result
   should survive; source pixels must not have the external mask baked twice.
6. Save/reopen a scratch document, duplicate its chain and export PSD. Verify flags, folder structure
   and the exported merged image. Use copies rather than existing work for round-trip checks.

No Unity compilation, reimport or live GPU tests were launched while adding these checks.
