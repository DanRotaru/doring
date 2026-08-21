"""Regenerates the bundled Simple Icons assets from an upstream release.

Inputs (download from https://github.com/simple-icons/simple-icons-font/releases):
  SimpleIcons.woff2      -> converted to DoRing/Assets/SimpleIcons.ttf (WPF cannot read woff2)
  simple-icons.min.css   -> codepoint/slug/brand color map, embedded as simple-icons.txt

Usage (needs `pip install fonttools brotli`), from the repository root:
  python DoRing/Resources/generate-simple-icons.py SimpleIcons.woff2 simple-icons.min.css
"""

import re
import sys

from fontTools.ttLib import TTFont

# The stylesheet uses a handful of CSS color keywords instead of hex.
NAMED = {
    "red": "ff0000", "maroon": "800000", "grey": "808080",
    "orange": "ffa500", "navy": "000080", "teal": "008080", "green": "008000",
}

FONT_OUT = "DoRing/Assets/SimpleIcons.ttf"
MAP_OUT = "DoRing/Resources/simple-icons.txt"


def main(woff2_path, css_path):
    font = TTFont(woff2_path)
    font.flavor = None
    font.save(FONT_OUT)
    cmap = TTFont(FONT_OUT).getBestCmap()

    css = open(css_path, encoding="utf-8").read()
    codepoints = {
        m.group(1): int(m.group(2).lstrip(chr(92)), 16)
        for m in re.finditer(r'[.]si-([A-Za-z0-9_-]+)::before[{]content:"([^"]+)"[}]', css)
    }
    colors = {}
    for m in re.finditer(r'[.]si-([A-Za-z0-9_-]+)[.]si--color::before[{]color:([^;}]+)[}]', css):
        value = m.group(2).strip().lower()
        if value.startswith("#"):
            value = value[1:]
            value = "".join(c * 2 for c in value) if len(value) == 3 else value
        else:
            value = NAMED.get(value)
        if value and len(value) == 6:
            colors[m.group(1)] = value

    rows = sorted((code, slug) for slug, code in codepoints.items() if code in cmap)
    with open(MAP_OUT, "w", encoding="utf-8") as out:
        for code, slug in rows:
            out.write("%04X %s %s\n" % (code, colors.get(slug, "ffffff"), slug))
    print(f"{FONT_OUT}: {len(cmap)} glyphs, {MAP_OUT}: {len(rows)} icons")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
