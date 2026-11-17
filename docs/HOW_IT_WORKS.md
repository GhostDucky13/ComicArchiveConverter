# How Comic Archive Converter Works

A complete walkthrough of the code: the ideas behind it, then every file in the order you
would write them if you were building the program from scratch.

This describes version 2.1. Open the solution in Visual Studio next to this document and read along. Each section quotes
the code and explains it line by line or block by block.

---

## Contents

**Part 1 — Background**
1. [The big picture](#1-the-big-picture)
2. [C# and .NET in 5 minutes](#2-c-and-net-in-5-minutes)
3. [How the solution is organised](#3-how-the-solution-is-organised)
4. [The project files](#4-the-project-files)

**Part 2 — The Core library (the logic)**
5. [ImageFormats.cs](#5-imageformatscs)
6. [NaturalSortComparer.cs](#6-naturalsortcomparercs)
7. [FolderGrouping.cs](#7-foldergroupingcs)
8. [Models.cs](#8-modelscs)
9. [ComicScanner.cs](#9-comicscannercs)
10. [CbzWriter.cs](#10-cbzwritercs)
11. [ComicInfo.cs](#11-comicinfocs)
12. [Crc32.cs](#12-crc32cs)
13. [ImageValidation.cs](#13-imagevalidationcs)
14. [ArchiveVerifier.cs](#14-archiveverifiercs)
15. [ArchiveExtractor.cs](#15-archiveextractorcs)
16. [OutputPaths.cs](#16-outputpathscs)
17. [AppSettings.cs](#17-appsettingscs)
18. [ConversionService.cs](#18-conversionservicecs)

**Part 3 — The Windows app**
19. [Program.cs](#19-programcs)
20. [Theme.cs](#20-themecs)
21. [Controls.cs](#21-controlscs)
22. [GdiImageValidator.cs](#22-gdiimagevalidatorcs)
23. [NativeMethods.cs](#23-nativemethodscs)
24. [MainForm.cs](#24-mainformcs)

**Part 4 — Building, tests and practice**
25. [Building the release zip](#25-building-the-release-zip)
26. [The tests](#26-the-tests)
27. [Rebuilding it yourself from an empty folder](#27-rebuilding-it-yourself-from-an-empty-folder)
28. [Exercises](#28-exercises)
29. [Glossary](#29-glossary)

---

# Part 1 — Background

## 1. The big picture

Everything the program does fits in one sentence:

> Find images, decide their order, put them in a ZIP file named `.cbz`, then check the ZIP.

And the reverse:

> Open a `.cbz` / `.cbr` / `.cb7`, decide where each image goes, copy them out.

### What a CBZ really is

A CBZ is **an ordinary ZIP file** whose extension has been changed to `.cbz`. Comic readers open
the ZIP and show the images sorted by name. That is why:

- we don't need WinRAR to *create* comics: .NET has ZIP support built in (`System.IO.Compression`);
- **the names of the files inside the ZIP decide the reading order**, so a lot of the code is about
  choosing good names (`001.webp`, `002.webp`, …).

Other comic archives are the same idea with a different container: **CBR** = RAR, **CB7** = 7-Zip,
**CBT** = TAR. .NET can't read those, so the app uses a small open-source library,
**SharpCompress**, to *read* them. (Nobody but WinRAR's makers may *write* RAR files, which is why
the app creates CBZ only.)

### The bug that shaped version 2.1

Version 2.0 only accepted `.jpg` and `.png`. A manga folder whose 30 chapters were full of `.webp`
pages produced *"No JPG or PNG images found"*: the chapter folders were found, but every file in
them was filtered out. Version 2.1 accepts WebP, GIF, BMP and AVIF too, and the warning now lists
the supported types so the cause is obvious.

### The flow of one conversion

```
 User drops files and clicks Convert
        │
        ▼
 MainForm  ──reads the screen──►  AppSettings
        │
        ▼
 ConversionService.CreatePlan()        ◄── also used by Preview
        │   ├── ComicScanner.Scan()      finds comics + page order (one CBZ per series or per chapter)
        │   │       ├── FolderGrouping   "is this a Chapter folder?"
        │   │       └── NaturalSortComparer  "Chapter 2 before Chapter 10"
        │   └── ArchiveExtractor.Plan()  lists images inside CBZ/CBR/CB7/CBT files
        ▼
 ConversionService.Run()               (on a background thread)
        ├── OutputPaths.Resolve()      keep both / skip / replace
        ├── CbzWriter.Write()          writes the ZIP (+ ComicInfo.xml)
        ├── ArchiveVerifier.Verify()   reads it all back
        │       ├── Crc32              checksum
        │       └── IImageValidator    is every image complete and readable?
        └── ArchiveExtractor.Extract() reverse direction (or CBR → CBZ)
        │
        ▼
 Progress, log lines and per-row results flow back to MainForm
```

### Two key design decisions

1. **Logic and screen are separated.** All real work lives in `ComicArchiveConverter.Core`, which
   knows nothing about windows or buttons. The window (`MainForm`) only collects settings and shows
   results. That makes the logic testable and keeps the window code simple.
2. **Plan first, then act.** `CreatePlan` builds a complete list of everything that will happen
   without touching the disk. Preview shows that list; Convert executes it. So the preview can
   never disagree with what actually happens.

---

## 2. C# and .NET in 5 minutes

If some C# syntax in later sections is unfamiliar, come back here.

| Syntax | Meaning |
|---|---|
| `namespace ComicArchiveConverter.Core;` | Groups related types, like a folder for code. The `;` form applies to the whole file. |
| `using System.IO.Compression;` | Lets you write `ZipArchive` instead of `System.IO.Compression.ZipArchive`. |
| `public` / `private` / `internal` | Who can use it: everyone / only this class / only this project. |
| `sealed class` | A class nobody can inherit from (simpler and slightly faster). |
| `static class` | A class you never create with `new`; it's just a bag of methods. |
| `record` | A class designed to hold data. You get a constructor, equality and `ToString()` for free. |
| `enum` | A fixed list of named choices, e.g. `Rename`, `Skip`, `Overwrite`. |
| `interface` | A contract: "anything that is an `IImageValidator` has a `TryValidate` method". |
| `string?` | The `?` means "this may be `null`". Without it, the compiler warns if you might store `null`. |
| `var x = …` | Let the compiler work out the type. |
| `x => x.Name` | A lambda: a tiny unnamed function. "Given x, return x.Name". |
| `a ?? b` | `a`, unless `a` is null, then `b`. |
| `a?.B` | `a.B`, unless `a` is null, then the whole thing is null. |
| `$"Hello {name}"` | String interpolation: inserts the value of `name`. |
| `using var file = …;` | Closes/frees `file` automatically at the end of the block. Essential for files. |
| `out string? error` | The method returns an extra value through this parameter. |
| `=> expression` | Short method body: `int Twice(int x) => x * 2;` |
| `{ get; set; }` | A property: a field with a getter and setter. |
| `{ get; init; }` | Can only be set while creating the object. |
| `required` | Must be set when the object is created. |
| `IEnumerable<T>` | "Something you can loop over." |
| `List<T>`, `HashSet<T>` | A growable list / a set with fast "does it contain…?" lookups. |
| **LINQ**: `.Where`, `.Select`, `.OrderBy`, `.ToList` | Filter, transform, sort and collect sequences. |
| `async` / `await` / `Task` | Run slow work without freezing the window. |
| `switch` expression | `x switch { A => 1, B => 2, _ => 3 }` — `_` means "anything else". |
| `try / catch / finally` | Handle errors; `finally` runs whether or not there was an error. |
| `catch (X ex) when (…)` | Only catch the error if the condition is true. |
| `///` comments | XML documentation; Visual Studio shows it when you hover over a name. |

**.NET** is the platform (runtime + libraries) that C# runs on. **.NET 8** is the version used
here. **Windows Forms (WinForms)** is the .NET library for classic Windows windows, buttons and
dialogs.

---

## 3. How the solution is organised

A **solution** (`.sln`) is a container for one or more **projects** (`.csproj`). Each project
compiles into one file (`.exe` or `.dll`).

| Project | Output | Purpose | Depends on |
|---|---|---|---|
| `ComicArchiveConverter.Core` | `ComicArchiveConverter.Core.dll` | All logic | SharpCompress (NuGet) |
| `ComicArchiveConverter` | `ComicArchiveConverter.exe` | The window | Core |
| `ComicArchiveConverter.Tests` | test DLL | Automated tests | Core |

Why split? The Core targets plain `net8.0`, so it has no Windows dependency: the tests can run
anywhere (including on GitHub's build machines), and you could later build a command-line
version or a different GUI on top of the same Core without touching it.

---

## 4. The project files

### Directory.Build.props

MSBuild (the build engine behind Visual Studio) automatically reads any file called
`Directory.Build.props` in the project's folder or a folder above it. Settings here apply to
**all three projects**.

```xml
<LangVersion>latest</LangVersion>      <!-- use the newest C# features the compiler knows -->
<Nullable>enable</Nullable>            <!-- warn about possible null mistakes -->
<ImplicitUsings>enable</ImplicitUsings><!-- auto-add common usings (System, System.IO, System.Linq…) -->
<Version>2.1.0</Version>               <!-- title bar, file properties AND the release zip name -->
<Authors>GhostDucky13</Authors>           <!-- shown in the .exe file properties -->
```

Change `Version` here when you release a new version, and it updates everywhere, including the
name of the zip that `build-release.ps1` produces.

### ComicArchiveConverter.Core.csproj

```xml
<TargetFramework>net8.0</TargetFramework>
...
<PackageReference Include="SharpCompress" Version="0.41.0" />
<InternalsVisibleTo Include="ComicArchiveConverter.Tests" />
```

- `TargetFramework net8.0` — build for .NET 8, any operating system.
- `PackageReference` — download a library from **NuGet** (.NET's package store) when the project is
  restored. SharpCompress reads RAR, 7-Zip, TAR and ZIP. It brings its own dependency
  (ZstdSharp.Port) automatically. The version is *pinned* (0.41.0) so every build is identical.
  Both are MIT-licensed; their licences are reproduced in `THIRD-PARTY-NOTICES.md`, which is
  required when you distribute them.
- `InternalsVisibleTo` — lets the test project use things marked `internal`.

### ComicArchiveConverter.csproj (the app)

```xml
<OutputType>WinExe</OutputType>                   <!-- a Windows program, no console window -->
<TargetFramework>net8.0-windows</TargetFramework> <!-- .NET 8, Windows-only APIs allowed -->
<UseWindowsForms>true</UseWindowsForms>           <!-- bring in WinForms -->
<ApplicationManifest>app.manifest</ApplicationManifest>
<ApplicationIcon>Resources\app.ico</ApplicationIcon>
<EnableWindowsTargeting>true</EnableWindowsTargeting>
...
<EmbeddedResource Include="Resources\app.ico" LogicalName="ComicArchiveConverter.app.ico" />
<ProjectReference Include="..\ComicArchiveConverter.Core\ComicArchiveConverter.Core.csproj" />
```

- `WinExe` vs `Exe`: `Exe` would open a black console window behind your app.
- `ApplicationIcon` — the icon Windows shows for the `.exe` in Explorer, on the desktop and on
  the taskbar.
- `EmbeddedResource` — *also* stores the icon inside the program, so the code can load it at run
  time (window icon, header picture) without needing a separate file. `LogicalName` is the name
  the code asks for.
- `EnableWindowsTargeting` allows compiling on non-Windows machines (for the GitHub check).
- `ProjectReference` means "this project uses Core".

### The icon (`Resources/app.ico`)

An `.ico` file holds the same picture at several sizes (16, 20, 24, 32, 40, 48, 64, 128 and
256 pixels), because Windows picks the size that fits each place it's shown. The small sizes use a
simplified drawing (no speech bubble, thicker lines) so they stay readable at 16 pixels. The design:
a comic page with three panels on an indigo-to-blue tile, with an amber zipper down the side,
meaning "comic + archive". `docs/icon.png` is the 256-pixel version used in the README.

### app.manifest

A small XML file embedded into the `.exe` that tells Windows:

- `requestedExecutionLevel level="asInvoker"` — run as a normal user, never ask for admin rights;
- `supportedOS` — built for Windows 10/11;
- `longPathAware` — allow paths longer than 260 characters (deep comic libraries hit this).

### The publish profile and build scripts

Explained in [section 25](#25-building-the-release-zip).

---

# Part 2 — The Core library

The files are presented from the simplest (no dependencies) to the most complex (uses everything).

## 5. ImageFormats.cs

This file holds two small helper classes: which **images** we collect, and which **archives** we
can open.

```csharp
public static class ImageFormats
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif",
    };

    public static IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public static bool IsImage(string fileName) => SupportedExtensions.Contains(Path.GetExtension(fileName));
}
```

| Line | Explanation |
|---|---|
| `public static class ImageFormats` | A helper class; you never create one, you call `ImageFormats.IsImage(...)`. |
| `private static readonly HashSet<string>` | One shared set, created once (`static`), that can't be replaced (`readonly`). A `HashSet` answers "is X in here?" instantly. |
| `new(StringComparer.OrdinalIgnoreCase)` | Compare ignoring capitals, so `.JPG` and `.WebP` count. `new(...)` is short for `new HashSet<string>(...)` because the type is already known. |
| `{ ".jpg", … ".avif" }` | Collection initialiser. **`.webp` here is the fix for the "No JPG or PNG images found" bug.** |
| `Extensions => SupportedExtensions` | A read-only view for other code (used in the "No images found" warning, so it lists the types). |
| `Path.GetExtension(fileName)` | `"page01.WEBP"` → `".WEBP"`. |

```csharp
public static class ArchiveFormats
{
    private static readonly HashSet<string> ReadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".zip", ".cbr", ".rar", ".cb7", ".7z", ".cbt", ".tar",
    };

    public static bool IsArchive(string fileName) => ReadableExtensions.Contains(Path.GetExtension(fileName));

    public static bool IsZip(string fileName) =>
        Path.GetExtension(fileName).Equals(".cbz", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    public static string Label(string fileName) => Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
}
```

- `IsArchive` decides whether a dropped **file** is accepted (dropped folders are always accepted).
- `IsZip` — a CBZ is already a CBZ, so "Convert CBR to CBZ" never applies to it; it's always extracted.
- `Label("Batman.cbr")` → `"CBR"`, used in the list's *Action* column ("CBR → Folder").
  `TrimStart('.')` removes the dot; `ToUpperInvariant` makes capitals the same way on every PC.

---

## 6. NaturalSortComparer.cs

**Problem:** Computers sort text character by character. `"Chapter 10"` vs `"Chapter 2"`: the
first different character is `1` vs `2`, and `1` is smaller, so `Chapter 10` comes first. Wrong.

**Solution:** Split each name into **tokens**: runs of text and numbers.
`"Chapter 1.5 Extra"` → `["Chapter ", 1.5, " Extra"]`. Compare token by token: numbers as numbers,
text as text.

```csharp
public sealed partial class NaturalSortComparer : IComparer<string>
```

- `IComparer<string>` — the .NET interface for "something that can compare two strings". Anything
  that sorts (`OrderBy`, `List.Sort`) can use it.
- `partial` — needed because part of this class is generated by the compiler (see the regex next).

```csharp
public static NaturalSortComparer Instance { get; } = new();
```

One shared instance. The comparer holds no data, so everyone can use the same one:
`list.OrderBy(x => x, NaturalSortComparer.Instance)`.

```csharp
[GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
private static partial Regex NumberPattern();
```

A **regular expression** (regex) is a mini-language for patterns in text.

| Piece | Meaning |
|---|---|
| `\d+` | one or more digits |
| `(?: … )` | a group (that we don't need to capture separately) |
| `\.\d+` | a dot followed by digits |
| `?` after the group | the group is optional |

So it matches `12`, `001`, `1.5`, but not `.5`. `@"..."` is a verbatim string, so `\d` is not
treated as an escape. `[GeneratedRegex]` makes the compiler turn the pattern into fast C# code at
build time instead of interpreting it at run time.

```csharp
public int Compare(string? x, string? y)
{
    if (ReferenceEquals(x, y)) return 0;
    if (x is null) return -1;
    if (y is null) return 1;
```

`Compare` must return a **negative number** if `x` comes first, **0** if equal, **positive** if `y`
comes first. These three lines handle the trivial cases: same object, or nulls (nulls first).

```csharp
    List<Token> left = Tokenize(x);
    List<Token> right = Tokenize(y);

    int shared = Math.Min(left.Count, right.Count);
    for (int i = 0; i < shared; i++)
    {
        int result = CompareTokens(left[i], right[i]);
        if (result != 0) return result;
    }
```

Break both names into tokens, then walk them side by side. The first pair that differs decides
the order. `Math.Min` stops us running off the end of the shorter list.

```csharp
    if (left.Count != right.Count) return left.Count.CompareTo(right.Count);
```

All shared tokens were equal; the shorter name wins: `"Chapter 1"` before `"Chapter 1 Extra"`.

```csharp
    int ignoreCase = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    return ignoreCase != 0 ? ignoreCase : string.CompareOrdinal(x, y);
```

Tokens are equal but the strings differ (e.g. `"01"` and `"1"` both mean 1). We still need a
consistent answer, otherwise sorting could be unstable, so fall back to plain text comparison.
`? :` is the conditional operator: `condition ? ifTrue : ifFalse`.

```csharp
private static int CompareTokens(Token a, Token b)
{
    if (a.Number is decimal numberA && b.Number is decimal numberB)
        return numberA.CompareTo(numberB);

    if (a.Number is not null) return -1;
    if (b.Number is not null) return 1;

    return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
}
```

- `a.Number is decimal numberA` — pattern matching: "if `a.Number` holds a value, put it in
  `numberA`". Both numbers → compare numerically (so 1.5 < 2 < 10).
- Number vs text → number first.
- Text vs text → alphabetical, ignoring capitals.

`decimal` is used rather than `double` because it stores `1.5` exactly.

```csharp
private static List<Token> Tokenize(string value)
{
    var tokens = new List<Token>();
    int position = 0;

    foreach (Match match in NumberPattern().Matches(value))
    {
        if (match.Index > position)
            tokens.Add(new Token(value[position..match.Index], null));
```

Find every number in the string. `position` remembers where the previous token ended. If there is
text between the last token and this number, add it as a text token. `value[a..b]` is a *range*:
the characters from index `a` up to (not including) `b`.

```csharp
        bool parsed = decimal.TryParse(match.Value, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal number);
        tokens.Add(new Token(match.Value, parsed ? number : null));

        position = match.Index + match.Length;
    }
```

- `TryParse` returns `false` instead of crashing when the text can't be converted (a 40-digit
  scan ID is too big for `decimal`). Then we store it as text.
- `CultureInfo.InvariantCulture` — **important**: in Romanian, German, French… the decimal
  separator is `,`. Without this, `"1.5"` could be read wrongly on a PC with those regional settings.

```csharp
    if (position < value.Length)
        tokens.Add(new Token(value[position..], null));
    return tokens;
}

private readonly record struct Token(string Text, decimal? Number);
```

Add any text after the last number (`value[position..]` = "from position to the end"). The
`Token` is a tiny `record struct`: a lightweight value holding the text and, if it's a number,
its value. `decimal?` means "a decimal or nothing".

**Known limitation:** `1.10` is read as one-point-ten = 1.1, so it sorts before `1.9`. For chapter
numbers this is right (Chapter 1.1 < 1.9), for version numbers it would be wrong.

---

## 7. FolderGrouping.cs

### GroupingOptions

```csharp
public sealed class GroupingOptions
{
    public bool Chapter { get; set; } = true;
    public bool Volume { get; set; }
    public bool Part { get; set; } = true;
    public bool Pages { get; set; } = true;
    public bool Images { get; set; } = true;

    public GroupingOptions Clone() => (GroupingOptions)MemberwiseClone();
}
```

Five on/off switches matching the five checkboxes on the Settings page. `= true` sets the default;
`Volume` has no default so it's `false` (each volume becomes its own CBZ unless you tick it).

### Two ways a name can be a chapter folder

Real-world chapter folders are named in many ways:

| Name | Why it's a chapter |
|---|---|
| `Chapter 1`, `Ch.5`, `01 - Chapter 1` | **starts with** the keyword |
| `Berserk - Chapter 12`, `[Group] Vol.03 Ch.012` | **contains** the keyword followed by a number |

So every type gets **two** regular expressions.

```csharp
private const string Lead = @"^[\s\d._\-\[\]()#]*";
```

The start of rule 1. `^` = start of the name. `[ … ]*` = any number of spaces, digits, `.`, `_`,
`-`, `[`, `]`, `(`, `)`, `#`. Allows `01 - Chapter 1` or `[02] Vol 2`. Inside `[ ]`, the characters
`-`, `[` and `]` must be escaped with `\`.

```csharp
private const string Anywhere = @"(?<![a-z])";
private const string NumberAfter = @"\.?[\s_\-#:]*\d";
```

Pieces of rule 2:

- `(?<![a-z])` is a **negative look-behind**: "the character *before* the keyword must not be a
  letter". That's why `Watch 5` doesn't count as `ch 5`, and `Step 3` doesn't count as `ep 3`.
- `\.?` an optional dot (`Ch.12`), then `[\s_\-#:]*` any spaces/separators, then `\d` a digit.
  Requiring a number is what makes rule 2 safe: `Marvel Chapter` (no number) is not a chapter.

```csharp
private static readonly (Regex Start, Regex Anywhere) ChapterName = Build("chapter|chap|ch|episode|ep");
private static readonly (Regex Start, Regex Anywhere) VolumeName = Build("volume|vol");
...
private static (Regex, Regex) Build(string keywords) =>
(
    new Regex(Lead + "(?:" + keywords + ")(?![a-z])", Options),
    new Regex(Anywhere + "(?:" + keywords + ")" + NumberAfter, Options)
);
```

- `(Regex Start, Regex Anywhere)` is a **tuple**: two values travelling together, with names.
- `chapter|chap|ch|episode|ep` — any of these words (`|` means "or"). Webtoons often use *Episode*.
- `(?![a-z])` — **negative look-ahead**: the keyword must not be followed by another letter, so
  `Chronicles` doesn't match `ch` and `Partners` doesn't match `part`.
- `Options` = `IgnoreCase | CultureInvariant | Compiled`: capitals don't matter, behaves the same on
  every PC, and is turned into fast code once at start-up.

```csharp
private static bool Matches((Regex Start, Regex Anywhere) rule, string name) =>
    rule.Start.IsMatch(name) || rule.Anywhere.IsMatch(name);

public static bool IsGroupingFolder(string folderName, GroupingOptions options) =>
    (options.Chapter && Matches(ChapterName, folderName)) ||
    (options.Volume && Matches(VolumeName, folderName)) || ...
```

True if any **enabled** type matches either rule. `&&` short-circuits: if `options.Volume` is
false, the volume patterns aren't even run.

### ExtractNumber — the chapter number for ComicInfo.xml

```csharp
private static readonly Regex NumberAfterKeyword = new(
    Anywhere + @"(?:chapter|chap|ch|episode|ep|volume|vol|part|pt)\.?[\s_\-#:]*(\d+(?:\.\d+)?)", Options);
private static readonly Regex AnyNumber = new(@"\d+(?:\.\d+)?", Options);

public static string? ExtractNumber(string folderName)
{
    Match match = NumberAfterKeyword.Match(folderName);
    string? raw = match.Success ? match.Groups[1].Value : AnyNumber.Match(folderName) is { Success: true } m ? m.Value : null;
    if (raw is null) return null;

    return decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
        ? value.ToString(CultureInfo.InvariantCulture)
        : raw;
}
```

- The `( … )` around the number is a **capture group**; `match.Groups[1].Value` is the text it
  captured. For `Chapter 11.5` that's `11.5`.
- If there's no keyword, fall back to the first number anywhere; if there's none, return `null`.
- `is { Success: true } m` is a **property pattern**: "if the result's `Success` is true, call it `m`".
- Parsing as `decimal` and printing it back turns `011` into `11` and keeps `11.5` as `11.5`.

---

## 8. Models.cs

Small data types that the other files pass around. No logic.

```csharp
public enum InputKind { ComicFolder, Archive }
public sealed record InputItem(InputKind Kind, string Path);
```

One row of the list: "convert this folder" or "open this archive". This one-line `record` generates
a class with a constructor `new InputItem(kind, path)` and properties `Kind` and `Path`. Records
compare **by value**: two `InputItem`s with the same kind and path are equal, which lets the window
use them as dictionary keys to find "the row this result belongs to".

```csharp
public enum ChapterOutput { OneFilePerSeries, OneFilePerChapter }
public enum ArchiveAction { ExtractToFolder, ConvertToCbz }
```

The two new choices: the *Chapter folders* radio buttons on the main page, and what to do with
CBR/CB7/CBT files (Settings page).

```csharp
public sealed record PageEntry(string SourcePath, string EntryName, string RelativeSource, string Group = "");
```

One page going into a CBZ: where it is on disk, its name inside the ZIP (this decides the reading
order), a short path for the preview, and the **chapter it belongs to** (`Group`), used to put
chapter bookmarks in ComicInfo.xml. `= ""` is a default value, so older code can leave it out.

```csharp
public sealed class ComicJob
{
    public required string Name { get; init; }           // file name without .cbz
    public required string Series { get; init; }         // series name for ComicInfo.xml
    public string? Number { get; init; }                 // chapter number (one-per-chapter mode)
    public required string SourceFolder { get; init; }   // the folder whose images go in
    public required string ComicFolder { get; init; }    // the series folder; outputs go next to it
    public required string RelativeParent { get; init; } // for "Recreate the subfolders"
    public string OutputSubfolder { get; init; } = string.Empty; // e.g. the series folder for chapters
    public required IReadOnlyList<PageEntry> Pages { get; init; }
    public int MergedFolderCount { get; init; }
}
```

Everything needed to build one CBZ. `required … init` means you **must** fill it in when creating
the object and it can't be changed afterwards, so a `ComicJob` is never half-filled.
In one-CBZ-per-chapter mode, `SourceFolder` is the chapter (`…\Series\Chapter 3`) while
`ComicFolder` is the series (`…\Series`), so the output lands next to the series, inside a folder
named `OutputSubfolder` = the series name.

```csharp
public sealed record PlannedComic(InputItem Source, ComicJob Job, string OutputPath);
public sealed record PlannedExtraction(InputItem Source, string ArchivePath, string OutputPath,
    IReadOnlyList<ExtractEntry> Entries, bool ToCbz);
```

The planned work, each remembering which list row (`Source`) it came from. `ToCbz` is true when a
CBR/CB7/CBT will be converted to CBZ instead of extracted.

```csharp
public sealed class ConversionPlan
{
    public List<PlannedComic> Comics { get; } = new();
    public List<PlannedExtraction> Extractions { get; } = new();
    public List<string> Warnings { get; } = new();
    public HashSet<InputItem> EmptyInputs { get; } = new();
    public int TotalItems => Comics.Count + Extractions.Count;
    public int TotalPages => Comics.Sum(c => c.Job.Pages.Count) + Extractions.Sum(e => e.Entries.Count);
}
```

The whole plan. `EmptyInputs` lists the rows that produced nothing, so the window can mark them
"No images found". `TotalItems` and `TotalPages` are computed properties.

```csharp
public sealed record ConversionProgress(int Completed, int Total, int ItemNumber, int ItemCount, string CurrentItem);
public enum ItemStatus { Done, Skipped, Failed }
public sealed record ItemResult(InputItem Source, ItemStatus Status, string? OutputPath);
public sealed record ConversionSummary(int Created, int Extracted, int Skipped, int Failed);
```

Messages the background work sends to the window:

- `ConversionProgress` counts **pages** (`Completed` of `Total`) so the bar moves smoothly even when
  there's one huge CBZ, plus "item 2 of 5" for the status text.
- `ItemResult` is sent once per finished CBZ/folder, so each row can show ✓ / ✗ and remember where
  its result went (for double-click → Explorer).

---

## 9. ComicScanner.cs

The heart of the "which images belong to which comic" logic.

### Fields and constructor

```csharp
private readonly GroupingOptions _grouping;
private readonly bool _flattenPages;
private readonly ChapterOutput _chapterOutput;

public ComicScanner(GroupingOptions grouping, bool flattenPages,
    ChapterOutput chapterOutput = ChapterOutput.OneFilePerSeries)
```

The scanner remembers which folders count as chapters, how to name pages, and whether chapters are
merged or split. The last parameter has a default, so `new ComicScanner(options, true)` still works.

### Scan and ScanFolder — recursion

```csharp
public IReadOnlyList<ComicJob> Scan(string rootFolder, CancellationToken cancellationToken = default)
{
    var root = new DirectoryInfo(rootFolder);
    if (!root.Exists) throw new DirectoryNotFoundException($"Folder not found: {rootFolder}");

    var jobs = new List<ComicJob>();
    ScanFolder(root, root, jobs, cancellationToken);
    return jobs;
}

private void ScanFolder(DirectoryInfo root, DirectoryInfo folder, List<ComicJob> jobs, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    jobs.AddRange(BuildJobs(root, folder, cancellationToken));

    foreach (DirectoryInfo subfolder in GetSubfolders(folder))
    {
        if (IsGrouping(subfolder)) continue;
        ScanFolder(root, subfolder, jobs, cancellationToken);
    }
}
```

- `DirectoryInfo` describes a folder and can list its files and subfolders.
- `CancellationToken` lets the Cancel button stop a long scan; `ThrowIfCancellationRequested` throws
  an `OperationCanceledException` when Cancel was pressed.
- **Recursion**: `ScanFolder` calls itself for each subfolder, reaching every level of the tree.
- Chapter folders are skipped by the loop (`continue`) because `BuildJobs` already used them as
  chapters of *this* folder. Every other folder is scanned the same way, which is how
  `Library\Marvel\Spider-Man` is found.

### BuildJobs — one CBZ, or one per chapter

```csharp
private IEnumerable<ComicJob> BuildJobs(DirectoryInfo root, DirectoryInfo folder, CancellationToken cancellationToken)
{
    var looseImages = GetImages(folder)
        .Select(file => new SourceImage(file.FullName, file.Name, string.Empty))
        .ToList();
```

Images sitting directly in the folder (usually a cover). Their group is `""` (not in a chapter).

```csharp
    var chapters = new List<(DirectoryInfo Folder, List<SourceImage> Images)>();
    foreach (DirectoryInfo subfolder in GetSubfolders(folder).Where(IsGrouping))
    {
        var images = new List<SourceImage>();
        CollectEverything(subfolder, subfolder.Name, subfolder.Name, images, cancellationToken);
        if (images.Count > 0) chapters.Add((subfolder, images));
    }

    if (looseImages.Count == 0 && chapters.Count == 0) yield break;
```

Each chapter folder, in natural order, with all the images inside it. A list of **tuples** keeps
each folder together with its images. Empty chapters are dropped. No images at all → this folder is
not a comic.

This method returns `IEnumerable<ComicJob>` and uses **`yield return`**: an *iterator*. Instead of
building a list, it hands back jobs one at a time; `yield break` means "no more". It's a neat way
to return zero, one or many results from different branches.

```csharp
    if (_chapterOutput == ChapterOutput.OneFilePerSeries || chapters.Count == 0)
    {
        List<SourceImage> all = looseImages.Concat(chapters.SelectMany(c => c.Images)).ToList();
        yield return new ComicJob
        {
            Name = folder.Name,
            Series = folder.Name,
            SourceFolder = folder.FullName,
            ComicFolder = folder.FullName,
            RelativeParent = relativeParent,
            Pages = BuildEntries(folder.Name, all),
            MergedFolderCount = chapters.Count,
        };
        yield break;
    }
```

**All chapters in one CBZ** (or a folder with no chapters): loose images first, then every chapter.
`SelectMany` flattens "a list of chapters, each with a list of images" into one list of images.

```csharp
    if (looseImages.Count > 0)
    {
        yield return new ComicJob { Name = folder.Name, ..., OutputSubfolder = folder.Name, Pages = BuildEntries(folder.Name, looseImages) };
    }

    foreach ((DirectoryInfo chapter, List<SourceImage> images) in chapters)
    {
        yield return new ComicJob
        {
            Name = folder.Name + " - " + chapter.Name,
            Series = folder.Name,
            Number = FolderGrouping.ExtractNumber(chapter.Name),
            SourceFolder = chapter.FullName,
            ComicFolder = folder.FullName,
            RelativeParent = relativeParent,
            OutputSubfolder = folder.Name,
            Pages = BuildEntries(chapter.Name, images),
        };
    }
}
```

**One CBZ per chapter**: every chapter becomes `Series - Chapter N.cbz`, all inside a folder named
after the series (`OutputSubfolder`), so 30 chapters don't spill loose into your library folder. Any
loose images (cover) get their own `Series.cbz` in the same folder.
`foreach ((DirectoryInfo chapter, List<SourceImage> images) in chapters)` **deconstructs** each
tuple into two named variables.

### CollectEverything

```csharp
private void CollectEverything(DirectoryInfo folder, string label, string relativeFolder,
    List<SourceImage> images, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    foreach (FileInfo file in GetImages(folder))
        images.Add(new SourceImage(file.FullName, Path.Combine(relativeFolder, file.Name), label));

    foreach (DirectoryInfo subfolder in GetSubfolders(folder))
        CollectEverything(subfolder, label + " - " + subfolder.Name,
            Path.Combine(relativeFolder, subfolder.Name), images, cancellationToken);
}
```

Also recursive: the images in this chapter folder, then everything below it. There's **no chapter
check** here: inside a chapter, everything belongs to that chapter.

- `label` names the group: `"Volume 1 - Chapter 3"`. It becomes the folder name inside the CBZ when
  not numbering pages, and the bookmark name in ComicInfo.xml.
- `relativeFolder` is the path for the preview: `Volume 1\Chapter 3\001.webp`.

### BuildEntries — choosing names inside the CBZ

**Numbered (default):**

```csharp
if (_flattenPages)
{
    int width = PadWidth(images.Count);
    for (int i = 0; i < images.Count; i++)
    {
        SourceImage image = images[i];
        entries.Add(new PageEntry(image.Path, Number(i + 1, width) + Extension(image.Path), image.Relative, image.Group));
    }
    return entries;
}
```

Every page is renamed with its position: `001.webp`, `002.jpg` … `PadWidth` picks the number of
digits: at least 3, more if there are 1000+ pages. **Zero-padding matters**: without it a reader
would sort `10.png` before `2.png`. `i + 1` because people count from 1, C# from 0. The `Group` is
kept so bookmarks still know where chapters start.

**Keep chapter folders:**

```csharp
var groups = images.GroupBy(image => image.Group).ToList();
int folderWidth = PadWidth(groups.Count);
int folderNumber = 0;

foreach (IGrouping<string, SourceImage> group in groups)
{
    string folderName = group.Key.Length == 0
        ? Number(0, folderWidth) + " " + comicName
        : Number(++folderNumber, folderWidth) + " " + group.Key;

    List<SourceImage> pages = group.ToList();
    int pageWidth = PadWidth(pages.Count);
    for (int i = 0; i < pages.Count; i++)
    {
        string entryName = folderName + "/" + Number(i + 1, pageWidth) + Extension(pages[i].Path);
        entries.Add(new PageEntry(pages[i].Path, entryName, pages[i].Relative, pages[i].Group));
    }
}
```

- `GroupBy` gathers images by chapter label, keeping first-appearance order.
- Loose images go in folder `000 <comic name>` so they sort first; chapters get `001 Chapter 1`,
  `002 Chapter 1.5`, … so readers show chapters in *our* order. `++folderNumber` adds 1, then uses it.
- **ZIP files always use `/`** between folders, even on Windows, so `/` is hard-coded.

### Helpers

```csharp
private static IEnumerable<FileInfo> GetImages(DirectoryInfo folder) =>
    folder.EnumerateFiles()
        .Where(file => ImageFormats.IsImage(file.Name) && !IsHidden(file))
        .OrderBy(file => file.Name, NaturalSortComparer.Instance);

private static IEnumerable<DirectoryInfo> GetSubfolders(DirectoryInfo folder) =>
    folder.EnumerateDirectories()
        .Where(sub => !IsHidden(sub) && !sub.Name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
        .OrderBy(sub => sub.Name, NaturalSortComparer.Instance);

private static bool IsHidden(FileSystemInfo item) =>
    item.Name.StartsWith('.') || item.Attributes.HasFlag(FileAttributes.Hidden);
```

LINQ chains: list → filter → sort naturally. `EnumerateFiles` streams results instead of building
a big array. `__MACOSX` is junk macOS adds to ZIPs. `FileSystemInfo` is the shared base class of
`FileInfo` and `DirectoryInfo`, so one method serves both.

```csharp
private static string GetRelativeParent(DirectoryInfo root, DirectoryInfo folder)
{
    if (folder.Parent is null || PathsEqual(folder.FullName, root.FullName)) return string.Empty;
    string relative = Path.GetRelativePath(root.FullName, folder.Parent.FullName);
    return relative == "." ? string.Empty : relative;
}
```

For `root = Library` and `folder = Library\Marvel\Spider-Man`, this gives `Marvel`.

```csharp
internal static int PadWidth(int count) => Math.Max(3, count.ToString(CultureInfo.InvariantCulture).Length);
private static string Number(int value, int width) => value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

private static string Extension(string path)
{
    string extension = Path.GetExtension(path).ToLowerInvariant();
    return extension == ".jpeg" ? ".jpg" : extension;
}

private sealed record SourceImage(string Path, string Relative, string Group);
```

- `PadWidth(42)` = 3, `PadWidth(1500)` = 4. `Number(7, 3)` = `"007"`.
- Extensions are made lower-case, and `.jpeg` becomes `.jpg`, so names inside the CBZ are consistent.
- `SourceImage` is private: only the scanner uses it.

---

## 10. CbzWriter.cs

```csharp
public enum ArchiveCompression { Store, Fastest, Optimal }
```

The three choices in the *Compression* drop-down (*None*, *Fast*, *Smallest file*).

```csharp
public static void Write(ComicJob job, string destinationPath, ArchiveCompression compression,
    bool includeComicInfo = false, CancellationToken cancellationToken = default, Action? pageWritten = null)
{
    string? folder = Path.GetDirectoryName(destinationPath);
    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
```

Make sure the output folder exists. `CreateDirectory` does nothing if it already exists and creates
every missing level. `Action? pageWritten` is an optional **callback**: a function the caller passes
in, which we call after each page so the progress bar can move. `Action` = "a function with no
parameters that returns nothing".

```csharp
    CompressionLevel level = ToCompressionLevel(compression);

    using var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
    using var zip = new ZipArchive(file, ZipArchiveMode.Create);
```

- `FileStream` opens the file: `Create` (make new / replace), `Write` access, `FileShare.None`
  (nobody else may open it while we write).
- `ZipArchive` in `Create` mode writes ZIP structure into that stream.
- `using var` — when the method ends (normally or by an error) the ZIP is finished and the file is
  closed. **Without `using`, the ZIP's table of contents is never written and the file is broken.**

```csharp
    if (includeComicInfo)
    {
        ZipArchiveEntry info = zip.CreateEntry(ComicInfo.FileName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(info.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(ComicInfo.Build(job));
    }
```

`ComicInfo.xml` goes in first. `CreateEntry` makes an empty entry; `Open()` gives a stream to write
into it; a `StreamWriter` turns text into bytes. UTF-8 *without* a "byte order mark" is what XML
readers expect. Text compresses well, so this one entry always uses `Optimal`.

```csharp
    foreach (PageEntry page in job.Pages)
    {
        cancellationToken.ThrowIfCancellationRequested();
        zip.CreateEntryFromFile(page.SourcePath, page.EntryName, level);
        pageWritten?.Invoke();
    }
}
```

For each page: check Cancel, copy the image into the ZIP under its new name (calculating its
CRC-32 on the way), then report progress. `?.Invoke()` calls the callback only if one was given.

```csharp
public static CompressionLevel ToCompressionLevel(ArchiveCompression compression) => compression switch
{
    ArchiveCompression.Store => CompressionLevel.NoCompression,
    ArchiveCompression.Fastest => CompressionLevel.Fastest,
    _ => CompressionLevel.Optimal,
};
```

A `switch` expression translating our enum into .NET's. `_` catches everything else.

---

## 11. ComicInfo.cs

**ComicInfo.xml** is a small metadata file inside a CBZ, invented by the ComicRack app and now read
by Komga, Kavita, YACReader, Mihon/Tachiyomi and many others. It lets them show the series name,
chapter number and a list of chapters you can jump to. For a 3-page comic it looks like:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ComicInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Title>My Series</Title>
  <Series>My Series</Series>
  <PageCount>3</PageCount>
  <Notes>Created with Comic Archive Converter</Notes>
  <Pages>
    <Page Image="0" Type="FrontCover" Bookmark="Chapter 1" />
    <Page Image="1" />
    <Page Image="2" Bookmark="Chapter 2" />
  </Pages>
</ComicInfo>
```

```csharp
public static string Build(ComicJob job, string? appName = "Comic Archive Converter")
{
    var root = new XElement("ComicInfo",
        new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
        new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"));
```

**LINQ to XML** (`System.Xml.Linq`): `XElement` is an XML tag, `XAttribute` an attribute. Building
XML this way (instead of gluing strings) means special characters like `&` or `<` in a series name
are escaped correctly. The two `xmlns` attributes are what ComicRack itself writes; some readers
expect them.

```csharp
    string title = job.Name.StartsWith(job.Series + " - ", StringComparison.Ordinal)
        ? job.Name[(job.Series.Length + 3)..]
        : job.Name;
```

For `Series - Chapter 3` the title is just `Chapter 3`: cut off the series name and the 3 characters
of `" - "`. `[(n)..]` means "from position n to the end".

```csharp
    root.Add(new XElement("Title", title));
    root.Add(new XElement("Series", job.Series));
    if (job.Number is not null) root.Add(new XElement("Number", job.Number));
    root.Add(new XElement("PageCount", job.Pages.Count.ToString(CultureInfo.InvariantCulture)));
```

`Number` is only written for single chapters (from `FolderGrouping.ExtractNumber`).

```csharp
    var pages = new XElement("Pages");
    string? previousGroup = null;
    for (int i = 0; i < job.Pages.Count; i++)
    {
        var page = new XElement("Page", new XAttribute("Image", i));
        if (i == 0) page.Add(new XAttribute("Type", "FrontCover"));

        string group = job.Pages[i].Group;
        if (group.Length > 0 && group != previousGroup) page.Add(new XAttribute("Bookmark", group));
        previousGroup = group;

        pages.Add(page);
    }
    root.Add(pages);
```

One `<Page>` per image (`Image` is its 0-based position). The first is the cover. Whenever the
chapter (`Group`) changes, that page gets a **Bookmark**: that's how readers build a chapter list
inside one big CBZ.

```csharp
    return new XDocument(new XDeclaration("1.0", "utf-8", null), root).Declaration + Environment.NewLine + root;
}
```

The `<?xml … ?>` declaration line, then the formatted XML.

---

## 12. Crc32.cs

**CRC-32** is a 32-bit "fingerprint" of some bytes. A ZIP stores the CRC of every file when it is
written. When verifying, we recalculate it from the bytes we read; if one bit changed, the
numbers almost certainly differ.

```csharp
private static readonly uint[] Table = CreateTable();

private static uint[] CreateTable()
{
    var table = new uint[256];
    for (uint i = 0; i < table.Length; i++)
    {
        uint value = i;
        for (int bit = 0; bit < 8; bit++)
            value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
        table[i] = value;
    }
    return table;
}
```

A pre-computed lookup table for every possible byte value (0–255), so the main loop is fast.

- `uint` — unsigned 32-bit number (0 to 4,294,967,295).
- `value & 1` — is the lowest bit set? (`&` = bitwise AND)
- `value >> 1` — shift all bits one place right (halve it).
- `^` — bitwise XOR.
- `0xEDB88320` — the standard CRC-32 "polynomial" used by ZIP, written in hexadecimal. It's a
  fixed constant from the ZIP specification; you don't need to understand the maths to use it.

```csharp
public static uint Compute(ReadOnlySpan<byte> data)
{
    uint crc = 0xFFFFFFFFu;
    foreach (byte b in data)
        crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
    return ~crc;
}
```

The standard algorithm: start with all bits set, mix in each byte using the table, flip all bits at
the end (`~`). `ReadOnlySpan<byte>` is a fast, copy-free view of a block of bytes.

The test checks it with the official reference value: CRC-32 of `"123456789"` is `0xCBF43926`.

---

## 13. ImageValidation.cs

### The interface

```csharp
public interface IImageValidator
{
    bool TryValidate(Stream image, out string? error);
}
```

"Check this image; return `true` if it's OK, otherwise `false` and a reason in `error`."
The **Core library only defines the contract**. Full decoding needs Windows' graphics library,
which the Core deliberately doesn't use, so the Windows app provides `GdiImageValidator` and the
tests use `SignatureImageValidator`. Giving an object the helper it needs, instead of letting it
create one, is called **dependency injection**.

### Detecting the format from the bytes

Every image format starts with fixed **"magic bytes"**. The file name can lie (a PNG renamed to
`.jpg`); the bytes can't.

| Format | First bytes | How we know it's complete |
|---|---|---|
| PNG | `89 50 4E 47 0D 0A 1A 0A` | an `IEND` chunk at the end |
| JPEG | `FF D8 FF` | `FF D9` near the end |
| WebP | `RIFF` + size + `WEBP` | the size stored in bytes 4–7 fits inside the file |
| GIF | `GIF87a` or `GIF89a` | the `;` (0x3B) trailer byte at the end |
| BMP | `BM` | the size stored in bytes 2–5 fits inside the file |
| AVIF | `....ftypavif` | its chain of "boxes" ends exactly at the end of the file |

```csharp
public static ImageKind Detect(ReadOnlySpan<byte> head)
{
    if (head.Length >= 8 && head[..8].SequenceEqual(PngHeader)) return ImageKind.Png;
    if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ImageKind.Jpeg;
    if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return ImageKind.WebP;
    ...
    return ImageKind.Unknown;
}
```

- `ReadOnlySpan<byte>` is a fast, copy-free view of some bytes. `head[..8]` = the first 8;
  `head[8..12]` = bytes 8, 9, 10 and 11.
- `"RIFF"u8` is a **UTF-8 literal**: the four bytes of that text.
- `public static`, so `GdiImageValidator` can reuse it.

### TryValidate

```csharp
long length = image.Length;
if (length < 16) { error = "File is too small to be an image."; return false; }

image.Position = 0;
Span<byte> head = stackalloc byte[16];
image.ReadExactly(head);

int tailLength = (int)Math.Min(TailSize, length);
image.Position = length - tailLength;
byte[] tail = new byte[tailLength];
image.ReadExactly(tail);
```

Read the first 16 bytes and the last 64. `stackalloc` makes a tiny temporary buffer without
garbage-collector overhead; `ReadExactly` throws if it can't read that many bytes.

```csharp
try
{
    error = Detect(head) switch
    {
        ImageKind.Png => tail.AsSpan().IndexOf(PngEnd) >= 0 ? null : "PNG file is cut off (no IEND marker).",
        ImageKind.Jpeg => ContainsJpegEnd(tail) ? null : "JPEG file is cut off (no end-of-image marker).",
        ImageKind.WebP => BinaryPrimitives.ReadUInt32LittleEndian(head[4..8]) + 8L <= length
            ? null : "WebP file is cut off (shorter than its RIFF header says).",
        ImageKind.Gif => tail.AsSpan().LastIndexOf((byte)0x3B) >= 0 ? null : "GIF file is cut off (no trailer).",
        ImageKind.Bmp => BinaryPrimitives.ReadUInt32LittleEndian(head[2..6]) <= length
            ? null : "BMP file is cut off (shorter than its header says).",
        ImageKind.Avif => CheckIsoBoxes(image, length),
        _ => "Not a supported image (unknown file header).",
    };
}
finally
{
    image.Position = 0;
}
return error is null;
```

One `switch` expression: detect the format, then run that format's completeness check. `null` means
"no problem".

- **WebP** files are "RIFF" containers: bytes 4–7 hold the size of everything after byte 8, stored
  *little-endian* (lowest byte first). `BinaryPrimitives.ReadUInt32LittleEndian` reads that number.
  If the file is shorter than it claims, the download or copy was cut off. (The real WebP page from
  your manga folder, 27,202 bytes, stores exactly 27,194 + 8.)
- **BMP** stores its total size the same way at bytes 2–5.
- `finally` resets the position whatever happens, so the next reader starts at the beginning.

### CheckIsoBoxes (AVIF)

```csharp
while (position < length)
{
    if (length - position < 8) return "AVIF file has trailing garbage.";
    image.Position = position;
    image.ReadExactly(header[..8]);
    long size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);

    if (size == 1) { image.ReadExactly(header[8..16]); size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..16]); }
    else if (size == 0) size = length - position;

    if (size < 8 || position + size > length) return "AVIF file is cut off (a box runs past the end).";
    position += size;
}
return null;
```

An AVIF file is a chain of **boxes**; each starts with its own size (big-endian: highest byte first).
Hop from box to box: a size of `1` means "the real size is in the next 8 bytes", `0` means "until
the end of the file". If a box claims to run past the end, the file is cut off.

---

## 14. ArchiveVerifier.cs

```csharp
public sealed record VerificationResult(IReadOnlyList<string> Problems)
{
    public bool Success => Problems.Count == 0;
}
```

The result: a list of problems. No problems = success.

```csharp
public VerificationResult Verify(string cbzPath, IReadOnlyCollection<string>? expectedEntries,
    CancellationToken cancellationToken = default, Action? imageChecked = null)
{
    var problems = new List<string>();
    try
    {
        using ZipArchive zip = ZipFile.OpenRead(cbzPath);
        List<ZipArchiveEntry> images = zip.Entries
            .Where(entry => entry.Name.Length > 0 && ImageFormats.IsImage(entry.Name))
            .ToList();
```

Open the finished ZIP for reading. `entry.Name` is the file name part only; folder entries have an
empty `Name`, so `Length > 0` skips them.

```csharp
        if (images.Count == 0) problems.Add("The archive contains no images.");

        if (expectedEntries is not null)
        {
            var actualNames = new HashSet<string>(images.Select(e => e.FullName), StringComparer.Ordinal);
            foreach (string expected in expectedEntries.Where(name => !actualNames.Contains(name)))
                problems.Add($"Missing page: {expected}");

            if (images.Count != expectedEntries.Count)
                problems.Add($"Expected {expectedEntries.Count} images but the archive has {images.Count}.");
        }
```

Compare what's in the ZIP with what we meant to write. Every expected page must exist, and the
count must match (so nothing extra slipped in). A `HashSet` makes each lookup instant even with
thousands of pages.

```csharp
        foreach (ZipArchiveEntry entry in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (problems.Count >= MaxProblems) break;

            string? problem = CheckEntry(entry);
            if (problem is not null) problems.Add($"{entry.FullName}: {problem}");
            imageChecked?.Invoke();
        }
    }
    catch (InvalidDataException ex)
    {
        problems.Add("The archive is damaged: " + ex.Message);
    }

    return new VerificationResult(problems);
}
```

Check every image (calling `imageChecked` after each, for the progress bar). `ComicInfo.xml` isn't an image, so it's ignored here. Stop after 50 problems: if everything is broken, a list of 5,000 errors helps
nobody. If the ZIP itself is unreadable, .NET throws `InvalidDataException`, which becomes a problem
instead of a crash.

```csharp
private string? CheckEntry(ZipArchiveEntry entry)
{
    using var buffer = new MemoryStream(capacity: (int)Math.Min(entry.Length, int.MaxValue));
    try
    {
        using Stream data = entry.Open();
        data.CopyTo(buffer);
    }
    catch (InvalidDataException ex)
    {
        return "could not be read (" + ex.Message + ")";
    }
```

Read the whole image out of the ZIP into memory (decompressing it if needed). `capacity` reserves
the right amount of memory up front.

```csharp
    if (buffer.Length != entry.Length)
        return $"size is {buffer.Length} bytes but the archive says {entry.Length}.";

    uint crc = Crc32.Compute(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    if (crc != entry.Crc32)
        return "checksum mismatch, the data is damaged.";

    buffer.Position = 0;
    return _validator.TryValidate(buffer, out string? error) ? null : error;
}
```

Three checks, cheapest first:

1. **Size** matches what the ZIP's table of contents says.
2. **CRC-32** of the bytes matches the value stored when it was written.
3. **The image itself** is valid (the injected validator — on Windows, full decoding).

`GetBuffer()` gives the MemoryStream's internal array without copying it; we only look at the
filled part (`AsSpan(0, length)`).

---

## 15. ArchiveExtractor.cs

The reverse direction for **every** archive type. .NET can read ZIP but not RAR or 7-Zip, so this
file uses **SharpCompress**, which reads them all through one set of types:

| SharpCompress type | Meaning |
|---|---|
| `IArchive` | An opened archive. `Entries` lists everything inside it. |
| `IArchiveEntry` | One file inside: `Key` (its path), `IsDirectory`, `IsEncrypted`. |
| `IReader` | Walks through the archive **once, from start to end**, opening each entry in turn. |

```csharp
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
```

### Plan — what's inside, in reading order

```csharp
private static List<ExtractEntry> Plan(IArchive archive, bool flatten)
{
    var images = archive.Entries
        .Where(entry => !entry.IsDirectory && entry.Key is not null)
        .Where(entry => ImageFormats.IsImage(entry.Key!) && !IsJunk(entry.Key!))
        .ToList();

    if (images.Any(entry => entry.IsEncrypted))
        throw new InvalidDataException("The archive is password-protected.");
```

Keep image files only, skip `__MACOSX` junk, and refuse password-protected archives with a clear
message (instead of a confusing error half way through). Reading the list of entries is fast even
for big archives: only the headers are read, not the images.

```csharp
    List<string> names = images
        .Select(entry => entry.Key!)
        .OrderBy(Normalise, NaturalSortComparer.Instance)
        .ToList();
```

Natural order of the full path, so `Chapter 2/1.png` comes before `Chapter 10/1.png`. `Normalise`
turns `\` into `/` (Windows tools sometimes store backslashes) so both kinds sort the same.
`OrderBy(Normalise, …)` passes the method itself as the "sort by" function.

```csharp
    for (int i = 0; i < names.Count; i++)
    {
        string target = flatten
            ? (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0') + Path.GetExtension(names[i]).ToLowerInvariant()
            : Normalise(names[i]);
        plan.Add(new ExtractEntry(names[i], target));
    }
```

Flatten → renumber into one folder (`001.webp`, `002.png`…). Otherwise keep the path from the archive.

### Extract

```csharp
var targets = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (ExtractEntry item in plan) targets.TryAdd(Normalise(item.EntryName), item.RelativeTarget);
```

A lookup table: "archive entry name → where it goes". `TryAdd` ignores duplicates instead of crashing.

```csharp
string destination = Path.GetFullPath(destinationFolder);
string temp = destination + ".partial-" + Guid.NewGuid().ToString("N")[..8];
string tempRoot = temp + Path.DirectorySeparatorChar;
```

Work in a temporary folder like `Batman.partial-3f9a1c2e`. A `Guid` is a random unique ID, so we
never clash with (or touch) a real folder of yours.

```csharp
using IReader reader = archive.ExtractAllEntries();
while (reader.MoveToNextEntry())
{
    cancellationToken.ThrowIfCancellationRequested();
    if (reader.Entry.IsDirectory || reader.Entry.Key is null) continue;
    if (!targets.TryGetValue(Normalise(reader.Entry.Key), out string? relative)) continue;
```

**Why a reader?** Many RAR and 7-Zip files are **solid**: all files are compressed together as one
stream, so to get file 300 you must decompress files 1–299 first. Opening entries one by one would
decompress the start of the archive hundreds of times. A reader walks the archive once from start
to end, which is fast for every format. Entries we don't want (text files, junk) are simply passed.

```csharp
    string target = Path.GetFullPath(Path.Combine(temp, relative.Replace('/', Path.DirectorySeparatorChar)));

    if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"Unsafe path inside archive: {reader.Entry.Key}");
```

**Zip-slip protection — an important security check.** A malicious archive can contain an entry
named `../../Windows/evil.png`. `Path.Combine` + `GetFullPath` would resolve that to a place *outside*
our folder. So we compute the real final path and refuse anything that doesn't start inside the
temporary folder.

```csharp
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    using (Stream input = reader.OpenEntryStream())
    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        input.CopyTo(output);
    }
    written.Add(relative);
    imageExtracted?.Invoke();
}
```

Create sub-folders as needed, then copy the decompressed bytes into a new file. The `using ( … )`
blocks close both streams before moving on. Then report progress.

```csharp
if (written.Count != targets.Count)
    throw new InvalidDataException($"Only {written.Count} of {targets.Count} images could be read from the archive.");

MoveIntoPlace(temp, destination, written);
```

If the archive is damaged and some images never came out, fail instead of producing an incomplete
folder. Otherwise move the result into place.

```csharp
finally
{
    if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
}
```

Whatever happened (success, error, Cancel), the temporary folder is removed. After success it was
already moved, so it no longer exists.

```csharp
return plan
    .Select(item => Path.Combine(destination, item.RelativeTarget.Replace('/', Path.DirectorySeparatorChar)))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();
```

The final paths **in reading order** (the reader may have met them in a different order). The
CBR → CBZ conversion relies on this order.

### Open

```csharp
private static IArchive Open(string archivePath)
{
    try { return ArchiveFactory.Open(archivePath); }
    catch (Exception ex) when (ex is InvalidOperationException or ArchiveException or InvalidFormatException)
    {
        throw new InvalidDataException("Not a readable CBZ/CBR/CB7/CBT archive (" + ex.Message + ")", ex);
    }
}
```

`ArchiveFactory.Open` looks at the file's first bytes to detect the format (so a CBR that's really a
ZIP inside still works). If it's not an archive at all, we translate the library's errors into one
clear message. Passing `ex` as the second argument keeps the original error attached for debugging.

### MoveIntoPlace

```csharp
if (!Directory.Exists(destination))
{
    ...
    Directory.Move(temp, destination);
    return;
}

foreach (string relative in relativePaths)
{
    ...
    File.Move(Path.Combine(temp, localRelative), target, overwrite: true);
}
```

Usually the destination doesn't exist: rename the whole temp folder in one go (instant, same drive).
If it does exist (only possible with *Replace it*), move files in one by one, replacing same-named
files but **never deleting anything else** in your folder.

---

## 16. OutputPaths.cs

Decides the final file name when there's a clash.

```csharp
public static string? Resolve(string desiredPath, ExistingOutputPolicy policy,
    Func<string, bool> existsOnDisk, ISet<string> claimedThisRun, bool isFolder = false)
{
    bool Taken(string path) => claimedThisRun.Contains(path) || existsOnDisk(path);
```

- `Func<string, bool> existsOnDisk` — a *function passed as a parameter*. The service passes
  `File.Exists` for CBZs and `Directory.Exists` for folders; tests pass fake functions so they don't
  need real files.
- `claimedThisRun` — paths already produced in this run.
- `bool Taken(...)` — a **local function**: a helper that only exists inside `Resolve`.

```csharp
    string? chosen;
    if (!Taken(desiredPath))
        chosen = desiredPath;
    else if (claimedThisRun.Contains(desiredPath))
        chosen = NextFreeName(desiredPath, Taken, isFolder);
    else
        chosen = policy switch
        {
            ExistingOutputPolicy.Skip => null,
            ExistingOutputPolicy.Overwrite => desiredPath,
            _ => NextFreeName(desiredPath, Taken, isFolder),
        };

    if (chosen is not null) claimedThisRun.Add(chosen);
    return chosen;
}
```

1. Free → use it.
2. We already made a file with this name *in this run* (e.g. two different comics both called
   `Annual` going into one output folder) → always pick a new name, so the second never overwrites
   the first, whatever the setting.
3. Exists from before → follow the user's choice (*Keep both* = `Rename`, *Skip it* = `Skip`,
   *Replace it* = `Overwrite`). `null` means "skip".

```csharp
public static string NextFreeName(string path, Func<string, bool> isTaken, bool isFolder = false)
{
    string folder = Path.GetDirectoryName(path) ?? string.Empty;
    string name = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
    string extension = isFolder ? string.Empty : Path.GetExtension(path);

    for (int number = 2; ; number++)
    {
        string candidate = Path.Combine(folder, $"{name} ({number}){extension}");
        if (!isTaken(candidate)) return candidate;
    }
}
```

Try `Name (2).cbz`, `Name (3).cbz`, … until one is free. `for (…; ; …)` with no condition loops
until `return`. Folders are treated differently because `Vol. 1` has no extension; without
`isFolder`, `.NET` would think ` 1` is the extension and produce `Vol (2). 1`.

---

## 17. AppSettings.cs

### AppSettings

A plain class with one property per option, grouped like the Settings page, with the safest defaults:

```csharp
public ChapterOutput ChapterOutput { get; set; } = ChapterOutput.OneFilePerSeries;
public bool UseCustomOutputFolder { get; set; }
public string OutputFolder { get; set; } = string.Empty;
public bool KeepFolderStructure { get; set; }
public ExistingOutputPolicy ExistingOutput { get; set; } = ExistingOutputPolicy.Rename;

public bool FlattenPagesInCbz { get; set; } = true;
public bool AddComicInfo { get; set; } = true;
public ArchiveCompression Compression { get; set; } = ArchiveCompression.Store;
public GroupingOptions Grouping { get; set; } = new();

public ArchiveAction ArchiveAction { get; set; } = ArchiveAction.ExtractToFolder;
public bool FlattenWhenExtracting { get; set; }
public bool VerifyArchives { get; set; } = true;
public bool OpenOutputWhenDone { get; set; }
public bool PlaySoundWhenDone { get; set; } = true;

public int[]? WindowBounds { get; set; }
public bool WindowMaximized { get; set; }
```

`WindowBounds` stores the window's x, y, width and height as 4 numbers, so it reopens where you left it.

### SettingsStore

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
};
```

**JSON** is a simple text format. `System.Text.Json` (built into .NET) converts objects to and from
JSON. `WriteIndented` makes the file readable; `JsonStringEnumConverter` saves enums as `"Rename"`
instead of `0`.

```csharp
public SettingsStore(string? filePath = null)
{
    FilePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ComicArchiveConverter", "settings.json");
}
```

Normally `C:\Users\<you>\AppData\Roaming\ComicArchiveConverter\settings.json`, the standard place
for per-user settings. Tests pass their own path so they don't touch your real settings.

```csharp
public AppSettings Load()
{
    try
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        string json = File.ReadAllText(FilePath);
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.Grouping ??= new GroupingOptions();
        if (!Enum.IsDefined(settings.ChapterOutput)) settings.ChapterOutput = ChapterOutput.OneFilePerSeries;
        ...
        return settings;
    }
    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
    {
        return new AppSettings();
    }
}
```

- No file (first run) → defaults. Broken file → defaults instead of crashing.
- A settings file from **version 2.0** simply lacks the new properties; they keep their defaults.
  That's why adding settings never breaks old files.
- `??=` means "if null, set it to…". `Enum.IsDefined` protects against a hand-edited file containing
  a number that isn't a valid choice (the combo boxes would crash on it).

```csharp
public void Save(AppSettings settings)
{
    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
    string temp = FilePath + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
    File.Move(temp, FilePath, overwrite: true);
}
```

**Write-then-rename**: if the PC crashes mid-write, only the `.tmp` file is damaged; the real file is
replaced in one step. The same idea protects your CBZs.

---

## 18. ConversionService.cs

The coordinator. It owns no special algorithm; it calls the other classes in the right order and
reports what happens.

### CreatePlan

```csharp
var scanner = new ComicScanner(_settings.Grouping, _settings.FlattenPagesInCbz, _settings.ChapterOutput);
var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var seenArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (InputItem input in inputs)
{
    cancellationToken.ThrowIfCancellationRequested();
    try
    {
        if (input.Kind == InputKind.ComicFolder)
            PlanFolder(input, scanner, plan, seenFolders, cancellationToken);
        else if (seenArchives.Add(Path.GetFullPath(input.Path)))
            PlanArchive(input, plan);
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        plan.Warnings.Add($"{Path.GetFileName(input.Path)}: {ex.Message}");
        plan.EmptyInputs.Add(input);
    }
}
```

- `HashSet.Add` returns `false` if the item was already there: a neat way to skip duplicates.
- One bad input (a deleted folder, a corrupt CBR) becomes a warning and its row is marked; the rest
  still run.
- `catch (OperationCanceledException) { throw; }` comes **before** the general `catch`, so Cancel
  is passed up instead of becoming a warning. C# uses the first `catch` that matches.

```csharp
private void PlanFolder(InputItem input, ComicScanner scanner, ConversionPlan plan, ...)
{
    IReadOnlyList<ComicJob> jobs = scanner.Scan(input.Path, cancellationToken);
    if (jobs.Count == 0)
    {
        plan.Warnings.Add($"No images ({string.Join(", ", ImageFormats.Extensions)}) found in {input.Path}");
        plan.EmptyInputs.Add(input);
        return;
    }
    foreach (ComicJob job in jobs.Where(job => seenFolders.Add(job.SourceFolder)))
        plan.Comics.Add(new PlannedComic(input, job, GetCbzPath(job)));
}
```

The warning now **lists the supported types**, so "why wasn't my folder found?" answers itself.
`string.Join(", ", …)` glues a list into one string.

```csharp
private void PlanArchive(InputItem input, ConversionPlan plan)
{
    bool toCbz = _settings.ArchiveAction == ArchiveAction.ConvertToCbz && !ArchiveFormats.IsZip(input.Path);
    IReadOnlyList<ExtractEntry> entries = ArchiveExtractor.Plan(input.Path, flatten: !toCbz && _settings.FlattenWhenExtracting);
    ...
    string output = toCbz ? GetCbzPathForArchive(input.Path) : GetExtractFolder(input.Path);
    plan.Extractions.Add(new PlannedExtraction(input, input.Path, output, entries, toCbz));
}
```

Decide per archive: CBZ is always extracted; CBR/CB7/CBT follow the setting.

### Where outputs go

```csharp
public string GetCbzPath(ComicJob job)
{
    string baseFolder;
    if (!UsesOutputFolder)
        baseFolder = Path.GetDirectoryName(job.ComicFolder) ?? job.ComicFolder;
    else if (_settings.KeepFolderStructure)
        baseFolder = Path.Combine(_settings.OutputFolder, job.RelativeParent);
    else
        baseFolder = _settings.OutputFolder;

    return Path.Combine(baseFolder, job.OutputSubfolder, job.Name + ".cbz");
}
```

| Setting | `Manga\Series` with one CBZ per series | …with one CBZ per chapter |
|---|---|---|
| Next to the original | `Manga\Series.cbz` | `Manga\Series\Series - Chapter 1.cbz` |
| Output folder `D:\Out` | `D:\Out\Series.cbz` | `D:\Out\Series\Series - Chapter 1.cbz` |

`Path.Combine` skips empty parts, so an empty `OutputSubfolder` adds nothing.

### Run

```csharp
int passes = _settings.VerifyArchives ? 2 : 1;
int totalUnits = Math.Max(1, plan.Comics.Sum(c => c.Job.Pages.Count * passes)
    + plan.Extractions.Sum(e => e.Entries.Count * (e.ToCbz ? passes + 1 : passes)));
int doneUnits = 0, itemNumber = 0;
string current = string.Empty;

void Report() => progress?.Report(new ConversionProgress(Math.Min(doneUnits, totalUnits), totalUnits,
    itemNumber, plan.TotalItems, current));
void Advance()
{
    doneUnits++;
    Report();
}
```

**Progress is counted in pages.** Each page written is one unit, each page verified another
(CBR → CBZ also extracts first, hence `+ 1`). With 30 chapters in *one* CBZ, the old "1 of 1 item"
progress would sit at 0 % for a long time; counting pages makes the bar move smoothly.
`Report` and `Advance` are **local functions** that can use the method's variables. `Advance` is
handed to the writer, verifier and extractor as their callback.

```csharp
foreach (PlannedComic comic in plan.Comics)
{
    cancellationToken.ThrowIfCancellationRequested();
    itemNumber++;
    current = comic.Job.Name;
    int unitsAtStart = doneUnits;
    Report();

    (Outcome outcome, string? output) = CreateCbz(comic, claimed, log, cancellationToken, Advance);
    Count(outcome);
    results?.Report(new ItemResult(comic.Source, ToStatus(outcome), output));

    doneUnits = unitsAtStart + comic.Job.Pages.Count * passes;
    Report();
}
```

- `CreateCbz` returns a **tuple** `(Outcome, string?)`, deconstructed into two variables.
- `results?.Report(...)` tells the window which row finished and where its result is.
- If an item is skipped or fails early, it didn't call `Advance` for every page; resetting
  `doneUnits` keeps the bar honest.

The extraction loop is the same, choosing between `ConvertArchiveToCbz` and `ExtractArchive`.

### WriteVerifiedCbz — the safe write sequence

```csharp
string temp = target + ".partial-" + Guid.NewGuid().ToString("N")[..8];
try
{
    CbzWriter.Write(job, temp, _settings.Compression, _settings.AddComicInfo, cancellationToken, advance);

    if (_settings.VerifyArchives)
    {
        VerificationResult result = _verifier.Verify(temp, job.Pages.Select(page => page.EntryName).ToList(), cancellationToken, advance);
        if (!result.Success) { log?.Report(...); return Outcome.Failed; }
    }

    File.Move(temp, target, overwrite: true);
    log?.Report(new LogMessage(LogLevel.Success, $"Created {target} (...)"));
    return Outcome.Done;
}
catch (OperationCanceledException) { throw; }
catch (Exception ex) { log?.Report(...); return Outcome.Failed; }
finally { TryDeleteFile(temp); }
```

The most important safety sequence in the program, shared by folder → CBZ and CBR → CBZ:

1. Write to `Series.cbz.partial-1a2b3c4d`, never directly to `Series.cbz`.
2. Verify that temporary file.
3. Only if everything passed, **rename** it to `Series.cbz` (instant, all-or-nothing).
4. `finally` deletes the temp file if anything went wrong (error, failed check, Cancel).

So a `Series.cbz` on your disk is **always** complete and checked.

### ExtractArchive

Resolve the folder name (`isFolder: true`), call `ArchiveExtractor.Extract`, then (with
verification on) check each extracted image. A bad image becomes a warning but the good ones are
kept: 99 of 100 pages is better than none.

### ConvertArchiveToCbz — CBR/CB7/CBT → CBZ

```csharp
string unpacked = target + ".unpack-" + Guid.NewGuid().ToString("N")[..8];
try
{
    IReadOnlyList<string> files = ArchiveExtractor.Extract(extraction.ArchivePath, unpacked, flatten: false, cancellationToken, advance);

    for (int i = 0; i < files.Count; i++)
    {
        string relative = Path.GetRelativePath(unpacked, files[i]).Replace('\\', '/');
        int slash = relative.LastIndexOf('/');
        string group = slash > 0 ? relative[..slash].Replace("/", " - ") : string.Empty;
        string entryName = _settings.FlattenPagesInCbz ? <numbered> : relative;
        pages.Add(new PageEntry(files[i], entryName, relative, group));
    }

    var job = new ComicJob { Name = name, Series = name, SourceFolder = unpacked, ComicFolder = unpacked, RelativeParent = string.Empty, Pages = pages };
    return (WriteVerifiedCbz(job, target, from, log, cancellationToken, advance), target);
}
...
finally
{
    if (Directory.Exists(unpacked)) Directory.Delete(unpacked, recursive: true);
}
```

**Reuse instead of rewrite:** extract to a hidden temporary folder, build an ordinary `ComicJob`
from the extracted files (folders inside the archive become chapter groups, for bookmarks), then
call the same `WriteVerifiedCbz`. The temporary folder is always deleted afterwards. The original
CBR is never touched.

---

# Part 3 — The Windows app

## 19. Program.cs

```csharp
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm());
    }
}
```

| Line | Explanation |
|---|---|
| `Main` | Every C# program starts here. |
| `[STAThread]` | Windows dialogs, the clipboard and drag-and-drop need the UI thread to be a "single-threaded apartment" (a COM rule). Forget this and Open dialogs crash. |
| `SetHighDpiMode(PerMonitorV2)` | Crisp text on scaled/4K monitors, even when dragging between screens with different scaling. |
| `EnableVisualStyles` | Modern-looking buttons instead of Windows 95 style. |
| `SetCompatibleTextRenderingDefault(false)` | Use the newer, better text renderer. |
| `ThreadException += …` | If an unexpected error reaches the UI, show it instead of closing the program. `(_, e) =>` is an event handler; `_` means "I don't use this parameter". |
| `Application.Run(new MainForm())` | Show the window and process clicks/keys until it's closed (the "message loop"). |

---

## 20. Theme.cs

All colours, fonts and button styles in one place, so the whole app looks consistent and a colour
change is one line.

```csharp
public static readonly Color Accent = Color.FromArgb(79, 70, 229);   // indigo
public static readonly Color Background = Color.FromArgb(244, 246, 251);
...
public static readonly Font Body = new("Segoe UI", 9.75F);
public static readonly Font Title = new("Segoe UI Semibold", 16F);
```

`Color.FromArgb(r, g, b)` makes a colour from red/green/blue values (0–255). **Segoe UI** is the
Windows system font, so the app looks native. `F` marks a `float` number.

```csharp
public static Button PrimaryButton(string text) => Style(new Button { Text = text }, Accent, Color.White, Accent);
public static Button SecondaryButton(string text) => Style(new Button { Text = text }, Surface, Text, Border);

private static Button Style(Button button, Color back, Color fore, Color border)
{
    button.FlatStyle = FlatStyle.Flat;
    button.BackColor = back;
    button.ForeColor = fore;
    button.FlatAppearance.BorderColor = border;
    button.FlatAppearance.MouseOverBackColor = back == Accent ? AccentDark : AccentLight;
    button.AutoSize = true;
    button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
    button.MinimumSize = new Size(96, 36);
    button.Padding = new Padding(12, 0, 12, 0);
    button.Cursor = Cursors.Hand;
    button.UseVisualStyleBackColor = false;
    return button;
}
```

- `FlatStyle.Flat` turns off the classic 3-D look so our own colours are used.
- **`AutoSize` + `MinimumSize`** is the fix for buttons being cut off: a button is never smaller
  than 96×36 and grows to fit its text at any Windows text-scaling setting.
- `UseVisualStyleBackColor = false` — otherwise Windows ignores `BackColor`.
- `MakePrimary` flips a button between the two styles (Convert ↔ Cancel).

`Label`, `CheckBox`, `Radio` and `Combo` are small **factory methods** that create a control with the
standard font, colour and spacing. `RoundedRectangle` builds a `GraphicsPath` (a shape made of
lines and arcs) for the rounded drop zone: four quarter-circle arcs, one per corner, joined by
`CloseFigure`.

---

## 21. Controls.cs

Three small **custom controls**: classes that inherit from a WinForms control and override
`OnPaint` to draw themselves.

### HeaderPanel

```csharp
internal sealed class HeaderPanel : Panel
{
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var brush = new LinearGradientBrush(ClientRectangle, Theme.Accent, Theme.AccentSecond, LinearGradientMode.Horizontal))
            g.FillRectangle(brush, ClientRectangle);

        g.DrawImage(_icon, new Rectangle(x, (Height - 44) / 2, 44, 44));
        TextRenderer.DrawText(g, Text, Theme.Title, new Point(x, 12), Color.White, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Subtitle, Theme.Body, new Point(x + 1, 44), ..., TextFormatFlags.NoPadding);
    }
}
```

- `OnPaint` is called by Windows whenever the control needs drawing; `e.Graphics` is the "canvas".
- `LinearGradientBrush` paints a smooth indigo → blue fade, like the icon.
- `TextRenderer.DrawText` draws text the same way as normal WinForms labels (crisp, ClearType).
- `DoubleBuffered = true` draws off-screen first, so there's no flicker; `ResizeRedraw = true`
  repaints the whole thing when the window is resized.

### DropPanel

A white card with a **dashed rounded border**. `Highlight` turns it light indigo with a solid accent
border while files are dragged over the window, so it's obvious where to drop:

```csharp
public bool Highlight
{
    get => _highlight;
    set
    {
        if (_highlight == value) return;
        _highlight = value;
        Invalidate();
    }
}
```

A property with a body: when it changes, `Invalidate()` asks Windows to repaint. Returning early
when nothing changed avoids needless repaints during a drag (which fires many events per second).

```csharp
using var pen = new Pen(_highlight ? Theme.Accent : Theme.Border, 2F) { DashStyle = DashStyle.Dash };
g.DrawPath(pen, path);
```

### EmptyHint

What the drop zone shows while the list is empty: an arrow-into-tray drawing (three `DrawLine` /
`DrawLines` calls with round line caps) and two lines of text explaining what to drop. It replaces
the old *Add comic folder / Add CBZ files / Add all CBZs* buttons: the screen itself tells you what
to do.

---

## 22. GdiImageValidator.cs

The full image check used by the real program. **GDI+** is Windows' built-in image library, used
through `System.Drawing`.

```csharp
public bool TryValidate(Stream image, out string? error)
{
    if (!_signature.TryValidate(image, out error)) return false;

    Span<byte> head = stackalloc byte[16];
    image.Position = 0;
    image.ReadExactly(head);
    image.Position = 0;

    ImageKind kind = SignatureImageValidator.Detect(head);
    if (kind is ImageKind.WebP or ImageKind.Avif) return true;
```

1. First the structural check from Core (header + completeness). That catches cut-off files, which
   GDI+ sometimes "decodes" by filling the missing part with grey.
2. **GDI+ cannot decode WebP or AVIF**, so those stop here: they passed the structural check, and
   the ZIP checksum already proved the bytes are exactly what we wrote.

```csharp
    try
    {
        using Image picture = Image.FromStream(image, useEmbeddedColorManagement: false, validateImageData: true);
        if (picture.Width <= 0 || picture.Height <= 0) { error = "Image has no size."; return false; }

        using var target = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.DrawImage(picture, 0, 0, 1, 1);

        error = null;
        return true;
    }
    catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
    {
        error = "Windows could not decode the image.";
        return false;
    }
    finally
    {
        image.Position = 0;
    }
}
```

3. JPG, PNG, GIF and BMP are fully decoded. GDI+ decodes lazily (only when pixels are needed), so
   **drawing** the image forces every pixel to be decoded. Drawing onto a 1×1 bitmap does that
   without the memory of a full-size copy (a 4000×6000 page would need ~96 MB).
4. GDI+ reports a corrupt image with odd exception types (notably `OutOfMemoryException`, for
   historical reasons). Only those are caught; anything else is a real bug and should surface.

---

## 23. NativeMethods.cs

Some Windows features aren't wrapped by WinForms. **P/Invoke** lets C# call a function in a Windows
DLL directly. Here: making the taskbar button flash when a long conversion finishes while you're in
another window.

```csharp
[StructLayout(LayoutKind.Sequential)]
private struct FLASHWINFO
{
    public uint cbSize;
    public IntPtr hwnd;
    public uint dwFlags;
    public uint uCount;
    public uint dwTimeout;
}
```

A C# copy of the structure the Windows function expects. `Sequential` keeps the fields in exactly
this order in memory, as Windows requires. `IntPtr` is a "handle": Windows' ID for our window.

```csharp
[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool FlashWindowEx(ref FLASHWINFO info);
```

`DllImport` says "this method lives in `user32.dll`". `extern` = no body here, it's in Windows.
`ref` passes the structure by reference, as the C function expects a pointer.

```csharp
public static void FlashTaskbar(IntPtr windowHandle)
{
    var info = new FLASHWINFO
    {
        cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
        hwnd = windowHandle,
        dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
        uCount = uint.MaxValue,
        dwTimeout = 0,
    };
    FlashWindowEx(ref info);
}
```

`cbSize` tells Windows the structure's size (how it checks we're using the right version).
`FLASHW_ALL | FLASHW_TIMERNOFG` = flash the title bar and taskbar button until the window is brought
to the front.

---

## 24. MainForm.cs

The window. It's the longest file, but each part is simple: create controls, place them, react to
events. Read it in this order.

### 24.1 The class and its state

```csharp
[DesignerCategory("Code")]
public sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly Icon? _appIcon = LoadAppIcon(null);
    private CancellationTokenSource? _cancellation;
    private bool _loadingSettings;
    private string? _lastOutputFolder;
```

- `: Form` — MainForm **inherits** from WinForms' `Form`, so it *is* a window, plus what we add.
- `[DesignerCategory("Code")]` opens the file as code in Visual Studio, not in the drag-and-drop designer.
- `_cancellation` exists **only while work is running**, so it doubles as the busy flag:
  `private bool IsBusy => _cancellation is not null;`
- `_loadingSettings` is true while settings are copied *into* the controls, so those changes don't
  trigger "a setting changed, save it" (see 24.8).
- `_lastOutputFolder` is where the "Open output folder" link points.

### 24.2 The controls

Every control is a field, created once with an object initialiser, e.g.:

```csharp
private readonly ListView _list = new()
{
    View = View.Details,            // a table with columns
    FullRowSelect = true,           // clicking selects the whole row
    HideSelection = false,          // keep the selection visible when focus moves
    Dock = DockStyle.Fill,          // fill the space it's placed in
    AllowDrop = true,               // accept drag and drop
    BorderStyle = BorderStyle.None, // the drop zone draws the border
    ShowItemToolTips = true,        // hover a row to see its full path
};
private readonly RadioButton _oneCbzPerSeries = Theme.Radio("All chapters in one CBZ");
private readonly RadioButton _oneCbzPerChapter = Theme.Radio("One CBZ per chapter");
private readonly Button _removeButton = Theme.SecondaryButton("Remove");
private readonly Button _clearButton = Theme.SecondaryButton("Clear list");
```

They're grouped by page: **Convert** (drop zone, list, chapter choice, Remove/Clear), **Preview**
(summary + tree), **Log** (text box), **Settings** (all other options) and the **footer** (progress
bar, status text, Preview and Convert buttons).

### 24.3 The constructor

```csharp
public MainForm()
{
    _settings = _settingsStore.Load();
    _header = new HeaderPanel(LoadAppIcon(new Size(64, 64))?.ToBitmap()) { Text = "Comic Archive Converter", Subtitle = "..." };

    Text = "Comic Archive Converter " + typeof(MainForm).Assembly.GetName().Version?.ToString(3);
    if (_appIcon is not null) Icon = _appIcon;
    Font = Theme.Body;
    BackColor = Theme.Background;
    MinimumSize = new Size(820, 600);
    ...
    KeyPreview = true;

    BuildLayout();
    WireEvents();
    ApplySettingsToScreen(_settings);
    RestoreWindowBounds();
    UpdateControlStates();
}
```

Load settings → create the header with the 64-pixel icon → window title with the version
(`ToString(3)` gives `2.1.0`) → build the layout → connect events → fill the controls from the
settings → put the window where it was last time → enable/disable controls.
`KeyPreview = true` lets the form see key presses first (for the shortcuts).

### 24.4 Layout and "tabs"

```csharp
var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));   // header
root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // tabs
root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // current page
root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // progress + buttons
```

A `TableLayoutPanel` is a grid. `Absolute` = fixed pixels; `AutoSize` = exactly as tall as its
contents; `Percent` rows share what's left. So the page gets all the spare space and the footer is
never squeezed.

```csharp
private RadioButton AddPage(string title, Control page)
{
    var tab = new RadioButton
    {
        Text = title,
        Appearance = Appearance.Button,
        FlatStyle = FlatStyle.Flat,
        ...
    };
    tab.FlatAppearance.CheckedBackColor = Theme.AccentLight;
    tab.CheckedChanged += (_, _) =>
    {
        tab.ForeColor = tab.Checked ? Theme.Accent : Theme.Muted;
        page.Visible = tab.Checked;
        if (tab.Checked) page.BringToFront();
    };
    ...
}
```

The tab bar is a row of **radio buttons drawn as flat buttons**. Radio buttons in the same container
already behave like tabs (choosing one un-chooses the others), so each tab just shows its own page
when checked. This looks more modern than the classic `TabControl`.

### 24.5 The Convert page — why the buttons always fit

```csharp
var chapterChoice = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
chapterChoice.Controls.Add(Theme.Label("Chapter folders:", Theme.BodyBold));
chapterChoice.Controls.Add(_oneCbzPerSeries);
chapterChoice.Controls.Add(_oneCbzPerChapter);

var listButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
listButtons.Controls.Add(_clearButton);
listButtons.Controls.Add(_removeButton);

var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
```

**The bug in 2.0:** the buttons were stacked vertically next to the list, in a row that was 30 % of
the window's height. On a smaller window there wasn't room for the fifth button, so *Clear list*
was cut off.

**The fix:** the buttons sit *below* the list, in a column whose width is `AutoSize`: the table
gives that column exactly what the buttons need **first**, and the chapter choice gets whatever is
left (`Percent, 100`). If space runs out, the chapter choice wraps (`WrapContents = true`); the
buttons never shrink. Together with `MinimumSize` on every button (Theme.cs) and the window's own
`MinimumSize`, nothing can be cut off.

The list itself sits in the `DropPanel` together with the `EmptyHint`; `UpdateControlStates` shows
one or the other.

### 24.6 The Settings page

```csharp
void Section(string title, string? help = null) { ... }
void AddRow(params Control[] controls) { ... }

Section("Where to save");
AddRow(_saveNextTo);
AddRow(_saveToFolder, _outputFolderBox, _browseButton);
AddRow(Indent(_keepStructure));
...
```

Two **local functions** keep the page readable: `Section` adds a coloured heading (and optional
grey help text), `AddRow` adds one line of controls. The whole stack sits in a `Panel` with
`AutoScroll = true`, so it scrolls on small screens.

```csharp
private static void LinkRadios(RadioButton a, RadioButton b)
{
    a.CheckedChanged += (_, _) => { if (a.Checked) b.Checked = false; };
    b.CheckedChanged += (_, _) => { if (b.Checked) a.Checked = false; };
}
```

Radio buttons only switch each other off **when they're in the same container**. *Next to the
original* and *In this folder* are on different rows (different containers), so they're linked by hand.

### 24.7 Events

```csharp
foreach (Control target in new Control[] { this, _dropPanel, _list, _emptyHint, _previewTree })
{
    target.AllowDrop = true;
    target.DragEnter += OnDragEnter;
    target.DragOver += OnDragEnter;
    target.DragLeave += (_, _) => SetDropHighlight(false);
    target.DragDrop += OnDragDrop;
}
```

Drag events go to the control **under the mouse**, so every big control is wired up.
`+=` subscribes a method to an event.

```csharp
_convertButton.Click += async (_, _) =>
{
    if (IsBusy) _cancellation?.Cancel();
    else await ConvertAsync();
};
```

One button, two jobs: **Convert** normally, **Cancel** while working (its text and colour change in
`UpdateControlStates`). The `async` lambda can `await` without freezing the window.

```csharp
foreach (Control control in new Control[] { _oneCbzPerSeries, ..., _playSound })
{
    switch (control)
    {
        case CheckBox box: box.CheckedChanged += (_, _) => OnSettingChanged(); break;
        case RadioButton radio: radio.CheckedChanged += (_, _) => { if (radio.Checked) OnSettingChanged(); }; break;
    }
}
```

Every setting saves itself the moment it changes. `switch` with `case CheckBox box:` is **type
pattern matching**: "if it's a CheckBox, call it `box`". Radio buttons fire `CheckedChanged` twice
(one turning off, one on), so only the one turning *on* saves.

### 24.8 Keyboard shortcuts

```csharp
protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
{
    switch (keyData)
    {
        case Keys.Control | Keys.V when _list.Focused || _emptyHint.Focused || _convertTab.Checked && !_outputFolderBox.Focused:
            PasteFiles();
            return true;
        case Keys.Delete when _list.Focused:
            RemoveSelected();
            return true;
        case Keys.F5:
            _ = PreviewAsync();
            return true;
        ...
    }
    return base.ProcessCmdKey(ref msg, keyData);
}
```

`ProcessCmdKey` sees every key press before the focused control. `Keys.Control | Keys.V` combines
two flags (Ctrl **and** V). `when` adds a condition, e.g. Ctrl+V only pastes files when you're not
typing in the output-folder box. Returning `true` means "handled". `_ = PreviewAsync();` starts the
task without waiting for it (`_` discards the result).

### 24.9 Drag and drop

```csharp
private void OnDragEnter(object? sender, DragEventArgs e)
{
    bool ok = !IsBusy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
    e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
    SetDropHighlight(ok);
}

private void OnDragDrop(object? sender, DragEventArgs e)
{
    SetDropHighlight(false);
    if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
}
```

Show the "copy" cursor and light up the drop zone when files are dragged over; on drop, Windows
hands us an array of paths. `is string[] paths` checks the type and names it in one step.

### 24.10 The list

```csharp
private sealed class Row
{
    public required InputItem Input { get; init; }
    public required ListViewItem Item { get; init; }
    public List<string> Outputs { get; } = new();
    public int Done { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
}
```

Each list row's `Tag` (a slot every WinForms control has for your own data) holds a `Row`: the input,
its list item, and what happened to it.

```csharp
private void AddPaths(IEnumerable<string> paths)
{
    ...
    foreach (string path in paths)
    {
        if (Directory.Exists(path)) added += AddInput(InputKind.ComicFolder, path) ? 1 : 0;
        else if (File.Exists(path) && ArchiveFormats.IsArchive(path)) added += AddInput(InputKind.Archive, path) ? 1 : 0;
        else { ignored++; Log(LogLevel.Warning, $"Ignored (not a folder or comic archive): {path}"); }
    }
    ...
}
```

Used by drag-and-drop **and** paste. Folders become "Folder → CBZ" rows; archives become
"CBR → Folder" (or "→ CBZ") rows; anything else is logged and ignored.

```csharp
private bool AddInput(InputKind kind, string path)
{
    string fullPath = Path.GetFullPath(path);
    if (AllRows().Any(r => string.Equals(r.Input.Path, fullPath, StringComparison.OrdinalIgnoreCase))) return false;

    var input = new InputItem(kind, fullPath);
    string name = kind == InputKind.ComicFolder ? new DirectoryInfo(fullPath).Name : Path.GetFileName(fullPath);
    var item = new ListViewItem(new[] { name, ActionLabel(input), "Ready" }) { ToolTipText = fullPath, UseItemStyleForSubItems = false };
    item.Tag = new Row { Input = input, Item = item };
    ...
}
```

No duplicates. The *Name* column shows just the name; hovering shows the full path.
`UseItemStyleForSubItems = false` allows the *Status* column its own colour (green ✓, red ✗).

```csharp
private void PasteFiles()
{
    if (Clipboard.ContainsFileDropList())
        AddPaths(Clipboard.GetFileDropList().Cast<string>());
    else if (Clipboard.ContainsText())
        AddPaths(Clipboard.GetText().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().Trim('"')));
}
```

Ctrl+C on files in Explorer puts a "file drop list" on the clipboard. Copied *text* paths (one per
line, e.g. from "Copy as path", which adds quotes) work too.

```csharp
private static void OpenInExplorer(string path, bool select)
{
    if (select && (File.Exists(path) || Directory.Exists(path)))
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    else if (Directory.Exists(path))
        Process.Start("explorer.exe", $"\"{path}\"");
}
```

`explorer.exe /select,"path"` opens the folder with that file highlighted. Used by double-click,
the right-click menu and the "Open output folder" link.

### 24.11 Preview and Convert

```csharp
private async Task ConvertAsync()
{
    if (IsBusy || !CheckReady()) return;

    var started = Stopwatch.StartNew();
    await RunBusyAsync("Looking through your folders…", async token =>
    {
        ConversionService service = CreateService();
        List<InputItem> inputs = AllRows().Select(r => r.Input).ToList();
        ConversionPlan plan = await Task.Run(() => service.CreatePlan(inputs, token), token);
        ...
```

**Never block the UI thread.** The window can only redraw and respond while its thread is free.
`Task.Run` runs the slow work on a **background thread**; `await` gives the UI thread back to
Windows while waiting and continues on the UI thread afterwards. `inputs` is read from the list
**before** `Task.Run`: background threads must never touch controls. `Stopwatch` measures the
elapsed time for the "Done in 12.3 s" message.

```csharp
        var progress = new Progress<ConversionProgress>(p =>
        {
            _progressBar.Maximum = Math.Max(1, p.Total);
            _progressBar.Value = Math.Min(p.Completed, _progressBar.Maximum);
            int percent = (int)(100L * p.Completed / Math.Max(1, p.Total));
            SetStatus(... $"{percent}%  •  {p.ItemNumber} of {p.ItemCount}: {p.CurrentItem}");
        });
        var log = new Progress<LogMessage>(m => Log(m.Level, m.Text));
        var results = new Progress<ItemResult>(r =>
        {
            if (!rows.TryGetValue(r.Source, out Row? row)) return;
            switch (r.Status) { case ItemStatus.Done: row.Done++; break; ... }
            ...
            UpdateRowAfterRun(row);
        });

        ConversionSummary summary = await Task.Run(() => service.Run(plan, progress, log, token, results), token);
```

How does background work update the screen if it may not touch controls? **`Progress<T>`**
remembers the thread it was *created* on (the UI thread). When the background code calls
`Report(...)`, the lambda is automatically run back on the UI thread. That's why the Core never
knows about progress bars. `100L` is a `long`, so `100 × pages` can't overflow on huge jobs.

`rows` is a dictionary from `InputItem` to `Row` (records compare by value, so they work as keys).
Each `ItemResult` updates its row's counters and status text.

```csharp
private void OnFinished(ConversionSummary summary)
{
    if (_settings.PlaySoundWhenDone)
    {
        (summary.Failed > 0 ? SystemSounds.Exclamation : SystemSounds.Asterisk).Play();
        if (!ContainsFocus) NativeMethods.FlashTaskbar(Handle);
    }
    if (_settings.OpenOutputWhenDone && _lastOutputFolder is not null && summary.Created + summary.Extracted > 0)
        OpenInExplorer(_lastOutputFolder, select: false);
}
```

A different sound if something failed; flash the taskbar only if you're in another window
(`ContainsFocus`); open the output folder if you asked for it.

### 24.12 RunBusyAsync

```csharp
private async Task RunBusyAsync(string status, Func<CancellationToken, Task> work)
{
    SaveSettings();
    _cancellation = new CancellationTokenSource();
    ...
    UpdateControlStates();
    try { await work(_cancellation.Token); }
    catch (OperationCanceledException) { Log(...); SetStatus("Cancelled."); ... }
    catch (Exception ex) { Log(LogLevel.Error, ex.Message); SetStatus("Error: " + ex.Message); }
    finally
    {
        _cancellation.Dispose();
        _cancellation = null;
        UpdateControlStates();
    }
}
```

Preview and Convert both need "become busy, run, handle Cancel and errors, become idle". The part
that differs is passed in as a function (`Func<CancellationToken, Task>` = "takes a token, returns
a Task"). `finally` guarantees the window becomes usable again whatever happened.

### 24.13 ShowPlan and ShowPlanInList

```csharp
_previewTree.BeginUpdate();
try
{
    _previewTree.Nodes.Clear();
    foreach (PlannedComic comic in plan.Comics)
    {
        TreeNode node = _previewTree.Nodes.Add($"{comic.Job.Name}.cbz   ({comic.Job.Pages.Count} pages...)   →   {comic.OutputPath}");
        foreach (PageEntry page in comic.Job.Pages)
            node.Nodes.Add($"{page.EntryName}   ←   {page.RelativeSource}");
    }
    ...
}
finally { _previewTree.EndUpdate(); }
```

One node per CBZ, one child per page: **final name ← original file**, in final order.
`BeginUpdate`/`EndUpdate` stop the tree redrawing after each of thousands of additions.
`ShowPlanInList` writes a short summary into each row (`3 CBZ • 245 pages`, or *No images found*).

### 24.14 Settings ↔ screen

```csharp
private void ApplySettingsToScreen(AppSettings s)
{
    _loadingSettings = true;
    try
    {
        _oneCbzPerChapter.Checked = s.ChapterOutput == ChapterOutput.OneFilePerChapter;
        _oneCbzPerSeries.Checked = !_oneCbzPerChapter.Checked;
        ...
        _existingCombo.SelectedIndex = (int)s.ExistingOutput; // combo items are in the same order as the enum
    }
    finally { _loadingSettings = false; }
}
```

Copies each setting into its control. Enums are numbers underneath (`Rename = 0, Skip = 1,
Overwrite = 2`) and the combo items are listed in the same order, so a cast converts between them.
**If you reorder an enum, reorder its combo items too.** `ReadSettingsFromScreen` does the reverse.

```csharp
private void OnSettingChanged()
{
    if (_loadingSettings) return;
    SaveSettings();
    UpdateControlStates();
    foreach (Row row in AllRows()) row.Item.SubItems[1].Text = ActionLabel(row.Input);
}
```

Save, refresh which controls are enabled, and refresh the *Action* column (switching "Convert them
to CBZ" turns "CBR → Folder" into "CBR → CBZ" immediately).

`ResetSettings` applies a fresh `AppSettings` (the defaults) but keeps the typed folder and window
position. `RestoreWindowBounds` only restores the old position if it's still on a connected screen
(`Screen.AllScreens`), so the window never opens on an unplugged second monitor.

### 24.15 UpdateControlStates

```csharp
bool busy = IsBusy;
bool empty = _list.Items.Count == 0;

_emptyHint.Visible = empty;
_list.Visible = !empty;
_removeButton.Enabled = !busy && _list.SelectedItems.Count > 0;
_clearButton.Enabled = !busy && !empty;
_previewButton.Enabled = !busy && !empty;
_convertButton.Enabled = busy || !empty;
_convertButton.Text = busy ? "Cancel" : "Convert";
Theme.MakePrimary(_convertButton, !busy);
_pages[_settingsTab].Enabled = !busy;
_outputFolderBox.Enabled = _saveToFolder.Checked;
_flattenExtract.Enabled = _archiveExtract.Checked;
```

One place decides what can be clicked. It's called after anything relevant changes, so the screen
always matches the state. Settings are locked while converting, so they can't change mid-run.

### 24.16 LoadAppIcon

```csharp
private static Icon? LoadAppIcon(Size? size)
{
    using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ComicArchiveConverter.app.ico");
    if (stream is null) return null;
    return size is Size wanted ? new Icon(stream, wanted) : new Icon(stream);
}
```

Reads the icon embedded in the `.exe` (see section 4). `new Icon(stream, size)` picks the closest
size from the `.ico`, so the header gets the sharp 64-pixel picture instead of a stretched 32-pixel one.

---

# Part 4 — Building, tests and practice

## 25. Building the release zip

There are two ways to ship a .NET app:

| | Framework-dependent | **Self-contained (used here)** |
|---|---|---|
| Size | ~2 MB | ~150 MB folder (~70 MB zipped) |
| Needs .NET installed? | Yes | **No** |
| Works on any Windows 10/11 PC | Only after installing the .NET 8 Desktop Runtime | **Yes** |

### The publish profile (`Properties/PublishProfiles/windows.pubxml`)

```xml
<RuntimeIdentifier>win-x86</RuntimeIdentifier>   <!-- 32-bit build: runs on ALL Windows PCs -->
<SelfContained>true</SelfContained>              <!-- include the .NET runtime DLLs -->
<PublishSingleFile>false</PublishSingleFile>     <!-- keep the DLLs as files next to the exe -->
<PublishReadyToRun>true</PublishReadyToRun>      <!-- pre-compile for faster start-up -->
<DebugType>none</DebugType>                      <!-- no .pdb debug files in the release -->
```

### Why a 32-bit (x86) build?

| Build | 32-bit Windows 10 | 64-bit Windows 10/11 | Windows 11 on ARM | Windows 10 on ARM |
|---|---|---|---|---|
| win-x64 | ❌ | ✅ | ✅ (emulated) | ❌ |
| win-arm64 | ❌ | ❌ | ✅ | ✅ |
| **win-x86** | ✅ | ✅ | ✅ (emulated) | ✅ (emulated) |

Only an x86 build runs everywhere, so **one zip works on every Windows device**. 64-bit Windows runs
32-bit programs natively (through "WOW64"), and ARM Windows emulates x86. The only cost is that the
program can use up to 4 GB of memory instead of more, and this app never needs that much: it handles
one image at a time.

Windows 7 and 8.1 can't run it: .NET 8 itself requires Windows 10 or newer.

### build-release.ps1

A **PowerShell** script (Windows' modern scripting language). The same script runs on your PC
(via `build-release.bat`) and on GitHub's build machines, so both produce identical zips.

```powershell
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
```

`param` declares an optional `-SkipTests` switch. `Stop` makes any error end the script instead of
carrying on. `$PSScriptRoot` is the folder the script is in, so it works from anywhere.

```powershell
$version = (Select-Xml -Path 'Directory.Build.props' -XPath '//Version').Node.InnerText
$staging = Join-Path $publishRoot "staging\$name"
$zip = Join-Path $publishRoot "$name-v$version-windows.zip"
```

Read the version from `Directory.Build.props` (XPath `//Version` = "the Version element anywhere"),
so the zip is always named after the real version. `$staging` is the folder that becomes the
inside of the zip.

```powershell
dotnet test 'tests\ComicArchiveConverter.Tests\ComicArchiveConverter.Tests.csproj' -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing was published.' }

dotnet publish 'src\ComicArchiveConverter\ComicArchiveConverter.csproj' -p:PublishProfile=windows "-p:PublishDir=$staging\"
```

Run the tests (a failing test stops everything), then publish **into** the staging folder.
`$LASTEXITCODE` is the result of the last program: 0 means success.

```powershell
(Get-Content 'docs\RELEASE-README.txt' -Raw).Replace('{VERSION}', $version) | Set-Content (Join-Path $staging 'README.txt') -Encoding UTF8
Copy-Item 'LICENSE' (Join-Path $staging 'LICENSE.txt')
Copy-Item 'THIRD-PARTY-NOTICES.md' (Join-Path $staging 'THIRD-PARTY-NOTICES.txt')
```

Add a plain-text quick-start (with the version filled in), the licence and the third-party notices.
`.txt` names so they open in Notepad with a double-click on any PC.

```powershell
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal
if ($env:GITHUB_OUTPUT) { "zip=$zip" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8 }
```

Zipping the folder itself (not its contents) means the zip unpacks into **one tidy folder**:

```
ComicArchiveConverter-v2.1.0-windows.zip
└── ComicArchiveConverter\
    ├── ComicArchiveConverter.exe
    ├── README.txt, LICENSE.txt, THIRD-PARTY-NOTICES.txt
    └── *.dll
```

The last line only runs on GitHub: it tells the workflow where the zip is, so the next step can
attach it to the release.

### build-release.bat

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" %*
```

Windows won't run a `.ps1` on double-click, so this tiny batch file starts it. `-ExecutionPolicy
Bypass` allows this one script to run without changing your PC's settings; `%~dp0` is the batch
file's folder; `%*` passes on any arguments (e.g. `build-release.bat -SkipTests`).

### .github/workflows/build.yml

On every push and pull request, GitHub builds the solution and runs all the tests on a Windows
machine. The result appears as a green tick or red cross next to each commit and in the README's
*Build* badge.

Releases are made by hand: run `build-release.bat`, test the zips, then create a **GitHub Release**
(for example `v2.1.0`) and attach both zips. That way the files people download are exactly the
ones you tested.

---

## 26. The tests

Tests are small programs that check the real code automatically. Run them in Visual Studio with
**Test → Run All Tests** (Test Explorer shows green ticks) or with `dotnet test`. There are **62**.

```csharp
[Fact]
public void Chapters_are_sorted_like_a_person_would()
{
    string[] names = { "Chapter 10", "Chapter 2", "Chapter 1.5", "Chapter 1", ... };          // Arrange
    string[] sorted = names.OrderBy(n => n, NaturalSortComparer.Instance).ToArray();          // Act
    Assert.Equal(new[] { "Chapter 1", "Chapter 1.5", "Chapter 2", ... }, sorted);             // Assert
}
```

- `[Fact]` marks a test. `[Theory]` + `[InlineData(...)]` runs the same test with several inputs.
- **Arrange / Act / Assert**: set up, do the thing, check the result.
- Test names are sentences describing the expected behaviour.

`TestFolder` creates a unique temporary folder per test and deletes it afterwards. Its `Image(...)`
method writes a **real** tiny image of the type the name says (PNG, JPEG, WebP or GIF), so the
scanner and validators work on genuine files.

The `Assets` folder holds two tiny real archives made with the official tools:

- `sample.cbr` — a **solid RAR5** archive (the hardest case for extraction),
- `sample.cb7` — a 7-Zip archive,

each containing `Chapter 1/001.webp`, `Chapter 2/001.png`, `Chapter 10/001.png`, a text file and
`__MACOSX` junk. `.gitignore` normally ignores archives; the line `!tests/**/Assets/**` makes an
exception for these.

| Test file | What it protects |
|---|---|
| `NaturalSortTests` | 1, 1.5, 2, 10 ordering; leading zeros; huge numbers |
| `FolderGroupingTests` | `Chapter 1`, `Berserk - Chapter 12`, `Vol.03 Ch.012`, `Episode 7` recognised; `Chronicles`, `Watch 5`, `Step 3` not; chapter numbers extracted |
| `ComicScannerTests` | **The WebP manga that failed in 2.0**; one CBZ per chapter; merging; one CBZ per comic; cover first |
| `ArchiveTests` | Verification passes/fails correctly; cut-off WebP detected; ComicInfo bookmarks; **CBR and CB7 extraction**; junk skipped; zip-slip refused; non-archives give a clear error |
| `ServiceTests` | Keep both/skip/replace; same-name safety; full runs; **one-CBZ-per-chapter paths**; **CBR → CBZ**; settings save/load |

**Habit to build:** whenever you find a bug, first write a test that fails because of it (like
`Webp_manga_with_chapter_folders_is_found`), then fix it. The bug can never come back unnoticed.

---

## 27. Rebuilding it yourself from an empty folder

The best way to really learn the code is to type it in yourself. Recommended order:

1. **Create the solution** in Visual Studio: *Create a new project* → **Class Library** (C#, .NET 8)
   named `ComicArchiveConverter.Core`, solution name `ComicArchiveConverter`.
2. **Add SharpCompress**: right-click the Core project → *Manage NuGet Packages* → *Browse* →
   `SharpCompress` → version **0.41.0** → *Install*.
3. **Add the app**: right-click the solution → *Add → New Project* → **Windows Forms App** (.NET 8)
   named `ComicArchiveConverter`. Right-click it → *Add → Project Reference* → tick Core. Right-click
   it → *Set as Startup Project*.
4. **Add the tests**: *Add → New Project* → **xUnit Test Project** named
   `ComicArchiveConverter.Tests`, with a project reference to Core.
5. **Write Core in this order**, building (Ctrl+Shift+B) after each file:
   `ImageFormats` → `NaturalSortComparer` (+ tests) → `FolderGrouping` (+ tests) → `Models`
   → `ComicScanner` (+ tests) → `ComicInfo` → `CbzWriter` → `Crc32` → `ImageValidation`
   → `ArchiveVerifier` (+ tests) → `ArchiveExtractor` (+ tests) → `OutputPaths` (+ tests)
   → `AppSettings` → `ConversionService` (+ tests).
6. **Write the app**: `Program` → `Theme` → `Controls` → `GdiImageValidator` → `NativeMethods`
   → `MainForm`. Start `MainForm` with just the list, drag-and-drop and a Convert button that calls
   the service; add the pages one at a time, running (F5) after each.
7. **Add** `Directory.Build.props`, `app.manifest`, the icon, the publish profile and
   `build-release.ps1`.

Use the debugger: click in the left margin to set a **breakpoint** (red dot) on a line in
`ComicScanner.BuildJobs`, press F5, convert a folder, and step through with **F10** (next line)
and **F11** (step into a method). Hover over variables to see their values. This is the fastest way
to understand what each line does.

---

## 28. Exercises

Ordered from easiest to hardest. Each one touches a different part of the code.

1. **Add JPEG XL (`.jxl`)**: an extension in `ImageFormats`, a signature (`FF 0A`, or the
   container header `00 00 00 0C 4A 58 4C 20`) in `SignatureImageValidator.Detect`, and a test.
2. **Add "Extras" as a grouping type**: a property in `GroupingOptions`, a pattern in
   `FolderGrouping`, a checkbox on the Settings page (remember `ApplySettingsToScreen` and
   `ReadSettingsFromScreen`).
3. **Show the total size** of each planned CBZ in the preview (sum of `new FileInfo(path).Length`).
4. **Add a `Writer` field** to the Settings page and write it into ComicInfo.xml.
5. **Show a thumbnail** of the first page when a preview row is selected (a `PictureBox`; remember
   GDI+ can't open WebP).
6. **Convert several comics in parallel** with `Parallel.ForEach` — think carefully about the
   shared `claimed` set and the counters (hint: `lock` or `Interlocked.Increment`).

---

## 29. Glossary

| Term | Meaning |
|---|---|
| **Assembly** | A compiled `.dll` or `.exe`. |
| **Async / await** | Waiting for slow work without freezing the thread. |
| **Background thread** | A second line of execution so the UI stays responsive. |
| **Callback** | A function you pass to another method so it can call you back (e.g. `pageWritten`). |
| **CBZ / CBR / CB7 / CBT** | Comic archives: ZIP / RAR / 7-Zip / TAR. |
| **ComicInfo.xml** | Metadata file inside a CBZ (series, number, bookmarks). |
| **CRC-32** | A checksum used to detect damaged data. |
| **Dependency injection** | Giving an object the things it needs (e.g. a validator) instead of it creating them. |
| **Enum** | A named list of fixed choices. |
| **Interface** | A contract listing methods a class must have. |
| **Iterator (`yield return`)** | A method that hands back results one at a time. |
| **JSON** | A text format for storing data, used for settings. |
| **LINQ** | `Where`, `Select`, `OrderBy`… methods for working with sequences. |
| **Magic bytes** | Fixed bytes at the start of a file that identify its format. |
| **Natural sort** | Sorting numbers inside text by value. |
| **NuGet** | .NET's package store; `PackageReference` downloads from it. |
| **Nullable (`?`)** | Allowed to be `null`. |
| **P/Invoke** | Calling a function in a Windows DLL from C# (`DllImport`). |
| **Publish** | Produce the final, distributable program. |
| **Recursion** | A method calling itself, used here to walk folder trees. |
| **Regex** | A pattern language for matching text. |
| **Self-contained** | A build that includes the .NET runtime itself. |
| **Solid archive** | RAR/7z where all files are compressed together, so they must be read in order. |
| **Tuple** | A few values travelling together, e.g. `(Outcome, string?)`. |
| **UI thread** | The one thread allowed to touch windows and controls. |
| **WebP** | A modern image format, common for manga pages. |
| **Zip slip** | An attack using `../` in archive entry names to write files outside the target folder. |

