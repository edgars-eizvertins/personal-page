namespace PersonalPage.Web.Content;

/// <summary>
/// Front matter for every content type. One shape covers all of them: unknown keys are ignored
/// on the way in, and a key that is meaningless for a given type is simply never read. This is
/// what lets an author add a key without a code change.
/// </summary>
public sealed class FrontMatter
{
    // Common
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool Draft { get; set; }

    // Pages
    public string? NavTitle { get; set; }
    public int? NavOrder { get; set; }

    // Experience
    public string? Company { get; set; }
    public string? Role { get; set; }
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public string? Location { get; set; }
    public List<string> Tech { get; set; } = [];

    // Projects and blog
    public string? Summary { get; set; }
    public DateOnly? Date { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Repo { get; set; }
    public string? Url { get; set; }
    public string? Image { get; set; }
    public bool Featured { get; set; }

    /// <summary>Front matter as it is when a file declares none, or when its YAML failed to parse.</summary>
    public static FrontMatter Empty() => new();
}
