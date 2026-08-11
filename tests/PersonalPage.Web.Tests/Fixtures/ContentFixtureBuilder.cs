using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalPage.Web.Content;
using PersonalPage.Web.Content.Markdown;

namespace PersonalPage.Web.Tests.Fixtures;

/// <summary>
/// Fluent composition of a content tree, so each test reads as a statement of intent rather than
/// a pile of string literals.
/// </summary>
public sealed class ContentFixtureBuilder
{
    private readonly FakeContentFileSystem _fileSystem = new();
    private readonly ContentOptions _options = new() { RootPath = FakeContentFileSystem.Root };

    public FakeContentFileSystem FileSystem => _fileSystem;

    public static ContentFixtureBuilder Create() => new();

    public ContentFixtureBuilder ShowingDrafts(bool showDrafts = true)
    {
        _options.ShowDrafts = showDrafts;
        return this;
    }

    /// <summary>Writes a file verbatim, for tests about parsing rather than about content.</summary>
    public ContentFixtureBuilder File(string relativePath, string content)
    {
        _fileSystem.Write(relativePath, content);
        return this;
    }

    public ContentFixtureBuilder Page(string slug, string? frontMatter = null, string body = "Body.")
        => Document(ContentFolders.Pages, slug, frontMatter, body);

    public ContentFixtureBuilder Post(string fileName, string? frontMatter = null, string body = "Body.")
        => Document(ContentFolders.Blog, fileName, frontMatter, body);

    public ContentFixtureBuilder Project(string slug, string? frontMatter = null, string body = "Body.")
        => Document(ContentFolders.Projects, slug, frontMatter, body);

    public ContentFixtureBuilder Role(string slug, string? frontMatter = null, string body = "Body.")
        => Document(ContentFolders.Experience, slug, frontMatter, body);

    public ContentFixtureBuilder Asset(string relativePath, string content = "binary")
    {
        _fileSystem.Write($"{ContentFolders.Assets}/{relativePath}", content);
        return this;
    }

    public ContentFixtureBuilder Site(string yaml)
    {
        _fileSystem.Write("site.yml", yaml);
        return this;
    }

    private ContentFixtureBuilder Document(string folder, string fileName, string? frontMatter, string body)
    {
        var name = fileName.EndsWith(".md", StringComparison.Ordinal) ? fileName : fileName + ".md";

        var builder = new StringBuilder();
        if (frontMatter is not null)
        {
            builder.Append("---\n").Append(frontMatter.Trim('\n')).Append("\n---\n\n");
        }

        builder.Append(body);

        _fileSystem.Write($"{folder}/{name}", builder.ToString());
        return this;
    }

    public MarkdownContentStore Build() => new(
        _fileSystem,
        new MemoryCache(new MemoryCacheOptions()),
        new ContentRoot(FakeContentFileSystem.Root),
        Options.Create(_options),
        new MarkdownRenderer(),
        NullLogger<MarkdownContentStore>.Instance);
}
