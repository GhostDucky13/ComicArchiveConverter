using System.IO.Compression;

namespace ComicArchiveConverter.Core;

public sealed record VerificationResult(IReadOnlyList<string> Problems)
{
    public bool Success => Problems.Count == 0;
}

/// <summary>
/// Opens a finished CBZ and reads every image back out of it. For each image it checks:
///   - the entry is the one we expected to write (nothing missing, nothing extra)
///   - the number of bytes matches what the ZIP says
///   - the CRC-32 checksum matches (the data is not damaged)
///   - the image itself can be read (via the supplied <see cref="IImageValidator"/>)
/// </summary>
public sealed class ArchiveVerifier
{
    private const int MaxProblems = 50;
    private readonly IImageValidator _validator;

    public ArchiveVerifier(IImageValidator validator) => _validator = validator;

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

            if (images.Count == 0) problems.Add("The archive contains no images.");

            if (expectedEntries is not null)
            {
                var actualNames = new HashSet<string>(images.Select(e => e.FullName), StringComparer.Ordinal);
                foreach (string expected in expectedEntries.Where(name => !actualNames.Contains(name)))
                    problems.Add($"Missing page: {expected}");

                if (images.Count != expectedEntries.Count)
                    problems.Add($"Expected {expectedEntries.Count} images but the archive has {images.Count}.");
            }

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

        if (buffer.Length != entry.Length)
            return $"size is {buffer.Length} bytes but the archive says {entry.Length}.";

        uint crc = Crc32.Compute(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (crc != entry.Crc32)
            return "checksum mismatch, the data is damaged.";

        buffer.Position = 0;
        return _validator.TryValidate(buffer, out string? error) ? null : error;
    }
}
