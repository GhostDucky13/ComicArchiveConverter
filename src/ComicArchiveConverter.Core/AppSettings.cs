using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComicArchiveConverter.Core;

/// <summary>Every option on the screen. Saved to settings.json so it is remembered next time.</summary>
public sealed class AppSettings
{
    // ---- Main screen
    public ChapterOutput ChapterOutput { get; set; } = ChapterOutput.OneFilePerSeries;

    // ---- Where to save
    public bool UseCustomOutputFolder { get; set; }
    public string OutputFolder { get; set; } = string.Empty;
    public bool KeepFolderStructure { get; set; }
    public ExistingOutputPolicy ExistingOutput { get; set; } = ExistingOutputPolicy.Rename;

    // ---- Creating CBZ files
    public bool FlattenPagesInCbz { get; set; } = true;
    public bool AddComicInfo { get; set; } = true;
    public ArchiveCompression Compression { get; set; } = ArchiveCompression.Store;
    public GroupingOptions Grouping { get; set; } = new();

    // ---- Opening archives
    public ArchiveAction ArchiveAction { get; set; } = ArchiveAction.ExtractToFolder;
    public bool FlattenWhenExtracting { get; set; }

    // ---- Safety
    public bool VerifyArchives { get; set; } = true;

    // ---- When finished
    public bool OpenOutputWhenDone { get; set; }
    public bool PlaySoundWhenDone { get; set; } = true;

    // ---- Window (remembered automatically)
    public int[]? WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in %AppData%\ComicArchiveConverter.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ComicArchiveConverter", "settings.json");
    }

    public string FilePath { get; }

    /// <summary>Returns the saved settings, or defaults if there is no file or it cannot be read.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            string json = File.ReadAllText(FilePath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Grouping ??= new GroupingOptions();
            if (!Enum.IsDefined(settings.ChapterOutput)) settings.ChapterOutput = ChapterOutput.OneFilePerSeries;
            if (!Enum.IsDefined(settings.ExistingOutput)) settings.ExistingOutput = ExistingOutputPolicy.Rename;
            if (!Enum.IsDefined(settings.Compression)) settings.Compression = ArchiveCompression.Store;
            if (!Enum.IsDefined(settings.ArchiveAction)) settings.ArchiveAction = ArchiveAction.ExtractToFolder;
            settings.OutputFolder ??= string.Empty;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes to a temporary file first so a crash can never leave a half-written settings file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
