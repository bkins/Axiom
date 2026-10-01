"""Inspect the retained native PDF fixture and render every page for visual QA.

Requires pypdf, pdfplumber, Pillow and Poppler (provided by the Codex runtime).
Usage: python Verify-PdfExportEvidence.py <evidence-directory>
"""
import json
import pathlib
import re
import subprocess
import sys

import pdfplumber
from PIL import Image, ImageDraw
from pypdf import PdfReader

root = pathlib.Path(sys.argv[1]).resolve()
summary = {}
for name, dimensions in {"Letter": (612, 792), "A4": (595.28, 841.89)}.items():
    source = root / (name + ".pdf")
    reader = PdfReader(source)
    texts = [page.extract_text() or "" for page in reader.pages]
    text = "\n".join(texts)
    assert len(reader.pages) > 2, "Fixture did not paginate"
    for page in reader.pages:
        assert abs(float(page.mediabox.width) - dimensions[0]) < 1
        assert abs(float(page.mediabox.height) - dimensions[1]) < 1
        for annotation in page.get("/Annots", []):
            action = annotation.get_object().get("/A", {})
            if action.get("/S") == "/URI":
                assert str(action["/URI"]).startswith(("https:", "http:", "mailto:")), action
        assert "/AA" not in page
    for required in ["Café", "日本語", "一", "Ελληνικά", "Привет", "Final fixture marker", "Missing image", "End of long code"]:
        assert required in text, required
    assert "documentId" not in text
    assert "/JavaScript" not in reader.trailer["/Root"].get("/Names", {})
    assert "/OpenAction" not in reader.trailer["/Root"]
    for index in range(1, 101):
        assert re.search(rf"Table row {index}(?!\d)", text), f"Missing table row {index}"
    for index in range(1, 901):
        assert re.search(rf"TallToken{index}(?!\d)", text), f"Missing oversized-row token {index}"
    for index in range(1, 46):
        assert re.search(rf"Section {index}(?!\d)", text), f"Missing section {index}"
    for number, page_text in enumerate(texts, 1):
        assert f"Page {number} of {len(texts)}" in page_text, f"Missing page number {number}"
        if re.search(r"Table row \d", page_text):
            assert "Header A" in page_text and "Header B" in page_text, "Missing repeated table header"
    with pdfplumber.open(source) as document:
        image_count = sum(len(page.images) for page in document.pages)
        assert image_count > 0, "Embedded local image missing"
        outside = []
        for number, page in enumerate(document.pages, 1):
            for char in page.chars:
                if char["x0"] < -1 or char["x1"] > page.width + 1 or char["top"] < -1 or char["bottom"] > page.height + 1:
                    outside.append((number, char["text"]))
        assert not outside, f"Text outside page bounds: {outside[:10]}"
    (root / (name + ".txt")).write_text(text, encoding="utf-8")
    render = root / (name + "-pages")
    render.mkdir(exist_ok=True)
    subprocess.run(["pdftoppm", "-scale-to", "900", "-png", str(source), str(render / "page")], check=True, capture_output=True)
    pages = sorted(render.glob("page-*.png"))
    assert len(pages) == len(reader.pages)
    for start in range(0, len(pages), 12):
        sheet = Image.new("RGB", (1200, 1700), "#d9dfe5")
        draw = ImageDraw.Draw(sheet)
        for offset, path in enumerate(pages[start:start + 12]):
            tile = Image.open(path).convert("RGB")
            assert all(channel > 245 for channel in tile.getpixel((5, 5))), "PDF page margin is not neutral white"
            tile.thumbnail((290, 385))
            column, row = offset % 4, offset // 4
            x, y = column * 300 + (300 - tile.width) // 2, row * 420 + 25
            sheet.paste(tile, (x, y))
            draw.text((column * 300 + 12, row * 420 + 5), f"{name} page {start + offset + 1}", fill="black")
        sheet.save(root / f"{name}-contact-{start // 12 + 1}.png")
    summary[name] = {"Pages": len(reader.pages), "PagePoints": dimensions, "EmbeddedImages": image_count, "AllContentPresent": True, "PageBoundsPassed": True, "AllPagesRendered": True}
(root / "pdf-verification.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
