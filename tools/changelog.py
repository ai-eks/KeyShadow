#!/usr/bin/env python3
"""Validate docs/changelog.json and render GitHub release notes from it.

The website (keyshadow.aix.uy) reads the same file at each release tag, so every
entry needs Chinese and English copy.

  python3 tools/changelog.py check            # schema, order, and an entry for VERSION
  python3 tools/changelog.py notes 0.2.0      # Markdown release notes for one version
"""
import datetime
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CHANGELOG = REPO / "docs" / "changelog.json"
VERSION_FILE = REPO / "VERSION"
VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+$")
LANGUAGES = ("zh", "en")
PACKAGE_NOTE = (
    "macOS: Developer ID signed and Apple notarized universal DMG. "
    "Windows: unsigned standalone EXE. SHA256 checksums are included."
)


def fail(message):
    print(f"changelog: {message}", file=sys.stderr)
    sys.exit(1)


def version_key(version):
    return tuple(int(part) for part in version.split("."))


def load():
    try:
        data = json.loads(CHANGELOG.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read {CHANGELOG.relative_to(REPO)}: {error}")
    releases = data.get("releases") if isinstance(data, dict) else None
    if not isinstance(releases, list) or not releases:
        fail('expected {"releases": [...]} with at least one entry')

    previous = None
    for index, release in enumerate(releases):
        where = f"releases[{index}]"
        if not isinstance(release, dict):
            fail(f"{where} must be an object")
        version = release.get("version")
        if not isinstance(version, str) or not VERSION_PATTERN.match(version):
            fail(f"{where}.version must look like 1.2.3")
        try:
            datetime.date.fromisoformat(release.get("date", ""))
        except (TypeError, ValueError):
            fail(f"{where}.date must be YYYY-MM-DD")
        for language in LANGUAGES:
            copy = release.get(language)
            if not isinstance(copy, dict):
                fail(f"{where}.{language} is required")
            if not isinstance(copy.get("title"), str) or not copy["title"].strip():
                fail(f"{where}.{language}.title must be a non-empty string")
            items = copy.get("items")
            if not isinstance(items, list) or not items:
                fail(f"{where}.{language}.items must be a non-empty list")
            if not all(isinstance(item, str) and item.strip() for item in items):
                fail(f"{where}.{language}.items must contain non-empty strings")
        # Newest first, so the website and release notes can take the head of the list.
        if previous and version_key(version) >= version_key(previous):
            fail(f"{where}.version {version} must be older than {previous}")
        previous = version
    return releases


def find(releases, version):
    return next((release for release in releases if release["version"] == version), None)


def check(releases):
    version = VERSION_FILE.read_text(encoding="utf-8").strip()
    if not find(releases, version):
        fail(f"add an entry for VERSION {version} at the top of {CHANGELOG.relative_to(REPO)}")
    print(f"changelog: {len(releases)} release(s) valid; VERSION {version} documented")


def notes(releases, version):
    release = find(releases, version.lstrip("v"))
    if not release:
        fail(f"no entry for {version}")
    sections = []
    for language in LANGUAGES:
        copy = release[language]
        lines = [f"## {copy['title']}", ""] + [f"- {item}" for item in copy["items"]]
        sections.append("\n".join(lines))
    print("\n\n".join(sections + [PACKAGE_NOTE]))


def main(argv):
    releases = load()
    if argv[:1] == ["check"] and len(argv) == 1:
        check(releases)
    elif argv[:1] == ["notes"] and len(argv) == 2:
        notes(releases, argv[1])
    else:
        fail("usage: changelog.py check | changelog.py notes <version>")


if __name__ == "__main__":
    main(sys.argv[1:])
