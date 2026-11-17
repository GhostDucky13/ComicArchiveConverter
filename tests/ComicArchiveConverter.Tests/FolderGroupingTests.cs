using ComicArchiveConverter.Core;

namespace ComicArchiveConverter.Tests;

public class FolderGroupingTests
{
    private static readonly GroupingOptions AllOn = new() { Chapter = true, Volume = true, Part = true, Pages = true, Images = true };

    [Theory]
    [InlineData("Chapter 1")]
    [InlineData("chapter_12")]
    [InlineData("Ch. 5")]
    [InlineData("Ch5")]
    [InlineData("01 - Chapter 1")]
    [InlineData("Berserk - Chapter 12")]
    [InlineData("[Group] Vol.03 Ch.012")]
    [InlineData("Episode 7")]
    [InlineData("Solo Leveling Ep. 110")]
    [InlineData("Volume 2")]
    [InlineData("Vol.3")]
    [InlineData("Part 1")]
    [InlineData("Pages")]
    [InlineData("images")]
    [InlineData("IMG")]
    public void Recognises_grouping_folders(string name) => Assert.True(FolderGrouping.IsGroupingFolder(name, AllOn));

    [Theory]
    [InlineData("Chronicles of Narnia")]
    [InlineData("Partners in Crime")]
    [InlineData("Imagine")]
    [InlineData("Batman")]
    [InlineData("Volumetric")]
    [InlineData("Watch 5")]
    [InlineData("Step 3")]
    [InlineData("Marvel Chapter")] // keyword in the middle without a number is not enough
    public void Does_not_match_words_that_only_look_similar(string name) =>
        Assert.False(FolderGrouping.IsGroupingFolder(name, AllOn));

    [Fact]
    public void Disabled_kinds_are_not_grouped()
    {
        var options = new GroupingOptions { Chapter = true, Volume = false, Part = false, Pages = false, Images = false };

        Assert.True(FolderGrouping.IsGroupingFolder("Chapter 1", options));
        Assert.False(FolderGrouping.IsGroupingFolder("Volume 1", options));
        Assert.False(FolderGrouping.IsGroupingFolder("Images", options));
    }

    [Theory]
    [InlineData("Chapter 11.5", "11.5")]
    [InlineData("Chapter 011", "11")]
    [InlineData("Berserk Vol.2 Ch.15", "2")]
    [InlineData("Episode 7", "7")]
    [InlineData("Extras", null)]
    public void Extracts_the_chapter_number(string name, string? expected) =>
        Assert.Equal(expected, FolderGrouping.ExtractNumber(name));
}
