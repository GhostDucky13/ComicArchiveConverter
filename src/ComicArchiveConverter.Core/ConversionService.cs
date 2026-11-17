namespace ComicArchiveConverter.Core;

/// <summary>
/// Ties everything together. The GUI calls two methods:
///   <see cref="CreatePlan"/> - works out what will happen (used by Preview AND by Convert)
///   <see cref="Run"/>        - actually does it, reporting progress, log lines and per-item results
/// Because Convert always runs the same plan the preview shows, the preview is exactly what you get.
/// </summary>
public sealed class ConversionService
{
    private readonly AppSettings _settings;
    private readonly ArchiveVerifier _verifier;
    private readonly IImageValidator _validator;

    public ConversionService(AppSettings settings, IImageValidator validator)
    {
        _settings = settings;
        _validator = validator;
        _verifier = new ArchiveVerifier(validator);
    }

    // ================================================================== planning

    public ConversionPlan CreatePlan(IEnumerable<InputItem> inputs, CancellationToken cancellationToken = default)
    {
        var plan = new ConversionPlan();
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                plan.Warnings.Add($"{Path.GetFileName(input.Path)}: {ex.Message}");
                plan.EmptyInputs.Add(input);
            }
        }

        return plan;
    }

    private void PlanFolder(InputItem input, ComicScanner scanner, ConversionPlan plan,
        HashSet<string> seenFolders, CancellationToken cancellationToken)
    {
        IReadOnlyList<ComicJob> jobs = scanner.Scan(input.Path, cancellationToken);
        if (jobs.Count == 0)
        {
            plan.Warnings.Add($"No images ({string.Join(", ", ImageFormats.Extensions)}) found in {input.Path}");
            plan.EmptyInputs.Add(input);
            return;
        }

        // If the user drops both a library folder and a comic inside it, do that comic once.
        foreach (ComicJob job in jobs.Where(job => seenFolders.Add(job.SourceFolder)))
            plan.Comics.Add(new PlannedComic(input, job, GetCbzPath(job)));
    }

    private void PlanArchive(InputItem input, ConversionPlan plan)
    {
        // CBZ files are always extracted. CBR/CB7/CBT follow the "Opening archives" setting.
        bool toCbz = _settings.ArchiveAction == ArchiveAction.ConvertToCbz && !ArchiveFormats.IsZip(input.Path);

        IReadOnlyList<ExtractEntry> entries = ArchiveExtractor.Plan(input.Path, flatten: !toCbz && _settings.FlattenWhenExtracting);
        if (entries.Count == 0)
        {
            plan.Warnings.Add($"No images inside {input.Path}");
            plan.EmptyInputs.Add(input);
            return;
        }

        string output = toCbz ? GetCbzPathForArchive(input.Path) : GetExtractFolder(input.Path);
        plan.Extractions.Add(new PlannedExtraction(input, input.Path, output, entries, toCbz));
    }

    /// <summary>Where the CBZ for this comic should go (before handling name clashes).</summary>
    public string GetCbzPath(ComicJob job)
    {
        string baseFolder;
        if (!UsesOutputFolder)
            baseFolder = Path.GetDirectoryName(job.ComicFolder) ?? job.ComicFolder;
        else if (_settings.KeepFolderStructure)
            baseFolder = Path.Combine(_settings.OutputFolder, job.RelativeParent);
        else
            baseFolder = _settings.OutputFolder;

        // In one-file-per-chapter mode OutputSubfolder is the series name, so chapters stay together.
        return Path.Combine(baseFolder, job.OutputSubfolder, job.Name + ".cbz");
    }

    /// <summary>Where an archive gets extracted to: a folder with the same name as the file.</summary>
    public string GetExtractFolder(string archivePath) =>
        Path.Combine(ArchiveOutputBase(archivePath), Path.GetFileNameWithoutExtension(archivePath));

    /// <summary>Where a CBR/CB7/CBT goes when it is converted to CBZ.</summary>
    public string GetCbzPathForArchive(string archivePath) =>
        Path.Combine(ArchiveOutputBase(archivePath), Path.GetFileNameWithoutExtension(archivePath) + ".cbz");

    private string ArchiveOutputBase(string archivePath) =>
        UsesOutputFolder ? _settings.OutputFolder : Path.GetDirectoryName(Path.GetFullPath(archivePath))!;

    private bool UsesOutputFolder =>
        _settings.UseCustomOutputFolder && !string.IsNullOrWhiteSpace(_settings.OutputFolder);

    // ================================================================== running

    public ConversionSummary Run(ConversionPlan plan, IProgress<ConversionProgress>? progress,
        IProgress<LogMessage>? log, CancellationToken cancellationToken = default,
        IProgress<ItemResult>? results = null)
    {
        int created = 0, extracted = 0, skipped = 0, failed = 0;
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Progress is counted in pages: each page written/extracted is one unit, each page verified another.
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

            doneUnits = unitsAtStart + comic.Job.Pages.Count * passes; // keep the bar honest after skips/failures
            Report();
        }

        foreach (PlannedExtraction extraction in plan.Extractions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            itemNumber++;
            current = Path.GetFileName(extraction.ArchivePath);
            int unitsAtStart = doneUnits;
            Report();

            (Outcome outcome, string? output) = extraction.ToCbz
                ? ConvertArchiveToCbz(extraction, claimed, log, cancellationToken, Advance)
                : ExtractArchive(extraction, claimed, log, cancellationToken, Advance);
            Count(outcome, isExtraction: !extraction.ToCbz);
            results?.Report(new ItemResult(extraction.Source, ToStatus(outcome), output));

            doneUnits = unitsAtStart + extraction.Entries.Count * (extraction.ToCbz ? passes + 1 : passes);
            Report();
        }

        current = "Finished";
        Report();
        return new ConversionSummary(created, extracted, skipped, failed);

        void Count(Outcome outcome, bool isExtraction = false)
        {
            switch (outcome)
            {
                case Outcome.Done when isExtraction: extracted++; break;
                case Outcome.Done: created++; break;
                case Outcome.Skipped: skipped++; break;
                default: failed++; break;
            }
        }
    }

    private (Outcome, string?) CreateCbz(PlannedComic comic, ISet<string> claimed, IProgress<LogMessage>? log,
        CancellationToken cancellationToken, Action advance)
    {
        string? target = OutputPaths.Resolve(comic.OutputPath, _settings.ExistingOutput, File.Exists, claimed);
        if (target is null)
        {
            log?.Report(new LogMessage(LogLevel.Warning, $"Skipped {comic.Job.Name}: {comic.OutputPath} already exists."));
            return (Outcome.Skipped, comic.OutputPath);
        }

        string merged = comic.Job.MergedFolderCount > 0 ? $" from {comic.Job.MergedFolderCount} chapters" : string.Empty;
        return (WriteVerifiedCbz(comic.Job, target, merged, log, cancellationToken, advance), target);
    }

    /// <summary>
    /// Writes a CBZ under a temporary name, verifies it, and only then gives it its real name.
    /// A CBZ with the final name is therefore always complete (and checked, if verification is on).
    /// </summary>
    private Outcome WriteVerifiedCbz(ComicJob job, string target, string detail, IProgress<LogMessage>? log,
        CancellationToken cancellationToken, Action advance)
    {
        string temp = target + ".partial-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            CbzWriter.Write(job, temp, _settings.Compression, _settings.AddComicInfo, cancellationToken, advance);

            if (_settings.VerifyArchives)
            {
                VerificationResult result = _verifier.Verify(temp,
                    job.Pages.Select(page => page.EntryName).ToList(), cancellationToken, advance);
                if (!result.Success)
                {
                    log?.Report(new LogMessage(LogLevel.Error,
                        $"Verification failed for {job.Name}. The CBZ was not saved.{Environment.NewLine}    "
                        + string.Join(Environment.NewLine + "    ", result.Problems.Take(10))));
                    return Outcome.Failed;
                }
            }

            File.Move(temp, target, overwrite: true);

            string verified = _settings.VerifyArchives ? ", verified" : string.Empty;
            log?.Report(new LogMessage(LogLevel.Success,
                $"Created {target} ({job.Pages.Count} pages{detail}{verified})"));
            return Outcome.Done;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Report(new LogMessage(LogLevel.Error, $"Failed {job.Name}: {ex.Message}"));
            return Outcome.Failed;
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private (Outcome, string?) ExtractArchive(PlannedExtraction extraction, ISet<string> claimed,
        IProgress<LogMessage>? log, CancellationToken cancellationToken, Action advance)
    {
        string name = Path.GetFileName(extraction.ArchivePath);
        string? target = OutputPaths.Resolve(extraction.OutputPath, _settings.ExistingOutput,
            Directory.Exists, claimed, isFolder: true);
        if (target is null)
        {
            log?.Report(new LogMessage(LogLevel.Warning, $"Skipped {name}: {extraction.OutputPath} already exists."));
            return (Outcome.Skipped, extraction.OutputPath);
        }

        try
        {
            IReadOnlyList<string> files = ArchiveExtractor.Extract(extraction.ArchivePath, target,
                _settings.FlattenWhenExtracting, cancellationToken, advance);

            int bad = 0;
            if (_settings.VerifyArchives)
            {
                foreach (string file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = new MemoryStream(File.ReadAllBytes(file));
                    advance();
                    if (_validator.TryValidate(stream, out string? error)) continue;
                    bad++;
                    log?.Report(new LogMessage(LogLevel.Warning, $"    {Path.GetFileName(file)}: {error}"));
                }
            }

            log?.Report(new LogMessage(bad == 0 ? LogLevel.Success : LogLevel.Warning,
                $"Extracted {name} to {target} ({files.Count} images"
                + (_settings.VerifyArchives ? $", {bad} unreadable)" : ")")));
            return (Outcome.Done, target);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Report(new LogMessage(LogLevel.Error, $"Failed {name}: {ex.Message}"));
            return (Outcome.Failed, null);
        }
    }

    /// <summary>CBR/CB7/CBT to CBZ: extract to a hidden temporary folder, pack those images, delete the folder.</summary>
    private (Outcome, string?) ConvertArchiveToCbz(PlannedExtraction extraction, ISet<string> claimed,
        IProgress<LogMessage>? log, CancellationToken cancellationToken, Action advance)
    {
        string name = Path.GetFileNameWithoutExtension(extraction.ArchivePath);
        string? target = OutputPaths.Resolve(extraction.OutputPath, _settings.ExistingOutput, File.Exists, claimed);
        if (target is null)
        {
            log?.Report(new LogMessage(LogLevel.Warning, $"Skipped {name}: {extraction.OutputPath} already exists."));
            return (Outcome.Skipped, extraction.OutputPath);
        }

        string unpacked = target + ".unpack-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            IReadOnlyList<string> files = ArchiveExtractor.Extract(extraction.ArchivePath, unpacked,
                flatten: false, cancellationToken, advance);

            int width = ComicScanner.PadWidth(files.Count);
            var pages = new List<PageEntry>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                string relative = Path.GetRelativePath(unpacked, files[i]).Replace('\\', '/');
                int slash = relative.LastIndexOf('/');
                string group = slash > 0 ? relative[..slash].Replace("/", " - ") : string.Empty;
                string entryName = _settings.FlattenPagesInCbz
                    ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0')
                      + Path.GetExtension(files[i]).ToLowerInvariant()
                    : relative;
                pages.Add(new PageEntry(files[i], entryName, relative, group));
            }

            var job = new ComicJob
            {
                Name = name,
                Series = name,
                SourceFolder = unpacked,
                ComicFolder = unpacked,
                RelativeParent = string.Empty,
                Pages = pages,
            };

            string from = $" from {ArchiveFormats.Label(extraction.ArchivePath)}";
            return (WriteVerifiedCbz(job, target, from, log, cancellationToken, advance), target);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Report(new LogMessage(LogLevel.Error, $"Failed {name}: {ex.Message}"));
            return (Outcome.Failed, null);
        }
        finally
        {
            try
            {
                if (Directory.Exists(unpacked)) Directory.Delete(unpacked, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static ItemStatus ToStatus(Outcome outcome) => outcome switch
    {
        Outcome.Done => ItemStatus.Done,
        Outcome.Skipped => ItemStatus.Skipped,
        _ => ItemStatus.Failed,
    };

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Not worth failing the whole run over a leftover temp file.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private enum Outcome
    {
        Done,
        Skipped,
        Failed,
    }
}
