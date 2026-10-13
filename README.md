<p align="center">
  <img src="src/PackWise/Assets/PackWise-512.png" width="96" alt="PackWise logo" />
</p>

<h1 align="center">PackWise</h1>

<p align="center">
  A free archive manager for Windows with the classic, keyboard-friendly workflow you already know,
  safer extraction, and a built-in duplicate finder.<br />
  No account. No ads. No telemetry. No cloud.
</p>

<p align="center">
  <a href="https://github.com/whynotdhyan/PackWise/actions/workflows/ci.yml"><img src="https://github.com/whynotdhyan/PackWise/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-0E8A87" alt="MIT License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0E8A87" alt="Windows 10 and 11" />
</p>

---

## Why PackWise

Opening archives is easy; opening them **safely** is not. Archive tools have shipped a steady
stream of path-traversal and parser bugs, some exploited in the wild. PackWise keeps the familiar
file-manager layout (toolbar, address bar, file list, status bar) and changes what happens underneath:

- **Nothing lands outside the folder you chose.** Entry names are sanitised before anything is
  written: `..` paths are blocked, drive and network prefixes are stripped, NTFS alternate data
  streams (`file.txt:payload.exe`), reserved device names (`CON`, `NUL`…), trailing dots and
  direction-override characters are neutralised. Links inside archives are never created.
- **Never a silent overwrite.** Every collision is decided up front: ask, keep both, skip or
  replace. Replaced files go to the Recycle Bin.
- **The internet mark travels with your files.** When an archive came from the internet, extracted
  files keep Windows' Mark-of-the-Web, so SmartScreen and Office Protected View still warn you.
- **Atomic writes.** Files are written to a temporary name and only appear once they pass their
  checksum. Cancelled or failed items leave nothing half-written behind.
- **Zip-bomb guard.** Extraction stops if output would exceed the free space on the drive.
- **Smaller attack surface.** Disk images, file systems, installers and executables are rarely
  needed and have complex parsers, so they stay closed unless you enable them in Settings.
- **Honest warnings.** Programs, scripts, macro documents and disguised names like
  `invoice.pdf.exe` are flagged before you open them. PackWise does not pretend to be antivirus.

## Features

| | |
|---|---|
| **Browse** | Drives, folders and archive contents in one list, with breadcrumbs, back/forward, folder sizes, sorting and instant filtering. Search a whole archive by name. |
| **Open** | ZIP, 7z, RAR/RAR5, TAR, GZip, BZip2, XZ, Zstandard, LZMA, CAB, ISO, WIM, ARJ, LZH, CPIO, split `.001` volumes and multi-part RAR. |
| **Create** | ZIP and 7z with six compression presets, AES-256 encryption, encrypted file names (7z), and an automatic test of the new archive. |
| **Edit** | Add files to, or delete entries from, existing ZIP and 7z archives. The original is replaced only after the new copy is complete. |
| **Test** | Verify every file against its checksum and get a per-file report. |
| **Preview** | Images and text inside archives, read in memory — nothing is extracted or executed. |
| **Duplicate finder** | Size → sampled fingerprint → SHA-256 → optional byte comparison. File names are never treated as evidence. Hard links are recognised; OneDrive online-only files are skipped instead of downloaded. Clean-up goes to the Recycle Bin or a review folder, and at least one copy of every group is always kept. |
| **Right-click menu** | In File Explorer, right-click files for PackWise: compress to ZIP/7Z, extract here or to a folder, test, convert, checksums and duplicate search. Selecting many files makes one archive. |
| **Convert** | Re-pack any archive as ZIP or 7Z. The result is tested and the original is kept. |
| **Checksums** | CRC32, SHA-256 and SHA-512 in one pass; compare with a published hash; save or verify `.sha256`, `.sha512` and `.sfv` files. |
| **Comfort** | Light and dark themes (or follow Windows), drag and drop, keyboard shortcuts, high-DPI and long-path support, screen-reader names, local activity log. |

## Download

Get the latest version from [Releases](https://github.com/whynotdhyan/PackWise/releases):

| File | Use it when |
|---|---|
| `PackWise-1.0.0-Setup.exe` | You want the normal install: Start menu, right-click menu, uninstall from Settings. |
| `PackWise-1.0.0-Setup.zip` | Your browser or email prefers zips. Same installer plus a short read-me. |
| `PackWise-1.0.0-Portable.zip` | You want to run PackWise without installing it, e.g. from a USB drive. |

Setup installs for the current user only, so it never asks for administrator rights.
Requirements: Windows 10 or 11, 64-bit. Nothing else to install.
Every release lists SHA-256 checksums in `SHA256SUMS.txt`.

New to PackWise? A short tour opens on first start, and **Help › User guide** explains everything
([online copy](docs/guide/index.html)).

## Keyboard shortcuts

| Action | Keys | Action | Keys |
|---|---|---|---|
| Add to archive | <kbd>Alt</kbd>+<kbd>A</kbd> | Open archive | <kbd>Ctrl</kbd>+<kbd>O</kbd> |
| Extract to | <kbd>Alt</kbd>+<kbd>E</kbd> | Filter / find | <kbd>Ctrl</kbd>+<kbd>F</kbd> |
| Test | <kbd>Alt</kbd>+<kbd>T</kbd> | Up one level | <kbd>Backspace</kbd> |
| View | <kbd>Alt</kbd>+<kbd>V</kbd> | Back / forward | <kbd>Alt</kbd>+<kbd>←</kbd> / <kbd>→</kbd> |
| Information | <kbd>Alt</kbd>+<kbd>I</kbd> | Refresh | <kbd>F5</kbd> |
| Delete | <kbd>Del</kbd> | Details pane | <kbd>F9</kbd> |
| Duplicate finder | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>D</kbd> | Checksums | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>K</kbd> |
| Add favourite | <kbd>Ctrl</kbd>+<kbd>D</kbd> | Settings | <kbd>Ctrl</kbd>+<kbd>,</kbd> |
| Getting started | <kbd>F1</kbd> | Copy path | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd> |

## Privacy

PackWise works entirely offline. It never uploads file names, contents, hashes, passwords or
usage data, and it has no telemetry. Passwords are kept in memory only while the app is open.
Details: [docs/privacy.md](docs/privacy.md).

## What PackWise does not do

- **Create RAR archives.** RAR creation is proprietary; PackWise extracts RAR only.
- **Repair archives.** It reports exactly which files are damaged and can keep partial output.
- **Scan for malware.** File-type warnings are hints, not a verdict.
- **Recover lost passwords.**

## Building from source

```powershell
git clone https://github.com/whynotdhyan/PackWise.git
cd packwise
dotnet build          # fetches the pinned 7-Zip engine on first build
dotnet test --project tests/PackWise.Tests
dotnet run --project src/PackWise
```

To build the installer and zips, run `./eng/Publish.ps1 -Version 1.0.0`; output lands in
`artifacts/release`.

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). See
[CONTRIBUTING.md](CONTRIBUTING.md) for the project layout and conventions, and
[docs/architecture.md](docs/architecture.md) for how the pieces fit together. The UI is built on
the shared [Northstar Labs design system](docs/design-system.md).

## Support the project

PackWise is free and always will be. If it saves you time, you can support development through
[Buy Me a Coffee](https://buymeacoffee.com/whynotdhyan) — also in **Help › Support PackWise**.

## License

PackWise is released under the [MIT License](LICENSE) by Northstar Labs.
It uses the [7-Zip](https://www.7-zip.org) engine by Igor Pavlov (GNU LGPL with the unRAR
restriction); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
PackWise is not affiliated with 7-Zip, RARLAB or WinRAR.
