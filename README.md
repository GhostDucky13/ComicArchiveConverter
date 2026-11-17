<p align="center">
  <img src="docs/icon.png" width="96" alt="Comic Archive Converter icon">
</p>

<h1 align="center">Comic Archive Converter</h1>

<p align="center">
  <a href="https://github.com/GhostDucky13/ComicArchiveConverter/actions/workflows/build.yml"><img src="https://github.com/GhostDucky13/ComicArchiveConverter/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8">
  <a href="https://github.com/GhostDucky13/ComicArchiveConverter/releases/latest"><img src="https://img.shields.io/github/v/release/GhostDucky13/ComicArchiveConverter?label=latest%20release" alt="Latest release"></a>
</p>

<p align="center">
  A free Windows tool that turns folders of comic and manga pages into <b>CBZ</b> files,<br>
  and turns <b>CBZ, CBR and CB7</b> files back into folders of images.<br>
  Drag, drop, convert. Every archive is checked image by image.
</p>

<p align="center">
  <a href="https://github.com/GhostDucky13/ComicArchiveConverter/releases/latest"><b>⬇ Download the latest version</b></a>
  &nbsp;·&nbsp; <a href="#how-to-use-it">How to use it</a>
  &nbsp;·&nbsp; <a href="docs/HOW_IT_WORKS.md">How the code works</a>
</p>

<!-- Add a screenshot: save it as docs/screenshot.png and uncomment the next line -->
<!-- <p align="center"><img src="docs/screenshot.png" width="800" alt="Screenshot"></p> -->

---

## Contents

- [Features](#features)
- [Download and run](#download-and-run)
- [How to use it](#how-to-use-it)
- [How folders are turned into comics](#how-folders-are-turned-into-comics)
- [Settings](#settings)
- [CBZ, CBR and CB7 back to folders](#cbz-cbr-and-cb7-back-to-folders)
- [FAQ](#faq)
- [Building from source](#building-from-source)
- [Project structure](#project-structure)
- [Contributing](#contributing)
- [License and disclaimer](#license-and-disclaimer)

---

## Features

| | |
|---|---|
| 🗂️ **Folders → CBZ** | Drop a comic folder or a whole library; get one CBZ per comic. |
| 📚 **Chapters your way** | Merge all chapters into one CBZ, or make **one CBZ per chapter**. |
| 🔢 **Natural order** | `Chapter 1, 1.5, 2, 10` — never `1, 10, 1.5, 2`. |
| 🖼️ **All common images** | JPG, PNG, **WebP**, GIF, BMP and AVIF. |
| 📦 **CBZ / CBR / CB7 / CBT → folders** | Extract any comic archive, or convert CBR/CB7 straight to CBZ. |
| 🏷️ **ComicInfo.xml** | Series name, chapter number, page count and chapter bookmarks for Komga, Kavita, YACReader, Mihon… |
| ✅ **Integrity check** | Every image is read back from the finished CBZ, checksum-tested and decoded. Bad archives are never saved. |
| 👀 **Preview** | See every page and its final position before anything is written. |
| 🖱️ **Drag and drop** | Or paste copied files with **Ctrl+V**. |
| 🛡️ **Safe** | Originals are never changed. Unfinished files are cleaned up if you cancel. |
| 💾 **Remembers everything** | Settings, output folder and window size. |
| 🔌 **No extra software** | No WinRAR or 7-Zip needed. Works fully offline. |

## Download and run

1. Open the [**Releases**](https://github.com/GhostDucky13/ComicArchiveConverter/releases/latest) page.
2. Download **`ComicArchiveConverter-vX.Y.Z-windows.zip`**. It works on every Windows 10/11 PC
   (32-bit, 64-bit and ARM). A `-win-x64.zip` is also provided for 64-bit PCs; either one is fine there.
3. Right-click it → **Extract All…**. You get one folder:

   ```
   ComicArchiveConverter\
   ├── ComicArchiveConverter.exe     ← double-click this
   ├── README.txt
   ├── LICENSE.txt
   ├── THIRD-PARTY-NOTICES.txt
   └── *.dll                         ← needed by the .exe, keep them next to it
   ```

4. Double-click **`ComicArchiveConverter.exe`**.

Nothing needs installing: the .NET runtime is included. **Requirements:** Windows 10 or 11 — 32-bit, 64-bit or ARM, all from the same download.

> **"Windows protected your PC"?** SmartScreen shows this for programs that aren't code-signed
> (certificates are expensive). Click **More info → Run anyway**, or
> [build it yourself](#building-from-source) from the source code.

**Uninstall:** delete the folder, and `%AppData%\ComicArchiveConverter` (settings) if you like.

## How to use it

1. **Drag** comic folders or CBZ / CBR / CB7 files into the window (or copy them in Explorer and
   press **Ctrl+V** in the app).
2. Choose **Chapter folders**: *All chapters in one CBZ* or *One CBZ per chapter*.
3. Press **Convert**. Each row shows its progress; double-click a finished row to see the result.

Want to check first? Press **Preview**: it lists every file that will be created and every page in
reading order. Everything else is on the **Settings** page.

| Shortcut | Action |
|---|---|
| `Ctrl+V` | Paste copied files or folders |
| `Delete` | Remove selected rows |
| `Ctrl+A` | Select all rows |
| `F5` | Preview |
| `Ctrl+Enter` | Convert |
| Double-click | Show the result (or the original) in Explorer |

## How folders are turned into comics

The converter looks for **comic folders**: folders that contain images, or that contain
**chapter folders** with images in them.

### A manga with chapters

```
Manga/
└── One Punch Man/
    ├── cover.jpg
    ├── Chapter 1/      001.webp 002.webp …
    ├── Chapter 1.5/    001.webp …
    ├── Chapter 2/      …
    └── Chapter 10/     …
```

| Chapter folders setting | Result |
|---|---|
| **All chapters in one CBZ** | `Manga\One Punch Man.cbz` — cover first, then chapters 1, 1.5, 2, 10. |
| **One CBZ per chapter** | `Manga\One Punch Man\One Punch Man - Chapter 1.cbz`, `… - Chapter 1.5.cbz`, … (plus `One Punch Man.cbz` for the loose cover). |

### A whole library

```
Library/
├── Batman/         Chapter 1/ Chapter 2/
├── Superman/       001.jpg 002.jpg …
└── Marvel/
    └── Spider-Man/ Chapter 1/ …
```

Result: `Batman.cbz`, `Superman.cbz` and `Spider-Man.cbz`. `Marvel` holds no images, so it's just
a container.

### The rules

1. Images directly inside a comic folder come first (usually a cover).
2. Chapter folders follow in natural order. Everything inside a chapter folder, including deeper
   folders, belongs to that chapter.
3. Any other subfolder is checked on its own and can become a separate CBZ.
4. Hidden folders, folders starting with `.` and `__MACOSX` are ignored.
5. Images: `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`, `.bmp`, `.avif`. Other files are left out.

A folder is a **chapter folder** when its name

- **starts with** a keyword (`Chapter 1`, `Ch.5`, `01 - Chapter 1`, `Pages`), or
- **contains** a keyword followed by a number (`Berserk - Chapter 12`, `[Group] Vol.03 Ch.012`).

| Type | Keywords | On by default |
|---|---|---|
| Chapter / Episode | `Chapter`, `Chap`, `Ch`, `Episode`, `Ep` | ✅ |
| Volume | `Volume`, `Vol` | ❌ (each volume becomes its own CBZ) |
| Part | `Part`, `Pt` | ✅ |
| Pages | `Pages`, `Page` | ✅ |
| Images | `Images`, `Image`, `Imgs`, `Img` | ✅ |

`Chronicles`, `Partners`, `Imagine` or `Watch 5` are **not** chapter folders: a keyword must be a
word of its own.

## Settings

All settings are saved automatically. **Restore defaults** puts everything back.

| Section | Setting | Meaning |
|---|---|---|
| **Where to save** | Next to the original / In this folder | Where CBZs and extracted folders go. |
| | Recreate the subfolders | With an output folder: `Out\Marvel\Spider-Man.cbz` instead of `Out\Spider-Man.cbz`. |
| | If the file already exists | *Keep both* (`Name (2).cbz`), *Skip it*, or *Replace it* (folders are merged). |
| **Creating CBZ files** | Number the pages in reading order | `001.webp, 002.webp …` — works in every reader. Off: each chapter keeps its own folder inside the CBZ, so chapters come back when extracted. |
| | Add ComicInfo.xml | Series, chapter number, page count and chapter bookmarks for library apps. |
| | Compression | *None* is fastest; images are already compressed, so the file is barely bigger. |
| **Chapter folders** | Chapter / Volume / Part / Pages / Images | Which subfolders count as chapters (see table above). |
| **Opening CBR, CB7 and CBT files** | Extract / Convert to CBZ | CBZ files are always extracted. |
| | Put all extracted images in one folder | Renumbers pages in reading order instead of recreating folders. |
| **Safety** | Check every image | Reads every image back and checks it. Recommended. |
| **When finished** | Open the output folder / Sound and taskbar flash | Small conveniences. |

Settings live in `%AppData%\ComicArchiveConverter\settings.json`.

## CBZ, CBR and CB7 back to folders

Drop the archives and press **Convert**. `Name.cbr` becomes a folder called `Name` with its
images in reading order. Password-protected archives aren't supported.

- If a CBZ was made with *Number the pages in reading order* **off**, the chapter folders come back.
- If it was numbered, the chapter boundaries don't exist inside the file any more, so you get one
  folder of pages.

Creating **CBR** files isn't possible: the RAR format may only be written by WinRAR's makers.
CBZ is supported by every reader, so that's what this tool creates.

## FAQ

**Do I need WinRAR or 7-Zip?**
No. CBZ is a ZIP file, which Windows and .NET create natively. CBR/CB7 are read with the
built-in [SharpCompress](https://github.com/adamhathcock/sharpcompress) library.

**Isn't a CBZ just a renamed RAR?**
No: renaming a `.rar` to `.cbz` produces a file many readers refuse. A RAR comic is a **CBR**.

**Which readers can open the result?**
Any CBZ reader: CDisplayEx, YACReader, Komga, Kavita, Calibre, Mihon/Tachiyomi, Panels, Chunky…

**How are WebP images verified?**
Windows' built-in decoder can't read WebP, so WebP and AVIF are checked structurally (header and
the size recorded inside the file), plus the ZIP checksum. JPG, PNG, GIF and BMP are fully decoded.

**What happens if I cancel?**
Finished files are kept. The one in progress is removed, so you never get a half-written CBZ.

## Building from source

### Visual Studio

1. Install **Visual Studio 2022** (17.8+) with the **.NET desktop development** workload.
2. Open `ComicArchiveConverter.sln`, press **F5**.
3. Run the tests with **Test → Run All Tests**.

### Release zip (one click)

Double-click **`build-release.bat`**. It runs the tests, publishes the self-contained build and
creates `publish\ComicArchiveConverter-vX.Y.Z-windows.zip` and `…-win-x64.zip`, each with the structure shown above.

## Project structure

```
ComicArchiveConverter/
├── src/
│   ├── ComicArchiveConverter/            Windows app
│   │   ├── Program.cs                    Entry point
│   │   ├── MainForm.cs                   The window and its pages
│   │   ├── Theme.cs                      Colours, fonts, button styles
│   │   ├── Controls.cs                   Header, drop zone, empty-list hint
│   │   ├── GdiImageValidator.cs          Decodes images with Windows to verify them
│   │   ├── NativeMethods.cs              Taskbar flashing
│   │   └── Resources/app.ico             The icon
│   └── ComicArchiveConverter.Core/       All the logic (no Windows dependency)
│       ├── ComicScanner.cs               Finds comics and orders their pages
│       ├── FolderGrouping.cs             Recognises Chapter/Volume/... folders
│       ├── NaturalSortComparer.cs        "Chapter 2" before "Chapter 10"
│       ├── CbzWriter.cs                  Writes CBZ files
│       ├── ComicInfo.cs                  Builds ComicInfo.xml
│       ├── ArchiveExtractor.cs           Extracts CBZ/CBR/CB7/CBT safely
│       ├── ArchiveVerifier.cs            Integrity check of finished CBZs
│       ├── ImageValidation.cs            Image format detection and checks
│       ├── Crc32.cs                      Checksum used by the integrity check
│       ├── ImageFormats.cs               Supported image and archive types
│       ├── OutputPaths.cs                Keep both / skip / replace decisions
│       ├── AppSettings.cs                Saved preferences
│       ├── ConversionService.cs          Plans and runs a conversion
│       └── Models.cs                     Small shared data types
├── tests/ComicArchiveConverter.Tests/    Automated tests (+ sample CBR/CB7 in Assets/)
├── docs/                                 Code walkthrough, icon, release README
├── .github/                              Automatic build + test, issue templates
├── build-release.bat / .ps1              One-click release zip
└── THIRD-PARTY-NOTICES.md                Licences of the libraries used
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and ideas go in
[Issues](https://github.com/GhostDucky13/ComicArchiveConverter/issues).

## License and disclaimer

Released under the [MIT License](LICENSE) © 2026 GhostDucky13. Third-party components are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

This software is provided as-is, without warranty. Keep backups of collections you care about, and
only convert comics you have the right to copy. Please read the full [DISCLAIMER](DISCLAIMER.md).
