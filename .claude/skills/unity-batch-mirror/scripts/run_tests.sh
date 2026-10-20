#!/bin/sh
. "$(dirname "$0")/env.sh"
PLATFORM="$1"
ASSEMBLIES="$2"
FILTER="$3"
if [ -z "$PLATFORM" ] || [ -z "$ASSEMBLIES" ]; then
  echo "usage: run_tests.sh <EditMode|PlayMode> <Assembly1;Assembly2> [testFilter]"
  exit 2
fi
RESULT="$WORK/results_$PLATFORM.xml"
LOG="$WORK/tests_$PLATFORM.log"
rm -f "$RESULT" "$LOG"
set -- -batchmode -projectPath "$(winpath "$MIRROR")" -runTests -testPlatform "$PLATFORM" -assemblyNames "$ASSEMBLIES" -testResults "$(winpath "$RESULT")" -logFile "$(winpath "$LOG")"
if [ -n "$FILTER" ]; then set -- "$@" -testFilter "$FILTER"; fi
"$UNITY" "$@" > /dev/null 2>&1
echo "exit $? log $(winpath "$LOG")"
grep -E "error CS|Scripts have compiler errors" "$LOG" | sort -u | head -20
if [ -f "$RESULT" ]; then
  node "$SKILL_DIR/results.js" "$RESULT"
else
  echo "no results file: tests did not run"
fi
