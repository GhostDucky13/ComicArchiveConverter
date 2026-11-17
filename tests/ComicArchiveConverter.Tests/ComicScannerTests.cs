using ComicArchiveConverter.Core;

namespace ComicArchiveConverter.Tests;

public class ComicScannerTests
{
    [Fact]
    public void Chapters_are_merged_into_one_comic_in_natural_order()
    {
        using var t = new TestFolder();
        t.Image("Library", "Batman", "Chapter 10", "01.png");
        t.Image("Library", "Batman", "Chapter 2", "01.png");
        t.Image("Library", "Batman", "Chapter 1.5", "01.png");
        t.Image("Library", "Batman", "Chapter 1", "02.png");
        t.Image("Library", "Batman", "Chapter 1", "01.png");

        var jobs = new ComicScanner(new GroupingOptions(), flattenPages: true).Scan(t.Path_("Library"));

        ComicJob job = Assert.Single(jobs);
        Assert.Equal("Batman", job.Name);
        Assert.Equal(4, job.MergedFolderCount);
        Assert.Equal(new[] { "001.png", "002.png", "003.png", "004.png", "005.png" }, job.Pages.Select(p => p.EntryName));
        Assert.Equal(
            new[] { "Chapter 1/01.png", "Chapter 1/02.png", "Chapter 1.5/01.png", "Chapter 2/01.png", "Chapter 10/01.png" },
            job.Pages.Select(p => p.RelativeSource.Replace('\\', '/')));
    }

    /// <summary>The exact layout that failed in version 2.0.0: a series folder of chapters full of .webp pages.</summary>
    [Fact]
    public void Webp_manga_with_chapter_folders_is_found()
    {
        using var t = new TestFolder();
        foreach (string chapter in new[] { "Chapter 1", "Chapter 11.5", "Chapter 2", "Chapter 22.1" })
        {
            t.Image("Manga", "Series (Official)", chapter, "001.webp");
            t.Image("Manga", "Series (Official)", chapter, "002.webp");
        }

        ComicJob job = Assert.Single(new ComicScanner(new GroupingOptions(), true).Scan(t.Path_("Manga", "Series (Official)")));

        Assert.Equal(8, job.Pages.Count);
        Assert.Equal(4, job.MergedFolderCount);
        Assert.All(job.Pages, p => Assert.EndsWith(".webp", p.EntryName));
        Assert.Equal("Chapter 22.1", job.Pages[^1].Group);
    }

    [Fact]
    public void One_cbz_per_chapter_mode_makes_a_job_for_every_chapter()
    {
        using var t = new TestFolder();
        t.Image("Series", "cover.jpg");
        t.Image("Series", "Chapter 2", "001.webp");
        t.Image("Series", "Chapter 1", "001.webp");
        t.Image("Series", "Chapter 1", "002.webp");

        var jobs = new ComicScanner(new GroupingOptions(), true, ChapterOutput.OneFilePerChapter).Scan(t.Path_("Series"));

        Assert.Equal(new[] { "Series", "Series - Chapter 1", "Series - Chapter 2" }, jobs.Select(j => j.Name));
        Assert.All(jobs, j => Assert.Equal("Series", j.OutputSubfolder));
        Assert.All(jobs, j => Assert.Equal("Series", j.Series));
        Assert.Equal("1", jobs[1].Number);
        Assert.Equal(2, jobs[1].Pages.Count);
        Assert.Equal(new[] { "001.webp", "002.webp" }, jobs[1].Pages.Select(p => p.EntryName));
    }

    [Fact]
    public void Each_differently_named_folder_becomes_its_own_comic()
    {
        using var t = new TestFolder();
        t.Image("Library", "Batman", "Chapter 1", "01.png");
        t.Image("Library", "Superman", "01.jpg");
        t.Image("Library", "Marvel", "Spider-Man", "Chapter 1", "01.gif");
        t.Text("not an image", "Library", "Empty", "notes.txt");

        var jobs = new ComicScanner(new GroupingOptions(), flattenPages: true).Scan(t.Path_("Library"));

        Assert.Equal(new[] { "Batman", "Spider-Man", "Superman" }, jobs.Select(j => j.Name).OrderBy(n => n));
        Assert.Equal("Marvel", jobs.Single(j => j.Name == "Spider-Man").RelativeParent);
    }

    [Fact]
    public void Volume_folders_are_separate_comics_unless_grouping_is_turned_on()
    {
        using var t = new TestFolder();
        t.Image("Series", "Volume 1", "Chapter 1", "01.png");
        t.Image("Series", "Volume 2", "Chapter 1", "01.png");

        var separate = new ComicScanner(new GroupingOptions { Volume = false }, true).Scan(t.Path_("Series"));
        var merged = new ComicScanner(new GroupingOptions { Volume = true }, true).Scan(t.Path_("Series"));

        Assert.Equal(new[] { "Volume 1", "Volume 2" }, separate.Select(j => j.Name));
        Assert.Equal("Series", Assert.Single(merged).Name);
        Assert.Equal(2, merged[0].Pages.Count);
    }

    [Fact]
    public void Cover_images_come_before_chapters()
    {
        using var t = new TestFolder();
        t.Image("Comic", "Chapter 1", "01.png");
        t.Image("Comic", "cover.jpeg");

        ComicJob job = Assert.Single(new ComicScanner(new GroupingOptions(), true).Scan(t.Path_("Comic")));

        Assert.Equal("cover.jpeg", job.Pages[0].RelativeSource);
        Assert.Equal("001.jpg", job.Pages[0].EntryName);
    }

    [Fact]
    public void Unflattened_comics_keep_numbered_chapter_folders()
    {
        using var t = new TestFolder();
        t.Image("Comic", "Chapter 2", "a.png");
        t.Image("Comic", "Chapter 1", "a.png");
        t.Image("Comic", "Chapter 1", "b.png");

        ComicJob job = Assert.Single(new ComicScanner(new GroupingOptions(), flattenPages: false).Scan(t.Path_("Comic")));

        Assert.Equal(new[] { "001 Chapter 1/001.png", "001 Chapter 1/002.png", "002 Chapter 2/001.png" },
            job.Pages.Select(p => p.EntryName));
    }
}
