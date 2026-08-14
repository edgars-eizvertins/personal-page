using System.Text.RegularExpressions;
using PersonalPage.Web.Content.Markdown;

namespace PersonalPage.Web.Tests;

public class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void A_stray_h1_in_the_body_is_demoted_to_h2()
    {
        var html = _renderer.Render("# Top\n\ntext");

        Assert.Contains("<h2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Demotion_never_overflows_past_h6()
    {
        var html = _renderer.Render("# One\n\n###### Six\n");

        Assert.Contains("<h2", html, StringComparison.Ordinal);
        Assert.Contains("<h6", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h7", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_that_already_starts_at_h2_is_left_alone()
    {
        // Demotion is a safety net for a stray h1, not an unconditional shift. Moving a
        // correctly authored body would leave h2 permanently unused and push everything one
        // step out of the designed type scale.
        var html = _renderer.Render("## Section\n\n### Subsection\n");

        Assert.Contains("<h2", html, StringComparison.Ordinal);
        Assert.Contains("<h3", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h4", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_headings_with_identical_text_get_unique_ids()
    {
        // The auto-identifier extension is what makes "#section" links inside a body work, so
        // duplicate heading text must not produce duplicate anchors.
        var html = _renderer.Render("## Notes\n\ntext\n\n## Notes\n\nmore\n");

        var ids = Regex.Matches(html, """<h2 id="(?<id>[^"]+)">""")
            .Select(m => m.Groups["id"].Value)
            .ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Images_gain_lazy_loading_and_async_decoding()
    {
        var html = _renderer.Render("![alt](/media/x.png)");

        Assert.Contains("loading=\"lazy\"", html, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_image_dimensions_are_carried_through()
    {
        var html = _renderer.Render("![alt](/media/x.png){width=800 height=450}");

        Assert.Contains("width=\"800\"", html, StringComparison.Ordinal);
        Assert.Contains("height=\"450\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_known_language_is_highlighted_server_side()
    {
        var html = _renderer.Render("```csharp\nvar x = 1;\n```");

        Assert.Contains("class=\"language-csharp\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"keyword\">var</span>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("sh")]
    [InlineData("yaml")]
    [InlineData("yml")]
    public void Shell_and_yaml_are_highlighted_too(string language)
    {
        // Neither ships with ColorCode; both are defined in AdditionalLanguages because they are
        // the fences a developer site uses most after its own stack.
        var html = _renderer.Render($"```{language}\n# a comment\n```");

        Assert.Contains("<span class=\"comment\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognised_language_renders_as_plain_code()
    {
        var html = _renderer.Render("```brainfuck\n+++>+++\n```");

        Assert.Contains("class=\"language-brainfuck\"", html, StringComparison.Ordinal);
        Assert.Contains("+++&gt;+++", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span class=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fence_with_no_language_renders()
    {
        var html = _renderer.Render("```\nplain text\n```");

        Assert.Contains("<pre><code>plain text", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"language-", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_code_block_is_wrapped_for_the_copy_button()
    {
        var html = _renderer.Render("```json\n{}\n```");

        Assert.Contains("<div class=\"code-block\" data-language=\"json\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Code_content_is_html_escaped()
    {
        var html = _renderer.Render("```\n<script>alert(1)</script>\n```");

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_html_in_the_body_survives_to_the_output()
    {
        // Documents the trust decision as a test: content/ is author-trusted, and raw HTML
        // passthrough is what makes embeds possible. See docs/architecture.md.
        var html = _renderer.Render("<figure><img src=\"/media/x.png\"><figcaption>Hi</figcaption></figure>");

        Assert.Contains("<figcaption>Hi</figcaption>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tables_and_autolinks_from_the_advanced_extensions_work()
    {
        var html = _renderer.Render("| a | b |\n| - | - |\n| 1 | 2 |\n\nhttps://example.com\n");

        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://example.com\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_leading_horizontal_rule_is_not_swallowed_as_front_matter()
    {
        // The front matter fence is removed before Markdig sees the text, so the YAML front
        // matter extension is deliberately absent from the pipeline. If it were enabled, this
        // rule would disappear.
        var html = _renderer.Render("---\n\nText after a rule.\n");

        Assert.Contains("<hr", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_document_renders_nothing()
    {
        Assert.Equal(string.Empty, _renderer.Render("   "));
    }
}
