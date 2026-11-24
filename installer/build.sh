#!/usr/bin/env bash
# Builds the classic Windows installer artifacts for Clipped.
#
#   setup:    Clipped-Setup-<version>.exe   (Inno Setup, per-user install)
#   portable: Clipped-<version>-portable.zip
#
# Usage: bash installer/build.sh
#
# Environment:
#   DOTNET_ROOT          SDK location (else dotnet on PATH with an SDK, else CLIPPED_DOTNET)
#   CLIPPED_BUILD_DIR    scratch root (default <parent of repo>/build/clipped-installer)
#   CLIPPED_PUBLISH_DIR  package an existing publish folder instead of publishing
#   ISCC                 path to ISCC.exe
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
# Scratch lives next to the repo, never inside it, so the publish scrub cannot eat it.
BUILD_DIR="${CLIPPED_BUILD_DIR:-$(dirname "$REPO_DIR")/build/clipped-installer}"
DIST_DIR="$BUILD_DIR/dist"
PUBLISH_DIR="${CLIPPED_PUBLISH_DIR:-$BUILD_DIR/publish}"
SKIP_PUBLISH="${CLIPPED_SKIP_PUBLISH:-0}"

win_path() { cygpath -m "$1"; }

ISS="$SCRIPT_DIR/Clipped.iss"
APP_VERSION="$(sed -n 's/^#define AppVersion "\(.*\)"/\1/p' "$ISS" | head -1)"
if [ -z "$APP_VERSION" ]; then
  echo "error: could not read AppVersion from $ISS" >&2
  exit 1
fi

DOTNET="${DOTNET:-}"
if [ -z "$DOTNET" ]; then
  if [ -n "${DOTNET_ROOT:-}" ] && [ -x "${DOTNET_ROOT}/dotnet.exe" ]; then
    DOTNET="${DOTNET_ROOT}/dotnet.exe"
  else
    path_dotnet="$(command -v dotnet || true)"
    # A dotnet without an SDK still exits 0 on --list-sdks, so check for output.
    if [ -n "$path_dotnet" ] && [ -n "$("$path_dotnet" --list-sdks 2>/dev/null | head -1)" ]; then
      DOTNET="$path_dotnet"
    else
      # Extra SDK locations on this machine, overridable with CLIPPED_DOTNET.
      for candidate in "${CLIPPED_DOTNET:-}" "/d/dotnet-sdk/dotnet.exe" "/d/Apps/dotnet/dotnet.exe"; do
        if [ -n "$candidate" ] && [ -x "$candidate" ]; then DOTNET="$candidate"; break; fi
      done
    fi
  fi
fi
if [ -z "$DOTNET" ]; then
  echo "error: dotnet SDK not found (set DOTNET_ROOT or CLIPPED_DOTNET)" >&2
  exit 1
fi
if [ -z "$("$DOTNET" --list-sdks 2>/dev/null | head -1)" ]; then
  echo "error: $DOTNET has no .NET SDK (set DOTNET_ROOT or CLIPPED_DOTNET)" >&2
  exit 1
fi

ISCC="${ISCC:-}"
if [ -z "$ISCC" ]; then
  for candidate in "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" "/c/Program Files/Inno Setup 6/ISCC.exe"; do
    if [ -f "$candidate" ]; then ISCC="$candidate"; break; fi
  done
fi
if [ -z "$ISCC" ]; then
  echo "error: Inno Setup compiler not found (set ISCC=/path/to/ISCC.exe)" >&2
  exit 1
fi

# assets/Clipped.ico is owned by the visuals lane; consume it, never regenerate it.
if [ ! -f "$REPO_DIR/assets/Clipped.ico" ]; then
  echo "error: assets/Clipped.ico missing (visuals lane owns it)" >&2
  exit 1
fi

if [ "$SKIP_PUBLISH" = "0" ] && [ -z "${CLIPPED_PUBLISH_DIR:-}" ]; then
  echo "==> publishing self-contained release build"
  rm -rf "$PUBLISH_DIR"
  "$DOTNET" publish "$(win_path "$REPO_DIR")/Clipped.csproj" \
    -c Release -r win-x64 --self-contained true \
    -o "$(win_path "$PUBLISH_DIR")" -p:DebugType=none
else
  echo "==> using existing publish folder: $PUBLISH_DIR"
fi

if [ ! -f "$PUBLISH_DIR/Clipped.exe" ]; then
  echo "error: $PUBLISH_DIR/Clipped.exe missing" >&2
  exit 1
fi

echo "==> compiling installer"
mkdir -p "$DIST_DIR"
rm -f "$DIST_DIR/Clipped-Setup-$APP_VERSION.exe" "$DIST_DIR/Clipped-$APP_VERSION-portable.zip"
# MSYS path conversion would mangle the /D<name>=<value> switches.
MSYS2_ARG_CONV_EXCL='*' MSYS_NO_PATHCONV=1 "$ISCC" \
  "/DPublishDir=$(win_path "$PUBLISH_DIR")" \
  "/DOutputDir=$(win_path "$DIST_DIR")" \
  "$(win_path "$SCRIPT_DIR")/Clipped.iss"

echo "==> packaging portable build"
powershell.exe -NoProfile -Command \
  "Compress-Archive -Path '$(win_path "$PUBLISH_DIR")/*' -DestinationPath '$(win_path "$DIST_DIR")/Clipped-$APP_VERSION-portable.zip' -Force"

SETUP="$DIST_DIR/Clipped-Setup-$APP_VERSION.exe"
PORTABLE="$DIST_DIR/Clipped-$APP_VERSION-portable.zip"
SETUP_SHA="$(sha256sum "$SETUP" | cut -d' ' -f1)"
EXE_MTIME="$(date -r "$PUBLISH_DIR/Clipped.exe" '+%Y-%m-%d %H:%M:%S')"

echo
echo "artifacts:"
ls -la "$DIST_DIR"
echo
echo "INSTALLER COMPLETE — setup: $SETUP — portable: $PORTABLE — sha256: $SETUP_SHA — source exe mtime: $EXE_MTIME"
