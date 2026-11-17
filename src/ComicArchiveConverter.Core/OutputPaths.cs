namespace ComicArchiveConverter.Core;

/// <summary>What to do when the CBZ or folder we are about to create already exists.</summary>
public enum ExistingOutputPolicy
{
    /// <summary>Add " (2)", " (3)" ... to the name. Nothing is ever replaced.</summary>
    Rename,

    /// <summary>Leave the existing file alone and skip this item.</summary>
    Skip,

    /// <summary>Replace the existing CBZ, or merge into the existing folder.</summary>
    Overwrite,
}

public static class OutputPaths
{
    /// <summary>
    /// Picks the final output path. Returns null when the item should be skipped.
    /// </summary>
    /// <param name="desiredPath">The path we would like to use, e.g. D:\Out\Batman.cbz.</param>
    /// <param name="policy">What the user chose for existing outputs.</param>
    /// <param name="existsOnDisk">File.Exists for CBZs, Directory.Exists for folders.</param>
    /// <param name="claimedThisRun">
    /// Paths already produced in this run. Two different comics with the same name are never
    /// allowed to overwrite each other, whatever the policy says.
    /// </param>
    /// <param name="isFolder">Folders have no extension: "Vol. 1" must become "Vol. 1 (2)", not "Vol (2). 1".</param>
    public static string? Resolve(string desiredPath, ExistingOutputPolicy policy,
        Func<string, bool> existsOnDisk, ISet<string> claimedThisRun, bool isFolder = false)
    {
        bool Taken(string path) => claimedThisRun.Contains(path) || existsOnDisk(path);

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

    /// <summary>"Batman.cbz" becomes "Batman (2).cbz", then "Batman (3).cbz", and so on.</summary>
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
}
