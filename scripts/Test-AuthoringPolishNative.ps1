[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh authoring polish evidence directory.' }
$process = Start-Process -FilePath $app -ArgumentList @('--authoring-polish-probe', ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(55)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250; $process.Refresh() }
if (-not $process.HasExited) { Stop-Process -Id $process.Id; throw 'Authoring polish native probe timed out.' }
if ($process.ExitCode -ne 0) { throw "Probe exit code: $($process.ExitCode)" }
$errorPath = Join-Path $output 'error.txt'
if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
$result = Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
foreach ($name in @('NativeFieldInfoLabelsAndClicks','NativeGroupedContextMenu','NativeTabShiftTabOrderedNestingAndUndo','ActualWebViewCSharpAliasesAndColours','EditorCSharpTokensLightAndDark','ActualWebViewCalloutIconsWithoutMarkers','SavedDirtyIdentityRecoveryUndoPreserved')) {
    if ($result.$name -ne $true) { throw "Native check failed: $name" }
}
$result | ConvertTo-Json -Depth 5
