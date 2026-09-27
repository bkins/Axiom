[CmdletBinding()]
param(
    [int]$AllowedLossMilliseconds = 350
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$probeRoot = Join-Path $env:TEMP ("AxiomRecovery-" + [Guid]::NewGuid().ToString('N'))
$readyPath = Join-Path $probeRoot 'ready.txt'
$heartbeatPath = Join-Path $probeRoot 'heartbeat.txt'
$resultPath = Join-Path $probeRoot 'result.txt'
$appPath = Join-Path $repoRoot 'src\Axiom.App\bin\Release\net10.0\Axiom.App.exe'

New-Item -ItemType Directory -Path $probeRoot | Out-Null
try {
    dotnet build (Join-Path $repoRoot 'Axiom.slnx') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    $process = Start-Process -FilePath $appPath -ArgumentList @('--recovery-probe', $probeRoot, $readyPath, $heartbeatPath) -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $readyPath)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Recovery probe did not become ready.' }
        Start-Sleep -Milliseconds 50
    }

    Start-Sleep -Milliseconds 2200
    $announced = [int](Get-Content -Raw -LiteralPath $heartbeatPath)
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()

    $verifyProcess = Start-Process -FilePath $appPath -ArgumentList @('--verify-recovery', $probeRoot, $resultPath) -PassThru -Wait -WindowStyle Hidden
    if ($verifyProcess.ExitCode -ne 0) { throw "Recovery verification failed with exit code $($verifyProcess.ExitCode)." }
    $recovered = [int](Get-Content -Raw -LiteralPath $resultPath)
    $observedLossMilliseconds = ($announced - $recovered) * 50
    if ($observedLossMilliseconds -gt $AllowedLossMilliseconds) {
        throw "Observed recovery loss ${observedLossMilliseconds}ms exceeded ${AllowedLossMilliseconds}ms."
    }

    [pscustomobject]@{
        AnnouncedSequence = $announced
        RecoveredSequence = $recovered
        ObservedLossMilliseconds = $observedLossMilliseconds
        AllowedLossMilliseconds = $AllowedLossMilliseconds
        Result = 'PASS'
    }
}
finally {
    if (Test-Path -LiteralPath $probeRoot) {
        $resolved = (Resolve-Path -LiteralPath $probeRoot).Path
        $tempRoot = (Resolve-Path -LiteralPath $env:TEMP).Path
        if ($resolved.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolved).StartsWith('AxiomRecovery-', [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
