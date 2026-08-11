using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace PersonalPage.Web.Tests.Fixtures;

/// <summary>
/// Drives the real application over a throwaway content directory on disk.
/// </summary>
/// <remarks>
/// The unit tests use a fake filesystem; this one deliberately does not. Routing precedence,
/// middleware order and the health check are exactly the things a fake would hide.
/// </remarks>
public sealed class SiteFixture : WebApplicationFactory<Program>
{
    public string ContentRoot { get; } = Path.Combine(
        Path.GetTempPath(), "personal-page-tests", Guid.NewGuid().ToString("n"));

    public SiteFixture()
    {
        Directory.CreateDirectory(Path.Combine(ContentRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(ContentRoot, "blog"));
        Directory.CreateDirectory(Path.Combine(ContentRoot, "projects"));
        Directory.CreateDirectory(Path.Combine(ContentRoot, "experience"));
        Directory.CreateDirectory(Path.Combine(ContentRoot, "assets"));

        WriteSite("""
            title: Test Site
            nav:
              - title: Writing
                url: /blog
                order: 2
            """);

        Write("pages/home.md", "---\ntitle: Home page\n---\n\nWelcome.");
        Write("pages/about.md", "---\ntitle: About page\nnav_order: 1\n---\n\n## Section\n\nText.");
        Write("pages/404.md", "---\ntitle: Nothing here\n---\n\nTry the [home page](/).");
        Write("blog/2026-01-15-hello.md", "---\ntitle: Hello post\ndate: 2026-01-15\n---\n\nPost body.");
        Write("blog/2026-02-01-secret.md", "---\ntitle: Secret post\ndate: 2026-02-01\ndraft: true\n---\n\nHidden.");
        Write("projects/thing.md", "---\ntitle: A project\ndate: 2026-01-01\n---\n\nProject body.");
        Write("experience/role.md", "---\ncompany: Example Co\nrole: Developer\nstart: 2020-01\n---\n\nRole body.");
    }

    public void Write(string relativePath, string content)
    {
        var full = Path.Combine(ContentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void WriteSite(string yaml) => Write("site.yml", yaml);

    public void Delete(string relativePath) => File.Delete(Path.Combine(ContentRoot, relativePath));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("Content:RootPath", ContentRoot);
        builder.UseSetting("Content:ShowDrafts", "false");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        try
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
