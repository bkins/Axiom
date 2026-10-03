[CmdletBinding()]
param([string]$AppPath, [string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $AppPath) { $AppPath = Join-Path $repoRoot 'src/Axiom.App/bin/Release/net10.0/Axiom.App.exe' }
$evidence = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot ('artifacts/UsageReliability-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $evidence) { throw 'Choose a fresh evidence directory.' }
$priorStateRoot = $env:AXIOM_STATE_ROOT
$process = Start-Process -FilePath $AppPath -ArgumentList @('--usage-reliability-probe', ('"' + $evidence + '"')) -PassThru -WindowStyle Hidden
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $activationSent = $false
    while (-not $process.WaitForExit(250)) {
        if (-not $activationSent -and (Test-Path -LiteralPath (Join-Path $evidence 'activation-ready.txt'))) {
            $document = [IO.File]::ReadAllText((Join-Path $evidence 'activation-ready.txt'))
            $env:AXIOM_STATE_ROOT = Join-Path $evidence 'state'
            $secondary = Start-Process -FilePath $AppPath -ArgumentList @('"' + $document + '"') -PassThru -WindowStyle Hidden
            if (-not $secondary.WaitForExit(12000)) { Stop-Process -Id $secondary.Id -Force; throw 'Second launch did not forward and exit.' }
            if ($secondary.ExitCode -ne 0) { throw 'Second launch failed.' }
            $activationSent = $true
            $env:AXIOM_STATE_ROOT = $priorStateRoot
        }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Native reliability probe timed out.' }
    }
    if (Test-Path -LiteralPath (Join-Path $evidence 'error.txt')) { throw ([IO.File]::ReadAllText((Join-Path $evidence 'error.txt'))) }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $evidence 'result.json'))) { throw 'Native reliability evidence was not produced.' }
    Get-Content -LiteralPath (Join-Path $evidence 'result.json') -Raw
    "Evidence retained at $evidence"
}
finally {
    $env:AXIOM_STATE_ROOT = $priorStateRoot
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
