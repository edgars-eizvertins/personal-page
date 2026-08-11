# Alpine on both stages: small, and unlike the chiseled images it still has a shell and wget,
# which the compose healthcheck needs. Both tags are multi-arch manifests, so this one file
# covers arm64 and amd64 with no conditionals.

# ---- Build stage ----
# Pinned to the *build* host's architecture, never the target's. A framework-dependent,
# RID-less publish is pure IL and therefore architecture-independent, so one native build
# serves both the amd64 and the arm64 image, and `buildx --platform linux/amd64,linux/arm64`
# never puts the expensive .NET compile through QEMU. Only the thin runtime stage below
# varies per architecture.
#
# BUILDPLATFORM is set by BuildKit/buildx; the fallback keeps a plain `docker build` on the
# legacy builder working.
FROM --platform=${BUILDPLATFORM:-linux/amd64} mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore first, against the project file alone, so editing a .cs file does not invalidate the
# NuGet layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PersonalPage.Web/PersonalPage.Web.csproj src/PersonalPage.Web/
RUN dotnet restore src/PersonalPage.Web/PersonalPage.Web.csproj

COPY src/ src/

# No RuntimeIdentifier and no ReadyToRun, both deliberate.
#
# R2R pins the output to one architecture, which drags the build stage onto the target's
# platform — and under buildx that means an emulated .NET compile for every non-native arch,
# turning the arm64 leg into minutes of QEMU. What R2R buys is faster *cold start*, and this
# container runs with `restart: unless-stopped`, so it starts about as often as the host
# reboots. A portable publish is the better trade. If you ever want R2R back, build
# single-arch on the target host and add `-r linux-musl-arm64 -p:PublishReadyToRun=true`
# here and on the restore above.
#
# Trimming stays off regardless. Blazor's reflection makes trimmed builds fail at runtime, on
# one specific page, rather than at build time — the worst possible failure mode for a site
# nobody is watching.
RUN dotnet publish src/PersonalPage.Web/PersonalPage.Web.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish \
    -p:PublishTrimmed=false


# ---- Runtime stage ----
# This stage *does* vary per target architecture. The tag is a multi-arch manifest, so buildx
# pulls the right one for each --platform.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Content__RootPath=/app/content \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./

# content/ is never baked in — it arrives as a bind mount. The directory is created so the
# container still starts (and the health check still reports the problem clearly) when the
# mount is missing.
RUN mkdir -p /app/content

# The base image ships a non-root user; APP_UID is its numeric id.
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "PersonalPage.Web.dll"]
