using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PersonalPage.Web.Content;

/// <summary>Result of splitting a markdown file into front matter and body.</summary>
/// <param name="FrontMatter">Parsed front matter, or defaults when absent or malformed.</param>
/// <param name="Body">The markdown body, with the front matter fence removed.</param>
/// <param name="Error">Why deserialization failed, when it did. Null on success.</param>
public readonly record struct FrontMatterResult(FrontMatter FrontMatter, string Body, string? Error);

/// <summary>
/// Splits a leading <c>---</c> fence off a markdown file and deserializes it.
/// </summary>
/// <remarks>
/// A malformed fence or an unparseable value is an authoring typo, and typos happen at midnight.
/// Nothing in here throws: a failure yields default front matter, the untouched body, and an
/// error string for the caller to log once and surface on <c>/_diagnostics</c>.
/// </remarks>
public static class FrontMatterParser
{
    private const string Fence = "---";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new DateOnlyYamlConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    public static FrontMatterResult Parse(string text)
    {
        var content = StripBom(text);

        if (!TrySplit(content, out var yaml, out var body))
        {
            return new FrontMatterResult(FrontMatter.Empty(), content, null);
        }

        if (string.IsNullOrWhiteSpace(yaml))
        {
            // "---\n---" is legal and means "no keys".
            return new FrontMatterResult(FrontMatter.Empty(), body, null);
        }

        try
        {
            var parsed = Deserializer.Deserialize<FrontMatter>(yaml) ?? FrontMatter.Empty();
            return new FrontMatterResult(parsed, body, null);
        }
        catch (Exception ex)
        {
            // The body is still perfectly good markdown; serve it.
            return new FrontMatterResult(FrontMatter.Empty(), body, Describe(ex));
        }
    }

    /// <summary>Deserializes <c>site.yml</c>, falling back to defaults rather than throwing.</summary>
    public static SiteConfig ParseSiteConfig(string text, out string? error)
    {
        error = null;
        var content = StripBom(text);
        if (string.IsNullOrWhiteSpace(content))
        {
            return SiteConfig.Default();
        }

        try
        {
            return Deserializer.Deserialize<SiteConfig>(content) ?? SiteConfig.Default();
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            return SiteConfig.Default();
        }
    }

    /// <summary>
    /// Finds the opening and closing <c>---</c> fences. The opening fence must be the very first
    /// line, so a <c>---</c> horizontal rule further down the body is never mistaken for one.
    /// </summary>
    private static bool TrySplit(string content, out string yaml, out string body)
    {
        yaml = string.Empty;
        body = content;

        var firstLineEnd = LineEnd(content, 0);
        if (!IsFence(content.AsSpan(0, firstLineEnd.contentEnd)))
        {
            return false;
        }

        var cursor = firstLineEnd.next;
        while (cursor < content.Length)
        {
            var line = LineEnd(content, cursor);
            if (IsFence(content.AsSpan(cursor, line.contentEnd - cursor)))
            {
                yaml = content[firstLineEnd.next..cursor];
                body = line.next <= content.Length ? content[line.next..] : string.Empty;
                return true;
            }

            cursor = line.next;
        }

        // Unclosed fence: the whole file is body. Callers treat this as "no front matter".
        return false;
    }

    /// <summary>
    /// Returns where the line's text ends (before any CR/LF) and where the next line starts.
    /// Handles LF and CRLF identically — files get edited from Windows machines.
    /// </summary>
    private static (int contentEnd, int next) LineEnd(string content, int start)
    {
        var index = content.IndexOf('\n', start);
        if (index < 0)
        {
            return (content.Length, content.Length);
        }

        var end = index > start && content[index - 1] == '\r' ? index - 1 : index;
        return (end, index + 1);
    }

    private static bool IsFence(ReadOnlySpan<char> line) => line.TrimEnd().SequenceEqual(Fence);

    private static string StripBom(string text) =>
        text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

    private static string Describe(Exception ex)
    {
        // YamlDotNet nests the useful message one level down.
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.ReplaceLineEndings(" ").Trim();
    }
}
