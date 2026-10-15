#!/usr/bin/env python3
"""Fail if a playable zip ships a stub map, an LFS pointer, or a bad credit file.

Run from a checkout of this repo. For each shipped .wrmb, the Git blob at HEAD
must be an LFS pointer, and the zip bytes must match that pointer's size and
oid sha256. Each shipped .wav must be a RIFF/WAVE file whose SHA-256 is listed
in art-sources/audio/placeholder/SHA256SUMS.
"""

from __future__ import annotations

import hashlib
import subprocess
import sys
import zipfile
from pathlib import Path, PurePosixPath

LFS_PREFIX = b"version https://git-lfs"
MIN_WRMB_BYTES = 1024
SUMS_REL = "art-sources/audio/placeholder/SHA256SUMS"
AUDIO_TABLE_REL = "Assets/_Project/Audio/Placeholder/ATTRIBUTION.md"
REQUIRED_WRMB = (
    "golden-ci-columbia-gorge-100.v1-marsh.wrmb",
    "golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb",
)
REQUIRED_TEXT = (
    "Map data © OpenStreetMap contributors, available under the Open Database License (ODbL): https://www.openstreetmap.org/copyright",
    "https://download.geofabrik.de/north-america/us/oregon-260927.osm.pbf",
    "https://download.geofabrik.de/north-america/us/washington-260927.osm.pbf",
    "https://github.com/GarandWolf/wasteland-rts/releases/download/playable-2026-10/gorge-clip.osm.pbf",
    "3eebb2957d33270446cbb2f46ea4a87631086da7c6418f7700693b7dc8b87176",
    "Contains modified Copernicus DEM data (GLO-30), © DLR e.V. 2010–2014 and © Airbus Defence and Space GmbH 2014–2018, provided under COPERNICUS by the European Union and ESA",
    "ESA WorldCover © ESA 2021, CC BY 4.0",
    "USGS 3DEP elevation data (public domain)",
    "Kenney",
    "CC0",
)
WIN_LINE = "Capture the enemy headquarters."


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: verify_playable_zip.py <zip>", file=sys.stderr)
        return 2
    repo = find_repo()
    zip_path = Path(argv[1])
    errors: list[str] = []
    if not zip_path.is_file():
        errors.append(f"zip not found: {zip_path}")
        return report(errors)
    try:
        archive = zipfile.ZipFile(zip_path)
    except zipfile.BadZipFile as ex:
        errors.append(f"not a zip: {ex}")
        return report(errors)
    with archive:
        names = [info.filename.replace("\\", "/") for info in archive.infolist() if not info.is_dir()]
        blobs = {name: archive.read(name) for name in names}
    check_docs(blobs, repo, errors)
    check_wrmb(blobs, repo, errors)
    check_wav(blobs, repo, errors)
    return report(errors)


def find_repo() -> Path:
    here = Path(__file__).resolve()
    for candidate in (here.parents[2], Path.cwd()):
        if (candidate / ".git").exists() and (candidate / SUMS_REL).is_file():
            return candidate
    print("verify_playable_zip.py must run inside the wasteland-rts checkout", file=sys.stderr)
    sys.exit(2)


def report(errors: list[str]) -> int:
    if not errors:
        print("playable zip ok")
        return 0
    print(f"playable zip failed ({len(errors)})", file=sys.stderr)
    for error in errors:
        print(f"  {error}", file=sys.stderr)
    return 1


def root_name(name: str) -> str:
    return name.replace("\\", "/").lstrip("./")


def check_docs(blobs: dict[str, bytes], repo: Path, errors: list[str]) -> None:
    attribution = root_file(blobs, "ATTRIBUTION.md")
    if attribution is None:
        errors.append("ATTRIBUTION.md is missing from the zip root")
    else:
        text = attribution.decode("utf-8", errors="replace")
        for phrase in REQUIRED_TEXT:
            if phrase not in text:
                errors.append(f"ATTRIBUTION.md is missing {phrase!r}")
        shipped_table = markdown_table(text)
        source_table = markdown_table((repo / AUDIO_TABLE_REL).read_text(encoding="utf-8"))
        if not source_table:
            errors.append(f"could not read the audio table from {AUDIO_TABLE_REL}")
        elif shipped_table != source_table:
            errors.append("ATTRIBUTION.md audio table does not match Assets/_Project/Audio/Placeholder/ATTRIBUTION.md")
    readme = root_file(blobs, "README.txt")
    if readme is None:
        errors.append("README.txt is missing from the zip root")
    else:
        lines = readme.decode("utf-8", errors="replace").splitlines()
        if WIN_LINE not in lines:
            errors.append(f"README.txt is missing the win line {WIN_LINE!r}")


def root_file(blobs: dict[str, bytes], filename: str) -> bytes | None:
    for name, data in blobs.items():
        if root_name(name) == filename:
            return data
    return None


def markdown_table(text: str) -> str:
    lines: list[str] = []
    started = False
    for line in text.splitlines():
        if line.startswith("| Output file |"):
            started = True
        if not started:
            continue
        if line.startswith("|"):
            lines.append(line.rstrip())
        elif lines:
            break
    return "\n".join(lines)


def check_wrmb(blobs: dict[str, bytes], repo: Path, errors: list[str]) -> None:
    tracked = tracked_wrmb(repo)
    seen: dict[str, str] = {}
    shipped = 0
    for name, data in blobs.items():
        if not name.lower().endswith(".wrmb"):
            continue
        shipped += 1
        base = PurePosixPath(root_name(name)).name
        if is_pointer(data):
            errors.append(f"{name} is a Git LFS pointer, not a map file")
        if len(data) < MIN_WRMB_BYTES:
            errors.append(f"{name} is {len(data)} bytes, under {MIN_WRMB_BYTES}")
        repo_path = match_tracked(root_name(name), base, tracked)
        if repo_path is None:
            errors.append(f"{name} does not match a tracked .wrmb")
            continue
        if base in seen:
            errors.append(f"{name} repeats {base}, already shipped as {seen[base]}")
            continue
        seen[base] = name
        pointer = read_pointer(repo, repo_path, errors, name)
        if pointer is None:
            continue
        oid, size = pointer
        digest = hashlib.sha256(data).hexdigest()
        if len(data) != size:
            errors.append(f"{name} is {len(data)} bytes; Git LFS pointer for {repo_path} says size {size}")
        if digest != oid:
            errors.append(f"{name} sha256 {digest} != oid sha256:{oid} ({repo_path})")
    if shipped == 0:
        errors.append("zip contains no .wrmb files")
    for required in REQUIRED_WRMB:
        if required not in seen:
            errors.append(f"zip is missing {required}")


def tracked_wrmb(repo: Path) -> dict[str, list[str]]:
    listing = git(repo, ["ls-files", "*.wrmb"])
    index: dict[str, list[str]] = {}
    for line in listing.splitlines():
        path = line.strip()
        if not path:
            continue
        index.setdefault(PurePosixPath(path).name, []).append(path)
    return index


def match_tracked(zip_name: str, base: str, tracked: dict[str, list[str]]) -> str | None:
    hits = tracked.get(base, [])
    if len(hits) == 1:
        return hits[0]
    for hit in hits:
        if zip_name.endswith(hit):
            return hit
    return None


def read_pointer(repo: Path, repo_path: str, errors: list[str], label: str) -> tuple[str, int] | None:
    blob = git(repo, ["cat-file", "-p", f"HEAD:{repo_path}"])
    if not blob.startswith("version https://git-lfs.github.com/spec/v1"):
        errors.append(f"{label} Git blob {repo_path} is not an LFS pointer")
        return None
    oid = ""
    size = -1
    for line in blob.splitlines():
        if line.startswith("oid sha256:"):
            oid = line[len("oid sha256:") :].strip().lower()
        elif line.startswith("size "):
            try:
                size = int(line.split()[1])
            except (IndexError, ValueError):
                size = -1
    if len(oid) != 64 or size < 0:
        errors.append(f"{label} Git LFS pointer for {repo_path} has no oid sha256 or size")
        return None
    return oid, size


def check_wav(blobs: dict[str, bytes], repo: Path, errors: list[str]) -> None:
    expected = load_sums(repo / SUMS_REL, errors)
    if not expected:
        return
    seen: dict[str, str] = {}
    for name, data in blobs.items():
        if not name.lower().endswith(".wav"):
            continue
        base = PurePosixPath(root_name(name)).name
        if is_pointer(data):
            errors.append(f"{name} is a Git LFS pointer, not a WAV")
        if not is_riff_wave(data):
            errors.append(f"{name} does not start with a RIFF/WAVE header")
        digest = hashlib.sha256(data).hexdigest()
        wanted = expected.get(base)
        if wanted is None:
            errors.append(f"{name} is not listed in {SUMS_REL}")
        elif digest != wanted:
            errors.append(f"{name} sha256 {digest} != {SUMS_REL} {wanted}")
        if base in seen:
            errors.append(f"{name} repeats {base}, already shipped as {seen[base]}")
        else:
            seen[base] = name
    for filename in sorted(expected):
        if filename not in seen:
            errors.append(f"zip is missing audio file {filename} from {SUMS_REL}")


def load_sums(path: Path, errors: list[str]) -> dict[str, str]:
    if not path.is_file():
        errors.append(f"missing {path}")
        return {}
    sums: dict[str, str] = {}
    for lineno, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split()
        if len(parts) != 2 or len(parts[0]) != 64:
            errors.append(f"{path}:{lineno} is not 'sha256 filename'")
            continue
        sums[parts[1]] = parts[0].lower()
    if len(sums) < 12:
        errors.append(f"{path} lists {len(sums)} files; expected the Freesound, Kenney, and HUD cues")
    return sums


def is_pointer(data: bytes) -> bool:
    return data.startswith(LFS_PREFIX)


def is_riff_wave(data: bytes) -> bool:
    return len(data) >= 12 and data[:4] == b"RIFF" and data[8:12] == b"WAVE"


def git(repo: Path, args: list[str]) -> str:
    proc = subprocess.run(
        ["git", *args],
        cwd=repo,
        check=False,
        capture_output=True,
    )
    if proc.returncode != 0:
        detail = proc.stderr.decode("utf-8", errors="replace").strip()
        raise SystemExit(f"git {' '.join(args)} failed: {detail}")
    return proc.stdout.decode("utf-8", errors="replace")


if __name__ == "__main__":
    sys.exit(main(sys.argv))
