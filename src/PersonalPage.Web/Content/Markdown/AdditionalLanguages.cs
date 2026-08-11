using ColorCode;
using ColorCode.Common;

namespace PersonalPage.Web.Content.Markdown;

/// <summary>
/// Extra ColorCode language definitions.
/// </summary>
/// <remarks>
/// ColorCode ships C#, F#, JS, TypeScript, Python, SQL, JSON, XML, HTML, CSS, PowerShell and a
/// handful more, but no shell or YAML — the two fence languages a developer site uses most after
/// its own stack. A ColorCode language is just an ordered list of regex rules, so defining them
/// here is cheaper than taking on a second highlighting dependency. Anything still unrecognised
/// renders as plain, unhighlighted code.
/// </remarks>
internal static class AdditionalLanguages
{
    public static IEnumerable<ILanguage> All => [Shell, Yaml];

    private static ILanguage Shell => new SimpleLanguage(
        id: "shell",
        name: "Shell",
        cssClassName: "shell",
        aliases: ["bash", "sh", "zsh", "console", "shell-session"],
        rules:
        [
            // Comments first: everything after them on the line is not code.
            Rule(@"(?m)(?<=^|\s)(#(?!\!).*)$", (1, ScopeName.Comment)),
            Rule(@"(?m)^(#\!.*)$", (1, ScopeName.PreprocessorKeyword)),
            Rule(@"(""[^""\\]*(?:\\.[^""\\]*)*"")|('[^']*')",
                (1, ScopeName.String), (2, ScopeName.String)),
            Rule(@"\b(if|then|elif|else|fi|for|while|until|do|done|case|esac|in|function|return|exit|break|continue|local|export|declare|readonly|source|set|unset|trap)\b",
                (1, ScopeName.Keyword)),
            Rule(@"(\$\{[^}]*\}|\$[A-Za-z_][A-Za-z0-9_]*|\$[0-9@?*#$!])", (1, ScopeName.Predefined)),
            // The first word of a line or of a pipeline segment is the command being run.
            Rule(@"(?m)(?<=^|\||;|&&|\|\||\$\()\s*([A-Za-z_][\w.\-]*)", (1, ScopeName.BuiltinFunction)),
            Rule(@"(\s)(--?[A-Za-z][\w\-]*)", (2, ScopeName.Attribute)),
            Rule(@"(\|\||&&|\||;|>>|>|<<|<|&)", (1, ScopeName.Operator)),
            Rule(@"\b(\d+)\b", (1, ScopeName.Number)),
        ]);

    private static ILanguage Yaml => new SimpleLanguage(
        id: "yaml",
        name: "YAML",
        cssClassName: "yaml",
        aliases: ["yml"],
        rules:
        [
            Rule(@"(?m)(?<=^|\s)(#.*)$", (1, ScopeName.Comment)),
            Rule(@"(?m)^(---|\.\.\.)$", (1, ScopeName.Delimiter)),
            Rule(@"(?m)^\s*(?:-\s+)?([A-Za-z_][\w.\-]*)(\s*:)(?=\s|$)",
                (1, ScopeName.JsonKey), (2, ScopeName.Operator)),
            Rule(@"(""[^""\\]*(?:\\.[^""\\]*)*"")|('[^']*')",
                (1, ScopeName.String), (2, ScopeName.String)),
            Rule(@"(?<=[:\-]\s)(true|false|yes|no|on|off|null|~)(?=\s*$|\s)",
                (1, ScopeName.BuiltinValue)),
            Rule(@"(?<=[:\-]\s)(-?\d+(?:\.\d+)?)(?=\s*$|\s)", (1, ScopeName.Number)),
            Rule(@"(?m)^\s*(-)(?=\s)", (1, ScopeName.Operator)),
            Rule(@"([&*][A-Za-z_][\w\-]*|<<)", (1, ScopeName.Attribute)),
        ]);

    private static LanguageRule Rule(string regex, params (int Group, string Scope)[] captures) =>
        new(regex, captures.ToDictionary(c => c.Group, c => c.Scope));

    /// <summary>A ColorCode language defined purely by an ordered list of regex rules.</summary>
    private sealed class SimpleLanguage(
        string id,
        string name,
        string cssClassName,
        string[] aliases,
        IList<LanguageRule> rules) : ILanguage
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string CssClassName { get; } = cssClassName;
        public string FirstLinePattern => string.Empty;
        public IList<LanguageRule> Rules { get; } = rules;

        public bool HasAlias(string lang) =>
            aliases.Contains(lang, StringComparer.OrdinalIgnoreCase);
    }
}
