#!/usr/bin/env python3
"""WRMB map blob (formatVersion 0 and 1) - standalone reader / encoder / validator. Python 3 stdlib only.

    python3 wrmb_reader.py <main.wrmb> [--terrain <terrain.wrmb>] [--debug-json <main.debug.json>]
                           [--terrain-debug-json <terrain.debug.json>] [--movement-dir <dir>] [--report out.json]
    python3 wrmb_reader.py <terrain.wrmb> [--debug-json <terrain.debug.json>] [--movement-dir <dir>]   (terrain file alone)

A map is a PAIR of files: the main/AI file (META.fileKind 0) and the terrain file (fileKind 1: movement slope, land cover,
ford bitset, elevation). Each has its own header, META, directory, CRCs and content hash; the pair is bound by
META.terrainPayloadSha256 (both files) and the terrain file's META.pairedContentSha256 = the main file's header contentHash.

Checks: header (magic, versions, sizes, content hash), directory (alignment, bounds, per-section CRC32, count x elementSize),
META counts, GRID descriptors (exact rational cell size, extents, byte sizes), per-fileKind section sets, pair binding, decode of every section incl. pool spans (bounds), exact round trip encode(decode(blob)) == blob bytes,
equality with the JSON debug export (if given), and map invariants: dense sorted ids, a < b, every reference valid,
sorted id lists, one connected passable road graph, every town gate reachable, bridge-head model (every span edge has two
BridgeHead ends, every BridgeHead touches a span), territories/cells consistency, value ranges, terrain grid sizes/codes and
(with --movement-dir) byte equality of MVSL/MVLC/MVFD/MVEL (and WTRA) with the pipeline source arrays.
v1.2 terrain files may carry the optional art-only section WTRA (water marks + ford list, see encode_wtra/decode_wtra and
water_art()); it is validated against MVLC/MVFD (blocking water == MVLC 6 exactly, fords == MVFD) and is not a path input.

LAYOUT STATUS: container + section directory APPROVED by the Lead Engineer (2026-09-28). formatVersion 1 (this writer's
default) adds permanent u64 stableKey fields to TERR / BRDG / CHOK, CHOK.bypassSurfaceClass, RNOD.territoryId and (v1.2) CHOK.defenseGroupId
(u8 in the former pad byte, 255 = no group; group key = min member stableKey, see defense_groups()); formatVersion 0 files still
decode and validate (schema chosen by the header). Header flag bit0 DRAFT_LAYOUT stays set while record layouts may change.
Readers must locate sections via the directory (tag lookup), never by fixed offsets.
The field layout below is the single source of truth; the pipeline writer imports SCHEMA/encode from this file."""
import hashlib, json, struct, sys, zlib
from collections import defaultdict

MAGIC = b"WRMB"
FORMAT_VERSION = 1          # written by default. 0 = v0 (2026-09-28 golden v0); 1 = v1 (stable keys, bypassSurfaceClass). Both decode.
SUPPORTED_FORMAT_VERSIONS = (0, 1)
SCHEMA_VERSION = 1          # Enemy AI design doc s3.1 schema v1 + accepted contract changes (ai-contract-review.md s12)
HEADER_FMT = "<4sHHIIIIQ32s"        # 64 bytes
DIR_FMT = "<4sIQQIIII"              # 40 bytes
HEADER_SIZE = struct.calcsize(HEADER_FMT); DIR_SIZE = struct.calcsize(DIR_FMT)
FLAG_DRAFT_LAYOUT, FLAG_HAS_DEBUG = 1, 2
FLAG_V1_FINAL = 4          # header flags bit2: formatVersion 1 FINAL layout (MVLC v1.2 semantics, WTRA required in terrain files,
                           # CHOK.defenseGroupId @7 / bypassWheeledBlocked @79). REQUIRED for formatVersion 1: pre-final v1 blobs lack it -> rejected.
ALIGN = 8

# field types: i8 u8 i16 u16 i32 u32 u64 | pos (int32 x,y) | b32 (32 raw bytes, hex in model) | str (u32 off,u32 len into STRP)
# | ids (u32 off,u32 count into I32P) | pts (u32 off,u32 count into PTSP, list of [x,y]) | tag4 (4 ASCII bytes, str in model)
# | padN (N zero bytes)
FILE_MAIN, FILE_TERRAIN = 0, 1
PRIM = {"i8": "b", "u8": "B", "i16": "h", "u16": "H", "i32": "i", "u32": "I", "u64": "Q"}
SCHEMA = {
 "META": [("schemaVersion", "i32"), ("fileKind", "i32"), ("mapWidthCm", "i32"), ("mapHeightCm", "i32"), ("cellSizeCm", "i32"), ("gridWidth", "i32"),
          ("gridHeight", "i32"), ("townCount", "i32"), ("territoryCount", "i32"), ("roadNodeCount", "i32"), ("roadEdgeCount", "i32"),
          ("chokepointCount", "i32"), ("riverCount", "i32"), ("bridgeCount", "i32"), ("waterBodyCount", "i32"),
          ("minorSettlementCount", "i32"), ("resourceCount", "i32"), ("sourceCount", "i32"), ("projectionType", "i32"),
          ("centerLatMicroDeg", "i32"), ("centerLonMicroDeg", "i32"), ("falseEastingCm", "i32"), ("falseNorthingCm", "i32"),
          ("scaleFactorPpb", "i32"), ("verticalDatum", "i32"), ("lodLadderLevel", "i32"), ("moveWidth", "i32"), ("moveHeight", "i32"),
          ("moveCellsPerCell", "i32"), ("transformSeed", "u64"), ("mapId", "str"), ("pipelineVersion", "str"),
          ("transformVersion", "str"), ("attribution", "str"), ("provenanceJson", "str"), ("attributionSha256", "b32"),
          ("weightsSha256", "b32"), ("configSha256", "b32"), ("osmClipSha256", "b32"), ("elevOffsetCm", "i32"), ("elevUnitCm", "i32"),
          ("pairedFileName", "str"), ("pairedContentSha256", "b32"), ("terrainPayloadSha256", "b32"), ("pad", "pad4")],
 "GRID": [("sectionTag", "tag4"), ("elemType", "u8"), ("rowOrder", "u8"), ("pad", "pad2"), ("width", "i32"), ("height", "i32"),
          ("originXCm", "i32"), ("originYCm", "i32"), ("extentXCm", "i32"), ("extentYCm", "i32"), ("cellSizeNumCm", "i32"),
          ("cellSizeDen", "i32"), ("elemBytes", "i32"), ("valueOffset", "i32"), ("valueScale", "i32"), ("pad2", "pad4")],
 "SRCS": [("id", "i32"), ("key", "str"), ("version", "str"), ("licence", "str"), ("sha256", "b32")],
 "TOWN": [("id", "i32"), ("positionCm", "pos"), ("territoryId", "i32"), ("nameHash", "i32"), ("population", "i32"),
          ("offRoadDistanceCm", "i32"), ("roadNodeId", "i32"), ("strategicValue", "i16"), ("populationClass", "i8"),
          ("sourceFlags", "u8"), ("neighborTownIds", "ids"), ("exitEdgeIds", "ids"), ("name", "str"), ("pad", "pad4")],
 "TDBG": [("townId", "i32"), ("P", "i16"), ("R", "i16"), ("C", "i16"), ("H", "i16"), ("X", "i16"), ("base", "i16"), ("abs", "i16"),
          ("pad", "pad2")],
 "TERR": [("id", "i32"), ("seatTownId", "i32"), ("areaCells", "i32"), ("areaHa", "i32"), ("centroidCm", "pos"), ("townIds", "ids"),
          ("neighborTerritoryIds", "ids")],
 "RNOD": [("id", "i32"), ("positionCm", "pos"), ("elevationCm", "i32"), ("flags", "u8"), ("kind", "i8"), ("pad", "pad2"),
          ("chokepointId", "i32")],
 "REDG": [("id", "i32"), ("a", "i32"), ("b", "i32"), ("lengthCm", "i32"), ("travelCost", "i32"), ("climbCmAB", "i32"),
          ("climbCmBA", "i32"), ("elevDeltaCm", "i32"), ("controlTownId", "i32"), ("territoryId", "i32"), ("widthClass", "i8"),
          ("surface", "i8"), ("condition", "i8"), ("highwayClass", "i8"), ("flags", "u16"), ("pad", "pad2"), ("geometry", "pts"),
          ("name", "str")],
 "CHOK": [("id", "i32"), ("kind", "i8"), ("flags", "u8"), ("widthClass", "i8"), ("pad", "pad1"), ("positionCm", "pos"),
          ("elevationCm", "i32"), ("roadNodeId", "i32"), ("roadEdgeId", "i32"), ("widthCm", "i32"), ("defensibility", "i16"),
          ("detourRatioPpt", "i16"), ("approachReliefCmA", "i32"), ("approachReliefCmB", "i32"), ("sideATerritoryId", "i32"),
          ("sideBTerritoryId", "i32"), ("gatesTerritoryIds", "ids"), ("relatedTownId", "i32"), ("riverId", "i32"),
          ("alsoNodeIds", "ids"), ("bypassMaxGradePpt", "u16"), ("pad2", "pad2")],
 "RIVR": [("id", "i32"), ("kind", "i8"), ("flags", "u8"), ("pad", "pad2"), ("widthCm", "i32"), ("polylineCm", "pts"), ("name", "str"),
          ("pad2", "pad4")],
 "BRDG": [("id", "i32"), ("roadEdgeId", "i32"), ("nodeAId", "i32"), ("nodeBId", "i32"), ("riverId", "i32"), ("lengthCm", "i32"),
          ("deckElevationCm", "i32"), ("widthClass", "i8"), ("condition", "i8"), ("flags", "u16")],
 "WATR": [("id", "i32"), ("kind", "i8"), ("flags", "u8"), ("pad", "pad2"), ("areaHa", "i32"), ("ringIds", "ids"), ("name", "str")],
 "WRNG": [("id", "i32"), ("waterBodyId", "i32"), ("role", "i8"), ("pad", "pad3"), ("ringCm", "pts")],
 "MSET": [("id", "i32"), ("positionCm", "pos"), ("nearestRoadNodeId", "i32"), ("name", "str")],
 "RSRC": [("id", "i32"), ("kind", "i8"), ("pad", "pad3"), ("positionCm", "pos"), ("areaM2", "i32"), ("nearestRoadNodeId", "i32"),
          ("roadNodeDistCm", "i32"), ("nearestTownId", "i32"), ("name", "str")],
 "CELL": [("elevationCm", "i32"), ("slopePpt", "i16"), ("class", "i8"), ("pad", "pad1")],
 "CELX": [("territoryId", "i32"), ("steepFractionPpt", "i16"), ("waterFractionPpt", "i16"), ("coverPpt", "i16"), ("landCover", "i8"),
          ("edgeBlockMask", "u8")],
}
RAW = {"MVSL": ("uint16 LE per-mille slope, 1800x1800, row 0 = south, index y*w+x", 2),
       "MVLC": ("uint8 land-cover code (see META provenance / docs/map/wrmb-layout-v0.md), 1800x1800", 1),
       "MVFD": ("OSM ford flag bitset (bit i = byte i>>3, bit i&7), 1800x1800", 1),
       "MVEL": ("uint16 LE elevation: elevationCm = META.elevOffsetCm + value * META.elevUnitCm (10 = decimetres), 1800x1800", 2),
       "WTRA": ("art-only water marks (v1.2, NOT a sim/path input): u32 version=1, u32 width, u32 height, u32 fordCount, u8[w*h] codes "
                "(0 land, 1 blocking water == MVLC 6, 2 fordable water, 3 ford), zero pad to 8, fordCount x {u32 cellIndex, u8 osmType "
                "(0 none, 1 node, 2 way, 3 relation), pad3, u64 osmId}", 1)}
ORDER = ["META", "SRCS", "GRID", "TOWN", "TDBG", "TERR", "RNOD", "REDG", "CHOK", "RIVR", "BRDG", "WATR", "WRNG", "MSET", "RSRC", "CELL",
         "CELX", "MVSL", "MVLC", "MVFD", "MVEL", "WTRA", "I32P", "PTSP", "STRP"]
TERRAIN_PAYLOAD = ["MVSL", "MVLC", "MVFD", "MVEL"]     # terrainPayloadSha256 = SHA-256 of these section bytes concatenated in this order
MAIN_SECTIONS = ["META", "SRCS", "GRID", "TOWN", "TERR", "RNOD", "REDG", "CHOK", "RIVR", "BRDG", "WATR", "WRNG", "MSET", "RSRC", "CELL", "CELX"]
TERRAIN_SECTIONS = ["META", "SRCS", "GRID"] + TERRAIN_PAYLOAD
# v1.2 optional terrain section WTRA (art-only water marks + ford list): self-describing, no GRID descriptor, NOT part of
# terrainPayloadSha256; sims skip it (unknown tag). Absent in v0 / v1.0 / v1.1 files.
WTRA_VERSION = 1
WTRA_CODES = {0: "land", 1: "blocking_water", 2: "fordable_water", 3: "ford"}
WTRA_OSM_TYPE = {0: "", 1: "n", 2: "w", 3: "r"}
def encode_wtra(width, height, codes, fords):
    """codes: bytes (w*h, row 0 = south); fords: [{"cellIndex", "osmType" ('n'/'w'/'r'/''), "osmId"}] sorted by cellIndex."""
    inv = {v: k for k, v in WTRA_OSM_TYPE.items()}
    out = bytearray(struct.pack("<IIII", WTRA_VERSION, width, height, len(fords))) + bytes(codes)
    out += b"\0" * ((-len(out)) % 8)
    for f in fords: out += struct.pack("<IB3xQ", f["cellIndex"], inv[f["osmType"]], f["osmId"])
    return bytes(out)
def decode_wtra(buf):
    """-> {"version", "width", "height", "codes": bytes, "fords": [{"cellIndex", "osmType", "osmId"}]}; raises BlobError."""
    if len(buf) < 16: raise BlobError("WTRA shorter than its header")
    ver, w, h, n = struct.unpack_from("<IIII", buf, 0)
    if ver != WTRA_VERSION: raise BlobError(f"WTRA version {ver}")
    o = 16 + w * h; o += (-o) % 8
    if len(buf) != o + 16 * n: raise BlobError(f"WTRA length {len(buf)} != {o + 16 * n}")
    fords = []
    for k in range(n):
        c, t, i = struct.unpack_from("<IB3xQ", buf, o + 16 * k)
        if t not in WTRA_OSM_TYPE: raise BlobError(f"WTRA ford {k}: osmType {t}")
        fords.append({"cellIndex": c, "osmType": WTRA_OSM_TYPE[t], "osmId": i})
    return {"version": ver, "width": w, "height": h, "codes": bytes(buf[16:16 + w * h]), "fords": fords}
NO_DEFENSE_GROUP = 255     # CHOK.defenseGroupId of an internal chokepoint (v1.2; u8 in the former pad byte at offset 7)
def defense_groups(model):
    """Derive the defense-group table from CHOK (v1.2): [{id, key = min member stableKey, sidePair, memberChokepointIds}],
    sorted by id. The key is not stored in the file; this derivation is normative (blob-layout-v1.md s4.8)."""
    ch = model["sections"].get("CHOK", [])
    if not ch or "defenseGroupId" not in ch[0]: return None
    g = defaultdict(list)
    for c in ch:
        if c["defenseGroupId"] != NO_DEFENSE_GROUP: g[c["defenseGroupId"]].append(c)
    return [{"id": k, "key": min(c["stableKey"] for c in m), "sidePair": sorted({(c["sideATerritoryId"], c["sideBTerritoryId"]) for c in m}),
             "memberChokepointIds": sorted(c["id"] for c in m)} for k, m in sorted(g.items())]
MAX_DEFENSE_GROUPS = 254   # ids 0..253; 255 = none; 254 is reserved so a u8 overflow can never alias "no group"
def check_defense_groups(chok):
    """Validator rules for CHOK.defenseGroupId (final v1). Returns a list of error strings."""
    errs = []
    for c in chok:
        border = c["sideATerritoryId"] > 0 and c["sideBTerritoryId"] > 0 and c["sideATerritoryId"] != c["sideBTerritoryId"]
        g = c["defenseGroupId"]
        if not border and g != NO_DEFENSE_GROUP: errs.append(f"CHOK {c['id']}: internal chokepoint has defenseGroupId {g}")
        if border and g == NO_DEFENSE_GROUP: errs.append(f"CHOK {c['id']}: border chokepoint without a defense group")
    groups = defaultdict(list)
    for c in chok:
        if c["defenseGroupId"] != NO_DEFENSE_GROUP: groups[c["defenseGroupId"]].append(c)
    if len(groups) > MAX_DEFENSE_GROUPS or any(g >= MAX_DEFENSE_GROUPS for g in groups):
        errs.append(f"CHOK.defenseGroupId: {len(groups)} groups (max id {max(groups)}) exceeds the limit of {MAX_DEFENSE_GROUPS} groups (ids 0..253)")
    ids = sorted(groups)
    if ids != list(range(len(ids))): errs.append("CHOK.defenseGroupId: ids not dense 0..N-1")
    mins = [min(c["id"] for c in groups[g]) for g in ids]
    if mins != sorted(mins): errs.append("CHOK.defenseGroupId: ids not in order of each group's lowest member chokepoint id")
    for g in ids:
        ps = {tuple(sorted((c["sideATerritoryId"], c["sideBTerritoryId"]))) for c in groups[g]}
        if len(ps) != 1: errs.append(f"defense group {g}: members have different side pairs {sorted(ps)}")
    return errs
# ---- cliff / crossing rule (final v1; shared by the pipeline S9 CI and validate_pair; blob-layout-v1.md s4.6)
CLIFF_PPT = 1000               # MVSL >= this: cliff, closed to foot and wheeled units
CLIFF_SPAN_WINDOW_CELLS = 25   # border span: MVLC-6 cell with bank cells of >= 2 territories within this Chebyshev distance
CLIFF_MAX_CROSSING_DIST_M = 1000   # a span-adjacent cliff fails only within this straight-line distance of a bridgehead or ford
def cliff_crossing_check(w, h, mvsl, mvlc, ford_cells, head_cells, cell_territory, grid_w, cell_num_cm, cell_den,
                         cliff_ppt=CLIFF_PPT, span_window=CLIFF_SPAN_WINDOW_CELLS, max_dist_m=CLIFF_MAX_CROSSING_DIST_M):
    """mvsl: u16 LE bytes (w*h); mvlc: u8 bytes; ford_cells / head_cells: iterables of movement cell indices (MVFD cells /
    cells of BridgeHead road nodes); cell_territory: 1-mile CELX territory list (grid_w x grid_w; movement cell -> its 1-mile cell).
    A cliff cell (MVSL >= cliff_ppt) that is inside or 8-adjacent to a blocking-river border span fails only if it lies within
    max_dist_m (straight line between cell centres, cell size cell_num_cm / cell_den cm) of a bridgehead or ford cell.
    Border span = MVLC-6 cells with bank cells (non-6 cells 8-adjacent to a 6 cell) of >= 2 different non-zero territories within
    Chebyshev distance span_window. Returns {"cliffCells", "spanAdjacentCliffs": [...], "failing": [...]}"""
    import array
    sl = array.array("H"); sl.frombytes(mvsl)
    if sys.byteorder != "little": sl.byteswap()
    k = w // grid_w
    def terr(i): return cell_territory[((i // w) // k) * grid_w + (i % w) // k]
    def nb(i):
        y, x = divmod(i, w)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if (dy or dx) and 0 <= y + dy < h and 0 <= x + dx < w: yield (y + dy) * w + x + dx
    def is_bank(i): return mvlc[i] != 6 and any(mvlc[j] == 6 for j in nb(i))
    memo = {}
    def is_span(i):
        if i not in memo:
            y, x = divmod(i, w); ts = set()
            for yy in range(max(0, y - span_window), min(h, y + span_window + 1)):
                for xx in range(max(0, x - span_window), min(w, x + span_window + 1)):
                    j = yy * w + xx
                    if mvlc[j] != 6 and is_bank(j):
                        t = terr(j)
                        if t: ts.add(t)
                        if len(ts) >= 2: break
                if len(ts) >= 2: break
            memo[i] = len(ts) >= 2
        return memo[i]
    cliffs = [i for i in range(w * h) if sl[i] >= cliff_ppt]
    cross = [(c, "bridgehead") for c in sorted(set(head_cells))] + [(c, "ford") for c in sorted(set(ford_cells))]
    cm = cell_num_cm / cell_den
    out, fail = [], []
    for c in cliffs:
        zone = [c] + list(nb(c))
        if not any(mvlc[j] == 6 and is_span(j) for j in zone): continue
        y, x = divmod(c, w)
        d, cc, kind = min(((((x - q % w) ** 2 + (y - q // w) ** 2) ** 0.5) * cm / 100, q, kd) for q, kd in cross) if cross else (float("inf"), -1, "")
        r = {"cellIndex": c, "slopePpt": sl[c], "nearestCrossingM": round(d, 1), "nearestCrossing": kind, "nearestCrossingCell": cc,
             "fails": d <= max_dist_m}
        out.append(r)
        if r["fails"]: fail.append(c)
    return {"cliffPpt": cliff_ppt, "spanWindowCells": span_window, "maxCrossingDistM": max_dist_m, "cliffCells": len(cliffs),
            "spanAdjacentCliffs": out, "failing": fail}
def cliff_check_model(main, terrain):
    """cliff_crossing_check on a decoded (main, terrain) pair: heads = BridgeHead RNOD cells, fords = MVFD cells, territory = CELX."""
    M = terrain["sections"]["META"][0]; S = terrain["sections"]; w, h = M["moveWidth"], M["moveHeight"]
    g = {x["sectionTag"]: x for x in S["GRID"]}["MVLC"]
    fdb = S["MVFD"]; fords = [i for i in range(w * h) if fdb[i >> 3] >> (i & 7) & 1]
    E = M["mapWidthCm"]
    heads = [min(n["positionCm"][1] * h // E, h - 1) * w + min(n["positionCm"][0] * w // E, w - 1)
             for n in main["sections"]["RNOD"] if n["flags"] & RNOD_FLAGS["BridgeHead"]]
    ct = [c["territoryId"] for c in main["sections"]["CELX"]]
    return cliff_crossing_check(w, h, S["MVSL"], S["MVLC"], fords, heads, ct, main["sections"]["META"][0]["gridWidth"],
                                g["cellSizeNumCm"], g["cellSizeDen"])
def water_art(model):
    """Expose the terrain file's WTRA section (None if absent, e.g. v0)."""
    b = model["sections"].get("WTRA")
    return decode_wtra(b) if b is not None else None
GRID_ELEM = {"struct": 0, "u8": 1, "u16le": 2, "bitset": 3}      # GRID.elemType
GRID_ROW_SOUTH_FIRST = 0                                        # GRID.rowOrder
POOL_ELEM = {"I32P": 4, "PTSP": 8, "STRP": 1}
ID_SECTIONS_ALL = ["SRCS", "TOWN", "TERR", "RNOD", "REDG", "CHOK", "RIVR", "BRDG", "WATR", "WRNG", "MSET", "RSRC"]
RNOD_FLAGS = {"TownGate": 1, "BridgeHead": 2, "Chokepoint": 4, "Way": 8, "TerritoryBorder": 16, "Pass": 32, "MapBorder": 64, "Cluster": 128}
REDG_FLAGS = {"bridge": 1, "bridgeSpan": 2, "tunnel": 4, "destructible": 8, "repairable": 16, "ford": 32, "mainRiverBridge": 64,
              "crossesBlockingWater": 128}

def fmt_of(fields):
    f = "<"
    for _, t in fields:
        if t in PRIM: f += PRIM[t]
        elif t == "pos": f += "ii"
        elif t == "b32": f += "32s"
        elif t == "tag4": f += "4s"
        elif t in ("str", "ids", "pts"): f += "II"
        elif t.startswith("pad"): f += f"{int(t[3:])}x"
    return f
SCHEMA_V0 = SCHEMA
def _v1(schema):
    s = {k: list(v) for k, v in schema.items()}
    s["TERR"] = s["TERR"] + [("stableKey", "u64")]
    s["BRDG"] = s["BRDG"] + [("stableKey", "u64")]
    i = [n for n, _ in s["CHOK"]].index("pad2")
    s["CHOK"] = s["CHOK"][:i] + [("bypassSurfaceClass", "u8"), ("pad2", "pad1"), ("stableKey", "u64")]
    s["CHOK"] = [("defenseGroupId", "u8") if n == "pad" else ("bypassWheeledBlocked", "u8") if n == "pad2" else (n, t)
                 for n, t in s["CHOK"]]   # final v1: defenseGroupId @7 (was pad1), bypassWheeledBlocked @79 (was pad2); 88 B
    s["RNOD"] = [("territoryId", "u16") if n == "pad" else (n, t) for n, t in s["RNOD"]]   # v1: node territory (was pad2)
    return s
SCHEMAS = {0: SCHEMA_V0, 1: _v1(SCHEMA_V0)}
FMTS = {fv: {k: fmt_of(v) for k, v in sc.items()} for fv, sc in SCHEMAS.items()}
SIZES = {fv: {k: struct.calcsize(v) for k, v in f.items()} for fv, f in FMTS.items()}
SCHEMA, FMT, SIZE = SCHEMAS[FORMAT_VERSION], FMTS[FORMAT_VERSION], SIZES[FORMAT_VERSION]   # default (writer) layout
def fnv1a32(s):
    h = 0x811C9DC5
    for b in s.encode("utf-8"): h = ((h ^ b) * 0x01000193) & 0xFFFFFFFF
    return h - (1 << 32) if h >= 1 << 31 else h

# ---------------------------------------------------------------- encode
def encode(model, flags, fv=FORMAT_VERSION):
    """model: {"sections": {tag: [records] | bytes}}; records use expanded lists/strings. Pools filled in ORDER, record id order,
    field order. Returns bytes (content hash filled). formatVersion 1 always carries header flag bit2 V1_FINAL."""
    if fv == 1: flags |= FLAG_V1_FINAL
    i32p, ptsp, strp = [], [], bytearray()
    blobs = {}
    for tag in ORDER:
        if tag in POOL_ELEM: continue
        sec = model["sections"].get(tag)
        if sec is None: continue
        if tag in RAW: blobs[tag] = (bytes(sec), len(sec) // RAW[tag][1], RAW[tag][1]); continue
        out = bytearray(); st = struct.Struct(FMTS[fv][tag])
        for r in sec:
            vals = []
            for name, t in SCHEMAS[fv][tag]:
                if t.startswith("pad"): continue
                v = r[name]
                if t == "pos": vals += [v[0], v[1]]
                elif t == "b32": vals.append(bytes.fromhex(v))
                elif t == "tag4": vals.append(v.encode("ascii"))
                elif t == "str":
                    b = v.encode("utf-8"); vals += [len(strp), len(b)]; strp += b
                elif t == "ids": vals += [len(i32p), len(v)]; i32p += v
                elif t == "pts": vals += [len(ptsp), len(v)]; ptsp += [tuple(p) for p in v]
                else: vals.append(v)
            try: out += st.pack(*vals)
            except struct.error as ex: raise ValueError(f"{tag} record {r.get('id', '?')}: {ex}: {vals}") from None
        blobs[tag] = (bytes(out), len(sec), SIZES[fv][tag])
    blobs["I32P"] = (struct.pack(f"<{len(i32p)}i", *i32p), len(i32p), 4)
    blobs["PTSP"] = (struct.pack(f"<{2 * len(ptsp)}i", *[c for p in ptsp for c in p]), len(ptsp), 8)
    blobs["STRP"] = (bytes(strp), len(strp), 1)
    tags = [t for t in ORDER if t in blobs]
    off = HEADER_SIZE + DIR_SIZE * len(tags); off += (-off) % ALIGN
    body, dirs = bytearray(), []
    start = off
    for t in tags:
        data, cnt, es = blobs[t]
        dirs.append(struct.pack(DIR_FMT, t.encode(), 0, off, len(data), cnt, es, zlib.crc32(data) & 0xFFFFFFFF, 0))
        body += data; pad = (-len(data)) % ALIGN; body += b"\0" * pad; off += len(data) + pad
    dir_bytes = b"".join(dirs)
    pre = b"\0" * (start - HEADER_SIZE - len(dir_bytes))
    total = start + len(body)
    hdr0 = struct.pack(HEADER_FMT, MAGIC, fv, SCHEMA_VERSION, HEADER_SIZE, flags, len(tags), HEADER_SIZE, total, b"\0" * 32)
    blob = hdr0 + dir_bytes + pre + bytes(body)
    h = hashlib.sha256(blob).digest()
    return struct.pack(HEADER_FMT, MAGIC, fv, SCHEMA_VERSION, HEADER_SIZE, flags, len(tags), HEADER_SIZE, total, h) + blob[HEADER_SIZE:]

# ---------------------------------------------------------------- decode
class BlobError(Exception): pass
def decode(buf):
    if len(buf) < HEADER_SIZE: raise BlobError("file shorter than header")
    magic, fv, sv, hs, flags, nsec, doff, fsize, h = struct.unpack_from(HEADER_FMT, buf, 0)
    if magic != MAGIC: raise BlobError(f"bad magic {magic!r}")
    if fv not in SUPPORTED_FORMAT_VERSIONS: raise BlobError(f"unsupported formatVersion {fv}")
    if fv == 1 and not flags & FLAG_V1_FINAL:
        raise BlobError("formatVersion 1 file without header flag bit2 V1_FINAL: stale pre-final v1 layout (built before the final v1 "
                        "definition); rebuild it - it must not be read with the final v1 schema")
    SC, FM, SZ = SCHEMAS[fv], FMTS[fv], SIZES[fv]
    if hs != HEADER_SIZE or doff != HEADER_SIZE: raise BlobError("unexpected header/directory offset")
    if fsize != len(buf): raise BlobError(f"fileSize {fsize} != actual {len(buf)}")
    z = bytearray(buf); z[HEADER_SIZE - 32:HEADER_SIZE] = b"\0" * 32
    if hashlib.sha256(bytes(z)).digest() != h: raise BlobError("contentHash mismatch")
    dirs = {}
    for i in range(nsec):
        tag, _, off, ln, cnt, es, crc, _ = struct.unpack_from(DIR_FMT, buf, HEADER_SIZE + i * DIR_SIZE)
        tag = tag.decode()
        if off % ALIGN or off + ln > len(buf): raise BlobError(f"{tag}: bad offset/length")
        if zlib.crc32(buf[off:off + ln]) & 0xFFFFFFFF != crc: raise BlobError(f"{tag}: crc32 mismatch")
        if cnt * es != ln: raise BlobError(f"{tag}: count*elementSize != byteLength")
        if tag in SC and es != SZ[tag]: raise BlobError(f"{tag}: elementSize {es} != schema v{fv} {SZ[tag]}")
        if tag in RAW and es != RAW[tag][1]: raise BlobError(f"{tag}: elementSize {es} != {RAW[tag][1]} (raw section)")
        if tag in POOL_ELEM and es != POOL_ELEM[tag]: raise BlobError(f"{tag}: elementSize {es} != {POOL_ELEM[tag]} (pool)")
        if tag in dirs: raise BlobError(f"duplicate section {tag}")
        dirs[tag] = (off, ln, cnt, es)
    def raw(tag):
        o, l, _, _ = dirs[tag]; return buf[o:o + l]
    i32p = struct.unpack(f"<{dirs['I32P'][2]}i", raw("I32P"))
    pp = struct.unpack(f"<{2 * dirs['PTSP'][2]}i", raw("PTSP"))
    strp = raw("STRP")
    secs = {}
    for tag in ORDER:
        if tag not in dirs or tag in POOL_ELEM: continue
        if tag in RAW: secs[tag] = bytes(raw(tag)); continue
        if tag not in SC: continue       # unknown sections are skipped (forward compatible)
        o, l, cnt, es = dirs[tag]; st = struct.Struct(FM[tag]); recs = []
        for k in range(cnt):
            vals = list(st.unpack_from(buf, o + k * es)); r = {}; j = 0
            for name, t in SC[tag]:
                if t.startswith("pad"): continue
                if t == "pos": r[name] = [vals[j], vals[j + 1]]; j += 2
                elif t == "b32": r[name] = vals[j].hex(); j += 1
                elif t == "tag4": r[name] = vals[j].decode("ascii"); j += 1
                elif t in ("str", "ids", "pts"):
                    a, n = vals[j], vals[j + 1]; j += 2
                    if t == "str":
                        if a + n > len(strp): raise BlobError(f"{tag}.{name}: string span out of bounds")
                        r[name] = bytes(strp[a:a + n]).decode("utf-8")
                    elif t == "ids":
                        if a + n > len(i32p): raise BlobError(f"{tag}.{name}: id span out of bounds")
                        r[name] = list(i32p[a:a + n])
                    else:
                        if 2 * (a + n) > len(pp): raise BlobError(f"{tag}.{name}: point span out of bounds")
                        r[name] = [[pp[2 * q], pp[2 * q + 1]] for q in range(a, a + n)]
                else: r[name] = vals[j]; j += 1
            recs.append(r)
        secs[tag] = recs
    return {"header": {"magic": magic.decode(), "formatVersion": fv, "schemaVersion": sv, "headerSize": hs, "flags": flags,
                       "sectionCount": nsec, "fileSize": fsize, "contentHash": h.hex()},
            "directory": {t: {"offset": v[0], "byteLength": v[1], "count": v[2], "elementSize": v[3]} for t, v in dirs.items()},
            "sections": secs}

def debug_view(model):
    """JSON-safe view: raw grid arrays replaced by descriptors (sha256/bytes)."""
    out = {"sections": {}}
    for t, v in model["sections"].items():
        out["sections"][t] = {"rawArray": True, "bytes": len(v), "sha256": hashlib.sha256(v).hexdigest(), "encoding": RAW[t][0]} \
            if isinstance(v, (bytes, bytearray)) else v
    return out

# ---------------------------------------------------------------- invariants
NO_SURFACE = 255           # CHOK.bypassSurfaceClass when there is no bypass (v1)
NO_BYPASS = 65535          # CHOK.bypassMaxGradePpt when no cross-country bypass exists in the search region
MAX_GRADE_PPT = 65534
def terrain_payload_sha256(sections):
    h = hashlib.sha256()
    for t in TERRAIN_PAYLOAD: h.update(sections[t])
    return h.hexdigest()
def grid_index(x_cm, y_cm, g):
    """cell index rule for a GRID descriptor g: floor(x * width / extentX); never assume a round cell size."""
    cx = min(max((x_cm - g["originXCm"]) * g["width"] // g["extentXCm"], 0), g["width"] - 1)
    cy = min(max((y_cm - g["originYCm"]) * g["height"] // g["extentYCm"], 0), g["height"] - 1)
    return cy * g["width"] + cx
def _gcd(a, b):
    while b: a, b = b, a % b
    return a
def validate(model):
    """Per-file invariants (dispatch on META.fileKind). Pair checks: validate_pair()."""
    S = model["sections"]; errs = []; info = {}
    def err(m):
        if len(errs) < 200: errs.append(m)
    for t in ("META", "SRCS", "GRID"):
        if t not in S: err(f"missing section {t}"); return errs, info
    M = S["META"][0]; kind = M["fileKind"]; info["fileKind"] = kind
    if model["header"]["schemaVersion"] != M["schemaVersion"]: err("header/META schemaVersion differ")
    if hashlib.sha256(M["attribution"].encode()).hexdigest() != M["attributionSha256"]: err("attributionSha256 mismatch")
    if len(S["SRCS"]) != M["sourceCount"]: err("META.sourceCount mismatch")
    if [r["id"] for r in S["SRCS"]] != list(range(1, len(S["SRCS"]) + 1)): err("SRCS ids not dense")
    if M["elevUnitCm"] <= 0: err("elevUnitCm <= 0")
    want = MAIN_SECTIONS if kind == FILE_MAIN else TERRAIN_SECTIONS if kind == FILE_TERRAIN else None
    if want is None: err(f"unknown fileKind {kind}"); return errs, info
    missing = [t for t in want if t not in S]
    if missing: err(f"missing sections {missing}"); return errs, info
    # GRID descriptors: exact rational cell size, extents = map, byte sizes
    for g in S["GRID"]:
        t = g["sectionTag"]
        if t not in S: err(f"GRID {t}: section absent"); continue
        if (g["originXCm"], g["originYCm"]) != (0, 0) or (g["extentXCm"], g["extentYCm"]) != (M["mapWidthCm"], M["mapHeightCm"]): err(f"GRID {t}: origin/extent")
        if g["rowOrder"] != GRID_ROW_SOUTH_FIRST: err(f"GRID {t}: rowOrder")
        n, d = g["cellSizeNumCm"], g["cellSizeDen"]
        if d <= 0 or _gcd(n, d) != 1 or n * g["width"] != g["extentXCm"] * d or n * g["height"] != g["extentYCm"] * d: err(f"GRID {t}: cellSize {n}/{d} not exact reduced extent/width")
        cnt = g["width"] * g["height"]; v = S[t]
        if g["elemType"] == GRID_ELEM["struct"]:
            if len(v) != cnt or g["elemBytes"] != SIZES[model["header"]["formatVersion"]][t]: err(f"GRID {t}: record count/size")
        else:
            nb = (cnt + 7) // 8 if g["elemType"] == GRID_ELEM["bitset"] else cnt * g["elemBytes"]
            if len(v) != nb: err(f"GRID {t}: byte length {len(v)} != {nb}")
    gt = {g["sectionTag"]: g for g in S["GRID"]}
    if "CELL" in gt and (gt["CELL"]["width"], gt["CELL"]["height"]) != (M["gridWidth"], M["gridHeight"]): err("GRID CELL dims != META grid")
    if kind == FILE_MAIN:
        _validate_main(model, S, M, err, info)
        if M["pairedContentSha256"] != "0" * 64: err("main META.pairedContentSha256 must be zero (the terrain file points at the main file)")
    else:
        for key in ("townCount", "territoryCount", "roadNodeCount", "roadEdgeCount", "chokepointCount", "riverCount", "bridgeCount",
                    "waterBodyCount", "minorSettlementCount", "resourceCount"):
            if M[key]: err(f"terrain META.{key} != 0")
        if sorted(gt) != sorted(TERRAIN_PAYLOAD): err(f"terrain GRID descriptors {sorted(gt)} != {sorted(TERRAIN_PAYLOAD)}")
        for t in TERRAIN_PAYLOAD:
            if t in gt and (gt[t]["width"], gt[t]["height"]) != (M["moveWidth"], M["moveHeight"]): err(f"GRID {t}: dims != META move grid")
        if S["MVLC"] and max(S["MVLC"]) > 7: err("MVLC code > 7")
        if terrain_payload_sha256(S) != M["terrainPayloadSha256"]: err("terrainPayloadSha256 != SHA-256 of MVSL|MVLC|MVFD|MVEL")
        if "MVEL" in gt and (gt["MVEL"]["valueOffset"], gt["MVEL"]["valueScale"]) != (M["elevOffsetCm"], M["elevUnitCm"]): err("GRID MVEL offset/scale != META")
        if M["pairedContentSha256"] == "0" * 64: err("terrain META.pairedContentSha256 is zero")
        if "WTRA" not in S and model["header"]["formatVersion"] >= 1:
            err("terrain file formatVersion 1 without WTRA (required in final v1; a pre-final v1 terrain file)")
        if "WTRA" in S:   # v1.2 art-only water marks: must agree exactly with the sim's movement data
            try: A = decode_wtra(S["WTRA"])
            except BlobError as e: err(str(e)); A = None
            if A is not None:
                n = M["moveWidth"] * M["moveHeight"]; lcv = S["MVLC"]; fdv = S["MVFD"]; cod = A["codes"]
                if (A["width"], A["height"]) != (M["moveWidth"], M["moveHeight"]): err("WTRA dims != META move grid")
                elif len(lcv) == n:
                    fbit = lambda i: (fdv[i >> 3] >> (i & 7)) & 1
                    if max(cod) > 3: err("WTRA code > 3")
                    blk_bad = sum(1 for i in range(n) if (cod[i] == 1) != (lcv[i] == 6))
                    if blk_bad: err(f"WTRA blocking-water cells != MVLC impassable water (code 6) at {blk_bad} cells")
                    ford_bad = sum(1 for i in range(n) if (cod[i] == 3) != bool(fbit(i)))
                    if ford_bad: err(f"WTRA ford cells != MVFD at {ford_bad} cells")
                    fw_bad = sum(1 for i in range(n) if cod[i] == 2 and lcv[i] == 6)
                    if fw_bad: err(f"WTRA fordable water on impassable cells: {fw_bad}")
                    fc = [f["cellIndex"] for f in A["fords"]]
                    if fc != sorted(set(fc)): err("WTRA ford list not strictly sorted by cellIndex")
                    if fc != [i for i in range(n) if fbit(i)]: err("WTRA ford list cells != MVFD cells")
                    if any(f["osmType"] and f["osmId"] <= 0 for f in A["fords"]): err("WTRA ford with osmType but no osmId")
                    info["wtra"] = {"codeCounts": {WTRA_CODES[k]: cod.count(k) for k in range(4)}, "fords": len(fc),
                                    "blockingEqualsMvlcWater": blk_bad == 0}
    info["counts"] = {t: len(v) for t, v in S.items()}
    return errs, info
def validate_pair(main, terrain, main_name, terrain_name):
    errs = []; info = {}
    Mm, Mt = main["sections"]["META"][0], terrain["sections"]["META"][0]
    if (Mm["fileKind"], Mt["fileKind"]) != (FILE_MAIN, FILE_TERRAIN): errs.append("fileKind pair != (main, terrain)"); return errs, info
    if Mt["pairedContentSha256"] != main["header"]["contentHash"]: errs.append("terrain.pairedContentSha256 != main header contentHash")
    if Mm["terrainPayloadSha256"] != Mt["terrainPayloadSha256"] or Mm["terrainPayloadSha256"] != terrain_payload_sha256(terrain["sections"]):
        errs.append("terrainPayloadSha256 mismatch between files / payload")
    if Mm["pairedFileName"] != terrain_name: errs.append(f"main.pairedFileName {Mm['pairedFileName']!r} != {terrain_name!r}")
    if Mt["pairedFileName"] != main_name: errs.append(f"terrain.pairedFileName {Mt['pairedFileName']!r} != {main_name!r}")
    for k in ("schemaVersion", "mapWidthCm", "mapHeightCm", "cellSizeCm", "gridWidth", "gridHeight", "moveWidth", "moveHeight",
              "moveCellsPerCell", "transformSeed", "mapId", "pipelineVersion", "transformVersion", "configSha256", "osmClipSha256",
              "attributionSha256", "elevOffsetCm", "elevUnitCm", "projectionType", "centerLatMicroDeg", "centerLonMicroDeg"):
        if Mm[k] != Mt[k]: errs.append(f"META.{k} differs between main and terrain")
    if main["sections"]["SRCS"] != terrain["sections"]["SRCS"]: errs.append("SRCS differ between main and terrain")
    sl = terrain["sections"]["MVSL"]; mx = max(struct.unpack(f"<{len(sl) // 2}H", sl)) if sl else 0
    bad = [c["id"] for c in main["sections"]["CHOK"] if c["bypassMaxGradePpt"] != NO_BYPASS and c["bypassMaxGradePpt"] > mx]
    if bad: errs.append(f"CHOK bypassMaxGradePpt above max MVSL for {bad}")
    info["noBypassChokepoints"] = sum(c["bypassMaxGradePpt"] == NO_BYPASS for c in main["sections"]["CHOK"])
    if main["header"]["formatVersion"] >= 1:   # final v1: cliff next to a blocking-river border span near a crossing
        cc = cliff_check_model(main, terrain)
        info["cliffCheck"] = {k: v for k, v in cc.items() if k != "spanAdjacentCliffs"} | {"spanAdjacentCliffs": len(cc["spanAdjacentCliffs"]),
                              "nearestCrossingM": min((r["nearestCrossingM"] for r in cc["spanAdjacentCliffs"]), default=None)}
        if cc["failing"]: errs.append(f"cliff cells (MVSL >= {CLIFF_PPT}) next to a blocking-river border span within {CLIFF_MAX_CROSSING_DIST_M} m of a bridgehead/ford: {cc['failing'][:20]}")
    return errs, info

def _validate_main(model, S, M, err, info):
    ID_SECTIONS = ID_SECTIONS_ALL
    for tag, key in (("TOWN", "townCount"), ("TERR", "territoryCount"), ("RNOD", "roadNodeCount"), ("REDG", "roadEdgeCount"),
                     ("CHOK", "chokepointCount"), ("RIVR", "riverCount"), ("BRDG", "bridgeCount"), ("WATR", "waterBodyCount"),
                     ("MSET", "minorSettlementCount"), ("RSRC", "resourceCount"), ("SRCS", "sourceCount")):
        if len(S[tag]) != M[key]: err(f"META.{key}={M[key]} but {tag} has {len(S[tag])}")
    for tag in ID_SECTIONS:
        ids = [r["id"] for r in S[tag]]
        if ids != list(range(1, len(ids) + 1)): err(f"{tag}: ids not dense/sorted 1..n")
    ids = {t: set(range(1, len(S[t]) + 1)) for t in ID_SECTIONS}
    W, G = M["mapWidthCm"], M["gridWidth"]
    if W != G * M["cellSizeCm"] or M["mapHeightCm"] != M["gridHeight"] * M["cellSizeCm"]: err("map extent != grid x cellSizeCm")
    def ref(v, tag, what, zero_ok=False):
        if v == 0 and zero_ok: return
        if v not in ids[tag]: err(f"{what}: invalid {tag} ref {v}")
    def sorted_unique(l, what):
        if l != sorted(set(l)): err(f"{what}: list not sorted/unique")
    def inb(p, what):
        if not (0 <= p[0] <= W and 0 <= p[1] <= M["mapHeightCm"]): err(f"{what}: position out of map {p}")
    nodes, edges = S["RNOD"], S["REDG"]
    inc = defaultdict(list)
    prev = None
    for e in edges:
        if not e["a"] < e["b"]: err(f"REDG {e['id']}: a >= b")
        ref(e["a"], "RNOD", f"REDG {e['id']}.a"); ref(e["b"], "RNOD", f"REDG {e['id']}.b")
        ref(e["controlTownId"], "TOWN", f"REDG {e['id']}.controlTownId", True); ref(e["territoryId"], "TERR", f"REDG {e['id']}.territoryId", True)
        if prev and (e["a"], e["b"]) < prev: err(f"REDG {e['id']}: not sorted by (a,b)")
        prev = (e["a"], e["b"])
        if e["lengthCm"] <= 0 or e["travelCost"] <= 0: err(f"REDG {e['id']}: non-positive length/cost")
        if len(e["geometry"]) < 2: err(f"REDG {e['id']}: geometry < 2 points")
        if e["a"] in ids["RNOD"] and e["b"] in ids["RNOD"]: inc[e["a"]].append(e); inc[e["b"]].append(e)
    for n in nodes:
        inb(n["positionCm"], f"RNOD {n['id']}"); ref(n["chokepointId"], "CHOK", f"RNOD {n['id']}.chokepointId", True)
        if not inc[n["id"]]: err(f"RNOD {n['id']}: isolated node")
        bh = bool(n["flags"] & RNOD_FLAGS["BridgeHead"])
        if bh and not any(e["flags"] & REDG_FLAGS["bridgeSpan"] for e in inc[n["id"]]): err(f"RNOD {n['id']}: BridgeHead without span edge")
    for e in edges:
        if e["flags"] & REDG_FLAGS["bridgeSpan"]:
            for x in (e["a"], e["b"]):
                if x in ids["RNOD"] and not nodes[x - 1]["flags"] & RNOD_FLAGS["BridgeHead"]: err(f"REDG {e['id']}: span end {x} not BridgeHead")
    # connectivity on passable edges
    par = list(range(len(nodes) + 1))
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for e in edges:
        if e["condition"] < 3 and e["a"] in ids["RNOD"] and e["b"] in ids["RNOD"]: par[f(e["a"])] = f(e["b"])
    comps = len({f(n["id"]) for n in nodes}); info["passableComponents"] = comps
    if comps != 1: err(f"road graph has {comps} passable components")
    towns = S["TOWN"]
    if towns:
        root = f(towns[0]["roadNodeId"]) if towns[0]["roadNodeId"] in ids["RNOD"] else None
        unreach = [t["id"] for t in towns if t["roadNodeId"] not in ids["RNOD"] or f(t["roadNodeId"]) != root]
        info["townGatesUnreachable"] = unreach
        if unreach: err(f"town gates not reachable: {unreach}")
    for t in towns:
        ref(t["territoryId"], "TERR", f"TOWN {t['id']}.territoryId"); ref(t["roadNodeId"], "RNOD", f"TOWN {t['id']}.roadNodeId")
        inb(t["positionCm"], f"TOWN {t['id']}")
        sorted_unique(t["neighborTownIds"], f"TOWN {t['id']}.neighborTownIds"); sorted_unique(t["exitEdgeIds"], f"TOWN {t['id']}.exitEdgeIds")
        for x in t["neighborTownIds"]:
            ref(x, "TOWN", f"TOWN {t['id']}.neighborTownIds")
            if x == t["id"]: err(f"TOWN {t['id']}: self neighbour")
        for x in t["exitEdgeIds"]:
            ref(x, "REDG", f"TOWN {t['id']}.exitEdgeIds")
            if x in ids["REDG"] and t["roadNodeId"] not in (edges[x - 1]["a"], edges[x - 1]["b"]): err(f"TOWN {t['id']}: exit edge {x} not incident to gate")
        if t["roadNodeId"] in ids["RNOD"] and not nodes[t["roadNodeId"] - 1]["flags"] & RNOD_FLAGS["TownGate"]: err(f"TOWN {t['id']}: roadNode not flagged TownGate")
        if t["nameHash"] != fnv1a32(t["name"]): err(f"TOWN {t['id']}: nameHash != fnv1a32(name)")
        if not 0 <= t["strategicValue"] <= 1000 or not 0 <= t["populationClass"] <= 4: err(f"TOWN {t['id']}: value out of range")
    for t in towns:
        for x in t["neighborTownIds"]:
            if x in ids["TOWN"] and t["id"] not in towns[x - 1]["neighborTownIds"]: err(f"TOWN {t['id']}: neighbour {x} not symmetric")
    if [d["townId"] for d in S.get("TDBG", [])] not in ([], [t["id"] for t in towns]): err("TDBG not aligned with TOWN ids")
    terr_of_town = {}
    for tr in S["TERR"]:
        sorted_unique(tr["townIds"], f"TERR {tr['id']}.townIds"); sorted_unique(tr["neighborTerritoryIds"], f"TERR {tr['id']}.neighbors")
        if tr["seatTownId"] not in tr["townIds"]: err(f"TERR {tr['id']}: seat not in townIds")
        for x in tr["townIds"]:
            ref(x, "TOWN", f"TERR {tr['id']}.townIds"); terr_of_town.setdefault(x, []).append(tr["id"])
        for x in tr["neighborTerritoryIds"]:
            ref(x, "TERR", f"TERR {tr['id']}.neighbors")
            if x in ids["TERR"] and tr["id"] not in S["TERR"][x - 1]["neighborTerritoryIds"]: err(f"TERR {tr['id']}: neighbour {x} not symmetric")
    for t in towns:
        if terr_of_town.get(t["id"]) != [t["territoryId"]]: err(f"TOWN {t['id']}: not in exactly its own territory")
    for c in S["CHOK"]:
        ref(c["roadNodeId"], "RNOD", f"CHOK {c['id']}.roadNodeId"); ref(c["roadEdgeId"], "REDG", f"CHOK {c['id']}.roadEdgeId", True)
        ref(c["relatedTownId"], "TOWN", f"CHOK {c['id']}.relatedTownId", True); ref(c["riverId"], "RIVR", f"CHOK {c['id']}.riverId", True)
        sorted_unique(c["gatesTerritoryIds"], f"CHOK {c['id']}.gates"); sorted_unique(c["alsoNodeIds"], f"CHOK {c['id']}.alsoNodeIds")
        for x in c["gatesTerritoryIds"]: ref(x, "TERR", f"CHOK {c['id']}.gates")
        for x in c["alsoNodeIds"]: ref(x, "RNOD", f"CHOK {c['id']}.alsoNodeIds")
        if not ((c["sideATerritoryId"] == 0 == c["sideBTerritoryId"]) or 0 < c["sideATerritoryId"] < c["sideBTerritoryId"]): err(f"CHOK {c['id']}: sideA/B")
        if not (0 <= c["kind"] <= 6 and 0 <= c["defensibility"] <= 1000 and 1000 <= c["detourRatioPpt"] <= 10000): err(f"CHOK {c['id']}: range")
        if not (c["bypassMaxGradePpt"] == NO_BYPASS or c["bypassMaxGradePpt"] <= MAX_GRADE_PPT): err(f"CHOK {c['id']}: bypassMaxGradePpt range")
        if "bypassSurfaceClass" in c:
            if (c["bypassMaxGradePpt"] == NO_BYPASS) != (c["bypassSurfaceClass"] == NO_SURFACE) or not (c["bypassSurfaceClass"] <= 7 or c["bypassSurfaceClass"] == NO_SURFACE):
                err(f"CHOK {c['id']}: bypassSurfaceClass {c['bypassSurfaceClass']} inconsistent with bypassMaxGradePpt")
        if "stableKey" in c and (c["stableKey"] >> 52) & 0xFF != c["kind"] + 1: err(f"CHOK {c['id']}: stableKey discriminator != kind + 1")
        if c["roadNodeId"] in ids["RNOD"] and not nodes[c["roadNodeId"] - 1]["flags"] & RNOD_FLAGS["Chokepoint"]: err(f"CHOK {c['id']}: node not flagged")
        inb(c["positionCm"], f"CHOK {c['id']}")
    if model["header"]["formatVersion"] >= 1:   # stable keys: non-zero, valid OSM type, unique per section
        for tag, types in (("TERR", (1,)), ("BRDG", (2,)), ("CHOK", (1, 2, 3))):
            ks = [r["stableKey"] for r in S[tag]]
            if len(set(ks)) != len(ks): err(f"{tag}: duplicate stableKey")
            for r in S[tag]:
                if r["stableKey"] >> 60 not in types or r["stableKey"] & ((1 << 52) - 1) == 0: err(f"{tag} {r['id']}: bad stableKey {r['stableKey']}")
        for tr in S["TERR"]:
            if (tr["stableKey"] >> 52) & 0xFF: err(f"TERR {tr['id']}: stableKey discriminator != 0")
        if S["CHOK"] and "defenseGroupId" in S["CHOK"][0]:   # final v1 defense groups
            for e in check_defense_groups(S["CHOK"]): err(e)
            DG = defense_groups(model)
            info["defenseGroups"] = [{"id": g["id"], "key": g["key"], "sidePair": [list(p) for p in g["sidePair"]], "members": g["memberChokepointIds"]} for g in DG]
        for c in S["CHOK"]:
            if "bypassWheeledBlocked" in c:
                if c["bypassWheeledBlocked"] not in (0, 1): err(f"CHOK {c['id']}: bypassWheeledBlocked {c['bypassWheeledBlocked']} not 0/1")
                if c["bypassMaxGradePpt"] == NO_BYPASS and c["bypassWheeledBlocked"] != 1: err(f"CHOK {c['id']}: no bypass at all but bypassWheeledBlocked = 0")
        z = [n["id"] for n in S["RNOD"] if n["territoryId"] == 0]   # final v1: every road node lies in a territory
        if z: err(f"RNOD territoryId 0 on {len(z)} nodes (first {z[:10]}); every road node must carry its TERR.id")
        tids = {t["id"] for t in S["TERR"]}
        for n in S["RNOD"]:   # node territory: 0 or an existing territory
            if n["territoryId"] and n["territoryId"] not in tids: err(f"RNOD {n['id']}: territoryId {n['territoryId']} not in TERR")
        gate_t = {}
        for t in S["TOWN"]:
            if t["roadNodeId"] in ids["RNOD"]: gate_t.setdefault(t["roadNodeId"], set()).add(t["territoryId"])
        for nid, ts in gate_t.items():   # a town's gate node lies in the town's territory (unless shared by towns of several)
            if len(ts) == 1 and nodes[nid - 1]["territoryId"] not in ts: err(f"RNOD {nid}: gate node territory {nodes[nid - 1]['territoryId']} != town territory {sorted(ts)}")
        for e in edges:   # a dry edge (both ends one territory) carries that territory
            na, nb = nodes[e["a"] - 1]["territoryId"], nodes[e["b"] - 1]["territoryId"]
            if na == nb and na and e["territoryId"] != na and not e["flags"] & (1 | 2 | 128): err(f"REDG {e['id']}: territoryId {e['territoryId']} != both end nodes' {na}")
        for t in S["TOWN"]:   # population provenance bits are exclusive; bit5 (design override) needs a population
            sf = t["sourceFlags"]
            if bin(sf & (1 | 2 | 16 | 32)).count("1") > 1: err(f"TOWN {t['id']}: more than one population-source bit in sourceFlags {sf}")
            if sf & 32 and t["population"] <= 0: err(f"TOWN {t['id']}: design-override flag with population 0")
    for b in S["BRDG"]:
        ref(b["roadEdgeId"], "REDG", f"BRDG {b['id']}.roadEdgeId"); ref(b["riverId"], "RIVR", f"BRDG {b['id']}.riverId", True)
        if b["roadEdgeId"] in ids["REDG"]:
            e = edges[b["roadEdgeId"] - 1]
            if (e["a"], e["b"]) != (b["nodeAId"], b["nodeBId"]) or not e["flags"] & REDG_FLAGS["bridgeSpan"]: err(f"BRDG {b['id']}: edge mismatch")
    spans = sorted(e["id"] for e in edges if e["flags"] & REDG_FLAGS["bridgeSpan"])
    if spans != sorted(b["roadEdgeId"] for b in S["BRDG"]): err("BRDG set != span edges")
    for r in S["RIVR"]:
        if len(r["polylineCm"]) < 2: err(f"RIVR {r['id']}: polyline < 2 points")
    for w in S["WATR"]:
        if not w["ringIds"]: err(f"WATR {w['id']}: no rings")
        for k, x in enumerate(w["ringIds"]):
            ref(x, "WRNG", f"WATR {w['id']}.ringIds")
            if x in ids["WRNG"]:
                g = S["WRNG"][x - 1]
                if g["waterBodyId"] != w["id"] or g["role"] != (0 if k == 0 else 1): err(f"WATR {w['id']}: ring {x} owner/role")
    for g in S["WRNG"]:
        if len(g["ringCm"]) < 4 or g["ringCm"][0] != g["ringCm"][-1]: err(f"WRNG {g['id']}: ring not closed / < 4 points")
    for r in S["RSRC"]:
        ref(r["nearestRoadNodeId"], "RNOD", f"RSRC {r['id']}"); ref(r["nearestTownId"], "TOWN", f"RSRC {r['id']}.town", True); inb(r["positionCm"], f"RSRC {r['id']}")
    for m in S["MSET"]:
        ref(m["nearestRoadNodeId"], "RNOD", f"MSET {m['id']}"); inb(m["positionCm"], f"MSET {m['id']}")
    NC = G * M["gridHeight"]
    if len(S["CELL"]) != NC or len(S["CELX"]) != NC: err("CELL/CELX count != grid")
    for i, c in enumerate(S["CELX"]):
        ref(c["territoryId"], "TERR", f"CELX {i}.territoryId", True)
        if not 0 <= S["CELL"][i]["class"] <= 3: err(f"CELL {i}: class")
    cells_of = defaultdict(int)
    for c in S["CELX"]: cells_of[c["territoryId"]] += 1
    for tr in S["TERR"]:
        if cells_of[tr["id"]] != tr["areaCells"]: err(f"TERR {tr['id']}: areaCells {tr['areaCells']} != painted cells {cells_of[tr['id']]}")
    for i, c in enumerate(S["CELX"]):   # edgeBlockMask symmetric
        x, y = i % G, i // G
        if x + 1 < G and bool(c["edgeBlockMask"] & 2) != bool(S["CELX"][i + 1]["edgeBlockMask"] & 8): err(f"CELX {i}: E/W mask asymmetric")
        if y + 1 < M["gridHeight"] and bool(c["edgeBlockMask"] & 1) != bool(S["CELX"][i + G]["edgeBlockMask"] & 4): err(f"CELX {i}: N/S mask asymmetric")
    if M["moveWidth"] != G * M["moveCellsPerCell"]: err("moveWidth != gridWidth * moveCellsPerCell")
    grid_tags = sorted(g["sectionTag"] for g in S["GRID"])
    if grid_tags != ["CELL", "CELX"]: err(f"main GRID descriptors {grid_tags} != [CELL, CELX]")
    if any(S[t] for t in TERRAIN_PAYLOAD if t in S): err("main file carries terrain sections")

def _check_file(path, debug_json, movement_dir):
    import os
    buf = open(path, "rb").read()
    rep = {"file": os.path.basename(path), "bytes": len(buf), "sha256": hashlib.sha256(buf).hexdigest(), "checks": {}}
    try:
        model = decode(buf); rep["checks"]["headerDirectoryCrcHash"] = "PASS"
    except BlobError as e:
        rep["checks"]["headerDirectoryCrcHash"] = f"FAIL: {e}"; return rep, None
    h = model["header"]; rep["header"] = h; rep["draftLayout"] = bool(h["flags"] & FLAG_DRAFT_LAYOUT)
    rep["fileKind"] = ["main", "terrain"][model["sections"]["META"][0]["fileKind"]] if model["sections"]["META"][0]["fileKind"] in (0, 1) else "?"
    rt = encode({"sections": model["sections"]}, h["flags"], h["formatVersion"])
    rep["checks"]["roundTripBinaryIdentical"] = "PASS" if rt == buf else "FAIL"
    dv = debug_view(model)
    if debug_json:
        dj = json.load(open(debug_json))
        same = dj["sections"] == dv["sections"]
        diff = [t for t in sorted(set(dj["sections"]) | set(dv["sections"])) if dj["sections"].get(t) != dv["sections"].get(t)]
        rep["checks"]["matchesDebugJson"] = "PASS" if same else f"FAIL: {diff}"
        if same:   # JSON -> binary -> identical bytes (raw arrays are taken from the blob; their sha256 is compared above)
            back = {"sections": {t: (model["sections"][t] if isinstance(model["sections"][t], (bytes, bytearray)) else dj["sections"][t])
                                 for t in dj["sections"]}}
            rep["checks"]["jsonToBinaryIdentical"] = "PASS" if encode(back, h["flags"], h["formatVersion"]) == buf else "FAIL"
    if movement_dir and rep["fileKind"] == "terrain":
        md = movement_dir; ok = {}
        ok["MVSL"] = open(os.path.join(md, "slope_ppt.u16"), "rb").read() == model["sections"]["MVSL"]
        ok["MVLC"] = open(os.path.join(md, "landcover.u8"), "rb").read() == model["sections"]["MVLC"]
        fd = open(os.path.join(md, "..", "osm", "ford_flags.u8"), "rb").read()
        bits = bytearray((len(fd) + 7) // 8)
        for i, v in enumerate(fd):
            if v: bits[i >> 3] |= 1 << (i & 7)
        ok["MVFD"] = bytes(bits) == model["sections"]["MVFD"]
        ok["MVEL"] = open(os.path.join(md, "elevation_dm.u16"), "rb").read() == model["sections"]["MVEL"]
        if "WTRA" in model["sections"]:
            A = decode_wtra(model["sections"]["WTRA"])
            ok["WTRA.codes"] = open(os.path.join(md, "water_type.u8"), "rb").read() == A["codes"]
            wb = json.load(open(os.path.join(md, "water_bake.json")))
            ok["WTRA.fords"] = wb["fordList"] == A["fords"]
        rep["checks"]["gridsMatchSourceArrays"] = "PASS" if all(ok.values()) else f"FAIL: {ok}"
    errs, info = validate(model)
    rep["checks"]["invariants"] = "PASS" if not errs else f"FAIL ({len(errs)})"
    rep["invariantErrors"] = errs; rep["info"] = info
    return rep, model
def main(argv):
    import argparse, os
    ap = argparse.ArgumentParser(); ap.add_argument("blob"); ap.add_argument("--terrain"); ap.add_argument("--debug-json")
    ap.add_argument("--terrain-debug-json"); ap.add_argument("--movement-dir"); ap.add_argument("--report")
    a = ap.parse_args(argv)
    files = {}
    r1, m1 = _check_file(a.blob, a.debug_json, a.movement_dir); files[r1.get("fileKind", "file")] = r1
    rep = {"files": files, "checks": {}}
    if a.terrain:
        r2, m2 = _check_file(a.terrain, a.terrain_debug_json, a.movement_dir); files["terrain"] = r2
        if m1 is not None and m2 is not None:
            errs, info = validate_pair(m1, m2, os.path.basename(a.blob), os.path.basename(a.terrain))
            rep["checks"]["pair"] = "PASS" if not errs else f"FAIL ({len(errs)})"; rep["pairErrors"] = errs; rep["pairInfo"] = info
        else: rep["checks"]["pair"] = "FAIL: undecodable file"
    for k, r in files.items():
        for c, v in r["checks"].items(): rep["checks"][f"{k}.{c}"] = v
    rep["result"] = "PASS" if all(v == "PASS" for v in rep["checks"].values()) else "FAIL"
    s = json.dumps(rep, indent=1, sort_keys=True)
    if a.report: open(a.report, "w").write(s + "\n")
    print(s)
    return 0 if rep["result"] == "PASS" else 1

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
