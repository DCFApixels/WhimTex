# Node catalog thumbnails

Internal static catalog artwork, separate from the [operation icon pool](../Icons/README.md).
`Catalog.png` and `Catalog.json` are generated together. Both catalogs share the same
texture; the manifest maps node kind names to atlas UV rectangles. Missing entries use
operation icons. No graph preparation, compilation, materials or RenderTextures are
created by catalog browsing. The only persistent change is the user display preference;
it has a separate notification so switching display does not invalidate graph previews.

## Rebuilding

With `WHIMTEX_DEEP_DEBUG`, use **Window > WhimTex > Debug > Rebuild Node Thumbnails**.
The explicit editor operation evaluates one program at a time on the Editor thread.
Procedural examples use SVM; image examples use the native HLSL preview renderer,
including the node's actual intermediate passes. Compilation is polled across updates.
It starts from `ShaderGraphNodes.Create`, then applies the catalog-only recipes below,
with 2D coordinates, a 64x64 canvas, pixel scale one, seed 1337 and time zero.
Blue Noise tables are bound to the owned material. Warp outputs feed a Grid UV input.
Unsupported programs, invalid pixels or constant rendered examples fail generation.
Symbols and diagrams for data, channels, matrices, arithmetic, lighting and graph
utilities are drawn directly into the atlas.
No document is changed.

## Noise examples

Recipes emphasize features at the actual 32px display size. They do not change factory
defaults, runtime algorithms or graph previews. All noise examples use Value; Warp uses
Position to deform a five-cell grid, with line width 0.15. The grid shows coordinate
deformation instead of treating a vector as a grayscale value.

| Kind | Catalog settings | Fixed display black / white |
| --- | --- | --- |
| Perlin | Scale 4 | 0.15 / 0.85 |
| Value | Scale 5 | 0 / 1 |
| Value Cubic | Scale 5 | 0.25 / 0.75 |
| Open Simplex 2, Open Simplex 2S | Scale 3 | 0.05 / 0.95 |
| Flow | Scale 3, Phase 1.4 | 0.1 / 0.9 |
| Cellular | Scale 4, default F1 | 0 / 0.6 |
| Gabor | Scale 4, Frequency 1, Direction (0.8, 0.6) | 0.05 / 0.95 |
| Spots | Scale 4 | 0 / 1 |
| Shard | Scale 4 | 0.1 / 0.9 |
| Triangle Voronoi | Scale 4, Evolution 0.7 | 0 / 0.35 |
| Caustics Noise | Scale 5, default Simplex | 0 / 1 |
| Erosion Noise | Scale 0.2, UV offset (5.09, 0.08), default noise controls. Levels: Input 0.41/1, Gamma 0.38, Output 0/11.67, Clamp Input enabled | 0 / 1 |
| White, Blue | Grain Size 4 | 0 / 1 |
| All three Warp kinds | Scale 3, Strength 0.07 | 0 / 1 |

Fixed levels retain gradients without per-image auto-normalization. The smaller range
of Cellular and Triangle Voronoi is expanded from zero; applying centered contrast to
these distance outputs would erase much of their pattern. Flow uses a nonzero phase
because its phase-zero example coincides with Open Simplex 2S. Related gradient-noise
families can still resemble one another in a static image.

Images are 64x64, displayed at 32x32; two extruded edge pixels prevent atlas bleeding.
Linear output RGB is remapped by the fixed levels, clamped and encoded to sRGB; alpha is opaque. The atlas
uses no mipmaps, rescaling, compression or readable CPU copy. Recipe changes or algorithm
changes require an explicit rebuild. Browsing never checks source timestamps or bakes
missing previews. Coverage includes built-in noise kinds, Caustics Noise, Shape,
Checkerboard, Stripes, Grid, Rings, Gradient Factor, Truchet, all current Filter and UV
categories, Sample Texture, twenty Color operations and all thirteen Height/Normal nodes. External HLSL and Sub Graphs use icons. This is one
representative example per kind, not every mode or output.

## Filter and UV examples

`ShaderGraphCatalogThumbnailBaker.Images.cs` constructs a 256x256 linear reference:
a cyan gradient arrow with a straight tail and thin inner outline, an upper-right
white ring, a reflected warm wedge, a four-cell grid and a grayscale ramp along the
bottom. Filters process this image; UV nodes feed the coordinates of another
lookup of it.

Within Filter/UV examples, height, normal, slope and flow inputs use a separate smooth periodic height map,
`0.5 + 0.22 sin(6πx) sin(4πy) + 0.12 cos(4πx + 6πy)`. This keeps sharp color edges
from dominating the displacement. Caustics evaluates this height map directly.
All examples still execute the actual node algorithms.

Catalog-only settings make the effect visible at 32px:

| Kind | Example settings |
| --- | --- |
| Blur | Radius 3 |
| Sharpen | Radius 2, Strength 2, ordinary mode |
| Dilate, Erode, Median | Radius 2 |
| Directional Blur | Radius 7, Angle 35 |
| Radial Blur | Spin, Angle 30 |
| RGB Shift | Directional, Amount 3, Angle 15 |
| Slope Blur | Smooth auxiliary height as slope, Strength 5 |
| Bilateral Blur | Radius 3, Tolerance 0.2 |
| Kuwahara | Classic, Radius 3 |
| Supersample | 4x4 |
| Make Tileable | Blend Width 0.45 |
| Fade Transition | Reference R, Fade Value 0.6, Fade Contrast 4 |
| Caustics | Smooth auxiliary height; Strength 0.015, Dispersion 0, 128x128 buffer, Value output |
| Transform UV | Rotation 25, Scale 0.85 |
| Apply Transform2D | Position (0.5, 0.5), Size (0.8, 0.65), Rotation 20; Local output |
| Pixelate UV | Cell Size 5 |
| Repeat, Tile UV | Two repeats per axis; Tile UV uses Staggered |
| Mirror, Kaleidoscope | Both axes; five sectors, respectively |
| Twirl, Bulge / Pinch | Strength 140/360 turns and 0.7, respectively |
| Wave, Ripple | Strength 0.08 / 0.07, Frequency 3 / 4 |
| Radial Shear | Strength 3 |
| Perspective UV | Top Left (0.23, 0.92), Top Right (0.77, 0.8) |
| Flipbook | 2x2, Frame 2 |
| Parallax Mapping, Parallax Occlusion Mapping | Smooth auxiliary height, Amplitude 0.3 |
| Height / Normal to Distortion, Vector Displacement, Flow Warp | Smooth auxiliary height derivative at Strength 5; normal displacement Strength 0.12, Flow Warp Strength 20 |

Other controls retain creation defaults. Texture lookups repeat. Image examples use
fixed display levels 0/1. Some related operations remain visually similar at small sizes;
the image indicates their action, while the name identifies the node.

The 208-entry atlas is 544x1768 (about 3.67 MiB of uncompressed GPU texels). Browsing
still loads one texture and creates no render targets. The baker releases its temporary
color/height/relief/arrow references, document, programs and render targets on success, failure or assembly reload,
and restores the caller's render target and sRGB-write state.

## Color examples

`ShaderGraphCatalogThumbnailBaker.Color.cs` constructs a separate 256x256 linear
texture from a color table: hue runs left to right, lightness runs from dark
through saturated colors to pale colors, and a grayscale ramp fills the bottom.
The source is composed in display RGB and decoded to linear before node evaluation.
The table fills the same 32x32 display area as the other catalog images.

Twenty-one adjustment, conversion, mapping, selection and mixing nodes evaluate the table through
their actual HLSL. The following settings affect only catalog examples:

| Kind | Example settings |
| --- | --- |
| Hue | Offset 1/6 turn |
| Saturation | Amount 0.25 |
| Exposure | Stops 1 |
| Lightness | Amount 25 |
| Contrast | Contrast 1.8 |
| Levels | Input Range 0.1/0.85, Gamma 1.4; Luminance mode |
| Posterize | Levels 4 |
| White Balance | Temperature 0.4, Tint -0.15 |
| Color Balance | Shadows (-0.06, 0, 0.1), Highlights (0.15, 0.04, -0.06) |
| Channel Mixer | Red from G, Green from B, Blue from R |
| Luminance | Default weights |
| Hue Range | Range 11/12–1/12 turns, Feather 1/24 turn |
| Color Mask | Green key, Range 0.3, Fuzziness 0.25; Perceptual distance |
| Replace Color | Green to pink (1, 0.1, 0.5), Range 0.3, Fuzziness 0.25; Perceptual distance |
| Dither | Levels 3, Pixel Size 2 |
| Gradient Map | Dark blue → red-orange → pale yellow |
| Palette Quantize | Same palette, 5 colors; Perceptual distance |
| Color Curves | Smooth Master curve through (0,0), (0.25,0.1), (0.75,0.9), (1,1) |
| Mix Color | Table mixed halfway with (0.05, 0.15, 0.8), default color model |
| Blend | Table background, foreground (0.8, 0.2, 0.04), Opacity 0.5, default blend mode |
| Convert Color | Source color table above, Linear RGB → HSV below, with H, S and V displayed as R, G and B |

Premultiply, Unpremultiply and Set Alpha use the separate soft disc example below.
Blackbody uses the dedicated temperature ramp described below.
The baker releases the color-table source together with its other references.

## Height and Normal examples

`ShaderGraphCatalogThumbnailBaker.Relief.cs` builds a shared 256x256 linear height
texture: a smooth upper-left mound, a lower-right plateau with a bevel, and a
diagonal recessed groove. These distinguish rounded slopes, convex edges and concave
edges at 32px. A quarter-turn copy supplies the second input for blending.
The existing Filter/UV auxiliary height and their thumbnails stay unchanged.

All thirteen Height/Normal nodes evaluate actual HLSL. Normal inputs come from
Height to Normal at Strength 8. Signed XYZ is displayed directly, with no implicit
packing; only the Normal Encode recipe converts to packed colors. Decode and Unpack
receive an explicitly encoded normal; Reconstruct Z receives signed XY. Conversion
results can look alike because they recover the same reference normal.

| Kind | Catalog-only settings / output |
| --- | --- |
| Height Blend | Relief and its quarter-turn copy; Height output |
| Height Curvature | Radius 2, Strength 12; Convex output |
| Height to AO | Radius 12, Height Scale 20, Directions 16, Steps 6 |
| Height Shadow | Height Scale 24, Distance 32; default diagonal light |
| Height to Normal | Strength 8; Signed XYZ |
| Normal Blend | Reoriented; rotated detail normal at Strength 5 |
| Normal Strength | Strength 2 |
| Normal Curvature | Radius 2, Strength 3; Convex output |
| Normal Encode | Signed source normal to packed RGB |
| Normal Decode | Explicitly encoded reference to Signed XYZ |
| Normal Reconstruct Z | Signed XY from the reference normal |
| Normal Unpack | Object mode, explicitly encoded reference |
| Normal Lighting | Local light, gray surface 0.45; source normal Strength 24 |

Texture lookups clamp, display levels remain 0/1, and the atlas remains opaque.
These recipes do not change node creation defaults, algorithms or graph previews.

## Pattern and math examples

`ShaderGraphCatalogThumbnailBaker.Patterns.cs` evaluates native HLSL examples.
All use fixed display levels 0/1 and opaque atlas output. They distinguish
repetition, scattering, range selection and transfer functions at 32px:

| Kind | Catalog-only input and settings |
| --- | --- |
| Fractal | Repeated ellipse, Size 0.7, Feather 0.015; FBm, 3 octaves, Lacunarity 2, Gain 0.65, Seed Step 0 |
| Scatter | Isolated cyan arrow; Count (3,3), Size 0.95, Size Random 0.35, Rotation Random 130/360 turns, Jitter 0.65, Density 0.8 |
| Range Mask | Linear 0..1 ramp; Center 0.5, Width 0.5, Feather 0.12, Smooth falloff |
| Step | Linear 0..1 ramp; Edge 0.5 |
| Smoothstep | Linear 0..1 ramp; Min 1/32, Max 31/32 |
| One Minus | Shared Filter/UV reference image |
| Power | Linear 0..1 ramp raised to 3 |
| Absolute | Linear -1..1 ramp; V-shaped output |
| Square Root | Linear 0..1 ramp |
| Exponential | Base E on a 0..3 ramp; output 1..e³ remapped to 0..1 |
| Log | Base 2 on a 1/64..1 ramp; output -6..0 remapped to 0..1 |
| Soft Sign, Hyperbolic Tangent | Linear -4..4 and -2..2 ramps, respectively; signed output remapped to 0..1 |
| Inverse Soft Sign | Linear -0.8..0.8 ramp; output -4..4 remapped to 0..1 |
| Sine, Cosine | One period from 0 to 2π radians with a zero guide |
| Fresnel Effect | Analytic sphere normals, view direction +Z and Power 1; masked to the sphere silhouette |
| Distance | Distance in UV from (0.5,0.5), normalized to one at the corners |
| DDX, DDY, FWidth | Shared shape with vertical, horizontal and diagonal edges; absolute response displayed with 3.5× gain |
| Premultiply, Unpremultiply | Same soft cyan disc; Unpremultiply receives premultiplied input. The swatch shows raw RGB, revealing a checker near zero coverage. |
| Set Alpha | Same cyan disc with a soft mask fading toward the left in Replace mode, composited over checkerboard |
| Reflection, Refract | Incident vector (0.8, -0.6, 0) meets a horizontal interface, normal (0, 1, 0). Refract uses IOR 1 → 1.5. The outgoing cyan arrow follows the node output. |
| Blackbody | Temperature ramp 800..14000 K; the node retains its normal temperature clamp |
| Noise Sine Wave | Three periods (phase 0..3 turns), Min Max -0.3/0.3; signed -1.3..1.3 remapped to 0..1 |
| Sawtooth Wave, Square Wave, Triangle Wave | Three periods (phase 0..3); signed -1..1 remapped to 0..1 |
| Sphere Mask | UV coordinates, Center (0.5,0.5), Radius 0.22, Hardness 0.8 |
| Combine Distance | Union of ellipse at (0.36,0.61), Size 0.48, and rectangle at (0.63,0.39), Size (0.46,0.42); Feather 0.01, Mask output |
| Sample Gradient | Linear 0..1 input; opaque dark blue → cyan → red-orange → yellow gradient |
| Sample Curve | Linear 0..1 input; smooth curve through (0,0.05), (0.25,0.95), (0.65,0.12), (1,0.8) |

The arrow stamp reuses the reference image's polygon and has a zero background.
Wave recipes use explicit Remap nodes so their negative half remains visible.
Curve and gradient resources are owned by each temporary graph.
Sample Curve, Power, Smoothstep, Triangle Wave, Range Mask, Step, Sawtooth Wave,
Square Wave, Absolute, Saturate, Square Root, Exponential, Log, Soft Sign,
Hyperbolic Tangent, Inverse Soft Sign, Sine and Cosine include a thin green curve
over their grayscale result. The five signed examples mark zero output with a muted horizontal line.
The baker samples the same HLSL at 256 horizontal positions
and draws an antialiased line using the numeric values before display gamma. Smoothstep
has exactly one display pixel of horizontal plateau at each edge of the 32px image;
its transition follows the cubic function. Final curve pixels are also extruded into
the atlas padding. Step and the discontinuous waves use vertical jump markers
instead of diagonal interpolation across the jump. Antialiasing affects the line
coverage, not the sampled function. The overlay is baked, with no drawing work while browsing.
All other controls retain creation defaults. The recipes are baked during the explicit
rebuild operation; displaying them adds no shader preparation or GPU evaluation.

## Coordinates, vectors, logic and transfer functions

The additional recipes use native graph nodes or draw static symbols and diagrams:

| Kinds | Catalog recipe |
| --- | --- |
| UV | Red/green coordinates inside the scaled and rotated layer rectangle, over a muted canvas grid. |
| Canvas Position | Normalized canvas coordinates fill the tile as a red/green gradient. |
| Transform | A scaled and rotated layer over a canvas grid; maps a point from the layer to the canvas. |
| Sample Input, Sample Texture | The same mountains and sun as Texture Size, cropped to fill the tile. |
| Scatter Instance | The arrow stamp tinted by each instance's Random output inside Scatter. |
| Random, Random Range | A shared six-cell seed grid with a zero hash seed. Random Range uses 0.28..0.59. |
| Light Attenuation | A point light in the temporary document. The scalar falloff is tinted warm for display. |
| Arctangent2 | The angle of centered UV is mapped to color. The discontinuity is visible on the negative X axis. |
| Projection, Rejection, Rotate About Axis, Normalize, Dot Product, Cross Product | Input vectors, guides and the evaluated result share a diagram. Dot Product displays signed alignment over all directions. |
| Compare, Select, Switch | Two compared fields form a mask, Select uses it to pick a pattern, and Switch shows three conditions plus the Fallback. |
| And, Or, Not | Circle masks show overlap, union and the region outside one circle. |
| All, Any | Three circular component masks show triple intersection or union, respectively. |
| Is NaN, Is Infinite | Centered NaN and ∞. The baker rasterizes their strokes directly without evaluating a shader. |
| Constant, Value | Centered π and 1.0 in the same symbol style; no shader evaluation. |
| Add, Subtract, Multiply, Divide | Large arithmetic signs: +, −, × and ÷. |
| Split, Combine, Swizzle, Append | Colored components separate, merge, reorder or join in sequence. |
| Matrix Construction, Matrix Split, Matrix Transpose, Matrix Determinant | Rows assemble into a matrix, one row is extracted, a row becomes a column, and a square changes area under a linear transform. |
| Length, Convert Angle | A measured vector and one angle shown with degree and radian units. |
| Canvas Data, Texture Size | Dimensioned canvas grid and dimensioned image. |
| Instance Index, Time | Numbered instances and a clock. |
| Light Count, Light Direction, Light Color | Three sources, the direction of a ray and a colored light cone. |
| Buffer | A picture beside its storage cylinder. |
| Expression, Custom Function | A formula and code brackets. External HLSL functions use compact icons. |
| For Begin, For End | A circular arrow between curly braces. Begin highlights the opening brace and End highlights the closing brace. |
| Lerp, Inverse Lerp | A funnel links a narrow black-to-white T ramp to a wider cyan-to-amber ramp. Inverse Lerp swaps the top and bottom. |
| Remap | A skewed funnel links offset intervals on two differently colored ramps. |
| Clamp, Saturate | One shared sine period, `0.5 + 0.75 sin(2πx)`. Clamp clips at 0.22/0.78; Saturate at 0/1, with a full green display pixel along the image edges. |
| Minimum, Maximum, Negate, Sign | Fixed scalar ramps with marked reference lines. Minimum and Maximum use two crossed ramps. |
| Floor, Ceiling, Round, Truncate | Shared input −2.5..2.5 and output −3..3 with zero and identity guides. |
| Fraction, Modulo | Shared signed input and a divisor of one for Modulo. Their jumps retain vertical markers. |
| Tangent, Arcsine, Arccosine, Arctangent | A half-turn for Tangent and valid finite sample ranges for the inverse functions. The Tangent stroke ends where it exits the chart at the pole. |
| Hyperbolic Sine, Hyperbolic Cosine | Shared input −2..2 and output −4..4 with a zero guide. |
| Reciprocal, Reciprocal Square Root | Shared positive input 0.4..3 and output 0..2.5. |

The point light and layer transform belong to the bake fixture.
Catalog browsing uses the same static atlas in both panels.
