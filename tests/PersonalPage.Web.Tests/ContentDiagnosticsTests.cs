using PersonalPage.Web.Content.Diagnostics;
using PersonalPage.Web.Tests.Fixtures;

namespace PersonalPage.Web.Tests;

public class ContentDiagnosticsTests
{
    [Fact]
    public async Task A_clean_content_tree_reports_nothing()
    {
        // Guards against false positives, which are what make a diagnostics page get ignored.
        var store = ContentFixtureBuilder.Create()
            .Page("home", "title: Home", "Read [about](/about) and the [blog](/blog).")
            .Page("about", "title: About", "Back [home](/).")
            .Post("2026-01-01-hello", "title: Hello\ndate: 2026-01-01", "See ![it](/media/x.png)")
            .Asset("x.png")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.True(report.IsClean, DescribeFailure(report));
    }

    [Fact]
    public async Task An_internal_link_to_a_nonexistent_page_is_reported()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("home", "title: Home", "Read [the manual](/manual).")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        var broken = Assert.Single(report.BrokenLinks);
        Assert.Equal("/manual", broken.Href);
        Assert.Equal("pages/home.md", broken.SourcePath);
    }

    [Fact]
    public async Task Valid_internal_and_external_links_are_not_reported()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("home", "title: Home",
                "[About](/about) and [example](https://example.com) and [mail](mailto:a@b.c) and [top](#intro).")
            .Page("about", "title: About")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.Empty(report.BrokenLinks);
    }

    [Fact]
    public async Task A_link_to_a_collection_item_resolves()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("home", "title: Home", "Read [the post](/blog/hello).")
            .Post("2026-01-01-hello", "title: Hello\ndate: 2026-01-01")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.Empty(report.BrokenLinks);
    }

    [Fact]
    public async Task A_media_reference_with_no_matching_file_is_reported()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("home", "title: Home", "![A chart](/media/chart.png)")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        var missing = Assert.Single(report.MissingMedia);
        Assert.Equal("/media/chart.png", missing.Href);
    }

    [Fact]
    public async Task An_asset_referenced_by_nothing_is_reported_as_orphaned()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("home", "title: Home", "![Used](/media/used.png)")
            .Asset("used.png")
            .Asset("forgotten.png")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.Equal("forgotten.png", Assert.Single(report.OrphanedAssets));
    }

    [Fact]
    public async Task An_image_named_only_in_front_matter_counts_as_referenced()
    {
        var store = ContentFixtureBuilder.Create()
            .Project("thing", "title: Thing\nimage: /media/thumb.png")
            .Asset("thumb.png")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.Empty(report.OrphanedAssets);
        Assert.Empty(report.MissingMedia);
    }

    [Fact]
    public async Task Front_matter_failures_are_surfaced()
    {
        var store = ContentFixtureBuilder.Create()
            .File("pages/broken.md", "---\ntitle: Broken: nope\n---\n\nBody.")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.Equal("pages/broken.md", Assert.Single(report.ParseIssues).RelativePath);
    }

    [Fact]
    public async Task Drafts_and_future_dated_documents_are_listed()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);

        var store = ContentFixtureBuilder.Create()
            .ShowingDrafts()
            .Post("draft-post", "title: Draft\ndraft: true\ndate: 2020-01-01")
            .Post("future-post", $"title: Future\ndate: {future:yyyy-MM-dd}")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.Equal("Draft", Assert.Single(report.Drafts).Title);
        Assert.Equal("Future", Assert.Single(report.FutureDated).Title);
    }

    [Fact]
    public async Task A_relative_link_resolves_against_the_source_document()
    {
        var store = ContentFixtureBuilder.Create()
            .Post("2026-01-01-one", "title: One\ndate: 2026-01-01", "See [two](two).")
            .Post("2026-01-02-two", "title: Two\ndate: 2026-01-02")
            .Build();

        var report = await new ContentDiagnostics(store).RunAsync();

        Assert.Empty(report.BrokenLinks);
    }

    private static string DescribeFailure(DiagnosticsReport report) =>
        $"parse={report.ParseIssues.Count} links={string.Join(", ", report.BrokenLinks.Select(l => l.Href))} " +
        $"media={string.Join(", ", report.MissingMedia.Select(m => m.Href))} " +
        $"orphans={string.Join(", ", report.OrphanedAssets)} future={report.FutureDated.Count}";
}
