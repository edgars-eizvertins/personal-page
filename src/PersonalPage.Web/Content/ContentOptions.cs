using System.ComponentModel.DataAnnotations;

namespace PersonalPage.Web.Content;

/// <summary>
/// Configuration for the content root. Bound from the "Content" configuration section and
/// validated on start, so a mistyped <c>Content__RootPath</c> refuses to boot rather than
/// quietly serving an empty site.
/// </summary>
public sealed class ContentOptions
{
    public const string SectionName = "Content";

    /// <summary>
    /// Directory holding <c>site.yml</c>, <c>pages/</c>, the collections and <c>assets/</c>.
    /// Relative paths resolve against the application content root.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string RootPath { get; set; } = "content";

    /// <summary>Show documents marked <c>draft: true</c>. Development only.</summary>
    public bool ShowDrafts { get; set; }

    /// <summary>
    /// Backstop expiry for cached collection listings, in seconds. Listings are also invalidated
    /// by a filesystem change token; this bounds the staleness if the watcher never fires.
    /// </summary>
    [Range(1, 3600)]
    public int CollectionCacheSeconds { get; set; } = 5;

    /// <summary>
    /// Poll the filesystem for changes instead of relying on inotify. Only needed when
    /// <c>content/</c> is itself a network mount inside the container.
    /// </summary>
    public bool UsePollingFileWatcher { get; set; }
}
