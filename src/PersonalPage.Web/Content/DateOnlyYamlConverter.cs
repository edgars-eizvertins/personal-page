using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace PersonalPage.Web.Content;

/// <summary>
/// Binds <c>date</c>, <c>start</c> and <c>end</c> to <see cref="DateOnly"/> using the invariant
/// culture.
/// </summary>
/// <remarks>
/// Left to its defaults YamlDotNet produces a <see cref="DateTime"/> carrying a timezone
/// interpretation nobody asked for, and collection sort order depends on these values. Three
/// precisions are accepted so an author can write only what they know:
/// <c>2026-01-15</c>, <c>2026-01</c> (first of the month) and <c>2026</c> (first of January).
/// </remarks>
public sealed class DateOnlyYamlConverter : IYamlTypeConverter
{
    private static readonly string[] FullFormats = ["yyyy-MM-dd", "yyyy-M-d"];

    public bool Accepts(Type type) => type == typeof(DateOnly) || type == typeof(DateOnly?);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        var text = scalar.Value.Trim();

        if (text.Length == 0 || text is "~" or "null")
        {
            return type == typeof(DateOnly?)
                ? null
                : throw new YamlException(scalar.Start, scalar.End, "Expected a date.");
        }

        if (TryParse(text, out var date))
        {
            return date;
        }

        throw new YamlException(scalar.Start, scalar.End,
            $"'{text}' is not a date. Use yyyy-MM-dd, yyyy-MM or yyyy.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var text = value is DateOnly date
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;
        emitter.Emit(new Scalar(text));
    }

    public static bool TryParse(string text, out DateOnly date)
    {
        // A full timestamp ("2026-01-15T09:00:00Z") is a reasonable thing for an author to
        // paste in; take the date part rather than rejecting it.
        var separator = text.IndexOfAny(['T', ' ']);
        var candidate = separator > 0 ? text[..separator] : text;

        foreach (var format in FullFormats)
        {
            if (DateOnly.TryParseExact(candidate, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        // Reduced precision: "2026-01" is the first of that month, "2026" the first of January.
        // DateOnly.TryParseExact cannot express a missing day component, so widen by hand.
        if (DateTime.TryParseExact(candidate, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month))
        {
            date = DateOnly.FromDateTime(month);
            return true;
        }

        if (candidate.Length == 4 && int.TryParse(candidate, NumberStyles.None,
                CultureInfo.InvariantCulture, out var year) && year is >= 1 and <= 9999)
        {
            date = new DateOnly(year, 1, 1);
            return true;
        }

        date = default;
        return false;
    }
}
