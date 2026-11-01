#!/usr/bin/env python3
"""Flag Harmony patch targets that name a method with more than one overload.

A `[HarmonyPatch(typeof(X), "Y")]` with no parameter list throws AmbiguousMatchException at
runtime when X has several Y overloads. Harmony refuses the class, and because PatchAll is
all-or-nothing, every patch class after it is silently left unapplied — the mod loads, some
features work, the rest do nothing.

Usage: python3 tooling/audit-targets.py [decompiled-assembly.cs]
Default path comes from VALHEIM_DECOMPILED, then the known scratch location.
Exit code 1 when any target is ambiguous.
"""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DECOMPILED = (
    sys.argv[1] if len(sys.argv) > 1
    else os.environ.get("VALHEIM_DECOMPILED")
    or "/home/hermes/.hermes/cache/scratch/vg/src/assembly_valheim.decompiled.cs"
)


def targets():
    found = set()
    for path in glob.glob(os.path.join(ROOT, "src", "*.cs")):
        text = open(path, encoding="utf-8").read()
        for type_name, literal, named, pinned in re.findall(
                r'\[HarmonyPatch\(typeof\((\w+)\)\s*,\s*(?:"(\w+)"|nameof\(\w+\.(\w+)\))\s*(,?)', text):
            # A trailing third argument is an explicit parameter list, so the overload is pinned.
            if pinned:
                continue
            found.add((type_name, literal or named))
    return sorted(found)


def class_block(source, name):
    match = re.search(r'(?:public|internal|private)\s+(?:sealed\s+|abstract\s+|partial\s+)*'
                      r'(?:class|struct)\s+' + name + r'\b', source)
    if not match:
        return None
    start = source.index("{", match.end())
    depth = 0
    index = start
    while index < len(source):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                break
        index += 1
    return source[start:index]


def main():
    if not os.path.exists(DECOMPILED):
        print(f"decompiled assembly not found: {DECOMPILED}", file=sys.stderr)
        print("pass the path or set VALHEIM_DECOMPILED", file=sys.stderr)
        return 2

    source = open(DECOMPILED, encoding="utf-8", errors="ignore").read()
    ambiguous = 0
    checked = 0
    for type_name, method in targets():
        block = class_block(source, type_name)
        if block is None:
            print(f"MISSING TYPE {type_name}.{method} - verify the name by hand")
            ambiguous += 1
            continue
        checked += 1
        signatures = set(re.findall(
            r'\b(?:public|private|internal|protected)\s+'
            r'(?:static\s+|virtual\s+|override\s+|new\s+)*[\w\.<>\[\]]+\s+'
            + method + r'\s*\(([^)]*)\)', block))
        if len(signatures) > 1:
            ambiguous += 1
            print(f"AMBIGUOUS {type_name}.{method}: {len(signatures)} overloads "
                  f"{sorted(signatures)} - pin the parameter list")

    print(f"{checked} targets checked, {ambiguous} ambiguous")
    return 1 if ambiguous else 0


if __name__ == "__main__":
    sys.exit(main())
