using Microsoft.Extensions.Primitives;

namespace PersonalPage.Web.Content;

/// <summary>Last write time and length of a file, read in a single stat.</summary>
public readonly record struct FileStat(DateTime LastWriteTimeUtc, long Length)
{
    public ContentValidator ToValidator() => new(LastWriteTimeUtc, Length);
}

/// <summary>
/// The only way <see cref="MarkdownContentStore"/> touches the filesystem.
/// </summary>
/// <remarks>
/// This seam exists for the tests, not for architectural symmetry. Without it every cache test
/// has to manipulate real mtimes and ends up sleeping to dodge filesystem timestamp granularity.
/// With it, "the file changed" is a one-line fake. The real implementation is kept trivial so
/// there is nothing in it to test.
/// </remarks>
public interface IContentFileSystem
{
    bool FileExists(string fullPath);

    bool DirectoryExists(string fullPath);

    /// <summary>Stat for a file, or null when it does not exist.</summary>
    FileStat? Stat(string fullPath);

    Task<string> ReadAllTextAsync(string fullPath, CancellationToken cancellationToken = default);

    /// <summary>Files inside a directory, in no guaranteed order. Empty when it is missing.</summary>
    IEnumerable<string> EnumerateFiles(string fullDirectoryPath, string searchPattern, bool recursive = false);

    /// <summary>
    /// Fully resolved path with all symlinks followed, or null when the path cannot be resolved.
    /// Used by the traversal guard: a symlink inside <c>content/</c> pointing outside it must be
    /// rejected, which a lexical check cannot catch.
    /// </summary>
    string? ResolveRealPath(string fullPath);

    /// <summary>
    /// Change token that fires when a file matching <paramref name="relativeGlob"/> under the
    /// content root is created, changed or deleted.
    /// </summary>
    IChangeToken Watch(string relativeGlob);
}
