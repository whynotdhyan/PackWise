# Changelog

All notable changes to PackWise are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-10-13

First public release.

### Added

- Installer (`PackWise-Setup.exe`): per-user, no administrator rights, update-in-place, with
  options for the right-click menu, "Open with" associations and a desktop shortcut. Uninstall
  from Windows Settings removes everything it added.
- Portable zip for running without installation.
- File Explorer right-click menu: open, extract here / to its own folder / to…, test, convert to
  ZIP or 7Z, compress to ZIP or 7Z, add to archive, checksums and find duplicates. Selecting many
  files produces a single archive.
- Convert archives between formats; results are tested and originals kept.
- Getting-started tour on first launch (Help › Getting started, F1) and an offline user guide.
- New brand marks: the PackWise "Pw" tile and the Northstar Labs "Ni" tile.
- Northstar Labs design system (`Northstar.Design`): shared palettes, controls, icons, dialogs
  and brand tiles for future apps.
- Settings › Windows to manage Explorer integration and shortcuts.

- File manager with breadcrumbs, back/forward, folder sizes, sorting, filtering and a details
  pane with image and text previews (including files inside archives, read in memory).
- Open and extract ZIP, 7z, RAR/RAR5, TAR, GZip, BZip2, XZ, Zstandard, LZMA, CAB, ISO, WIM, ARJ,
  LZH, CPIO, split `.001` volumes and multi-part RAR archives.
- Create ZIP and 7z archives with compression presets, AES-256 encryption, encrypted file names
  (7z) and an automatic test after creation.
- Add files to and delete entries from existing ZIP and 7z archives.
- Safe extraction: path sanitising, link and alternate-stream blocking, up-front conflict
  decisions, atomic writes, Recycle Bin for replaced files, free-space guard and
  Mark-of-the-Web propagation.
- Test archives with a per-file report.
- Duplicate finder with staged hashing, hard-link awareness, cloud-placeholder skipping, keep
  rules and reversible clean-up.
- Checksum tool for CRC32, SHA-256 and SHA-512, with compare, save and verify.
- Light and dark themes, original vector icon set, keyboard shortcuts, drag and drop.
- Local activity log with retention and a privacy-first settings page.
- Archive engine: 7-Zip 26.04.
- Accessible names for rows, toolbar and icon buttons.
