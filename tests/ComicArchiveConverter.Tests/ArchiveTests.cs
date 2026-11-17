using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ComicArchiveConverter.Core;

namespace ComicArchiveConverter.Tests;

public class ArchiveTests
{
    private static ComicJob ScanOne(TestFolder t, string folder, bool flatten = true) =>
        Assert.Single(new ComicScanner(new GroupingOptions(), flatten).Scan(t.Path_(folder)));

    [Fact]
    public void Written_cbz_passes_verification()
    {
        using var t = new TestFolder();
        t.Image("Comic", "Chapter 1", "01.png");
        t.Image("Comic", "Chapter 2", "01.jpg");
        t.Image("Comic", "Chapter 3", "01.webp");
        ComicJob job = ScanOne(t, "Comic");
        string cbz = t.Path_("Comic.cbz");

        CbzWriter.Write(job, cbz, ArchiveCompression.Store, includeComicInfo: true);
        VerificationResult result = new ArchiveVerifier(new SignatureImageValidator())
            .Verify(cbz, job.Pages.Select(p => p.EntryName).ToList());

        Assert.True(result.Success, string.Join("\n", result.Problems));
    }

    [Fact]
    public void ComicInfo_has_series_page_count_and_chapter_bookmarks()
    {
        using var t = new TestFolder();
        t.Image("My Series", "Chapter 1", "01.webp");
        t.Image("My Series", "Chapter 1", "02.webp");
        t.Image("My Series", "Chapter 2", "01.webp");
        ComicJob job = ScanOne(t, "My Series");
        string cbz = t.Path_("out.cbz");
        CbzWriter.Write(job, cbz, ArchiveCompression.Store, includeComicInfo: true);

        using ZipArchive zip = ZipFile.OpenRead(cbz);
        using Stream stream = zip.GetEntry("ComicInfo.xml")!.Open();
        XDocument xml = XDocument.Load(stream);

        Assert.Equal("My Series", xml.Root!.Element("Series")!.Value);
        Assert.Equal("3", xml.Root.Element("PageCount")!.Value);
        var bookmarks = xml.Root.Element("Pages")!.Elements("Page")
            .Where(p => p.Attribute("Bookmark") is not null)
            .Select(p => (p.Attribute("Image")!.Value, p.Attribute("Bookmark")!.Value))
            .ToList();
        Assert.Equal(new[] { ("0", "Chapter 1"), ("2", "Chapter 2") }, bookmarks);
    }

    [Fact]
    public void Verification_detects_a_cut_off_image()
    {
        using var t = new TestFolder();
        string cbz = t.Path_("broken.cbz");
        using (ZipArchive zip = ZipFile.Open(cbz, ZipArchiveMode.Create))
        {
            using Stream s = zip.CreateEntry("001.png").Open();
            s.Write(TestFolder.TinyPng, 0, 30); // only the first 30 bytes
        }

        VerificationResult result = new ArchiveVerifier(new SignatureImageValidator()).Verify(cbz, new[] { "001.png" });

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p => p.Contains("cut off"));
    }

    [Fact]
    public void Signature_check_catches_a_cut_off_webp()
    {
        byte[] truncated = TestFolder.TinyWebp[..40];
        bool ok = new SignatureImageValidator().TryValidate(new MemoryStream(truncated), out string? error);

        Assert.False(ok);
        Assert.Contains("WebP", error);
        Assert.True(new SignatureImageValidator().TryValidate(new MemoryStream(TestFolder.TinyWebp), out _));
        Assert.True(new SignatureImageValidator().TryValidate(new MemoryStream(TestFolder.TinyGif), out _));
    }

    [Fact]
    public void Verification_detects_a_missing_page()
    {
        using var t = new TestFolder();
        t.Image("Comic", "01.png");
        ComicJob job = ScanOne(t, "Comic");
        string cbz = t.Path_("Comic.cbz");
        CbzWriter.Write(job, cbz, ArchiveCompression.Store);

        VerificationResult result = new ArchiveVerifier(new SignatureImageValidator())
            .Verify(cbz, new[] { "001.png", "002.png" });

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p => p.Contains("Missing page: 002.png"));
    }

    [Fact]
    public void Round_trip_keeps_chapter_folders_when_not_flattened()
    {
        using var t = new TestFolder();
        t.Image("Comic", "Chapter 1", "a.png");
        t.Image("Comic", "Chapter 2", "a.png");
        ComicJob job = ScanOne(t, "Comic", flatten: false);
        string cbz = t.Path_("Comic.cbz");
        CbzWriter.Write(job, cbz, ArchiveCompression.Optimal, includeComicInfo: true);

        IReadOnlyList<string> files = ArchiveExtractor.Extract(cbz, t.Path_("Out", "Comic"), flatten: false);

        Assert.Equal(2, files.Count); // ComicInfo.xml is not an image, so it isn't extracted
        Assert.True(File.Exists(t.Path_("Out", "Comic", "001 Chapter 1", "001.png")));
        Assert.True(File.Exists(t.Path_("Out", "Comic", "002 Chapter 2", "001.png")));
    }

    [Fact]
    public void Flattened_extraction_puts_every_page_in_one_folder_in_order()
    {
        using var t = new TestFolder();
        string cbz = t.Path_("mixed.cbz");
        using (ZipArchive zip = ZipFile.Open(cbz, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "Chapter 10/1.png", "Chapter 2/1.png", "info.txt" })
            {
                using Stream s = zip.CreateEntry(name).Open();
                s.Write(name.EndsWith(".png") ? TestFolder.TinyPng : Encoding.UTF8.GetBytes("hi"));
            }
        }

        IReadOnlyList<ExtractEntry> plan = ArchiveExtractor.Plan(cbz, flatten: true);

        Assert.Equal(new[] { "Chapter 2/1.png", "Chapter 10/1.png" }, plan.Select(e => e.EntryName));
        Assert.Equal(new[] { "001.png", "002.png" }, plan.Select(e => e.RelativeTarget));
    }

    [Theory]
    [InlineData("sample.cbr")] // solid RAR5 archive
    [InlineData("sample.cb7")] // 7-Zip archive
    public void Cbr_and_cb7_files_are_extracted_in_reading_order(string asset)
    {
        using var t = new TestFolder();

        IReadOnlyList<string> files = ArchiveExtractor.Extract(TestFolder.Asset(asset), t.Path_("Out"), flatten: true);

        // __MACOSX junk and info.txt are left out; chapters come out 1, 2, 10.
        Assert.Equal(new[] { "001.webp", "002.png", "003.png" }, files.Select(Path.GetFileName));
        Assert.Equal(TestFolder.TinyWebp, File.ReadAllBytes(files[0]));
        Assert.Equal(TestFolder.TinyPng, File.ReadAllBytes(files[2]));
    }

    [Fact]
    public void A_file_that_is_not_an_archive_gives_a_clear_error()
    {
        using var t = new TestFolder();
        string fake = t.Text("this is not a comic", "fake.cbr");

        var ex = Assert.Throws<InvalidDataException>(() => ArchiveExtractor.Plan(fake, flatten: true));
        Assert.Contains("Not a readable", ex.Message);
    }

    [Fact]
    public void Extraction_refuses_entries_that_try_to_escape_the_folder()
    {
        using var t = new TestFolder();
        string cbz = t.Path_("evil.cbz");
        using (ZipArchive zip = ZipFile.Open(cbz, ZipArchiveMode.Create))
        {
            using Stream s = zip.CreateEntry("../../escaped.png").Open();
            s.Write(TestFolder.TinyPng);
        }

        Assert.Throws<InvalidDataException>(() => ArchiveExtractor.Extract(cbz, t.Path_("Out", "evil"), flatten: false));
        Assert.False(File.Exists(t.Path_("escaped.png")));
        Assert.False(Directory.Exists(t.Path_("Out", "evil")));
    }

    [Fact]
    public void Crc32_matches_the_known_value()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }
}
