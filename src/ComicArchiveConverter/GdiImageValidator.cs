using System.Drawing;
using System.Runtime.InteropServices;
using ComicArchiveConverter.Core;

namespace ComicArchiveConverter;

/// <summary>
/// Full image check used by the Windows app:
///   1. the structural check from Core (header, end marker / stored size), then
///   2. for JPEG, PNG, GIF and BMP: actually decoding the picture with Windows' own decoder (GDI+).
/// GDI+ cannot decode WebP or AVIF, so those rely on the structural check, which still catches
/// empty, renamed and cut-off files.
/// </summary>
internal sealed class GdiImageValidator : IImageValidator
{
    private readonly SignatureImageValidator _signature = new();

    public bool TryValidate(Stream image, out string? error)
    {
        if (!_signature.TryValidate(image, out error)) return false;

        Span<byte> head = stackalloc byte[16];
        image.Position = 0;
        image.ReadExactly(head);
        image.Position = 0;

        ImageKind kind = SignatureImageValidator.Detect(head);
        if (kind is ImageKind.WebP or ImageKind.Avif) return true;

        try
        {
            using Image picture = Image.FromStream(image, useEmbeddedColorManagement: false, validateImageData: true);
            if (picture.Width <= 0 || picture.Height <= 0)
            {
                error = "Image has no size.";
                return false;
            }

            // Drawing the image (even onto a 1x1 bitmap) forces every pixel to be decoded,
            // without needing the memory for a full-size copy.
            using var target = new Bitmap(1, 1);
            using Graphics graphics = Graphics.FromImage(target);
            graphics.DrawImage(picture, 0, 0, 1, 1);

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            // GDI+ reports a corrupt image as ArgumentException / OutOfMemoryException.
            error = "Windows could not decode the image.";
            return false;
        }
        finally
        {
            image.Position = 0;
        }
    }
}
