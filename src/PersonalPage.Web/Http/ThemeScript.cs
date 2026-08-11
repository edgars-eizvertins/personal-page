using System.Security.Cryptography;
using System.Text;

namespace PersonalPage.Web.Http;

/// <summary>
/// The one inline script the site is allowed.
/// </summary>
/// <remarks>
/// It has to read <c>localStorage</c> and set <c>data-theme</c> <em>before first paint</em>, or a
/// visitor whose choice differs from their OS setting sees a flash of the wrong theme on every
/// navigation — a server-rendered site has no client-side router to hide it behind. Because it
/// is inline it needs a CSP hash, which is computed here from the same string that gets rendered
/// so the two can never drift apart.
/// </remarks>
public static class ThemeScript
{
    /// <summary>Rendered verbatim inside <c>&lt;script&gt;</c> in the document head.</summary>
    public const string Source =
        "(function(){try{var t=localStorage.getItem('theme');" +
        "if(t==='dark'||t==='light'){document.documentElement.setAttribute('data-theme',t);}}" +
        "catch(e){}})();";

    /// <summary>The <c>'sha256-…'</c> source expression for <c>script-src</c>.</summary>
    public static string CspHash { get; } =
        "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Source))) + "'";
}
