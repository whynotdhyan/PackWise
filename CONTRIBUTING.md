# Contributing to PackWise

Thanks for helping. Bug reports, fixes, translations of wording and test archives that break
things are all welcome.

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows 10 or 11.
2. `dotnet build` — the first build runs `eng/Get-SevenZip.ps1`, which downloads the official
   7-Zip release pinned in `third_party/7zip/engine.json`, verifies its SHA-256 and extracts
   `7z.dll`. Nothing is installed.
3. `dotnet test --project tests/PackWise.Tests`
4. `dotnet run --project src/PackWise`

## Project layout

| Path | Purpose |
|---|---|
| `src/PackWise.Archiving` | Engine bridge: COM interop with `7z.dll`, format policy, open/list/extract/test/create/update. Never writes extracted data to disk itself. |
| `src/PackWise.Core` | Safety and logic with no UI: path sanitising, extraction planning and atomic writes, Mark-of-the-Web, risk hints, duplicate scanning, checksums, settings, logging. |
| `src/Northstar.Design` | Shared Northstar Labs design system: palettes, control styles, icons, dialogs, brand tiles. See [docs/design-system.md](docs/design-system.md). |
| `src/PackWise` | WPF desktop app (MVVM with CommunityToolkit.Mvvm), Explorer integration and command-line verbs. |
| `installer/PackWise.Setup` | Per-user installer (.NET Framework 4.8, no prerequisites) that embeds the published app. |
| `tests/PackWise.Tests` | xUnit tests, including integration tests against the real engine. |
| `eng/` | Build tooling: engine fetch script, icon generator, publish script. |

## Ground rules

- **Safety code needs tests.** Anything touching paths, overwrites, deletion, passwords or the
  Mark-of-the-Web must come with tests that fail without the change.
- **No network calls.** PackWise must work offline and never send data anywhere. Links that open
  in the browser on an explicit click are the only exception.
- **Never log secrets.** Passwords and file contents must not reach logs, exceptions or settings.
- **Engine calls run off the UI thread.** `ArchiveReader` enforces this; wrap work in `Task.Run`
  or `Operations.RunAsync`.
- **Warnings are errors.** The build uses `TreatWarningsAsErrors` and the analyzers configured in
  `.editorconfig`. Run `dotnet build` before opening a pull request.
- **User-facing text** is plain, specific and calm. Say what happened and what the user can do.
  Avoid jargon, blame and exclamation marks.

## Commit messages

Use the imperative mood and keep the subject under 72 characters, for example
`Block symlink entries during extraction`. Explain the why in the body when it isn't obvious.

## Updating the 7-Zip engine

1. Update `version`, `source` and `sha256` in `third_party/7zip/engine.json` (use the digest
   published on the official GitHub release).
2. Run `eng/Get-SevenZip.ps1 -Force` and the full test suite.
3. Mention the engine version and any security fixes in `CHANGELOG.md`.

## Regenerating the icon

Brand marks are vector presets in `src/Northstar.Design/Branding/MonogramLogo.cs`. After changing
them, run `dotnet run --project eng/IconTool` to rebuild the `.ico` and PNG files in
`src/PackWise/Assets`.
