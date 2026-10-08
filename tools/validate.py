#!/usr/bin/env python3
"""Balance data validator. Every building role is required and closed.

    python3 tools/validate.py [data/balance/buildings.json]

Unknown or missing role fails. The error names the building id and the bad value.
"""
import json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
BUILDINGS = os.path.join(REPO, "data", "balance", "buildings.json")
ROLES = ("hq", "production", "supply_depot", "wall", "fortification", "resource", "recruitment")

def check_building_roles(path=BUILDINGS):
    """Return errors. Each error is '<building id>: <bad role value>'."""
    data = json.load(open(path, encoding="utf-8"))
    buildings = data.get("buildings")
    errs = []
    if not isinstance(buildings, dict):
        errs.append(f"buildings: {buildings!r}")
        return errs
    for bid, row in buildings.items():
        if not isinstance(row, dict) or "role" not in row:
            errs.append(f"{bid}: {None!r}")
            continue
        role = row["role"]
        if role not in ROLES:
            errs.append(f"{bid}: {role!r}")
    return errs

if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else BUILDINGS
    errs = check_building_roles(path)
    print(json.dumps({"result": "PASS" if not errs else "FAIL", "errors": errs}, indent=1))
    sys.exit(1 if errs else 0)
