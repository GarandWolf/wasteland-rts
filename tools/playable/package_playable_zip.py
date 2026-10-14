#!/usr/bin/env python3
"""Build wasteland-rts-playable-win64.zip from this checkout.

The Unity player directory is optional. Without it, the zip still carries the
map, balance data, placeholder audio, ATTRIBUTION.md, and README.txt so the
headless verifier can run. Pass --player-dir to the StandaloneWindows64 output
when a license is available.
"""

from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--player-dir", type=Path)
    parser.add_argument("--repo", type=Path)
    args = parser.parse_args(argv[1:])
    repo = (args.repo or Path(__file__).resolve().parents[2]).resolve()
    output = args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    reserved = {"ATTRIBUTION.md", "README.txt"}
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.write(repo / "ATTRIBUTION.md", "ATTRIBUTION.md")
        archive.write(repo / "tools" / "playable" / "README.txt", "README.txt")
        add_tree(archive, repo / "data" / "balance", "data/balance/")
        golden = repo / "data" / "golden"
        wrmb = sorted(golden.glob("*.wrmb"))
        if not wrmb:
            print(f"no .wrmb files in {golden}", file=sys.stderr)
            return 1
        for path in wrmb:
            archive.write(path, f"data/golden/{path.name}")
        audio = repo / "Assets" / "_Project" / "Audio" / "Placeholder"
        wavs = sorted(audio.glob("*.wav"))
        if not wavs:
            print(f"no .wav files in {audio}", file=sys.stderr)
            return 1
        for path in wavs:
            archive.write(path, f"audio/{path.name}")
        if args.player_dir is not None:
            player = find_player_root(args.player_dir.resolve())
            for path in sorted(p for p in player.rglob("*") if p.is_file()):
                arc = path.relative_to(player).as_posix()
                if arc in reserved or arc.startswith("data/") or arc.startswith("audio/"):
                    print(f"player file collides with packaged content: {arc}", file=sys.stderr)
                    return 1
                archive.write(path, arc)
    print(output)
    return 0


def add_tree(archive: zipfile.ZipFile, source: Path, prefix: str) -> None:
    if not source.is_dir():
        raise SystemExit(f"missing {source}")
    for path in sorted(p for p in source.rglob("*") if p.is_file()):
        if path.suffix == ".meta":
            continue
        archive.write(path, prefix + path.relative_to(source).as_posix())


def find_player_root(player_dir: Path) -> Path:
    if not player_dir.is_dir():
        raise SystemExit(f"player dir not found: {player_dir}")
    if any(player_dir.glob("*.exe")):
        return player_dir
    children = [path for path in player_dir.iterdir() if path.is_dir() and any(path.glob("*.exe"))]
    if len(children) == 1:
        return children[0]
    return player_dir


if __name__ == "__main__":
    sys.exit(main(sys.argv))
