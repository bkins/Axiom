"""Validate retained EPUB/ODT package evidence; does not assert reader layout acceptance."""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED

from lxml import etree

arguments = argparse.ArgumentParser()
arguments.add_argument("evidence_directory", type=Path)
arguments.add_argument("schema_directory", type=Path)
options = arguments.parse_args()
parser = etree.XMLParser(resolve_entities=False, no_network=True)
content_schema = etree.RelaxNG(etree.parse(str(options.schema_directory / "OpenDocument-v1.3-schema.rng"), parser))
manifest_schema = etree.RelaxNG(etree.parse(str(options.schema_directory / "OpenDocument-v1.3-manifest-schema.rng"), parser))
proof = {"ReaderLayoutVerified": False, "Packages": []}
for suffix, mime in [("epub", "application/epub+zip"), ("odt", "application/vnd.oasis.opendocument.text")]:
    path = options.evidence_directory / ("export." + suffix)
    with ZipFile(path) as package:
        entries = package.infolist()
        assert entries[0].filename == "mimetype" and entries[0].compress_type == ZIP_STORED
        assert package.read("mimetype").decode("ascii") == mime
        assert len(entries) == len({entry.filename for entry in entries})
        assert not package.testzip()
        for entry in entries:
            assert not entry.filename.startswith("/") and ".." not in Path(entry.filename).parts
        content = package.read("content.xml" if suffix == "odt" else "EPUB/content.xhtml")
        assert b"documentId" not in content
        document = etree.fromstring(content, parser)
        if suffix == "odt":
            content_schema.assertValid(document)
            manifest_schema.assertValid(etree.fromstring(package.read("META-INF/manifest.xml"), parser))
            manifest = etree.fromstring(package.read("META-INF/manifest.xml"), parser)
            namespace = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"
            listed = {element.get("{" + namespace + "}full-path") for element in manifest}
            assert listed - {"/"} == {entry.filename for entry in entries} - {"mimetype", "META-INF/manifest.xml"}
        else:
            namespace = "http://www.w3.org/1999/xhtml"
            assert document.tag == "{" + namespace + "}html"
            assert not document.findall(".//{" + namespace + "}script")
            etree.fromstring(package.read("EPUB/package.opf"), parser)
            etree.fromstring(package.read("EPUB/nav.xhtml"), parser)
        proof["Packages"].append({"Format": suffix, "Sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "XmlAndPackageChecksPassed": True, "OasisSchemaValidated": suffix == "odt"})
destination = options.evidence_directory / "additional-export-verification.json"
if destination.exists():
    raise FileExistsError("Verification evidence already exists; choose a fresh fixture.")
destination.write_text(json.dumps(proof, indent=2), encoding="utf-8")
print(json.dumps(proof, indent=2))
