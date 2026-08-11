# Alpine on both stages: small, and unlike the chiseled images it still has a shell and wget,
# which the compose healthcheck needs. Both tags are multi-arch manifests, so this one file
# covers arm64 and amd64 with no conditionals.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# ReadyToRun compiles ahead of time, so it needs a concrete runtime identifier at both restore
# and publish, and the two have to agree. BuildKit supplies TARGETARCH; under `buildx
# --platform linux/arm64` this stage runs emulated, so the identifier is simply the local one.
ARG TARGETARCH=amd64
RUN case "$TARGETARCH" in \
      arm64) echo linux-musl-arm64 ;; \
      arm)   echo linux-musl-arm ;; \
      *)     echo linux-musl-x64 ;; \
    esac > /rid

# Restore first, against the project file alone, so editing a .cs file does not invalidate the
# NuGet layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PersonalPage.Web/PersonalPage.Web.csproj src/PersonalPage.Web/
# PublishReadyToRun has to be set here too: the crossgen2 runtime pack is a restore-time
# dependency, and publish fails without it.
RUN dotnet restore src/PersonalPage.Web/PersonalPage.Web.csproj \
    --runtime "$(cat /rid)" \
    -p:PublishReadyToRun=true

COPY src/ src/

# ReadyToRun measurably improves startup on arm64 and is worth the larger image. Not
# self-contained: the runtime image already has ASP.NET Core, so shipping a second copy would
# only make the image bigger.
#
# Trimming is deliberately off. Blazor's reflection makes trimmed builds fail at runtime, on one
# specific page, rather than at build time — the worst possible failure mode for a site nobody is
# watching.
RUN dotnet publish src/PersonalPage.Web/PersonalPage.Web.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish \
    --runtime "$(cat /rid)" \
    --self-contained false \
    -p:PublishReadyToRun=true \
    -p:PublishTrimmed=false


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
