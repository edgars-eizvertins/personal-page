using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace PersonalPage.Web.Content;

/// <summary>
/// The real filesystem. Deliberately thin — every decision lives in
/// <see cref="MarkdownContentStore"/>, so there is nothing here worth a unit test.
/// </summary>
public sealed class PhysicalContentFileSystem : IContentFileSystem, IDisposable
{
    private readonly string _root;
    private readonly PhysicalFileProvider? _provider;

    public PhysicalContentFileSystem(IOptions<ContentOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));

        if (Directory.Exists(_root))
        {
            _provider = new PhysicalFileProvider(_root)
            {
                UsePollingFileWatcher = options.Value.UsePollingFileWatcher,
                UseActivePolling = options.Value.UsePollingFileWatcher,
            };
        }
    }

    public bool FileExists(string fullPath) => File.Exists(fullPath);

    public bool DirectoryExists(string fullPath) => Directory.Exists(fullPath);

    public FileStat? Stat(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists ? new FileStat(info.LastWriteTimeUtc, info.Length) : null;
    }

    public Task<string> ReadAllTextAsync(string fullPath, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(fullPath, cancellationToken);

    public IEnumerable<string> EnumerateFiles(string fullDirectoryPath, string searchPattern, bool recursive = false) =>
        Directory.Exists(fullDirectoryPath)
            ? Directory.EnumerateFiles(fullDirectoryPath, searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : [];

    /// <summary>
    /// Canonicalizes a path by walking it segment by segment and following any symlink on the
    /// way. <see cref="Path.GetFullPath(string)"/> is purely lexical and
    /// <see cref="File.ResolveLinkTarget"/> only looks at the last segment, so neither alone
    /// catches a symlinked <em>parent</em> directory pointing out of the content root.
    /// </summary>
    public string? ResolveRealPath(string fullPath)
    {
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
            if (!File.Exists(normalized) && !Directory.Exists(normalized))
            {
                return null;
            }

            var root = Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var current = root;
            var segments = normalized[root.Length..]
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);

                var link = Directory.Exists(current)
                    ? Directory.ResolveLinkTarget(current, returnFinalTarget: true)
                    : File.ResolveLinkTarget(current, returnFinalTarget: true);

                if (link is not null)
                {
                    // A relative link target resolves against the directory holding the link.
                    current = Path.GetFullPath(link.FullName, Path.GetDirectoryName(current) ?? root);
                }
            }

            return Path.TrimEndingDirectorySeparator(current);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IChangeToken Watch(string relativeGlob) =>
        _provider?.Watch(relativeGlob) ?? NullChangeToken.Singleton;

    public void Dispose() => _provider?.Dispose();
}
