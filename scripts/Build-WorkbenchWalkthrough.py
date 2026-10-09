"""Build current offline walkthrough instructions with optional verified screenshots."""
import html
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "src/Axiom.App/Walkthrough"
steps = json.loads((root / "steps.json").read_text(encoding="utf-8-sig"))
if not steps or len(steps) > 50:
    raise ValueError("Choose 1–50 walkthrough steps")
sections = []
options = []
for index, step in enumerate(steps):
    title, caption, shortcut = [html.escape(step[field]) for field in ("title", "caption", "shortcut")]
    image_tag = ""
    if step.get("showImage", False):
        image = root / "images" / step["image"]
        if image.resolve().parent != (root / "images").resolve() or not image.is_file():
            raise ValueError(f"Missing or invalid screenshot: {image}")
        image_tag = f'<img src="images/{html.escape(step["image"], quote=True)}" alt="Axiom showing: {title}" width="1102" decoding="async">'
    sections.append(f'<section class="slide" aria-labelledby="step-{index}"><h2 id="step-{index}">{index+1}. {title}</h2>{image_tag}<p class="caption">{caption}</p><p class="shortcut">{shortcut}</p></section>')
    options.append(f'<option value="{index}">{index+1}. {title}</option>')

document = '''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'self'; script-src 'self'; base-uri 'none'; form-action 'none'"><title>Axiom — Guided walkthrough</title><link rel="stylesheet" href="walkthrough.css"><script src="walkthrough.js" defer></script></head><body><main><header><h1>Axiom — Guided walkthrough</h1><p>Windows · Current feature workflows</p><p>For detailed feature instructions, open Main menu → Help → Browse all feature topics, or press F1 in Axiom. Use the semantic-search Help exercise to test reviewed CP results.</p></header>''' + "\n".join(sections) + '''
<nav id="navigation" class="navigation" aria-label="Walkthrough steps" hidden><button id="previous" type="button">← Previous</button><span id="counter" class="counter" aria-live="off"></span><label for="jump" class="visually-hidden">Choose a walkthrough step</label><select id="jump">''' + "".join(options) + '''</select><button id="next" type="button">Next →</button></nav><p><a id="image-link" target="_blank" rel="noopener" hidden>Open current screenshot full size</a></p><p id="announcement" class="visually-hidden" aria-live="polite" aria-atomic="true"></p><footer>Use Previous/Next, ←/→, or Home/End. Nothing plays automatically. Find full feature guidance in offline Help. With JavaScript disabled, all steps remain readable in order.</footer></main></body></html>'''
(root / "index.html").write_text(document, encoding="utf-8")
print(f"Built {len(steps)} current walkthrough steps")