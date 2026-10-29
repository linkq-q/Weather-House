"""Rasterize the simple white spell silhouette at the existing HUD asset scale."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
scale = 8
image = Image.new("RGBA", (44 * scale, 44 * scale))
draw = ImageDraw.Draw(image)
def polygon(points):
    draw.polygon([(round(x * scale), round(y * scale)) for x, y in points], fill="white")
def star(x, y, radius):
    inner = radius * .28
    polygon([(x, y-radius), (x+inner, y-inner), (x+radius, y), (x+inner, y+inner),
             (x, y+radius), (x-inner, y+inner), (x-radius, y), (x-inner, y-inner)])
draw.line([(8*scale, 36*scale), (27*scale, 17*scale)], fill="white", width=5*scale)
for x, y in [(8,36), (27,17)]:
    draw.ellipse(((x-2.5)*scale,(y-2.5)*scale,(x+2.5)*scale,(y+2.5)*scale), fill="white")
star(31, 9, 7)
star(14, 12, 4)
star(36, 27, 4)
target = root / "Assets/Game/Resources/HudIcons/SpellCast.png"
image.save(target)
assert image.getbbox() == (44, 16, 321, 309)
print(target)
