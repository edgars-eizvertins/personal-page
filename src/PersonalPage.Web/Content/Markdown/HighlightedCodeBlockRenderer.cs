using System.Text;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace PersonalPage.Web.Content.Markdown;

/// <summary>
/// Replaces Markdig's default code block renderer with one that highlights on the server and
/// emits a wrapper the copy-button script can hook onto.
/// </summary>
/// <remarks>
/// Output shape:
/// <code>
/// &lt;div class="code-block" data-language="csharp"&gt;
///   &lt;pre&gt;&lt;code class="language-csharp"&gt;…&lt;/code&gt;&lt;/pre&gt;
/// &lt;/div&gt;
/// </code>
/// The copy button itself is added by <c>code-copy.js</c>, not rendered here: a button that
/// cannot work without script has no business existing in the server-rendered HTML.
/// </remarks>
public sealed class HighlightedCodeBlockRenderer(SyntaxHighlighter highlighter)
    : HtmlObjectRenderer<CodeBlock>
{
    protected override void Write(HtmlRenderer renderer, CodeBlock obj)
    {
        var info = (obj as FencedCodeBlock)?.Info;
        var language = SyntaxHighlighter.FindLanguage(info);
        var label = LanguageLabel(info);
        var code = ExtractCode(obj);

        renderer.EnsureLine();
        renderer.Write("<div class=\"code-block\"");
        if (label is not null)
        {
            renderer.Write(" data-language=\"").WriteEscape(label).Write("\"");
        }

        renderer.Write("><pre><code");
        if (label is not null)
        {
            renderer.Write(" class=\"language-").WriteEscape(label).Write("\"");
        }

        renderer.Write(">");
        renderer.Write(highlighter.Highlight(code, language));
        renderer.Write("</code></pre></div>");
        renderer.EnsureLine();
    }

    /// <summary>The fence's language word, used for the class name and the copy button label.</summary>
    private static string? LanguageLabel(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return null;
        }

        var word = info.Trim().Split(' ', '\t')[0].Trim();
        return word.Length == 0 ? null : word.ToLowerInvariant();
    }

    private static string ExtractCode(LeafBlock block)
    {
        var lines = block.Lines.Lines;
        if (lines is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < block.Lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines[i].Slice.AsSpan());
        }

        return builder.ToString();
    }
}
