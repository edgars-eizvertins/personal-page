---
title: An example post
date: 2026-01-15
summary: What this file demonstrates, and every markdown feature the site renders.
tags: [markdown, example]
---

**This is example content.** Copy this file, rename it with today's date, and write something.

The filename carries the date, so `date` in the front matter above is optional — remove it and
the site reads `2026-01-15` from the filename instead. The slug drops the date prefix, so this
post lives at `/blog/example-post`.

## Headings start at level two

The page title comes from `title` in the front matter and is rendered as the one `<h1>` on the
page. Anything you write in the body is pushed down a level, so a stray `#` becomes an `<h2>`
rather than a second `<h1>`.

### A third-level heading

#### And a fourth

## Text

Ordinary paragraphs, with *emphasis*, **strong emphasis**, `inline code`, and
[a link to another page](/about). External links work the same way, and bare URLs like
https://example.com are turned into links automatically.

> A blockquote, for when someone else said it better.

## Lists

- An unordered list
- With a second item
  - and something nested underneath it

1. An ordered list
2. Which counts
3. As you would hope

## Code

Fenced code is highlighted on the server, so it is already coloured before any script runs — and
it stays coloured with JavaScript disabled entirely.

```csharp
public sealed record ContentDocument(string Slug, string Html)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Html);
}
```

```bash
# Shell fences are highlighted too.
docker compose up -d && docker compose logs -f personal-page
```

```yaml
# So is YAML, which is what front matter is written in.
title: An example post
tags: [markdown, example]
```

A fence with no language, or with one the highlighter does not know, renders as plain code:

```
$ this is not highlighted, and that is fine
```

## Tables

| Front matter key | Applies to | Meaning |
| --- | --- | --- |
| `title` | everything | The page heading |
| `draft` | everything | Hides the document entirely |
| `nav_order` | pages | Puts the page in the navigation |
| `tags` | posts, projects | Rendered as pills |

## Images

Images gain `loading="lazy"` and `decoding="async"` automatically. Drop a file into
`content/assets/` and reference it under `/media/`:

    ![A description of the image](/media/example.png)

That line is shown as literal text rather than an actual image, because `content.example` ships
no image files.

---

That is everything the renderer does. Delete this file once you have written a real one.
