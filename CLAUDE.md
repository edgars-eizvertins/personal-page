# CLAUDE.md

Guidance for AI sessions working in this repo.

## What this is

A markdown-driven personal developer portfolio site. Server-rendered ASP.NET Core (Blazor SSR);
all content is authored as plain markdown files.

The project is meant to be reusable: anyone can fork it, drop their own markdown into
`content/`, and have their own portfolio without touching C#. Keep the engine generic — no
hardcoded names, job history, hostnames, or personal details anywhere in `src/` or `docs/`.
Personal information belongs in `content/` only.

**Current state: implemented.** The engine, tests, placeholder content, Docker setup and docs are
all in place; what is deliberately absent is the visual design (`tokens.css` is a plain neutral
placeholder — see [docs/design.md](docs/design.md)) and any real CV content. The build plan it
was implemented from is [docs/plan.md](docs/plan.md), kept for the reasoning behind the design
decisions; [docs/architecture.md](docs/architecture.md) describes what actually exists.

## The one rule that shapes everything

**Content changes must never require code changes, a rebuild, or a redeploy.**

The site owner edits markdown in a plain text editor — locally or over a network mount — and the
site reflects it immediately. Adding a page, a project, a blog post, a nav entry, or an image is
a file operation, never a C# edit. Before adding any feature, check that it does not violate
this. If a proposed feature would force a rebuild to change content, redesign it.

Three mechanisms enforce the rule:

1. A catch-all Blazor route (`@page "/{*path}"`) maps `content/pages/foo.md` to `/foo`.
2. `content/` is a bind-mounted Docker volume, never baked into the image.
3. Parsed documents are cached but revalidated against file `LastWriteTimeUtc` on every read.

## Repo map

| Path | Purpose |
| --- | --- |
| `content/` | **The editable surface.** Markdown, `site.yml`, images. Gitignored, not in the Docker image. |
| `content.example/` | Tracked placeholder content. `cp -r content.example content` to start. |
| `src/PersonalPage.Web/Content/` | The markdown pipeline: parsing, rendering, caching. |
| `src/PersonalPage.Web/Components/` | Blazor SSR layout, pages, shared components. |
| `src/PersonalPage.Web/wwwroot/css/` | `tokens.css` (variables only), `site.css` (layout). |
| `tests/PersonalPage.Web.Tests/` | xunit tests for the content pipeline. |
| `docs/` | Plan, architecture, authoring guide, deployment, design brief. |

`content/` vs. `src/` is the important boundary. Content is the site owner's data; `src/` is the
engine that renders it. A task that sounds like "add my new job" or "write a post about X" is a
`content/` change only.

## Architecture

- **.NET 10**, Blazor Web App with `--interactivity None` — pure static server rendering. No
  SignalR circuit, no WebAssembly. This keeps memory low on small hardware such as a Raspberry
  Pi. **Do not enable interactive render modes.** Client-side JavaScript is allowed (see
  Styling); Blazor Server/WASM interactivity is not — the first is free, the second costs a
  circuit per visitor or a multi-megabyte payload.
- **Markdig** renders markdown, **YamlDotNet** parses `---` front matter and `site.yml`.
- `MarkdownContentStore` (singleton, implements `IContentStore`) is the single entry point for
  reading content. Nothing else should touch the filesystem for content.
- Routing precedence: literal routes (`/blog`, `/projects/{slug}`) out-rank the catch-all, so
  collection pages win and everything else falls through to `content/pages/`.
- `content/assets/` is served at `/media/*` via a second static file provider.

### Four non-obvious things, all load-bearing

Each of these looks like it could be simplified and cannot. Full reasoning in
`docs/architecture.md`.

1. **`Program.cs` calls `app.UseRouting()` explicitly.** `ContentPage`'s `/{*path}` catch-all
   matches every URL, and `StaticFileMiddleware` skips a request that already matched an
   endpoint. Without the explicit call, `/media` falls through to a 404.
2. **404s go through `NavigationManager.NotFound()` and the Router's `NotFoundPage`.** Blazor
   static SSR discards the output of a component that sets a 404 itself, so the component that
   discovers the miss cannot also render the 404 body.
3. **Heading demotion is conditional.** Only a body containing an `h1` is shifted. Demoting
   unconditionally would leave `<h2>` permanently unused and push correctly authored bodies out
   of the designed type scale.
4. **`SlugBuilder` folds diacritics from an explicit table, not Unicode normalization.**
   `InvariantGlobalization=true` drops ICU, which makes `string.Normalize` a no-op — so
   decomposition-based folding would silently delete every accented letter.

## Content conventions

Front matter is YAML between `---` fences. Unknown keys are ignored. Missing keys fall back to
defaults — title from filename, slug from filename minus any date prefix.

| Type | Location | Keys |
| --- | --- | --- |
| page | `content/pages/*.md` | `title`, `description`, `nav_title`, `nav_order`, `draft` |
| experience | `content/experience/*.md` | `company`, `role`, `start`, `end`, `location`, `tech[]` |
| project | `content/projects/*.md` | `title`, `summary`, `date`, `tags[]`, `repo`, `url`, `image`, `featured`, `draft` |
| blog | `content/blog/*.md` | `title`, `date`, `summary`, `tags[]`, `draft` |

- `draft: true` hides a document from the site — absent from listings *and* 404 at its own URL.
- Collections sort by `date` / `start`, newest first, with ties broken on slug so ordering is
  stable across restarts.
- A page appears in the nav only if it declares `nav_order`. The collection views are routes
  rather than files, so their nav entries come from the `nav:` list in `site.yml`; the two lists
  sort together.
- Omitting `end` on an experience entry renders as "Present" and sorts above finished roles.

Full authoring reference: `docs/content-authoring.md`.

## Styling and client-side script

`tokens.css` contains **only** CSS custom properties — colour, type, spacing, shape, `--measure`,
and the syntax-highlighting palette. `site.css` contains layout and must consume only those
tokens, never hardcoded colours or sizes.

Each theme is declared twice: under `@media (prefers-color-scheme: dark)` for the OS setting, and
under `:root[data-theme="dark"]` / `:root[data-theme="light"]` so the header toggle can override
it. The attribute selectors must win over the media query in both directions.

The visual design is produced separately using [docs/design-brief.md](docs/design-brief.md).
Keeping the token/layout split intact is what makes a design droppable as a file swap. Do not
scatter literal colour values into components.

**JavaScript rules:**

- Allowed as *enhancement only* — every page must render, read, and navigate with scripting off.
- Vanilla JS in `wwwroot`, no framework. Any library is vendored and committed, never a CDN.
- No external hosts of any kind — no fonts, no analytics, no third-party widgets. The site must
  work on a network with no route to the internet.
- One inline script is permitted: the pre-paint theme setter in `<head>`, which needs a CSP hash
  or nonce. Everything else is a deferred file.
- Syntax highlighting happens **server-side**, in the cached HTML — not in the browser.

## Working preferences

- Write documentation before code when starting a new area of work.
- Do not commit or push unless explicitly asked in that session.
- **Commit messages must not credit Claude, Anthropic, or any AI assistant.** No
  `Co-Authored-By: Claude` trailer, no "Generated with Claude Code" line, no "🤖" marker, no
  mention in the body. The repository owner is the sole author of every commit. The same applies
  to pull request descriptions and to any generated file headers.

## Deployment

Docker, targeting a small ARM64 host (Raspberry Pi class) on a private LAN — no TLS or public
exposure for now. Cross-build for arm64 with buildx, or build on the target host. Deployment
docs use placeholder hostnames (`<deploy-host>`); real hostnames and IPs stay out of the repo
and live in a local, untracked env file. Details in `docs/deployment.md`.

Updating content on the host does **not** involve Docker at all — edit the files in the mounted
`content/` directory and they are live.

## Out of scope

Real CV content (arrives later as markdown, owner-supplied), public internet exposure, TLS, RSS,
analytics, search.
