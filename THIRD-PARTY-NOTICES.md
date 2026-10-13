# Third-party notices

PackWise is licensed under the MIT License (see `LICENSE`). It includes or depends on
the components below, each under its own license.

## 7-Zip (7z.dll)

- Version: 26.04, from the official release at <https://github.com/ip7z/7zip/releases>
- Copyright (C) 1999–2026 Igor Pavlov
- License: GNU LGPL, with parts under the BSD 2-clause and 3-clause licenses, and RAR
  decompression code under the GNU LGPL plus the unRAR restriction.
- The full license text ships next to the application as `licenses/7-Zip.txt`.

PackWise loads 7z.dll as a separate, unmodified library. You may replace it with another
compatible build of 7z.dll. The unRAR restriction means the RAR decompression code may not
be used to develop a RAR-compatible archiver; PackWise only extracts RAR archives and never
creates them.

## CommunityToolkit.Mvvm

- Copyright (c) .NET Foundation and Contributors
- License: MIT — <https://github.com/CommunityToolkit/dotnet/blob/main/License.md>

## System.IO.Hashing

- Copyright (c) .NET Foundation and Contributors
- License: MIT — <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

## .NET runtime and WPF

- Copyright (c) .NET Foundation and Contributors
- License: MIT — <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>,
  <https://github.com/dotnet/wpf/blob/main/LICENSE.TXT>
