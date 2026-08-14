using System.Globalization;
using System.Text;

namespace PersonalPage.Web.Content;

/// <summary>
/// Filename to URL slug. The rules are fixed and documented in <c>docs/content-authoring.md</c>
/// so an author can predict the URL a file will get without running the site.
/// </summary>
public static class SlugBuilder
{
    /// <summary>
    /// Derives a slug from a filename: drop the extension, drop a leading <c>yyyy-MM-dd-</c>
    /// date prefix, lowercase, strip diacritics, and reduce everything that is not
    /// <c>[a-z0-9]</c> to single hyphens.
    /// </summary>
    /// <remarks>
    /// Non-ASCII is transliterated where a diacritic can simply be dropped (<c>é</c> becomes
    /// <c>e</c>) and discarded otherwise. That is a deliberate choice over percent-encoding,
    /// which produces URLs nobody can type. A filename that reduces to nothing falls back to
    /// its date prefix, and then to "untitled", so a slug is never empty.
    /// </remarks>
    public static string FromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
        var datePrefix = TryTakeDatePrefix(name, out var remainder);

        var slug = Normalize(remainder);
        if (slug.Length > 0)
        {
            return slug;
        }

        // "2026-01-15.md" — the date prefix was the whole filename.
        return datePrefix is not null ? Normalize(datePrefix) : "untitled";
    }

    /// <summary>
    /// Reads a leading <c>yyyy-MM-dd</c> date from a filename, used as the default
    /// <c>date</c> for blog posts that do not declare one.
    /// </summary>
    public static DateOnly? DateFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
        if (TryTakeDatePrefix(name, out _) is not { } prefix)
        {
            return null;
        }

        return DateOnly.TryParseExact(prefix, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
    }

    /// <summary>Turns a slug back into a readable title, for documents with no <c>title</c>.</summary>
    public static string ToDisplayTitle(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return "Untitled";
        }

        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0
            ? "Untitled"
            : string.Join(' ', words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    /// <summary>Lowercases, folds diacritics and reduces the rest to <c>[a-z0-9-]</c>.</summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var ch in value)
        {
            var folded = Fold(char.ToLowerInvariant(ch));

            if (folded.Length == 0)
            {
                pendingSeparator = true;
                continue;
            }

            if (pendingSeparator && builder.Length > 0)
            {
                builder.Append('-');
            }

            pendingSeparator = false;
            builder.Append(folded);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Maps one lowercase character to its ASCII equivalent, or to nothing when there is none.
    /// </summary>
    /// <remarks>
    /// This is an explicit table rather than Unicode decomposition on purpose. The application
    /// builds with <c>InvariantGlobalization=true</c>, which drops ICU — and without ICU,
    /// <see cref="string.Normalize(NormalizationForm)"/> is a no-op, so decomposition-based
    /// folding silently degrades to deleting every accented letter. A name like "Rīga" would
    /// slug as "rga", which is worse than either alternative. The table covers Latin-1
    /// Supplement and Latin Extended-A, which is every accented Latin letter a filename is
    /// likely to hold.
    /// </remarks>
    private static string Fold(char ch) => ch switch
    {
        >= 'a' and <= 'z' or >= '0' and <= '9' => ch.ToString(),

        'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' or 'ā' or 'ă' or 'ą' => "a",
        'ç' or 'ć' or 'ĉ' or 'ċ' or 'č' => "c",
        'ď' or 'đ' => "d",
        'è' or 'é' or 'ê' or 'ë' or 'ē' or 'ĕ' or 'ė' or 'ę' or 'ě' => "e",
        'ĝ' or 'ğ' or 'ġ' or 'ģ' => "g",
        'ĥ' or 'ħ' => "h",
        'ì' or 'í' or 'î' or 'ï' or 'ĩ' or 'ī' or 'ĭ' or 'į' or 'ı' => "i",
        'ĵ' => "j",
        'ķ' or 'ĸ' => "k",
        'ĺ' or 'ļ' or 'ľ' or 'ŀ' or 'ł' => "l",
        'ñ' or 'ń' or 'ņ' or 'ň' or 'ŉ' or 'ŋ' => "n",
        'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' or 'ō' or 'ŏ' or 'ő' => "o",
        'ŕ' or 'ŗ' or 'ř' => "r",
        'ś' or 'ŝ' or 'ş' or 'š' or 'ſ' => "s",
        'ţ' or 'ť' or 'ŧ' => "t",
        'ù' or 'ú' or 'û' or 'ü' or 'ũ' or 'ū' or 'ŭ' or 'ů' or 'ű' or 'ų' => "u",
        'ŵ' => "w",
        'ý' or 'ÿ' or 'ŷ' => "y",
        'ź' or 'ż' or 'ž' => "z",

        'æ' => "ae",
        'œ' => "oe",
        'ß' => "ss",
        'ð' => "d",
        'þ' => "th",

        _ => string.Empty,
    };

    /// <summary>
    /// Splits a leading <c>yyyy-MM-dd-</c> prefix off a filename stem.
    /// Returns the prefix, or null when there is none.
    /// </summary>
    private static string? TryTakeDatePrefix(string name, out string remainder)
    {
        remainder = name;

        // yyyy-MM-dd is exactly ten characters.
        if (name.Length < 10)
        {
            return null;
        }

        for (var i = 0; i < 10; i++)
        {
            var expectHyphen = i is 4 or 7;
            var ch = name[i];
            if (expectHyphen ? ch != '-' : !char.IsAsciiDigit(ch))
            {
                return null;
            }
        }

        var prefix = name[..10];
        remainder = name.Length > 10 && name[10] == '-' ? name[11..] : name[10..];
        return prefix;
    }
}
