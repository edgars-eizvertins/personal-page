# Deployment

Docker, targeting a small ARM64 single-board host on a private LAN. No TLS and no public exposure
— add both before putting this on the open internet.

Throughout, `<deploy-host>` stands for your own machine's hostname or IP. Keep the real value in
an untracked `.env` file, never in the repository.

## What ships and what does not

The image contains the application and nothing else. **`content/` is excluded by
`.dockerignore` and arrives as a bind mount.** That is what makes updating the site a text edit
rather than a deployment.

```
image     ← code. Changes when you change C#, CSS or JS.
bind mount ← content. Changes when you write.
```

## First deployment

On the target host:

```bash
git clone <your-fork> personal-page
cd personal-page
cp -r content.example content
docker compose up -d --build
```

If port 8080 is already in use on that host, put the port you want in a `.env` next to
`compose.yaml` before starting:

```bash
echo "SITE_PORT=8091" > .env
```

`.env` is gitignored, so the host's local port choice never travels back to the repository.

Then check it:

```bash
docker compose ps
```

The `STATUS` column should read `healthy` within a minute. Browse to
`http://<deploy-host>:8080`.

If the status is `unhealthy`, the health check is telling you the content root is missing or
empty — almost always a skipped `cp -r content.example content`, or a bind mount path that does
not exist on the host.

## Updating content

This does not involve Docker at all.

```bash
# on the host, or over a network mount from your laptop
vim content/pages/about.md
```

Save. Reload the page. That is the entire procedure. No restart, no rebuild, no `docker compose`
anything.

Mounting the content directory from your own machine over SSHFS makes this comfortable:

```bash
sshfs <deploy-host>:/home/<user>/personal-page/content ~/mnt/site-content
```

The mount is read-only from the container's side, which is intentional: the application only
reads, and authoring happens on the host.

## Updating code

```bash
git pull
docker compose up -d --build
```

The content mount is untouched, so nothing you have written is at risk.

## Publishing to a registry

The usual flow: build multi-arch on your own machine, push to a registry, pull on the host.
That keeps the .NET SDK — and the build — off the deployment host entirely.

```bash
docker login -u <your-registry-user>
echo "IMAGE_NAME=<your-registry-user>/personal-page" >> .env
./scripts/publish-images.sh v0.1.0
```

The script builds `linux/amd64,linux/arm64`, tags both the version and `latest`, attaches OCI
source/version/revision labels, and pushes. It refuses to run on a dirty working tree, because
a published tag should map to a commit.

The registry namespace is never hardcoded in this repository — it comes from
`DOCKERHUB_NAMESPACE` or from the gitignored `.env`, so a fork does not inherit somebody
else's account.

**No QEMU is involved.** The Dockerfile's build stage is pinned to `${BUILDPLATFORM}` and the
publish is portable IL with no `RuntimeIdentifier`, so the compile runs once, natively, and
serves both architectures. Only the `aspnet` runtime layer differs per arch. This is the
reason `PublishReadyToRun` is off — see the comment in the Dockerfile if you want it back.

Then on the deployment host, with only `compose.yaml`, a `.env` and `content/`:

```bash
docker compose pull && docker compose up -d
```

Upgrading later is the same two commands. Pin a release by setting `IMAGE_TAG=v0.1.0` in the
host's `.env` instead of tracking `latest`.

### Without a registry

Build for the target arch and ship the image over SSH:

```bash
docker buildx build --platform linux/arm64 -t personal-page:latest --load .
docker save personal-page:latest | ssh <deploy-host> docker load
```

Then start it on the host without rebuilding:

```bash
docker compose up -d --no-build
```

Building on the target host directly is the fallback when any of that misbehaves.

## What compose sets, and why

| Setting | Reason |
| --- | --- |
| `volumes: ./content:/app/content:ro` | The whole design. Read-only because the app only reads |
| `DOTNET_gcServer=0` | ASP.NET Core enables server GC when it sees multiple cores, which on a 4-core board costs substantially more resident memory for no benefit at these request volumes |
| `read_only: true` + `tmpfs: /tmp` | Nothing writes to the filesystem except the runtime's scratch space |
| `cap_drop: [ALL]` | The process needs no capabilities |
| `no-new-privileges:true` | Blocks setuid escalation |
| `mem_limit: 256m` | A runaway process cannot take the whole board down |
| `healthcheck` on `/healthz` | Fails when the mount is missing, not just when the process dies |

The image is published with `PublishReadyToRun=true`, which measurably improves startup on arm64.
Trimming is deliberately **off**: Blazor's reflection makes trimmed builds fail at runtime, on
one specific page, rather than at build time — the worst possible failure mode for a site nobody
is watching.

## Checking resource use

```bash
docker stats --no-stream personal-page
```

Confirm the resident memory figure is one you are happy with on your hardware rather than
trusting any default, including `DOTNET_gcServer=0`. If it sits close to `mem_limit`, raise the
limit rather than letting the OOM killer decide.

## Environment variables

| Variable | Default in the image | Notes |
| --- | --- | --- |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Change the container-side port |
| `Content__RootPath` | `/app/content` | Must match the mount target |
| `Content__ShowDrafts` | unset (false) | Leave it off in production |
| `Content__CollectionCacheSeconds` | `5` | Backstop staleness for collection listings |
| `Content__UsePollingFileWatcher` | `false` | Set to `true` if the content mount is itself a network filesystem inside the container |
| `DOTNET_gcServer` | `0` | Workstation GC |

Double underscores map to configuration sections: `Content__RootPath` is `Content:RootPath`.

## Troubleshooting

**The container is unhealthy.**

```bash
docker compose exec personal-page ls /app/content/pages
```

Empty or missing means the mount is wrong. Check that `./content` exists on the host and that
the path in `compose.yaml` matches.

**Content edits are not appearing.** Check you are editing the mounted directory and not a copy.
Confirm the mtime actually changed:

```bash
stat -c '%y %s %n' content/pages/about.md
```

If your editor writes without changing the modification time or the length, the cached copy is
kept. `touch` the file. This is a known, documented limitation of stat-based invalidation.

**A new post is missing from `/blog` but reachable at its own URL.** The collection listing is
waiting for its change token or its five-second backstop. If it never appears, the filesystem
watcher is not firing — set `Content__UsePollingFileWatcher=true`.

**A page renders but the title is wrong.** Front matter failed to parse. Open `/_diagnostics`,
which names the file and the error.

**Logs.**

```bash
docker compose logs -f personal-page
```

Content problems are logged once per file per modification, at warning level.

**Everything 404s.** The content root exists but has no `pages/` directory, or `RootPath` points
somewhere unexpected. `/_diagnostics` prints the resolved path it is actually reading from.

## Before going public

None of this is in scope for a private LAN, and all of it is required beyond one:

- TLS, via a reverse proxy holding the certificate
- A hardened `AllowedHosts` instead of `*`
- Rate limiting
- Rethinking the raw-HTML trust boundary described in [architecture.md](architecture.md) if the
  content root ever stops being yours alone
