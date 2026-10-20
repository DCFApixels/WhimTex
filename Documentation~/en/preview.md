---
title: "Canvas View and navigation"
parent: "English"
nav_order: 10
lang: "en"
permalink: "/en/preview/"
translations: "en/preview.md,ru/preview.md,zh/preview.md"
previous_page: "en/shader-fx.md"
next_page: "en/color.md"
---

<a id="preview-and-navigation"></a>

# Canvas View and navigation

Canvas View is the whole panel, including its tool settings and footer. The canvas is the image area inside it: use it to paint, inspect details and check seams or individual channels.
Changing the view does not resize the document.

The two rows above the canvas contain document and tool settings. In a narrow panel, slider tracks hide while their number fields remain available. You can type a value or drag a numeric field's label; controls wrap to another line if needed.

The Canvas View footer holds controls for quality, live texture updates, post-processing,
UV overlays, guides and channel viewing. It also contains **HDR**, the color-input mode,
and hints for the active tool.

## Profiler

Background history writes appear as **worker** events with their own duration. Their time overlaps UI work and must not be added to the main-thread total. **input.canvas_ready** measures how long brush input waits for the updated canvas; **input.gpu_observed** includes waiting for GPU completion when available. It does not measure when the monitor displays the result. Several input events may share one canvas update.

In **Capture settings**, leave **Document ID** empty to record all documents; for one document, paste the returned
`profilerDocumentId` string unchanged.

To investigate a slow canvas update, open **Window → Analysis → WhimTex Profiler** and click
**Record** (replaces the previous stopped capture). Repeat the action in WhimTex, then click **Stop**;
results update automatically. Filter by **Layer** name or ID, or by **Phase**; confirm the field and select an event for details.
In **Groups**, select any combination of **UI & Input**, **Tools & Editing**, **Rendering**,
**Resources & Memory**, **Load & Save** and **Diagnostics**. Uncheck a group to hide it, use **All**
to restore every group or **None** to clear the selection; then check one group to isolate it. Counts reflect the
other filters before group selection; some groups may have no recorded events. The selection survives
Refresh and script reload. Summary and Events share this filter; muted ancestors provide context only.
Filtering does not change recorded timings or the complete JSON export. Colors identify groups,
with separate cache hit/miss and error colors. SDF, text, shapes and FX belong to Rendering.
Indents show nested stages; duration bars help
spot expensive work. Drag the dividers to give the summary, event list or details more room.
Click a stage's arrow to collapse or expand its children, or use Left/Right on the selected stage.
**Expand all** opens every branch; **Collapse all** leaves only root events across all pages.
Searching shows the path to each match; folding never changes the summary timings.
New **Events** captures list stages in start order, earliest first. A running stage keeps its place
and receives its duration when it finishes. **Summary** defaults to the largest total times first.
Click **Count**, **Total ms**, **Self ms**, **Max ms**, **Prep ms**, **GPU ms**, **Allocations** or **Self allocations** to sort; click again to reverse the arrow.
**Allocations** counts main-thread managed allocations including nested stages; **Self allocations** excludes nested stages. These are counts, not bytes.
A `*` marks a partial sum: hover to see measured/eligible calls. Sorting uses the measured sum without extrapolation; unavailable values show `—`, not zero.
Summary scrolls horizontally in narrow windows, keeping headers aligned with rows.
The choice survives Refresh and script reload. Unavailable values stay at the bottom in either direction; duration bars
always compare total time, regardless of sorting. Sorting Summary does not reorder Events.
Script recompilation automatically restores the capture and displays it again. Active recording stops
before reload; unfinished stages show **Interrupted** without a fabricated duration.
**Export Temp JSON** saves a report for analysis; export before starting another capture or closing
the Editor. **Clear** discards the stopped capture. Recording stops automatically at its event or time
limit. Status and changed results update automatically; **Refresh** also updates them immediately.
Automatic updates preserve selection, filters, sorting, folded branches and scroll position.

The capture also covers brush batches and pixel readback, selection changes and mask uploads,
transforms, canvas-frame edits, fill, Undo/Redo restoration, interface refresh, loading, saving and export.
Select **render.trigger** to see why the canvas was requested; **Occurrences** counts repeated requests
coalesced into this update. **cache.miss**, **cache.bypass**, **cache.release** and **cache.invalidate**
explain cache decisions. **Cache entry** identifies the affected entry; **Latest change** shows the last
recorded edit for that document, with its origin/layer when known. This is context, not proof that this
edit caused every downstream miss. These reason rows have no elapsed work time.
Brush wall timings include waits/submission; selected draw/composite batches also have separate GPU intervals.

For FX, expand **fx.block** to see each effect by name. **Prep ms** shows CPU preparation;
**GPU ms** shows the device interval for the block or effect. Details also show CPU command-submission time.
After Record, wait for **GPU ready** (or **GPU unconfirmed** with Graphics Jobs) before testing a one-shot action. GPU results arrive several frames later:
**…** means pending, **—** unavailable, **\*** a partial summary. They update automatically even after Stop; export becomes
available when pending queries finish. Wall times include waits; CPU/GPU and nested timings overlap and must
not be added together. See the [profiler reference](../AgentAPI.md#profiler-captures) for interpretation.
Zero or ambiguous native GPU timestamps show **—**, not zero-cost work. GPU intervals can include
idle time; small differences need repeated comparisons under the same conditions.
With active Graphics Jobs, GPU collection continues, but returned timings have a yellow background:
Unity does not support GPU profiling in this mode, so these results are unconfirmed. Hover for details;
the warning is not repeated beside each number. The header shows actual Graphics Jobs On/Off and a
restart notice if the configured setting differs. Captures and baselines retain their recorded mode.
For a supported comparison, disable Graphics Jobs, restart Unity and repeat. The profiler never changes project settings.
Synchronous pixel readback can truncate pending GPU timestamps. Affected rows show **—** with
`invalidated_by_readback` in Details; CPU timings and already completed GPU values remain available.

To compare two runs, stop the first capture and click **Set baseline**, then record and stop the second.
Choose **Allocations** or **Self allocations** in Comparison's **Metric** selector to compare main-thread
managed allocation counts with or without nested stages. Baseline, Current, **Δ alloc** and **Δ %** show
the before/after change; rows sort by the magnitude of that change, largest first. These are counts, not bytes.
Partial counts show `*` with measured/eligible calls in the tooltip. Their delta is `—` until both rows
have complete measurements; a zero baseline also leaves the percentage unavailable. Compare call counts as well.
Time and allocation decreases are green in Δ and Δ %, increases are red; zero and unavailable values remain neutral.
Call-count differences remain neutral. Signs are retained, and values rounded to zero are not colored.
Enable **Compare baseline** in the summary area. The baseline is independent: **Record** and **Clear** do not
discard it, and script reload restores both records. **Clear baseline** removes it separately.
The comparison shows before/after values and differences; check call counts and mean time as well as
totals. More repeated actions can increase total time without slowing each action down. Missing GPU
timings and added/removed stages show **—**, not a false improvement. Filters apply to both captures;
the single-capture operation filter does not apply to comparison. Use the same document, input,
quality and cache conditions when assessing a change. Export needed reports before closing Unity.

Open **Diagnostics & benchmarks** to inspect memory. The separate **Managed allocated · all Editor threads**
line shows total managed bytes allocated during the capture, including unrelated Editor work and
instrumentation. Document and display filters do not limit this total; it is allocation traffic, not
live heap size. Event details show main-thread allocation **counts**, inclusive and self, including
instrumentation; they do not show stage bytes. Inclusive counts overlap across nested stages.
Unavailable or recorder-limited measurements show **—**, not zero; recorder limits are marked.
The panel also shows tracked texture/native-cache payload and sampled Unity-wide memory for the whole
capture, unaffected by display filters. Texture payload is not physical VRAM, and returned RTs may
remain in Unity's pool. **Explain cause** follows observed property changes through cache dependencies;
missing evidence remains unknown, and tracking limits are marked.
GPU coverage also includes common composition/layer/transform passes, cache copies, Drawing and
output encoding; unavailable device intervals remain **—**.

For repeated measurements, supply **Document ID**, choose Cold/Warm/Both, repetitions, warmups and
maximum size, then click **Run**. This renders a separate snapshot without replacing the capture or
baseline. Results update automatically with median, p95 and range. **Cancel** retains partial results.
The interval includes synchronous GPU readback: it is wall time, not GPU-only time. Cold means fresh
owned document caches, not a restarted Unity/JIT/driver. Source textures/Drawing are frozen; fonts,
meshes and machine load remain shared. **Method & limitations** contains the full scope. Only the
latest benchmark is retained in this Editor session; no archive is created.

## Comments

Set your nickname in **WhimTex Settings → Comments → Nickname**. WhimTex first uses your personal
Unity organization name, preserving its original capitalization when available. If the organization
is unavailable, it uses the part of your Unity email before `@`. The result is remembered across
projects. You can enter your own; **↺** detects the name again in this order. If account
data is unavailable, new messages show **User** and detection retries when WhimTex opens or
you post. Changing the setting affects new messages only, including replies.

Choose **Comment** (**N**), directly below **Crop**, and click a point on the canvas. Type a note, then press
**Post** or **Ctrl+Enter** (**Cmd+Enter** on macOS). Enter adds a new line; **Esc** or
the close button hides the card and keeps the draft. **Cancel** discards it. Text, attachments and message edits
survive switching tools or discussions and script reloads in the same editor window. **Resume Draft** reopens
an unposted new comment. Until **Post** or **Save**, draft text and attachments stay local to the editor window and are invisible to agents. Notes appear as numbered pins while
Comment is selected, including on an empty canvas.
The tool icon is outlined when the document has no comments. Once a comment is posted,
it becomes filled, with transparent text lines; resolved comments count too.

New comments use your **WhimTex Settings → Comments → Color**. Until you choose a color,
WhimTex derives its hue from your nickname, keeping the saturation and brightness of the
original blue. **↺** beside the setting returns to this automatic color. Changing your color
also updates your automatic comments in open documents and when you reopen other documents.

The color swatch in the card header overrides the color for that discussion, before or after
posting; right-click the swatch and choose **Reset to Author Color** to restore the author's color.
Replies keep the discussion creator's color.
All older comments retain their colors as manual choices. Light colors use dark numbers for readability.
Each message shows a 5 px stripe to the left of its author's name. It follows that author's
color independently of the discussion marker, including in replies.

Click a pin to read its discussion or add a reply. Each message's **⋮** menu at the right edge offers **Copy Text**,
**Edit**, and **Delete Reply** for replies. Right-click anywhere on the author row to open the same menu.
Edited messages show **edited**; hover over the date for the edit time.
Messages posted on different local calendar days are separated by a muted date and thin lines. Editing a message does not change its day group.
The trash button, immediately to the left of the close button in the card header, removes the whole discussion after a **Cancel / Delete** confirmation beside the trash button.
The confirmation initially focuses **Cancel**; **Esc** or closing its window also cancels deletion.
**Cancel** keeps the discussion and current draft; **Undo** restores a deleted discussion and its messages.
Drag a pin to move it without opening its card; click to open it.
Beside the card number, press the status icon, move onto an option in the vertical menu and release to select it in one gesture.
You can also click to open the menu, then click an option. The status icons are
a half-filled circle (**In Progress**), a red circle (**Awaiting Review**) or a checkmark
(**Resolved**). The list and status filters use the same icons; hover to read their full names.
You can switch freely between all three states.
Awaiting Review adds a red dot beside the discussion number in the list, to that pin and to the filled Comment tool icon, even when a filter hides
the discussion. The status is saved with the document in TIFF. Reading, opening and replying do not
change it automatically; change the status explicitly when handing work over or accepting it.

You can open several discussions at once, each in its own card with a line to its marker.
A short, thicker accent follows the card outline where the line meets it, keeps its thickness around rounded corners, and extends slightly over the inside edge.
Its outer edge forms a short arrow with concave sides, adjusting its base around corners and shortening as the marker approaches the card. The arrow and line meet at the same width, without a crosswise outline at the junction. The thin line stays behind all cards; the arrow and contact accent sit directly above their own card and can be covered by cards stacked higher.
Clicking an already open discussion brings its card forward. Closing a card keeps the others open;
each has its own draft and attachments. Switching away from **Comment** hides its cards, pins and side panel;
returning restores the open cards. Script reloads retain this open set and changed drafts in the same window.
Filters temporarily hide cards together with their markers; clearing the filter restores them.
**Close All**, to the right of **Comments** in the list header, closes every card in the current document, including filtered ones.
Discussions, drafts and attachments stay intact. Click a pin or list entry to reopen a discussion;
use **Resume Draft** for an unposted new comment.
A new card with no text, attachments or manual settings is discarded when you click outside it.
Clicking another canvas point starts a fresh card there. Published discussions remain open.

Drag the card's header to move it and its edges or corners to resize it. The header background and title show the move cursor; buttons and other controls keep their own cursors. Tooltips stay hidden while moving or resizing the card. A line keeps it connected to
the canvas marker. Once moved, the card stays in place while you pan or zoom. Double-click its header
to restore automatic placement and size. Closing a card resets its position: reopening places it beside its marker.
The chosen size is retained.
Published message text is justified across the available width, with each paragraph's last line aligned left. While editing, text stays left-aligned.
Published discussions show a floating **+** at the bottom right of the message area, beside the scrollbar. It stays in place while scrolling and has no footer. While creating or editing a message, **+** is hidden; **Post**, **Save**, or **Cancel** brings it back.
Click it to add a draft message directly to the list;
**Edit** or a double-click on a message opens its text and attachments for editing in that message's own segment, with **Save / Cancel**.
New comments also start with an editable message. There is no separate input block below the list.
Switching messages or leaving the card cancels an unchanged edit without blocking; an empty new draft row disappears.
Changed text or attachments stay in the draft: save or cancel them before editing another message.
**Post** publishes a new message; **Save** finishes an edit; **Cancel** discards the draft. Closing the card preserves changed drafts,
which reopen in place. Empty new messages are not saved to the document.
Changing the card height resizes the message list. The text field grows as you type, then scrolls for long messages.
Editing automatically scrolls the list to show the input and its action buttons together whenever they fit; a taller editor keeps the action buttons in view.
Comment numbers are permanent: deleting a discussion never renumbers the remaining ones.

The side panel and canvas share **Open / In Progress / Awaiting Review / Resolved / All** and search.
**Open** includes both In Progress and Awaiting Review. Search matches all message text,
authors and comment numbers. The header shows the open count; **Draft** marks unfinished replies or edits.
Off-canvas discussions remain available in the list. Each row has a thick left stripe matching its marker color. A slightly lighter,
subtly outlined header shows the comment number; thin lines separate the rows. Scroll the list and
click a row to open its discussion. **Comments (open count)** or the arrow at the panel edge
collapses or reopens the panel.
The panel fits its contents up to the available canvas height; the scrollbar appears only
when the list no longer fits.

Use **◎ — Show on Canvas** in the discussion header to center and briefly highlight its marker.
The current zoom and rotation stay unchanged, even for comments left outside the canvas after cropping.
Your current reply or message edit stays in place.
Double-click a row in the full Comments list to make the same jump directly; a single click only opens it.

Expand **Filters & Sort** to choose an author (including reply authors), a creation/activity period,
and sort by number, creation time, last activity or original author, in either direction.
**Custom dates** accepts inclusive local dates in `yyyy-MM-dd` format; leave either bound empty.
Last activity means the latest reply or message edit. Filters apply to both the list and canvas pins.
Nearby closed pins combine into a stacked count as they overlap on screen. Click the count to choose
a discussion; zooming in separates distinct anchors. Open cards keep their own visible pins.

Use **Copy Link** in a row's context menu or the card's ↗ button. Paste it into the Comments search
field to find that discussion in the current document. Search matches parts of message text, authors
and numbers without case sensitivity; a discussion's own link must match in full, including case,
ignoring only surrounding whitespace. Other filters still apply.
Paste a link onto the canvas to open it directly. Links included in message text get an
**Open linked comment** button. Navigation reveals
the exact discussion even if the current filters hide it. Saved-document links survive asset renames;
save new discussions before sharing. Unsaved-document links are temporary and expire with their
window session. These links open inside WhimTex; external applications do not launch Unity with them.

You can ask an agent using **whimtex-live** to address all open comments or specific numbers.
It reads each discussion and its location, checks the edited image, then replies in the same thread.
It can also leave new notes at specific canvas positions, with layers or assets attached as context.
After verifying the result, the agent explicitly sets **Awaiting Review** with its reply, or **Resolved**
if you asked it to close the discussion. Partial work stays **In Progress**, with the remaining work described.
New comments are
not automatically added to work already in progress; changed feedback must be reread before delivery.

Attachments can be added below a message while composing it or using **Edit**. Click **+** for an
empty field, or drop layers from this document and assets from the Project window onto a field,
the **+**, or the attachment block. Multiple dragged objects create multiple fields.
A blue outline marks a valid drop target. Post/Save
commits the links; empty fields are omitted. Published links can be clicked to ping their targets.
Layer ping reveals the row and briefly highlights it without changing selection. Asset links follow
renames and moves within the project; deleted targets remain visibly missing until the message is edited.

Use **Attach Selection** to attach a snapshot of the current nonempty selection, including soft
edges and holes. Its **Selection · W × H** row shows a miniature mask. Click the selection field or its miniature to
toggle a blue canvas preview without changing your working selection; **Use** restores the mask
as the working selection. While composing or using **Edit**, **↻** replaces it with the current
selection and **×** removes it; Post/Save commits these changes. Later selection edits do not change
the attachment. Masks follow canvas Crop, Scale, flips and rotations, preserving cropped-away
areas for later expansion. They are saved with comments in TIFF and do not appear in exported
images. An agent can read a mask, use its exact region for an edit and attach masks to its replies.

For approximate selections, an agent can allow a small soft margin around the marked area while
preserving pixels outside it. It can compare the candidate with the original and attach a mask of
the visible changes to its reply. If Unity reloads scripts during generation, the agent can resume
while the original document remains open and its source is unchanged.

Opening a discussion scrolls to its latest message, or to the message being edited when restoring a draft.
You can scroll upward to read earlier replies.

Posted comments are saved with the TIFF document and follow canvas cropping, scaling,
flips and rotations. They do not appear in the exported image. Comments are local notes
in the document; posting does not send notifications to other people.

<a id="layer-mini-preview"></a>

## Layer Preview

Drag the **Layer Preview** strip at the bottom of **Layer Settings** upward to inspect the selected layer; drag it down to hide the image. The separate **Properties** window has the same Layer Preview and opens with it visible. The block stays below the scrolling settings and is at most 256 px high, including the strip, or less if space is limited. Its chosen height stays unchanged when switching layers or image resolutions. Small images enlarge proportionally to fit; large images shrink without cropping.

The Layer Preview shows the layer before blending with other layers. A group shows its own colored content against transparency.

The compact, neutral **R / G / B / A** buttons toggle channels independently, like the Canvas View footer. One RGB channel is displayed in grayscale; **A** controls transparency. Enable only **A** to inspect alpha in grayscale, or turn all four off to show black. Transparent areas use a checkerboard. This selection belongs to the Layer Preview only: it does not change the Canvas View, painting, the layer or saved output.

In Canvas View or Layer Preview, hold the left mouse button and drag across **R / G / B / A** to switch several channels together.

## Context tools

Tools below the toolbar separator appear when they are relevant. Only selecting a Gradient layer automatically selects **Gradient Handles** (the hand icon), except while creating a gradient or using Crop or Comment. For Shape, Vector and Boolean Group gradients, select the hand button manually. Manually activated handles remain selected when switching between layers with an editable fill or stroke gradient in the same document, including Gradient layers. Selecting a layer without an editable gradient or choosing another tool ends this behavior. Automatically activated handles return to the last basic tool when you select a non-Gradient layer. You can also switch back to a basic tool manually; it stays selected until you activate another layer. With multiple layers selected, context tools use the active, last-selected layer.

Enabling **UV** adds **UV Island Select** without switching tools. **Edit on Canvas** on an FX parameter adds one temporary hand tool at the bottom. Its tooltip and the Canvas View footer identify the parameter; editing another parameter replaces this tool.

Click the active context-tool button or press `Escape` to return to your last basic tool. Leaving a temporary FX tool returns to the tool used before it, including an available context tool. During a drag, `Escape` cancels only that drag; press it again to leave the tool. Changing inspector values, opening a color picker or switching applications does not leave the tool.

## Mesh UV overlay

Enable **UV** in the footer and assign a **Mesh** to see its UV island outlines over the image. The UV panel lets you choose the channel, submesh, line color and opacity. The outlines follow zoom, pan and view rotation and never appear in exports. To paint a specific part of the model, use [UV Island selection](selection.md#select-uv-islands).

The **Mesh** field also accepts a model or GameObject containing a mesh; if it contains several meshes, WhimTex uses the first one it finds.

<a href="{{ '/Images/uv-rubik-cube.png' | relative_url }}"><img src="{{ '/Images/uv-rubik-cube.png' | relative_url }}" alt="Painting a Rubik's cube texture in WhimTex with its UV outlines visible and the textured cube beside it in Unity Scene view" width="720"></a>

Use the UV outlines to place details on the right faces. With [Live Update](saving.md) enabled, you can see brush edits on the model in Scene view as you paint.

## Image filtering

**Canvas → Filter** controls image sampling in WhimTex: **Point** keeps pixels crisp,
**Bilinear** smooths pixel transitions, and **Trilinear** blends mip levels when available.
It does not generate mipmaps. Pencil temporarily uses Point and full preview quality.

For the saved TIFF in a material or sprite, set **Filter Mode** and mipmaps in its Unity Inspector.
PNG/JPEG/EXR files do not store a sampling mode for other applications.

## Move around

- The top and left rulers show pixels, with divisions that adapt to zoom and a marker for the pointer. Zero follows the canvas's top-left corner; on a rotated view, distances follow the current view axes.
- Drag the thin scrollbars on the right or bottom to pan. Click an end arrow for a small step; hold it to keep moving. Click the track to move by a page, or use the wheel over a scrollbar to pan along its axis. You can scroll beyond the edges even when the whole canvas fits in the view: the thumb shrinks as you move farther away and grows again on your return.
- Hold the mouse wheel and drag to pan.
- Hold `Shift` and drag with the mouse wheel pressed to rotate the view with any tool. Rotation lightly snaps to 90° steps and to angles that make a guide horizontal or vertical on screen. The closest angle wins; hold `Ctrl` to bypass snapping. Hidden guides and guides with **Snap to Guides** turned off do not add magnetic angles.
- Scroll to zoom around the pointer with any tool.
- With **Zoom** (`Z`), click to zoom in, `Alt`-click to zoom out, or drag a rectangle around the area you want to inspect.
- **Fit** shows the whole canvas upright; **100%** is useful for checking pixel detail.
- Enter a precise percentage in **Zoom %**, then press Enter or leave the field. The center of the view and its rotation stay in place.
- Enter an exact angle in **Zoom → Angle °** and press Enter (or leave the field) to apply it without snapping. **0°** resets rotation without changing zoom or pan.

View rotation does not rotate the layers or affect saving and export.

**Tiled** is a toggle button in the Canvas View footer, beside UV and Guides. Enable it to see repeated copies
and work across the edges: painting, erasing, filling and selections wrap to the opposite side.
The saved image size and layer transforms remain unchanged.
See [seamless painting](symmetry.md).

## Crop, extend or scale the canvas

Choose **Crop** (`C`). Hold its toolbar button or drag it by 3 UI pixels to choose **Scale** or **Resize**, then release over the icon. You can also choose the mode in the tool settings. The button's icon and tooltip show the current mode; switching tools and returning with `C` or `Q` keeps that choice.

**Resize** changes the canvas bounds while keeping the image's pixel size. Drag a corner or side handle to resize the frame, drag inside it to move it, or drag outside it to draw a new frame. The frame can extend beyond the current canvas. **X**, **Y**, **W** and **H** give exact placement: X and Y measure the frame's top-left corner from the current canvas's top-left corner, with negative positions extending it left or upward.

**Scale** resizes the whole image to new canvas dimensions, including every layer, the area selection and guides. Drag the frame's corner or side handles to preview the result, or enter **W** and **H**. The frame's position only anchors the preview; its width and height determine the applied image size. X and Y are hidden, and dragging inside or outside the frame does not move it or create a new frame. Layers and their source pixels remain editable.

Hold `Shift` while dragging a corner to preserve the frame's proportions; hold `Ctrl` to bypass snapping. Press **Apply** or `Enter` to apply the current mode. The frame is a preview until you apply it. **Reset** or `Escape` returns it to the current canvas dimensions; during a drag, `Escape` cancels that drag first. Changing modes resets the preview. Panning and zooming keep it in place.

The **Flip Canvas Horizontal**, **Flip Canvas Vertical**, **Rotate Canvas Left 90°** and **Rotate Canvas Right 90°** icons in Crop affect the whole document immediately, including unselected layers. The rotation buttons turn it by 90° and swap the canvas width and height. The crop frame, area selection and guides follow the image. These actions are unavailable during a frame drag; their tooltips identify each icon. Use **Transform** for the corresponding actions on selected layers.

In Resize mode, cropping hides content outside the new canvas without deleting layer pixels. Extending it reveals existing content outside its old bounds, with transparency where no layer contributes. Canvas-dependent procedural patterns and FX recalculate for the new bounds, so their appearance can change.

The frame snaps to canvas edges and center, guides and guide intersections, and the current area selection's bounds. Selected layers also attract it by the combined bounds of their nontransparent content after FX, without counting their own Opacity or Visibility. These bounds follow the outermost pixels with nonzero alpha, leaving out transparent margins. The shared **Snap Radius (px)** controls attraction distance; hidden guides and guides with snapping disabled do not attract the frame.

**Pixel Scale** sets how many canvas pixels occupy one logical pixel; its default is **1**. It scales pixel-sized strokes, text, brushes and effect distances while keeping their entered values unchanged. For example, a width of 3 occupies 6 canvas pixels when Pixel Scale is 2. Canvas dimensions, texture pixels and the screen's snapping radius keep their own pixel units.

In **Scale**, **Auto** is enabled by default. It multiplies the applied Pixel Scale by the smaller width or height scale factor, for proportional and nonproportional changes alike: doubling the width and tripling the height doubles pixel sizes. Repeated draft edits always start from the applied document. Turn Auto off to enter a multiplier manually; **Resize** also allows manual entry. Pixel Scale and frame changes stay pending until **Apply**. **Reset** or idle `Escape` restores both to the applied document without changing Auto; after Apply, that result becomes the new reset point.

## Guides

### Create and edit guides

Drag from the left ruler of Canvas View to create a vertical guide, or from the top ruler for a horizontal one.
Drag from the ruler intersection to create both lines together. Cancelling or releasing outside Canvas View discards both; Undo/Redo also treats the pair as one action.
The new line is parallel to the ruler even on a rotated canvas; afterwards it moves, zooms and rotates with the canvas.

Drag an existing line to reposition it. Drop it back on either ruler or outside Canvas View to remove it; `Esc` cancels the drag.
You can create guides from the rulers with any tool. Move existing guides with Layer Select, Zoom, or Transform outside its frame; temporary FX Edit on Canvas tools also support guides away from their handles. Transform handles and the inside of its frame take priority, including for multiple layers and temporary FX transforms; Point and Normal handles also take priority. Hold `Ctrl` to bypass the guide and use the active tool. Brushes, Pencil, Fill, Shape, Crop and area-selection tools ignore existing guides. The lines stay visible and draw below all tool handles.
Click a guide to select it: arrow keys nudge it, `Shift` increases the step tenfold, and `Delete` removes it. Click elsewhere or press `Esc` to deselect. Double-click a guide to enter an exact **Position (px)** and **Angle (°)**. Angles are relative to the canvas: 0° is horizontal, 90° is vertical. For those two orientations, Position is the distance from the top or left edge; at other angles it is the signed perpendicular distance from the top-left corner.

Right-click a guide to **Edit**, **Duplicate** or **Delete** it. Right-click either ruler for the shared controls:

- **Show Guides** — hide or show the lines. The **Guides** button next to **UV** in the Canvas View footer does the same. Hidden guides do not attract tools; creating a new guide shows them again.
- **Lock Guides** — protect existing lines from editing. You can still create new guides and snap tools to locked ones.
- **Snap to Guides** — toggle snapping; hold `Ctrl` to bypass it temporarily.
- **Undo Guide Change / Redo Guide Change** — undo or redo guide edits without affecting painting.
- **Clear Guides** — remove all guides.

### Snap tools to guides

Moving or resizing a layer (including a Shader FX Transform 2D area) snaps its edges to parallel guides. Moving also snaps its center lines. Edges ignore oblique guides; rotating a transform can align it parallel or perpendicular to a guide. `Shift` retains its usual rotation and resize constraints.

Nearby guide intersections take priority over individual lines, with the same snap radius regardless of zoom. They attract the brush/pencil center, selection and shape points, the pivot, and a transform's center, corners and edge midpoints. When movement is constrained, only intersections on the allowed path attract the point. Holding `Ctrl` bypasses snapping.

The pivot and polygonal lasso vertices can also snap to a single guide at any angle. Rectangle selection snaps to individual lines only when they are parallel to the canvas axes; intersections work at any angle.

### Paint along a guide

**Brush** and **Pencil** also snap their stroke center to guides at any angle and to intersections, including while erasing. The cursor shows the snapped position. If you start a stroke snapped to a guide, holding `Shift` keeps it on that same line no matter how far the pointer moves away. Release `Shift` for ordinary nearby snapping; press it again during the same stroke to return to the original guide. At an intersection, the guide closest to the starting pointer is used. If you start away from guides, `Shift` keeps its usual horizontal or vertical screen direction. Hold `Ctrl` to bypass guide snapping and locking. Pencil strokes still follow the pixel grid; brush Scatter is applied after snapping the stroke path.

### Position a guide with snapping

Dragging a guide snaps it to parallel canvas edges, the canvas center, and the selected layer's parallel edges and center lines, but not to other guides. It can also snap to guide intersections, unless a guide parallel to the dragged one passes through that intersection. Hold `Ctrl` for free placement.

Guides are not exported and are cleared when switching documents.

### Appearance and snap distance

Guides that are horizontal or vertical in the current Canvas View appear bright cyan; angled guides use a softer blue-gray. The colors update as you rotate the view, without changing snapping.

In **User Settings → Guides & Snapping**, choose colors for **Aligned Guides**, **Angled Guides** and the **Active Guide** (hovered, selected or dragged). A guide about to be deleted stays red.
**Snap Radius (px)** sets the attraction distance for guides, intersections, canvas edges and pivot anchors: 1–64 UI pixels, default 8, independent of zoom. Angular snapping is unchanged. These preferences apply across windows and are saved between sessions; **Reset Guides & Snapping** restores their defaults.

## Balance detail and responsiveness

**Live Quality** defaults to **100%**. Your manually chosen value is remembered.
Lower it in the footer if painting on a large image feels slow.
Drag the **Live Quality** value horizontally, or double-click it to enter an exact percentage; `Enter`, `Tab` or leaving the field confirms it, and `Escape` cancels.
Save and export still use full resolution.

Canvas View adjusts detail to zoom and the visible area. At 100% zoom or closer, the visible fragment uses source pixels instead of enlarging a 512-pixel preview. Adding a layer FX such as HSV keeps this detail, including text on other layers. **Live Quality** can reduce detail during a stroke; releasing the stroke restores normal view quality.

The same visible-area preview works during Brush/Pencil strokes, including soft selections and HDR; panning while painting reveals the retained offscreen stroke.
Effects that need pixels outside the view may take more time to update, but do not reduce the detail of the whole canvas. **Post FX**, **Tiled** and crop-frame previews retain the complete reduced preview. In these modes select **Pencil** without painting for full-resolution preview, and enable **Tiled** when checking seams.
Patch Quilting can select different patches at reduced resolution; verify this full-resolution view before export.

## See your paint on a model

1. Save the document and assign its texture to your model's material.
2. Turn on **Live Update** (the circle button) in the Canvas View footer, next to **Post FX**.
3. Paint or adjust layers: objects using that texture update in Scene View.

<a href="{{ '/Images/uv-rubik-cube.png' | relative_url }}"><img src="{{ '/Images/uv-rubik-cube.png' | relative_url }}" alt="Live Update in WhimTex: white lettering painted across the cube's texture appears on the model in Unity Scene view" width="720"></a>

The white lettering crosses two faces in the texture; Live Update lets you check how it wraps around the cube while painting.

The material keeps its texture reference when you toggle Live Update or close WhimTex.
Turning it off or closing without saving restores the last saved image. Press **Save** or `Ctrl+S`
to keep your edits; **Save As** creates a different asset and does not redirect existing materials.

Live Update shows the composition without EV, channel-display masks, Debug or preview Post FX.
While editing it uses preview quality, then refines the result when you stop.
If you resize the canvas, the live image fits the saved texture size until you save again.
Live Update starts off when you open the window and turns off when you switch documents or reload scripts.

### Use a document inside another document

Assign the saved document's output texture to a **File** layer in another document.
Saving the source refreshes the receiving window automatically. To see changes while editing, enable
**Live Update in the source window**. The receiving window does not need Live Update enabled just to display them.
Turning Live Update off restores the saved image in the receiving window too.

Only one TIFF document can publish Live Update at a time. A receiving window can display the
source's live changes, but save each intermediate document to pass a chain of results onwards.
**Save As** creates a new asset; existing File layers keep referencing the original.
Automatic refresh across cyclic links, such as A using B while B uses A, is disabled to prevent feedback loops.

## Check brightness and channels

**EV** changes the viewing exposure, not the image itself. Keep it at **0** for the normal view.
If white looks gray or colors look overexposed, check this field first.

**R / G / B / A** lets you inspect the color channels or transparency separately.
See [color and channels](color.md) for the display modes.

> Channel buttons also affect new paint: disabled color channels receive zero,
> and turning off A prevents Brush, Pencil and Fill from leaving a mark.
> Turn all four channels on for ordinary painting.

## Background and problem pixels

Change the transparency checkerboard's colors and size in
**Window tab ⋮ → User Settings…**.

The bug button highlights pixels with invalid color values. Use it to investigate a broken-looking
effect; its highlight color is also in User Settings. The overlay is not included in saved images.

Zoom, EV and [Post FX](post-fx.md) change only the view. **Tiled** also affects painting by wrapping strokes across canvas edges.
