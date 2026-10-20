#!/bin/sh
. "$(dirname "$0")/env.sh"
MODE="${1:-code}"
mirror_dir() {
  powershell.exe -NoProfile -Command "robocopy '$PROJECT/$1' '$MIRROR/$1' /MIR /R:0 /W:0 /NFL /NDL /NJH /NJS /XD __XRayTests_tmp | Out-Null; if (\$LASTEXITCODE -ge 8) { exit 1 } else { exit 0 }" || echo "robocopy failed: $1"
}
if [ "$MODE" = "full" ]; then
  for d in Assets Packages ProjectSettings; do mirror_dir "$d"; done
else
  for d in Assets/_Source/Scripts Assets/_Source/Tests Assets/_Source/Shaders Assets/_Modules "Assets/_Tools/X-Ray OGF OMG Importer"; do
    [ -d "$PROJECT/$d" ] && mirror_dir "$d"
  done
  cp "$PROJECT/Assets/_Source/Settings/Input/StalkerControls.inputactions" "$MIRROR/Assets/_Source/Settings/Input/StalkerControls.inputactions" 2>/dev/null
fi
date +%s > "$WORK/last_sync"
echo "synced ($MODE) -> $MIRROR"
