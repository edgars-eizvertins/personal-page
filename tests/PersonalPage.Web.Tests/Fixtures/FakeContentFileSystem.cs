using Microsoft.Extensions.Primitives;
using PersonalPage.Web.Content;

namespace PersonalPage.Web.Tests.Fixtures;

/// <summary>
/// An in-memory content tree.
/// </summary>
/// <remarks>
/// This is why <see cref="IContentFileSystem"/> exists. Against the real filesystem, "the file
/// changed" means sleeping past timestamp granularity; here it is a one-line call. It also
/// counts reads, so cache tests can assert on parse counts instead of on timing.
/// </remarks>
public sealed class FakeContentFileSystem : IContentFileSystem
{
    public const string Root = "/content";

    private readonly Dictionary<string, Entry> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _symlinks = new(StringComparer.Ordinal);
    private readonly List<CancellationChangeTokenSource> _watchers = [];

    private DateTime _clock = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>How many times each file has been read, keyed by path relative to the root.</summary>
    public Dictionary<string, int> ReadCounts { get; } = new(StringComparer.Ordinal);

    public int TotalReads => ReadCounts.Values.Sum();

    // ------------------------------------------------------------ authoring

    /// <summary>Adds or replaces a file, advancing the clock so its mtime differs from the last.</summary>
    public FakeContentFileSystem Write(string relativePath, string content)
    {
        _clock = _clock.AddSeconds(1);
        _files[Full(relativePath)] = new Entry(content, _clock);
        FireWatchers();
        return this;
    }

    /// <summary>Rewrites a file <em>without</em> changing its stat, to pin the mtime limitation.</summary>
    public FakeContentFileSystem WriteKeepingStat(string relativePath, string content)
    {
        var full = Full(relativePath);
        var existing = _files[full];
        if (content.Length != existing.Content.Length)
        {
            throw new InvalidOperationException(
                "WriteKeepingStat is only meaningful when the length is unchanged too — " +
                "length is part of the cache validator.");
        }

        _files[full] = existing with { Content = content };
        return this;
    }

    public FakeContentFileSystem Delete(string relativePath)
    {
        _files.Remove(Full(relativePath));
        FireWatchers();
        return this;
    }

    /// <summary>Marks a path as a symlink resolving to <paramref name="target"/>.</summary>
    public FakeContentFileSystem Symlink(string relativePath, string target)
    {
        var full = Full(relativePath);
        _files[full] = new Entry(string.Empty, _clock);
        _symlinks[full] = [target];
        return this;
    }

    public void ResetReadCounts() => ReadCounts.Clear();

    public int ReadsOf(string relativePath) =>
        ReadCounts.TryGetValue(relativePath, out var count) ? count : 0;

    private static string Full(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    // --------------------------------------------------- IContentFileSystem

    public bool FileExists(string fullPath) => _files.ContainsKey(Normalize(fullPath));

    public bool DirectoryExists(string fullPath)
    {
        var prefix = Normalize(fullPath) + Path.DirectorySeparatorChar;
        return Normalize(fullPath) == Root || _files.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
    }

    public FileStat? Stat(string fullPath) =>
        _files.TryGetValue(Normalize(fullPath), out var entry)
            ? new FileStat(entry.LastWriteTimeUtc, entry.Content.Length)
            : null;

    public Task<string> ReadAllTextAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(fullPath);
        if (!_files.TryGetValue(normalized, out var entry))
        {
            throw new FileNotFoundException(fullPath);
        }

        var key = Path.GetRelativePath(Root, normalized).Replace(Path.DirectorySeparatorChar, '/');
        ReadCounts[key] = ReadsOf(key) + 1;

        return Task.FromResult(entry.Content);
    }

    public IEnumerable<string> EnumerateFiles(string fullDirectoryPath, string searchPattern, bool recursive = false)
    {
        var prefix = Normalize(fullDirectoryPath) + Path.DirectorySeparatorChar;
        var extension = searchPattern.StartsWith("*.", StringComparison.Ordinal)
            ? searchPattern[1..]
            : null;

        foreach (var path in _files.Keys)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = path[prefix.Length..];
            if (!recursive && remainder.Contains(Path.DirectorySeparatorChar))
            {
                continue;
            }

            if (extension is null || path.EndsWith(extension, StringComparison.Ordinal))
            {
                yield return path;
            }
        }
    }

    public string? ResolveRealPath(string fullPath)
    {
        var normalized = Normalize(fullPath);

        if (_symlinks.TryGetValue(normalized, out var targets))
        {
            return targets.Single();
        }

        if (normalized == Root)
        {
            return Root;
        }

        // A symlinked ancestor redirects everything beneath it.
        foreach (var (link, targetSet) in _symlinks)
        {
            var prefix = link + Path.DirectorySeparatorChar;
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Path.Combine(targetSet.Single(), normalized[prefix.Length..]);
            }
        }

        return _files.ContainsKey(normalized) || DirectoryExists(normalized) ? normalized : null;
    }

    public IChangeToken Watch(string relativeGlob)
    {
        var source = new CancellationChangeTokenSource();
        _watchers.Add(source);
        return source.Token;
    }

    private void FireWatchers()
    {
        var firing = _watchers.ToList();
        _watchers.Clear();

        foreach (var watcher in firing)
        {
            watcher.Fire();
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed record Entry(string Content, DateTime LastWriteTimeUtc);

    private sealed class CancellationChangeTokenSource
    {
        private readonly CancellationTokenSource _source = new();

        public IChangeToken Token => new CancellationChangeToken(_source.Token);

        public void Fire() => _source.Cancel();
    }
}
