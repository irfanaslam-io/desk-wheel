#!/usr/bin/env bash
# Builds the ffmpeg that ships inside DeskWheel.app: one self-contained program, LGPL-licensed (no GPL or
# "nonfree" parts), which keeps Desk Wheel free to license as it chooses.
#
#   scripts/build-ffmpeg.sh            → engines/ffmpeg/<rid>/ffmpeg   (skipped if it's already there)
#   scripts/build-ffmpeg.sh --force    → build again
#
# Encoders: H.264 / HEVC and AAC from macOS itself (VideoToolbox, AudioToolbox; patent-licensed by Apple),
# VP9 (libvpx), AV1 for AVIF (libaom), Opus, Vorbis, MP3 (LAME), WebP, plus ffmpeg's own (GIF, PNG, FLAC, WAV…).
# Decoding covers practically every format.
#
# Needs, on the computer that BUILDS DeskWheel only (people who install DeskWheel need nothing):
#   Xcode command-line tools, and:  brew install cmake pkg-config nasm
# Takes 10–20 minutes the first time. Sources are downloaded from each project's official site.
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$PWD"

FFMPEG_VERSION=7.1.1
LAME_VERSION=3.100
OGG_VERSION=1.3.5
VORBIS_VERSION=1.3.7
OPUS_VERSION=1.5.2
VPX_VERSION=1.15.0
WEBP_VERSION=1.5.0
AOM_VERSION=3.11.0

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64)  RID=osx-arm64 ;;
  Darwin-x86_64) RID=osx-x64 ;;
  Linux-x86_64)  RID=linux-x64 ;;   # for checking this script on Linux; not shipped
  *) echo "Unsupported system: $(uname -s) $(uname -m)"; exit 1 ;;
esac
OUT="$ROOT/engines/ffmpeg/$RID"
FORCE=0; [[ "${1:-}" == "--force" ]] && FORCE=1
if [[ -x "$OUT/ffmpeg" && "$FORCE" != 1 ]]; then
  echo "ffmpeg for $RID is already built ($OUT/ffmpeg). Use --force to rebuild."
  exit 0
fi

for tool in cmake pkg-config make cc; do
  command -v "$tool" >/dev/null || { echo "Missing '$tool' (needed only to build ffmpeg). Run: brew install cmake pkg-config nasm"; exit 1; }
done
if [[ "$(uname -m)" == "x86_64" ]] && ! command -v nasm >/dev/null; then
  echo "Missing 'nasm' (needed on Intel to build ffmpeg). Run: brew install nasm"; exit 1
fi

WORK="$ROOT/.build/ffmpeg-$RID"
PREFIX="$WORK/prefix"
mkdir -p "$WORK/src" "$PREFIX"
if [[ "$(uname -s)" == "Darwin" ]]; then JOBS=$(sysctl -n hw.ncpu); else JOBS=$(nproc); fi
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
export PATH="$PREFIX/bin:$PATH"
if [[ "$(uname -s)" == "Darwin" ]]; then
  export MACOSX_DEPLOYMENT_TARGET=12.0
  export CFLAGS="-mmacosx-version-min=12.0 -O2" CXXFLAGS="-mmacosx-version-min=12.0 -O2" LDFLAGS="-mmacosx-version-min=12.0"
else
  export CFLAGS="-O2 -fPIC" CXXFLAGS="-O2 -fPIC"
fi
SED_INPLACE=(sed -i); [[ "$(uname -s)" == "Darwin" ]] && SED_INPLACE=(sed -i '')

# fetch <url> <folder name after unpacking>
fetch() {
  local url=$1 dir=$2
  local file="$WORK/src/$dir-$(basename "${url%%\?*}")"
  if [[ ! -d "$WORK/src/$dir" ]]; then
    echo "· downloading $(basename "$file")"
    curl -fsSL --retry 3 -o "$file" "$url"
    mkdir -p "$WORK/src/$dir.tmp"
    tar -xf "$file" -C "$WORK/src/$dir.tmp"
    # Archives either contain one top folder or the files directly.
    if [[ $(ls "$WORK/src/$dir.tmp" | wc -l) -eq 1 && -d "$WORK/src/$dir.tmp/$(ls "$WORK/src/$dir.tmp")" ]]; then
      mv "$WORK/src/$dir.tmp/$(ls "$WORK/src/$dir.tmp")" "$WORK/src/$dir"; rmdir "$WORK/src/$dir.tmp"
    else
      mv "$WORK/src/$dir.tmp" "$WORK/src/$dir"
    fi
  fi
  cd "$WORK/src/$dir"
}

step() { echo; echo "== $* =="; }
# built <name>: true when that library was already installed by an earlier run (reruns skip it)
built() { [[ -f "$PREFIX/.done-$1" && "${1:-}" != "" && "$FORCE" != 1 ]]; }
mark() { touch "$PREFIX/.done-$1"; }

step "LAME $LAME_VERSION (MP3, LGPL)"
if ! built lame; then
fetch "https://downloads.sourceforge.net/project/lame/lame/$LAME_VERSION/lame-$LAME_VERSION.tar.gz" lame
"${SED_INPLACE[@]}" '/lame_init_old/d' include/libmp3lame.sym  # known build fix for newer compilers
./configure --prefix="$PREFIX" --disable-shared --enable-static --disable-frontend --disable-decoder --enable-nasm >/dev/null
make -j"$JOBS" >/dev/null && make install >/dev/null
mark lame
fi

step "Ogg $OGG_VERSION and Vorbis $VORBIS_VERSION (BSD)"
if ! built vorbis; then
fetch "https://downloads.xiph.org/releases/ogg/libogg-$OGG_VERSION.tar.gz" ogg
./configure --prefix="$PREFIX" --disable-shared --enable-static >/dev/null && make -j"$JOBS" >/dev/null && make install >/dev/null
fetch "https://downloads.xiph.org/releases/vorbis/libvorbis-$VORBIS_VERSION.tar.gz" vorbis
"${SED_INPLACE[@]}" 's/-force_cpusubtype_ALL//g' configure  # flag removed from newer Apple linkers
./configure --prefix="$PREFIX" --disable-shared --enable-static --with-ogg="$PREFIX" --disable-docs --disable-examples >/dev/null
make -j"$JOBS" >/dev/null && make install >/dev/null
mark vorbis
fi

step "Opus $OPUS_VERSION (BSD)"
if ! built opus; then
fetch "https://downloads.xiph.org/releases/opus/opus-$OPUS_VERSION.tar.gz" opus
./configure --prefix="$PREFIX" --disable-shared --enable-static --disable-doc --disable-extra-programs >/dev/null
make -j"$JOBS" >/dev/null && make install >/dev/null
mark opus
fi

step "libvpx $VPX_VERSION (VP9, BSD)"
if ! built vpx; then
fetch "https://chromium.googlesource.com/webm/libvpx/+archive/refs/tags/v$VPX_VERSION.tar.gz" libvpx
./configure --prefix="$PREFIX" --disable-shared --enable-static --enable-pic --disable-examples --disable-tools \
  --disable-docs --disable-unit-tests --enable-vp9-highbitdepth --as=auto >/dev/null
make -j"$JOBS" >/dev/null && make install >/dev/null
mark vpx
fi

step "libwebp $WEBP_VERSION (BSD)"
if ! built webp; then
fetch "https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-$WEBP_VERSION.tar.gz" webp
./configure --prefix="$PREFIX" --disable-shared --enable-static --disable-gl --disable-sdl --disable-png --disable-jpeg \
  --disable-tiff --disable-gif --disable-wic >/dev/null
make -j"$JOBS" >/dev/null && make install >/dev/null
mark webp
fi

step "libaom $AOM_VERSION (AV1 for AVIF, BSD)"
if ! built aom; then
fetch "https://storage.googleapis.com/aom-releases/libaom-$AOM_VERSION.tar.gz" aom
mkdir -p "$WORK/src/aom-build" && cd "$WORK/src/aom-build"
cmake "$WORK/src/aom" -DCMAKE_INSTALL_PREFIX="$PREFIX" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=0 \
  -DENABLE_TESTS=0 -DENABLE_EXAMPLES=0 -DENABLE_DOCS=0 -DENABLE_TOOLS=0 \
  ${MACOSX_DEPLOYMENT_TARGET:+-DCMAKE_OSX_DEPLOYMENT_TARGET=$MACOSX_DEPLOYMENT_TARGET} >/dev/null
make -j"$JOBS" >/dev/null && make install >/dev/null
mark aom
fi

step "FFmpeg $FFMPEG_VERSION (LGPL)"
fetch "https://ffmpeg.org/releases/ffmpeg-$FFMPEG_VERSION.tar.xz" ffmpeg
PLATFORM_FLAGS=()
if [[ "$(uname -s)" == "Darwin" ]]; then
  # libiconv is part of macOS but has to be named when linking a static ffmpeg.
  PLATFORM_FLAGS=(--enable-videotoolbox --enable-audiotoolbox --enable-coreimage --enable-avfoundation --extra-libs=-liconv)
fi
# --disable-autodetect: only what's listed here goes in, so nothing from Homebrew sneaks into the binary.
./configure --prefix="$PREFIX" \
  --pkg-config-flags=--static \
  --extra-cflags="-I$PREFIX/include" --extra-ldflags="-L$PREFIX/lib" \
  --enable-static --disable-shared --disable-ffplay --disable-ffprobe --disable-doc --disable-debug \
  --disable-autodetect --enable-zlib --enable-bzlib --enable-iconv \
  --enable-libmp3lame --enable-libvorbis --enable-libopus --enable-libvpx --enable-libwebp --enable-libaom \
  "${PLATFORM_FLAGS[@]}" >/dev/null
make -j"$JOBS" >/dev/null

mkdir -p "$OUT"
cp ffmpeg "$OUT/ffmpeg"
strip "$OUT/ffmpeg" 2>/dev/null || true
cp COPYING.LGPLv2.1 "$OUT/FFmpeg-LICENSE.txt"
{
  echo "FFmpeg $FFMPEG_VERSION, built for DeskWheel by scripts/build-ffmpeg.sh on $(date -u +%Y-%m-%d)."
  echo "Licence: GNU LGPL 2.1 or later (no GPL or nonfree components)."
  echo "Source: https://ffmpeg.org/releases/ffmpeg-$FFMPEG_VERSION.tar.xz"
  echo "Libraries: LAME $LAME_VERSION (LGPL), libogg $OGG_VERSION, libvorbis $VORBIS_VERSION, Opus $OPUS_VERSION,"
  echo "  libvpx $VPX_VERSION, libwebp $WEBP_VERSION, libaom $AOM_VERSION (all BSD-style)."
  echo "To rebuild it yourself, or with a changed FFmpeg, run scripts/build-ffmpeg.sh from DeskWheel's source."
  echo
  echo "Configuration:"
  ./ffmpeg -hide_banner -buildconf 2>&1 | sed 's/^/  /'
} > "$OUT/FFmpeg-SOURCE.txt"

echo
echo "Licence check:"
"$OUT/ffmpeg" -hide_banner -L 2>&1 | head -3
if "$OUT/ffmpeg" -hide_banner -buildconf 2>&1 | grep -qE -- '--enable-(gpl|nonfree)'; then
  echo "ERROR: this build includes GPL or nonfree parts; it must not ship."; exit 1
fi
if [[ "$(uname -s)" == "Darwin" ]]; then
  echo "Linked libraries (should be system ones only):"
  otool -L "$OUT/ffmpeg" | tail -n +2
  if otool -L "$OUT/ffmpeg" | grep -qE '/opt/homebrew|/usr/local'; then
    echo "ERROR: ffmpeg links to Homebrew libraries and wouldn't run on other Macs."; exit 1
  fi
fi
echo "Done: $OUT/ffmpeg ($(du -h "$OUT/ffmpeg" | cut -f1))"
