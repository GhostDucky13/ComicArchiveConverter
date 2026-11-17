# Disclaimer

**Please read this before using Comic Archive Converter.**

## 1. No warranty

This software is provided "as is", without warranty of any kind (see [LICENSE](LICENSE)).
The author is not responsible for any loss of data, damaged files, or other harm
that results from using it.

## 2. Back up your files

The converter is designed to be safe:

- It **never deletes or changes your original image folders or CBZ files**.
- New archives are written under a temporary name and only renamed once they are
  complete (and verified, if verification is on).
- Extraction writes to a temporary folder and is moved into place only when it succeeds.

However, the **"Replace it"** option (Settings → *If the file already exists*) *does* replace existing CBZ files and
same-named images in existing folders. **Always keep a backup** of any collection you
care about before running a large batch conversion, and use **Preview** first.

## 3. Copyright and legal use

This tool only repackages image files you already have. It does **not** download,
crack, or remove protection from anything.

- Only convert comics you own or have the right to copy.
- Do not use this tool to distribute copyrighted material.
- Respect the terms of the store or service you got your comics from.

The author does not condone piracy and is not responsible for how others use this software.

## 4. Trademarks

"Windows" and "Visual Studio" are trademarks of Microsoft Corporation. "WinRAR" is a
trademark of win.rar GmbH. "RAR" is a format owned by Alexander Roshal; this program only reads it. This project is not affiliated with or endorsed by them.
CBZ is an informal, open convention (a ZIP file of images), not a trademark.

## 5. Privacy

The program works entirely offline. It collects no data, has no telemetry and makes
no network connections. Its only file outside your chosen folders is its settings file:
`%AppData%\ComicArchiveConverter\settings.json`.
