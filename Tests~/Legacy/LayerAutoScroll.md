# Layer drag auto-scroll

Run `node Tests~/LayerAutoScroll.test.mjs` for scalar/source checks without Unity compilation.
This validates edge bands, speed/direction, short viewports, outside positions and lifecycle wiring;
it does not simulate native editor drag events.

After manual compilation, use an existing document with enough layers to scroll:

1. Drag a single layer near either edge of the Layers viewport; stop moving the mouse.
   Scrolling continues, speeds up nearer the edge, and stops at the list limits.
2. Repeat with multiple selected layers, groups and drags starting on name/opacity/eye controls.
3. While scrolling, confirm the insertion marker follows the row now under the cursor.
   Drop into a group and between rows; verify the indicated destination is used.
4. Move into the middle, onto the footer/header or out of the window: scrolling stops.
5. Cancel with Escape, drop on a footer action, switch documents, close/reopen the window.
   No background scrolling should remain. A later drag should work again.
6. A short list with no scrollbar must not move. Project texture drops retain their existing behavior.
