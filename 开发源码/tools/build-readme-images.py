"""Annotate freshly captured fictional examples for the public README."""

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
RAW = ROOT / "开发源码" / "qa" / "使用说明" / "原始截图"
FIG = ROOT / "docs" / "images"
FIG.mkdir(parents=True, exist_ok=True)
REGIONS = json.loads((RAW / "regions.json").read_text(encoding="utf-8"))
NUMBER_FONT = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 20)
ACCENT = "#C84632"


def annotated(source, name, crop=None, marks=(), padding=30):
    with Image.open(RAW / f"{source}.png") as original:
        original = original.convert("RGBA")
        image = Image.new("RGB", original.size, "white")
        image.paste(original, mask=original.getchannel("A"))
    bounds = crop or (0, 0, image.width, image.height)
    fragment = image.crop(bounds)
    canvas = Image.new("RGB", (fragment.width + 2 * padding, fragment.height + 2 * padding), "white")
    canvas.paste(fragment, (padding, padding))
    draw = ImageDraw.Draw(canvas)
    for number, rect, side in marks:
        x1, y1, x2, y2 = rect
        x1, x2 = x1 - bounds[0] + padding, x2 - bounds[0] + padding
        y1, y2 = y1 - bounds[1] + padding, y2 - bounds[1] + padding
        draw.rounded_rectangle((x1 - 2, y1 - 2, x2 + 2, y2 + 2), radius=4, outline=ACCENT, width=3)
        if side == "left":
            cx, cy = x1 - 17, (y1 + y2) / 2
        elif side == "right":
            cx, cy = x2 + 17, (y1 + y2) / 2
        else:
            cx, cy = (x1 + x2) / 2, y1 - 17
        draw.ellipse((cx - 13, cy - 13, cx + 13, cy + 13), fill=ACCENT)
        draw.text((cx, cy), str(number), font=NUMBER_FONT, fill="white", anchor="mm")
    canvas.save(FIG / name)


def rect(source, control):
    box = REGIONS[source][control]
    return (box["X"], box["Y"], box["X"] + box["Width"], box["Y"] + box["Height"])


annotated("01_fill_start", "01-name-quota.png", (12, 67, 853, 686), [
    (1, rect("01_fill_start", "name"), "left"),
    (2, (720, 98, 841, 137), "right"),
])
annotated("02_fill_choices", "02-preferences.png", (20, 140, 851, 640), [
    (1, (26, 156, 132, 192), "left"),
    (2, (139, 156, 245, 192), "right"),
    (3, (26, 609, 216, 628), "right"),
])
annotated("03_fill_code", "03-copy-code.png", (867, 75, 1162, 484), [
    (1, (891, 382, 1144, 419), "left"),
    (2, rect("03_fill_code", "copy"), "left"),
])
annotated("04_import", "04-import.png", marks=[
    (1, rect("04_import", "input"), "left"),
    (2, (481, 623, 561, 659), "top"),
    (3, rect("04_import", "accept"), "top"),
])

with Image.open(RAW / "05_ready.png") as source:
    ready = source.convert("RGB")
toolbar = Image.new("RGB", (830, 130), "white")
toolbar.paste(ready.crop((16, 64, 447, 114)), (20, 50))
toolbar.paste(ready.crop((1108, 64, 1440, 114)), (478, 50))
draw = ImageDraw.Draw(toolbar)
for number, box in [(1, (319, 57, 438, 93)), (2, (648, 57, 756, 93))]:
    draw.rounded_rectangle(box, radius=4, outline=ACCENT, width=3)
    cx, cy = (box[0] + box[2]) / 2, 29
    draw.ellipse((cx - 13, cy - 13, cx + 13, cy + 13), fill=ACCENT)
    draw.text((cx, cy), str(number), font=NUMBER_FONT, fill="white", anchor="mm")
toolbar.save(FIG / "05-confirm-solve.png")
annotated("05_ready", "06-roster.png", (15, 124, 291, 552), [
    (3, (34, 220, 179, 256), "right"),
])
annotated("06_schedule", "07-schedule.png", (300, 152, 1166, 628), padding=6)
annotated("08_candidates", "08-manual-edit.png", padding=10)
annotated("10_issues", "09-warnings.png", (14, 15, 451, 144), padding=8)
annotated("06_schedule", "10-export.png", (1258, 8, 1448, 64), [
    (1, (1266, 18, 1309, 54), "top"),
    (2, rect("06_schedule", "export"), "top"),
], padding=34)
print("README_IMAGES=10")
