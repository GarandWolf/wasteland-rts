#!/usr/bin/env python3
"""Compare a headless MVP run log against the designer timing targets.

    python3 tools/check_mvp_timings.py RUN_LOG [--targets data/design/mvp_targets.json]
                                       [--line baseline|remnant_commit] [--core-only]

Python 3 standard library only. Prints a pass/drift table and exits:
    0  every checked target is present and within its tolerance
    1  at least one target drifted by more than its tolerance, or is missing from the log
    2  bad input (unreadable or malformed targets file or run log, unknown --line)

RUN LOG FORMAT (JSON lines, for the headless runner to emit)
    One JSON object per line. Blank lines are ignored. Required fields:
        {"tick": <int>, "event": "<target id>"}
    - "tick" is the sim tick (20 ticks per second) at which the event happened. It must be an
      integer >= 0 (not a float, not a bool).
    - "event" is a target id from data/design/mvp_targets.json, for example
      "compact_reaches_the_dalles" or "remnant_captures_hood_river".
    - Any other fields (seat, faction, nodeId, ...) are allowed and ignored.
    - Events that are not target ids are allowed and ignored (they are counted in the summary).
    - If an event id appears more than once, the FIRST occurrence (lowest line number) is used.
    Example:
        {"tick": 4560, "event": "compact_reaches_the_dalles", "seat": 1}

TARGETS
    Each target has an id, a tick, a toleranceTicks and the list of run "lines" it applies to.
    --line picks which run the log came from (default "baseline"); only targets whose "lines"
    contain it are checked. --core-only checks only the targets marked "core": true (the events
    named in the rules sheet's tuning plan, section 7 step 2).
    drift = logged tick - target tick. A target passes when |drift| <= toleranceTicks.
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
DEFAULT_TARGETS = os.path.join(REPO, "data", "design", "mvp_targets.json")


class InputError(Exception):
    pass


def is_int(v):
    return isinstance(v, int) and not isinstance(v, bool)


def clock(ticks, tps):
    sign = "-" if ticks < 0 else ""
    s = abs(ticks) // tps
    rem = abs(ticks) % tps
    out = "%s%d:%02d" % (sign, s // 60, s % 60)
    if rem:
        out += "+%dt" % rem
    return out


def load_targets(path):
    try:
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
    except (OSError, ValueError) as e:
        raise InputError("targets %s: %s" % (path, e))
    if not isinstance(doc, dict) or not isinstance(doc.get("targets"), list):
        raise InputError("targets %s: expected an object with a 'targets' array" % path)
    tps = doc.get("ticksPerSecond", 20)
    if not is_int(tps) or tps < 1:
        raise InputError("targets %s: ticksPerSecond must be a positive integer" % path)
    seen = set()
    for i, t in enumerate(doc["targets"]):
        where = "targets %s: targets[%d]" % (path, i)
        if not isinstance(t, dict):
            raise InputError(where + ": expected an object")
        for key in ("id", "tick", "toleranceTicks", "lines"):
            if key not in t:
                raise InputError(where + ": missing '%s'" % key)
        if not isinstance(t["id"], str) or not t["id"]:
            raise InputError(where + ": 'id' must be a non-empty string")
        if t["id"] in seen:
            raise InputError(where + ": duplicate id %r" % t["id"])
        seen.add(t["id"])
        for key in ("tick", "toleranceTicks"):
            if not is_int(t[key]) or t[key] < 0:
                raise InputError(where + ": '%s' must be an integer >= 0" % key)
        if not isinstance(t["lines"], list) or not all(isinstance(x, str) for x in t["lines"]):
            raise InputError(where + ": 'lines' must be an array of strings")
    return doc, tps


def load_log(path):
    first = {}
    try:
        with open(path, encoding="utf-8") as f:
            lines = f.read().splitlines()
    except OSError as e:
        raise InputError("log %s: %s" % (path, e))
    for n, raw in enumerate(lines, 1):
        if not raw.strip():
            continue
        try:
            obj = json.loads(raw)
        except ValueError as e:
            raise InputError("log %s line %d: not JSON (%s)" % (path, n, e))
        if not isinstance(obj, dict):
            raise InputError("log %s line %d: expected a JSON object" % (path, n))
        tick, event = obj.get("tick"), obj.get("event")
        if not is_int(tick) or tick < 0:
            raise InputError("log %s line %d: 'tick' must be an integer >= 0" % (path, n))
        if not isinstance(event, str) or not event:
            raise InputError("log %s line %d: 'event' must be a non-empty string" % (path, n))
        if event not in first:
            first[event] = (tick, n)
    return first


def main(argv=None):
    ap = argparse.ArgumentParser(description="Check a headless MVP run log against data/design/mvp_targets.json.")
    ap.add_argument("log", help="run log, JSON lines: {\"tick\": int, \"event\": \"<target id>\"}")
    ap.add_argument("--targets", default=DEFAULT_TARGETS, help="targets file (default: data/design/mvp_targets.json)")
    ap.add_argument("--line", default="baseline", help="which run line the log is from (default: baseline)")
    ap.add_argument("--core-only", action="store_true", help="check only targets marked core")
    args = ap.parse_args(argv)
    try:
        doc, tps = load_targets(args.targets)
        known_lines = set(doc.get("lines", {}).keys()) or {x for t in doc["targets"] for x in t["lines"]}
        if args.line not in known_lines:
            raise InputError("unknown --line %r (known: %s)" % (args.line, ", ".join(sorted(known_lines))))
        events = load_log(args.log)
    except InputError as e:
        print("ERROR: %s" % e, file=sys.stderr)
        return 2

    checked = [t for t in doc["targets"]
               if args.line in t["lines"] and (t.get("core", False) or not args.core_only)]
    ids = {t["id"] for t in doc["targets"]}
    ignored = sorted(e for e in events if e not in ids)

    header = ("RESULT", "TARGET ID", "TARGET", "LOGGED", "DRIFT", "TOL")
    rows = []
    failures = 0
    for t in checked:
        tol = t["toleranceTicks"]
        target = "%d (%s)" % (t["tick"], clock(t["tick"], tps))
        if t["id"] not in events:
            rows.append(("MISSING", t["id"], target, "-", "-", str(tol)))
            failures += 1
            continue
        tick = events[t["id"]][0]
        drift = tick - t["tick"]
        ok = abs(drift) <= tol
        if not ok:
            failures += 1
        rows.append(("PASS" if ok else "DRIFT", t["id"], target, "%d (%s)" % (tick, clock(tick, tps)),
                     "%+d (%s)" % (drift, clock(drift, tps)), str(tol)))

    widths = [max(len(r[i]) for r in [header] + rows) for i in range(len(header))]
    fmt = "  ".join("%%-%ds" % w for w in widths)
    print("MVP timing check: line=%s%s, %d targets, log %s" % (
        args.line, " (core only)" if args.core_only else "", len(checked), args.log))
    print(fmt % header)
    print(fmt % tuple("-" * w for w in widths))
    for r in rows:
        print(fmt % r)
    print("%d passed, %d failed (drift or missing); %d log event ids not in the targets file were ignored%s" % (
        len(rows) - failures, failures, len(ignored), (": " + ", ".join(ignored)) if ignored else ""))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
