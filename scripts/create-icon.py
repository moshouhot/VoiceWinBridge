"""Regenerate the original app artwork (optional developer tool: Pillow)."""

from pathlib import Path

from PIL import Image, ImageDraw


assets = Path(__file__).resolve().parents[1] / "assets"
assets.mkdir(exist_ok=True)
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)


def box(values):
    return tuple(round(value * scale) for value in values)


def line(points, fill, width):
    draw.line([box(point) for point in points], fill=fill, width=width * scale, joint="curve")
    for point in (points[0], points[-1]):
        x, y = point
        radius = width / 2
        draw.ellipse(box((x - radius, y - radius, x + radius, y + radius)), fill=fill)


dark, mint, white = "#0B2330", "#57E0C4", "#F4FAF9"
draw.rounded_rectangle(box((8, 8, 248, 248)), radius=56 * scale, fill=dark)
draw.rounded_rectangle(box((98, 42, 158, 140)), radius=30 * scale, fill=mint)
draw.arc(box((76, 74, 180, 178)), 0, 180, fill=white, width=14 * scale)
line([(76, 110), (76, 126)], white, 14)
line([(180, 110), (180, 126)], white, 14)
line([(128, 177), (128, 210)], white, 14)
line([(104, 210), (152, 210)], white, 14)
draw.rounded_rectangle(box((175, 174, 239, 238)), radius=17 * scale, fill=mint)
line([(216, 191), (216, 211), (193, 211)], dark, 7)
line([(201, 203), (193, 211), (201, 219)], dark, 7)

image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(assets / "VoiceWinBridge.png")
image.save(assets / "VoiceWinBridge.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
(assets / "VoiceWinBridge.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect x="8" y="8" width="240" height="240" rx="56" fill="#0B2330"/>
  <rect x="98" y="42" width="60" height="98" rx="30" fill="#57E0C4"/>
  <g fill="none" stroke="#F4FAF9" stroke-width="14" stroke-linecap="round">
    <path d="M76 110v16a52 52 0 0 0 104 0v-16M128 178v32m-24 0h48"/>
  </g>
  <rect x="175" y="174" width="64" height="64" rx="17" fill="#57E0C4"/>
  <path d="M216 191v20h-23m8-8-8 8 8 8" fill="none" stroke="#0B2330" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
''', encoding="utf-8", newline="\n")
print("Generated original SVG, PNG and multi-size ICO in assets/.")
