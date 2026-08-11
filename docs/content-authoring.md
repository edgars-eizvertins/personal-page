# Authoring content

Everything on the site is a plain markdown file under `content/`. No tooling is assumed: a text
editor and a file manager are the whole toolchain. Save a file and the next request to the site
reflects it — no restart, no rebuild, no redeploy.

## The shape of `content/`

```
content/
  site.yml                       title, tagline, footer, social links, nav
  pages/                         free-form pages   ->  /{slug}
    home.md  about.md  404.md  ...
  experience/                    timeline entries  ->  /experience
  projects/                      cards and detail  ->  /projects, /projects/{slug}
  blog/                          posts             ->  /blog, /blog/{slug}
  assets/                        images and files  ->  /media/*
```

Filenames are lowercase by convention. The site folds request paths to lowercase before looking
them up, so `/About` finds `about.md`, but a file named `About.md` on a case-sensitive
filesystem will not be found at all.

## Front matter

Front matter is YAML between two `---` fences, and the opening fence must be the very first line
of the file:

```markdown
---
title: About
nav_order: 1
---

The body starts here.
```

Rules that apply everywhere:

- **Unknown keys are ignored.** Adding a key the site does not read is harmless.
- **Missing keys fall back.** No `title` means the title is derived from the filename.
- **A broken fence is not fatal.** If the YAML fails to parse, the body still renders, the title
  falls back to the filename, and the file is listed on `/_diagnostics` with the error.
- **`draft: true` hides a document completely** — absent from listings *and* 404 at its own URL.
- A `---` further down the body is a horizontal rule, not a second fence.
- CRLF line endings and a UTF-8 BOM both parse fine.

### Keys by content type

| Key | Type | Applies to | Meaning |
| --- | --- | --- | --- |
| `title` | text | all | The page's `<h1>` |
| `description` | text | all | `<meta name="description">` |
| `draft` | boolean | all | Hides the document entirely |
| `nav_title` | text | pages | Shorter label for the nav |
| `nav_order` | number | pages | Puts the page in the nav, sorted ascending |
| `company` | text | experience | Employer name |
| `role` | text | experience | Job title |
| `start` | date | experience | Start of the role |
| `end` | date | experience | End of the role. Omit for "Present" |
| `location` | text | experience | Where the role was based |
| `tech` | list | experience | Technology pills |
| `summary` | text | projects, blog | One line, shown in listings |
| `date` | date | projects, blog | Sort key, newest first |
| `tags` | list | projects, blog | Tag pills |
| `repo` | url | projects | Source link |
| `url` | url | projects | Live link |
| `image` | url | projects | Thumbnail, e.g. `/media/thing.png` |
| `featured` | boolean | projects | Surfaces the project on the home page |

### Values

**Dates** are written unquoted, in one of three precisions:

```yaml
date: 2026-01-15    # a day
start: 2020-03      # a month, treated as the first of it
end: 2023           # a year, treated as 1 January
```

They are always read as year-month-day regardless of the machine's locale.

**Booleans** — `true`, `True` and `yes` all mean true; `false`, `False` and `no` all mean false.

**Lists** work in either form:

```yaml
tags: [dotnet, docker]

tags:
  - dotnet
  - docker
```

**Text containing a colon must be quoted.** `title: Blazor: a retrospective` is a YAML mapping,
not a string, and will not parse. Write:

```yaml
title: "Blazor: a retrospective"
```

If you forget, nothing breaks — the body still renders and `/_diagnostics` tells you why the
title looks wrong.

## URLs and filenames

The URL slug is the filename, minus its extension, minus a leading `yyyy-MM-dd-` date prefix,
lowercased, with everything that is not `a-z0-9` reduced to single hyphens.

| File | URL |
| --- | --- |
| `pages/about.md` | `/about` |
| `pages/home.md` | `/` |
| `pages/notes/setup.md` | `/notes/setup` |
| `blog/2026-01-15-hello-world.md` | `/blog/hello-world` |
| `blog/hello-world.md` | `/blog/hello-world` |
| `projects/My Thing.md` | `/projects/my-thing` |

Accented Latin letters fold to their base letter (`Rīga` becomes `riga`); anything with no ASCII
equivalent is dropped. If two files would produce the same slug, the one whose filename sorts
first wins and the other is reported on `/_diagnostics` — rename one of them.

A dated filename supplies the `date` for a post, so `blog/2026-01-15-hello.md` needs no `date`
key at all.

## Writing the body

### Headings

The `title` from front matter is rendered as the page's one and only `<h1>`, outside the body.
**Start body headings at `##`.**

If a body does contain a `#`, every heading in that file is pushed down one level so the page
still has exactly one `<h1>`. A file that already starts at `##` is left exactly as written.

### Links

Use site-absolute paths for internal links:

```markdown
Read [about me](/about), or [the first post](/blog/hello-world).
```

`/_diagnostics` checks every internal link and tells you which ones resolve to nothing.

### Images

Put the file in `content/assets/` and reference it under `/media/`:

```markdown
![A description of the picture](/media/diagram.png)
```

Subfolders work: `assets/posts/2026/chart.svg` is served at `/media/posts/2026/chart.svg`.

Every image automatically gets `loading="lazy"` and `decoding="async"`. If you know the
dimensions, adding them prevents the page shifting while the image loads:

```markdown
![A description](/media/diagram.png){width=800 height=450}
```

### Code

Fenced code is highlighted **on the server**, so it is coloured before any script runs and stays
coloured with JavaScript switched off.

````markdown
```csharp
var greeting = "hello";
```
````

Recognised languages: `csharp`, `fsharp`, `cpp`, `java`, `javascript`, `typescript`, `python`,
`php`, `powershell`, `sql`, `json`, `xml`, `html`, `css`, `markdown`, `haskell`, `fortran`,
`matlab`, `vbdotnet`, plus `bash`/`sh`/`shell` and `yaml`/`yml`.

Anything else — a language nobody has taught the highlighter, or no language at all — renders as
plain, readable code rather than failing.

### Everything else

Tables, task lists, footnotes, definition lists, strikethrough, autolinked URLs and the rest of
Markdig's advanced extensions are all on. Raw HTML in the body is passed straight through, which
is what makes an embedded `<figure>` or `<details>` possible.

That last point is worth stating plainly: **`content/` is trusted input.** Whatever is in these
files runs in visitors' browsers. That is the right trade for a site you are the only author of;
it would be the wrong one if anybody else could write here.

## `site.yml`

```yaml
title: Your Name
tagline: Backend developer, somewhere on the internet
description: Personal site and notes of a software developer.
author: Your Name

footer: © Your Name. Built with markdown files and no build step.

links:
  - label: GitHub
    url: https://github.com/your-handle
  - label: Email
    url: mailto:you@example.com

# Pages carry their own nav_order. These entries cover the collection views, which are routes
# rather than files. Both lists sort together by order.
nav:
  - title: Experience
    url: /experience
    order: 2
  - title: Projects
    url: /projects
    order: 5
  - title: Writing
    url: /blog
    order: 6
```

Editing this file is live like everything else. If it is missing or unparseable the site falls
back to built-in defaults rather than failing to start.

## Templates to copy

### A page

```markdown
---
title: Uses
description: Hardware, editor and tools.
nav_order: 8
---

## Machine

...
```

Drop `nav_order` and the page still works — it just does not appear in the nav.

### An experience entry

`content/experience/2021-example-company.md`:

```markdown
---
company: Example Company
role: Senior Backend Developer
start: 2021-03
location: Remote
tech: [C#, .NET, PostgreSQL]
---

What you owned, and the one change you are proudest of.

- A specific outcome
- Another one
```

Omitting `end` renders "Present" and sorts the entry above every finished role.

### A project

`content/projects/example-project.md`:

```markdown
---
title: Example Project
summary: A small service that does one thing and keeps doing it.
date: 2025-11-02
tags: [C#, Docker]
repo: https://github.com/your-handle/example-project
url: https://example.com
image: /media/example-project.png
featured: true
---

## What it does

...
```

### A blog post

`content/blog/2026-01-15-hello-world.md`:

```markdown
---
title: Hello world
date: 2026-01-15
summary: One sentence, shown on the blog index.
tags: [dotnet]
---

## The first section

...
```

## Checking your work

Open `/_diagnostics`. It lists, with no styling budget and no nav entry:

- documents whose front matter failed to parse, with the error
- internal links that resolve to no page, item, or asset
- `/media/` references pointing at files that do not exist
- files in `assets/` that nothing references
- drafts, and anything dated in the future

A build would have caught all of these. Content never passes through a build, so this page is
the substitute. It is the page to open every week.

## Two things that are not instant

Almost everything is live on the next request. Two exceptions, both deliberate and both
explained in [architecture.md](architecture.md):

- A **newly created** file can take up to five seconds to appear in `/blog`, `/projects` or
  `/experience` if the filesystem watcher does not fire. Edits to files that already exist are
  always immediate.
- If your editor saves a file **without changing its modification time or its length**, the
  cached copy is kept. Touching the file fixes it.
