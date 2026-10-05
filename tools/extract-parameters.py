#!/usr/bin/env python
"""
extract-parameters.py — turn the researched Streamer parameter tables into a machine-readable
catalog the app embeds. The research file holds several tables with different column sets, so
this is header-driven rather than positional:

    python tools/extract-parameters.py \
        research/04-streamer-settings/01-config-keys.md \
        src/VdHelper/Resources/parameters.json

Every table starts with a header row `| # | 键名（JSON 路径） | ... |`. Column names are mapped,
so adding a table upstream does not silently drop rows here.
"""
import json
import re
import sys
from pathlib import Path

HEADER = re.compile(r"^\|\s*#\s*\|")
CELL = re.compile(r"\s*(?P<v>.*?)\s*$")


def clean(cell: str) -> str:
    cell = re.sub(r"\*\*(.+?)\*\*", r"\1", cell.strip())
    return cell.replace("`", "").strip()


def yes(value: str) -> bool:
    return clean(value) == "是"


def parse_table(lines, start):
    header = [clean(c) for c in lines[start].strip().strip("|").split("|")]
    names = []
    for h in header:
        h = re.sub(r"（.*?）|\(.*?\)", "", h).strip()
        names.append(h)
    if "键名" not in names:
        return None, start
    out = []
    i = start + 2  # skip header + separator
    while i < len(lines):
        raw = lines[i].strip()
        if not raw.startswith("|"):
            break
        cells = [clean(c) for c in raw.strip().strip("|").split("|")]
        if not cells or not cells[0].isdigit():
            break
        out.append(dict(zip(names, cells)))
        i += 1
    return (names, out), i


def to_entry(row, table_index):
    key = row.get("键名", "")
    return {
        "key": key,
        "type": row.get("类型", ""),
        "default": row.get("默认", ""),
        "range": row.get("合法取值 / 范围") or row.get("合法取值", ""),
        "effect": row.get("作用", ""),
        "affectsLan": yes(row.get("LAN", "否")),
        "affectsQuality": yes(row.get("画质", "否")),
        "needsRestart": yes(row.get("重启", "否")),
        "needsReconnect": yes(row.get("重连", "否")),
        "readOnly": "只读不改" in row.get("安全", ""),
        "caution": "谨慎" in row.get("安全", ""),
        "source": row.get("来源 file:line", row.get("来源", "")),
        "table": table_index,
    }


def main() -> int:
    src, dst = Path(sys.argv[1]), Path(sys.argv[2])
    lines = src.read_text(encoding="utf-8").splitlines()

    entries, i, table_index = [], 0, 0
    while i < len(lines):
        if HEADER.match(lines[i].strip()):
            parsed, i = parse_table(lines, i)
            if parsed:
                _, rows = parsed
                entries.extend(to_entry(r, table_index) for r in rows if r.get("键名"))
                table_index += 1
        i += 1

    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(json.dumps(entries, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{len(entries)} parameters from {table_index} tables -> {dst}")
    print(f"  readOnly={sum(e['readOnly'] for e in entries)} "
          f"caution={sum(e['caution'] for e in entries)} "
          f"affectsLan={sum(e['affectsLan'] for e in entries)} "
          f"tables={sorted({e['table'] for e in entries})}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())