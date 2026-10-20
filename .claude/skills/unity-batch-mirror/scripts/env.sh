#!/bin/sh
PROJECT="${STALKER_PROJECT:-E:/Projects/stalker_raid_into_darkness}"
MIRROR="${STALKER_MIRROR:-C:/Users/kagami/AppData/Local/Temp/claude/e--Projects-stalker-raid-into-darkness/f9f30669-85fe-4c0e-9402-a46207e682fe/scratchpad/CleanProject}"
UNITY="${STALKER_UNITY:-/c/Program Files/Unity/Hub/Editor/6000.3.4f1/Editor/Unity.exe}"
WORK="${STALKER_WORK:-$(dirname "$MIRROR")/batch}"
SKILL_DIR="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$WORK"
winpath() { cygpath -w "$1" 2>/dev/null || echo "$1"; }
