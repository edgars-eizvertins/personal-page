using System.Net;

namespace PersonalPage.Web.Content;

/// <summary>
/// Turns an incoming URL path into a lookup key, or rejects it.
/// </summary>
/// <remarks>
/// The container runs on a case-sensitive Linux filesystem, so <c>/About</c> would miss
/// <c>pages/about.md</c> and 404 — a trap that only surfaces after someone links to the site with
/// the wrong casing. Filenames under <c>content/</c> are lowercase by convention and every
/// request path is folded to match.
/// </remarks>
public static class ContentPath
{
    /// <summary>What an empty path resolves to.</summary>
    public const string HomeSlug = "home";

    private const int MaxDecodePasses = 4;

    /// <summary>
    /// Lowercases, URL-decodes, trims and collapses slashes. Returns null for anything that
    /// tries to escape the content root or that no file could legitimately be named.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return HomeSlug;
        }

        // Decode repeatedly until it stops changing, so a double- or triple-encoded traversal
        // ("%252e%252e%252f") is unwrapped far enough to be recognised and rejected below.
        // Slugs are [a-z0-9-] by construction, so no legitimate path loses meaning to this.
        string decoded;
        try
        {
            decoded = path;
            for (var pass = 0; pass < MaxDecodePasses; pass++)
            {
                var next = WebUtility.UrlDecode(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                {
                    break;
                }

                decoded = next;
            }
        }
        catch (Exception)
        {
            return null;
        }

        if (decoded.Contains('\0') || decoded.Contains('\\'))
        {
            return null;
        }

        var segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0 || trimmed == ".")
            {
                continue;
            }

            if (trimmed == "..")
            {
                // Never silently resolve upwards — that would let "/blog/../../etc/passwd"
                // land inside the root by accident of arithmetic. Reject outright.
                return null;
            }

            if (Path.IsPathRooted(trimmed) || trimmed.Contains(':'))
            {
                return null;
            }

            kept.Add(trimmed.ToLowerInvariant());
        }

        return kept.Count == 0 ? HomeSlug : string.Join('/', kept);
    }

    /// <summary>True when a slug is safe to use as a single filename component.</summary>
    public static bool IsValidSlug(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && slug.Length <= 200
        && slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
}
