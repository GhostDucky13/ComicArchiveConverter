using ComicArchiveConverter.Core;

namespace ComicArchiveConverter.Tests;

public class ServiceTests
{
    [Fact]
    public void Rename_policy_adds_a_number()
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string existing = Path.Combine("out", "Batman.cbz");

        string? result = OutputPaths.Resolve(existing, ExistingOutputPolicy.Rename, p => p == existing, claimed);

        Assert.Equal(Path.Combine("out", "Batman (2).cbz"), result);
    }

    [Fact]
    public void Skip_policy_returns_null_and_overwrite_keeps_the_name()
    {
        string existing = Path.Combine("out", "Batman.cbz");

        Assert.Null(OutputPaths.Resolve(existing, ExistingOutputPolicy.Skip, _ => true, new HashSet<string>()));
        Assert.Equal(existing, OutputPaths.Resolve(existing, ExistingOutputPolicy.Overwrite, _ => true, new HashSet<string>()));
    }

    [Fact]
    public void Two_comics_with_the_same_name_in_one_run_never_overwrite_each_other()
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine("out", "Annual.cbz");

        string? first = OutputPaths.Resolve(path, ExistingOutputPolicy.Overwrite, _ => false, claimed);
        string? second = OutputPaths.Resolve(path, ExistingOutputPolicy.Overwrite, _ => false, claimed);

        Assert.Equal(path, first);
        Assert.Equal(Path.Combine("out", "Annual (2).cbz"), second);
    }

    [Fact]
    public void Folder_names_with_dots_are_renamed_correctly()
    {
        string path = Path.Combine("out", "Vol. 1");
        string? result = OutputPaths.Resolve(path, ExistingOutputPolicy.Rename, p => p == path, new HashSet<string>(), isFolder: true);
        Assert.Equal(Path.Combine("out", "Vol. 1 (2)"), result);
    }

    [Fact]
    public void Full_run_creates_cbzs_in_the_output_folder_then_extracts_them_back()
    {
        using var t = new TestFolder();
        t.Image("Library", "Batman", "Chapter 1", "01.png");
        t.Image("Library", "Batman", "Chapter 2", "01.png");
        t.Image("Library", "DC", "Superman", "01.jpg");

        var settings = new AppSettings
        {
            UseCustomOutputFolder = true,
            OutputFolder = t.Path_("Out"),
            KeepFolderStructure = true,
        };
        var service = new ConversionService(settings, new SignatureImageValidator());

        ConversionPlan plan = service.CreatePlan(new[] { new InputItem(InputKind.ComicFolder, t.Path_("Library")) });
        ConversionSummary summary = service.Run(plan, null, null);

        Assert.Equal(new ConversionSummary(2, 0, 0, 0), summary);
        Assert.True(File.Exists(t.Path_("Out", "Batman.cbz")));
        Assert.True(File.Exists(t.Path_("Out", "DC", "Superman.cbz")));
        Assert.Empty(Directory.GetFiles(t.Path_("Out"), "*.partial-*", SearchOption.AllDirectories));

        // Reverse: extract into a different folder.
        settings.OutputFolder = t.Path_("Extracted");
        ConversionPlan back = service.CreatePlan(new[] { new InputItem(InputKind.Archive, t.Path_("Out", "Batman.cbz")) });
        Assert.Equal(new ConversionSummary(0, 1, 0, 0), service.Run(back, null, null));
        Assert.Equal(2, Directory.GetFiles(t.Path_("Extracted", "Batman")).Length);
    }

    [Fact]
    public void One_cbz_per_chapter_goes_into_a_series_folder()
    {
        using var t = new TestFolder();
        t.Image("Manga", "Series", "Chapter 1", "001.webp");
        t.Image("Manga", "Series", "Chapter 2", "001.webp");

        var settings = new AppSettings { ChapterOutput = ChapterOutput.OneFilePerChapter };
        var service = new ConversionService(settings, new SignatureImageValidator());
        ConversionPlan plan = service.CreatePlan(new[] { new InputItem(InputKind.ComicFolder, t.Path_("Manga", "Series")) });

        var results = new List<ItemResult>();
        ConversionSummary summary = service.Run(plan, null, null, default, new SyncProgress<ItemResult>(results.Add));

        Assert.Equal(new ConversionSummary(2, 0, 0, 0), summary);
        Assert.True(File.Exists(t.Path_("Manga", "Series", "Series - Chapter 1.cbz")));
        Assert.True(File.Exists(t.Path_("Manga", "Series", "Series - Chapter 2.cbz")));
        Assert.Equal(2, results.Count(r => r.Status == ItemStatus.Done));
    }

    [Fact]
    public void Cbr_can_be_converted_straight_to_cbz()
    {
        using var t = new TestFolder();
        string cbr = t.Path_("In", "sample.cbr");
        Directory.CreateDirectory(t.Path_("In"));
        File.Copy(TestFolder.Asset("sample.cbr"), cbr);

        var settings = new AppSettings { ArchiveAction = ArchiveAction.ConvertToCbz };
        var service = new ConversionService(settings, new SignatureImageValidator());
        ConversionPlan plan = service.CreatePlan(new[] { new InputItem(InputKind.Archive, cbr) });

        Assert.True(Assert.Single(plan.Extractions).ToCbz);
        Assert.Equal(new ConversionSummary(1, 0, 0, 0), service.Run(plan, null, null));

        string cbz = t.Path_("In", "sample.cbz");
        using var zip = System.IO.Compression.ZipFile.OpenRead(cbz);
        Assert.Equal(new[] { "ComicInfo.xml", "001.webp", "002.png", "003.png" }, zip.Entries.Select(e => e.FullName));
        Assert.Empty(Directory.GetDirectories(t.Path_("In"))); // the temporary unpack folder is gone
    }

    [Fact]
    public void Folder_without_images_is_reported()
    {
        using var t = new TestFolder();
        t.Text("hello", "Empty", "readme.txt");
        var input = new InputItem(InputKind.ComicFolder, t.Path_("Empty"));

        ConversionPlan plan = new ConversionService(new AppSettings(), new SignatureImageValidator()).CreatePlan(new[] { input });

        Assert.Contains(input, plan.EmptyInputs);
        Assert.Contains(plan.Warnings, w => w.Contains(".webp"));
    }

    /// <summary>Progress&lt;T&gt; reports asynchronously; tests need the result immediately.</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public void Settings_are_saved_and_loaded()
    {
        using var t = new TestFolder();
        var store = new SettingsStore(t.Path_("settings.json"));
        var original = new AppSettings
        {
            OutputFolder = @"D:\Comics\Out",
            ExistingOutput = ExistingOutputPolicy.Skip,
            Compression = ArchiveCompression.Optimal,
            Grouping = new GroupingOptions { Volume = true, Images = false },
        };

        store.Save(original);
        AppSettings loaded = store.Load();

        Assert.Equal(original.OutputFolder, loaded.OutputFolder);
        Assert.Equal(ExistingOutputPolicy.Skip, loaded.ExistingOutput);
        Assert.Equal(ArchiveCompression.Optimal, loaded.Compression);
        Assert.True(loaded.Grouping.Volume);
        Assert.False(loaded.Grouping.Images);
    }

    [Fact]
    public void Broken_settings_file_falls_back_to_defaults()
    {
        using var t = new TestFolder();
        string file = t.Text("{ this is not json", "settings.json");

        AppSettings loaded = new SettingsStore(file).Load();

        Assert.True(loaded.VerifyArchives);
    }
}
