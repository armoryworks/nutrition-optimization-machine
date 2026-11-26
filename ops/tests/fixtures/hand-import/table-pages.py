#!/usr/bin/env python3
"""
Makes the page images of table-pages.json: each page set like a scanned page of an old cookbook (a running head, a
title, an ingredient table with dot leaders and the amount in a right-hand column, the method in prose), in a serif
type at 300 dpi, turned a little, blurred and speckled like a scan, saved as JPEG. The images are made here, never
fetched; the text is NCI's (U.S. government text, public domain). Needs Pillow (HPND licence) and a serif TrueType
font (DejaVu Serif by default); only to make the fixtures again, never by the tests or the import.

Run: python3 ops/tests/fixtures/hand-import/table-pages.py [--font /path/to/serif.ttf]
"""

import argparse
import json
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
WIDTH, HEIGHT = 2550, 3300
MARGIN = 255
AMOUNT_AT = 1770
SIZE = 50
LINE = 75


def wrapped(draw, text, font, width):
    lines, line = [], ""
    for word in text.split():
        trial = f"{line} {word}".strip()
        if draw.textlength(trial, font=font) <= width:
            line = trial
        else:
            lines.append(line)
            line = word
    return lines + [line] if line else lines


def page_image(page, font, head_font, seed):
    image = Image.new("L", (WIDTH, HEIGHT), 246)
    draw = ImageDraw.Draw(image)
    y = 180
    draw.text((MARGIN, y), page["head"], font=head_font, fill=30)
    y += LINE * 2
    dot = draw.textlength(" .", font=font)
    for recipe in page["recipes"]:
        title_width = draw.textlength(recipe["title"], font=font)
        draw.text(((WIDTH - title_width) / 2, y), recipe["title"], font=font, fill=20)
        y += LINE * 2
        for name, amount in recipe["rows"]:
            draw.text((MARGIN + 60, y), name, font=font, fill=20)
            x = MARGIN + 60 + draw.textlength(name, font=font)
            dots = int((AMOUNT_AT - 30 - x) // dot)
            draw.text((x, y), " ." * dots, font=font, fill=20)
            draw.text((AMOUNT_AT, y), amount, font=font, fill=20)
            y += LINE
        y += LINE // 2
        for paragraph in recipe["method"]:
            for number, line in enumerate(wrapped(draw, paragraph, font, WIDTH - 2 * MARGIN - 60)):
                draw.text((MARGIN + (60 if number == 0 else 0), y), line, font=font, fill=20)
                y += LINE
        y += LINE
    rng = random.Random(seed)
    for _ in range(4000):
        draw.point((rng.randrange(WIDTH), rng.randrange(HEIGHT)), fill=rng.randrange(120, 200))
    image = image.rotate(0.4, resample=Image.BICUBIC, fillcolor=246).filter(ImageFilter.GaussianBlur(0.8))
    return image


def main():
    parser = argparse.ArgumentParser(description="Makes the hand-import table page images from table-pages.json.")
    parser.add_argument("--font", default="/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf")
    args = parser.parse_args()
    with open(os.path.join(HERE, "table-pages.json"), encoding="utf-8") as handle:
        spec = json.load(handle)
    font = ImageFont.truetype(args.font, SIZE)
    head_font = ImageFont.truetype(args.font, SIZE - 6)
    for number, page in enumerate(spec["pages"], start=1):
        page_image(page, font, head_font, number).save(os.path.join(HERE, page["file"]), "JPEG", quality=80)


if __name__ == "__main__":
    main()
