param([Parameter(Mandatory=$true)][string]$ZigCompiler, [Parameter(Mandatory=$true)][string]$PixSdk, [Parameter(Mandatory=$true)][string]$ArtifactOutput, [string]$UnityPluginApi='C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Data\PluginAPI')
$ErrorActionPreference='Stop'
$archive=[IO.Path]::GetFullPath('E:\Projects\stalker_raid_into_darkness_Archive')+[IO.Path]::DirectorySeparatorChar
$output=[IO.Path]::GetFullPath($ArtifactOutput)
if (!$output.StartsWith($archive,[StringComparison]::OrdinalIgnoreCase))
{
    throw 'Native build intermediates must remain in Archive.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR=Join-Path $output 'Cache'
$env:ZIG_LOCAL_CACHE_DIR=Join-Path $output 'LocalCache'
$source=Join-Path $PSScriptRoot 'RenderCostPixBridge.cpp'
$library=Join-Path $PixSdk 'bin\x64\WinPixEventRuntime.lib'
$binary=Join-Path $output 'RenderCostPixBridge.dll'
& $ZigCompiler c++ -target x86_64-windows-gnu -shared -O2 -fms-extensions -DUSE_PIX -DUSE_PIX_ON_ALL_ARCHITECTURES -I (Join-Path $PixSdk 'Include\WinPixEventRuntime') -I $UnityPluginApi $source $library -o $binary -lkernel32 -lshell32
if ($LASTEXITCODE -ne 0)
{
    throw 'Native PIX bridge compilation failed.'
}
$plugins=Join-Path $PSScriptRoot '..\Plugins\x86_64'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item -LiteralPath $binary -Destination (Join-Path $plugins 'RenderCostPixBridge.dll')
Copy-Item -LiteralPath (Join-Path $PixSdk 'bin\x64\WinPixEventRuntime.dll') -Destination (Join-Path $plugins 'WinPixEventRuntime.dll')
Get-FileHash -LiteralPath $binary,(Join-Path $plugins 'WinPixEventRuntime.dll') -Algorithm SHA256
