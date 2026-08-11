# Personal page

A markdown-driven developer portfolio. Server-rendered ASP.NET Core (Blazor SSR), no build step
for content, no JavaScript framework, no external hosts.

**The one rule that shapes everything:** adding or editing content never requires a code change,
a rebuild, or a redeploy. You edit a markdown file in a plain text editor — locally or over a
network mount — save it, and the site reflects it on the next request.

It is meant to be forked. Nothing about a specific person, employer or host lives in `src/` or
`docs/`; all of that belongs in `content/`.

## Quick start

```bash
cp -r content.example content
dotnet run --project src/PersonalPage.Web
```

Then open <http://localhost:5018>. Every route renders on first run because
`content.example/` ships one working file per content type.

Now edit `content/pages/about.md`, save, and reload `/about`. That is the whole workflow.

## What you get

| Route | Comes from |
| --- | --- |
| `/` | `content/pages/home.md`, plus recent projects and posts |
| `/{anything}` | `content/pages/{anything}.md` — a new file is a new URL |
| `/experience` | `content/experience/*.md`, rendered as a timeline |
| `/projects`, `/projects/{slug}` | `content/projects/*.md` |
| `/blog`, `/blog/{slug}` | `content/blog/*.md` |
| `/media/*` | `content/assets/*` |
| `/healthz` | Health check that fails when the content root is missing or empty |
| `/_diagnostics` | Broken links, missing images, orphaned assets, front matter errors |

Nav entries come from `nav_order` in a page's front matter and from the `nav:` list in
`content/site.yml`. Neither needs a code change.

## Making it yours

1. `cp -r content.example content` if you have not already. `content/` is gitignored, so your CV
   never lands in this repo's history.
2. Edit `content/site.yml` — title, tagline, footer, social links, nav.
3. Replace the files under `content/pages/`, `content/experience/`, `content/projects/` and
   `content/blog/`. Every shipped file is marked as an example and is safe to delete.
4. Drop images into `content/assets/` and reference them as `/media/your-image.png`.
5. Visit `/_diagnostics` and fix whatever it lists.

Full front matter reference: [docs/content-authoring.md](docs/content-authoring.md).

## Deploying

Docker, on anything from a Raspberry Pi upwards:

```bash
docker compose up -d --build
```

`content/` is bind-mounted read-only, never baked into the image, so updating the site on the
host is a text edit and involves Docker not at all.

To publish a multi-arch image instead, so the deployment host only ever pulls:

```bash
./scripts/publish-images.sh v0.1.0
```

Both architectures come out of one native build — no emulation. Details, and the registry-free
alternative: [docs/deployment.md](docs/deployment.md).

## Documentation

| Document | What is in it |
| --- | --- |
| [docs/content-authoring.md](docs/content-authoring.md) | Every front matter field, copy-paste templates, how to add a page/post/project/image |
| [docs/architecture.md](docs/architecture.md) | Request flow, the content store, caching and invalidation, routing precedence, the trust boundary |
| [docs/deployment.md](docs/deployment.md) | Docker, compose, arm64, updating content vs. updating code, troubleshooting |
| [docs/design.md](docs/design.md) | The CSS token contract and how to swap in a design |
| [docs/design-brief.md](docs/design-brief.md) | A fill-in-the-blanks brief for generating that design |
| [docs/plan.md](docs/plan.md) | The build plan this repository was implemented from |

## Development

```bash
dotnet build && dotnet test
```

Requires the .NET 10 SDK. In Development the app reads `content/` from the repository root and
shows drafts; in the container it reads `/app/content` and hides them.

## Licence

MIT — see [LICENSE](LICENSE). Use it, fork it, put your own name on your own site.
