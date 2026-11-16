param([string[]]$Routes = @('military-overview', 'military-player-eye'))

$ErrorActionPreference = 'Stop'
$seriesId = Get-Date -Format 'yyyyMMdd_HHmmss'
foreach ($route in $Routes)
{
    foreach ($entry in @(@('U1', 'U'), @('G1_1', 'G1'), @('G1_2', 'G1'), @('U2', 'U')))
    {
        python (Join-Path $PSScriptRoot 'run_benchmark.py') $route $entry[1] --label ($seriesId + '_' + $route + '_' + $entry[0])
        if ($LASTEXITCODE -ne 0)
        {
            throw "Series stopped after invalid process $route / $($entry[0]); evidence retained."
        }
    }
}
