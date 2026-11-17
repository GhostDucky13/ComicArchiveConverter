using System.IO.Compression;
using System.Text;

namespace ComicArchiveConverter.Core;

/// <summary>Speed vs size for the CBZ. JPG/PNG/WebP are already compressed, so "Store" is usually best.</summary>
public enum ArchiveCompression
{
    Store,
    Fastest,
    Optimal,
}

/// <summary>
/// Writes a CBZ. A CBZ is simply a ZIP file with a different extension, so .NET's built-in
/// System.IO.Compression does the job: no WinRAR or 7-Zip needed.
/// </summary>
public static class CbzWriter
{
    public static void Write(ComicJob job, string destinationPath, ArchiveCompression compression,
        bool includeComicInfo = false, CancellationToken cancellationToken = default, Action? pageWritten = null)
    {
        string? folder = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        CompressionLevel level = ToCompressionLevel(compression);

        using var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        if (includeComicInfo)
        {
            ZipArchiveEntry info = zip.CreateEntry(ComicInfo.FileName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(info.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(ComicInfo.Build(job));
        }

        foreach (PageEntry page in job.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            zip.CreateEntryFromFile(page.SourcePath, page.EntryName, level);
            pageWritten?.Invoke();
        }
    }

    public static CompressionLevel ToCompressionLevel(ArchiveCompression compression) => compression switch
    {
        ArchiveCompression.Store => CompressionLevel.NoCompression,
        ArchiveCompression.Fastest => CompressionLevel.Fastest,
        _ => CompressionLevel.Optimal,
    };
}
