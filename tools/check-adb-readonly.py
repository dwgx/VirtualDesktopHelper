#!/usr/bin/env python3
"""The adb probes must only ever run read-only commands.

Why this exists: reference/quest-adb-dashboard/docs/ADB_QUEST_NOTES.md:67-81 lists the commands that
project treats as write or state-changing — settings put/delete, input keyevent, am broadcast, adb
tcpip/usb/reboot, adb install/uninstall/push, pm clear/disable/enable. This project inherits those
probes' role without inheriting that list, and nothing checked.

This is a whitelist, not a blocklist. A blocklist asks "does this file contain pm clear?", which
`headset-grant`'s Apply delegate answers yes to legitimately — it grants runtime permissions, which is
a write, and is supposed to be one because it only runs when someone asks for that fix. A blocklist
would either flag it or need an exception, and both versions get disabled the first time the exception
is extended.

A whitelist asks the question that actually matters: exactly which shell commands does a probe run?
Anything new shows up as an addition to the list, which is a decision someone has to make on purpose.

Extracts every literal that follows "shell" in src/VdHelper/Core/Adb/, normalises it, and compares to
the set approved here. Probes only — the fix actions live in Core/Health/Fixes.cs and are not scanned,
because a fix is allowed to write; that is what a fix is for.

Usage:  python tools/check-adb-readonly.py
Exit:   0 unchanged, 1 when the set differs.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ADB_DIR = ROOT / "src" / "VdHelper" / "Core" / "Adb"

# Every adb shell command these probes are allowed to run, as written in the source, with the
# placeholders kept. Adding one here means deciding it is safe to run without asking anyone.
APPROVED = {
    "cat /sys/class/net/wlan0/address",
    "dumpsys package",
    "getprop ro.build.version.release",
    "getprop ro.product.model",
    "ip -4 addr show wlan0",
    "ip addr show wlan0",
    "pidof",
    "pm list packages",
    "ps -A",
    "settings get global http_proxy",
    # headset-grant's FixAction is constructed here, not in Core/Health/Fixes.cs, so it lands in the
    # scan. Both are writes and both are correct — they run only inside Apply, which is only reached
    # when someone asks for that fix. Listed rather than special-cased because the honest description
    # is "these commands appear in these files", and a special case would be a rule that hides itself.
    "pm grant",
}

# The literal tokens that make up one "shell", "..." invocation. Source writes them as separate array
# entries, so they are joined; variable placeholders are kept as the source names them.
SHELL = re.compile(r'"shell"\s*,\s*((?:"[a-zA-Z0-9_./\-]*"\s*,?\s*)+)')
TOKEN = re.compile(r'"([a-zA-Z0-9_./\-]*)"')


def _force_utf8() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def used_commands() -> set[str]:
    found: set[str] = set()
    for f in sorted(ADB_DIR.glob("*.cs")):
        text = f.read_text(encoding="utf-8-sig", errors="replace")
        for m in SHELL.finditer(text):
            parts = [t for t in TOKEN.findall(m.group(1)) if t]
            if parts:
                found.add(" ".join(parts))
    return found


def main() -> int:
    _force_utf8()
    if not ADB_DIR.is_dir():
        print(f"找不到 {ADB_DIR}", file=sys.stderr)
        return 1

    used = used_commands()
    added = sorted(used - APPROVED)
    gone = sorted(APPROVED - used)

    if not added and not gone:
        print(f"adb read-only gate: OK  {len(used)} 条 shell 命令全部在已批准清单里")
        return 0

    print("adb read-only gate: FAIL", file=sys.stderr)
    if added:
        print("  探针里出现了未批准的 shell 命令——**先确认它是只读的**，再加进 tools/check-adb-readonly.py 的 "
              "APPROVED：", file=sys.stderr)
        for c in added:
            print(f"      {c}", file=sys.stderr)
    if gone:
        print("  清单里有、代码里没有了（可以顺手删掉，避免清单自己漂移）：", file=sys.stderr)
        for c in gone:
            print(f"      {c}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())