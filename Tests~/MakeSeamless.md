# Make Seamless and layer presentation checks

Offline checks (do not launch Unity or request compilation):

```sh
node --test --experimental-test-isolation=none Tests~/MakeSeamless.test.mjs Tests~/LayerPresentation.test.mjs
```

The numerical checks execute the shader's scalar weight function and compare edge/corner results,
axis order, identity, alpha-aware mixing and HDR values. Source contracts cover registration,
settings, material cleanup, Properties titles and ghost lifecycle. They do not compile C#/HLSL
or establish visual correctness.

After **manual** compilation, the opt-in `MakeSeamlessSmoke.cs` can run in the intended Editor
through the project's existing C# execution bridge. It creates transient objects, renders actual
GPU previews, reads opposite edges, checks hidden-source input and serialization, then cleans up.
It does not import, save or change the active document. Do not run automatically.

`EdgeSelectorSmoke.cs` runs through Pipeline `run_script`, entry `EdgeSelectorSmoke.Run`.
It checks the shared Noise/Make Seamless selector in a temporary window: linked and independent
hover groups, hover cleanup, all four pair states across the five Poisson/Quilting controls,
all 16 copy-edge masks, all nine Mirror direction combinations, center activation and one change
transaction per click. Verified: 731 checks. Mirror center turns both axes off if either is active;
from both off it enables Left To Right and Bottom To Top. Other centers invert each selected edge/pair.

Manual UI checks:

1. Select each layer type and a group. Properties includes the type name from the add menu;
   unavailable sections remain disabled. Check the standalone Properties window too.
2. Drag a nested layer by its thumbnail, name, opacity and eye; drag a group by its foldout.
   The translucent ghost keeps the thumbnail/name indentation and the original grab offset.
   Name is plain text, background fades towards the right, and drop indicators remain usable.
3. Scroll while dragging; cancel with Escape, drop outside the list, leave the window, reopen it.
   No stale ghost should remain. Focused text editing and horizontal text selection still work.
4. Add Make Seamless above a texture. Enable Tiled; try each direction and Off on each axis.
   Adjust Fade Width and Falloff, check all four corners. Test a hidden source and a hidden
   group containing visible children. Check Undo/Redo and saved-document reopening.
5. Check transparent edges and HDR sources with matching HDR color ranges. Applying transforms
   after Make Seamless is allowed but can change the edge match.
6. Click each edge of the icon above Horizontal/Vertical. The highlighted edge is the destination:
   left = RightToLeft, right = LeftToRight, top = BottomToTop, bottom = TopToBottom.
   Repeat a click to turn the axis Off, or click the opposite edge to switch. The other axis
   stays unchanged. Dropdown changes, Undo/Redo and API changes update the highlight in both
   the main inspector and standalone Properties window. Check keyboard focus/Space as well.
