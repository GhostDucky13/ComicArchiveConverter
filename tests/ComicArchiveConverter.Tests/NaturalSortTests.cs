using ComicArchiveConverter.Core;

namespace ComicArchiveConverter.Tests;

public class NaturalSortTests
{
    [Fact]
    public void Chapters_are_sorted_like_a_person_would()
    {
        string[] names = { "Chapter 10", "Chapter 2", "Chapter 1.5", "Chapter 1", "Chapter 11", "Chapter 2.1" };

        string[] sorted = names.OrderBy(n => n, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(new[] { "Chapter 1", "Chapter 1.5", "Chapter 2", "Chapter 2.1", "Chapter 10", "Chapter 11" }, sorted);
    }

    [Fact]
    public void Page_files_with_and_without_leading_zeros_sort_by_number()
    {
        string[] names = { "page10.png", "page9.png", "page001.png", "page2.png" };

        string[] sorted = names.OrderBy(n => n, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(new[] { "page001.png", "page2.png", "page9.png", "page10.png" }, sorted);
    }

    [Fact]
    public void Text_comparison_ignores_case()
    {
        Assert.True(NaturalSortComparer.Instance.Compare("chapter 2", "Chapter 10") < 0);
    }

    [Fact]
    public void Very_long_numbers_do_not_crash()
    {
        string a = "scan_" + new string('9', 40);
        string b = "scan_" + new string('1', 40);
        Assert.True(NaturalSortComparer.Instance.Compare(a, b) > 0);
    }
}
