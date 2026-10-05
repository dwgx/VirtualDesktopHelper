#!/usr/bin/env python3
"""Validate .github/ISSUE_TEMPLATE/*.yml against GitHub's documented issue-form rules.

Why this exists: the template was sitting in the legacy shape — front matter plus loose markdown,
no `body:` key. GitHub treats a .yml there as an issue FORM, and a form without `body` fails
schema validation, so the whole template was dead on arrival: nobody ever saw the instructions that
told them to attach a report instead of hand-typing symptoms.

That failure was invisible from the repo. The file parsed as YAML, it was in the right directory,
and nothing said anything. So the rules GitHub documents are checked here instead.

What this does NOT do: prove GitHub accepts the result. The issue chooser redirects to a login even
for public repositories, so there is no unauthenticated way to see the rendered form. Schema rules
plus a parse are as far as an unauthenticated check can go; the rest is marked unverified in the
commit that introduced the form.

Rules enforced (from GitHub's issue-form syntax docs):
  - top level: name, description, body are required; title/labels/assignees optional
  - body is a non-empty list
  - each entry: type in {markdown, input, textarea, dropdown, checkboxes}
  - markdown takes `value`; interactive types take `attributes.label` and a valid `id`
  - ids match [a-zA-Z0-9_-]+ and are unique across the form
  - a malformed entry is reported by its index so it can be found without reading the whole file

Exit codes: 0 clean, 1 problems found, 2 bad usage.
"""

import argparse
import pathlib
import re
import sys

try:
    import yaml
except ImportError:  # PyYAML is not in the toolchain's guaranteed set.
    print("PyYAML is required for this check: pip install pyyaml", file=sys.stderr)
    raise SystemExit(2)

VALID_TYPES = {"markdown", "input", "textarea", "dropdown", "checkboxes"}
INTERACTIVE = VALID_TYPES - {"markdown"}
ID_RE = re.compile(r"^[a-zA-Z0-9_-]+$")
REQUIRED_TOP = ("name", "description", "body")


def check(path: pathlib.Path) -> list:
    problems = []
    try:
        doc = yaml.safe_load(path.read_text(encoding="utf-8-sig"))
    except Exception as exc:                                  # noqa: BLE001 - report, don't raise
        return [f"{path.name}: not valid YAML — {exc}"]

    if not isinstance(doc, dict):
        return [f"{path.name}: top level must be a mapping, got {type(doc).__name__}"]

    for key in REQUIRED_TOP:
        if key not in doc or doc[key] in (None, ""):
            problems.append(f"{path.name}: missing required key '{key}'")

    body = doc.get("body")
    if body is not None and not isinstance(body, list):
        problems.append(f"{path.name}: 'body' must be a list, got {type(body).__name__}")
        return problems
    if isinstance(body, list) and not body:
        problems.append(f"{path.name}: 'body' is empty — a form with no fields shows an empty page")
        return problems

    seen_ids = set()
    for i, block in enumerate(body or []):
        where = f"{path.name}: body[{i}]"
        if not isinstance(block, dict):
            problems.append(f"{where}: must be a mapping, got {type(block).__name__}")
            continue
        btype = block.get("type")
        if btype not in VALID_TYPES:
            problems.append(f"{where}: type '{btype}' is not one of {sorted(VALID_TYPES)}")
            continue
        if btype == "markdown":
            if not block.get("value"):
                problems.append(f"{where}: markdown block needs 'value'")
        else:
            attrs = block.get("attributes")
            if not isinstance(attrs, dict):
                problems.append(f"{where}: {btype} needs an 'attributes' mapping")
            elif not attrs.get("label"):
                problems.append(f"{where}: {btype} needs 'label' inside 'attributes'")
            elif "label" in block:
                problems.append(f"{where}: 'label' must be inside 'attributes', not at the top level")
            bid = block.get("id")
            if not bid:
                problems.append(f"{where}: {btype} needs 'id'")
            elif not ID_RE.match(str(bid)):
                problems.append(f"{where}: id '{bid}' must match [a-zA-Z0-9_-]+")
            elif bid in seen_ids:
                problems.append(f"{where}: id '{bid}' is already used earlier in the form")
            else:
                seen_ids.add(bid)
            if btype == "dropdown" and not (isinstance(attrs, dict) and attrs.get("options")):
                problems.append(f"{where}: dropdown needs 'options' inside 'attributes'")

    # Anything outside the documented key set is silently dropped by GitHub, which reads as
    # "the setting is there" when it is not doing anything.
    known = {"name", "description", "title", "labels", "assignees", "body"}
    for extra in sorted(set(doc) - known):
        problems.append(f"{path.name}: unrecognised top-level key '{extra}' (GitHub will ignore it)")

    return problems


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=".github/ISSUE_TEMPLATE")
    args = ap.parse_args()

    d = pathlib.Path(args.dir)
    files = sorted(list(d.glob("*.yml")) + list(d.glob("*.yaml")))
    forms = [f for f in files if f.name != "config.yml"]
    if not forms:
        print(f"no issue forms in {d} — nothing to check")
        return 0

    problems = []
    for f in forms:
        problems += check(f)

    if problems:
        print(f"FAIL {len(problems)} problem(s):\n")
        for p in problems:
            print("  " + p)
        return 1

    total = 0
    for f in forms:
        doc = yaml.safe_load(f.read_text(encoding="utf-8-sig"))
        n = len(doc.get("body") or [])
        total += n
        print(f"ok   {f.name}: body has {n} block(s)")
    print(f"OK   {len(forms)} issue form(s) conform to GitHub's documented schema")
    return 0


if __name__ == "__main__":
    sys.exit(main())