[CmdletBinding()]
param([string]$AppPath, [string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $AppPath) { $AppPath = Join-Path $repoRoot 'src/Axiom.App/bin/Release/net10.0/Axiom.App.exe' }
$evidence = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot ('artifacts/EverydayUse-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $evidence) { throw 'Choose a fresh evidence directory.' }
$process = Start-Process -FilePath $AppPath -ArgumentList @('--everyday-use-probe', ('"' + $evidence + '"')) -PassThru -WindowStyle Hidden
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(75)
    while (-not $process.WaitForExit(1000)) { if ([DateTime]::UtcNow -ge $deadline) { throw 'Native everyday-use probe timed out.' } }
    if (Test-Path -LiteralPath (Join-Path $evidence 'error.txt')) { throw (Get-Content -LiteralPath (Join-Path $evidence 'error.txt') -Raw) }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $evidence 'result.json'))) { throw 'Native everyday-use evidence was not produced.' }
    Get-Content -LiteralPath (Join-Path $evidence 'result.json') -Raw
    "Evidence retained at $evidence"
}
finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
