[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh template/asset evidence directory.' }
$process = Start-Process -FilePath $app -ArgumentList @('--template-asset-probe', ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(90)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
}
if (-not $process.HasExited) { Stop-Process -Id $process.Id; throw 'Template/asset native probe timed out.' }
if ($process.ExitCode -ne 0) { throw "Probe exit code: $($process.ExitCode)" }
$errorPath = Join-Path $output 'error.txt'
if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
$resultPath = Join-Path $output 'result.json'
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Native probe did not produce result.json.' }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
foreach ($name in @('NativePreviewAndCreateButton','ReviewedCreationAndTemplateSourcePreserved','DropRoutingDirtyTabSelectionUndoPreserved','EscapedRelativeImageInsertionAndUndo','OriginalAndCopiedImageBytesPreserved','RecoveryPreserved')) {
    if ($result.$name -ne $true) { throw "Native check failed: $name" }
}
$result | ConvertTo-Json -Depth 5
