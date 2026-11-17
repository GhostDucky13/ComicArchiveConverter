namespace ComicArchiveConverter.Core;

/// <summary>What the user dropped into the list.</summary>
public enum InputKind
{
    /// <summary>A folder of images (or of chapter folders) to turn into CBZ.</summary>
    ComicFolder,

    /// <summary>A CBZ, CBR, CB7 or CBT archive to extract (or convert to CBZ).</summary>
    Archive,
}

public sealed record InputItem(InputKind Kind, string Path);

/// <summary>How chapter folders inside a series are packed.</summary>
public enum ChapterOutput
{
    /// <summary>All chapters of a series go into one big CBZ.</summary>
    OneFilePerSeries,

    /// <summary>Every chapter folder becomes its own CBZ, inside a folder named after the series.</summary>
    OneFilePerChapter,
}

/// <summary>What happens to CBR / CB7 / CBT files.</summary>
public enum ArchiveAction
{
    /// <summary>Extract the images into a folder.</summary>
    ExtractToFolder,

    /// <summary>Re-pack the images into a CBZ (CBZ files themselves are always extracted).</summary>
    ConvertToCbz,
}

/// <summary>One image that will go into a CBZ.</summary>
/// <param name="SourcePath">Full path of the image on disk.</param>
/// <param name="EntryName">Name it gets inside the CBZ (this decides the reading order).</param>
/// <param name="RelativeSource">Where it came from, relative to the comic folder (for the preview).</param>
/// <param name="Group">The chapter it belongs to ("" for loose images). Used for bookmarks.</param>
public sealed record PageEntry(string SourcePath, string EntryName, string RelativeSource, string Group = "");

/// <summary>Everything needed to build one CBZ.</summary>
public sealed class ComicJob
{
    /// <summary>File name of the CBZ without ".cbz".</summary>
    public required string Name { get; init; }

    /// <summary>The series name (the comic folder's name). Written to ComicInfo.xml.</summary>
    public required string Series { get; init; }

    /// <summary>Chapter/volume number when this CBZ is a single chapter, otherwise null.</summary>
    public string? Number { get; init; }

    /// <summary>The folder whose images go into this CBZ (a chapter folder in one-file-per-chapter mode).</summary>
    public required string SourceFolder { get; init; }

    /// <summary>The comic/series folder. Outputs are placed next to it (or in the output folder).</summary>
    public required string ComicFolder { get; init; }

    /// <summary>Folder of the comic relative to the folder the user added (used to keep folder structure).</summary>
    public required string RelativeParent { get; init; }

    /// <summary>Extra folder to put the CBZ in, e.g. the series name in one-file-per-chapter mode.</summary>
    public string OutputSubfolder { get; init; } = string.Empty;

    public required IReadOnlyList<PageEntry> Pages { get; init; }

    /// <summary>How many chapter/volume/part folders were merged into this comic.</summary>
    public int MergedFolderCount { get; init; }
}

/// <summary>One image that will come out of an archive.</summary>
public sealed record ExtractEntry(string EntryName, string RelativeTarget);

public sealed record PlannedComic(InputItem Source, ComicJob Job, string OutputPath);

/// <summary>An archive to extract to a folder, or (when <see cref="ToCbz"/> is set) to re-pack as CBZ.</summary>
public sealed record PlannedExtraction(InputItem Source, string ArchivePath, string OutputPath,
    IReadOnlyList<ExtractEntry> Entries, bool ToCbz);

/// <summary>The full list of work, built before anything is written. The preview shows exactly this.</summary>
public sealed class ConversionPlan
{
    public List<PlannedComic> Comics { get; } = new();
    public List<PlannedExtraction> Extractions { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>Inputs that produced nothing (no images found, unreadable archive ...).</summary>
    public HashSet<InputItem> EmptyInputs { get; } = new();

    public int TotalItems => Comics.Count + Extractions.Count;
    public int TotalPages => Comics.Sum(c => c.Job.Pages.Count) + Extractions.Sum(e => e.Entries.Count);
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record LogMessage(LogLevel Level, string Text);

/// <summary>Progress in "work units" (one per page written, extracted or verified) plus which item is running.</summary>
public sealed record ConversionProgress(int Completed, int Total, int ItemNumber, int ItemCount, string CurrentItem);

public enum ItemStatus
{
    Done,
    Skipped,
    Failed,
}

/// <summary>Reported once for every CBZ created or archive processed, so the list can show a status per row.</summary>
public sealed record ItemResult(InputItem Source, ItemStatus Status, string? OutputPath);

public sealed record ConversionSummary(int Created, int Extracted, int Skipped, int Failed);
