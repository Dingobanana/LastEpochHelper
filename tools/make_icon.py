"""Draws the program's icon (src/LastEpochHelper/app.ico) in the overlay's own look: the dark plate,
the bronze edge that catches the light at the top, and "LEH" in gold in the title face.

Run:  python tools/make_icon.py        (needs Pillow, and Palatino Linotype from Windows' fonts)
"""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "LastEpochHelper", "app.ico")
PREVIEW = os.path.join(ROOT, "tools", "app-icon-preview.png")
FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
FONT = os.path.join(FONTS, "palab.ttf")          # Palatino Linotype Bold, the title face
SMALL_FONT = os.path.join(FONTS, "seguibl.ttf")  # Segoe UI Black: serifs vanish below 32 px, weight does not
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

PLATE_TOP, PLATE_MID, PLATE_BOTTOM = (0x1E, 0x20, 0x2B), (0x12, 0x13, 0x19), (0x0A, 0x0B, 0x0F)
EDGE_TOP, EDGE_MID, EDGE_BOTTOM = (0xC9, 0xA8, 0x5C), (0x7A, 0x65, 0x38), (0x3A, 0x30, 0x1A)
GOLD_LIGHT, GOLD = (0xF0, 0xD9, 0x9C), (0xC9, 0xA8, 0x5C)


def blend(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def vertical(size, top, mid, bottom, split=0.35):
    image = Image.new("RGB", (size, size))
    draw = ImageDraw.Draw(image)
    for y in range(size):
        t = y / max(1, size - 1)
        colour = blend(top, mid, t / split) if t < split else blend(mid, bottom, (t - split) / (1 - split))
        draw.line([(0, y), (size, y)], fill=colour)
    return image


def draw(size):
    scale = 8 if size < 64 else 4                      # draw large, shrink: smooth edges at every size
    big = size * scale
    radius = round(big * 0.22)
    small = size <= 24
    edge = max(scale, round(big * (0.055 if small else 0.045)))

    rounded = Image.new("L", (big, big), 0)
    ImageDraw.Draw(rounded).rounded_rectangle([0, 0, big - 1, big - 1], radius=radius, fill=255)
    inner = Image.new("L", (big, big), 0)
    ImageDraw.Draw(inner).rounded_rectangle([edge, edge, big - 1 - edge, big - 1 - edge], radius=max(1, radius - edge), fill=255)

    icon = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    icon.paste(vertical(big, EDGE_TOP, EDGE_MID, EDGE_BOTTOM, 0.4), (0, 0), rounded)
    icon.paste(vertical(big, PLATE_TOP, PLATE_MID, PLATE_BOTTOM), (0, 0), inner)

    # "LEH", as wide as the plate allows, a shade lighter at the top like the panels' gold.
    text = "LEH"
    face = SMALL_FONT if small else FONT
    room = big - 2 * edge - round(big * (0.06 if small else 0.16))
    font_size = big
    font = ImageFont.truetype(face, font_size)
    while font_size > 4:
        font = ImageFont.truetype(face, font_size)
        left, top, right, bottom = font.getbbox(text)
        if right - left <= room and bottom - top <= big * (0.56 if small else 0.5):
            break
        font_size -= max(1, font_size // 40)
    left, top, right, bottom = font.getbbox(text)
    x = (big - (right - left)) / 2 - left
    y = (big - (bottom - top)) / 2 - top + big * 0.01
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).text((x, y), text, font=font, fill=255)
    if size >= 64:  # a soft dark glow keeps the letters off the plate
        shadow = mask.filter(ImageFilter.GaussianBlur(big * 0.02)).point(lambda v: v * 0.6)
        icon.paste((0x05, 0x05, 0x08), (0, 0), shadow)
    gold = vertical(big, GOLD_LIGHT, GOLD, blend(GOLD, (0x8A, 0x70, 0x38), 1.0), 0.45)
    icon.paste(gold, (0, 0), mask)

    return icon.resize((size, size), Image.LANCZOS)


def main():
    images = [draw(size) for size in SIZES]
    images[-1].save(OUT, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    # A strip of every size, to look at.
    strip = Image.new("RGBA", (sum(SIZES) + 10 * len(SIZES), max(SIZES)), (40, 40, 40, 255))
    x = 0
    for size, image in zip(SIZES, images):
        strip.paste(image, (x, max(SIZES) - size), image)
        x += size + 10
    strip.save(PREVIEW)
    print("wrote", OUT, "and", PREVIEW)


if __name__ == "__main__":
    main()
