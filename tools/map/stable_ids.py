#!/usr/bin/env python3
"""Stable ids (v1): check the golden main blob against data/golden/stable-ids.json.

The v1 blob's TERR / BRDG / CHOK ids and u64 stableKeys must match the manifest
exactly, and those same integer ids must equal the v0 blob's ids.

    python3 tools/map/stable_ids.py \
      --check data/golden/golden-ci-columbia-gorge-100.v1.wrmb \
      --against data/golden/golden-ci-columbia-gorge-100.v0.wrmb \
      [--manifest data/golden/stable-ids.json] [--report out.json]
    python3 tools/map/stable_ids.py --update <main.wrmb> [--manifest data/golden/stable-ids.json]
"""
import json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
MANIFEST = os.path.join(REPO, "data", "golden", "stable-ids.json")
OSM_NODE, OSM_WAY, OSM_REL = 1, 2, 3
SECTIONS = {"TERR": "territories", "BRDG": "bridges", "CHOK": "chokepoints"}
class StableIdError(Exception): pass
def make_key(osm_type, osm_id, disc=0):
    assert 0 < osm_id < (1 << 52) and 0 <= disc < 256 and osm_type in (1, 2, 3)
    return (osm_type << 60) | (disc << 52) | osm_id
def split_key(k): return {"osmType": ["?", "node", "way", "relation"][k >> 60], "disc": (k >> 52) & 0xFF, "osmId": k & ((1 << 52) - 1)}
def load(path=MANIFEST):
    if not os.path.exists(path): return {v: {} for v in SECTIONS.values()}
    m = json.load(open(path))
    return {sec: {int(k): v["id"] for k, v in m[sec].items()} for sec in SECTIONS.values()}
def assign(section, keys, path=MANIFEST):
    """keys: list of u64 in the default (deterministic) order. Returns list of ids (same order)."""
    man = load(path)[section]
    if len(set(keys)) != len(keys):
        dup = sorted({k for k in keys if keys.count(k) > 1})
        raise StableIdError(f"{section}: duplicate stable keys {[split_key(k) for k in dup]}")
    nxt = max(man.values(), default=0) + 1; ids = []
    for k in keys:
        if k in man: ids.append(man[k])
        else: ids.append(nxt); nxt += 1
    if sorted(ids) != list(range(1, len(ids) + 1)):
        gone = sorted(k for k in man if k not in set(keys))
        raise StableIdError(f"{section}: ids not dense 1..{len(ids)}; manifest keys missing from this build (would force renumbering): "
                            f"{[(man[k], split_key(k)) for k in gone]}")
    return ids
def _blob_keys(path):
    sys.path.insert(0, HERE); import wrmb_reader as W
    m = W.decode(open(path, "rb").read())
    return {SECTIONS[t]: [(r["stableKey"], r["id"]) for r in m["sections"][t]] for t in SECTIONS}
def _blob_ids(path):
    sys.path.insert(0, HERE); import wrmb_reader as W
    m = W.decode(open(path, "rb").read())
    return {SECTIONS[t]: [r["id"] for r in m["sections"][t]] for t in SECTIONS}
def check_blob(path, manifest=MANIFEST):
    man = load(manifest); errs = []; info = {}
    for sec, recs in _blob_keys(path).items():
        seen = {k: i for k, i in recs}
        changed = [(k, man[sec][k], seen[k]) for k in man[sec] if k in seen and seen[k] != man[sec][k]]
        missing = [k for k in man[sec] if k not in seen]
        extra = [k for k, _ in recs if k not in man[sec]]
        errs += [f"{sec}: key {split_key(k)} id {a} -> {b}" for k, a, b in changed]
        errs += [f"{sec}: manifest key {split_key(k)} (id {man[sec][k]}) missing" for k in missing]
        errs += [f"{sec}: blob key {split_key(k)} (id {seen[k]}) not in manifest" for k in extra]
        info[sec] = {"records": len(recs), "inManifest": sum(k in man[sec] for k, _ in recs), "new": len(extra)}
    return errs, info
def check_v0_ids(v0_path, v1_path):
    """v0 and v1 TERR / BRDG / CHOK integer ids must be identical (seeded-from-v0 contract)."""
    a, b = _blob_ids(v0_path), _blob_ids(v1_path)
    errs = []
    for sec in SECTIONS.values():
        if a[sec] != b[sec]:
            if len(a[sec]) != len(b[sec]):
                errs.append(f"{sec}: v0 id count {len(a[sec])} != v1 id count {len(b[sec])}")
            else:
                diffs = [(i + 1, x, y) for i, (x, y) in enumerate(zip(a[sec], b[sec])) if x != y]
                errs.append(f"{sec}: v0 ids != v1 ids at {diffs[:8]}")
    return errs, {sec: {"v0": len(a[sec]), "v1": len(b[sec]), "equal": a[sec] == b[sec]} for sec in SECTIONS.values()}
def update(path, manifest=MANIFEST, labels=None):
    old = json.load(open(manifest)) if os.path.exists(manifest) else {}
    labels = labels or {sec: {int(k): v["label"] for k, v in old.get(sec, {}).items() if "label" in v} for sec in SECTIONS.values()}
    out = {"note": __doc__.split("\n\n")[0], "keyLayout": "bits63..60 osmType(1 node,2 way,3 relation) | bits59..52 disc | bits51..0 osmId"}
    if "seededFrom" in old: out["seededFrom"] = old["seededFrom"]
    for sec, recs in _blob_keys(path).items():
        out[sec] = {str(k): {"id": i, **split_key(k), **({"label": labels[sec][k]} if labels and k in labels.get(sec, {}) else {})}
                    for k, i in sorted(recs, key=lambda r: r[1])}
    json.dump(out, open(manifest, "w"), indent=1, sort_keys=False); open(manifest, "a").write("\n")
if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(); ap.add_argument("--check"); ap.add_argument("--against")
    ap.add_argument("--update"); ap.add_argument("--manifest", default=MANIFEST); ap.add_argument("--report")
    a = ap.parse_args()
    if a.check:
        e, i = check_blob(a.check, a.manifest); rep = {"result": "PASS" if not e else "FAIL", "errors": e, "info": i}
        if a.against:
            e0, i0 = check_v0_ids(a.against, a.check)
            rep["v0IdsEqualV1"] = "PASS" if not e0 else "FAIL"
            rep["v0IdErrors"] = e0
            rep["v0IdInfo"] = i0
            if e0:
                rep["result"] = "FAIL"
                rep["errors"] = list(rep["errors"]) + e0
        print(json.dumps(rep, indent=1))
        if a.report: json.dump(rep, open(a.report, "w"), indent=1)
        sys.exit(1 if rep["result"] != "PASS" else 0)
    if a.update: update(a.update, a.manifest); print("wrote", a.manifest)
    else:
        ap.error("one of --check or --update is required")
