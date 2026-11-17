namespace ComicArchiveConverter.Core;

/// <summary>The image types the converter collects and verifies.</summary>
public static class ImageFormats
{
    // WebP is what most manga sites and downloaders use today, so it must be supported.
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif",
    };

    public static IReadOnlyCollection<string> Extensions => SupportedExtensions;

    /// <summary>True when the file name ends in a supported image extension (any capitalisation).</summary>
    public static bool IsImage(string fileName) => SupportedExtensions.Contains(Path.GetExtension(fileName));
}

/// <summary>The archive types the converter can open.</summary>
public static class ArchiveFormats
{
    // .cbz/.zip = ZIP, .cbr/.rar = RAR, .cb7/.7z = 7-Zip, .cbt/.tar = TAR
    private static readonly HashSet<string> ReadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".zip", ".cbr", ".rar", ".cb7", ".7z", ".cbt", ".tar",
    };

    public static IReadOnlyCollection<string> Extensions => ReadableExtensions;

    public static bool IsArchive(string fileName) => ReadableExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>True for ZIP-based files: these are already CBZ and never need re-packing.</summary>
    public static bool IsZip(string fileName) =>
        Path.GetExtension(fileName).Equals(".cbz", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Short label for the list, e.g. "CBR".</summary>
    public static string Label(string fileName) => Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
}
