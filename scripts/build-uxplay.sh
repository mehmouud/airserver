#!/usr/bin/env bash
# Builds vendor/UxPlay (internal mDNS, no Bonjour) and stages a self-contained
# engine in dist/AirServer/engine (uxplay.exe + DLLs + GStreamer plugins).
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/vendor/UxPlay"
BUILD="$ROOT/build/uxplay"
ENGINE="$ROOT/dist/AirServer/engine"
UCRT=/ucrt64

echo "== apply patches"
# Git for Windows (autocrlf=true) checks out CRLF; MSYS2 git does not convert -> force LF once.
if [ "$(git -C "$SRC" config core.autocrlf || true)" != "false" ]; then
  git -C "$SRC" config core.autocrlf false
  git -C "$SRC" rm --cached -r -q . && git -C "$SRC" reset --hard -q
fi
for p in "$ROOT"/patches/*.patch; do
  if git -C "$SRC" apply --reverse --check "$p" 2>/dev/null; then echo "  already: $(basename "$p")";
  else git -C "$SRC" apply "$p"; echo "  applied: $(basename "$p")"; fi
done

echo "== configure + build"
mkdir -p "$BUILD"; cd "$BUILD"
cmake -G Ninja -DCMAKE_BUILD_TYPE=Release "$SRC" > cmake.log 2>&1 || { tail -30 cmake.log; exit 1; }
ninja > ninja.log 2>&1 || { tail -40 ninja.log; exit 1; }
ls -la uxplay.exe

echo "== stage engine"
rm -rf "$ENGINE"; mkdir -p "$ENGINE/lib/gstreamer-1.0" "$ENGINE/libexec"
cp uxplay.exe "$ENGINE/"
# GStreamer plugins actually needed by the UxPlay pipelines on Windows
PLUGINS="coreelements app typefindfunctions playback videoconvertscale videorate videoparsersbad
  libav d3d11 d3d12 nvcodec autodetect audioconvert audioresample volume wasapi directsound
  isomp4 rtp rtpmanager udp tcp coretracers videofilter audioparsers jpeg debugutilsbad
  adaptivedemux2 hls soup level imagefreeze pango"
for p in $PLUGINS; do
  f="$UCRT/lib/gstreamer-1.0/libgst$p.dll"
  if [ -f "$f" ]; then cp "$f" "$ENGINE/lib/gstreamer-1.0/"; else echo "  (plugin $p not found, skipped)"; fi
done
cp "$UCRT/libexec/gstreamer-1.0/gst-plugin-scanner.exe" "$ENGINE/libexec/" 2>/dev/null || true
# Resolve every DLL dependency that lives in ucrt64/bin (recursive, via ntldd)
deps() { ntldd -R "$@" 2>/dev/null | grep -io '[a-z]:\\msys64\\ucrt64\\bin\\[^ ]*\.dll' | sort -u; }
cp "$UCRT/bin/gst-inspect-1.0.exe" "$ENGINE/libexec/"   # diagnostics + verification below
deps "$ENGINE/uxplay.exe" "$ENGINE"/lib/gstreamer-1.0/*.dll "$ENGINE"/libexec/*.exe | while read -r d; do
  cp -n "$(cygpath -u "$d")" "$ENGINE/"
done
cp "$SRC/LICENSE" "$ENGINE/UxPlay-LICENSE.txt"
echo "  engine files: $(find "$ENGINE" -type f | wc -l), size: $(du -sh "$ENGINE" | cut -f1)"

echo "== verify every GStreamer element UxPlay needs, using ONLY the staged engine"
export PATH="$ENGINE:/usr/bin"
export GST_PLUGIN_SYSTEM_PATH_1_0="$(cygpath -w "$ENGINE/lib/gstreamer-1.0")"
export GST_PLUGIN_PATH_1_0=""
export GST_PLUGIN_SCANNER_1_0="$(cygpath -w "$ENGINE/libexec/gst-plugin-scanner.exe")"
export GST_REGISTRY_1_0="$(cygpath -w "$ROOT/build/verify-registry.bin")"
rm -f "$ROOT/build/verify-registry.bin"
REQUIRED="appsrc queue capsfilter filesink typefind decodebin playbin playbin3
  h264parse h265parse avdec_h264 avdec_h265 d3d11h264dec d3d11h265dec d3d12h264dec d3d12h265dec
  videoconvert videoscale videoflip imagefreeze jpegdec textoverlay
  autovideosink d3d11videosink d3d12videosink fpsdisplaysink
  audioconvert audioresample avdec_aac avdec_alac aacparse level volume autoaudiosink wasapisink directsoundsink
  rtph264pay rtph265pay mp4mux"
missing=0
for el in $REQUIRED; do
  "$ENGINE/libexec/gst-inspect-1.0.exe" "$el" > /dev/null 2>&1 || { echo "  MISSING element: $el"; missing=1; }
done
[ $missing -eq 0 ] && echo "  all $(echo $REQUIRED | wc -w) elements OK" || exit 1
"$ENGINE/uxplay.exe" -v 2>&1 | head -1 || true
