[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh workspace Git evidence directory.' }
$process = Start-Process -FilePath $app -ArgumentList @('--workspace-git-probe', ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(55)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
}
if (-not $process.HasExited) { Stop-Process -Id $process.Id; throw 'Workspace Git native probe timed out.' }
if ($process.ExitCode -ne 0) { throw "Probe exit code: $($process.ExitCode)" }
$errorPath = Join-Path $output 'error.txt'
if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
$resultPath = Join-Path $output 'result.json'
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Native probe did not produce result.json.' }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
foreach ($name in @('NativeStatusAndSavedStagedDiffs','EmbeddedChangesDiffAndCommitMessage','EditingAndDirtyAvailabilityWhilePanelOpen','WorkspaceSwitchClearsStalePanel','DirtyBufferStageBlocked','ActualStageUnstageAndCommitDialogs','CommitCancellationPreservedHead','SelectedOnlyBranchCommit','UnrelatedIndexAndWorkingBytesPreserved','UnsupportedRowsNotSelectable','StageCarriesCommitSelection')) {
    if ($result.$name -ne $true) { throw "Native check failed: $name" }
}
$result | ConvertTo-Json -Depth 5
