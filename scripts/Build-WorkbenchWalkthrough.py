"""Build the offline captioned walkthrough from maintained steps and real screenshots."""
import html
import json
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "src/Axiom.App/Walkthrough"
version = ET.parse(root.parents[2] / "Directory.Build.props").findtext(".//AxiomVersion")
steps = json.loads((root / "steps.json").read_text(encoding="utf-8"))
if not steps or len(steps) > 50:
    raise ValueError("Choose 1–50 walkthrough steps")
sections = []
options = []
for index, step in enumerate(steps):
    image = root / "images" / step["image"]
    if image.resolve().parent != (root / "images").resolve() or not image.is_file():
        raise ValueError(f"Missing or invalid screenshot: {image}")
    title, caption, shortcut = [html.escape(step[field]) for field in ("title", "caption", "shortcut")]
    sections.append(f'<section class="slide" aria-labelledby="step-{index}"><h2 id="step-{index}">{index+1}. {title}</h2><img src="images/{html.escape(step["image"], quote=True)}" alt="Axiom Windows app showing: {title}" width="1102" decoding="async"><p class="caption">{caption}</p><p class="shortcut">{shortcut}</p></section>')
    options.append(f'<option value="{index}">{index+1}. {title}</option>')
document = '''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'self'; script-src 'self'; base-uri 'none'; form-action 'none'"><title>Axiom — Guided walkthrough</title><link rel="stylesheet" href="walkthrough.css"><script src="walkthrough.js" defer></script></head><body><main><header><h1>Axiom — Guided walkthrough</h1><p>1.0.23 · Windows · Captioned steps</p></header>''' + "\n".join(sections) + '''
<nav id="navigation" class="navigation" aria-label="Walkthrough steps" hidden><button id="previous" type="button">← Previous</button><span id="counter" class="counter" aria-live="off"></span><label for="jump" class="visually-hidden">Choose a walkthrough step</label><select id="jump">''' + "".join(options) + '''</select><button id="next" type="button">Next →</button></nav><p><a id="image-link" href="images/01-workspace.jpg" target="_blank" rel="noopener" hidden>Open current screenshot full size</a></p><p id="announcement" class="visually-hidden" aria-live="polite" aria-atomic="true"></p><footer>Use Previous/Next, ←/→, or Home/End. Nothing plays automatically. Screenshots show actual Windows app workflows with example documents. Find additional workflows in Axiom's offline Help (F1). With JavaScript disabled, all steps remain readable in order.</footer></main></body></html>'''
document = document.replace("1.0.23 · Windows · Captioned steps", html.escape(version or "") + " · Windows · Basics 1–8 · Advanced Tools 9–16")
document = document.replace("</header>", "<p>Captions describe the current commands. Reference screenshots were captured on 1.0.24 and show earlier chrome; see the maintained offline User Guide (F1) for the compact workbench.</p></header>")
(root / "index.html").write_text(document, encoding="utf-8")
print(f"Built {len(steps)} captioned steps with local screenshots")
