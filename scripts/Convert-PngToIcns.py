"""Convert the maintained icon into a macOS package resource without changing it."""
import pathlib
import sys

from PIL import Image

source = pathlib.Path(sys.argv[1]).resolve()
destination = pathlib.Path(sys.argv[2]).resolve()
if destination.exists() or source == destination:
    raise ValueError("Choose a fresh icon output path")
with Image.open(source) as original:
    image = original.convert("RGBA")
    image.save(destination, format="ICNS")
with Image.open(destination) as generated:
    generated.load()
    if generated.format != "ICNS" or generated.width < 256 or generated.height < 256:
        raise ValueError("The generated macOS icon is invalid")
