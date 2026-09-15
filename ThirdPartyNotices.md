# Third-party notices

WhimTex's own MIT license does not replace the licenses of third-party components.
FastNoiseLite is bundled as source. The documentation theme is resolved separately at site build time.
The Unity packages listed below are resolved by Package
Manager and are not vendored into this repository; their installed packages supply their
own licenses and any transitive third-party notices.

## FastNoiseLite

WhimTex includes the HLSL implementation of
[FastNoiseLite v1.1.1](https://github.com/Auburn/FastNoiseLite/tree/v1.1.1),
commit `7ccfbc16eb1c932568f177d63a9ba51d89bbe516`.

File: `src/Shaders/ThirdParty/FastNoiseLite.hlsl`.

Local modification: an include guard prevents duplicate declarations when the built-in library
is also included explicitly. Noise algorithms are unchanged.
The separate Noise shader adapts its output and parameters to WhimTex.

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
