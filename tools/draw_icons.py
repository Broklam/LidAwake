"""Regenerate the two tray icons (requires Pillow)."""
from pathlib import Path

from PIL import Image, ImageDraw


def draw_icon(awake):
    # Draw large, then let Pillow antialias at the standard Windows icon sizes.
    scale = 16
    image = Image.new("RGBA", (16 * scale, 16 * scale))
    pen = ImageDraw.Draw(image)

    def coords(*values):
        return tuple(round(value * scale) for value in values)

    def line(points, color, width):
        pen.line(coords(*points), fill=color, width=round(width * scale), joint="curve")

    dark = "#075941" if awake else "#303B4C"
    light = "#AAFFDA" if awake else "#F0F4FA"
    pen.rounded_rectangle(coords(.5, .5, 15.5, 15.5), radius=round(3.7 * scale), fill=dark)
    pen.rounded_rectangle(coords(3.5, 3.7, 12.5, 10.7), radius=round(1.05 * scale),
                          outline=light, width=round(1.35 * scale))
    pen.rounded_rectangle(coords(2.4, 11.3, 13.6, 12.9), radius=round(.75 * scale), fill=light)
    if awake:
        line((6.0, 7.0, 7.4, 8.35, 10.0, 5.95), light, 1.25)
    else:
        line((6.15, 7.25, 9.85, 7.25), "#CFD8E5", 1.25)
    return image


if __name__ == "__main__":
    target = Path(__file__).resolve().parent.parent / "assets"
    target.mkdir(exist_ok=True)
    for state, name in [(False, "off"), (True, "on")]:
        draw_icon(state).save(target / f"lid-{name}.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 48, 64, 256)],
                              bitmap_format="bmp")
