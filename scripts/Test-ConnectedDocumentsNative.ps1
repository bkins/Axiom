[CmdletBinding()]
param([string]$OutputDirectory, [switch]$SkipBuild, [int]$TimeoutSeconds = 120)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$probeRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot ('artifacts\ConnectedDocuments-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $probeRoot) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $probeRoot | Out-Null
if (-not $SkipBuild) {
    dotnet build (Join-Path $repoRoot 'Axiom.slnx') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
$appPath = Join-Path $repoRoot 'src\Axiom.App\bin\Release\net10.0\Axiom.App.exe'
foreach ($mode in @('--connected-documents-probe', '--verify-connected-documents')) {
    $process = Start-Process -FilePath $appPath -ArgumentList @($mode, ('"' + $probeRoot + '"')) -PassThru -WindowStyle Hidden
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.WaitForExit(1000)) { if ([DateTime]::UtcNow -ge $deadline) { throw "Native probe timed out: $mode" } }
        if (Test-Path -LiteralPath (Join-Path $probeRoot 'error.txt')) { throw (Get-Content -LiteralPath (Join-Path $probeRoot 'error.txt') -Raw) }
        if ($process.ExitCode -ne 0) { throw "Native probe failed: $mode" }
    }
    finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
Get-Content -LiteralPath (Join-Path $probeRoot 'result.json') -Raw
"Evidence retained at $probeRoot"
