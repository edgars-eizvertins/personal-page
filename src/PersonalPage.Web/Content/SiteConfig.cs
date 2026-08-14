namespace PersonalPage.Web.Content;

/// <summary>
/// Everything in <c>content/site.yml</c>. Loaded through the same stat-revalidated cache as
/// documents, so editing the title or a social link is live with no restart.
/// </summary>
public sealed class SiteConfig
{
    public string Title { get; set; } = "Your Name";
    public string? Tagline { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Footer { get; set; }
    public List<SiteLink> Links { get; set; } = [];

    /// <summary>
    /// Extra navigation entries. Pages carry their own <c>nav_order</c>, but the collection
    /// views (<c>/experience</c>, <c>/projects</c>, <c>/blog</c>) are routes rather than files,
    /// so this is where they get a place in the nav — still a content edit, not a code change.
    /// </summary>
    public List<SiteNavItem> Nav { get; set; } = [];

    /// <summary>Used when <c>site.yml</c> is missing or unparseable — never an exception.</summary>
    public static SiteConfig Default() => new() { Tagline = "Developer portfolio" };
}

public sealed class SiteNavItem
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Sorted against the <c>nav_order</c> of pages, so the two interleave.</summary>
    public int Order { get; set; }
}

public sealed class SiteLink
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Optional <c>rel</c> value; external links get <c>noopener</c> added regardless.</summary>
    public string? Rel { get; set; }
}
