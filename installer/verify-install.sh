#!/usr/bin/env bash
# Round-trip check for the classic installer, without disturbing the dev machine:
# installs into a scratch folder, inspects the machine state, uninstalls, restores.
# The app is never launched (it is single-instance and owns the Win+V hotkey).
#
# Usage: CLIPPED_VERIFY=1 bash installer/verify-install.sh [path-to-setup.exe]
#
# This installs and uninstalls Clipped on the live machine and rewrites
# HKCU\Software\Classes\clipped plus the Start menu entry, so it only runs on
# request.
set -uo pipefail

export MSYS2_ARG_CONV_EXCL='*'
export MSYS_NO_PATHCONV=1

if [ "${CLIPPED_VERIFY:-0}" != "1" ]; then
  echo "skipped: this does a live install/uninstall round trip."
  echo "re-run with CLIPPED_VERIFY=1 to actually install and uninstall Clipped."
  exit 0
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
# Scratch lives next to the repo, never inside it.
SCRATCH_DIR="${CLIPPED_BUILD_DIR:-$(dirname "$REPO_DIR")/build/clipped-installer}"
SETUP="${1:-$SCRATCH_DIR/dist/Clipped-Setup-0.1.0.exe}"
TEST_DIR="$SCRATCH_DIR/installtest"
BACKUP_DIR="$SCRATCH_DIR/regbackup"
START_MENU="$(cygpath -u "$APPDATA")/Microsoft/Windows/Start Menu/Programs/Clipped"

win_path() { cygpath -m "$1"; }
win_dir() { cygpath -w "$1"; }

if [ ! -f "$SETUP" ]; then
  echo "error: setup not found: $SETUP" >&2
  exit 1
fi

reg_get() {
  reg.exe query "$1" /ve 2>/dev/null | tr -d '\r' \
    | sed -n '2,$s/^[[:space:]]*[^[:space:]]*[[:space:]]*REG_[A-Z_]*[[:space:]]*//p'
}

reg_value() {
  reg.exe query "$1" /v "$2" 2>/dev/null | tr -d '\r' \
    | sed -n '2,$s/^[[:space:]]*[^[:space:]]*[[:space:]]*REG_[A-Z_]*[[:space:]]*//p'
}

reg_run_value() {
  reg.exe query 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run' /v Clipped 2>/dev/null | tr -d '\r' \
    | sed -n '2,$s/^[[:space:]]*Clipped[[:space:]]*REG_[A-Z_]*[[:space:]]*//p'
}

rm -rf "$TEST_DIR" "$BACKUP_DIR"
mkdir -p "$BACKUP_DIR"

echo "==> backing up existing registrations"
reg.exe export 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clipped' "$(win_dir "$BACKUP_DIR/uninstall.reg")" /y >/dev/null 2>&1
reg.exe export 'HKCU\Software\Classes\clipped' "$(win_dir "$BACKUP_DIR/protocol.reg")" /y >/dev/null 2>&1

# Simulate a clean user machine: the app's own fallback entry must not be in the way.
if [ "${KEEP_EXISTING:-0}" = "0" ]; then
  reg.exe delete 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clipped' /f >/dev/null 2>&1
  reg.exe delete 'HKCU\Software\Classes\clipped' /f >/dev/null 2>&1
fi

echo "==> installing silently to $TEST_DIR"
"$(cygpath -u "$SETUP")" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART "/LOG=$(win_path "$BACKUP_DIR")/install.log" "/DIR=$(win_path "$TEST_DIR")"
echo "  setup exit code: $?"

FAIL=0
check() {
  if [ "$2" = "0" ]; then echo "  ok    $1"; else echo "  FAIL  $1"; FAIL=1; fi
}

echo "==> inspecting installed state"
[ -f "$TEST_DIR/Clipped.exe" ]; check "Clipped.exe installed" $?
[ -f "$TEST_DIR/unins000.exe" ]; check "uninstaller present" $?
FILE_COUNT="$(ls "$TEST_DIR" 2>/dev/null | wc -l)"
[ "$FILE_COUNT" -gt 100 ]; check "payload copied ($FILE_COUNT files)" $?

UNINSTALL_STR="$(reg_value 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clipped' UninstallString)"
if [ -z "$UNINSTALL_STR" ]; then
  UNINSTALL_STR="$(reg_value 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clipped_is1' UninstallString)"
fi
case "$UNINSTALL_STR" in *unins000.exe*) check "uninstall entry points at setup uninstaller" 0;; *) check "uninstall entry points at setup uninstaller ($UNINSTALL_STR)" 1;; esac

PROTO="$(reg_get 'HKCU\Software\Classes\clipped\shell\open\command')"
case "$PROTO" in *Clipped.exe*) check "clipped: protocol registered" 0;; *) check "clipped: protocol registered ($PROTO)" 1;; esac

RUNVAL="$(reg_run_value)"
case "$RUNVAL" in *Clipped.exe*) check "autostart entry registered" 0;; *) check "autostart entry registered ($RUNVAL)" 1;; esac

[ -f "$START_MENU/Clipped.lnk" ]; check "start menu shortcut" $?
[ -f "$START_MENU/Uninstall Clipped.lnk" ]; check "start menu uninstall shortcut" $?

echo "==> uninstalling silently"
"$TEST_DIR/unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART "/LOG=$(win_path "$BACKUP_DIR")/uninstall.log"
echo "  uninstaller exit code: $?"
sleep 3

echo "==> inspecting post-uninstall state"
[ ! -d "$TEST_DIR" ] || [ -z "$(ls -A "$TEST_DIR" 2>/dev/null)" ]; check "install folder removed" $?
reg.exe query 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clipped' >/dev/null 2>&1
UNINSTALL_KEY_LEFT=$?
reg.exe query 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clipped_is1' >/dev/null 2>&1
UNINSTALL_KEY_IS1_LEFT=$?
[ "$UNINSTALL_KEY_LEFT" != "0" ] && [ "$UNINSTALL_KEY_IS1_LEFT" != "0" ]; check "uninstall entry removed" $?
reg.exe query 'HKCU\Software\Classes\clipped' >/dev/null 2>&1
[ "$?" != "0" ]; check "protocol handler removed" $?
[ ! -d "$START_MENU" ]; check "start menu group removed" $?

echo "==> restoring dev registrations"
[ -f "$BACKUP_DIR/uninstall.reg" ] && reg.exe import "$(win_dir "$BACKUP_DIR/uninstall.reg")" >/dev/null 2>&1
[ -f "$BACKUP_DIR/protocol.reg" ] && reg.exe import "$(win_dir "$BACKUP_DIR/protocol.reg")" >/dev/null 2>&1

if [ "$FAIL" = "0" ]; then echo "VERIFY OK"; else echo "VERIFY FAILED"; fi
exit "$FAIL"
