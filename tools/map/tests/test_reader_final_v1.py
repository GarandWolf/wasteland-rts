#!/usr/bin/env python3
"""Reader/validator tests for the FINAL formatVersion 1 layout (stdlib + wrmb_reader only).
    python3 tools/map/tests/test_reader_final_v1.py [<main.v1.wrmb> <terrain.v1.wrmb>]
Default: data/golden/ golden pair.
Proves: (1) a v1 file without header flag bit2 V1_FINAL (stale pre-final v1) is rejected; (2) a v1 terrain file without WTRA fails;
(3) a raw section with the wrong elementSize is rejected (WTRA must be 1); (4) a map needing more than 254 defense groups fails the
validator (synthetic 255-group chokepoint set, and the golden file with one group id forced to 254); (5) a road node with
territoryId 0 fails; (6) the unmodified golden pair passes. Exit 0 = all pass."""
import copy, hashlib, os, struct, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import wrmb_reader as W
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
B = os.path.join(REPO, "data", "golden")
MAIN = sys.argv[1] if len(sys.argv) > 2 else os.path.join(B, "golden-ci-columbia-gorge-100.v1.wrmb")
TERR = sys.argv[2] if len(sys.argv) > 2 else os.path.join(B, "golden-ci-columbia-gorge-100.terrain.v1.wrmb")
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
print("RESULT", "PASS" if not fails else f"FAIL {fails}")
sys.exit(1 if fails else 0)
