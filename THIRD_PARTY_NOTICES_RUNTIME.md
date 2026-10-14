# Third-party notices: runtime components and translated code

This file adds to two others and replaces neither: `THIRD_PARTY_NOTICES.md` (third-party work that nth adds) and Droppy
Code's own notices in `src/Marrow.App/Legal/THIRD_PARTY_NOTICES.md`. Each component below stays under its own license.
The GNU AGPL v3.0 that covers nth as a whole does not change those licenses.

## 1. Code translated into C#

### Swift project (swift-foundation, swift-corelibs-foundation and the Swift standard library)

Copyright (c) Apple Inc. and the Swift project authors (the years are given in each source file of the Swift project).
Licensed under the Apache License, Version 2.0, with the Runtime Library Exception: https://swift.org/LICENSE.txt. The
full text of the Apache License 2.0 is in `THIRD_PARTY_NOTICES.md`, section "Apache License 2.0 (OpenAI Whisper
large-v3 model files)".

Parts of these nth files are translations into C# of Swift project source code, changed for nth so that they run on
.NET and Windows: `src/Marrow.Core/Port/SwiftURL.cs`, `URLComponents.cs`, `SwiftDifference.cs`, `SwiftFormatters.cs`,
`SwiftString.cs`, `SwiftJson.cs`, `FileOps.cs`, `Http.cs` and `IcuNative.cs`. Comments in each file name the Swift
source files and lines that it follows. The Runtime Library Exception covers compiled Swift programs; nth uses this code
under the Apache License 2.0 itself.

swift-foundation's NOTICE.txt credits Base64 code by Fabian Fett, by Wojciech Mula, Alfred Klomp and Daniel Lemire, and
by Nick Galbreath, and logic and data derived from Unicode's ICU and CLDR projects. nth does not translate the Base64
code. Where code that nth translated is itself derived from ICU or CLDR, the Unicode License v3 (Copyright (c) Unicode,
Inc.; https://www.unicode.org/license.txt) also applies to that part.

### SwiftTerm (the terminal)

nth's terminal view (`src/Marrow.App/Views/Terminal/Port/`) transcribes tables and behaviour from SwiftTerm 1.20.0 into
C#: the colour palette, Unicode character widths, mouse reporting and selection. The SwiftTerm notice in
`src/Marrow.App/Legal/THIRD_PARTY_NOTICES.md` (MIT License; Copyright (c) 2019-2026 Miguel de Icaza, the xterm.js
authors, SourceLair Private Company and Christopher Jeffrey) applies to those parts; `TerminalPalette.cs` and
`TerminalUnicode.cs` also carry it in their headers.

### SwiftShader

`src/Marrow.UI/Theme/Wallpapers/WallpaperBranchArithmetic.cs` contains code from SwiftShader (ShaderCore.cpp).
Copyright 2016 The SwiftShader Authors. Apache License 2.0 (text in `THIRD_PARTY_NOTICES.md`).

### Hash without Sine

Several wallpaper shaders use the constants and steps of David Hoskins' "Hash without Sine", Copyright (c) 2014 David
Hoskins, MIT License (text in section 4).

## 2. Runtime libraries shipped with nth

### Avalonia

Copyright 2013-2026 (c) The AvaloniaUI Project. MIT License (section 4). Covers Avalonia, Avalonia.Desktop,
Avalonia.Win32, Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Fonts.Inter, Avalonia.Remote.Protocol and, where the build
copies them, Avalonia.FreeDesktop, Avalonia.X11 and Avalonia.Native. Avalonia.Controls.WebView is listed in
`THIRD_PARTY_NOTICES.md`.

### ANGLE (Avalonia.Angle.Windows.Natives)

```
Copyright 2018 The ANGLE Project Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

    Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

    Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following
    disclaimer in the documentation and/or other materials provided
    with the distribution.

    Neither the name of TransGaming Inc., Google Inc., 3DLabs Inc.
    Ltd., nor the names of their contributors may be used to endorse
    or promote products derived from this software without specific
    prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

### SkiaSharp and HarfBuzzSharp

Copyright (c) 2015-2016 Xamarin, Inc. Copyright (c) 2017-2018 Microsoft Corporation. MIT License (section 4).

SkiaSharp and HarfBuzzSharp include third-party code, among it ANGLE, HarfBuzz, Skia, libpng, expat, FreeType, ICU,
libjpeg-turbo, libwebp and zlib. Their notices are in the `THIRD-PARTY-NOTICES.txt` files of the
SkiaSharp.NativeAssets.Win32 3.119.4 and HarfBuzzSharp.NativeAssets.Win32 8.3.1.3 packages, which nth ships unchanged
beside nth.exe as `THIRD-PARTY-NOTICES-SkiaSharp.txt` and `THIRD-PARTY-NOTICES-HarfBuzzSharp.txt`.

Portions of this software are copyright (c) The FreeType Project (www.freetype.org). All rights reserved.

This software is based in part on the work of the Independent JPEG Group.

### MicroCom.Runtime

Copyright 2021 (c) Nikita Tsukanov. MIT License (section 4).

### Tmds.DBus.Protocol (where the build copies it)

Copyright Tom Deseyn. MIT License (section 4).

### .NET runtime

nth ships a self-contained .NET runtime. Copyright (c) .NET Foundation and Contributors. MIT License (section 4). The
runtime includes third-party code whose notices are in the runtime pack's `THIRD-PARTY-NOTICES.TXT`, which nth ships
unchanged beside nth.exe as `THIRD-PARTY-NOTICES-dotnet.txt`.

### Velopack (updater and installer engine)

Copyright (c) 2021 Caelan Sayler. Copyright (c) 2024 Velopack Ltd. MIT License (section 4). nth's `Update.exe` and the
installer engine inside nth Setup are Velopack's programs; nth sets its own name and version information on them.

### Microsoft Visual C++ runtime

`msvcp140.dll`, `vcruntime140.dll`, `vcruntime140_1.dll` and `vcomp140.dll` (the version pinned in
`scripts/release-toolchain.json`) are Microsoft Visual C++ runtime files, Copyright (c) Microsoft Corporation,
redistributed under Microsoft's license terms for them. They are not part of nth and are not covered by the GNU AGPL
v3.0, which grants no rights in them.

## 3. Data, fonts and icons

### Mozilla Public Suffix List

nth embeds part of the Public Suffix List (`src/Marrow.Core/Port/ModelsPalette/psl-icann.txt`) in Marrow.Core.dll. It
is licensed under the Mozilla Public License 2.0 (https://mozilla.org/MPL/2.0/; full text in
`src/Marrow.Core/Port/ModelsPalette/psl-notice.md`), separately from the GNU AGPL. It was taken from
https://raw.githubusercontent.com/publicsuffix/list/823beb1425def931c8b0b170a6bc33b424c3f9b0/public_suffix_list.dat.
The Source Code Form of the copy that nth embeds is the file named above, in https://github.com/Oak993/nth at the tag
of each release. It is provided under the MPL on an "as is" basis, without warranty of any kind.

### LobeHub icons index

`src/Marrow.Core/Port/ModelsPalette/toc-source1.json`, embedded in Marrow.Core.dll, comes from @lobehub/icons 5.22.0
(`es/toc.json`). Copyright (c) 2023 LobeHub. MIT License (section 4).

### OKLab

nth's colour maths uses the OKLab colour space and its conversion matrices by Bjorn Ottosson
(https://bottosson.github.io/posts/oklab/), published by him under the MIT License.

### LiteLLM price file (downloaded, not shipped)

Ignis downloads LiteLLM's public model price file while nth runs (https://github.com/BerriAI/litellm, Copyright (c)
2023 Berri AI, MIT License). nth does not ship a copy.

### Fonts

- Inter: Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter). SIL Open Font License 1.1.
- Cascadia Mono: Copyright (c) 2019 - Present, Microsoft Corporation, with Reserved Font Name Cascadia Code. SIL Open
  Font License 1.1.
- KaTeX fonts (used by artifacts): SIL Open Font License 1.1. Their copyright lines (Design Science, Inc. and Khan
  Academy) and Reserved Font Names (KaTeX_*) are in each font file.

The text of the SIL Open Font License 1.1 is in `src/Marrow.UI/Assets/Fonts/Inter-OFL.txt` and
`src/Marrow.UI/Assets/Fonts/CascadiaMono-OFL.txt`. Cascadia Mono and the KaTeX fonts are unmodified. The fonts are
bundled with nth and are not sold on their own.

### Lucide icons

The app's icon set is Lucide: Copyright (c) 2026 Lucide Icons and Contributors, ISC License. The icons derived from
Feather: Copyright (c) 2013-present Cole Bemis, MIT License. The full texts and the list of Feather-derived icons are in
`src/Marrow.UI/Assets/Icons/lucide/LICENSE`.

### Packages inside the artifact libraries

The Mermaid bundle (`src/Marrow.Services/Assets/lib/mermaid/mermaid.min.js`) includes other packages, among them
DOMPurify (dual-licensed under the Apache License 2.0 and the MPL 2.0; nth uses it under the Apache License 2.0),
lodash-es, js-yaml, cytoscape, d3, dagre and khroma, each under the MIT, ISC or BSD-3-Clause license; their notices are
in the bundle where its build kept them. The notices kept by the Recharts bundle are in
`src/Marrow.Services/Assets/lib/recharts/Recharts.min.js.LICENSE.txt`.

## 4. MIT License

The components above marked "MIT License" are licensed under these terms, with their own copyright lines:

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 5. Not shipped with nth

- The NVIDIA CUDA libraries for the optional speed-up are downloaded from NVIDIA only when you choose them, and are
  under NVIDIA's license terms, which apply to you directly.
- The Microsoft Edge WebView2 Runtime is installed from Microsoft only when you choose it, under Microsoft's terms.
- AI providers' command-line tools, models and skill packs are installed from their vendors, under the vendors' terms.
