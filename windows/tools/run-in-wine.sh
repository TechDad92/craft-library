#!/bin/bash
# Usage: run-in-wine.sh [--build] <wait-seconds> <shot-name> [xdotool steps...]
# Starts the app under Wine on a virtual screen (Xvfb), does the xdotool steps, and saves a screenshot
# to $CRAFT_TEST_DIR/shots. Needs wine64, xvfb, xdotool and imagemagick. Wine is slow to start (~45 s).
S="${CRAFT_TEST_DIR:-$HOME/craft-wine-test}"; mkdir -p "$S/shots"
export WINEPREFIX=$S/wineprefix WINEDEBUG=-all DISPLAY=:99 CRAFT_TRACE=1
pgrep -x Xvfb >/dev/null || { Xvfb :99 -screen 0 1600x1000x24 >/dev/null 2>&1 & sleep 2; }
if [ "$1" = "--build" ]; then
  shift
  wineserver -k 2>/dev/null; sleep 1
  (cd "$(dirname "$0")/.." && dotnet publish CraftLibrary.App -nologo -v q -c Release -p:PublishSingleFile=false -o $S/wintest 2>&1 | grep -E " error " | head -20)
fi
WAIT=$1; NAME=$2; shift 2
if ! pgrep -f CraftLibrary.exe >/dev/null; then
  rm -f "$WINEPREFIX/drive_c/users/root/AppData/Local/Craft Library/bindings.log"
  (cd $S/wintest && wine CraftLibrary.exe > $S/wine-run.log 2>&1 &)
fi
sleep $WAIT
for step in "$@"; do
  case "$step" in
    sleep:*) sleep ${step#sleep:} ;;
    *) xdotool $step; sleep 0.8 ;;
  esac
done
sleep 1.5
import -window root $S/shots/$NAME.png
echo "shot $NAME"
