using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PersonalPage.Web.Content.Markdown;

/// <summary>
/// Markdown to HTML. Every transformation here runs once per document and lands in the cached
/// HTML, so it costs nothing per request.
/// </summary>
public sealed class MarkdownRenderer
{
    private const int MaxHeadingLevel = 6;

    private readonly MarkdownPipeline _pipeline;
    private readonly SyntaxHighlighter _highlighter;

    public MarkdownRenderer()
    {
        _highlighter = new SyntaxHighlighter();

        // Raw HTML stays enabled: content/ is author-trusted and it makes embeds possible.
        // See the trust boundary note in docs/architecture.md before pointing this at anything
        // a stranger can write to.
        //
        // UseYamlFrontMatter is deliberately absent. FrontMatterParser has already removed the
        // fence, so the extension would have nothing to do except swallow a horizontal rule that
        // happens to be the first thing in a body.
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoLinks()
            .Build();
    }

    public string Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var document = Markdig.Markdown.Parse(markdown, _pipeline);

        DemoteHeadings(document);
        AnnotateImages(document);

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<CodeBlockRenderer>(new HighlightedCodeBlockRenderer(_highlighter));
        renderer.Render(document);
        writer.Flush();

        return writer.ToString();
    }

    /// <summary>
    /// Pushes every body heading down one level when the body contains an <c>h1</c>, so a stray
    /// <c>#</c> renders as <c>&lt;h2&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The layout renders the front-matter title as the page's single <c>&lt;h1&gt;</c>, so
    /// bodies are supposed to start at <c>##</c>. Demoting rather than trusting authors to
    /// remember keeps exactly one h1 per page — two would be an accessibility defect and an
    /// ambiguity the type scale cannot resolve.
    /// <para>
    /// The demotion is conditional on purpose. Shifting every document unconditionally would
    /// leave <c>&lt;h2&gt;</c> permanently unused and push a correctly authored body one step out
    /// of the type scale the design specifies. So a body that already starts at <c>##</c> is left
    /// exactly as written, and only one that opens an <c>h1</c> gets moved.
    /// </para>
    /// <c>######</c> stays at six rather than overflowing into an element that does not exist.
    /// </remarks>
    private static void DemoteHeadings(MarkdownDocument document)
    {
        var headings = document.Descendants<HeadingBlock>().ToList();
        if (!headings.Any(h => h.Level <= 1))
        {
            return;
        }

        foreach (var heading in headings)
        {
            heading.Level = Math.Min(heading.Level + 1, MaxHeadingLevel);
        }
    }

    /// <summary>
    /// Adds <c>loading="lazy"</c> and <c>decoding="async"</c> to every image, so a long page does
    /// not block on pictures being read off slow storage.
    /// </summary>
    /// <remarks>
    /// Explicit dimensions are not invented — an author who knows them writes
    /// <c>![alt](x.png){width=800 height=450}</c> and the generic-attributes extension carries
    /// them through untouched.
    /// </remarks>
    private static void AnnotateImages(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!link.IsImage)
            {
                continue;
            }

            var attributes = link.GetAttributes();
            attributes.AddPropertyIfNotExist("loading", "lazy");
            attributes.AddPropertyIfNotExist("decoding", "async");
        }
    }
}
