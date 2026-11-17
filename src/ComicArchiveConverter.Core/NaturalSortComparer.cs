using System.Globalization;
using System.Text.RegularExpressions;

namespace ComicArchiveConverter.Core;

/// <summary>
/// Sorts names the way a person would read them instead of character by character.
/// Plain text sorting gives "Chapter 1, Chapter 10, Chapter 2". This comparer gives
/// "Chapter 1, Chapter 1.5, Chapter 2, Chapter 10".
/// </summary>
public sealed partial class NaturalSortComparer : IComparer<string>
{
    public static NaturalSortComparer Instance { get; } = new();

    // A run of digits, optionally followed by ".digits" so that 1.5 is read as one number.
    [GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        List<Token> left = Tokenize(x);
        List<Token> right = Tokenize(y);

        int shared = Math.Min(left.Count, right.Count);
        for (int i = 0; i < shared; i++)
        {
            int result = CompareTokens(left[i], right[i]);
            if (result != 0) return result;
        }

        // "Chapter 1" comes before "Chapter 1 Extra".
        if (left.Count != right.Count) return left.Count.CompareTo(right.Count);

        // Same meaning ("01" vs "1"): fall back to plain text so the order is always stable.
        int ignoreCase = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        return ignoreCase != 0 ? ignoreCase : string.CompareOrdinal(x, y);
    }

    private static int CompareTokens(Token a, Token b)
    {
        if (a.Number is decimal numberA && b.Number is decimal numberB)
            return numberA.CompareTo(numberB);

        // Numbers sort before words, so "1 Intro" comes before "Intro".
        if (a.Number is not null) return -1;
        if (b.Number is not null) return 1;

        return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static List<Token> Tokenize(string value)
    {
        var tokens = new List<Token>();
        int position = 0;

        foreach (Match match in NumberPattern().Matches(value))
        {
            if (match.Index > position)
                tokens.Add(new Token(value[position..match.Index], null));

            // decimal holds 28 digits. Longer digit runs are compared as text instead of crashing.
            bool parsed = decimal.TryParse(match.Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal number);
            tokens.Add(new Token(match.Value, parsed ? number : null));

            position = match.Index + match.Length;
        }

        if (position < value.Length)
            tokens.Add(new Token(value[position..], null));

        return tokens;
    }

    private readonly record struct Token(string Text, decimal? Number);
}
