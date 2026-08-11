using PersonalPage.Web.Content;

namespace PersonalPage.Web.Tests;

public class SlugBuilderTests
{
    [Theory]
    [InlineData("2026-01-15-my-post.md", "my-post")]
    [InlineData("my-post.md", "my-post")]
    [InlineData("MY-POST.md", "my-post")]
    [InlineData("My Post.md", "my-post")]
    [InlineData("my_post.md", "my-post")]
    [InlineData("2026-01-15-Hello World.md", "hello-world")]
    public void Derives_the_documented_slug(string fileName, string expected) =>
        Assert.Equal(expected, SlugBuilder.FromFileName(fileName));

    [Fact]
    public void A_date_prefix_and_nothing_else_does_not_produce_an_empty_slug()
    {
        Assert.Equal("2026-01-15", SlugBuilder.FromFileName("2026-01-15.md"));
    }

    [Fact]
    public void A_filename_that_reduces_to_nothing_falls_back_rather_than_being_empty()
    {
        Assert.Equal("untitled", SlugBuilder.FromFileName("!!!.md"));
    }

    [Fact]
    public void Diacritics_are_folded_to_their_base_letters()
    {
        // The chosen policy, pinned explicitly: transliterate what can be transliterated and
        // drop the rest, rather than percent-encoding into a URL nobody can type.
        Assert.Equal("cafe-latte", SlugBuilder.FromFileName("Café Latté.md"));
    }

    [Fact]
    public void Characters_with_no_ascii_equivalent_are_dropped()
    {
        Assert.Equal("post", SlugBuilder.FromFileName("日本語-post.md"));
    }

    [Fact]
    public void A_number_only_date_prefix_is_recognised_but_a_lookalike_is_not()
    {
        Assert.Equal("example-company", SlugBuilder.FromFileName("2026-01-15-example-company.md"));

        // "2021-example-company" is not a date, so nothing is stripped.
        Assert.Equal("2021-example-company", SlugBuilder.FromFileName("2021-example-company.md"));
    }

    [Theory]
    [InlineData("2026-01-15-post.md", "2026-01-15")]
    [InlineData("post.md", null)]
    [InlineData("2026-13-45-post.md", null)]
    public void Reads_the_date_from_a_dated_filename(string fileName, string? expected)
    {
        var date = SlugBuilder.DateFromFileName(fileName);

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), date);
    }

    [Theory]
    [InlineData("my-post", "My Post")]
    [InlineData("about", "About")]
    [InlineData("", "Untitled")]
    public void Turns_a_slug_back_into_a_readable_title(string slug, string expected) =>
        Assert.Equal(expected, SlugBuilder.ToDisplayTitle(slug));
}
