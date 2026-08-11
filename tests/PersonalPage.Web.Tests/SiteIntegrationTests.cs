using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using PersonalPage.Web.Tests.Fixtures;

namespace PersonalPage.Web.Tests;

/// <summary>
/// End-to-end over the real application.
/// </summary>
/// <remarks>
/// Routing precedence — literal routes out-ranking <c>@page "/{*path}"</c> — is the single
/// riskiest assumption in this design, and no unit test on the content store can catch it if it
/// is wrong.
/// </remarks>
public class SiteIntegrationTests : IClassFixture<SiteFixture>
{
    private readonly SiteFixture _site;
    private readonly HttpClient _client;

    public SiteIntegrationTests(SiteFixture site)
    {
        _site = site;
        _client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task The_blog_index_wins_over_the_catch_all()
    {
        var html = await _client.GetStringAsync("/blog");

        Assert.Contains("Hello post", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing here", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blog_post_renders_at_its_slug()
    {
        var html = await _client.GetStringAsync("/blog/hello");

        Assert.Contains("Hello post", html, StringComparison.Ordinal);
        Assert.Contains("Post body.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_file_resolves_through_the_catch_all()
    {
        var html = await _client.GetStringAsync("/about");

        Assert.Contains("About page", html, StringComparison.Ordinal);
        Assert.Contains("<h2", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/about")]
    [InlineData("/blog")]
    [InlineData("/blog/hello")]
    [InlineData("/projects")]
    [InlineData("/projects/thing")]
    [InlineData("/experience")]
    [InlineData("/_diagnostics")]
    [InlineData("/healthz")]
    public async Task Every_route_returns_200(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_path_returns_a_real_404_with_the_editable_body()
    {
        var response = await _client.GetAsync("/no-such-page");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Nothing here", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_collection_item_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/blog/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/projects/nope")).StatusCode);
    }

    [Fact]
    public async Task A_draft_is_404_at_its_url_and_absent_from_the_index()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/blog/secret")).StatusCode);
        Assert.DoesNotContain("Secret post", await _client.GetStringAsync("/blog"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mixed_case_and_trailing_slashes_resolve_to_the_same_page()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/About")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/about/")).StatusCode);
    }

    [Fact]
    public async Task The_nav_combines_pages_and_site_yml_entries()
    {
        var html = await _client.GetStringAsync("/");

        Assert.Contains(">About page<", html, StringComparison.Ordinal);
        Assert.Contains(">Writing<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Security_headers_are_present_and_allow_the_inline_theme_script_by_hash()
    {
        var response = await _client.GetAsync("/");

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("'sha256-", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task An_asset_dropped_into_content_is_served_from_media()
    {
        _site.Write("assets/probe.txt", "hello");

        var response = await _client.GetAsync("/media/probe.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_request_with_a_matching_etag_returns_304_and_200_again_after_a_change()
    {
        _site.Write("pages/cacheable.md", "---\ntitle: Cacheable\n---\n\nOne.");

        var first = await _client.GetAsync("/cacheable");
        var etag = first.Headers.ETag;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(etag);

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/cacheable");
        conditional.Headers.IfNoneMatch.Add(etag!);
        Assert.Equal(HttpStatusCode.NotModified, (await _client.SendAsync(conditional)).StatusCode);

        _site.Write("pages/cacheable.md", "---\ntitle: Cacheable\n---\n\nTwo, which is longer.");

        var afterChange = new HttpRequestMessage(HttpMethod.Get, "/cacheable");
        afterChange.Headers.IfNoneMatch.Add(etag!);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(afterChange)).StatusCode);
    }

    [Fact]
    public async Task An_if_modified_since_from_the_future_returns_304()
    {
        var response = await _client.GetAsync("/about");
        var lastModified = response.Content.Headers.LastModified;

        Assert.NotNull(lastModified);

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/about");
        conditional.Headers.IfModifiedSince = lastModified;

        Assert.Equal(HttpStatusCode.NotModified, (await _client.SendAsync(conditional)).StatusCode);
    }

    [Fact]
    public async Task A_page_created_while_running_is_served_and_404s_once_deleted()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/scratch")).StatusCode);

        _site.Write("pages/scratch.md", "---\ntitle: Scratch\n---\n\nMade at runtime.");
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/scratch")).StatusCode);

        _site.Delete("pages/scratch.md");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/scratch")).StatusCode);
    }

    [Fact]
    public async Task Editing_a_page_is_visible_with_no_restart()
    {
        _site.Write("pages/live.md", "---\ntitle: Before\n---\n\nBody.");
        Assert.Contains("Before", await _client.GetStringAsync("/live"), StringComparison.Ordinal);

        _site.Write("pages/live.md", "---\ntitle: After the edit\n---\n\nBody.");
        Assert.Contains("After the edit", await _client.GetStringAsync("/live"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/../../etc/passwd")]
    [InlineData("/%2e%2e%2fetc%2fpasswd")]
    [InlineData("/%252e%252e%252fetc")]
    public async Task Traversal_attempts_do_not_escape_the_content_root(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_health_check_reports_healthy_over_a_real_content_root()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// Separate from the fixture-backed tests because it deliberately boots against a content root
/// that is not there — the failure a forker hits when they skip
/// <c>cp -r content.example content</c>, and the one Docker most needs to catch.
/// </summary>
public class HealthCheckTests
{
    [Fact]
    public async Task The_health_check_is_unhealthy_when_the_content_root_is_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "personal-page-tests", Guid.NewGuid().ToString("n"));

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("Content:RootPath", missing);
            });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var health = factory.Services.GetRequiredService<HealthCheckService>();
        var report = await health.CheckHealthAsync();
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
    }
}
