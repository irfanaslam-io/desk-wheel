#!/usr/bin/env bash
# Makes Desk Wheel's own Mac signing identity: a free, self-signed code-signing certificate.
#
#   scripts/make-mac-signing-cert.sh [output-folder]
#
# Run it ONCE, keep the result safe, and never commit it. It writes:
#   deskwheel-signing.p12          the certificate and its private key
#   MAC_SIGNING_P12.txt            the same, base64: paste into the GitHub secret MAC_SIGNING_P12
#   MAC_SIGNING_PASSWORD.txt       its password: paste into the GitHub secret MAC_SIGNING_PASSWORD
#
# Why: an ad-hoc signature changes with every build, so macOS treats each update as a new app and asks for
# Screen Recording, Accessibility and Files permissions again. Signed with the same certificate every time,
# macOS recognises Desk Wheel across versions. (Gatekeeper still asks once on the very first install; only
# Apple's paid Developer ID removes that.) Making a new certificate later resets permissions once, so reuse
# this one for as long as it's valid (20 years).
set -euo pipefail
OUT="${1:-.}"
mkdir -p "$OUT"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

cat > "$WORK/cert.cnf" <<'CNF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = Desk Wheel (Muhammad Irfan Aslam)
O = irfanaslam.io
[ext]
basicConstraints = critical, CA:false
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
subjectKeyIdentifier = hash
CNF

openssl req -x509 -newkey rsa:3072 -nodes -days 7300 -sha256 \
  -keyout "$WORK/key.pem" -out "$WORK/cert.pem" -config "$WORK/cert.cnf" 2>/dev/null

PASSWORD=$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-24)
# macOS's "security import" only reads the older PKCS#12 encryption, so ask OpenSSL 3 for it.
LEGACY=()
if openssl version | grep -q "^OpenSSL 3"; then LEGACY=(-legacy); fi
openssl pkcs12 -export "${LEGACY[@]}" -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1 \
  -inkey "$WORK/key.pem" -in "$WORK/cert.pem" -name "Desk Wheel (Muhammad Irfan Aslam)" \
  -passout "pass:$PASSWORD" -out "$OUT/deskwheel-signing.p12"

base64 < "$OUT/deskwheel-signing.p12" | tr -d '\n' > "$OUT/MAC_SIGNING_P12.txt"
printf '%s' "$PASSWORD" > "$OUT/MAC_SIGNING_PASSWORD.txt"
chmod 600 "$OUT/deskwheel-signing.p12" "$OUT/MAC_SIGNING_P12.txt" "$OUT/MAC_SIGNING_PASSWORD.txt"

echo "Certificate:"
openssl x509 -in "$WORK/cert.pem" -noout -subject -enddate -fingerprint -sha1
echo
echo "Next: GitHub > desk-wheel > Settings > Secrets and variables > Actions > New repository secret"
echo "  MAC_SIGNING_P12       = contents of $OUT/MAC_SIGNING_P12.txt"
echo "  MAC_SIGNING_PASSWORD  = contents of $OUT/MAC_SIGNING_PASSWORD.txt"
echo "Keep deskwheel-signing.p12 and the password somewhere safe (a password manager), then delete these files."
