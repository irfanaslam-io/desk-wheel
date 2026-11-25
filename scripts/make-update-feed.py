#!/usr/bin/env python3
"""Writes update.json for a release and signs it (update.json.sig).

  python3 scripts/make-update-feed.py --version 0.18.0 --base-url https://github.com/OWNER/REPO/releases/download/v0.18.0 \
      --notes "What's new" --key update-signing-key.pem --out dist/feed \
      mac-arm64=dist/DeskWheel-0.18.0.dmg windows-x64=dist/DeskWheel-0.18.0-win-x64-setup.exe ...

Each installer gets its download address and SHA-256. The signature is ECDSA P-256 over SHA-256 (openssl),
which Desk Wheel checks with platform/update-public-key.pem before trusting the feed."""
import argparse, datetime, hashlib, json, os, subprocess, sys

p = argparse.ArgumentParser()
p.add_argument("--version", required=True)
p.add_argument("--base-url", required=True)
p.add_argument("--notes", default="")
p.add_argument("--page", default="")
p.add_argument("--key", required=True, help="private key (PEM)")
p.add_argument("--out", required=True)
p.add_argument("assets", nargs="+", help="key=path, e.g. windows-x64=dist/DeskWheel-setup.exe")
a = p.parse_args()

feed = {"version": a.version.lstrip("v"), "date": datetime.date.today().isoformat(), "notes": a.notes, "page": a.page or None, "assets": {}}
for item in a.assets:
    key, path = item.split("=", 1)
    data = open(path, "rb").read()
    feed["assets"][key] = {"url": f"{a.base_url.rstrip('/')}/{os.path.basename(path)}", "sha256": hashlib.sha256(data).hexdigest(), "size": len(data)}
feed = {k: v for k, v in feed.items() if v is not None}

os.makedirs(a.out, exist_ok=True)
out = os.path.join(a.out, "update.json")
with open(out, "w") as f:
    json.dump(feed, f, indent=2)
subprocess.run(["openssl", "dgst", "-sha256", "-sign", a.key, "-out", out + ".sig", out], check=True)
pub = subprocess.run(["openssl", "ec", "-in", a.key, "-pubout"], check=True, capture_output=True).stdout
here = os.path.dirname(os.path.abspath(__file__))
expected = open(os.path.join(here, "..", "platform", "update-public-key.pem"), "rb").read()
if pub.strip() != expected.strip():
    sys.exit("ERROR: this signing key doesn't match platform/update-public-key.pem; installed apps would reject the update.")
print(json.dumps(feed, indent=2))
