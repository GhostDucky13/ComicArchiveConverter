using System.Globalization;

namespace ComicArchiveConverter.Core;

/// <summary>
/// Walks a folder tree and works out which folders are comics and which images belong to each.
///
/// Rules:
///  1. A folder is a comic when it holds images directly, or has grouping subfolders
///     (Chapter, Volume, ...) that hold images.
///  2. Grouping subfolders belong to the comic above them, in natural order. Depending on
///     <see cref="ChapterOutput"/> they are merged into one CBZ, or each becomes its own CBZ.
///     Everything inside a grouping folder (including deeper folders) belongs to that chapter.
///  3. Any other subfolder is scanned on its own, so it can become a separate comic.
/// </summary>
public sealed class ComicScanner
{
    private readonly GroupingOptions _grouping;
    private readonly bool _flattenPages;
    private readonly ChapterOutput _chapterOutput;

    public ComicScanner(GroupingOptions grouping, bool flattenPages,
        ChapterOutput chapterOutput = ChapterOutput.OneFilePerSeries)
    {
        _grouping = grouping;
        _flattenPages = flattenPages;
        _chapterOutput = chapterOutput;
    }

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
            // Grouping folders were already handled as chapters of this folder's comic.
            if (IsGrouping(subfolder)) continue;
            ScanFolder(root, subfolder, jobs, cancellationToken);
        }
    }

    /// <summary>Builds zero, one or (in one-file-per-chapter mode) several CBZ jobs for one comic folder.</summary>
    private IEnumerable<ComicJob> BuildJobs(DirectoryInfo root, DirectoryInfo folder, CancellationToken cancellationToken)
    {
        // Images sitting directly in the comic folder (usually a cover).
        var looseImages = GetImages(folder)
            .Select(file => new SourceImage(file.FullName, file.Name, string.Empty))
            .ToList();

        // Each grouping subfolder (Chapter 1, Chapter 2 ...) with all the images inside it.
        var chapters = new List<(DirectoryInfo Folder, List<SourceImage> Images)>();
        foreach (DirectoryInfo subfolder in GetSubfolders(folder).Where(IsGrouping))
        {
            var images = new List<SourceImage>();
            CollectEverything(subfolder, subfolder.Name, subfolder.Name, images, cancellationToken);
            if (images.Count > 0) chapters.Add((subfolder, images));
        }

        if (looseImages.Count == 0 && chapters.Count == 0) yield break;

        string relativeParent = GetRelativeParent(root, folder);

        if (_chapterOutput == ChapterOutput.OneFilePerSeries || chapters.Count == 0)
        {
            // Everything in one CBZ: loose images first, then each chapter in natural order.
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

        // One CBZ per chapter, all placed in a folder named after the series:
        //   Series\Series - Chapter 1.cbz, Series\Series - Chapter 2.cbz ...
        if (looseImages.Count > 0)
        {
            yield return new ComicJob
            {
                Name = folder.Name,
                Series = folder.Name,
                SourceFolder = folder.FullName,
                ComicFolder = folder.FullName,
                RelativeParent = relativeParent,
                OutputSubfolder = folder.Name,
                Pages = BuildEntries(folder.Name, looseImages),
            };
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

    /// <summary>Adds every image in this folder and all folders below it, in natural order.</summary>
    private void CollectEverything(DirectoryInfo folder, string label, string relativeFolder,
        List<SourceImage> images, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (FileInfo file in GetImages(folder))
            images.Add(new SourceImage(file.FullName, Path.Combine(relativeFolder, file.Name), label));

        foreach (DirectoryInfo subfolder in GetSubfolders(folder))
        {
            CollectEverything(subfolder, label + " - " + subfolder.Name,
                Path.Combine(relativeFolder, subfolder.Name), images, cancellationToken);
        }
    }

    /// <summary>Decides the name each image gets inside the CBZ.</summary>
    private IReadOnlyList<PageEntry> BuildEntries(string comicName, List<SourceImage> images)
    {
        var entries = new List<PageEntry>(images.Count);

        if (_flattenPages)
        {
            // 001.webp, 002.webp, ... one continuous run of pages. Every reader shows these in order.
            int width = PadWidth(images.Count);
            for (int i = 0; i < images.Count; i++)
            {
                SourceImage image = images[i];
                entries.Add(new PageEntry(image.Path, Number(i + 1, width) + Extension(image.Path), image.Relative, image.Group));
            }
            return entries;
        }

        // Keep one folder per chapter inside the CBZ: "001 Chapter 1/001.png".
        // The number prefix keeps folders in the right order in every reader.
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

        return entries;
    }

    private bool IsGrouping(DirectoryInfo folder) => FolderGrouping.IsGroupingFolder(folder.Name, _grouping);

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

    private static string GetRelativeParent(DirectoryInfo root, DirectoryInfo folder)
    {
        if (folder.Parent is null || PathsEqual(folder.FullName, root.FullName)) return string.Empty;
        string relative = Path.GetRelativePath(root.FullName, folder.Parent.FullName);
        return relative == "." ? string.Empty : relative;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    internal static int PadWidth(int count) => Math.Max(3, count.ToString(CultureInfo.InvariantCulture).Length);

    private static string Number(int value, int width) => value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    private static string Extension(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".jpeg" ? ".jpg" : extension;
    }

    private sealed record SourceImage(string Path, string Relative, string Group);
}
