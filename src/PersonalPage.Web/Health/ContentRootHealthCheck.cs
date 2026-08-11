using Microsoft.Extensions.Diagnostics.HealthChecks;
using PersonalPage.Web.Content;

namespace PersonalPage.Web.Health;

/// <summary>
/// Asserts that the content root is actually mounted and has pages in it.
/// </summary>
/// <remarks>
/// A health check that only proves the process is listening reports healthy when the bind mount
/// has failed and the site is serving nothing but 404s — precisely the failure Docker most needs
/// to catch. It is also what catches a forker who skipped <c>cp -r content.example content</c>.
/// </remarks>
public sealed class ContentRootHealthCheck(ContentRoot root, IContentFileSystem fileSystem) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!fileSystem.DirectoryExists(root.FullPath))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Content root '{root.FullPath}' does not exist. Is the volume mounted?"));
        }

        if (!fileSystem.DirectoryExists(root.PagesPath))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Content root '{root.FullPath}' has no pages/ directory."));
        }

        if (!fileSystem.EnumerateFiles(root.PagesPath, "*.md").Any())
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"'{root.PagesPath}' contains no markdown files."));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Serving content from '{root.FullPath}'."));
    }
}
