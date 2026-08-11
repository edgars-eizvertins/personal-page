using System.Net;
using ColorCode;
using ColorCode.Styling;

namespace PersonalPage.Web.Content.Markdown;

/// <summary>
/// Colourises fenced code at render time, into HTML that lands in the cached document.
/// </summary>
/// <remarks>
/// Highlighting on the server costs the visitor nothing and works before any script parses.
/// The emitted spans carry class names only — never inline colours — so the palette stays in
/// <c>tokens.css</c> with every other colour and a design swap does not have to touch C#.
/// </remarks>
public sealed class SyntaxHighlighter
{
    private static readonly object LoadLock = new();
    private static bool _languagesLoaded;

    public SyntaxHighlighter() => EnsureLanguagesLoaded();

    /// <summary>Resolves a fence info string ("csharp", "bash", "js") to a known language.</summary>
    public static ILanguage? FindLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return null;
        }

        EnsureLanguagesLoaded();

        // "```csharp title=Foo.cs" — only the first word names the language.
        var id = info.Trim().Split(' ', '\t')[0].Trim().ToLowerInvariant();
        return id.Length == 0 ? null : Languages.FindById(id);
    }

    /// <summary>
    /// Highlighted inner HTML for a block of code, or the plainly escaped code when the language
    /// is unknown or the highlighter produces something unexpected.
    /// </summary>
    public string Highlight(string code, ILanguage? language)
    {
        if (language is null)
        {
            return WebUtility.HtmlEncode(code);
        }

        try
        {
            // HtmlClassFormatter holds a TextWriter in a field, so it is not safe to share.
            var formatter = new HtmlClassFormatter(StyleDictionary.DefaultLight);
            var html = formatter.GetHtmlString(code, language);
            return Unwrap(html) ?? WebUtility.HtmlEncode(code);
        }
        catch (Exception)
        {
            // A bad regex backtrack or an unexpected input must never take a page down; the
            // reader would rather have unhighlighted code than a 500.
            return WebUtility.HtmlEncode(code);
        }
    }

    /// <summary>
    /// Strips ColorCode's <c>&lt;div class="lang"&gt;&lt;pre&gt;</c> wrapper so the caller can
    /// emit its own <c>&lt;pre&gt;&lt;code&gt;</c>. Returns null if the wrapper is not there,
    /// which is the signal to fall back to plain escaped code.
    /// </summary>
    internal static string? Unwrap(string html)
    {
        const string open = "<pre>";
        const string close = "</pre>";

        var start = html.IndexOf(open, StringComparison.Ordinal);
        var end = html.LastIndexOf(close, StringComparison.Ordinal);
        if (start < 0 || end < start + open.Length)
        {
            return null;
        }

        var inner = html[(start + open.Length)..end];

        // ColorCode opens with a newline after <pre>; a browser eats a leading newline inside
        // <pre> anyway, but trimming it keeps the emitted markup honest.
        if (inner.StartsWith('\n'))
        {
            inner = inner[1..];
        }
        else if (inner.StartsWith("\r\n", StringComparison.Ordinal))
        {
            inner = inner[2..];
        }

        return inner;
    }

    private static void EnsureLanguagesLoaded()
    {
        if (_languagesLoaded)
        {
            return;
        }

        lock (LoadLock)
        {
            if (_languagesLoaded)
            {
                return;
            }

            foreach (var language in AdditionalLanguages.All)
            {
                Languages.Load(language);
            }

            _languagesLoaded = true;
        }
    }
}
