# Implementation plan — markdown-driven developer portfolio

Status: **approved, not yet implemented.** No code exists in this repo yet.

## Context

This repo holds a reusable engine for a personal developer portfolio site. The defining
requirement: **all content lives in markdown files that can be edited in a plain text editor and
appear on the site without rebuilding or redeploying the app.**

The deliverable is the **site engine and template only**. No real CV or biography is part of the
build — content files ship as clearly-marked placeholder examples that demonstrate each content
type. Whoever runs the site replaces them with their own markdown; that is a `content/` change
with no code changes.

Nothing about a specific person, employer, or host belongs in `src/` or `docs/`. Keep the engine
forkable.

Decisions already made:

| Decision | Choice |
| --- | --- |
| Language | English only, no i18n layer |
| Stack | Blazor SSR (static server rendering, no interactivity), .NET 10 |
| Sections | Home, About, Experience, Skills, Education, Projects, Blog, Now, Uses, Contact |
| Hosting | Docker container on an ARM64 single-board host (Raspberry Pi class), private LAN |
| Design | Produced separately from [design-brief.md](design-brief.md) |

Minimum environment: .NET SDK 10.0.1xx, the `dotnet new blazor` template, and Docker with
buildx if you intend to cross-build for arm64.

## Core design principle

Adding or editing content must never require touching C# or rebuilding the image. Three
mechanisms enforce it:

1. **A catch-all route.** `content/pages/foo.md` is served at `/foo` with zero code.
2. **A bind-mounted content directory.** `./content` is mounted into the container, so edits on
   the host filesystem are live immediately.
3. **Stat-validated caching.** Parsed documents are cached but revalidated against the file's
   `LastWriteTimeUtc` on every read — always correct, no restart, no watcher races.

## Content model

`content/` is the editable surface. It is **not** baked into the Docker image.

```
content/
  site.yml                       # name, tagline, social links, footer text
  pages/                         # free-form pages -> /{slug}
    home.md  about.md  now.md  uses.md  contact.md  skills.md  education.md
  experience/                    # collection -> /experience (timeline)
    2020-example-company.md
  projects/                      # collection -> /projects and /projects/{slug}
    example-project.md
  blog/                          # dated collection -> /blog and /blog/{slug}
    2026-01-15-example-post.md
  assets/                        # images etc., served at /media/*
```

Front matter is YAML between `---` fences. Unknown keys are ignored; missing keys fall back to
defaults (title from filename, slug from filename minus any date prefix).

| Type | Keys |
| --- | --- |
| page | `title`, `description`, `nav_title`, `nav_order`, `draft` |
| experience | `company`, `role`, `start`, `end` (omit = "Present"), `location`, `tech[]` |
| project | `title`, `summary`, `date`, `tags[]`, `repo`, `url`, `image`, `featured`, `draft` |
| blog | `title`, `date`, `summary`, `tags[]`, `draft` |

Rules: `draft: true` hides a document; collections sort by `date`/`start` descending; a page
appears in the nav only if it declares `nav_order`.

## Target project layout

```
personal-page/
  PersonalPage.sln
  global.json                    # pins SDK to the .NET 10 feature band
  Directory.Build.props          # nullable, warnings-as-errors, langversion
  Directory.Packages.props       # central package versions
  src/PersonalPage.Web/
    Program.cs
    Components/
      App.razor  Routes.razor  _Imports.razor
      Layout/    MainLayout.razor  NavMenu.razor  Footer.razor  ThemeToggle.razor
      Pages/     Home.razor  Experience.razor  Projects.razor  ProjectDetail.razor
                 Blog.razor  BlogPost.razor  ContentPage.razor  Diagnostics.razor
                 Error.razor
      Shared/    MarkdownBlock.razor  DocCard.razor  TimelineItem.razor  TagList.razor
    Content/
      ContentOptions.cs          # RootPath + ShowDrafts, bound from configuration
      ContentDocument.cs         # record: Slug, RelativePath, FrontMatter, Html, Validator
      IContentStore.cs
      MarkdownContentStore.cs    # load, parse, render, cache
      IContentFileSystem.cs      # test seam; PhysicalContentFileSystem is the impl
      FrontMatterParser.cs       # splits --- fences, deserializes YAML
      SlugBuilder.cs             # filename -> slug, date-prefix stripping
      DateOnlyYamlConverter.cs   # culture-independent date binding
      SiteConfig.cs              # site.yml binding
      Diagnostics/               # link checker, orphaned-asset scan, parse-failure log
      Markdown/                  # HeadingDemotion, SyntaxHighlighting, ImageAttributes
    Http/
      ContentValidatorMiddleware.cs   # ETag / Last-Modified, 304 short-circuit
      SecurityHeadersMiddleware.cs    # CSP and friends
    Health/  ContentRootHealthCheck.cs
    wwwroot/
      css/  tokens.css  site.css
      js/   theme.js  code-copy.js      # deferred, enhancement only
  tests/PersonalPage.Web.Tests/
    Fixtures/  ContentFixtureBuilder.cs  FakeContentFileSystem.cs
  content/                       # bind-mounted, see above
  docs/
  Dockerfile  compose.yaml  .dockerignore  .gitignore
  CLAUDE.md  README.md  LICENSE
```

The pre-paint theme script is inlined in `App.razor`'s `<head>`, not in `wwwroot/js/` — it has to
run before first paint, so it cannot be a deferred file. See step 5.

## Build order — engine first, design second

The nine steps below are numbered for reference, not strictly for sequence. The ordering decision
that matters is this one:

**Build the whole engine against a deliberately plain `tokens.css` before generating any visual
design.** System font stack, a neutral grey palette, correct structure, both themes declared with
the dual `prefers-color-scheme` / `data-theme` selectors described in step 5. It should look
unfinished but never be structurally *wrong*.

Three reasons this order and not the reverse:

1. The design brief asks for sample renderings "against real content rather than in isolation".
   Only after the engine runs can that request be answered with actual rendered HTML — real
   markup, real class names, a real timeline entry — instead of a prose description of it.
2. The token/layout split exists so that applying a design is a late, cheap swap. Designing first
   means writing `site.css` against markup that does not exist yet, and discovering any mismatch
   at the very end, when it is most expensive to fix.
3. The part that genuinely had to be settled early — the token *contract*: exact variable names,
   dual theme declarations, the syntax-highlighting palette — is already locked in
   `docs/design-brief.md`. The values behind that interface can arrive at any time.

So: **engine + tests + placeholder content + plain tokens → generate design from the brief plus
the real rendered HTML → swap `tokens.css`, then adjust `site.css`.**

Two cautions for that last phase. Budget real time for it: a pure token swap is the ideal case,
and any real design will also want spacing and layout changes that live in `site.css`. And when
generating the design, state explicitly that the markup is fixed and must be styled as given —
otherwise the result will assume a scaffold of wrapper elements this engine does not emit.

## Implementation steps

### 1. Scaffold

```bash
dotnet new sln -n PersonalPage
dotnet new blazor -n PersonalPage.Web -o src/PersonalPage.Web --interactivity None
dotnet new xunit -n PersonalPage.Web.Tests -o tests/PersonalPage.Web.Tests
```

Target framework `net10.0`. `--interactivity None` gives pure static SSR: no SignalR circuit, no
WebAssembly payload, low memory on the target host. Strip the template's sample
`Counter`/`Weather` pages and its bootstrap CSS. Add `global.json` pinning the 10.0 feature band
so an older installed SDK isn't picked up by accident. `git init` plus a .NET `.gitignore` — no
commits unless explicitly asked, and no AI attribution in commit messages when they are (see
CLAUDE.md).

NuGet: `Markdig` (markdown → HTML), `YamlDotNet` (front matter and `site.yml`), plus a
server-side syntax highlighter (see step 2).

**Repo-wide build settings.** Set these once rather than per project:

- `Directory.Build.props` — `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`,
  `ImplicitUsings=enable`. Cheap to adopt on day one, painful to retrofit.
- `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`, so package versions live
  in one file.
- `InvariantGlobalization=true` in the web csproj. The site is English-only, so ICU buys nothing,
  and dropping it removes roughly 30 MB from the image and makes date parsing predictable rather
  than machine-dependent.

### 2. Content pipeline — `src/PersonalPage.Web/Content/`

- `FrontMatterParser` splits leading `---` fences and deserializes with YamlDotNet's
  `UnderscoredNamingConvention`, so `nav_order` maps to `NavOrder`.
- `MarkdownContentStore` implements `IContentStore`:
  - `GetPageAsync(path)`, `GetCollectionAsync(folder)`, `GetItemAsync(folder, slug)`
  - Markdig pipeline: `UseAdvancedExtensions()` + `UseAutoLinks()` + `UseYamlFrontMatter()`,
    plus the three custom renderers described below. Raw HTML stays enabled — content is
    author-trusted and it makes embeds possible.
  - Per-document `IMemoryCache` entry keyed by path, revalidated against
    `File.GetLastWriteTimeUtc` on read. Collection listings cached for 5 seconds so a blog index
    doesn't re-stat the whole folder on every request.
  - Path traversal guard: resolve the full *real* path (following symlinks) and reject anything
    outside `RootPath`.
  - Registered as a singleton. `ContentOptions.RootPath` binds from config, defaulting to
    `./content` locally and `/app/content` in the container. `ContentOptions.ShowDrafts` defaults
    to false and is enabled only in Development.
  - File access goes through an injected `IContentFileSystem` seam rather than static `File`
    calls — see step 7. This is a testability requirement, not architecture for its own sake:
    without it the cache tests have to manipulate real timestamps and end up sleeping.
  - The cache validator is `(LastWriteTimeUtc, Length)`, not mtime alone. Some editors preserve
    mtime on save, and some filesystems have coarse timestamp granularity; including length
    catches most of what mtime alone would miss.

**Malformed front matter must never take the site down.** A single bad `---` fence or an
unparseable YAML value is an authoring typo, and typos happen at midnight. The rule:

- A YAML deserialization failure is caught per document. The document is served with default
  front matter — title derived from the filename, no `nav_order`, not a draft — and the body
  still renders.
- The failure is logged once per `(path, LastWriteTimeUtc)` at warning level, so a broken file
  doesn't spam the log on every request.
- One broken file must not empty the nav, break a collection listing, or 500 any route. Files
  that fail to parse are skipped in collection enumeration, not fatal to it.

**`site.yml` gets the same stat-revalidation as documents.** `SiteConfig` must not bind once at
startup — if it did, editing the site title, tagline, footer, or a social link would need a
container restart, which breaks the core principle. Load it through the same cache-plus-stat
path, and fall back to built-in defaults (not an exception) if it is missing or malformed.

**Two documented caching caveats**, both deliberate:

- The 5-second collection cache means a *newly created* file can take up to 5 seconds to appear
  in a collection listing (`/blog`, `/projects`, `/experience`). Edits to an existing document
  are immediate, because those go through the per-document stat check. This is the one place the
  site is not instant; note it in `docs/architecture.md`.
- The document cache has no size limit. At portfolio scale (tens of files, each a few KB) that is
  a non-issue, and bounding it would add eviction complexity for no benefit. It is a conscious
  trade, not an oversight — revisit only if `content/` ever grows to thousands of files.

**Trust boundary.** Raw HTML passthrough plus `MarkupString` rendering means every file under
`content/` is executed as authored HTML in the visitor's browser. That is the correct trade for a
single-author site, but it makes `content/` a trusted input. Never wire this store to
user-submitted or third-party content without turning raw HTML off and sanitizing. State this
plainly in `docs/architecture.md` — a forker needs to know.

**Three custom Markdig renderers**, all of which run once per document and land in the cached
HTML, so they cost nothing per request:

1. **Syntax highlighting, server-side.** Fenced code blocks are highlighted at render time into
   `<span class="...">` markup, with the colours defined as CSS custom properties in `tokens.css`
   like everything else. ColorCode — the library behind the .NET documentation — is the usual
   .NET answer here; confirm the current package and its Markdig integration before committing,
   as this is the one dependency whose ecosystem state should be verified rather than assumed.
   A vendored client-side highlighter is the fallback, not the default: highlighting in the
   cached HTML costs the visitor nothing and works before any script runs.
2. **Heading demotion.** See the heading contract below.
3. **Image attributes.** Inject `loading="lazy"` and `decoding="async"` on every image, and carry
   through any width/height that is known, so long pages don't shift layout while images arrive
   over a slow disk.

**The heading contract.** The layout renders the front-matter `title` as the page's `<h1>`.
Markdown bodies therefore start at `##`. Rather than trusting authors to remember, the renderer
demotes headings found in the body by one level, so a stray `#` becomes `<h2>` and the page keeps
exactly one `<h1>`. Without this, every page ships two `<h1>` elements — an accessibility defect,
and an ambiguity the design's type scale cannot resolve. Document the rule in
`docs/content-authoring.md` and cover it with a test.

**Dates are parsed explicitly.** `date` and `start`/`end` bind to `DateOnly` using
`CultureInfo.InvariantCulture`. Left to its defaults, YamlDotNet will produce a `DateTime` with a
timezone interpretation nobody asked for, and collection sort order depends on these values.
Binding to `DateOnly` will likely need a small `IYamlTypeConverter`; write it rather than working
around it. `InvariantGlobalization=true` from step 1 makes the behaviour identical on every
machine.

**Collection invalidation — prefer change tokens to the 5-second TTL.** `PhysicalFileProvider`
exposes `Watch()`, and `MemoryCacheEntryOptions.AddExpirationToken` will invalidate a cached
listing the moment a file in the folder changes. That removes the polling window entirely and
makes collections as instant as pages. The usual objection is that inotify is unreliable over
network filesystems — but in the intended setup the network mount lives on the authoring machine
and the write arrives at the server as an ordinary local write, so inotify fires normally.
Prototype this first; keep the 5-second TTL as the documented fallback if it proves flaky, and
record whichever way it went in `docs/architecture.md`.

### 3. Routing and pages

- `ContentPage.razor` uses `@page "/{*path}"` as a catch-all at lowest routing precedence, so
  any `content/pages/*.md` becomes a live URL with no code.
- Explicit routes for collection views: `/experience`, `/projects`, `/projects/{slug}`, `/blog`,
  `/blog/{slug}`. These out-rank the catch-all because Blazor prefers literal segments.
- `Home.razor` renders `content/pages/home.md` plus the newest few projects and posts.
- `MarkdownBlock.razor` renders via `@((MarkupString)Html)`.
- `NavMenu.razor` builds itself from pages declaring `nav_order` — adding a nav entry is a front
  matter edit, not a code change.
- Per-page `<title>` and meta description from front matter via `<PageTitle>`/`<HeadContent>`.

**Path normalization.** The container runs on a case-sensitive Linux filesystem, so `/About`
would miss `pages/about.md` and 404 — a trap that only surfaces after someone links to the site
with the wrong casing. Before lookup, normalize the incoming path: lowercase it, trim leading and
trailing slashes, collapse repeated slashes, and URL-decode it. An empty path resolves to
`home.md`. Filenames in `content/` are lowercase by convention; the authoring guide says so.

**404 handling.** Unknown paths return HTTP 404. The body is rendered from
`content/pages/404.md` when that file exists, falling back to a built-in minimal message when it
does not — so the 404 page is editable like everything else, but the site still works before
anyone writes one. The status code comes from `HttpContext.Response.StatusCode`, and must be a
real 404, not a 200 with error text.

**Mobile nav.** Build the toggle CSS-only — a visually hidden checkbox plus a `<label>`, menu
shown via a sibling selector. Not because JavaScript is forbidden (see step 5, it is not), but
because this particular control has a complete CSS solution that works before any script parses
and cannot break. Script may enhance it — closing on outside click, an `aria-expanded` update —
but the menu must open and close with scripting disabled.

### 4. Static assets, HTTP caching, health, diagnostics

- `UseStaticFiles()` for `wwwroot`, plus a second `StaticFileOptions` mapping
  `{ContentRoot}/assets` to request path `/media`, so images drop in without a rebuild. Serve
  `/media` with a long `max-age` plus revalidation — images are the bulk of the bytes and the
  slowest thing to read from SD storage.
- Response compression for text responses.

**Conditional GETs.** The store already stats every file on read, so `LastWriteTimeUtc` is
already in hand — emitting `ETag` and `Last-Modified` derived from mtime plus length is nearly
free. A middleware or endpoint filter resolves the request path through `IContentStore`, computes
the validator, and short-circuits `If-None-Match` / `If-Modified-Since` with a 304 *before*
rendering anything. A returning visitor then costs one `stat` instead of a full render.

This is deliberately chosen over ASP.NET Core's output caching. Output caching would serve a
stale copy for the length of its TTL, trading away the instant-edit property that the whole
design exists to provide. Validators keep edits instant *and* make repeat visits cheap.

**A health check that can actually fail.** `MapGet("/healthz", () => 200)` reports healthy when
the bind mount has failed and the site is serving nothing but 404s — precisely the failure Docker
most needs to catch. Use `AddHealthChecks()` with a check asserting that `RootPath` exists and
contains a non-empty `pages/` directory, exposed via `MapHealthChecks("/healthz")`.

**Fail fast on configuration.** Bind `ContentOptions` with
`.ValidateDataAnnotations().ValidateOnStart()`. A mistyped `Content__RootPath` should refuse to
start, loudly, rather than quietly serving an empty site.

**A `/_diagnostics` page.** Because content never passes through a build, nothing validates it —
this page restores the feedback loop that a static site generator gets for free from its build
failing. It walks the content store and lists:

- documents whose front matter failed to parse, with the error
- internal links that resolve to no page, item, or asset
- `![](/media/...)` references pointing at files that do not exist
- assets under `content/assets/` that nothing references
- drafts, and any document dated in the future

Read-only, no styling budget, not linked from the nav. It is the page you will actually open
every week, and it is roughly 150 lines.

**Security headers.** A strict Content-Security-Policy is unusually easy here because every asset
is same-origin and there are no third-party scripts, fonts, or trackers: `default-src 'self'`,
`frame-ancestors 'none'`, plus `X-Content-Type-Options: nosniff` and `Referrer-Policy:
no-referrer`. The CSP also acts as a safety net for the raw-HTML passthrough kept enabled in step
2. Any first-party script must be a file under `wwwroot`, not an inline block, so that the policy
never needs `'unsafe-inline'` — the one exception is the pre-paint theme script in step 5, which
gets a hash or nonce rather than a blanket allowance.

### 5. Styling and client-side behaviour

- `tokens.css` holds only CSS custom properties: colour, type scale, spacing, radii, shadows.
  `site.css` holds layout and consumes only tokens.
- A design therefore lands as a token-file swap, not a rewrite.
- Typography defaults tuned for long-form markdown (measure ~68ch, generous line height).

**Dark mode is declared twice.** `tokens.css` defines the dark palette under both
`@media (prefers-color-scheme: dark)` *and* `:root[data-theme="dark"]`, with a matching
`:root[data-theme="light"]` block, and the attribute selectors must win over the media query in
both directions. The media query alone cannot express "this visitor overrode the OS setting",
which is what the theme toggle below needs. Getting this into the token contract now costs
nothing; retrofitting it means touching every colour declaration.

#### JavaScript policy

JavaScript is allowed where it makes the site better. Two boundaries keep it from undoing the
architecture:

**`--interactivity None` stays.** Allowing scripts does not mean enabling Blazor's interactive
render modes. Hand-written vanilla JS served as a static file from `wwwroot` costs the server
nothing. Blazor Server would open a SignalR circuit per visitor and hold component state in
server memory; Blazor WebAssembly would ship a multi-megabyte runtime. Neither is acceptable on
this hardware, and neither is needed for anything on the list below. This distinction is the
whole point — it is *client-side script*, not *server-side interactivity*, that has been
unblocked.

**No external hosts.** Same rule as fonts and images: no CDN, no analytics, no third-party
widget. The site must work fully on a network with no route to the internet. Any library gets
vendored into `wwwroot` and committed.

**Progressive enhancement, not dependence.** Every page must render, read, and navigate with
scripting disabled. Content is server-rendered HTML; script only adds affordances on top. Nothing
in the content pipeline may depend on the client.

Worth building, in rough order of value:

| Enhancement | Notes |
| --- | --- |
| Theme toggle | The one genuine gap in CSS alone — lets a visitor override the OS preference. Persist to `localStorage`, set `data-theme` on `<html>`. |
| Copy button on code blocks | Small, self-contained, immediately useful on a developer site. |
| Table-of-contents highlighting | Scroll-spy on long posts; headings already carry ids from `UseAutoIdentifiers`. |
| Image lightbox | Only if the projects pages end up image-heavy. |

**The theme toggle needs one inline script in `<head>`.** It must read `localStorage` and set the
`data-theme` attribute *before first paint*, or a visitor whose override differs from their OS
setting sees a flash of the wrong theme on every navigation — a server-rendered site has no
client-side router to hide it. This is the only inline script permitted; give it a CSP hash or
nonce rather than relaxing the policy from step 4 with `'unsafe-inline'`. Keep it to a few lines,
and put the toggle's own click handling in a normal deferred script file.

Anything beyond the table above should be justified against page weight before it ships. The
site's value is reading comfort; script that competes with that loses.

### 6. Docker

Multi-stage `Dockerfile`: `mcr.microsoft.com/dotnet/sdk:10.0-alpine` to build,
`mcr.microsoft.com/dotnet/aspnet:10.0-alpine` to run. Alpine keeps the image small and, unlike
chiseled images, still has a shell and `wget` for the healthcheck. Both are multi-arch
manifests, so one Dockerfile covers arm64 and amd64. Runs as the non-root `app` user.
**`content/` is excluded via `.dockerignore`** — it arrives as a mount.

```yaml
services:
  personal-page:
    build: .
    image: personal-page:latest
    ports: ["8080:8080"]
    volumes: ["./content:/app/content:ro"]
    environment:
      - ASPNETCORE_HTTP_PORTS=8080
      - Content__RootPath=/app/content
      - DOTNET_gcServer=0
    restart: unless-stopped
    read_only: true
    tmpfs: ["/tmp"]
    cap_drop: [ALL]
    security_opt: ["no-new-privileges:true"]
    mem_limit: 256m
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:8080/healthz"]
      interval: 30s
```

The mount is read-only: the app only reads, and editing happens on the host side. Bind mounts
share the host kernel, so a file saved in a text editor is visible inside the container
instantly.

**Why `DOTNET_gcServer=0`.** ASP.NET Core enables server GC when it detects multiple cores, which
on a 4-core board means substantially more resident memory for no benefit at single-visitor
request volumes. Workstation GC is the right mode here. Confirm the actual figure with
`docker stats` after deployment rather than trusting the default.

**Container hardening** is cheap and worth doing even on a private LAN: a read-only root
filesystem with a tmpfs for `/tmp` (the runtime needs somewhere writable), all capabilities
dropped, no privilege escalation, and a memory limit so a runaway process cannot take the whole
board down with it.

**On ReadyToRun and trimming.** Publishing with `PublishReadyToRun=true` measurably improves
startup on arm64 and is worth enabling. **Do not enable trimming** — Blazor's reflection makes
trimmed builds fail in ways that surface at runtime on a specific page rather than at build time,
which is the worst possible failure mode for a site nobody is watching.

**Deploying to an arm64 host** — cross-build from an amd64 dev machine:

```bash
docker buildx build --platform linux/arm64 -t personal-page:latest --load .
```

QEMU binfmt registration doesn't survive a reboot; if buildx reports arm64 as unsupported,
re-register once with `docker run --privileged --rm tonistiigi/binfmt --install arm64`.
Transfer with `docker save personal-page:latest | ssh <deploy-host> docker load`, or build on the
target host directly as a fallback. Keep the real hostname in an untracked `.env`, not in the
repo.

### 7. Tests — `tests/PersonalPage.Web.Tests/`

xunit. Almost all the logic in this project lives in pure transformations — bytes on disk to a
`ContentDocument`, a filename to a slug, a folder to an ordered list — which is the most testable
shape code comes in. Test it thoroughly; there is no UI to hide behind and no CI running against
content, so these tests are the only thing standing between a typo and a broken site.

#### Two prerequisites for good tests

**Abstract the filesystem.** Put a thin seam in front of file access — an `IContentFileSystem`
with `Exists`, `ReadAllText`, `EnumerateFiles`, `GetLastWriteTimeUtc` — and inject it into
`MarkdownContentStore`. Without this, every cache test has to manipulate real mtimes and ends up
sleeping to dodge filesystem timestamp granularity, which is how test suites become slow and
flaky. With it, "the file changed" is a one-line fake. Keep the real implementation trivial so
there is nothing in it to test.

**A fixture builder.** A small fluent helper for composing content trees in tests
(`Content.Page("about").WithFrontMatter(...).Build()`) pays for itself by the tenth test and keeps
each test readable as a statement of intent rather than a pile of string literals.

#### Front matter parsing

- no front matter at all — entire file is body, title falls back to filename
- front matter present — parsed, and the fence is not included in the body
- empty front matter block (`---\n---`)
- unclosed fence — treated as no front matter, logged, body preserved
- a `---` horizontal rule in the body is not mistaken for a front matter delimiter
- a `---` fence that is not the first thing in the file is not treated as front matter
- **CRLF line endings** parse identically to LF — files get edited from Windows machines
- **a UTF-8 BOM** at the start of the file does not break fence detection
- unknown keys are ignored rather than throwing
- `nav_order` maps to `NavOrder` via the underscore naming convention
- a title containing a colon (`title: Blazor: a retrospective`) — pins whether it needs quoting,
  and that the unquoted form degrades gracefully rather than crashing
- `tags` accepts both block-sequence and inline `[a, b]` form
- boolean forms — `true`, `True`, and `yes` — pinning what YamlDotNet actually does with each so
  the authoring guide can state it accurately

#### Slug derivation

- `2026-01-15-my-post.md` → `my-post`
- `my-post.md` → `my-post`
- `2026-01-15.md` — date prefix and nothing else; must not produce an empty slug
- uppercase filename → lowercased slug
- non-ASCII filename (accented or diacritic characters) — pins the chosen policy explicitly rather
  than leaving it to chance, since these produce awkward URLs either way
- two files deriving the same slug — deterministic winner, and the collision surfaces in
  `/_diagnostics` rather than silently dropping a document

#### Collections and ordering

- descending sort by `date` / `start`
- **ties sort deterministically** (secondary key on slug) so ordering does not shuffle between
  restarts
- a document with no date sorts last rather than throwing
- a future-dated document is still served, and is flagged in diagnostics
- experience with no `end` renders "Present" and sorts as the most recent entry
- date parsing is culture-independent: `date: 2026-01-15` yields the same `DateOnly` on any
  machine locale, and ordering is stable across a DST boundary
- `featured: true` projects surface where the home page expects them

#### Drafts

- `draft: true` is excluded from collection listings
- **a draft is 404 at its direct URL** — not merely hidden from lists, or an unpublished post
  leaks to anyone who guesses the slug
- a draft page never appears in the nav
- drafts *are* visible when the "show drafts" development setting is on — both modes tested, since
  this is the setting most likely to be wrong in production

#### Nav building

- only pages declaring `nav_order` appear
- ascending `nav_order`, with ties broken alphabetically and deterministically
- `nav_title` overrides `title` when present
- a malformed page does not remove the rest of the nav

#### Path handling and security

- `../` traversal, percent-encoded `%2e%2e%2f`, and double-encoded variants all rejected
- an absolute path in the request resolves nowhere outside the root
- a symlink inside `content/` pointing outside it is rejected — resolve the real path, not the
  lexical one
- a null byte in the path is rejected
- `/About`, `/about/`, `//about` and the URL-encoded form all resolve to `about.md`
- empty path resolves to `home.md`

#### Caching behaviour

- a second read of an unchanged document does not re-parse — assert via a parse counter on the
  filesystem fake, not by timing
- a changed `LastWriteTimeUtc` triggers a re-parse
- **same mtime, different content does *not* re-parse** — this pins a real limitation of
  mtime-based invalidation honestly rather than pretending it away. Include file length in the
  cache validator to narrow the window, and note the residual case in `docs/architecture.md`.
- a deleted file 404s and its cache entry is dropped
- concurrent reads of the same uncached document do not produce torn state or double-parse
- `site.yml` is picked up after an edit with no restart; missing or malformed yields defaults

#### Markdown rendering

- heading demotion: `#` becomes `<h2>`, and **`######` stays `<h6>`** rather than overflowing
- exactly one `<h1>` per rendered page
- two headings with identical text get unique anchor ids
- images gain `loading="lazy"` and `decoding="async"`
- a code fence with an unrecognised language renders as plain code instead of throwing
- a code fence with no language attribute renders
- raw HTML in the body survives to the output (documenting the trust decision as a test)
- malformed front matter degrades rather than throws: body still renders, title falls back to the
  filename, the document is skipped in collection enumeration, the rest of the collection survives

#### Diagnostics

These are pure functions over a content tree and are cheap to test properly:

- an internal link to a nonexistent page is reported
- a valid internal link, and an external link, are not reported
- a `/media/` reference with no matching file is reported
- an asset referenced by nothing is reported as orphaned
- a clean content tree reports nothing — guarding against false positives, which are what make a
  diagnostics page get ignored

**One integration test** with `WebApplicationFactory<Program>` over a fixture content directory.
Routing precedence — literal routes out-ranking `@page "/{*path}"` — is the single riskiest
assumption in this design, and no unit test on the content store can catch it if it is wrong.
Assert that `/blog` renders the blog index rather than falling through to the catch-all, that
`/blog/{slug}` renders a post, that a `pages/*.md` file resolves through the catch-all, and that
an unknown path returns status 404.

Two more integration assertions worth the small cost:

- a request carrying the `ETag` from a previous response comes back 304, and the same request
  after touching the file comes back 200
- the health check reports unhealthy when `RootPath` points at a directory that does not exist

### 8. Documentation

- `CLAUDE.md` — repo map and conventions for AI sessions
- `README.md` — what the project is, quick start, and how to fork it for your own site
- `docs/content-authoring.md` — every front matter field, copy-paste templates, how to add a
  page/project/post/image. Written for a plain text editor, no tooling assumed.
- `docs/architecture.md` — request flow, content store, caching and invalidation, routing precedence
- `docs/deployment.md` — Docker deployment to an arm64 host, compose, updating content vs.
  updating code, troubleshooting. Placeholder hostnames only.
- `docs/design.md` — the token contract and how to swap in a design
- `docs/design-brief.md` — a fill-in-the-blanks brief for generating the visual design
- `LICENSE` — MIT, so the engine is genuinely reusable

All docs are written for a generic reader, in second person. No names, employers, hostnames, or
IP addresses.

CLAUDE.md already links to `content-authoring.md`, `architecture.md`, and `deployment.md`. Those
are forward references until this step lands — write them alongside the code they describe, not
after, so the links stop dangling as early as possible. `architecture.md` in particular owes the
reader three things decided in steps 2 and 3: the caching caveats, the `content/` trust boundary,
and routing precedence.

### 9. Placeholder content

Ships as `content.example/` (see the decision at the end of this document). One example file per
type, each marked as an example, so every route renders on first run and each content type has a
working reference to copy. The examples use obvious placeholders ("Your Name", "Example Company")
— never a real identity. `cp -r content.example content` is the first thing anyone does, including
you.

## Verification

```bash
dotnet build && dotnet test
dotnet run --project src/PersonalPage.Web
```

Check every route returns 200: `/`, `/about`, `/experience`, `/skills`, `/education`,
`/projects`, `/blog`, `/now`, `/uses`, `/contact`, `/healthz`, plus a known 404.

**The critical test — content edits without redeploying.** With the container running:

1. `curl -s localhost:8080/about | grep -o "<h1>.*</h1>"` — note the heading.
2. Edit `content/pages/about.md` in a text editor and save.
3. Re-run the curl — the new text must appear, with no restart.
4. Create `content/pages/scratch.md` and confirm `/scratch` returns 200 while the container
   keeps running. Delete it and confirm it 404s.
5. Edit the site title in `content/site.yml`, save, reload — the header must change with no
   restart.
6. Add a new post under `content/blog/` and confirm it appears on `/blog`. Instant if change
   tokens are in use; up to ~5 seconds if the TTL fallback was kept. Step 4 is deliberately a page
   rather than a collection so it stays instant either way.

Steps 1–5 are the whole point of the design. If they pass, the requirement is met.

**Also check, once:**

- The site is readable and navigable with JavaScript disabled in the browser — nav, all routes,
  code blocks still highlighted.
- Toggling the theme persists across a full page load with no flash of the wrong theme.
- A second request for the same page returns 304, and returns 200 again after touching the file.
- `/_diagnostics` reports a deliberately broken internal link and a deliberately malformed front
  matter block.
- `docker stats` shows resident memory in a range you are happy with on the target board.

Finally, on the deployment host: `docker compose up -d`, confirm `docker compose ps` reports
healthy, browse to `http://<deploy-host>:8080` from the LAN.

## Decided — `content/` is not tracked in git

The repo ships `content.example/`; the real `content/` is gitignored.

This keeps the published repo a clean, reusable engine, and keeps personal CV details out of a
public commit history. The cost, accepted knowingly: your own site content is not version
controlled by this repo. Keep it in a private repo or a backup if that matters later.

Consequences, which the steps above already reflect:

- Step 9 produces `content.example/`, not `content/`.
- `.gitignore` contains `content/`; `.dockerignore` contains both.
- `compose.yaml` still mounts `./content` — the runtime path is unchanged.
- The README's first instruction is `cp -r content.example content`.
- The health check failing on a missing content root (step 4) is what catches a forker who skips
  that copy.

## Out of scope for now

Real CV/biography content (owner-supplied markdown, added later), public internet exposure and
TLS, RSS, analytics, and search. RSS and search are the natural next additions; the content store
already exposes what they would need.
