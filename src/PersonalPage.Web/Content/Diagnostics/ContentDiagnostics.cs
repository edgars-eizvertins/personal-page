using System.Net;
using System.Text.RegularExpressions;

namespace PersonalPage.Web.Content.Diagnostics;

/// <summary>A link in a document that resolves to nothing.</summary>
public sealed record BrokenLink(string SourcePath, string Href, string Reason);

/// <summary>Everything <c>/_diagnostics</c> shows.</summary>
public sealed record DiagnosticsReport(
    string RootPath,
    int DocumentCount,
    IReadOnlyList<ContentIssue> ParseIssues,
    IReadOnlyList<BrokenLink> BrokenLinks,
    IReadOnlyList<BrokenLink> MissingMedia,
    IReadOnlyList<string> OrphanedAssets,
    IReadOnlyList<ContentDocument> Drafts,
    IReadOnlyList<ContentDocument> FutureDated)
{
    public bool IsClean =>
        ParseIssues.Count == 0
        && BrokenLinks.Count == 0
        && MissingMedia.Count == 0
        && OrphanedAssets.Count == 0
        && FutureDated.Count == 0;
}

/// <summary>
/// Walks the content tree and reports what a build would have caught.
/// </summary>
/// <remarks>
/// Content never passes through a build, so nothing validates it. This restores the feedback
/// loop a static site generator gets for free from its build failing. Everything here is a pure
/// function over the store, which is why it is cheap to test.
/// </remarks>
public sealed partial class ContentDiagnostics(IContentStore store)
{
    /// <summary>Paths that are served by the application rather than by a content file.</summary>
    private static readonly string[] ApplicationPaths =
        ["/", "/blog", "/projects", "/experience", "/healthz", "/_diagnostics"];

    /// <summary>Prefixes served from <c>wwwroot</c>, which the content store knows nothing about.</summary>
    private static readonly string[] StaticPrefixes =
        ["/css/", "/js/", "/lib/", "/favicon", "/_framework/", "/_content/"];

    public async Task<DiagnosticsReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var documents = new List<ContentDocument>();
        foreach (var folder in ContentFolders.All)
        {
            documents.AddRange(await store.GetCollectionAsync(folder, cancellationToken).ConfigureAwait(false));
        }

        var site = await store.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        var assets = store.EnumerateAssets();
        var referencedAssets = new HashSet<string>(StringComparer.Ordinal);

        var brokenLinks = new List<BrokenLink>();
        var missingMedia = new List<BrokenLink>();

        foreach (var document in documents)
        {
            foreach (var href in ExtractReferences(document))
            {
                var resolved = Resolve(href, document);
                if (resolved is null)
                {
                    continue; // external, anchor-only, or a scheme we do not check
                }

                if (resolved.StartsWith("/media/", StringComparison.Ordinal))
                {
                    var asset = resolved["/media/".Length..];
                    referencedAssets.Add(asset);

                    if (!store.AssetExists(asset))
                    {
                        missingMedia.Add(new BrokenLink(document.RelativePath, href,
                            $"No file at assets/{asset}"));
                    }

                    continue;
                }

                if (!await ExistsAsync(resolved, cancellationToken).ConfigureAwait(false))
                {
                    brokenLinks.Add(new BrokenLink(document.RelativePath, href,
                        $"Nothing is served at {resolved}"));
                }
            }
        }

        // Social links in site.yml are usually external, but a relative one should still resolve.
        foreach (var link in site.Links)
        {
            var resolved = Resolve(link.Url, null);
            if (resolved is not null
                && !resolved.StartsWith("/media/", StringComparison.Ordinal)
                && !await ExistsAsync(resolved, cancellationToken).ConfigureAwait(false))
            {
                brokenLinks.Add(new BrokenLink("site.yml", link.Url, $"Nothing is served at {resolved}"));
            }
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new DiagnosticsReport(
            RootPath: store.RootPath,
            DocumentCount: documents.Count,
            ParseIssues: store.Issues,
            BrokenLinks: brokenLinks,
            MissingMedia: missingMedia,
            // favicon.png is never referenced from markdown — App.razor picks it up directly as
            // an optional override of the engine's default tab icon (see docs/content-authoring.md).
            OrphanedAssets: assets
                .Where(a => !referencedAssets.Contains(a) && a != "favicon.png")
                .ToList(),
            Drafts: documents.Where(d => d.IsDraft).ToList(),
            FutureDated: documents.Where(d => d.SortDate is { } date && date > today).ToList());
    }

    /// <summary>
    /// Every <c>href</c> and <c>src</c> in the rendered HTML, plus the front-matter fields that
    /// name a URL. Reading the rendered output rather than the markdown means raw HTML in a body
    /// is checked too.
    /// </summary>
    private static IEnumerable<string> ExtractReferences(ContentDocument document)
    {
        foreach (Match match in ReferenceRegex().Matches(document.Html))
        {
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return WebUtility.HtmlDecode(value);
            }
        }

        if (!string.IsNullOrWhiteSpace(document.FrontMatter.Image))
        {
            yield return document.FrontMatter.Image!;
        }
    }

    /// <summary>
    /// Reduces a reference to a site-absolute path, or null when it is not ours to check.
    /// </summary>
    private static string? Resolve(string href, ContentDocument? source)
    {
        var value = href.Trim();
        if (value.Length == 0 || value.StartsWith('#'))
        {
            return null;
        }

        // Protocol-relative, or carrying a scheme: not ours to check.
        //
        // The scheme test is a regex rather than Uri.TryCreate on purpose. On Unix, Uri treats a
        // leading slash as an absolute *file* path, so Uri.TryCreate("/blog", Absolute) succeeds
        // — which would silently classify every internal link on the site as external and make
        // this whole page report nothing.
        if (value.StartsWith("//", StringComparison.Ordinal) || SchemeRegex().IsMatch(value))
        {
            return null;
        }

        // Drop the fragment and query; only the path is resolvable against content.
        var cut = value.IndexOfAny(['#', '?']);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        if (value.Length == 0)
        {
            return null;
        }

        if (value.StartsWith('/'))
        {
            return value;
        }

        // Relative: resolve against the directory of the source document's URL.
        var baseUrl = source?.Url ?? "/";
        var directory = baseUrl[..(baseUrl.LastIndexOf('/') + 1)];
        var combined = new Uri(new Uri("http://local" + directory), value);
        return combined.AbsolutePath;
    }

    private async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            trimmed = "/";
        }

        if (ApplicationPaths.Contains(trimmed, StringComparer.Ordinal))
        {
            return true;
        }

        if (StaticPrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal)))
        {
            return true;
        }

        var segments = trimmed.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 2 && ContentFolders.Collections.Contains(segments[0], StringComparer.Ordinal))
        {
            return await store.GetItemAsync(segments[0], segments[1], cancellationToken)
                .ConfigureAwait(false) is not null;
        }

        return await store.GetPageAsync(trimmed, cancellationToken).ConfigureAwait(false) is not null;
    }

    [GeneratedRegex("""(?:href|src)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)')""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex SchemeRegex();
}
