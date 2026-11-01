#!/usr/bin/env bash
# Installs every MSYS2 UCRT64 package needed to build and run UxPlay.
set -u
PKGS=(git
  mingw-w64-ucrt-x86_64-cmake mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-ninja
  mingw-w64-ucrt-x86_64-pkgconf mingw-w64-ucrt-x86_64-libplist mingw-w64-ucrt-x86_64-openssl
  mingw-w64-ucrt-x86_64-gstreamer mingw-w64-ucrt-x86_64-gst-plugins-base
  mingw-w64-ucrt-x86_64-gst-plugins-good mingw-w64-ucrt-x86_64-gst-plugins-bad
  mingw-w64-ucrt-x86_64-gst-libav mingw-w64-ucrt-x86_64-ntldd)
for i in 1 2 3 4 5; do
  if pacman -S --noconfirm --needed --disable-download-timeout "${PKGS[@]}" > /tmp/pac.log 2>&1; then
    echo "pacman OK (try $i)"; exit 0
  fi
  echo "pacman FAIL (try $i)"; tail -3 /tmp/pac.log
done
exit 1
