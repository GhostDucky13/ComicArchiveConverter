using System.Globalization;
using System.Xml.Linq;

namespace ComicArchiveConverter.Core;

/// <summary>
/// Builds ComicInfo.xml, the de-facto metadata file inside CBZ archives (originally from
/// ComicRack). Komga, Kavita, YACReader, Mihon/Tachiyomi, CDisplayEx and others read it to show
/// the series name, chapter number, page count and a clickable list of chapters (bookmarks).
/// </summary>
public static class ComicInfo
{
    public const string FileName = "ComicInfo.xml";

    public static string Build(ComicJob job, string? appName = "Comic Archive Converter")
    {
        var root = new XElement("ComicInfo",
            new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"));

        // Title = the chapter name for single chapters, otherwise the series name.
        string title = job.Name.StartsWith(job.Series + " - ", StringComparison.Ordinal)
            ? job.Name[(job.Series.Length + 3)..]
            : job.Name;

        root.Add(new XElement("Title", title));
        root.Add(new XElement("Series", job.Series));
        if (job.Number is not null) root.Add(new XElement("Number", job.Number));
        root.Add(new XElement("PageCount", job.Pages.Count.ToString(CultureInfo.InvariantCulture)));
        if (appName is not null) root.Add(new XElement("Notes", "Created with " + appName));

        // One <Page> per image. The first is marked as the cover, and the first page of every
        // chapter gets a Bookmark so readers can jump straight to it.
        var pages = new XElement("Pages");
        string? previousGroup = null;
        for (int i = 0; i < job.Pages.Count; i++)
        {
            var page = new XElement("Page", new XAttribute("Image", i));
            if (i == 0) page.Add(new XAttribute("Type", "FrontCover"));

            string group = job.Pages[i].Group;
            if (group.Length > 0 && group != previousGroup) page.Add(new XAttribute("Bookmark", group));
            previousGroup = group;

            pages.Add(page);
        }
        root.Add(pages);

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).Declaration + Environment.NewLine + root;
    }
}
