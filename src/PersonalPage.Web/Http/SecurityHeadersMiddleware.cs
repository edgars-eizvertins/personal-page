namespace PersonalPage.Web.Http;

/// <summary>
/// Adds a strict Content-Security-Policy and its usual companions.
/// </summary>
/// <remarks>
/// A tight policy is unusually easy here: every asset is same-origin and there are no
/// third-party scripts, fonts or trackers. It also acts as a safety net for the raw-HTML
/// passthrough the markdown pipeline keeps enabled — a stray <c>&lt;script src="https://…"&gt;</c>
/// pasted into a content file simply will not load. The only inline script is the pre-paint
/// theme setter, which is allowed by hash rather than by relaxing the policy with
/// <c>'unsafe-inline'</c>.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly string ContentSecurityPolicy = string.Join("; ",
        "default-src 'self'",
        $"script-src 'self' {ThemeScript.CspHash}",
        "style-src 'self'",
        "img-src 'self' data:",
        "font-src 'self'",
        "connect-src 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'");

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";

        return next(context);
    }
}
