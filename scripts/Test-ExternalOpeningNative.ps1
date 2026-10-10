[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh external opening evidence directory.' }
$process = Start-Process -FilePath $app -ArgumentList @('--external-opening-probe', ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(60)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250; $process.Refresh() }
if (-not $process.HasExited) { Stop-Process -Id $process.Id; throw 'External opening probe timed out.' }
$errorPath = Join-Path $output 'error.txt'
if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
if ($process.ExitCode -ne 0) { throw "Probe exit code: $($process.ExitCode)" }
$result = Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
foreach ($name in @('RealSecondaryProcessForwarding','RememberedWorkspaceActivated','FilteredFileSelectedAndRevealed','DirtyExistingTabRetainedWithoutDuplicate','CancelRetainsWorkspace','StandaloneKeepsWorkspaceAndDirtyTabs','SavedBytesAndRecoveryRetained')) {
    if ($result.$name -ne $true) { throw "Opening check failed: $name" }
}
$result | ConvertTo-Json -Depth 5
