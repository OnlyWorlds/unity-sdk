"""Render the 22 element-type icons the World Browser draws, from Google's Material Symbols.

    python tools/render_type_icons.py FONT.ttf FONT.codepoints

FONT is the Material Symbols Outlined variable font and its codepoints file, from
github.com/google/material-design-icons (variablefont/), Apache License 2.0. The icon NAME per
type comes from the package's own Runtime/Resources/ow-presentation.json, so this script never
decides which icon a type gets; it only draws the one already named there.

Each icon is a white glyph on transparent, 32 x 32 (a 16 px editor icon at 2x), so the window
can tint it. Output: Packages/com.onlyworlds.sdk/Editor/Icons/<type>.png. Pillow draws the
font's default instance (weight 400, unfilled).
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

SIZE = 32
PKG = os.path.join(os.path.dirname(__file__), "..", "Packages", "com.onlyworlds.sdk")


def main(font_path, codepoints_path):
    with open(os.path.join(PKG, "Runtime", "Resources", "ow-presentation.json"), encoding="utf-8") as f:
        types = json.load(f)["types"]
    with open(codepoints_path, encoding="utf-8") as f:
        codepoints = dict(line.split() for line in f if line.strip())

    out = os.path.join(PKG, "Editor", "Icons")
    os.makedirs(out, exist_ok=True)
    font = ImageFont.truetype(font_path, SIZE)

    for type_, entry in sorted(types.items()):
        glyph = chr(int(codepoints[entry["icon"]], 16))
        im = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))
        draw = ImageDraw.Draw(im)
        left, top, right, bottom = draw.textbbox((0, 0), glyph, font=font)
        draw.text(((SIZE - (right - left)) / 2 - left, (SIZE - (bottom - top)) / 2 - top), glyph,
                  font=font, fill=(255, 255, 255, 255))
        im.save(os.path.join(out, type_ + ".png"), optimize=True)
        print(f"{type_:12} {entry['icon']}")


if __name__ == "__main__":
    main(*sys.argv[1:3])
