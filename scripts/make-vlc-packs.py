#!/usr/bin/env python3
"""
Builds the media player packs: LibVLC for each platform Desk Wheel ships on, downloaded by the app the first
time someone opens the player (so the app itself stays small).

    python3 scripts/make-vlc-packs.py --out dist/packs [--cache .cache/nuget]

Source: VideoLAN's own LibVLC NuGet packages (LGPL-2.1-or-later), pinned by version and SHA-512. From them:
  * only what playback needs: libvlc, libvlccore, plugins, the HRTF data for spatial audio;
  * every plugin that declares a GPL licence is left out (read from the licence string each plugin carries),
    plus the few listed below, so everything in a pack is LGPL;
  * the LGPL text and a README saying where the source is.
Writes one zip per platform and packs.json (name, size, SHA-256) next to them.
"""
import argparse
import base64
import hashlib
import io
import json
import os
import re
import sys
import urllib.request
import zipfile

VLC_VERSION = "3.0.24"
PACK_VERSION = f"{VLC_VERSION}-1"
SOURCES = {
    "windows": ("VideoLAN.LibVLC.Windows", "3.0.24",
                "Gt4Kk5nSNVVZ7z7dFnHDfwpLQMQIqWTJ6fshFnP/0A3erUdBkj7L5PLmWrVxkEVSh0WFk5CXiwEu8Me/c3Db7g=="),
    "mac": ("VideoLAN.LibVLC.Mac", "3.1.3.2",
            "1kzMhAiC32EsuqvshwEfZsLlm7+77FKfTJEZirBSVr2yDCQuzsMCDkQnTZVpM7RH79VJRtbuhCJ+LAG0e6tlZA=="),
}
# pack rid -> (source, folder inside the package, how files map into the pack)
PACKS = {
    "win-x64": ("windows", "build/x64/"),
    "win-arm64": ("windows", "build/arm64/"),
    "mac-arm64": ("mac", "build/macos/osx-arm64/"),
}
# Left out even though their own code is LGPL: Teletext (zvbi library licence not confirmed LGPL).
ALSO_LEFT_OUT = {"zvbi"}
GPL = re.compile(rb"Licensed under the terms of the GNU General Public License")
LGPL = re.compile(rb"Licensed under the terms of the GNU Lesser General Public License")
FIXED_TIME = (2026, 1, 1, 0, 0, 0)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def fetch(name, version, sha512, cache):
    os.makedirs(cache, exist_ok=True)
    path = os.path.join(cache, f"{name.lower()}.{version}.nupkg")
    if not os.path.exists(path):
        url = f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/{version}/{name.lower()}.{version}.nupkg"
        print(f"Downloading {url}")
        with urllib.request.urlopen(url) as r, open(path + ".part", "wb") as f:
            while chunk := r.read(1 << 20):
                f.write(chunk)
        os.replace(path + ".part", path)
    digest = base64.b64encode(hashlib.sha512(open(path, "rb").read()).digest()).decode()
    if digest != sha512:
        sys.exit(f"{path}: SHA-512 doesn't match the pinned value (got {digest})")
    return zipfile.ZipFile(path)


def plugin_name(path):
    base = os.path.basename(path)
    return re.sub(r"^lib|_plugin\.(dll|dylib)$", "", base)


def pack_files(package, prefix, mac):
    """(name in pack, bytes) for everything that goes in, and the plugins left out."""
    files, left_out = [], []
    for info in package.infolist():
        name = info.filename
        if not name.startswith(prefix) or name.endswith("/"):
            continue
        rel = name[len(prefix):]
        if mac:
            # lib/libvlc.dylib -> libvlc.dylib, lib/vlc/plugins/x -> plugins/x, lib/vlc/share/hrtfs -> hrtfs
            if rel in ("lib/libvlc.dylib", "lib/libvlccore.dylib"):
                target = rel[4:]
            elif rel.startswith("lib/vlc/plugins/"):
                target = "plugins/" + rel[len("lib/vlc/plugins/"):]
            elif rel.startswith("lib/vlc/share/hrtfs/"):
                target = "hrtfs/" + rel[len("lib/vlc/share/hrtfs/"):]
            else:
                continue  # Lua scripts and the web interface: not used by a player
        else:
            if rel in ("libvlc.dll", "libvlccore.dll") or rel.startswith(("plugins/", "hrtfs/")):
                target = rel
            else:
                continue  # import libraries, headers, Lua
        data = package.read(info)
        if target.startswith("plugins/") and target.endswith((".dll", ".dylib")):
            short = plugin_name(target)
            if GPL.search(data) or short in ALSO_LEFT_OUT:
                left_out.append(short)
                continue
            if not LGPL.search(data):
                sys.exit(f"{target}: no licence string found; check it by hand")
        files.append((target, data))
    return sorted(files), sorted(left_out)


def readme(rid, source, left_out):
    name, version, _ = SOURCES[source]
    return f"""Desk Wheel media player engine ({rid})

LibVLC {VLC_VERSION} by VideoLAN, used by Desk Wheel's media player. Downloaded by Desk Wheel only when you
choose to; remove it any time in Settings.

Licence: GNU Lesser General Public License, version 2.1 or later (LICENSE-LGPL-2.1.txt).
Source code: https://code.videolan.org/videolan/vlc/-/tree/{VLC_VERSION}
Built from: the NuGet package {name} {version} (https://www.nuget.org/packages/{name}/{version}), unchanged,
except that only playback files are included and these plugins (GPL, or not confirmed LGPL) are left out:
{", ".join(left_out) or "none"}.

You may replace these libraries with your own build of LibVLC {VLC_VERSION}: put libvlc, libvlccore and the
plugins folder in this folder's place (Desk Wheel loads them from here).
""".encode()


def write_zip(path, entries):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in entries:
            info = zipfile.ZipInfo(name, FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o755 if name.endswith((".dylib", ".dll")) else 0o644) << 16
            z.writestr(info, data, compresslevel=9)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--cache", default=os.path.join(ROOT, ".cache", "nuget"))
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    lgpl = open(os.path.join(ROOT, "LICENSES", "LGPL-2.1-or-later.txt"), "rb").read()
    packages = {key: fetch(*SOURCES[key], args.cache) for key in {s for s, _ in PACKS.values()}}

    manifest = {"id": "media-player", "version": PACK_VERSION, "vlc": VLC_VERSION, "files": {}}
    for rid, (source, prefix) in PACKS.items():
        files, left_out = pack_files(packages[source], prefix, mac=source == "mac")
        must = ("libvlc.dylib", "libvlccore.dylib") if source == "mac" else ("libvlc.dll", "libvlccore.dll")
        names = {n for n, _ in files}
        if not all(m in names for m in must) or sum(n.startswith("plugins/") for n in names) < 200:
            sys.exit(f"{rid}: the package doesn't look complete")
        files += [("LICENSE-LGPL-2.1.txt", lgpl), ("README.txt", readme(rid, source, left_out))]
        name = f"DeskWheel-media-player-{PACK_VERSION}-{rid}.zip"
        path = os.path.join(args.out, name)
        write_zip(path, sorted(files))
        data = open(path, "rb").read()
        manifest["files"][rid] = {"name": name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                                  "unpacked": sum(len(d) for _, d in files), "left_out": left_out}
        print(f"{name}: {len(data) / 1e6:.1f} MB ({manifest['files'][rid]['unpacked'] / 1e6:.0f} MB unpacked), "
              f"{len(files)} files, left out: {', '.join(left_out)}")
    with open(os.path.join(args.out, "packs.json"), "w") as f:
        json.dump(manifest, f, indent=2)


if __name__ == "__main__":
    main()
