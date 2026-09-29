[CmdletBinding()]
param(
    [int]$AllowedLossMilliseconds = 350,
    [int]$Trials = 3,
    [string]$OutputDirectory,
    [switch]$SkipBuild,
    [switch]$ReadingMode
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $repoRoot 'src\Axiom.App\bin\Release\net10.0\Axiom.App.exe'
$probeRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot ('artifacts\MultiBufferRecovery-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $probeRoot) { throw 'Choose a new output directory; existing evidence is not overwritten.' }
New-Item -ItemType Directory -Path $probeRoot | Out-Null
if (-not $SkipBuild) {
    dotnet build (Join-Path $repoRoot 'Axiom.slnx') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
$results = @()
for ($trial = 1; $trial -le $Trials; $trial++) {
    $trialRoot = Join-Path $probeRoot ('trial-' + $trial)
    New-Item -ItemType Directory -Path $trialRoot | Out-Null
    $process = $null
    try {
        $probeArguments = @('--multi-buffer-recovery-probe', ('"' + $trialRoot + '"'))
        if ($ReadingMode) { $probeArguments += '--reading-mode' }
        $process = Start-Process -FilePath $appPath -ArgumentList $probeArguments -PassThru -WindowStyle Hidden
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (-not (Test-Path -LiteralPath (Join-Path $trialRoot 'ready.txt'))) {
            if (Test-Path -LiteralPath (Join-Path $trialRoot 'error.txt')) { throw (Get-Content -LiteralPath (Join-Path $trialRoot 'error.txt') -Raw) }
            if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Native multi-buffer recovery probe did not become ready.' }
            Start-Sleep -Milliseconds 50
        }
        Start-Sleep -Milliseconds (2100 + (($trial * 97) % 300))
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
        $resultPath = Join-Path $trialRoot 'result.json'
        $verify = Start-Process -FilePath $appPath -ArgumentList @('--verify-multi-buffer-recovery', ('"' + $trialRoot + '"'), ('"' + $resultPath + '"')) -PassThru -WindowStyle Hidden -Wait
        if ($verify.ExitCode -ne 0) { throw (Get-Content -LiteralPath $resultPath -Raw) }
        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        if ($result.MaximumObservedLossMilliseconds -gt $AllowedLossMilliseconds) { throw "Trial $trial recovery loss $($result.MaximumObservedLossMilliseconds) ms exceeded $AllowedLossMilliseconds ms." }
        $results += $result
        [pscustomobject]@{ Trial = $trial; SavedBuffers = 3; UntitledBuffers = 2; MaximumObservedLossMilliseconds = $result.MaximumObservedLossMilliseconds; Result = 'PASS' }
    }
    finally { if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
@{ AllowedLossMilliseconds = $AllowedLossMilliseconds; ReadingMode = [bool]$ReadingMode; Trials = $results; EvidenceRoot = $probeRoot } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $probeRoot 'summary.json')
"Evidence retained at $probeRoot"
