"""Verify preview provenance, payload/ZIP integrity and target app-host identity.

This does not launch a target binary or establish native OS support.
Usage: python Verify-DesktopPreview.py <preview-root> <fresh-evidence-json>
"""
import hashlib
import json
import pathlib
import plistlib
import struct
import sys
import zipfile

root = pathlib.Path(sys.argv[1]).resolve(strict=True)
evidence = pathlib.Path(sys.argv[2]).resolve()
if evidence.exists():
    raise ValueError("Choose fresh verification evidence")
manifest_bytes = (root / "desktop-preview-manifest.json").read_bytes()
manifest = json.loads(manifest_bytes.decode("utf-8-sig"))
assert manifest["Kind"] == "DesktopPreview" and manifest["FormatVersion"] == 1
assert manifest["NativeVerified"] is False and manifest["SignedAndNotarized"] is False
assert manifest["InformationalVersion"] == manifest["Version"] + "+" + manifest["CommitId"]
assert not (root / "payload-manifest.json").exists(), "A preview cannot masquerade as a Windows update"
files = {}
for entry in manifest["Files"]:
    relative = pathlib.PurePosixPath(entry["RelativePath"])
    assert not relative.is_absolute() and ".." not in relative.parts and str(relative) not in files
    source = root / pathlib.Path(*relative.parts)
    assert source.resolve().is_relative_to(root) and not source.is_symlink()
    data = source.read_bytes()
    assert len(data) == entry["Length"] and hashlib.sha256(data).hexdigest().upper() == entry["Sha256"].upper()
    files[str(relative)] = hashlib.sha256(data).digest()
files["desktop-preview-manifest.json"] = hashlib.sha256(manifest_bytes).digest()
actual = {path.relative_to(root).as_posix() for path in root.rglob("*") if path.is_file()}
assert actual == set(files), "Manifest omits or invents payload files"
runtime = manifest["Runtime"]
is_mac = runtime.startswith("osx-")
host = "Axiom.app/Contents/MacOS/Axiom.App" if is_mac else "Axiom/Axiom.App"
header = (root / host).read_bytes()[:32]
if is_mac:
    assert struct.unpack("<I", header[:4])[0] == 0xFEEDFACF, "Expected 64-bit Mach-O app host"
    assert struct.unpack("<I", header[4:8])[0] == (0x0100000C if runtime == "osx-arm64" else 0x01000007)
    plist = plistlib.loads((root / "Axiom.app/Contents/Info.plist").read_bytes())
    assert plist["CFBundleExecutable"] == "Axiom.App" and plist["CFBundlePackageType"] == "APPL"
    assert plist["AxiomInformationalVersion"] == manifest["InformationalVersion"]
    assert (root / "Axiom.app/Contents/Resources/Axiom.icns").read_bytes()[:4] == b"icns"
else:
    assert runtime == "linux-x64" and header[:6] == b"\x7fELF\x02\x01"
    assert struct.unpack("<H", header[18:20])[0] == 62, "Expected x64 ELF app host"
archive = pathlib.Path(str(root) + ".zip")
with zipfile.ZipFile(archive) as packaged:
    entries = [entry for entry in packaged.infolist() if not entry.is_dir()]
    assert len(entries) == len(files) and {entry.filename for entry in entries} == set(files)
    for entry in entries:
        assert entry.create_system == 3, "Unix creator metadata missing"
        assert hashlib.sha256(packaged.read(entry)).digest() == files[entry.filename]
    app_host = packaged.getinfo(host)
    assert ((app_host.external_attr >> 16) & 0o777) == 0o755, "Executable permission missing"
    assert packaged.testzip() is None
result = {
    "Runtime": runtime,
    "InformationalVersion": manifest["InformationalVersion"],
    "PayloadFiles": len(manifest["Files"]),
    "ManifestSha256": hashlib.sha256(manifest_bytes).hexdigest().upper(),
    "ArchiveSha256": hashlib.sha256(archive.read_bytes()).hexdigest().upper(),
    "PayloadAndArchiveHashesVerified": True,
    "TargetAppHostAndPermissionsVerified": True,
    "NativeLaunchAndFeatureParityVerified": False,
}
evidence.write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))
