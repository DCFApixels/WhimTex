# Third-party notices

WhimTex's own MIT license does not replace the licenses of third-party components.
FastNoiseLite, the Clipper2 clipping core, Typography.OpenFont and an adapted edtaa3 distance transform are bundled as source.
The documentation theme is resolved separately at site build time.
Paint history includes a portable C#/Burst adaptation of the LZ4 block algorithm.
The Unity packages listed below are resolved by Package
Manager and are not vendored into this repository; their installed packages supply their
own licenses and any transitive third-party notices.

## LZ4

`src/Layers/PaintHistoryCodec.cs` adapts the independent-block format, fast hash search,
match extension and length encoding from [LZ4 v1.9.4, lib/lz4.c](https://github.com/lz4/lz4/blob/v1.9.4/lib/lz4.c).
The bounded C#/Burst adaptation uses Unity's portable memory APIs, a checked decoder and
per-worker reusable buffers. It includes no native library, hardware-specific intrinsics,
stream dictionaries, frame container or byte shuffling. Native LZ4 1.9.1 from an installed
Unity package is used only as an explicitly authorized test oracle, not a product dependency.

Copyright (C) 2011-2020, Yann Collet.

BSD 2-Clause License

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE
OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
OF THE POSSIBILITY OF SUCH DAMAGE.

## Typography.OpenFont

WhimTex includes managed OpenFont sources from
[LayoutFarm/Typography](https://github.com/LayoutFarm/Typography/tree/5877180c7c5271091379a0eaf9f03ab6ebd256b3),
commit `5877180c7c5271091379a0eaf9f03ab6ebd256b3`, under
`src/ThirdParty/Typography/`. The import follows the upstream OpenFont `.projitems`
and omits the TrueType hinting interpreter and its exception types. It includes
no GlyphLayout, renderer, native binaries, or decompression libraries.

The retained sources credit WinterDev, Samuel Carlsson, Michael Popoloski,
Sam Hocevar, Villu Ruusmann, the Apache PDFBox/FontBox authors, the FreeType team,
and Adobe Systems Incorporated. Their original headers and inherited notices
are preserved. WhimTex is based in part on the work of the FreeType Team
([FreeType Project](https://www.freetype.org/)) through the managed glyph translator;
no native FreeType library is bundled.

The [license and notice index](src/ThirdParty/Typography/NOTICE.md) includes
Apache-2.0, MIT, the FreeType Project License, Adobe glyph-list BSD terms, and
the inherited PDFBox/FontBox/PaDaF notices. The upstream repository's umbrella
MIT description does not replace these per-file licenses.

Local changes add `Typeface.ContainsTable(string)` for format safety checks,
make the 20 unimplemented CFF Type 2 operators throw `NotSupportedException`,
suppress one automatic DEBUG file dump, and replace DEBUG debugger breaks with
diagnostic messages. Unused WOFF2 transform flags, a glyph layout flag and an exception
binding are removed without changing stream reads or parsing. See the [integration notes](src/ThirdParty/Typography/README.md)
for exact scope and limitations, and the
[source manifest](src/ThirdParty/Typography/SOURCE-MANIFEST.json) for file hashes.

## Cascadia Mono

`src/Editor/Fonts/CascadiaMono` bundles the unmodified static TrueType Regular and
Bold faces from [Cascadia Code v2407.24](https://github.com/microsoft/cascadia-code/releases/tag/v2407.24),
archive `CascadiaCode-2407.24.zip`, entries `ttf/static/CascadiaMono-Regular.ttf`
and `ttf/static/CascadiaMono-Bold.ttf`. They are used only for code-area text and
line numbers. Unity-generated dynamic TextCore assets reference these local sources;
no system font installation is required.

Copyright (c) 2019 - Present, Microsoft Corporation, with Reserved Font Name Cascadia Code.
The fonts remain under the [SIL Open Font License 1.1](src/Editor/Fonts/CascadiaMono/LICENSE.txt),
included verbatim from the upstream release tag. WhimTex's MIT license does not replace it.

SHA-256 of the unchanged upstream font files:

- Regular: `06520d032ec274fa5040b22c6f4a1d829081b24ba40b2da56dae89bf10c7b481`.
- Bold: `b22cb603ed23cac36e8444846e1841caca21719a5e852780a8d37ae7a49b0a36`.

## Adobe font test fixtures

`Tests~/Fixtures/Fonts` contains two unmodified Adobe OTF files as test inputs,
not installed fonts or Unity-imported Font assets:

- [Source Serif Pro Regular 3.000R](https://github.com/adobe-fonts/source-serif/tree/fb33c384290783887810dcfcc25132817509cf9f),
  commit `fb33c384290783887810dcfcc25132817509cf9f`, supplies a static CFF1 input
  without variation, SVG, COLR or bitmap tables. Its exact upstream
  [license and copyright notice](Tests~/Fixtures/Fonts/SourceSerifPro-LICENSE.md)
  are included unchanged: Copyright 2014-2019 Adobe, reserved font name `Source`.
- [Source Code Pro Regular 2.030R](https://github.com/adobe-fonts/source-code-pro/tree/482adcaccf9777f2850974e08c60e706b1ad9169),
  release `2.030R-ro/1.050R-it`, commit `482adcaccf9777f2850974e08c60e706b1ad9169`,
  supplies CFF1 outlines and an original SVG table for conservative adapter
  rejection checks. Its exact upstream
  [license and copyright notice](Tests~/Fixtures/Fonts/SourceCodePro-LICENSE.txt)
  are included unchanged: Copyright 2010, 2012 Adobe Systems Incorporated,
  reserved font name `Source`.

Both fonts remain under SIL Open Font License 1.1. The embedded designer
credits are Frank Grießhammer (Source Serif Pro), and Paul D. Hunt and
Teo Tuominen (Source Code Pro). The copies came from the Typography checkout
pinned above, and their bytes were independently matched by SHA-256 to the
original Adobe release binaries. See the [fixture provenance and hashes](Tests~/Fixtures/Fonts/manifest.json)
and [coverage notes](Tests~/Fixtures/Fonts/README.md). No font tables or names
were modified, and WhimTex's MIT license does not replace the font licenses.

## Caustics Noise

The OpenSimplex2S ImproveXY derivative kernel used by `WTNoiseCaustics` is adapted
from KdotJPG's **Common** source in [Shadertoy wlc3zr](https://www.shadertoy.com/view/wlc3zr).
Source version: the user-supplied snapshot of 9 November 2026; no upstream revision ID
was supplied. The Common source is identified as **CC0 1.0 Universal**; this notice
does not assign that license to other tabs or assets on the page.

File: `src/Shaders/WTNoiseCaustics.hlsl`. The HLSL adaptation retains the signed
ImproveXY value/derivative kernel and follows its full XYZ gradient, with short steps
and path averaging at larger strengths. WhimTex adds Seed, WT domain/fractal/footprint
integration and a 578-unit Evolution loop. Positive XY periods repeat corner hashes on
both cubic halves of an axis-aligned BCC lattice instead of blending translated fields.
Local distances and analytic derivatives are preserved; periodic orientation changes.
The animation loop fits 501 temporal lattice units, with centered coordinates and a
separate integer half-lattice hash offset to reduce precision loss. Nonperiodic ImproveXY
keeps the reference pattern. The public `WTNoiseCaustics` call retains its raw
`0.5 + 0.5 * value` mapping and strength default of 0.625.

License: [CC0 1.0 Universal — legal code](https://creativecommons.org/publicdomain/zero/1.0/legalcode.en).
KdotJPG's upstream credit, Stefan Gustavson's permutation-polynomial credit and the CC0 designation are retained in the source header.

The **Simplex** algorithm in the same file ports the user-supplied snapshot of
10 November 2026, referencing [Shadertoy Ws23RD](https://www.shadertoy.com/view/Ws23RD)
(no revision ID supplied). Its kernel derives from [webgl-noise](https://github.com/ashima/webgl-noise),
by Ashima Arts and Stefan Gustavson, under MIT. The complete upstream copyright
and permission notice is retained in `src/Shaders/WTNoiseCaustics.hlsl`. It preserves
the supplied 0.6 kernel radius, scale 42, omitted gradient normalization and fixed -6
direction-field coefficient; it is not the current upstream kernel or an exact scalar
derivative. WhimTex adds seeded periodic corner hashing and footprint filtering. Periodic
Simplex rotates the lattice orthogonally to admit integer axis periods, wraps corner
identities and rotates its direction field back to input space. It preserves the local
kernel without translated-field blending or a density multiplier.

The single built-in Caustics Noise node selects OpenSimplex2S or Simplex. Its calibration
and complete 0–1 profile use a separate internal entry shared by HLSL and SVM;
the public WT API is unchanged. The temporary `src/ShaderGraphCausticsTest.hlsl`
catalog file is removed. Existing Custom Functions retain their embedded source and
license notice. The separate buffered Caustics operator is unchanged.

## FastNoiseLite

WhimTex includes the HLSL implementation of
[FastNoiseLite v1.1.1](https://github.com/Auburn/FastNoiseLite/tree/v1.1.1),
commit `7ccfbc16eb1c932568f177d63a9ba51d89bbe516`.

File: `src/Shaders/ThirdParty/FastNoiseLite.hlsl`.

Local modifications: an include guard prevents duplicate declarations; optional lattice hash hooks
support periodic sampling in WhimTex's Noise variants and the WT noise library. The WT hook
is active only during WT evaluation; direct FNL calls retain their original hash arithmetic.
Both paths use branch hints and allow both iterations of the 3D OpenSimplex2 BCC-grid loop,
instead of the bundled single-iteration unroll hint. The two-grid 3D warp loop has an
explicit two-iteration unroll hint, retaining its existing calculations. Gradient lookup is factored into shared
vector helpers; the original scalar dot products use those helpers, and WT Flow can rotate
the vectors during its evaluation. The simplex/OpenSimplex2/2S kernels accumulate analytic
spatial derivatives alongside their existing scalar terms; scalar overloads call the same
kernels and discard the derivative. Gradient tables, attenuation weights and scalar normalization
constants are retained. The separate WT adapter differentiates grid interpolation and handles
input-space transforms, filtered octaves and weighted-fractal derivatives.
The shared 3D gradient helper maps the repeating table to 16 vector entries instead
of three scalar lookups. All 64 directions, their hash mapping and phase rotation
are unchanged; the original table remains for direct users.
The shared 2D helper similarly uses 48 scalar entries with an exact remapping of all
128 original direction pairs, including the final eight exceptions. Scalar component
lookup and phase rotation are retained; the original full table remains available.
WT-active ValueCubic 2D/3D uses `FNL_DOUBLE_PRIME` shifts for the `+2` Y/Z neighbor offsets
to retain arithmetic modulo `2^32` where a compiler saturates an overflowing signed constant
product. This corrects WT lattice neighbors and can change those WT images; when the WT
layout is inactive, the original multiply expressions remain in use.
When the WT include is active in Unity, direct `fnlGetNoise3D` OpenSimplex2 calls retain
the previous one-grid result produced by `[unroll(1)]`: an explicit break applies when
`WTNoiseInternalLayout == 0`. WT entry points install a nonzero layout and evaluate both grids.
Direct include users without these opt-in flags retain the upstream hashes and hints.
The existing Noise shader and the separate `src/Shaders/WTNoise*.hlsl` API supply their own
periodic transforms, per-octave lattice periods, parameter handling and output mapping.
WT FixedRadius Cellular also uses the bundled random-direction tables. The public WT API
does not change this dependency's license or replace its attribution.
Comment-only `@wt.block` annotations select individual kernels, helpers and tables with
explicit dependencies. Every selected block retains the original MIT notice through
`FNL_License`; these annotations leave ordinary HLSL inclusion and calculations unchanged.
Warp dispatch and kernel feature branches also select dimension and full/reduced
gradient output for generated graph helpers; ordinary dispatch and arithmetic are retained.

MIT License

Copyright(c) 2023 Jordan Peck (jordan.me2@gmail.com)

Copyright(c) 2023 Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files(the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions :

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Clipper2

WhimTex includes the managed C# clipping core of
[Clipper2 source release 2.0.1](https://github.com/AngusJohnson/Clipper2/tree/Clipper2_2.0.1),
commit `21ebba05db8894f0c7217ad35ea518080f324946`.
The upstream project metadata at this source tag reports 2.0.0; the pinned source tag
and commit above identify the included implementation.

Files: `src/ThirdParty/Clipper2/Clipper.Core.cs`, `Clipper.Engine.cs`, `PooledList.cs`,
and a reduced `Clipper.cs` helper facade. No native binary, offset module or NuGet
dependency is included. It normalizes filled SDF contour regions before join generation.

Local modifications: namespace isolation under `DCFApixels.WhimTex.ThirdParty`,
explicit 2D compilation via `#undef USINGZ`, a minimal helper facade and a local
deterministic hash combiner instead of the upstream compatibility shim. Nullable
annotations are explicitly enabled in `PooledList.cs` for the Unity compilation context. The
clipping engine and predicates are otherwise unchanged. Copyright headers are retained.

Copyright Angus Johnson 2010–2025. Distributed under the Boost Software License 1.0;
the complete [license text](src/ThirdParty/Clipper2/LICENSE.txt) is included unchanged.

## edtaa3

WhimTex's experimental **AAEDT + 8SED** mode for SDF and Outline adapts Stefan Gustavson's
anti-aliased Euclidean sweep-and-update distance transform, `edtaa3`.
The reference is [edtaa3func.c in rougier/freetype-gl](https://github.com/rougier/freetype-gl/blob/ecf48bc72b9ef7d493a2491b9adcaa1504d01252/edtaa3func.c),
pinned to commit `ecf48bc72b9ef7d493a2491b9adcaa1504d01252` (27 October 2016).
The upstream header credits Ingemar Ragnemalm's SSED8 algorithm and Gustavson's C implementation,
including the anti-aliased extension.

File: `src/Layers/AaedtDistanceField.cs`.

Local modifications: a portable C#/Burst job ports the gradient calculation, `edgedf` edge-distance
metric and sweep/update flow. It uses single-precision values, integer nearest-pixel indices,
clamped gradient sampling at image borders and a tighter local update tolerance (0.0001 instead
of 0.001 pixel). This tolerance is not a global distance-error bound. A monotonic piecewise coverage remap maps the selected
byte threshold to 0.5 while retaining 0/1 endpoints; thresholds 0 and 255 use binary classification.
WhimTex integrates the scalar field with the shared SDF/Outline contract. No freetype-gl native
binary or runtime dependency is included. The input is assumed to be area coverage of a crisp edge;
blur, feather and arbitrary opacity do not provide exact geometry. The adaptation estimates the
raster boundary and does not add sharp/miter extensions.

The complete license from the pinned source follows:

```text
Copyright (C) 2009-2012 Stefan Gustavson (stefan.gustavson@gmail.com)
The code in this file is distributed under the MIT license:

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## Spring Simulation research reference

The experimental Spring Simulation distance mode draws on ideas from Marko Matusovic,
Amal Dev Parakkat and Elmar Eisemann,
[Interactive Depixelization of Pixel Art through Spring Simulation](https://doi.org/10.1111/cgf.14743),
Computer Graphics Forum 42(2), 51–60 (2023).

This is research attribution for an automatic adaptation: spring relaxation initializes a raster
contour before WhimTex's AA-tested corner fitting and scalar distance output. The paper concerns
interactive depixelization, not SDF generation. The mode does not reproduce the full interactive
tool or its C++ implementation. This citation is not a bundled third-party code license.

## RBF Your SDF research reference

The experimental RBF distance mode is an independent, bounded 2D adaptation of
Yong Cheng and Yotam Gingold,
[RBF Your SDF: Radial Basis Function Interpolation of Signed Distance Fields with Implied Tangent Points](https://arxiv.org/abs/2609.06209v3),
version 3, 15 September 2026. The paper is available under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

WhimTex uses cubic RBF interpolation, implied tangent circles and projected tangent refinement.
Local modifications use raster-derived initial distances, fixed overlapping 2D patches with
per-patch refinement, and half-pixel zero-contour extraction followed by WhimTex's experimental
unrestricted sharp-offset stage. That stage fits flanks and normalizes regions without
re-rasterizing the recovered contour, then omits angle/support caps, nearest-boundary ownership
and miter beveling. Extra rays and overlaps are possible. Numeric degeneracy and contour-validity
checks remain. This is not an exact port or a Euclidean
distance field. No source from the authors' code
repositories is bundled; this section attributes the research, not an upstream code license.

## Sobol direction numbers

The brush sampler in `src/BrushDynamics.cs` uses the first seven dimensions of
Joe–Kuo's `new-joe-kuo-6.21201` direction-number set (16 September 2010 revision).
The sampler is implemented locally; no external runtime library is required.

Source: [Frances Kuo and Stephen Joe, Sobol sequence generator](https://web.maths.unsw.edu.au/~fkuo/sobol/).
The direction numbers are covered by the following upstream license:

-----------------------------------------------------------------------------
Licence pertaining to sobol.cc and the accompanying sets of direction numbers

-----------------------------------------------------------------------------
Copyright (c) 2008, Frances Y. Kuo and Stephen Joe
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.

    * Neither the names of the copyright holders nor the names of the
      University of New South Wales and the University of Waikato
      and its contributors may be used to endorse or promote products derived
      from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY
EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDERS BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## Newtonsoft.Json

[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json), by James Newton-King and
contributors, is used through `com.unity.nuget.newtonsoft-json` 3.2.2, which supplies
Newtonsoft.Json 13.0.2. It provides JSON serialization for documents and the agent API.

The package's complete [third-party license notices](Documentation~/Licenses/Newtonsoft-ThirdPartyNotices.md)
are included unchanged. They cover Newtonsoft.Json and the Unity integration components
Json.Net.Unity3D, Newtonsoft.Json-for-Unity, and com.newtonsoft.json under MIT licenses.

The Unity package wrapper's license notice is reproduced below:

Nuget.Newtonsoft.Json copyright © 2022 Unity Technologies ApS

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.

## Unity packages

- [Burst](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html)
  (`com.unity.burst`, declared dependency 1.8.25) supplies optimized native CPU compilation.
- [Collections](https://docs.unity3d.com/Packages/com.unity.collections@2.5/manual/index.html)
  (`com.unity.collections`, declared dependency 2.5.1) supplies native collections.

The resolved versions depend on the host Unity project. Use the license and notices supplied
with that installed package or Editor version; these packages are not relicensed as MIT.
In newer Editors Burst may be supplied by the Editor through a shim package.

The Collections license notice supplied with the development project's package is reproduced below:

com.unity.collections copyright © 2024 Unity Technologies

Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/unity_companion_license).

Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.

## Documentation website

The website uses [Just the Docs 0.12.0](https://github.com/just-the-docs/just-the-docs/tree/v0.12.0),
copyright Patrick Marsceill and contributors, under the MIT license.
The original [license text](Documentation~/Licenses/JustTheDocs-LICENSE.txt) is included unchanged.
The theme is installed from RubyGems during documentation builds, not bundled into the Unity runtime.
Generated pages use its layouts, styles and search assets; their original license headers are retained.

The build uses [Jekyll](https://github.com/jekyll/jekyll) (MIT),
[jekyll-relative-links](https://github.com/benbalter/jekyll-relative-links) (MIT),
and [WEBrick](https://github.com/ruby/webrick) (BSD-2-Clause).
Direct and transitive versions are recorded in `Documentation~/Gemfile.lock`; resolved gems supply
their own licenses. These are documentation tooling dependencies, not Unity package dependencies.

## Gradient easing table

The 17-value easing table used by Smooth interpolation is adapted from PhotoCraft.
Copyright (c) 2026 ArtCraft Team and the PhotoCraft contributors.
License: MIT; the complete notice is retained in
[GradientEasing-MIT.txt](Documentation~/Licenses/GradientEasing-MIT.txt).
The table was supplied in source revision `769f663de5f86abec5decec593c027f2f4e16c21`;
its sampled values are retained in the portable CPU implementation. The supplying
project is not a runtime dependency or a persistent test oracle.
