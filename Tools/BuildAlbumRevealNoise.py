"""Prepare a shared technical mask from RogueGrass's alpha-channel dissolve noise."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

source = Path(r"C:\unity project\RogueGrass\Assets\Art\Sprites\噪声.png")
output = Path(__file__).resolve().parents[1] / "Assets/WeatherHousePrototype/Resources/AlbumBook/RevealNoise.png"
# Preserve the reference's branching texture. A little fine grain breaks its large
# flat alpha=255 areas so content does not all pop on at the last threshold.
alpha = Image.open(source).getchannel("A").resize((512, 512), Image.Resampling.LANCZOS)
field = np.asarray(alpha.filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255
rng = np.random.default_rng(2102026)
grain = Image.fromarray(rng.integers(0, 256, (128, 128), dtype=np.uint8)).resize((512, 512), Image.Resampling.BICUBIC)
field = field * .85 + np.asarray(grain, dtype=np.float32) / 255 * .15
# Uniform threshold occupancy gives the user a steady reveal-duration parameter.
values, counts = np.unique(field, return_counts=True)
cdf = (np.cumsum(counts) - counts * .5) / field.size
mask = np.interp(field, values, cdf)
Image.fromarray(np.uint8(np.clip(mask * .94 + .03, 0, 1) * 255)).convert("RGB").save(output)
print(output)
