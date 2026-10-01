"""Archive a fresh preview with actual Unix creator and permission metadata."""
import pathlib
import shutil
import sys
import zipfile

root = pathlib.Path(sys.argv[1]).resolve(strict=True)
archive = pathlib.Path(sys.argv[2]).resolve()
if archive.exists() or archive.is_relative_to(root):
    raise ValueError("Choose a fresh archive outside the payload")
with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as output:
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or not path.resolve().is_relative_to(root):
            raise ValueError("Linked or escaped payload entry")
        relative = path.relative_to(root).as_posix()
        directory = path.is_dir()
        info = zipfile.ZipInfo(relative + ("/" if directory else ""))
        info.create_system = 3
        info.compress_type = zipfile.ZIP_DEFLATED
        mode = 0o40755 if directory else 0o100755 if path.name == "Axiom.App" or path.suffix in (".dylib", ".so") else 0o100644
        info.external_attr = (mode << 16) | (0x10 if directory else 0)
        if directory:
            output.writestr(info, b"")
        else:
            info.file_size = path.stat().st_size
            with path.open("rb") as source, output.open(info, "w") as destination:
                shutil.copyfileobj(source, destination)
