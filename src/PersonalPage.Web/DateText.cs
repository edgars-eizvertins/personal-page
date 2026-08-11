using System.Globalization;

namespace PersonalPage.Web;

/// <summary>
/// Date formatting for display. Invariant culture throughout, matching
/// <c>InvariantGlobalization=true</c> — the site is English-only, so a machine-dependent format
/// would only mean the same page rendering differently on two hosts.
/// </summary>
public static class DateText
{
    /// <summary>"15 January 2026".</summary>
    public static string Long(DateOnly date) =>
        date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>"January 2026", for date ranges where the day is noise.</summary>
    public static string MonthYear(DateOnly date) =>
        date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// "March 2020 – June 2023", or "March 2020 – Present" when the role is ongoing.
    /// </summary>
    public static string Range(DateOnly? start, DateOnly? end)
    {
        var from = start is { } s ? MonthYear(s) : null;
        var to = end is { } e ? MonthYear(e) : "Present";

        return from is null ? to : $"{from} – {to}";
    }

    /// <summary>Machine-readable form for a <c>&lt;time datetime&gt;</c> attribute.</summary>
    public static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
