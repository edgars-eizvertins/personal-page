namespace PersonalPage.Web.Content;

/// <summary>An entry in the site navigation, built from a page's front matter.</summary>
public sealed record NavEntry(string Title, string Url, int Order);

/// <summary>A document that could not be fully understood, surfaced on <c>/_diagnostics</c>.</summary>
public sealed record ContentIssue(string RelativePath, string Message);

/// <summary>
/// The single entry point for reading content. Nothing else in the application touches the
/// filesystem for content.
/// </summary>
public interface IContentStore
{
    /// <summary>Absolute path of the content root.</summary>
    string RootPath { get; }

    /// <summary>Whether drafts are visible. False everywhere except Development.</summary>
    bool ShowDrafts { get; }

    /// <summary><c>site.yml</c>, revalidated against its mtime on every read.</summary>
    ValueTask<SiteConfig> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>A page under <c>pages/</c>. The path is normalized here; null means 404.</summary>
    ValueTask<ContentDocument?> GetPageAsync(string? path, CancellationToken cancellationToken = default);

    /// <summary>Every published document in a collection folder, already sorted.</summary>
    ValueTask<IReadOnlyList<ContentDocument>> GetCollectionAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>One document from a collection folder by slug. Null means 404.</summary>
    ValueTask<ContentDocument?> GetItemAsync(string folder, string? slug, CancellationToken cancellationToken = default);

    /// <summary>Navigation, built from pages declaring <c>nav_order</c>.</summary>
    ValueTask<IReadOnlyList<NavEntry>> GetNavAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache validator covering everything that shapes the response for a request path: the
    /// document itself plus the chrome (<c>site.yml</c> and the nav). Null when the path maps to
    /// no document, in which case the caller renders normally and no 304 is possible.
    /// </summary>
    ValueTask<ContentValidator?> GetValidatorAsync(string requestPath, CancellationToken cancellationToken = default);

    /// <summary>True when <c>assets/{relativePath}</c> exists.</summary>
    bool AssetExists(string relativePath);

    /// <summary>Every file under <c>assets/</c>, as paths relative to that folder.</summary>
    IReadOnlyList<string> EnumerateAssets();

    /// <summary>Documents whose front matter failed to parse, or whose slug collided.</summary>
    IReadOnlyList<ContentIssue> Issues { get; }
}
