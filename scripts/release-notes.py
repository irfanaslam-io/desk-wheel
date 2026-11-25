#!/usr/bin/env python3
"""Writes a Desk Wheel release's notes in the standard layout:

  What's new · Downloads (one row per system) · Verify your download (SHA-256) · Full changelog

and SHA256SUMS.txt for the files beside it.

  python3 scripts/release-notes.py --version 0.2.0 --repo irfanaslam-io/desk-wheel \
      --whats-new notes.txt --previous-tag v0.1.0 --dir out --out release-notes.md [--store-live]

--whats-new may be the release's current text: if it already uses this layout (a re-run), only its
"What's new" part is kept.
"""
import argparse
import hashlib
import os
import re

MARKER = "<!-- desk-wheel-release-notes -->"
STORE_URL = "https://apps.microsoft.com/detail/9PF534B6ZW4Z"


def whats_new(text: str, version: str) -> str:
    text = text.replace("\r\n", "\n").strip()
    if MARKER in text:
        m = re.search(r"## What's new\n(.*?)\n## Downloads", text, re.S)
        text = m.group(1).strip() if m else ""
    return text or f"Desk Wheel {version}."


def sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True)
    ap.add_argument("--repo", required=True)
    ap.add_argument("--whats-new", required=True, help="file with the release's own text")
    ap.add_argument("--previous-tag", default="")
    ap.add_argument("--dir", required=True, help="folder with the release files")
    ap.add_argument("--out", required=True)
    ap.add_argument("--store-live", action="store_true", help="the Microsoft Store listing is published")
    a = ap.parse_args()

    v, tag = a.version, f"v{a.version}"
    base = f"https://github.com/{a.repo}/releases/download/{tag}"
    files = {
        "win-x64": f"DeskWheel-{v}-win-x64-setup.exe",
        "win-arm64": f"DeskWheel-{v}-win-arm64-setup.exe",
        "mac": f"DeskWheel-{v}-mac-arm64.dmg",
    }
    for name in files.values():
        if not os.path.isfile(os.path.join(a.dir, name)):
            raise SystemExit(f"missing {name} in {a.dir}")

    def link(key: str) -> str:
        return f"[{files[key]}]({base}/{files[key]})"

    with open(a.whats_new, encoding="utf-8") as f:
        new = whats_new(f.read(), v)

    rows = []
    if a.store_live:
        rows.append(f"| **Windows 10 / 11** | [**Microsoft Store**]({STORE_URL}) | **Recommended.** Trusted by Windows and updated automatically |")
    rows += [
        f"| Windows 10 / 11 (Intel or AMD, most PCs) | {link('win-x64')} | Installs for you only, no administrator needed |",
        f"| Windows 11 on Arm (Snapdragon) | {link('win-arm64')} | Same, for Arm PCs |",
        f"| macOS 12 or later (Apple silicon) | {link('mac')} | Open it and drag Desk Wheel to Applications |",
    ]
    windows_note = (
        "> **Windows:** the installer isn't code-signed yet, so Windows may show a SmartScreen warning "
        "(*More info › Run anyway*) or Smart App Control may block it. "
        + ("The Microsoft Store version has no warnings." if a.store_live else "A Microsoft Store version without warnings is coming soon.")
        + "\n> Not sure which file? *Settings › System › About › System type* says x64 or ARM."
    )
    mac_note = (
        "> **Mac:** the first time, macOS says it can't check Desk Wheel. Open *System Settings › Privacy & Security* "
        "and click *Open Anyway*. You only do this once."
    )

    sums = [(name, sha256(os.path.join(a.dir, name))) for name in sorted(files.values())]
    with open(os.path.join(a.dir, "SHA256SUMS.txt"), "w", encoding="utf-8") as f:
        f.writelines(f"{h}  {n}\n" for n, h in sums)

    changelog = (
        f"https://github.com/{a.repo}/compare/{a.previous_tag}...{tag}" if a.previous_tag
        else f"https://github.com/{a.repo}/commits/{tag}"
    )

    notes = "\n".join([
        MARKER,
        "## What's new",
        new,
        "",
        "## Downloads",
        "",
        "| System | Download | |",
        "| --- | --- | --- |",
        *rows,
        "",
        windows_note,
        ">",
        mac_note,
        "",
        "Already using Desk Wheel? It updates itself: *Check for updates…* in its menu, or wait for the prompt.",
        "",
        "## Verify your download",
        "",
        "| File | SHA-256 |",
        "| --- | --- |",
        *[f"| `{n}` | `{h}` |" for n, h in sums],
        "",
        "Windows: `Get-FileHash <file>` in PowerShell. Mac: `shasum -a 256 <file>` in Terminal. "
        "Also in `SHA256SUMS.txt`. (`update.json` and its `.sig` are for the in-app updater.)",
        "",
        "## Full changelog",
        "",
        f"{a.previous_tag + '...' + tag if a.previous_tag else 'All changes up to ' + tag}: {changelog}",
        "",
        "Free for individuals and small businesses ([licence](https://github.com/" + a.repo + "/blob/main/LICENSE.md)). "
        "Support: hello@irfanaslam.io · [irfanaslam.io](https://irfanaslam.io)",
        "",
    ])
    with open(a.out, "w", encoding="utf-8") as f:
        f.write(notes)


if __name__ == "__main__":
    main()
