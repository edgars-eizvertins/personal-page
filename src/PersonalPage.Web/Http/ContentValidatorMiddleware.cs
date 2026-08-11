using System.Globalization;
using Microsoft.Net.Http.Headers;
using PersonalPage.Web.Content;

namespace PersonalPage.Web.Http;

/// <summary>
/// Emits <c>ETag</c> and <c>Last-Modified</c> for content-backed pages and answers
/// <c>If-None-Match</c> / <c>If-Modified-Since</c> with a 304 before anything renders.
/// </summary>
/// <remarks>
/// The store already stats every file it reads, so the validator is nearly free, and a returning
/// visitor costs one stat instead of a full render. This is deliberately chosen over ASP.NET
/// Core's output caching: output caching would serve a stale copy for the length of its TTL,
/// trading away the instant-edit property the whole design exists to provide. Validators keep
/// edits instant <em>and</em> make repeat visits cheap.
/// </remarks>
public sealed class ContentValidatorMiddleware(RequestDelegate next, IContentStore store)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";

        // Diagnostics reflects the whole tree and the health check must never be cached.
        if (path.StartsWith("/_", StringComparison.Ordinal) || path == "/healthz")
        {
            await next(context);
            return;
        }

        ContentValidator? validator;
        try
        {
            validator = await store.GetValidatorAsync(path, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (validator is not { } value || value.IsEmpty)
        {
            await next(context);
            return;
        }

        var etag = value.ToETag();

        // Truncate to whole seconds: that is all the HTTP-date format can carry, and comparing
        // at higher precision would make every If-Modified-Since look stale.
        var lastModified = new DateTimeOffset(value.LastWriteTimeUtc, TimeSpan.Zero)
            .ToUniversalTime();
        lastModified = lastModified.AddTicks(-(lastModified.Ticks % TimeSpan.TicksPerSecond));

        var headers = context.Response.Headers;
        headers[HeaderNames.ETag] = etag;
        headers[HeaderNames.LastModified] = lastModified.ToString("R", CultureInfo.InvariantCulture);
        headers[HeaderNames.CacheControl] = "no-cache";
        headers[HeaderNames.Vary] = "Accept-Encoding";

        if (IsNotModified(context.Request, etag, lastModified))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentLength = null;
            return;
        }

        await next(context);
    }

    private static bool IsNotModified(HttpRequest request, string etag, DateTimeOffset lastModified)
    {
        var ifNoneMatch = request.Headers[HeaderNames.IfNoneMatch];
        if (ifNoneMatch.Count > 0)
        {
            // If-None-Match wins outright when present: RFC 9110 says to ignore
            // If-Modified-Since in that case.
            foreach (var candidate in ifNoneMatch)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                foreach (var tag in candidate.Split(','))
                {
                    var trimmed = tag.Trim();
                    if (trimmed == "*" || trimmed == etag
                        || (trimmed.StartsWith("W/", StringComparison.Ordinal) && trimmed[2..] == etag))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        var ifModifiedSince = request.Headers[HeaderNames.IfModifiedSince];
        return ifModifiedSince.Count > 0
               && DateTimeOffset.TryParseExact(ifModifiedSince[0], "R", CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal, out var since)
               && lastModified <= since;
    }
}
