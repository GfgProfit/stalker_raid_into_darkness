#!/bin/sh
. "$(dirname "$0")/env.sh"
METHOD="$1"
NAME="${2:-method}"
GRAPHICS="${3:-nographics}"
if [ -z "$METHOD" ]; then
  echo "usage: run_method.sh <Namespace.Class.Method> [log-name] [nographics|graphics]"
  exit 2
fi
LOG="$WORK/$NAME.log"
rm -f "$LOG"
FLAGS="-batchmode"
if [ "$GRAPHICS" = "nographics" ]; then FLAGS="$FLAGS -nographics"; fi
"$UNITY" $FLAGS -projectPath "$(winpath "$MIRROR")" -executeMethod "$METHOD" -logFile "$(winpath "$LOG")" > /dev/null 2>&1
CODE=$?
echo "exit $CODE log $(winpath "$LOG")"
grep -E "error CS|Scripts have compiler errors|Aborting batchmode" "$LOG" | sort -u | head -20
exit $CODE
