using Microsoft.Extensions.Options;

namespace PersonalPage.Web.Content;

/// <summary>
/// The one place that turns <see cref="ContentOptions.RootPath"/> into an absolute path, so the
/// store, the filesystem seam, the static file provider and the health check cannot disagree
/// about where content lives.
/// </summary>
public sealed class ContentRoot
{
    public ContentRoot(string fullPath) =>
        FullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));

    public ContentRoot(IOptions<ContentOptions> options, IHostEnvironment environment)
        : this(Path.Combine(environment.ContentRootPath, options.Value.RootPath))
    {
    }

    public string FullPath { get; }

    public string PagesPath => Path.Combine(FullPath, ContentFolders.Pages);

    public string AssetsPath => Path.Combine(FullPath, ContentFolders.Assets);

    public string SiteConfigPath => Path.Combine(FullPath, "site.yml");

    public override string ToString() => FullPath;
}
