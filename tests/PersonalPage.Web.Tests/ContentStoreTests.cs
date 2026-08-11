using PersonalPage.Web.Content;
using PersonalPage.Web.Tests.Fixtures;

namespace PersonalPage.Web.Tests;

public class ContentStoreTests
{
    // ------------------------------------------------------------------ pages

    [Fact]
    public async Task Serves_a_page_from_pages_by_slug()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("about", "title: About", "## Hello")
            .Build();

        var page = await store.GetPageAsync("about");

        Assert.NotNull(page);
        Assert.Equal("About", page.Title);
        Assert.Contains("<h2", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_path_resolves_to_home()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("home", "title: Welcome")
            .Build();

        Assert.Equal("Welcome", (await store.GetPageAsync(""))?.Title);
        Assert.Equal("Welcome", (await store.GetPageAsync("/"))?.Title);
    }

    [Theory]
    [InlineData("/About")]
    [InlineData("about/")]
    [InlineData("//about")]
    public async Task Path_variants_resolve_to_the_same_page(string path)
    {
        var store = ContentFixtureBuilder.Create().Page("about", "title: About").Build();

        Assert.NotNull(await store.GetPageAsync(path));
    }

    [Fact]
    public async Task A_missing_page_is_null_rather_than_an_exception()
    {
        var store = ContentFixtureBuilder.Create().Page("about").Build();

        Assert.Null(await store.GetPageAsync("nope"));
    }

    [Fact]
    public async Task A_title_falls_back_to_the_filename_when_front_matter_has_none()
    {
        var store = ContentFixtureBuilder.Create().Page("my-page", frontMatter: null).Build();

        Assert.Equal("My Page", (await store.GetPageAsync("my-page"))!.Title);
    }

    [Fact]
    public async Task Pages_in_a_subfolder_keep_their_folder_in_the_url()
    {
        var store = ContentFixtureBuilder.Create()
            .File("pages/notes/setup.md", "---\ntitle: Setup\n---\n\nBody.")
            .Build();

        var page = await store.GetPageAsync("notes/setup");

        Assert.NotNull(page);
        Assert.Equal("/notes/setup", page.Url);
    }

    // ------------------------------------------------------------- security

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("%2e%2e%2fetc%2fpasswd")]
    [InlineData("%252e%252e%252fetc%252fpasswd")]
    [InlineData("/etc/passwd")]
    [InlineData("about\0")]
    public async Task Traversal_attempts_resolve_to_nothing(string path)
    {
        var store = ContentFixtureBuilder.Create().Page("about").Build();

        Assert.Null(await store.GetPageAsync(path));
    }

    [Fact]
    public async Task A_symlink_pointing_outside_the_root_is_rejected()
    {
        var builder = ContentFixtureBuilder.Create().Page("about");
        builder.FileSystem.Symlink("pages/escape.md", "/etc/passwd");

        var store = builder.Build();

        Assert.Null(await store.GetPageAsync("escape"));
    }

    // ---------------------------------------------------------- collections

    [Fact]
    public async Task A_collection_sorts_newest_first()
    {
        var store = ContentFixtureBuilder.Create()
            .Post("2025-01-01-old", "title: Old\ndate: 2025-01-01")
            .Post("2026-01-01-new", "title: New\ndate: 2026-01-01")
            .Post("2025-06-01-middle", "title: Middle\ndate: 2025-06-01")
            .Build();

        var posts = await store.GetCollectionAsync(ContentFolders.Blog);

        Assert.Equal(["New", "Middle", "Old"], posts.Select(p => p.Title));
    }

    [Fact]
    public async Task Ties_sort_deterministically_on_slug()
    {
        var store = ContentFixtureBuilder.Create()
            .Post("2026-01-01-zebra", "date: 2026-01-01")
            .Post("2026-01-01-apple", "date: 2026-01-01")
            .Build();

        var posts = await store.GetCollectionAsync(ContentFolders.Blog);

        Assert.Equal(["apple", "zebra"], posts.Select(p => p.Slug));
    }

    [Fact]
    public async Task A_document_with_no_date_sorts_last_rather_than_throwing()
    {
        var store = ContentFixtureBuilder.Create()
            .Post("undated", "title: Undated")
            .Post("2020-01-01-dated", "title: Dated\ndate: 2020-01-01")
            .Build();

        var posts = await store.GetCollectionAsync(ContentFolders.Blog);

        Assert.Equal(["Dated", "Undated"], posts.Select(p => p.Title));
    }

    [Fact]
    public async Task A_dated_filename_supplies_the_date_when_front_matter_does_not()
    {
        var store = ContentFixtureBuilder.Create()
            .Post("2026-01-15-hello", "title: Hello")
            .Build();

        var post = await store.GetItemAsync(ContentFolders.Blog, "hello");

        Assert.Equal(new DateOnly(2026, 1, 15), post!.FrontMatter.Date);
    }

    [Fact]
    public async Task A_future_dated_document_is_still_served()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        var store = ContentFixtureBuilder.Create()
            .Post("later", $"title: Later\ndate: {future:yyyy-MM-dd}")
            .Build();

        Assert.NotNull(await store.GetItemAsync(ContentFolders.Blog, "later"));
    }

    [Fact]
    public async Task An_ongoing_role_sorts_first_and_has_no_end_date()
    {
        var store = ContentFixtureBuilder.Create()
            .Role("finished", "company: Old Co\nstart: 2015-01\nend: 2019-12")
            .Role("current", "company: New Co\nstart: 2020-01")
            .Build();

        var roles = await store.GetCollectionAsync(ContentFolders.Experience);

        Assert.Equal(["current", "finished"], roles.Select(r => r.Slug));
        Assert.Null(roles[0].FrontMatter.End);
    }

    [Fact]
    public async Task Featured_projects_are_discoverable_from_front_matter()
    {
        var store = ContentFixtureBuilder.Create()
            .Project("plain", "title: Plain\ndate: 2026-01-01")
            .Project("starred", "title: Starred\ndate: 2020-01-01\nfeatured: true")
            .Build();

        var projects = await store.GetCollectionAsync(ContentFolders.Projects);

        Assert.Equal("Starred", projects.Single(p => p.FrontMatter.Featured).Title);
    }

    [Fact]
    public async Task Two_files_deriving_the_same_slug_pick_a_deterministic_winner()
    {
        var store = ContentFixtureBuilder.Create()
            .Post("2026-01-01-hello", "title: First by filename")
            .Post("2026-02-01-hello", "title: Second by filename")
            .Build();

        var posts = await store.GetCollectionAsync(ContentFolders.Blog);

        Assert.Equal("First by filename", Assert.Single(posts).Title);

        // The loser is reported rather than silently dropped.
        var issue = Assert.Single(store.Issues);
        Assert.Contains("already taken", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_collection_folder_yields_an_empty_list()
    {
        var store = ContentFixtureBuilder.Create().Post("one").Build();

        Assert.Empty(await store.GetCollectionAsync("../etc"));
    }

    // -------------------------------------------------------------- drafts

    [Fact]
    public async Task A_draft_is_excluded_from_listings()
    {
        var store = ContentFixtureBuilder.Create()
            .Post("published", "title: Published\ndate: 2026-01-01")
            .Post("hidden", "title: Hidden\ndate: 2026-01-02\ndraft: true")
            .Build();

        var posts = await store.GetCollectionAsync(ContentFolders.Blog);

        Assert.Equal("Published", Assert.Single(posts).Title);
    }

    [Fact]
    public async Task A_draft_is_404_at_its_direct_url()
    {
        // Not merely unlisted: an unpublished post must not leak to anyone who guesses the slug.
        var store = ContentFixtureBuilder.Create()
            .Post("hidden", "title: Hidden\ndraft: true")
            .Page("secret", "title: Secret\ndraft: true")
            .Build();

        Assert.Null(await store.GetItemAsync(ContentFolders.Blog, "hidden"));
        Assert.Null(await store.GetPageAsync("secret"));
    }

    [Fact]
    public async Task A_draft_page_never_appears_in_the_nav()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("shown", "title: Shown\nnav_order: 1")
            .Page("hidden", "title: Hidden\nnav_order: 2\ndraft: true")
            .Build();

        var nav = await store.GetNavAsync();

        Assert.Equal("Shown", Assert.Single(nav).Title);
    }

    [Fact]
    public async Task Drafts_are_visible_when_the_development_setting_is_on()
    {
        var store = ContentFixtureBuilder.Create()
            .ShowingDrafts()
            .Post("hidden", "title: Hidden\ndraft: true")
            .Build();

        Assert.NotNull(await store.GetItemAsync(ContentFolders.Blog, "hidden"));
        Assert.Single(await store.GetCollectionAsync(ContentFolders.Blog));
    }

    // ------------------------------------------------------------------ nav

    [Fact]
    public async Task Only_pages_declaring_nav_order_appear_in_the_nav()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("about", "title: About\nnav_order: 1")
            .Page("colophon", "title: Colophon")
            .Build();

        var nav = await store.GetNavAsync();

        Assert.Equal("About", Assert.Single(nav).Title);
    }

    [Fact]
    public async Task Nav_sorts_by_order_then_deterministically_on_title()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("zeta", "title: Zeta\nnav_order: 1")
            .Page("alpha", "title: Alpha\nnav_order: 1")
            .Page("later", "title: Later\nnav_order: 5")
            .Build();

        var nav = await store.GetNavAsync();

        Assert.Equal(["Alpha", "Zeta", "Later"], nav.Select(n => n.Title));
    }

    [Fact]
    public async Task Nav_title_overrides_title_when_present()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("about", "title: About this website\nnav_title: About\nnav_order: 1")
            .Build();

        Assert.Equal("About", Assert.Single(await store.GetNavAsync()).Title);
    }

    [Fact]
    public async Task Site_yml_contributes_nav_entries_for_the_collection_routes()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("about", "title: About\nnav_order: 1")
            .Site("title: A Site\nnav:\n  - title: Writing\n    url: /blog\n    order: 2\n")
            .Build();

        var nav = await store.GetNavAsync();

        Assert.Equal(["About", "Writing"], nav.Select(n => n.Title));
        Assert.Equal("/blog", nav[1].Url);
    }

    [Fact]
    public async Task A_malformed_page_does_not_empty_the_nav()
    {
        var store = ContentFixtureBuilder.Create()
            .Page("good", "title: Good\nnav_order: 1")
            .File("pages/broken.md", "---\ntitle: Broken: nope\nnav_order: 2\n---\n\nBody.")
            .Build();

        var nav = await store.GetNavAsync();

        Assert.Equal("Good", Assert.Single(nav).Title);
        Assert.Single(store.Issues);
    }

    [Fact]
    public async Task A_malformed_document_still_serves_its_body_with_a_fallback_title()
    {
        var store = ContentFixtureBuilder.Create()
            .File("pages/broken.md", "---\ntitle: Broken: nope\n---\n\nThe body is fine.")
            .Post("intact", "title: Intact")
            .Build();

        var page = await store.GetPageAsync("broken");

        Assert.NotNull(page);
        Assert.Equal("Broken", page.Title);
        Assert.Contains("The body is fine.", page.Html, StringComparison.Ordinal);
        Assert.NotNull(page.ParseError);

        // And the rest of the site is unaffected.
        Assert.Single(await store.GetCollectionAsync(ContentFolders.Blog));
    }

    // ---------------------------------------------------------------- site

    [Fact]
    public async Task Site_config_falls_back_to_defaults_when_the_file_is_missing()
    {
        var store = ContentFixtureBuilder.Create().Page("home").Build();

        Assert.Equal(SiteConfig.Default().Title, (await store.GetSiteAsync()).Title);
    }

    [Fact]
    public async Task Site_config_is_picked_up_after_an_edit_with_no_restart()
    {
        var builder = ContentFixtureBuilder.Create().Site("title: First");
        var store = builder.Build();

        Assert.Equal("First", (await store.GetSiteAsync()).Title);

        builder.FileSystem.Write("site.yml", "title: Second");

        Assert.Equal("Second", (await store.GetSiteAsync()).Title);
    }

    [Fact]
    public async Task A_malformed_site_config_yields_defaults_and_an_issue()
    {
        var store = ContentFixtureBuilder.Create().Site("title: [unclosed").Build();

        Assert.Equal(SiteConfig.Default().Title, (await store.GetSiteAsync()).Title);
        Assert.Contains(store.Issues, i => i.RelativePath == "site.yml");
    }

    // -------------------------------------------------------------- caching

    [Fact]
    public async Task A_second_read_of_an_unchanged_document_does_not_reparse()
    {
        var builder = ContentFixtureBuilder.Create().Page("about", "title: About");
        var store = builder.Build();

        await store.GetPageAsync("about");
        builder.FileSystem.ResetReadCounts();

        await store.GetPageAsync("about");
        await store.GetPageAsync("about");

        Assert.Equal(0, builder.FileSystem.ReadsOf("pages/about.md"));
    }

    [Fact]
    public async Task A_changed_write_time_triggers_a_reparse()
    {
        var builder = ContentFixtureBuilder.Create().Page("about", "title: First");
        var store = builder.Build();

        Assert.Equal("First", (await store.GetPageAsync("about"))!.Title);

        builder.FileSystem.Write("pages/about.md", "---\ntitle: Second\n---\n\nBody.");

        Assert.Equal("Second", (await store.GetPageAsync("about"))!.Title);
    }

    [Fact]
    public async Task Same_mtime_and_same_length_does_not_reparse()
    {
        // Pins a real limitation of stat-based invalidation honestly rather than pretending it
        // away. Length is in the validator to narrow the window; this is the residual case, and
        // it is documented in docs/architecture.md.
        var builder = ContentFixtureBuilder.Create().Page("about", "title: AAAAA");
        var store = builder.Build();

        Assert.Equal("AAAAA", (await store.GetPageAsync("about"))!.Title);

        builder.FileSystem.WriteKeepingStat("pages/about.md", "---\ntitle: BBBBB\n---\n\nBody.");

        Assert.Equal("AAAAA", (await store.GetPageAsync("about"))!.Title);
    }

    [Fact]
    public async Task A_deleted_file_404s_and_its_cache_entry_is_dropped()
    {
        var builder = ContentFixtureBuilder.Create().Page("temp", "title: Temp");
        var store = builder.Build();

        Assert.NotNull(await store.GetPageAsync("temp"));

        builder.FileSystem.Delete("pages/temp.md");

        Assert.Null(await store.GetPageAsync("temp"));
    }

    [Fact]
    public async Task Concurrent_reads_of_a_cold_document_parse_it_once()
    {
        var builder = ContentFixtureBuilder.Create().Page("about", "title: About");
        var store = builder.Build();

        var reads = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(async () => await store.GetPageAsync("about")))
            .ToArray();

        var documents = await Task.WhenAll(reads);

        Assert.All(documents, d => Assert.Equal("About", d!.Title));
        Assert.Equal(1, builder.FileSystem.ReadsOf("pages/about.md"));
    }

    [Fact]
    public async Task A_new_file_appears_in_its_collection()
    {
        var builder = ContentFixtureBuilder.Create().Post("first", "title: First");
        var store = builder.Build();

        Assert.Single(await store.GetCollectionAsync(ContentFolders.Blog));

        builder.FileSystem.Write("blog/second.md", "---\ntitle: Second\n---\n\nBody.");

        Assert.Equal(2, (await store.GetCollectionAsync(ContentFolders.Blog)).Count);
    }

    // ----------------------------------------------------------- validators

    [Fact]
    public async Task A_page_validator_changes_when_the_page_changes()
    {
        var builder = ContentFixtureBuilder.Create().Page("about", "title: About");
        var store = builder.Build();

        var before = await store.GetValidatorAsync("/about");
        builder.FileSystem.Write("pages/about.md", "---\ntitle: About\n---\n\nDifferent body.");
        var after = await store.GetValidatorAsync("/about");

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before!.Value.ToETag(), after!.Value.ToETag());
    }

    [Fact]
    public async Task A_page_validator_also_covers_the_chrome()
    {
        // Adding a nav entry changes every page, so it has to change every page's ETag too.
        var builder = ContentFixtureBuilder.Create()
            .Page("about", "title: About")
            .Site("title: A Site");
        var store = builder.Build();

        var before = await store.GetValidatorAsync("/about");
        builder.FileSystem.Write("site.yml", "title: A Renamed Site");
        var after = await store.GetValidatorAsync("/about");

        Assert.NotEqual(before!.Value.ToETag(), after!.Value.ToETag());
    }

    [Fact]
    public async Task An_unknown_path_has_no_validator()
    {
        var store = ContentFixtureBuilder.Create().Page("about").Build();

        Assert.Null(await store.GetValidatorAsync("/nope"));
    }

    // --------------------------------------------------------------- assets

    [Fact]
    public void Assets_are_enumerated_relative_to_the_assets_folder()
    {
        var store = ContentFixtureBuilder.Create()
            .Asset("logo.png")
            .Asset("posts/chart.svg")
            .Build();

        Assert.Equal(["logo.png", "posts/chart.svg"], store.EnumerateAssets());
        Assert.True(store.AssetExists("posts/chart.svg"));
        Assert.False(store.AssetExists("missing.png"));
    }

    [Fact]
    public void An_asset_path_cannot_escape_the_assets_folder()
    {
        var store = ContentFixtureBuilder.Create().Page("about").Asset("logo.png").Build();

        Assert.False(store.AssetExists("../pages/about.md"));
    }
}
