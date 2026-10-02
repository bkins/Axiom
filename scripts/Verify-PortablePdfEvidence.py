"""Verify offline renderer output; retain every page and report font extraction limits."""
import json
import pathlib
import re
import subprocess
import sys
import unicodedata

import pdfplumber
from PIL import Image, ImageDraw
from pypdf import PdfReader

root = pathlib.Path(sys.argv[1]).resolve()
summary = {}
for name, dimensions in {"Letter": (612, 792), "A4": (595.276, 841.89)}.items():
    source = root / (name + ".pdf")
    reader = PdfReader(source)
    text = "\n".join(page.extract_text() or "" for page in reader.pages)
    normalized = unicodedata.normalize("NFKC", text)
    assert len(reader.pages) > 2
    assert "documentId" not in text
    assert "/OpenAction" not in reader.trailer["/Root"]
    assert "/JavaScript" not in reader.trailer["/Root"].get("/Names", {})
    for required in ["Café", "日本語", "一", "Ελληνικά", "Привет", "Final fixture marker", "Missing image", "End of long code"]:
        assert required in normalized, required
    for index in range(1, 101):
        assert re.search(rf"Table row {index}(?!\d)", text), index
    for index in range(1, 901):
        assert re.search(rf"TallToken{index}(?!\d)", text), index
    for index in range(1, 46):
        assert re.search(rf"Section {index}(?!\d)", text), index
    for number, page in enumerate(reader.pages, 1):
        assert abs(float(page.mediabox.width) - dimensions[0]) < 1
        assert abs(float(page.mediabox.height) - dimensions[1]) < 1
        page_text = page.extract_text() or ""
        assert f"Page {number}" in page_text
        if re.search(r"Table row \d", page_text):
            assert "Header A" in page_text and "Header B" in page_text
        for annotation in page.get("/Annots", []):
            action = annotation.get_object().get("/A", {})
            if action.get("/S") == "/URI":
                assert str(action["/URI"]).startswith(("http:", "https:", "mailto:"))
    with pdfplumber.open(source) as document:
        image_count = sum(len(page.images) for page in document.pages)
        assert image_count > 0
        outside = [(index + 1, char["text"]) for index, page in enumerate(document.pages) for char in page.chars
                   if char["x0"] < -1 or char["x1"] > page.width + 1 or char["top"] < -1 or char["bottom"] > page.height + 1]
        assert not outside, outside[:10]
    render = root / (name + "-pages")
    render.mkdir(exist_ok=True)
    subprocess.run(["pdftoppm", "-scale-to", "900", "-png", str(source), str(render / "page")], check=True, capture_output=True)
    pages = sorted(render.glob("page-*.png"))
    contact = Image.new("RGB", (800, ((len(pages) + 3) // 4) * 285), "#dddddd")
    draw = ImageDraw.Draw(contact)
    for index, path in enumerate(pages):
        with Image.open(path) as image:
            image.thumbnail((190, 260))
            x, y = (index % 4) * 200, (index // 4) * 285
            contact.paste(image, (x, y))
            draw.text((x, y + 262), path.stem, fill="black")
    contact.save(root / (name + "-contact.png"))
    (root / (name + ".txt")).write_text(text, encoding="utf-8")
    summary[name] = {"Pages": len(pages), "EmbeddedImages": image_count,
                     "GeometryContentPaginationLinksAndBoundsVerified": True,
                     "JapaneseExtractsExactly": "日本語" in text and "一" in text,
                     "JapanesePreservedAfterCompatibilityNormalization": True,
                     "NativeLinuxExecution": "Pending; renderer exercised on Windows with installed fonts.",
                     "HumanAcceptance": "Pending"}
(root / "verification.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
