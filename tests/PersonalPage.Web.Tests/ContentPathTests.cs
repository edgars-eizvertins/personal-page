using PersonalPage.Web.Content;

namespace PersonalPage.Web.Tests;

public class ContentPathTests
{
    [Theory]
    [InlineData("/About", "about")]
    [InlineData("about/", "about")]
    [InlineData("//about", "about")]
    [InlineData("/about", "about")]
    [InlineData("%2Fabout", "about")]
    [InlineData("/notes/Setup", "notes/setup")]
    public void Normalizes_case_and_slashes(string input, string expected) =>
        Assert.Equal(expected, ContentPath.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("   ")]
    public void An_empty_path_resolves_to_home(string? input) =>
        Assert.Equal(ContentPath.HomeSlug, ContentPath.Normalize(input));

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("/blog/../../etc/passwd")]
    [InlineData("%2e%2e%2fetc%2fpasswd")]
    [InlineData("%252e%252e%252fetc")]
    [InlineData("..%2f..%2fetc")]
    [InlineData("about\0")]
    [InlineData("C:/windows")]
    [InlineData("..\\..\\windows")]
    public void Rejects_anything_that_tries_to_leave_the_root(string input) =>
        Assert.Null(ContentPath.Normalize(input));

    [Theory]
    [InlineData("my-post", true)]
    [InlineData("post_2", true)]
    [InlineData("2026-01-15", true)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("a.b", false)]
    [InlineData("../x", false)]
    public void Only_simple_filename_components_are_valid_slugs(string slug, bool expected) =>
        Assert.Equal(expected, ContentPath.IsValidSlug(slug));
}
