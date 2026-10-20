#!/bin/sh
. "$(dirname "$0")/env.sh"
WIN_MIRROR="$(winpath "$MIRROR")"
powershell.exe -NoProfile -Command "\$m = '$WIN_MIRROR'; Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | Where-Object { \$_.CommandLine -and \$_.CommandLine.Contains(\$m) } | ForEach-Object { Write-Output ('kill ' + \$_.ProcessId); Stop-Process -Id \$_.ProcessId -Force }"
rm -f "$MIRROR/Temp/UnityLockfile"
echo "mirror unlocked"
