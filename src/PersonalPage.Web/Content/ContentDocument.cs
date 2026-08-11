using System.Globalization;

namespace PersonalPage.Web.Content;

/// <summary>
/// Cache validator for a content file: last write time plus length.
/// </summary>
/// <remarks>
/// Length is included deliberately. Some editors preserve mtime on save and some filesystems
/// have coarse timestamp granularity, so mtime alone can miss an edit. Length catches most of
/// what mtime misses; the residual case (same mtime, same length, different bytes) is a known
/// limitation documented in <c>docs/architecture.md</c>.
/// </remarks>
public readonly record struct ContentValidator(DateTime LastWriteTimeUtc, long Length)
{
    public static ContentValidator None => default;

    public bool IsEmpty => Length == 0 && LastWriteTimeUtc == default;

    /// <summary>A weak-free, quoted ETag derived from the validator.</summary>
    public string ToETag() =>
        "\"" + LastWriteTimeUtc.Ticks.ToString("x", CultureInfo.InvariantCulture)
             + "-" + Length.ToString("x", CultureInfo.InvariantCulture) + "\"";

    /// <summary>Combines two validators, so a page's ETag can also cover <c>site.yml</c>.</summary>
    public ContentValidator Combine(ContentValidator other) => new(
        LastWriteTimeUtc > other.LastWriteTimeUtc ? LastWriteTimeUtc : other.LastWriteTimeUtc,
        Length + other.Length);
}

/// <summary>A single parsed and rendered markdown document.</summary>
/// <param name="Slug">URL segment derived from the filename, minus any date prefix.</param>
/// <param name="Collection">Folder under the content root: "pages", "blog", "projects", "experience".</param>
/// <param name="RelativePath">Path relative to the content root, using forward slashes.</param>
/// <param name="FrontMatter">Parsed front matter, or defaults when there was none.</param>
/// <param name="Html">Rendered body HTML. Does not include the title, which the layout renders as h1.</param>
/// <param name="Headings">In-body headings, for a table of contents.</param>
/// <param name="Validator">File stat at the time this document was parsed.</param>
/// <param name="ParseError">Front matter error message, when the YAML failed to deserialize.</param>
public sealed record ContentDocument(
    string Slug,
    string Collection,
    string RelativePath,
    FrontMatter FrontMatter,
    string Html,
    IReadOnlyList<DocumentHeading> Headings,
    ContentValidator Validator,
    string? ParseError = null)
{
    /// <summary>Title shown as the page h1. Falls back to a title-cased slug.</summary>
    public string Title => string.IsNullOrWhiteSpace(FrontMatter.Title)
        ? SlugBuilder.ToDisplayTitle(Slug)
        : FrontMatter.Title!;

    /// <summary>Label used in navigation, preferring <c>nav_title</c>.</summary>
    public string NavTitle => string.IsNullOrWhiteSpace(FrontMatter.NavTitle)
        ? Title
        : FrontMatter.NavTitle!;

    /// <summary>
    /// Sort key for collections: <c>date</c> for blog and projects, <c>start</c> for experience.
    /// Documents without one sort last.
    /// </summary>
    public DateOnly? SortDate => FrontMatter.Date ?? FrontMatter.Start;

    public bool IsDraft => FrontMatter.Draft;

    /// <summary>
    /// Public URL for this document. Pages keep any folder they sit in, so
    /// <c>pages/notes/setup.md</c> is served at <c>/notes/setup</c>.
    /// </summary>
    public string Url
    {
        get
        {
            if (Collection != ContentFolders.Pages)
            {
                return $"/{Collection}/{Slug}";
            }

            var directory = RelativePath.LastIndexOf('/') is var cut and > 0
                ? RelativePath[..cut]
                : string.Empty;

            var subFolder = directory.Length > ContentFolders.Pages.Length
                ? directory[(ContentFolders.Pages.Length + 1)..] + "/"
                : string.Empty;

            var path = subFolder + Slug;
            return path == ContentPath.HomeSlug ? "/" : "/" + path;
        }
    }
}

/// <summary>A heading found in a rendered document body.</summary>
public sealed record DocumentHeading(int Level, string Text, string Id);

/// <summary>Folder names under the content root.</summary>
public static class ContentFolders
{
    public const string Pages = "pages";
    public const string Blog = "blog";
    public const string Projects = "projects";
    public const string Experience = "experience";
    public const string Assets = "assets";

    public static readonly string[] Collections = [Blog, Projects, Experience];
    public static readonly string[] All = [Pages, Blog, Projects, Experience];
}
