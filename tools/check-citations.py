#!/usr/bin/env python3
r"""Verify that every File.cs:NNN citation in the shipped tool still points at real code.

The tool's central claims are `file:line` references into the decompiled Virtual Desktop trees —
"the search window is a hardcoded 3000 ms with no retry (ComputerDiscoveryClient.cs:98-107)",
"the client kills itself when it cannot get an account identity (NetworkManager.cs:184-186)".
Those citations are the evidence. If one silently stops resolving, the claim becomes folklore
while the report keeps citing it, and nobody notices until someone checks the source by hand.

Scope: src/VdHelper/**/*.cs and src/VdHelper/Resources/*.json — the artefacts that ship and are
shown to users. Research notes are historical records and are deliberately not gated.

Two trees are searched, because the same class name appears in both builds:
  F:\Project\VirtualDesktop\localization\desktop\decompiled_streamer   (PC Streamer)
  %TEMP%\vd_ep_01\vd                                                  (Quest/VD)
A citation resolves if it lands in AT LEAST ONE tree. It is out of range in the other one
constantly — SharedUserSettings.cs is 452 lines in the VD build and over 1500 in the Streamer
build — and treating that as a failure would bury the real ones in noise.

Exit codes: 0 clean or skipped, 1 dangling citation, 2 bad usage.
"""

import argparse
import collections
import pathlib
import re
import sys

DEFAULT_TREES = (
    r"F:\Project\VirtualDesktop\localization\desktop\decompiled_streamer",
    r"C:\Users\dwgx1\AppData\Local\Temp\vd_ep_01\vd",
)

CITATION = re.compile(r"([A-Za-z0-9_.\-]*\.cs):(\d+)(?:\s*[-–]\s*(\d+))?")
SCAN = ("src/VdHelper/**/*.cs", "src/VdHelper/Resources/*.json")


def build_index(root: pathlib.Path) -> dict:
    idx = {}
    for p in root.rglob("*.cs"):
        idx.setdefault(p.name.lower(), p)
    return idx


def line_count(p: pathlib.Path) -> int:
    with p.open(encoding="utf-8", errors="replace") as fh:
        return sum(1 for _ in fh)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--tree", action="append", default=None,
                    help="extra decompiled tree; repeatable")
    ap.add_argument("--root", default=".", help="repo root")
    args = ap.parse_args()

    root = pathlib.Path(args.root).resolve()
    trees = [t for t in (args.tree or DEFAULT_TREES) if pathlib.Path(t).is_dir()]

    targets = []
    for pattern in SCAN:
        targets.extend(root.glob(pattern))
    if not targets:
        print("citations: no source files found — check --root", file=sys.stderr)
        return 2

    cites = collections.Counter()
    origin = {}
    for f in sorted(set(targets)):
        text = f.read_text(encoding="utf-8-sig", errors="replace")
        for m in CITATION.finditer(text):
            key = (m.group(1), int(m.group(2)), int(m.group(3) or m.group(2)))
            cites[key] += 1
            origin.setdefault(key, f.relative_to(root).as_posix())

    print(f"citations: {len(cites)} distinct in {len(set(targets))} files")
    if not trees:
        print("SKIPPED — no decompiled tree present, citations not verified.")
        print("          (expected on CI; run locally after extracting one)")
        return 0

    indexes = [(label, build_index(pathlib.Path(t))) for label, t in zip(("streamer", "vd"), trees)]
    for label, t in zip(("streamer", "vd"), trees):
        print(f"  tree {label}: {len(indexes[0][1]) if label == 'streamer' else len(indexes[1][1])} .cs files")

    dangling = []
    for (name, start, end), n in sorted(cites.items()):
        hit = False
        for _, idx in indexes:
            p = idx.get(name.lower())
            if p is None:
                continue
            if end <= line_count(p):
                hit = True
                break
        if not hit:
            dangling.append((name, start, end, n, origin[(name, start, end)]))

    if dangling:
        print(f"\nFAIL {len(dangling)} citation(s) do not resolve to any decompiled tree:\n")
        for name, start, end, n, where in dangling:
            span = f"{start}" if start == end else f"{start}-{end}"
            print(f"  {name}:{span}  x{n}  cited in {where}")
        print("\nEither the line moved or the tree changed version. Re-derive the claim;")
        print("do not simply delete the citation — the claim is the evidence.")
        return 1

    print(f"OK   all {len(cites)} citations resolve")
    return 0


if __name__ == "__main__":
    sys.exit(main())