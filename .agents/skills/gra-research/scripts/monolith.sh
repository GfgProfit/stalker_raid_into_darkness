#!/bin/sh
CACHE="${MONOLITH_CACHE:-${TMPDIR:-${TEMP:-${TMP:-/tmp}}}/xray-monolith}"
if [ "$1" = "search" ]; then
  shift
  gh search code "$*" --repo themrdemonized/xray-monolith --limit 40
  exit $?
fi
if [ -z "$1" ]; then
  echo "usage: monolith.sh <src/path/File.cpp>... | monolith.sh search <symbol>"
  exit 2
fi
for FILE in "$@"; do
  TARGET="$CACHE/$FILE"
  if [ ! -f "$TARGET" ]; then
    mkdir -p "$(dirname "$TARGET")"
    curl -sfL "https://raw.githubusercontent.com/themrdemonized/xray-monolith/master/$FILE" -o "$TARGET" || { echo "fail $FILE"; rm -f "$TARGET"; continue; }
  fi
  echo "$TARGET"
done
