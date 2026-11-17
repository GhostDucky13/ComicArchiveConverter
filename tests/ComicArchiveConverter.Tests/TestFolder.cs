namespace ComicArchiveConverter.Tests;

/// <summary>A throw-away folder for one test. Deleted automatically when the test ends.</summary>
internal sealed class TestFolder : IDisposable
{
    // The smallest valid PNG: one transparent pixel.
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // A real 8x8 WebP image (the format most manga downloads use).
    public static readonly byte[] TinyWebp = Convert.FromBase64String(
        "UklGRjoAAABXRUJQVlA4IC4AAACwAQCdASoIAAgAAUAmJaACdLoABDAAAP7vUS/xbSOhTIf/cHH/YOP+wcfumAAA");

    // A real 8x8 GIF image.
    public static readonly byte[] TinyGif = Convert.FromBase64String(
        "R0lGODdhCAAIAIEAAMgyMgAAAAAAAAAAACwAAAAACAAIAAAIDwABCBxIsKDBgwgTKkwYEAA7");

    // A minimal JPEG-shaped file: start marker, some bytes, end marker.
    public static readonly byte[] TinyJpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0xFF, 0xD9 };

    public TestFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "cac-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path_(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>Creates a real image of the type its extension says, plus any folders above it.</summary>
    public string Image(params string[] parts)
    {
        string path = Path_(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => TinyJpeg,
            ".webp" => TinyWebp,
            ".gif" => TinyGif,
            _ => TinyPng,
        };
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Full path of a file in the test project's Assets folder (sample.cbr, sample.cb7).</summary>
    public static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    public string Text(string content, params string[] parts)
    {
        string path = Path_(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
