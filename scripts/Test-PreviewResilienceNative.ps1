[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$OutputDirectory, [string]$StateSnapshot)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh resilience evidence directory.' }
$arguments = @('--preview-resilience-probe', ('"' + $output + '"'))
if ($StateSnapshot) { $arguments += ('"' + [IO.Path]::GetFullPath($StateSnapshot) + '"') }
$process = Start-Process -FilePath ([IO.Path]::GetFullPath($AppPath)) -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(55000)) { Stop-Process -Id $process.Id; throw 'Preview resilience probe timed out.' }
$errorPath = Join-Path $output 'error.txt'
if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
if ($process.ExitCode -ne 0) { throw "Probe exit code: $($process.ExitCode)" }
$result = Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
foreach ($name in @('LargeImageLoaded','RepeatedLargeNavigationAndEditing','AdapterFailureContainedAndRetryWorks','SavedSourceAndUndoPreserved','OriginalStateUntouched')) {
    if ($result.$name -ne $true) { throw "Native check failed: $name" }
}
if ($StateSnapshot -and -not $result.ClonedUserStateRestored) { throw 'Cloned state restoration failed.' }
if (-not $StateSnapshot -and -not $result.LargeHtmlPdfChecked) { throw 'Large HTML PDF check failed.' }
$result | ConvertTo-Json -Depth 5
