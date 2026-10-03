#!/usr/bin/env python3
"""Snapshot Windows layout facts; --check prevents platform data from drifting."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DEST = ROOT / "apps/macos/Sources/KeyShadowCore/Resources/windows-pinyin.json"
THEMES = DEST.with_name("themes.json")


def snapshot():
    source = (ROOT / "apps/windows/src/PinyinScheme.cs").read_text()
    pattern = r'new PinyinScheme\("(.*?)", "(.*?)", "(.*?)", ZeroStyle\.(\w+),\s*((?:"[^"\n]*"\s*\+?\s*)+),\s*"(.*?)",\s*"(.*?)",\s*"(.*?)"\)'
    schemes = []
    for match in re.finditer(pattern, source):
        ident, name, initials, zero, layout, hint, umlaut, example = match.groups()
        schemes.append(dict(id=ident, name=name, initials=initials, zero=zero,
                            layout="".join(re.findall(r'"(.*?)"', layout)),
                            hints=[hint, umlaut, example]))
    assert len(schemes) == 4, "Windows scheme declarations changed; update the extractor"
    guide = (ROOT / "apps/windows/src/PinyinGuide.cs").read_text()
    inventory = guide.split("internal const string Syllables =", 1)[1].split(";", 1)[0]
    syllables = "".join(re.findall(r'"(.*?)"', inventory)).split()
    assert len(set(syllables)) == len(syllables) == 411
    return json.dumps(dict(schemes=schemes, syllables=syllables), ensure_ascii=False, indent=2) + "\n"


def themes_snapshot():
    source = (ROOT / "apps/windows/src/KeyboardTheme.cs").read_text()
    themes = []
    for block in source.split("new KeyboardTheme {")[1:]:
        ident, name = re.search(r'Id = "(.*?)", Name = "(.*?)"', block).groups()
        colors = re.findall(r'(\w+) = C\(0x([A-F0-9]+)\)', block)
        themes.append(dict(id=ident, name=name, colors={key[0].lower() + key[1:]: value for key, value in colors}))
    assert len(themes) == 4
    return json.dumps(themes, ensure_ascii=False, indent=2) + "\n"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for destination, expected in [(DEST, snapshot()), (THEMES, themes_snapshot())]:
        if args.check:
            if not destination.exists() or destination.read_text() != expected:
                raise SystemExit("Data differs from Windows. Run apps/macos/tools/sync-pinyin.py")
        else:
            destination.write_text(expected)
    if args.check:
        print("PASS: four layouts, 411 syllables and four themes match Windows")
