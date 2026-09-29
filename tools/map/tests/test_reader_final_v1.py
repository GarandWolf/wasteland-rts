#!/usr/bin/env python3
"""Reader/validator tests for the FINAL formatVersion 1 layout (stdlib + wrmb_reader only).
    python3 tools/map/tests/test_reader_final_v1.py [<main.v1-marsh.wrmb> <terrain.v1-marsh.wrmb>]
Default: data/golden/ v1-marsh pair (the merged v1 pair has WTRA version 1, so the WTRA v2 checks do not apply to it)
Proves: (1) a v1 file without header flag bit2 V1_FINAL (stale pre-final v1) is rejected; (2) a v1 terrain file without WTRA fails;
(3) a raw section with the wrong elementSize is rejected (WTRA must be 1); (4) a map needing more than 254 defense groups fails the
validator (synthetic 255-group chokepoint set, and the golden file with one group id forced to 254); (5) a road node with
territoryId 0 fails; (6) the unmodified golden pair passes; (7) cliff rule: a synthetic cliff next to a blocking-river border span
within 1 km of a bridgehead fails, one farther away passes, and the Gorge pair passes with no allow-list (11 span-adjacent cliffs).
(8) v1-marsh: WTRA version 2 (wetland 4), the WTRA/MVLC match check (fordable water 2 must be code 5, ford cells 3 exempt, every other
code-5 cell must be wetland 4), and the per-section xxHash64 (official vectors; tag + count mixed in). Exit 0 = all pass."""
import copy, hashlib, os, struct, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.dirname(HERE))
import wrmb_reader as W
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..")); B = os.path.join(REPO, "data", "golden")
MAIN = sys.argv[1] if len(sys.argv) > 2 else os.path.join(B, "golden-ci-columbia-gorge-100.v1-marsh.wrmb")
TERR = sys.argv[2] if len(sys.argv) > 2 else os.path.join(B, "golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb")
mb, tb = open(MAIN, "rb").read(), open(TERR, "rb").read()
fails = []
def check(name, cond):
    print(("PASS " if cond else "FAIL ") + name)
    if not cond: fails.append(name)
def rehash(buf):
    z = bytearray(buf); z[32:64] = b"\0" * 32; h = hashlib.sha256(bytes(z)).digest(); z[32:64] = h; return bytes(z)
def rejects(buf, needle):
    try: W.decode(buf)
    except W.BlobError as e: return needle in str(e)
    return False
# (6) baseline
m, t = W.decode(mb), W.decode(tb)
check("golden main validates", not W.validate(m)[0]); check("golden terrain validates", not W.validate(t)[0])
check("golden pair validates", not W.validate_pair(m, t, os.path.basename(MAIN), os.path.basename(TERR))[0])
# (1) stale pre-final v1: clear flags bit2
flags = struct.unpack_from("<I", mb, 12)[0]; s = bytearray(mb); struct.pack_into("<I", s, 12, flags & ~W.FLAG_V1_FINAL)
check("v1 without V1_FINAL flag rejected", rejects(rehash(bytes(s)), "V1_FINAL"))
# (2) v1 terrain without WTRA
t2 = {"sections": {k: v for k, v in t["sections"].items() if k != "WTRA"}}
d2 = W.decode(W.encode(t2, t["header"]["flags"], 1))
check("v1 terrain without WTRA fails", any("without WTRA" in e for e in W.validate(d2)[0]))
# (3) WTRA elementSize 2 in the directory
s = bytearray(tb); n = struct.unpack_from("<I", s, 16)[0]
for i in range(n):
    o = 64 + i * 40
    if s[o:o + 4] == b"WTRA":
        cnt, es = struct.unpack_from("<II", s, o + 24); struct.pack_into("<II", s, o + 24, cnt // 2, 2)
check("raw WTRA elementSize != 1 rejected", rejects(rehash(bytes(s)), "raw section"))
# (4a) synthetic: 255 border chokepoints, each its own side pair -> 255 groups (ids 0..254) -> limit error
syn = [{"id": i + 1, "sideATerritoryId": 1 + i // 300, "sideBTerritoryId": 2 + i, "defenseGroupId": i, "stableKey": i + 1} for i in range(255)]
e = W.check_defense_groups(syn)
check("255 defense groups fail (synthetic)", any("exceeds the limit of 254" in x for x in e))
check("254 defense groups pass (synthetic)", not W.check_defense_groups(syn[:254]))
# (4b) golden main with the last border group's id forced to 254
m2 = copy.deepcopy(m); ch = m2["sections"]["CHOK"]
last = max(c["defenseGroupId"] for c in ch if c["defenseGroupId"] != W.NO_DEFENSE_GROUP)
for c in ch:
    if c["defenseGroupId"] == last: c["defenseGroupId"] = 254
check("validator fails a map with defense group id 254", any("exceeds the limit of 254" in x for x in W.validate(m2)[0]))
# (5) RNOD territoryId 0
m3 = copy.deepcopy(m); m3["sections"]["RNOD"][0]["territoryId"] = 0
check("road node with territoryId 0 fails", any("territoryId 0" in x for x in W.validate(m3)[0]))
# (7) cliff / crossing rule
def synth(head_x):
    w = h = 40; lc = bytearray(w * h); sl = [0] * (w * h)
    for y in range(h): lc[y * w + 20] = 6          # blocking river column x = 20; territory 1 west, 2 east
    sl[10 * w + 19] = 1200                           # cliff on the west bank
    return W.cliff_crossing_check(w, h, struct.pack(f"<{w * h}H", *sl), bytes(lc), [], [10 * w + head_x], [1, 2, 1, 2], 2, 80467, 9)
r_near, r_far = synth(16), synth(0)
check("synthetic cliff 3 cells (268 m) from a bridgehead fails", r_near["failing"] == [10 * 40 + 19])
check("synthetic cliff 19 cells (1699 m) from a bridgehead passes", r_far["failing"] == [] and len(r_far["spanAdjacentCliffs"]) == 1)
# golden pair with a cliff planted on the bank next to a Columbia bridgehead -> validate_pair reports the cliff error
t4 = copy.deepcopy(t); Mt = t4["sections"]["META"][0]; w, E = Mt["moveWidth"], Mt["mapWidthCm"]; lc4 = t4["sections"]["MVLC"]
hn = [n for n in m["sections"]["RNOD"] if n["flags"] & W.RNOD_FLAGS["BridgeHead"]]
e_main = {(e["a"], e["b"]) for e in m["sections"]["REDG"] if e["flags"] & W.REDG_FLAGS["mainRiverBridge"]}
hn = [n for n in hn if any(n["id"] in ab for ab in e_main)]
n0 = hn[0]; c0 = min(n0["positionCm"][1] * w // E, w - 1) * w + min(n0["positionCm"][0] * w // E, w - 1)
cand = [c0 + dy * w + dx for dy in range(-3, 4) for dx in range(-3, 4)]
cand = [c for c in cand if lc4[c] != 6 and any(lc4[c + dy * w + dx] == 6 for dy in (-1, 0, 1) for dx in (-1, 0, 1))]
sl4 = bytearray(t4["sections"]["MVSL"]); struct.pack_into("<H", sl4, 2 * cand[0], 1500); t4["sections"]["MVSL"] = bytes(sl4)
e4 = W.validate_pair(m, t4, os.path.basename(MAIN), os.path.basename(TERR))[0]
check("validator: planted cliff next to a Columbia bridgehead fails", any(x.startswith("cliff cells") for x in e4))
cc = W.cliff_check_model(m, t)
check("Gorge cliff rule passes (no allow-list), 11 span-adjacent cliffs", cc["failing"] == [] and len(cc["spanAdjacentCliffs"]) == 11)
print("  Gorge: cliff cells", cc["cliffCells"], "span-adjacent", len(cc["spanAdjacentCliffs"]), "nearest crossing m",
      min(x["nearestCrossingM"] for x in cc["spanAdjacentCliffs"]))
# (8) v1-marsh: WTRA v2 + match check + xxHash64. Runs on the v1-marsh pair: the given pair if it is one, else the sibling
# golden-ci-columbia-gorge-100.(terrain.)v1-marsh.wrmb files next to it (so CI, which passes the merged v1 pair, still covers them).
def _marsh_paths():
    if W.water_art(t)["version"] == 2: return MAIN, TERR
    a, b = MAIN.replace(".v1.wrmb", ".v1-marsh.wrmb"), TERR.replace(".terrain.v1.wrmb", ".terrain.v1-marsh.wrmb")
    return (a, b) if a != MAIN and os.path.exists(a) and os.path.exists(b) else (None, None)
_mm_path, _tm_path = _marsh_paths()
if _mm_path is None: print("SKIP (8) no v1-marsh pair found beside the given pair")
else:
    tb8 = open(_tm_path, "rb").read(); t8 = W.decode(tb8); m8 = W.decode(open(_mm_path, "rb").read())
    check("v1-marsh pair validates", not W.validate(m8)[0] and not W.validate(t8)[0] and not W.validate_pair(m8, t8, os.path.basename(_mm_path), os.path.basename(_tm_path))[0])
    check("golden terrain is WTRA version 2", W.water_art(t8)["version"] == 2)
    mm = W.wtra_marsh_match(t8["sections"]["MVLC"], W.water_art(t8)["codes"], t8["sections"]["MVFD"])
    check("golden WTRA/MVLC match check: 0 violations", not any(mm[k] for k in ("fordableWaterNotMarsh", "marshNotWaterFordOrWetland", "wetlandNotMarsh", "wetlandOnFord")))
    def wtra_variant(edit):
        A = W.water_art(t8); cod = bytearray(A["codes"]); edit(cod, t8["sections"]["MVLC"], t8["sections"]["MVFD"])
        tv = copy.deepcopy(t8); tv["sections"]["WTRA"] = W.encode_wtra(A["width"], A["height"], bytes(cod), A["fords"], 2); return W.validate(tv)[0]
    def to_fordable_on_non_marsh(cod, lcv, fdv):    # a fordable-water mark on a non-marsh land cell
        i = next(i for i in range(len(cod)) if cod[i] == 0 and lcv[i] != 5 and not (fdv[i >> 3] >> (i & 7)) & 1); cod[i] = 2
    def marsh_without_mark(cod, lcv, fdv):          # a code-5 cell with no water/ford/wetland mark
        i = next(i for i in range(len(cod)) if cod[i] == 2 and lcv[i] == 5); cod[i] = 0
    def wetland_on_non_marsh(cod, lcv, fdv):        # a wetland mark on a non-marsh cell
        i = next(i for i in range(len(cod)) if cod[i] == 0 and lcv[i] != 5 and not (fdv[i >> 3] >> (i & 7)) & 1); cod[i] = 4
    check("WTRA fordable water on a non-marsh cell fails", any("fordable water (2) cells that are not MVLC code 5" in x for x in wtra_variant(to_fordable_on_non_marsh)))
    check("MVLC code-5 cell without water/ford/wetland mark fails", any("neither WTRA fordable water" in x for x in wtra_variant(marsh_without_mark)))
    check("WTRA wetland on a non-marsh cell fails", any("wetland (4) cells that are not MVLC code 5" in x for x in wtra_variant(wetland_on_non_marsh)))
    t1 = copy.deepcopy(t8); A = W.water_art(t8); t1["sections"]["WTRA"] = W.encode_wtra(A["width"], A["height"], bytes(0 if c == 4 else c for c in A["codes"]), A["fords"], 1)
    check("WTRA version 1 (no code 4, no match check) still decodes", W.water_art(t1)["version"] == 1 and any("neither WTRA" in x for x in W.validate(t1)[0]) is False)
    for txt, seed, exp in ((b"", 0, 0xEF46DB3751D8E999), (b"a", 0, 0xD24EC4F1A98C6E5B), (b"abc", 0, 0x44BC2CF5AD770999), (b"Hello World", 0, 0x6334D20719245BC2),
                           (b"0123456789abcdef0123456789abcdef", 0, 0x642A94958E71E6C5), (b"", 13, 0x99058BC02E06BBDB), (b"abc", 1, 0xBEA9CA8199328908)):
        check(f"xxHash64 vector {txt[:8]!r} seed {seed}", W.xxh64(txt, seed) == exp)
    sh = W.section_hashes(tb8)
    check("per-section hashes mix tag and count", W.section_xxh64("MVLC", 1, b"ab") != W.section_xxh64("MVFD", 1, b"ab") and W.section_xxh64("MVLC", 1, b"ab") != W.section_xxh64("MVLC", 2, b"ab")
          and set(sh) >= {"META", "MVSL", "MVLC", "MVFD", "MVEL", "WTRA"} and all(len(v["xxh64"]) == 16 for v in sh.values()))
print("RESULT", "PASS" if not fails else f"FAIL {fails}")
sys.exit(1 if fails else 0)
