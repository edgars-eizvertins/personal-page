using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using PersonalPage.Web.Components;
using PersonalPage.Web.Content;
using PersonalPage.Web.Content.Diagnostics;
using PersonalPage.Web.Content.Markdown;
using PersonalPage.Web.Health;
using PersonalPage.Web.Http;

var builder = WebApplication.CreateBuilder(args);

// A mistyped Content__RootPath should refuse to start, loudly, rather than quietly serving an
// empty site.
builder.Services.AddOptions<ContentOptions>()
    .Bind(builder.Configuration.GetSection(ContentOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ContentRoot>();
builder.Services.AddSingleton<IContentFileSystem, PhysicalContentFileSystem>();
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddSingleton<IContentStore, MarkdownContentStore>();
builder.Services.AddScoped<ContentDiagnostics>();

builder.Services.AddHealthChecks()
    .AddCheck<ContentRootHealthCheck>("content-root");

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// Static server rendering only. No interactive render mode is registered anywhere: Blazor Server
// would hold a SignalR circuit per visitor and WebAssembly would ship a multi-megabyte runtime,
// neither of which this hardware has to spare. Client-side behaviour is plain files in wwwroot.
builder.Services.AddRazorComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseResponseCompression();

// content/assets served at /media, so an image drops in without a rebuild. Images are the bulk
// of the bytes and the slowest thing to read off SD storage, hence the long max-age.
//
// This has to run before routing. ContentPage's "/{*path}" catch-all matches every URL, and
// StaticFileMiddleware skips any request that has already matched an endpoint — so with the
// automatic UseRouting placement, /media would fall through to a 404. Hence the explicit
// UseRouting() call further down, which suppresses the automatic one.
var contentRoot = app.Services.GetRequiredService<ContentRoot>();
if (Directory.Exists(contentRoot.AssetsPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(contentRoot.AssetsPath),
        RequestPath = "/media",
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = "public, max-age=604800, must-revalidate",
    });
}
else
{
    app.Logger.LogWarning(
        "No assets directory at {Path}; /media will return 404 until one exists.",
        contentRoot.AssetsPath);
}

app.UseRouting();

// wwwroot, with build-time fingerprinting and immutable cache headers.
app.MapStaticAssets();

// Answers conditional GETs with a 304 before any component renders.
app.UseMiddleware<ContentValidatorMiddleware>();

app.UseAntiforgery();

app.MapHealthChecks("/healthz");
app.MapRazorComponents<App>();

app.Run();

/// <summary>Exposed so the integration tests can drive the real application.</summary>
public partial class Program;
