using System.Globalization;
using System.Text.RegularExpressions;

namespace ComicArchiveConverter.Core;

/// <summary>
/// Which kinds of subfolder are treated as "part of the comic above them"
/// instead of being a comic of their own.
/// </summary>
public sealed class GroupingOptions
{
    public bool Chapter { get; set; } = true;
    public bool Volume { get; set; }
    public bool Part { get; set; } = true;
    public bool Pages { get; set; } = true;
    public bool Images { get; set; } = true;

    public GroupingOptions Clone() => (GroupingOptions)MemberwiseClone();
}

/// <summary>
/// Decides whether a folder name is a grouping folder such as "Chapter 3", "Vol.2",
/// "Episode 12" or "One Piece - Ch. 1050".
/// </summary>
public static class FolderGrouping
{
    // Rule 1: the name STARTS with the keyword, optionally after numbering: "Chapter 1", "01 - Ch.1", "Pages".
    private const string Lead = @"^[\s\d._\-\[\]()#]*";

    // Rule 2: the keyword appears ANYWHERE as its own word and is followed by a number:
    // "Berserk - Chapter 12", "[Group] Vol.03 Ch.012".
    // (?<![a-z]) = not preceded by a letter, so "Watch 5" or "Step 3" never match.
    private const string Anywhere = @"(?<![a-z])";
    private const string NumberAfter = @"\.?[\s_\-#:]*\d";

    private static readonly (Regex Start, Regex Anywhere) ChapterName = Build("chapter|chap|ch|episode|ep");
    private static readonly (Regex Start, Regex Anywhere) VolumeName = Build("volume|vol");
    private static readonly (Regex Start, Regex Anywhere) PartName = Build("part|pt");
    private static readonly (Regex Start, Regex Anywhere) PagesName = Build("pages|page");
    private static readonly (Regex Start, Regex Anywhere) ImagesName = Build("images|image|imgs|img");

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // (?![a-z]) means "the keyword must not be followed by another letter",
    // so "Ch 5" and "Chapter_5" match but "Chronicles" and "Partners" do not.
    private static (Regex, Regex) Build(string keywords) =>
    (
        new Regex(Lead + "(?:" + keywords + ")(?![a-z])", Options),
        new Regex(Anywhere + "(?:" + keywords + ")" + NumberAfter, Options)
    );

    private static bool Matches((Regex Start, Regex Anywhere) rule, string name) =>
        rule.Start.IsMatch(name) || rule.Anywhere.IsMatch(name);

    public static bool IsGroupingFolder(string folderName, GroupingOptions options) =>
        (options.Chapter && Matches(ChapterName, folderName)) ||
        (options.Volume && Matches(VolumeName, folderName)) ||
        (options.Part && Matches(PartName, folderName)) ||
        (options.Pages && Matches(PagesName, folderName)) ||
        (options.Images && Matches(ImagesName, folderName));

    // The first number after a chapter/volume keyword, e.g. "Chapter 11.5" -> "11.5".
    private static readonly Regex NumberAfterKeyword = new(
        Anywhere + @"(?:chapter|chap|ch|episode|ep|volume|vol|part|pt)\.?[\s_\-#:]*(\d+(?:\.\d+)?)", Options);

    private static readonly Regex AnyNumber = new(@"\d+(?:\.\d+)?", Options);

    /// <summary>
    /// The chapter/volume number in a folder name, used for ComicInfo.xml.
    /// "Chapter 011.5" gives "11.5". Returns null when the name has no number.
    /// </summary>
    public static string? ExtractNumber(string folderName)
    {
        Match match = NumberAfterKeyword.Match(folderName);
        string? raw = match.Success ? match.Groups[1].Value : AnyNumber.Match(folderName) is { Success: true } m ? m.Value : null;
        if (raw is null) return null;

        return decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : raw;
    }
}
