# Architecture

How the engine works, and why the parts that look unusual are the way they are.

The design has exactly one hard requirement: **content changes must never require a code change,
a rebuild, or a redeploy.** Every decision below either serves that or is a consequence of it.

## Stack

| Piece | Choice | Why |
| --- | --- | --- |
| Framework | ASP.NET Core 10, Blazor with `--interactivity None` | Pure static server rendering: no SignalR circuit per visitor, no WebAssembly payload |
| Markdown | Markdig, advanced extensions plus autolinks | The standard .NET answer, and its renderer pipeline is replaceable |
| YAML | YamlDotNet, underscored naming convention | `nav_order` maps to `NavOrder` with no attributes |
| Highlighting | ColorCode, class-based formatter, server-side | Colour lands in cached HTML; no highlighting library runs in the browser |
| Globalization | `InvariantGlobalization=true` | English-only site; removes ~30 MB from the image and makes date parsing machine-independent |

There is no client-side interactivity framework and no external host of any kind — no CDN, no
fonts, no analytics. The site works fully on a network with no route to the internet.

## Request flow

```
                          ┌─────────────────────────┐
 GET /blog/hello  ──────► │ SecurityHeadersMiddleware│  CSP, nosniff, referrer policy
                          └────────────┬─────────────┘
                          ┌────────────▼─────────────┐
                          │ ResponseCompression       │
                          └────────────┬─────────────┘
                          ┌────────────▼─────────────┐
                          │ StaticFiles → /media      │  content/assets, long max-age
                          └────────────┬─────────────┘
                          ┌────────────▼─────────────┐
                          │ UseRouting                │
                          └────────────┬─────────────┘
                          ┌────────────▼─────────────┐
                          │ MapStaticAssets → wwwroot │  fingerprinted css and js
                          └────────────┬─────────────┘
                          ┌────────────▼─────────────┐
                          │ ContentValidatorMiddleware│  ETag / Last-Modified; 304 exits here
                          └────────────┬─────────────┘
                          ┌────────────▼─────────────┐
                          │ Blazor endpoint           │  Router → page component
                          └────────────┬─────────────┘
                          ┌────────────▼─────────────┐
                          │ IContentStore             │  stat, cache, parse, render
                          └────────────┬─────────────┘
                                       ▼
                                  content/*.md
```

**The `/media` static file middleware has to run before routing.** `ContentPage` declares
`@page "/{*path}"`, which matches every URL, and `StaticFileMiddleware` skips any request that
has already matched an endpoint. With ASP.NET Core's automatic `UseRouting` placement, `/media`
would fall through to a 404. `Program.cs` therefore calls `UseRouting()` explicitly, after the
static file middleware, which suppresses the automatic placement.

## The content store

`MarkdownContentStore` (a singleton implementing `IContentStore`) is the only thing in the
application that reads content from disk. Everything else — components, diagnostics, the
validator middleware, the health check — goes through it.

```
GetPageAsync("/about")
  ├─ ContentPath.Normalize      lowercase, decode, collapse slashes, reject traversal
  ├─ ResolveWithinRoot          lexical check, then real path with symlinks followed
  ├─ IContentFileSystem.Stat    (LastWriteTimeUtc, Length)
  ├─ cache hit?  →  return the parsed document
  └─ miss:
       ├─ ReadAllTextAsync
       ├─ FrontMatterParser     split the --- fence, deserialize, never throw
       ├─ SlugBuilder           filename → slug, date prefix → default date
       └─ MarkdownRenderer      demote headings, annotate images, highlight code
```

### Caching and invalidation

**Documents** are cached indefinitely and revalidated against `(LastWriteTimeUtc, Length)` on
every read. That is what makes an edit appear with no restart, no watcher, and no race. A stat
is cheap; a parse is not, so the stat is what happens per request.

Length is in the validator alongside mtime because some editors preserve mtime on save and some
filesystems have coarse timestamp granularity. Including length catches most of what mtime alone
would miss.

**Collections** (`/blog`, `/projects`, `/experience`, and the page list behind the nav) are
cached behind two invalidation signals at once:

- a `PhysicalFileProvider.Watch` change token, which fires the moment a file in the folder is
  created, changed or deleted;
- a five-second absolute expiry as a backstop.

The change token normally wins, so a new file appears immediately. This was measured through the
Docker bind mount, which is the case that actually matters: a file created on the host shows up
in `/blog` in under a second, well inside the five-second backstop. Bind mounts share the host
kernel, so inotify fires normally.

The backstop exists because inotify is not reliable on every filesystem. Where it does not fire,
the listing is at most five seconds stale. Set `Content:UsePollingFileWatcher` to `true` if the
content root is itself a network mount inside the container.

**`site.yml`** goes through the same stat-revalidated path as documents. Binding it once at
startup would mean a container restart to change the site title, which breaks the core rule.

### Three caveats, all deliberate

1. **A newly created file can take up to five seconds to appear in a collection listing** when
   the watcher does not fire. Edits to files that already exist are always immediate, because
   those go through the per-document stat.
2. **Same mtime, same length, different bytes does not invalidate.** This is the residual case
   that stat-based caching cannot see. It is pinned by a test rather than pretended away.
   `touch` on the file fixes it.
3. **The document cache has no size limit.** At portfolio scale — tens of files of a few KB —
   bounding it would add eviction complexity for no benefit. Revisit only if `content/` grows to
   thousands of files.

### Concurrency

Cache misses take a per-path semaphore, so a burst of requests for a cold document parses it
once. `site.yml` has its own lock. There is no global lock: two different documents parse in
parallel.

## Path handling and the traversal guard

Every incoming path is normalized before it is used: URL-decoded repeatedly until it stops
changing, lowercased, slashes collapsed, and rejected outright if it contains a null byte, a
backslash, a drive letter, or a `..` segment. An empty path resolves to `home`.

Lowercasing is not cosmetic. The container runs on a case-sensitive Linux filesystem, so
`/About` would otherwise miss `pages/about.md` — a trap that only surfaces after someone links to
the site with the wrong casing.

Repeated decoding matters because routing has already decoded once. A double-encoded
`%252e%252e%252f` arrives as `%2e%2e%2f`, which a single further decode turns into `../` — and
that is then rejected rather than resolved.

After normalization, the resolved path is checked twice: lexically against the content root, and
then again after following every symlink on the way. A symlink inside `content/` pointing outside
it passes the lexical test and must still be refused, which only a real-path check catches.

## Routing precedence

```
/                    Home.razor
/blog                Blog.razor
/blog/{slug}         BlogPost.razor
/projects            Projects.razor
/projects/{slug}     ProjectDetail.razor
/experience          Experience.razor
/_diagnostics        Diagnostics.razor
/error               Error.razor
/{*path}             ContentPage.razor      ← lowest precedence
```

Blazor prefers literal segments over a catch-all parameter, so the collection views always win
and everything else falls through to `content/pages/`. This is the single riskiest assumption in
the design — no unit test on the content store can catch it if it is wrong — so it is pinned by
an integration test that asserts `/blog` renders the blog index rather than a page lookup.

## 404 handling

Unknown paths return a real HTTP 404 whose body comes from `content/pages/404.md`, falling back
to a built-in message when that file does not exist.

The mechanism is less obvious than it looks. Blazor's static SSR treats a 404 status set during
rendering as a not-found signal and **discards whatever the current component produced**, handing
over to the Router's `NotFoundPage`. So a component that discovers a miss cannot render the 404
body itself. Instead it calls `NavigationManager.NotFound()`, and `NotFoundPage.razor` — routable
only because the Router requires it to be — renders the content.

## Conditional GETs

`ContentValidatorMiddleware` resolves the request path through the store, computes an ETag from
`(mtime, length)`, and answers `If-None-Match` / `If-Modified-Since` with a 304 before any
component renders. A returning visitor costs one stat instead of a full render.

The validator combines the document with the "chrome": `site.yml` and the front matter of every
page in the nav. Without that, adding a nav entry would leave every cached page stale in
visitors' browsers.

This is deliberately chosen over ASP.NET Core's output caching. Output caching serves a stale
copy for the length of its TTL, which trades away the instant-edit property the whole design
exists to provide. Validators keep edits instant *and* make repeat visits cheap.

## Markdown rendering

Three transformations run once per document and land in the cached HTML, so they cost nothing
per request.

**Heading demotion.** The layout renders the front-matter `title` as the page's single `<h1>`, so
bodies are meant to start at `##`. A body that contains an `h1` has every heading pushed down one
level, capped at `h6`. A body that already starts at `##` is left exactly as written — demoting
unconditionally would leave `<h2>` permanently unused and push a correctly authored document one
step out of the designed type scale.

**Image attributes.** Every image gains `loading="lazy"` and `decoding="async"`. Explicit
dimensions are not invented; an author who knows them writes `{width=800 height=450}` and the
generic-attributes extension carries them through.

**Syntax highlighting.** Fenced code is colourised by ColorCode's class-based formatter into
`<span class="keyword">`-style markup. The colours themselves are CSS custom properties in
`tokens.css`, like every other colour on the site, so a design swap never has to touch C#.

ColorCode ships no shell or YAML definitions — the two fence languages a developer site uses most
after its own stack — so both are defined as regex rule sets in `AdditionalLanguages.cs`. A
ColorCode language is nothing more than an ordered list of rules, which made that cheaper than
taking on a second highlighting dependency. Anything still unrecognised renders as plain,
readable code rather than failing.

The YAML front matter extension is deliberately *absent* from the Markdig pipeline. The parser
has already removed the fence, so the extension would have nothing left to do except swallow a
horizontal rule that happens to be the first thing in a body.

## Trust boundary

Raw HTML passthrough plus `MarkupString` rendering means **every file under `content/` executes
as authored HTML in the visitor's browser.**

That is the correct trade for a single-author site: it is what makes an embedded `<figure>`,
`<details>` or `<iframe>` possible without a plugin system. It also makes `content/` a trusted
input.

**Never point this store at user-submitted or third-party content** without disabling raw HTML in
the Markdig pipeline and sanitizing the output. If you fork this to build something multi-author,
that is the change to make first.

The Content-Security-Policy is a partial safety net: `default-src 'self'` means a `<script src>`
pasted into a content file cannot load, and no inline script other than the pre-paint theme
setter (allowed by hash) can run.

## Error handling

Nothing about a content file can take the site down.

- A YAML deserialization failure is caught per document. The document is served with default
  front matter — title from the filename, no `nav_order`, not a draft — and the body still
  renders.
- The failure is logged once per `(path, mtime)`, so a broken file does not spam the log on every
  request, and is listed on `/_diagnostics`.
- Files that fail to parse are skipped in collection enumeration, not fatal to it. One broken
  file cannot empty the nav, break a listing, or 500 a route.
- An unreadable file, a bad regex backtrack in the highlighter, or an unexpected input all
  degrade rather than throw.

## Configuration

`ContentOptions` binds from the `Content` section with `.ValidateDataAnnotations()` and
`.ValidateOnStart()`, so a mistyped `Content__RootPath` refuses to boot rather than quietly
serving an empty site.

| Key | Default | Meaning |
| --- | --- | --- |
| `Content:RootPath` | `content` | Content root; relative paths resolve against the app content root |
| `Content:ShowDrafts` | `false` | Show `draft: true` documents. Development only |
| `Content:CollectionCacheSeconds` | `5` | Backstop expiry for collection listings |
| `Content:UsePollingFileWatcher` | `false` | Poll instead of relying on inotify |

## Health

`/healthz` asserts that the content root exists, has a `pages/` directory, and that the directory
contains at least one markdown file. A check that only proves the process is listening would
report healthy while the bind mount had failed and the site served nothing but 404s — precisely
the failure Docker most needs to catch. It is also what catches a forker who skipped
`cp -r content.example content`.

## Testing

Almost all the logic is pure transformation — bytes to a `ContentDocument`, a filename to a slug,
a folder to an ordered list — which is the most testable shape code comes in.

`IContentFileSystem` is a seam in front of file access, injected into the store. This is a
testability requirement, not architecture for its own sake: without it, every cache test has to
manipulate real mtimes and ends up sleeping to dodge filesystem timestamp granularity. With it,
"the file changed" is a one-line call on a fake that also counts reads, so cache tests assert on
parse counts rather than on timing.

One integration test class drives the real application with `WebApplicationFactory` over a
throwaway content directory on disk, covering the things a fake would hide: routing precedence,
middleware order, conditional GETs, real 404s, and the health check failing on a missing root.
