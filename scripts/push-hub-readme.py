#!/usr/bin/env python3
"""Push the Docker Hub repository overview (the page people land on after finding the image)
from scripts/hub/<repo>.md.

    ./scripts/push-hub-readme.py

The {{COMPOSE}} placeholder in the markdown is replaced with the real contents of
docker-compose.hub.yml, so the instructions on Docker Hub can never drift from the file in
this repo.

Credentials are reused from `docker login` (~/.docker/config.json) — nothing is printed and
nothing is stored here. The registry namespace comes from DOCKERHUB_NAMESPACE or from the
gitignored .env, never from this file: this repository is meant to be forked, and somebody
else's account should not be baked into it.
"""
import base64
import json
import os
import pathlib
import re
import sys
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Shown in Hub search results. Hub caps this at 100 characters.
SHORT = "Markdown-driven developer portfolio. Edit a file, the site updates — no rebuild. amd64 + arm64."


def target_repo():
    """(namespace, image) from DOCKERHUB_NAMESPACE/IMAGE, or from IMAGE_NAME in .env."""
    namespace = os.environ.get("DOCKERHUB_NAMESPACE")
    image = os.environ.get("IMAGE", "personal-page")

    env_file = ROOT / ".env"
    if not namespace and env_file.exists():
        match = re.search(r"^\s*IMAGE_NAME\s*=\s*(\S+)\s*$", env_file.read_text(), re.M)
        if match and "/" in match.group(1):
            namespace, _, image = match.group(1).partition("/")

    if not namespace:
        sys.exit("set DOCKERHUB_NAMESPACE, or put IMAGE_NAME=<namespace>/<image> in .env")

    return namespace, image


def stored_credentials(namespace):
    """Username + PAT saved by `docker login`."""
    config = pathlib.Path.home() / ".docker/config.json"
    if not config.exists():
        sys.exit(f"no Docker credentials found — run: docker login -u {namespace}")

    auth = json.loads(config.read_text()).get("auths", {}).get(
        "https://index.docker.io/v1/", {}
    ).get("auth")
    if not auth:
        sys.exit(f"no Docker Hub credentials found — run: docker login -u {namespace}")

    user, _, secret = base64.b64decode(auth).decode().partition(":")
    return user, secret


def post(url, payload):
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request) as response:
        return json.load(response)


def hub_token(user, secret):
    """Exchange the PAT for an API token. Hub has two login endpoints depending on
    account/token type, so try the newer one and fall back to the older."""
    try:
        return "Bearer " + post(
            "https://hub.docker.com/v2/auth/token",
            {"identifier": user, "secret": secret},
        )["access_token"]
    except (urllib.error.HTTPError, KeyError):
        return "JWT " + post(
            "https://hub.docker.com/v2/users/login/",
            {"username": user, "password": secret},
        )["token"]


def main():
    namespace, image = target_repo()

    if len(SHORT) > 100:
        sys.exit(f"short description is {len(SHORT)} chars, Hub allows 100")

    compose = (ROOT / "docker-compose.hub.yml").read_text().rstrip()
    body = (ROOT / f"scripts/hub/{image}.md").read_text().replace("{{COMPOSE}}", compose)

    user, secret = stored_credentials(namespace)
    token = hub_token(user, secret)

    request = urllib.request.Request(
        f"https://hub.docker.com/v2/repositories/{namespace}/{image}/",
        data=json.dumps({"description": SHORT, "full_description": body}).encode(),
        headers={"Content-Type": "application/json", "Authorization": token},
        method="PATCH",
    )
    try:
        with urllib.request.urlopen(request) as response:
            response.read()
        print(f"updated https://hub.docker.com/r/{namespace}/{image}  ({len(body)} chars)")
    except urllib.error.HTTPError as e:
        sys.exit(f"{image}: HTTP {e.code} — {e.read().decode()[:300]}")


if __name__ == "__main__":
    main()
