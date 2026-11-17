# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
uses [Semantic Versioning](https://semver.org/) (MAJOR.MINOR.PATCH).

## [Unreleased]

## [2.1.0] - 2026-11-17

### Fixed
- Folders of **WebP** pages (the format most manga downloads use) reported
  "No JPG or PNG images found". WebP, GIF, BMP and AVIF are now supported.
- The *Remove selected* / *Clear list* buttons could be cut off on smaller windows.

### Added
- **One CBZ per chapter** option, next to the existing "all chapters in one CBZ".
  Chapter CBZs are saved in a folder named after the series (`Series\Series - Chapter 1.cbz`).
- **CBR (RAR), CB7 (7-Zip) and CBT (TAR)** files can be extracted to folders, or converted
  straight to CBZ.
- Smarter chapter detection: `Berserk - Chapter 12`, `[Group] Vol.03 Ch.012` and
  `Episode 7` are recognised, not only names that start with "Chapter".
- `ComicInfo.xml` in every CBZ: series name, chapter number, page count and a bookmark at the
  start of every chapter (used by Komga, Kavita, YACReader, Mihon and others).
- Custom app icon.
- Status column per item (pages found, done, failed, skipped) and a right-click menu.
- Double-click a finished item to show its result in Explorer; "Open output folder" link.
- Paste copied files or paths with Ctrl+V. Shortcuts: Delete, F5 (preview), Ctrl+Enter (convert).
- Progress bar counts pages, so it moves smoothly even for one huge CBZ. Shows elapsed time.
- Optional sound and taskbar flash when finished; optional "open output folder when finished".
- Remembers window size and position.
- Release zip now contains a single tidy folder with the .exe, its DLLs, README, licence and
  third-party notices.

### Changed
- New look: header, drop zone, and separate Convert / Preview / Log / Settings pages.
- Removed the *Add…* buttons: drag and drop (or paste) is the way to add items.
- Most options moved to the Settings page, with plain-language labels and "Restore defaults".

## [2.0.0] - 2026-11-17

### Added
- Custom output folder, with an option to keep the source subfolder structure.
- Natural chapter sorting (Chapter 1, 1.5, 2, 10 ...).
- Choice of which subfolder types belong to the parent comic: Chapter, Volume, Part, Pages, Images.
- Archive integrity checking: every image is read back from the finished CBZ, its CRC-32
  checksum compared and the picture decoded. Failed archives are never saved.
- Drag and drop of folders and `.cbz` files.
- Settings are remembered between sessions.
- Preview showing every image that will be included and its final reading order.
- Choice of what to do when an output already exists (rename, skip, overwrite).
- Cancel button; cancelled or failed items never leave half-written files behind.
- Automated tests and a GitHub Actions build.

### Changed
- CBZ files are written directly as ZIP archives with .NET's built-in compression.
  WinRAR is no longer required.

## [1.0.0]

### Added
- Folder → CBZ and CBZ → folder conversion.
- Chapter folders merged into the parent comic.
- Batch conversion of a library folder (one CBZ per comic).
