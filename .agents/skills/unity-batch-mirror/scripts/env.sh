#!/bin/sh
PROJECT="${STALKER_PROJECT:-E:/Projects/stalker_raid_into_darkness}"
MIRROR="${STALKER_MIRROR:?Set STALKER_MIRROR to the authorized absolute mirror path}"
UNITY="${STALKER_UNITY:-/c/Program Files/Unity/Hub/Editor/6000.3.4f1/Editor/Unity.exe}"
WORK="${STALKER_WORK:-$(dirname "$MIRROR")/batch}"
SKILL_DIR="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$WORK"
winpath() { cygpath -w "$1" 2>/dev/null || echo "$1"; }
