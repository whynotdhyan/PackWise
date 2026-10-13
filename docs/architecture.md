# Architecture

PackWise is built from these projects, with a strict dependency direction:

```
PackWise (WPF app)  ──►  PackWise.Core  ──►  PackWise.Archiving  ──►  7z.dll
        │
        └──────────►  Northstar.Design (shared UI system)

PackWise.Setup (.NET Framework 4.8 installer)  ──embeds──►  published PackWise
```

`PackWise.exe` also accepts command-line verbs (`--zip`, `--extract-here`, `--test`,
`--convert-7z`, …) used by the File Explorer menu. When Explorer starts one process per selected
file, the first process gathers the others' paths over a named pipe (`Integration/CommandLine.cs`)
so a multi-file selection becomes one operation. `--integrate` and `--uninstall` are used by the
installer and by Windows "Installed apps".

## PackWise.Archiving — the engine bridge

Talks to the official, unmodified `7z.dll` through hand-written COM interop
(`SevenZip/Interop`). There is no third-party wrapper in between.

- `SevenZipLibrary` loads the DLL with `NativeLibrary`, enumerates its format handlers and
  creates handler objects through the `CreateObject` export.
- `FormatCatalog` is PackWise's **policy** on top of what the engine can parse: display names,
  categories, whether a format opens by default ("trusted") and which operations PackWise offers.
  Disk images, file systems, installers and executables are untrusted by default because their
  parsers are complex and rarely needed.
- `ArchiveReader` detects the format (extension and signature), opens the archive (asking for a
  password through `IPasswordProvider` when headers are encrypted), lists entries and runs extract
  or test operations. **It never writes to disk**: extraction goes through an `IExtractTarget`
  that decides where, and whether, each entry is written.
- `ArchiveWriter` creates archives and rewrites existing ones (add/remove). Output always goes to
  a temporary sibling file and is moved into place by `PendingArchive.Commit()`.

Engine objects are COM objects without marshalling support, so every engine call must run on a
background (MTA) thread. `ArchiveReader` throws if called from the UI thread instead of failing
in confusing ways later.

## PackWise.Core — safety and logic

No UI, fully unit-tested.

- `Extraction/EntryPathSanitizer` turns untrusted entry names into safe relative paths.
- `Extraction/ExtractionPlanner` decides every target path and resolves every conflict
  **before** extraction starts, including duplicate names inside the archive, links in the
  destination tree and entries that must be blocked.
- `Extraction/SafeExtraction` executes a plan: temporary file per entry, rename on success,
  timestamps and safe attributes, Mark-of-the-Web, free-space budget, password retries.
- `Security/MarkOfTheWeb` and `Security/FileRiskClassifier` implement the internet mark and the
  informational risk hints.
- `Duplicates/` holds the staged duplicate scanner and the reversible clean-up actions.
- `Hashing/Checksums`, `Settings/`, `Diagnostics/ActivityLog` and `Platform/` (Recycle Bin, file
  identity, free space, temporary workspace) complete the layer.

## PackWise — the desktop app

WPF with MVVM (`CommunityToolkit.Mvvm` source generators).

- `ViewModels/MainViewModel` drives one list that browses drives, folders and archive contents
  through `Browser/Location` values, so history, breadcrumbs and "up" work identically everywhere.
- `ViewModels/Operations` runs long jobs behind a modal progress dialog. Workers report progress
  through thread-safe fields that the UI samples on a timer.
- `Themes/` contains light and dark palettes plus custom control templates. All colours are
  resources, so switching themes is live.
- `Icons/` is PackWise's own vector icon set, rendered at any size by `GlyphIcon` and
  `FileTypeIcon`. `Branding/BrandArtwork` is the logo, shared with `eng/IconTool`.

## Testing

`tests/PackWise.Tests` runs on Microsoft.Testing.Platform with xUnit v3. Engine tests create,
open, test, extract and rewrite real ZIP and 7z archives (with and without encryption). Core
tests cover crafted traversal names, overwrite rules, Mark-of-the-Web propagation, the output
budget, duplicate detection edge cases and checksum formats.
