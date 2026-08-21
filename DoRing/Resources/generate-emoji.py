"""Regenerates DoRing/Resources/emoji.txt from the Unicode database.

Emits "CODEPOINT Name" lines for single-codepoint emoji, ordered the way a
picker reads best: faces first, then people and objects, with the older
symbol blocks last. Sequences (flags, skin tones, families) are left out -
they need more than one codepoint, which the picker does not model.

Usage, from the repository root:  python DoRing/Resources/generate-emoji.py
"""

import unicodedata

# In picker order, not codepoint order.
BLOCKS = [
    (0x1F600, 0x1F64F),  # emoticons
    (0x1F900, 0x1F9FF),  # supplemental symbols and pictographs
    (0x1FA70, 0x1FAFF),  # symbols and pictographs extended-A
    (0x1F300, 0x1F5FF),  # miscellaneous symbols and pictographs
    (0x1F680, 0x1F6FF),  # transport and map
    (0x2600, 0x27BF),    # miscellaneous symbols and dingbats
    (0x2B00, 0x2BFF),    # miscellaneous symbols and arrows
    (0x231A, 0x231B), (0x23E9, 0x23FA), (0x203C, 0x203C), (0x2049, 0x2049),
]
SKIN_TONES = range(0x1F3FB, 0x1F400)

OUT = "DoRing/Resources/emoji.txt"


def main():
    rows = []
    for low, high in BLOCKS:
        for code_point in range(low, high + 1):
            if code_point in SKIN_TONES:
                continue
            try:
                name = unicodedata.name(chr(code_point))
            except ValueError:
                continue
            rows.append((code_point, name.title()))

    with open(OUT, "w", encoding="utf-8") as out:
        for code_point, name in rows:
            out.write("%05X %s\n" % (code_point, name))
    print(f"{OUT}: {len(rows)} emoji")


if __name__ == "__main__":
    main()
