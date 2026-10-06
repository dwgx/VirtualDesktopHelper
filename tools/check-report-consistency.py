#!/usr/bin/env python3
"""Catch a report that contradicts itself.

Why this exists: on 2026-10-06 the report's headline said
此刻没有已建立的 VD 通道（不是「串流中」）while `udp-discovery` in the same document said
串流中：到头显的通道已建立, and Windows itself showed four Established channels. Found by rendering the
HTML and reading it — two audits had already passed over the same code, because both read the checks
and neither read the document those checks are rendered into.

The cause was structural, not a typo: HealthChecks wrote `_livePorts` from the fresh subset only, and
ReportWriter reads that key for the headline. Presence and freshness are different questions, and
nothing connected them.

This is the narrowest guard that catches that class: run the tool, read what it wrote, and fail if the
document asserts two opposite things about the same fact. It does not judge whether the report is
correct — only whether it agrees with itself. Anything wider is an opinion about wording, and an
opinionated gate is worse than none.

Read-only. Runs `--report` (which runs the health engine) and inspects the text it produced.

Usage:  python tools/check-report-consistency.py
Exit:   0 consistent, 1 with contradictions.
"""

from __future__ import annotations

import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXE = ROOT / "src" / "VdHelper" / "bin" / "Release" / "net10.0-windows" / "VdHelper.exe"

# The headline's negative claim, and the claims that contradict it.
NO_CHANNELS = r"串流会话：此刻没有已建立的 VD 通道"
SOME_UP = r"串流中：到头显的通道已建立|[1-9]\d* 个通道已建立"

# The one line that names the state. Exactly one of these may appear there.
STATES = ("可串流", "有隐患", "阻断")


def contradictions(text: str) -> list[str]:
    """Every way the document can assert two opposite things about the session."""
    found: list[str] = []

    none_here = re.search(NO_CHANNELS, text)
    up_elsewhere = re.search(SOME_UP, text)
    if none_here and up_elsewhere:
        found.append(
            "标题说没有已建立通道，同一份报告里却有通道已建立：\n"
            f"      标题    {none_here.group(0)}\n"
            f"      别处    {up_elsewhere.group(0)}"
        )

    # Grab only the metadata line, not every place the words appear in prose.
    line = next((l for l in text.splitlines() if l.strip().startswith("- 串流会话：")), "")
    named = [s for s in STATES if s in line]
    if len(named) > 1:
        found.append(f"串流会话一行同时声明了互斥状态：{named}")

    return found


def _force_utf8() -> None:
    """Make stdout/stderr UTF-8 before anything prints Chinese.

    GitHub's windows-latest console is cp1252, and this gate passed locally while failing in CI with
    UnicodeEncodeError on its success line — the check itself had already passed by then. The other
    Python gates get away with English output; this one would rather say what it means, so it makes
    sure the terminal can hold it.
    """
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def main() -> int:
    _force_utf8()
    if not EXE.exists():
        print(f"找不到 {EXE} —— 先 dotnet build -c Release。", file=sys.stderr)
        return 1

    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "report.md"
        # 0/3/4 are the documented codes; 5 would mean it failed to run, and no file means it never ran.
        proc = subprocess.run([str(EXE), "--report", str(out)],
                              capture_output=True, text=True, encoding="utf-8", errors="replace")
        if not out.exists():
            print(f"体检没有产出报告（exit={proc.returncode}）。", file=sys.stderr)
            return 1
        text = out.read_text(encoding="utf-8", errors="replace")

    problems = contradictions(text)
    if problems:
        print(f"report consistency gate: FAIL  {len(problems)} contradiction(s)\n", file=sys.stderr)
        for p in problems:
            print("  " + p, file=sys.stderr)
        return 1

    print("report consistency gate: OK  报告内部没有互相矛盾的断言")
    return 0


if __name__ == "__main__":
    sys.exit(main())