using System.Buffers.Binary;

namespace ComicArchiveConverter.Core;

/// <summary>
/// Checks that a stream really is a readable image.
/// The Core library only defines the contract; the Windows app supplies a version that fully
/// decodes the picture, and the tests use the lighter <see cref="SignatureImageValidator"/>.
/// </summary>
public interface IImageValidator
{
    bool TryValidate(Stream image, out string? error);
}

public enum ImageKind
{
    Unknown,
    Jpeg,
    Png,
    WebP,
    Gif,
    Bmp,
    Avif,
}

/// <summary>
/// Fast structural check: the right "magic bytes" at the start, and proof that the file is
/// complete (end marker, or the size written inside the file matches the real size).
/// This catches files that are empty, renamed, or cut off half way through.
/// </summary>
public sealed class SignatureImageValidator : IImageValidator
{
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] PngEnd = "IEND"u8.ToArray();
    private const int TailSize = 64;

    /// <summary>Works out the image type from its first bytes (not from the file name).</summary>
    public static ImageKind Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 8 && head[..8].SequenceEqual(PngHeader)) return ImageKind.Png;
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ImageKind.Jpeg;
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return ImageKind.WebP;
        if (head.Length >= 6 && (head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8))) return ImageKind.Gif;
        if (head.Length >= 2 && head[0] == (byte)'B' && head[1] == (byte)'M') return ImageKind.Bmp;
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8) &&
            (head[8..12].SequenceEqual("avif"u8) || head[8..12].SequenceEqual("avis"u8))) return ImageKind.Avif;
        return ImageKind.Unknown;
    }

    public bool TryValidate(Stream image, out string? error)
    {
        if (!image.CanSeek) throw new ArgumentException("The stream must support seeking.", nameof(image));

        long length = image.Length;
        if (length < 16)
        {
            error = "File is too small to be an image.";
            return false;
        }

        image.Position = 0;
        Span<byte> head = stackalloc byte[16];
        image.ReadExactly(head);

        int tailLength = (int)Math.Min(TailSize, length);
        image.Position = length - tailLength;
        byte[] tail = new byte[tailLength];
        image.ReadExactly(tail);

        try
        {
            error = Detect(head) switch
            {
                ImageKind.Png => tail.AsSpan().IndexOf(PngEnd) >= 0 ? null : "PNG file is cut off (no IEND marker).",
                ImageKind.Jpeg => ContainsJpegEnd(tail) ? null : "JPEG file is cut off (no end-of-image marker).",

                // RIFF stores the file size (minus 8) in bytes 4-7, so a cut-off WebP is easy to spot.
                ImageKind.WebP => BinaryPrimitives.ReadUInt32LittleEndian(head[4..8]) + 8L <= length
                    ? null : "WebP file is cut off (shorter than its RIFF header says).",

                // A GIF always ends with the ';' (0x3B) trailer byte.
                ImageKind.Gif => tail.AsSpan().LastIndexOf((byte)0x3B) >= 0 ? null : "GIF file is cut off (no trailer).",

                // BMP stores its total size in bytes 2-5.
                ImageKind.Bmp => BinaryPrimitives.ReadUInt32LittleEndian(head[2..6]) <= length
                    ? null : "BMP file is cut off (shorter than its header says).",

                ImageKind.Avif => CheckIsoBoxes(image, length),
                _ => "Not a supported image (unknown file header).",
            };
        }
        finally
        {
            image.Position = 0;
        }

        return error is null;
    }

    private static bool ContainsJpegEnd(byte[] tail)
    {
        // Some cameras add padding after the end marker, so look near the end, not only the last 2 bytes.
        for (int i = tail.Length - 2; i >= 0; i--)
            if (tail[i] == 0xFF && tail[i + 1] == 0xD9) return true;
        return false;
    }

    /// <summary>
    /// AVIF files are a chain of "boxes", each starting with its own size. Walking the chain must
    /// land exactly on the end of the file; if a box claims to go past the end, the file is cut off.
    /// </summary>
    private static string? CheckIsoBoxes(Stream image, long length)
    {
        Span<byte> header = stackalloc byte[16];
        long position = 0;
        while (position < length)
        {
            if (length - position < 8) return "AVIF file has trailing garbage.";
            image.Position = position;
            image.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);

            if (size == 1)
            {
                image.ReadExactly(header[8..16]);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
            }
            else if (size == 0)
            {
                size = length - position; // "until the end of the file"
            }

            if (size < 8 || position + size > length) return "AVIF file is cut off (a box runs past the end).";
            position += size;
        }
        return null;
    }
}
