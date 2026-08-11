# Personal page — a portfolio site with no build step for content

A markdown-driven developer portfolio. Server-rendered ASP.NET Core (Blazor SSR), no
JavaScript framework, no external hosts, no database.

**The one thing this image is built around:** your content is a bind-mounted directory of
plain markdown, not something baked into the image. Edit a file in a text editor, save it, and
the site reflects it on the next request — no restart, no rebuild, no redeploy. Adding a page,
a blog post, a project, a nav entry or an image is a file operation.

Source and full documentation:
[github.com/edgars-eizvertins/personal-page](https://github.com/edgars-eizvertins/personal-page)

## Supported architectures

`linux/amd64` and `linux/arm64` — a single tag serves both, so a Raspberry Pi 4/5 on 64-bit
Raspberry Pi OS pulls the right image with a plain `docker pull`.

## Quick start

```bash
mkdir personal-page && cd personal-page
# save the Compose file below as docker-compose.yml, then:
printf 'IMAGE_NAME=eeizvertins/personal-page\nSITE_PORT=8080\n' > .env

mkdir -p content/pages
printf -- '---\ntitle: Hello\n---\n\nIt works. Edit this file and reload.\n' > content/pages/home.md

docker compose up -d
```

Open `http://<host>:8080`. Now edit `content/pages/home.md`, save, and reload the page — that
round trip is the entire design.

For a fuller starting point, copy `content.example/` from the source repository: it ships one
working file per content type (pages, experience, projects, blog) so every route renders.

```bash
git clone --depth 1 https://github.com/edgars-eizvertins/personal-page /tmp/pp
cp -r /tmp/pp/content.example ./content
```

## docker-compose.yml

```yaml
{{COMPOSE}}
```

## Configuration

| Variable | Default | Meaning |
|---|---|---|
| `IMAGE_NAME` | **required** | Image to run, e.g. `eeizvertins/personal-page`. |
| `IMAGE_TAG` | `latest` | Set to `v0.1.0` to pin a release. |
| `SITE_PORT` | `8080` | Host port the site is served on. |
| `Content__RootPath` | `/app/content` | Where the app reads content. Must match the mount. |
| `Content__ShowDrafts` | `false` | Show documents marked `draft: true`. Leave off in production. |

## What goes in content/

```
content/
  site.yml          title, tagline, footer, social links, nav
  pages/            free-form pages    ->  /{slug}
  experience/       timeline entries   ->  /experience
  projects/         cards and detail   ->  /projects, /projects/{slug}
  blog/             posts              ->  /blog, /blog/{slug}
  assets/           images and files   ->  /media/*
```

Each markdown file opens with a YAML front matter block:

```markdown
---
title: About
nav_order: 1
---

Body starts here. Headings start at `##`.
```

`draft: true` hides a document completely. A page joins the navigation by declaring
`nav_order`. Collections sort newest first. Unknown keys are ignored, and a file whose front
matter fails to parse still renders its body rather than breaking the site.

Full reference:
[docs/content-authoring.md](https://github.com/edgars-eizvertins/personal-page/blob/main/docs/content-authoring.md)

## Health and diagnostics

`/healthz` asserts the content directory is actually mounted and contains pages, so the
container reports unhealthy when the bind mount has failed — not merely when the process has
died.

`/_diagnostics` is an unlisted, unstyled page listing broken internal links, `/media/`
references with no matching file, orphaned assets, front matter errors, drafts, and anything
dated in the future. Content never passes through a build, so nothing else validates it.

## Notes

- Runs as a non-root user, with a read-only root filesystem and all capabilities dropped.
- Code blocks are syntax-highlighted **on the server**, so they are coloured before any script
  runs and stay coloured with JavaScript disabled.
- No external hosts of any kind — no CDN, no fonts, no analytics. It works on a network with
  no route to the internet.
- `content/` is a **trusted** input: raw HTML in a markdown file is passed through to the
  browser. That is the right trade for a single-author site and the wrong one if anybody else
  can write there.

## Upgrading

```bash
docker compose pull && docker compose up -d
```

Your `content/` is untouched — it was never in the image.
