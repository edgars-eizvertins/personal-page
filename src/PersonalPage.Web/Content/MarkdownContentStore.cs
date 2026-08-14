using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PersonalPage.Web.Content.Markdown;

namespace PersonalPage.Web.Content;

/// <summary>
/// Reads, parses, renders and caches everything under the content root.
/// </summary>
/// <remarks>
/// <para>
/// Documents are cached indefinitely but revalidated against <c>(LastWriteTimeUtc, Length)</c> on
/// every read, which is what makes an edit appear without a restart, a watcher, or a race. The
/// cache has no size limit: at portfolio scale — tens of files of a few KB — bounding it would
/// add eviction complexity for no benefit. Revisit only if <c>content/</c> ever grows to
/// thousands of files.
/// </para>
/// <para>
/// Collection listings are cached behind a filesystem change token, with a short absolute expiry
/// as a backstop for the case where the watcher never fires. So a newly created file appears
/// immediately when inotify works and within <see cref="ContentOptions.CollectionCacheSeconds"/>
/// when it does not. Edits to an existing document are always immediate, because those go
/// through the per-document stat check.
/// </para>
/// <para>
/// <b>Trust boundary.</b> Raw HTML passthrough plus <c>MarkupString</c> rendering means every
/// file under <c>content/</c> executes as authored HTML in the visitor's browser. That is the
/// right trade for a single-author site, and it makes <c>content/</c> a trusted input. Never
/// point this store at user-submitted or third-party content without disabling raw HTML and
/// sanitizing the output.
/// </para>
/// </remarks>
public sealed class MarkdownContentStore : IContentStore, IDisposable
{
    private const string MarkdownPattern = "*.md";

    private readonly IContentFileSystem _fileSystem;
    private readonly IMemoryCache _cache;
    private readonly ContentRoot _root;
    private readonly ContentOptions _options;
    private readonly MarkdownRenderer _renderer;
    private readonly ILogger<MarkdownContentStore> _logger;

    private readonly ConcurrentDictionary<string, CachedDocument> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ContentIssue> _issues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _loggedIssues = new(StringComparer.Ordinal);

    private CachedSite? _site;
    private readonly SemaphoreSlim _siteLock = new(1, 1);

    public MarkdownContentStore(
        IContentFileSystem fileSystem,
        IMemoryCache cache,
        ContentRoot root,
        IOptions<ContentOptions> options,
        MarkdownRenderer renderer,
        ILogger<MarkdownContentStore> logger)
    {
        _fileSystem = fileSystem;
        _cache = cache;
        _root = root;
        _options = options.Value;
        _renderer = renderer;
        _logger = logger;
    }

    public string RootPath => _root.FullPath;

    public bool ShowDrafts => _options.ShowDrafts;

    public IReadOnlyList<ContentIssue> Issues =>
        _issues.Values.OrderBy(i => i.RelativePath, StringComparer.Ordinal).ToList();

    // ---------------------------------------------------------------- site.yml

    public async ValueTask<SiteConfig> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var path = _root.SiteConfigPath;
        var stat = _fileSystem.Stat(path);

        if (stat is null)
        {
            _site = null;
            return SiteConfig.Default();
        }

        var current = _site;
        if (current is not null && current.Stat == stat.Value)
        {
            return current.Config;
        }

        await _siteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _site;
            if (current is not null && current.Stat == stat.Value)
            {
                return current.Config;
            }

            var text = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var config = FrontMatterParser.ParseSiteConfig(text, out var error);

            if (error is null)
            {
                _issues.TryRemove("site.yml", out _);
            }
            else
            {
                RecordIssue("site.yml", $"site.yml could not be parsed, using defaults: {error}", stat.Value);
            }

            _site = new CachedSite(config, stat.Value);
            return config;
        }
        finally
        {
            _siteLock.Release();
        }
    }

    // ------------------------------------------------------------------- pages

    public async ValueTask<ContentDocument?> GetPageAsync(string? path, CancellationToken cancellationToken = default)
    {
        var normalized = ContentPath.Normalize(path);
        if (normalized is null)
        {
            return null;
        }

        var relative = $"{ContentFolders.Pages}/{normalized}.md";
        var document = await LoadAsync(relative, cancellationToken).ConfigureAwait(false);

        return document is null || (document.IsDraft && !ShowDrafts) ? null : document;
    }

    // ------------------------------------------------------------- collections

    public async ValueTask<IReadOnlyList<ContentDocument>> GetCollectionAsync(
        string folder, CancellationToken cancellationToken = default)
    {
        if (!ContentFolders.All.Contains(folder, StringComparer.Ordinal))
        {
            return [];
        }

        var key = $"collection:{folder}:{ShowDrafts}";
        if (_cache.TryGetValue(key, out IReadOnlyList<ContentDocument>? cached) && cached is not null)
        {
            return cached;
        }

        var directory = Path.Combine(_root.FullPath, folder);
        var files = _fileSystem.EnumerateFiles(directory, MarkdownPattern)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var bySlug = new Dictionary<string, ContentDocument>(StringComparer.Ordinal);

        foreach (var fileName in files)
        {
            var document = await LoadAsync($"{folder}/{fileName}", cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                continue;
            }

            if (document.IsDraft && !ShowDrafts)
            {
                continue;
            }

            if (bySlug.TryGetValue(document.Slug, out var winner))
            {
                // Files were enumerated in ordinal filename order, so the winner is stable
                // across restarts. Surface the loser rather than dropping it silently.
                RecordIssue(document.RelativePath,
                    $"Slug '{document.Slug}' is already taken by '{winner.RelativePath}'. " +
                    "This document is not reachable; rename one of the two files.",
                    new FileStat(document.Validator.LastWriteTimeUtc, document.Validator.Length));
                continue;
            }

            bySlug[document.Slug] = document;
        }

        var list = bySlug.Values.ToList();
        list.Sort(ComparerFor(folder));

        _cache.Set(key, (IReadOnlyList<ContentDocument>)list, CollectionCacheOptions(folder));
        return list;
    }

    public async ValueTask<ContentDocument?> GetItemAsync(
        string folder, string? slug, CancellationToken cancellationToken = default)
    {
        var normalized = ContentPath.Normalize(slug);
        if (normalized is null || !ContentPath.IsValidSlug(normalized))
        {
            return null;
        }

        // Going through the listing is what makes a draft 404 at its direct URL and what keeps
        // slug-to-filename mapping (date prefixes) in one place.
        var collection = await GetCollectionAsync(folder, cancellationToken).ConfigureAwait(false);
        return collection.FirstOrDefault(d => string.Equals(d.Slug, normalized, StringComparison.Ordinal));
    }

    // --------------------------------------------------------------------- nav

    public async ValueTask<IReadOnlyList<NavEntry>> GetNavAsync(CancellationToken cancellationToken = default)
    {
        var key = $"nav:{ShowDrafts}";
        if (_cache.TryGetValue(key, out IReadOnlyList<NavEntry>? cached) && cached is not null)
        {
            return cached;
        }

        var pages = await GetCollectionAsync(ContentFolders.Pages, cancellationToken).ConfigureAwait(false);
        var site = await GetSiteAsync(cancellationToken).ConfigureAwait(false);

        var fromPages = pages
            .Where(p => p.FrontMatter.NavOrder is not null)
            .Select(p => new NavEntry(p.NavTitle, p.Url, p.FrontMatter.NavOrder!.Value));

        var fromSite = site.Nav
            .Where(n => !string.IsNullOrWhiteSpace(n.Title) && !string.IsNullOrWhiteSpace(n.Url))
            .Select(n => new NavEntry(n.Title.Trim(), n.Url.Trim(), n.Order));

        // Ties break on title then URL, so the order does not shuffle between restarts.
        var entries = fromPages.Concat(fromSite)
            .GroupBy(e => e.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Order)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .ThenBy(e => e.Url, StringComparer.Ordinal)
            .ToList();

        var options = CollectionCacheOptions(ContentFolders.Pages);
        options.AddExpirationToken(_fileSystem.Watch("site.yml"));

        _cache.Set(key, (IReadOnlyList<NavEntry>)entries, options);
        return entries;
    }

    // -------------------------------------------------------------- validators

    public async ValueTask<ContentValidator?> GetValidatorAsync(
        string requestPath, CancellationToken cancellationToken = default)
    {
        var normalized = ContentPath.Normalize(requestPath);
        if (normalized is null)
        {
            return null;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        ContentValidator? document = null;

        if (segments.Length == 2 && ContentFolders.Collections.Contains(segments[0], StringComparer.Ordinal))
        {
            var item = await GetItemAsync(segments[0], segments[1], cancellationToken).ConfigureAwait(false);
            document = item?.Validator;
        }
        else if (segments.Length == 1 && ContentFolders.Collections.Contains(segments[0], StringComparer.Ordinal))
        {
            var collection = await GetCollectionAsync(segments[0], cancellationToken).ConfigureAwait(false);
            document = collection.Count == 0
                ? ContentValidator.None
                : collection.Aggregate(ContentValidator.None, (acc, d) => acc.Combine(d.Validator));
        }
        else
        {
            var page = await GetPageAsync(normalized, cancellationToken).ConfigureAwait(false);
            document = page?.Validator;
        }

        if (document is null)
        {
            return null;
        }

        // The header, footer and nav are part of every response, so they belong in the validator.
        // Without this, adding a nav entry would leave every cached page stale in the browser.
        var chrome = await GetChromeValidatorAsync(cancellationToken).ConfigureAwait(false);
        return document.Value.Combine(chrome);
    }

    private async ValueTask<ContentValidator> GetChromeValidatorAsync(CancellationToken cancellationToken)
    {
        const string key = "chrome-validator";
        if (_cache.TryGetValue(key, out ContentValidator cached))
        {
            return cached;
        }

        var validator = _fileSystem.Stat(_root.SiteConfigPath)?.ToValidator() ?? ContentValidator.None;

        var pages = await GetCollectionAsync(ContentFolders.Pages, cancellationToken).ConfigureAwait(false);
        validator = pages
            .Where(p => p.FrontMatter.NavOrder is not null)
            .Aggregate(validator, (acc, p) => acc.Combine(p.Validator));

        _cache.Set(key, validator, CollectionCacheOptions(ContentFolders.Pages));
        return validator;
    }

    // ------------------------------------------------------------------ assets

    public bool AssetExists(string relativePath)
    {
        var normalized = ContentPath.Normalize(relativePath);
        if (normalized is null)
        {
            return false;
        }

        var full = ResolveWithinRoot($"{ContentFolders.Assets}/{normalized}");
        return full is not null && _fileSystem.FileExists(full);
    }

    public IReadOnlyList<string> EnumerateAssets()
    {
        var assets = _root.AssetsPath;
        if (!_fileSystem.DirectoryExists(assets))
        {
            return [];
        }

        return _fileSystem.EnumerateFiles(assets, "*", recursive: true)
            .Select(f => Path.GetRelativePath(assets, f).Replace(Path.DirectorySeparatorChar, '/'))
            // Hidden files are tooling artefacts (.gitkeep, .DS_Store), not content. Listing
            // them would report a permanent orphan on a tree that is actually clean, which is
            // exactly how a diagnostics page earns being ignored.
            .Where(f => !f.Split('/').Any(segment => segment.StartsWith('.')))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    // ----------------------------------------------------------------- loading

    /// <summary>
    /// Loads one document by its path relative to the content root, serving the cached copy when
    /// the file's stat is unchanged.
    /// </summary>
    private async ValueTask<ContentDocument?> LoadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var full = ResolveWithinRoot(relativePath);
        if (full is null)
        {
            return null;
        }

        var stat = _fileSystem.Stat(full);
        if (stat is null)
        {
            // Deleted: drop the cache entry so a file that comes back is re-read.
            _documents.TryRemove(relativePath, out _);
            _issues.TryRemove(relativePath, out _);
            return null;
        }

        if (_documents.TryGetValue(relativePath, out var cached) && cached.Stat == stat.Value)
        {
            return cached.Document;
        }

        // One parse per document, even when several requests race for a cold entry.
        var gate = _locks.GetOrAdd(relativePath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_documents.TryGetValue(relativePath, out cached) && cached.Stat == stat.Value)
            {
                return cached.Document;
            }

            var document = await ParseAsync(relativePath, full, stat.Value, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                _documents.TryRemove(relativePath, out _);
                return null;
            }

            _documents[relativePath] = new CachedDocument(document, stat.Value);
            return document;
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<ContentDocument?> ParseAsync(
        string relativePath, string fullPath, FileStat stat, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await _fileSystem.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read content file {Path}.", relativePath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not read content file {Path}.", relativePath);
            return null;
        }

        var parsed = FrontMatterParser.Parse(text);
        var fileName = Path.GetFileName(relativePath);
        var folder = relativePath.Split('/')[0];
        var slug = SlugBuilder.FromFileName(fileName);

        if (parsed.Error is null)
        {
            _issues.TryRemove(relativePath, out _);
        }
        else
        {
            // One bad file must not empty the nav, break a listing, or 500 a route.
            RecordIssue(relativePath,
                $"Front matter could not be parsed, using defaults: {parsed.Error}", stat);
        }

        var frontMatter = parsed.FrontMatter;

        // A dated filename supplies the date when the front matter does not, so
        // "2026-01-15-hello.md" needs no date key at all.
        frontMatter.Date ??= SlugBuilder.DateFromFileName(fileName);

        return new ContentDocument(
            Slug: slug,
            Collection: folder,
            RelativePath: relativePath,
            FrontMatter: frontMatter,
            Html: _renderer.Render(parsed.Body),
            Validator: stat.ToValidator(),
            ParseError: parsed.Error);
    }

    /// <summary>
    /// Combines a root-relative path with the content root and rejects anything that lands
    /// outside it, following symlinks rather than trusting the lexical path.
    /// </summary>
    private string? ResolveWithinRoot(string relativePath)
    {
        if (relativePath.Contains('\0'))
        {
            return null;
        }

        var combined = Path.GetFullPath(Path.Combine(_root.FullPath, relativePath));

        // Lexical check first — it is cheap and rejects the common case.
        if (!IsInside(_root.FullPath, combined))
        {
            return null;
        }

        // Then the real check. A symlink inside content/ pointing outside it passes the lexical
        // test and must still be refused.
        var realTarget = _fileSystem.ResolveRealPath(combined);
        if (realTarget is null)
        {
            // Nothing there yet. The lexical check already proved it is inside the root, and the
            // caller will simply find no file.
            return combined;
        }

        var realRoot = _fileSystem.ResolveRealPath(_root.FullPath) ?? _root.FullPath;
        return IsInside(realRoot, realTarget) ? combined : null;
    }

    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        return candidate.Length > normalizedRoot.Length
               && candidate.StartsWith(normalizedRoot, StringComparison.Ordinal)
               && candidate[normalizedRoot.Length] == Path.DirectorySeparatorChar;
    }

    private MemoryCacheEntryOptions CollectionCacheOptions(string folder)
    {
        var options = new MemoryCacheEntryOptions
        {
            // Backstop. The change token below normally fires first; this bounds the staleness
            // if the watcher is unavailable, as it can be over some network filesystems.
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.CollectionCacheSeconds),
        };

        options.AddExpirationToken(_fileSystem.Watch($"{folder}/**/*.md"));
        return options;
    }

    private static Comparison<ContentDocument> ComparerFor(string folder) =>
        folder == ContentFolders.Experience ? CompareExperience : CompareByDate;

    /// <summary>Newest first; undated documents last; ties broken on slug so order is stable.</summary>
    private static int CompareByDate(ContentDocument left, ContentDocument right)
    {
        if (left.SortDate is null != right.SortDate is null)
        {
            return left.SortDate is null ? 1 : -1;
        }

        if (left.SortDate is { } leftDate && right.SortDate is { } rightDate)
        {
            var byDate = rightDate.CompareTo(leftDate);
            if (byDate != 0)
            {
                return byDate;
            }
        }

        return string.CompareOrdinal(left.Slug, right.Slug);
    }

    /// <summary>
    /// Roles with no <c>end</c> are current, so they come first; then most recent start first.
    /// </summary>
    private static int CompareExperience(ContentDocument left, ContentDocument right)
    {
        var leftOngoing = left.FrontMatter.End is null;
        var rightOngoing = right.FrontMatter.End is null;
        if (leftOngoing != rightOngoing)
        {
            return leftOngoing ? -1 : 1;
        }

        return CompareByDate(left, right);
    }

    private void RecordIssue(string relativePath, string message, FileStat stat)
    {
        _issues[relativePath] = new ContentIssue(relativePath, message);

        // Log once per (path, mtime), so a broken file does not spam the log on every request.
        var alreadyLogged = _loggedIssues.TryGetValue(relativePath, out var loggedAt)
                            && loggedAt == stat.LastWriteTimeUtc;
        if (alreadyLogged)
        {
            return;
        }

        _loggedIssues[relativePath] = stat.LastWriteTimeUtc;
        _logger.LogWarning("Content problem in {Path}: {Message}", relativePath, message);
    }

    public void Dispose()
    {
        _siteLock.Dispose();
        foreach (var gate in _locks.Values)
        {
            gate.Dispose();
        }

        _locks.Clear();
    }

    private sealed record CachedDocument(ContentDocument Document, FileStat Stat);

    private sealed record CachedSite(SiteConfig Config, FileStat Stat);
}
