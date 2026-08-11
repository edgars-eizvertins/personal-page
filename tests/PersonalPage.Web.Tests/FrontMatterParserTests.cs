using PersonalPage.Web.Content;

namespace PersonalPage.Web.Tests;

public class FrontMatterParserTests
{
    [Fact]
    public void No_front_matter_leaves_the_whole_file_as_body()
    {
        var result = FrontMatterParser.Parse("# Hello\n\nSome text.");

        Assert.Null(result.FrontMatter.Title);
        Assert.Equal("# Hello\n\nSome text.", result.Body);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Front_matter_is_parsed_and_removed_from_the_body()
    {
        var result = FrontMatterParser.Parse("---\ntitle: Hello\n---\n\nSome text.");

        Assert.Equal("Hello", result.FrontMatter.Title);
        Assert.Equal("\nSome text.", result.Body);
        Assert.DoesNotContain("---", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_front_matter_block_is_legal()
    {
        var result = FrontMatterParser.Parse("---\n---\nBody.");

        Assert.Null(result.Error);
        Assert.Null(result.FrontMatter.Title);
        Assert.Equal("Body.", result.Body);
    }

    [Fact]
    public void Unclosed_fence_is_treated_as_no_front_matter_and_keeps_the_body()
    {
        var text = "---\ntitle: Hello\n\nBody with no closing fence.";

        var result = FrontMatterParser.Parse(text);

        Assert.Null(result.FrontMatter.Title);
        Assert.Equal(text, result.Body);
    }

    [Fact]
    public void A_horizontal_rule_in_the_body_is_not_a_front_matter_fence()
    {
        var result = FrontMatterParser.Parse("---\ntitle: Hello\n---\n\nOne\n\n---\n\nTwo\n");

        Assert.Equal("Hello", result.FrontMatter.Title);
        Assert.Contains("---", result.Body, StringComparison.Ordinal);
        Assert.Contains("Two", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fence_that_is_not_the_first_line_is_not_front_matter()
    {
        var text = "Intro paragraph.\n\n---\ntitle: Hello\n---\n";

        var result = FrontMatterParser.Parse(text);

        Assert.Null(result.FrontMatter.Title);
        Assert.Equal(text, result.Body);
    }

    [Fact]
    public void Crlf_line_endings_parse_identically_to_lf()
    {
        var lf = FrontMatterParser.Parse("---\ntitle: Hello\ndraft: true\n---\n\nBody.");
        var crlf = FrontMatterParser.Parse("---\r\ntitle: Hello\r\ndraft: true\r\n---\r\n\r\nBody.");

        Assert.Equal(lf.FrontMatter.Title, crlf.FrontMatter.Title);
        Assert.Equal(lf.FrontMatter.Draft, crlf.FrontMatter.Draft);
        Assert.Contains("Body.", crlf.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_utf8_bom_does_not_break_fence_detection()
    {
        var result = FrontMatterParser.Parse("﻿---\ntitle: Hello\n---\n\nBody.");

        Assert.Equal("Hello", result.FrontMatter.Title);
    }

    [Fact]
    public void Unknown_keys_are_ignored_rather_than_throwing()
    {
        var result = FrontMatterParser.Parse("---\ntitle: Hello\nsome_future_key: 42\n---\nBody.");

        Assert.Null(result.Error);
        Assert.Equal("Hello", result.FrontMatter.Title);
    }

    [Fact]
    public void Underscored_keys_map_to_pascal_case_properties()
    {
        var result = FrontMatterParser.Parse("---\nnav_order: 3\nnav_title: Short\n---\nBody.");

        Assert.Equal(3, result.FrontMatter.NavOrder);
        Assert.Equal("Short", result.FrontMatter.NavTitle);
    }

    [Fact]
    public void A_quoted_title_containing_a_colon_parses()
    {
        var result = FrontMatterParser.Parse("---\ntitle: \"Blazor: a retrospective\"\n---\nBody.");

        Assert.Equal("Blazor: a retrospective", result.FrontMatter.Title);
    }

    [Fact]
    public void An_unquoted_title_containing_a_colon_degrades_instead_of_crashing()
    {
        // YAML reads "Blazor: a retrospective" as a nested mapping, which cannot bind to a
        // string. The body must still render and the title falls back to the filename later.
        var result = FrontMatterParser.Parse("---\ntitle: Blazor: a retrospective\n---\n\nBody.");

        Assert.NotNull(result.Error);
        Assert.Null(result.FrontMatter.Title);
        Assert.Contains("Body.", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Tags_accept_block_sequence_form()
    {
        var result = FrontMatterParser.Parse("---\ntags:\n  - one\n  - two\n---\nBody.");

        Assert.Equal(["one", "two"], result.FrontMatter.Tags);
    }

    [Fact]
    public void Tags_accept_inline_sequence_form()
    {
        var result = FrontMatterParser.Parse("---\ntags: [one, two]\n---\nBody.");

        Assert.Equal(["one", "two"], result.FrontMatter.Tags);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("no", false)]
    public void Boolean_forms_bind_as_documented(string written, bool expected)
    {
        // Pins what YamlDotNet actually does, so docs/content-authoring.md can state it.
        var result = FrontMatterParser.Parse($"---\ndraft: {written}\n---\nBody.");

        Assert.Null(result.Error);
        Assert.Equal(expected, result.FrontMatter.Draft);
    }

    [Fact]
    public void Dates_bind_to_DateOnly_independent_of_machine_locale()
    {
        var result = FrontMatterParser.Parse("---\ndate: 2026-01-15\nstart: 2020-03\nend: 2023\n---\nBody.");

        Assert.Equal(new DateOnly(2026, 1, 15), result.FrontMatter.Date);
        Assert.Equal(new DateOnly(2020, 3, 1), result.FrontMatter.Start);
        Assert.Equal(new DateOnly(2023, 1, 1), result.FrontMatter.End);
    }

    [Fact]
    public void An_unparseable_date_degrades_rather_than_throwing()
    {
        var result = FrontMatterParser.Parse("---\ntitle: Hello\ndate: last tuesday\n---\n\nBody.");

        Assert.NotNull(result.Error);
        Assert.Contains("Body.", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Site_config_falls_back_to_defaults_when_malformed()
    {
        var config = FrontMatterParser.ParseSiteConfig("title: [unclosed", out var error);

        Assert.NotNull(error);
        Assert.Equal(SiteConfig.Default().Title, config.Title);
    }

    [Fact]
    public void Site_config_binds_links_and_nav()
    {
        const string yaml = """
            title: A Site
            links:
              - label: GitHub
                url: https://example.com
            nav:
              - title: Writing
                url: /blog
                order: 2
            """;

        var config = FrontMatterParser.ParseSiteConfig(yaml, out var error);

        Assert.Null(error);
        Assert.Equal("A Site", config.Title);
        Assert.Equal("GitHub", Assert.Single(config.Links).Label);
        Assert.Equal("/blog", Assert.Single(config.Nav).Url);
    }
}
