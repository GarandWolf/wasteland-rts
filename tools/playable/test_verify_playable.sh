#!/usr/bin/env bash
# Headless packaging checks. Fails when a shipped map or clip is a stub.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

git lfs pull --include="data/golden/*.wrmb,Assets/_Project/Audio/Placeholder/*.wav"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

python3 tools/playable/package_playable_zip.py --output "$work/good.zip"
python3 tools/playable/verify_playable_zip.py "$work/good.zip"

python3 - "$work" << 'PY'
import hashlib
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

work = Path(sys.argv[1])
good = work / "good.zip"
script = Path("tools/playable/verify_playable_zip.py")


def verify(path: Path) -> int:
    return subprocess.run([sys.executable, str(script), str(path)]).returncode


def rewrite(src: Path, dest: Path, mutate) -> None:
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dest, "w") as zout:
        for info in zin.infolist():
            data = zin.read(info.filename)
            if not info.is_dir():
                data = mutate(info.filename.replace("\\", "/"), data)
            zout.writestr(info, data)


def expect_fail(name: str, mutate) -> None:
    dest = work / f"{name}.zip"
    rewrite(good, dest, mutate)
    code = verify(dest)
    if code == 0:
        raise SystemExit(f"expected {name} to fail verification")
    print(f"rejected {name}")


expect_fail(
    "missing-attribution",
    lambda name, data: b"" if name == "ATTRIBUTION.md" else data,
)
# An empty member is still present. Drop the name entirely.
dropped = work / "dropped-attribution.zip"
with zipfile.ZipFile(good) as zin, zipfile.ZipFile(dropped, "w") as zout:
    for info in zin.infolist():
        if info.filename.replace("\\", "/") == "ATTRIBUTION.md":
            continue
        zout.writestr(info, zin.read(info.filename))
if verify(dropped) == 0:
    raise SystemExit("expected missing ATTRIBUTION.md to fail")
print("rejected missing ATTRIBUTION.md")

expect_fail(
    "lfs-wav",
    lambda name, data: b"version https://git-lfs.github.com/spec/v1\noid sha256:00\nsize 1\n"
    if name.endswith("ui_click.wav")
    else data,
)

bad_wave = b"RIFF" + b"\x00\x00\x00\x00" + b"WAVE" + b"\x00" * 64
expect_fail(
    "wav-hash",
    lambda name, data: bad_wave if name.endswith("order_deny.wav") else data,
)

expect_fail(
    "tiny-wrmb",
    lambda name, data: b"WRMB" + b"\x00" * 32
    if name.endswith("golden-ci-columbia-gorge-100.v1-marsh.wrmb")
    else data,
)

pointer = subprocess.check_output(
    ["git", "cat-file", "-p", "HEAD:data/golden/golden-ci-columbia-gorge-100.v1-marsh.wrmb"]
)
expect_fail(
    "pointer-wrmb",
    lambda name, data: pointer
    if name.endswith("golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb")
    else data,
)

# Same length as a real file would still fail the oid check. Swap two maps.
main = terrain = None
with zipfile.ZipFile(good) as zin:
    for info in zin.infolist():
        filename = info.filename.replace("\\", "/")
        if filename.endswith("golden-ci-columbia-gorge-100.v1-marsh.wrmb"):
            main = zin.read(info.filename)
        if filename.endswith("golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb"):
            terrain = zin.read(info.filename)
if main is None or terrain is None:
    raise SystemExit("good zip is missing the marsh pair")
if len(main) == len(terrain) or hashlib.sha256(main).hexdigest() == hashlib.sha256(terrain).hexdigest():
    raise SystemExit("marsh pair is not two different files")
expect_fail(
    "swapped-wrmb",
    lambda name, data: terrain
    if name.endswith("golden-ci-columbia-gorge-100.v1-marsh.wrmb")
    else data,
)

print("packaging verify cases passed")
PY
