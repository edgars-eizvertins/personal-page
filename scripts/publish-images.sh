#!/usr/bin/env bash
# Build and push the site image to Docker Hub for amd64 + arm64, so the same tag runs on a
# laptop and on a Raspberry Pi.
#
#   ./scripts/publish-images.sh v0.1.0
#
# Requires: docker buildx (sudo apt install docker-buildx) and a prior
#   docker login -u <namespace>
#
# No QEMU needed — the .NET compile is a portable, RID-less publish that runs on the native
# build host, and only the thin aspnet runtime layer differs per architecture. See the
# comments in the Dockerfile.
set -euo pipefail

VERSION="${1:?usage: publish-images.sh <version>   e.g. v0.1.0}"
IMAGE="${IMAGE:-personal-page}"
PLATFORMS="${PLATFORMS:-linux/amd64,linux/arm64}"
BUILDER="${BUILDER:-personal-page}"

cd "$(dirname "$0")/.."

# The registry namespace is a personal detail, and this repository is meant to be forked, so
# it is never hardcoded here. Take it from the environment, or from the gitignored .env that
# compose already reads.
if [[ -z "${DOCKERHUB_NAMESPACE:-}" && -f .env ]]; then
  # IMAGE_NAME=<namespace>/<image>
  env_image="$(sed -n 's/^[[:space:]]*IMAGE_NAME[[:space:]]*=[[:space:]]*//p' .env | tail -1)"
  if [[ "$env_image" == */* ]]; then
    DOCKERHUB_NAMESPACE="${env_image%%/*}"
    IMAGE="${env_image##*/}"
  fi
fi

if [[ -z "${DOCKERHUB_NAMESPACE:-}" ]]; then
  echo "error: set DOCKERHUB_NAMESPACE, or put IMAGE_NAME=<namespace>/<image> in .env" >&2
  exit 1
fi

# Refuse to label a dirty tree with a version tag — a published tag should map to a commit.
if [[ -n "$(git status --porcelain)" ]]; then
  echo "error: working tree is dirty; commit or stash before publishing $VERSION" >&2
  exit 1
fi

# Source URL for the OCI label comes from the git remote, not from the Docker Hub namespace —
# the two accounts can differ.
SOURCE_URL="$(git remote get-url origin 2>/dev/null \
  | sed -E 's#^git@([^:]+):#https://\1/#; s#\.git$##')"

# Multi-platform output needs the docker-container driver; the default 'docker' driver can
# only produce single-arch images. Creating the builder is idempotent.
docker buildx inspect "$BUILDER" >/dev/null 2>&1 \
  || docker buildx create --name "$BUILDER" --driver docker-container --bootstrap

echo "==> $DOCKERHUB_NAMESPACE/$IMAGE:$VERSION ($PLATFORMS)"
docker buildx build \
  --builder "$BUILDER" \
  --platform "$PLATFORMS" \
  --tag "$DOCKERHUB_NAMESPACE/$IMAGE:$VERSION" \
  --tag "$DOCKERHUB_NAMESPACE/$IMAGE:latest" \
  --label "org.opencontainers.image.source=$SOURCE_URL" \
  --label "org.opencontainers.image.version=$VERSION" \
  --label "org.opencontainers.image.revision=$(git rev-parse HEAD)" \
  --push \
  .

echo
echo "Pushed $DOCKERHUB_NAMESPACE/$IMAGE:$VERSION (and :latest)"
docker buildx imagetools inspect "$DOCKERHUB_NAMESPACE/$IMAGE:$VERSION" | head -20
