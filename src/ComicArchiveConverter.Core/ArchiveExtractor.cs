using System.Globalization;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ComicArchiveConverter.Core;

/// <summary>
/// The reverse direction: CBZ / CBR / CB7 / CBT back to a folder of images.
/// Reading RAR and 7-Zip needs a library, so this uses SharpCompress (open source, MIT licence),
/// which reads every common archive format with one API.
///
/// If a CBZ was made with "keep chapter folders", those folders come back too.
/// If it was flattened, the chapter information is gone and you get one folder of pages.
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>Lists the images in an archive and where each one will be written, in reading order.</summary>
    public static IReadOnlyList<ExtractEntry> Plan(string archivePath, bool flatten)
    {
        using IArchive archive = Open(archivePath);
        return Plan(archive, flatten);
    }

    private static List<ExtractEntry> Plan(IArchive archive, bool flatten)
    {
        var images = archive.Entries
            .Where(entry => !entry.IsDirectory && entry.Key is not null)
            .Where(entry => ImageFormats.IsImage(entry.Key!) && !IsJunk(entry.Key!))
            .ToList();

        if (images.Any(entry => entry.IsEncrypted))
            throw new InvalidDataException("The archive is password-protected.");

        List<string> names = images
            .Select(entry => entry.Key!)
            .OrderBy(Normalise, NaturalSortComparer.Instance)
            .ToList();

        int width = ComicScanner.PadWidth(names.Count);
        var plan = new List<ExtractEntry>(names.Count);

        for (int i = 0; i < names.Count; i++)
        {
            string target = flatten
                ? (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0') + Path.GetExtension(names[i]).ToLowerInvariant()
                : Normalise(names[i]);
            plan.Add(new ExtractEntry(names[i], target));
        }

        return plan;
    }

    /// <summary>
    /// Extracts into <paramref name="destinationFolder"/> and returns the full paths of the files
    /// written, in reading order. Files are written to a temporary folder first so a cancelled or
    /// failed run leaves nothing half-done.
    /// </summary>
    public static IReadOnlyList<string> Extract(string archivePath, string destinationFolder, bool flatten,
        CancellationToken cancellationToken = default, Action? imageExtracted = null)
    {
        using IArchive archive = Open(archivePath);
        List<ExtractEntry> plan = Plan(archive, flatten);
        if (plan.Count == 0) throw new InvalidDataException("The archive contains no images.");

        // Archive entry name -> where it goes. Entry names may use \ or /, so compare them normalised.
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ExtractEntry item in plan) targets.TryAdd(Normalise(item.EntryName), item.RelativeTarget);

        string destination = Path.GetFullPath(destinationFolder);
        string temp = destination + ".partial-" + Guid.NewGuid().ToString("N")[..8];
        string tempRoot = temp + Path.DirectorySeparatorChar;
        var written = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            Directory.CreateDirectory(temp);

            // A "reader" walks the archive from start to end once. This is the only fast way to read
            // "solid" RAR and 7-Zip archives, and works for every other format too.
            using IReader reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory || reader.Entry.Key is null) continue;
                if (!targets.TryGetValue(Normalise(reader.Entry.Key), out string? relative)) continue;

                string target = Path.GetFullPath(Path.Combine(temp, relative.Replace('/', Path.DirectorySeparatorChar)));

                // "Zip slip" protection: a malicious entry like "../../Windows/evil.png" must not
                // be able to write outside the destination folder.
                if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsafe path inside archive: {reader.Entry.Key}");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (Stream input = reader.OpenEntryStream())
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
                written.Add(relative);
                imageExtracted?.Invoke();
            }

            if (written.Count != targets.Count)
                throw new InvalidDataException($"Only {written.Count} of {targets.Count} images could be read from the archive.");

            MoveIntoPlace(temp, destination, written);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }

        return plan
            .Select(item => Path.Combine(destination, item.RelativeTarget.Replace('/', Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IArchive Open(string archivePath)
    {
        try
        {
            return ArchiveFactory.Open(archivePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArchiveException or InvalidFormatException)
        {
            throw new InvalidDataException("Not a readable CBZ/CBR/CB7/CBT archive (" + ex.Message + ")", ex);
        }
    }

    /// <summary>Archives made on a Mac contain a "__MACOSX" folder of junk copies; never extract those.</summary>
    private static bool IsJunk(string key) =>
        Normalise(key).Split('/').Any(part => part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) || part.StartsWith("._", StringComparison.Ordinal));

    private static string Normalise(string key) => key.Replace('\\', '/').TrimStart('/');

    private static void MoveIntoPlace(string temp, string destination, IEnumerable<string> relativePaths)
    {
        if (!Directory.Exists(destination))
        {
            string? parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            Directory.Move(temp, destination);
            return;
        }

        // The folder already exists (the user chose "Replace it"): merge, replacing same-named files only.
        foreach (string relative in relativePaths)
        {
            string localRelative = relative.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.Combine(destination, localRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(Path.Combine(temp, localRelative), target, overwrite: true);
        }
    }
}
