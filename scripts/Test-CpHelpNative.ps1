[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh CP/Help evidence directory.' }
$process = Start-Process -FilePath ([IO.Path]::GetFullPath($AppPath)) -ArgumentList @('--cp-help-probe', ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$deadline = [datetime]::UtcNow.AddSeconds(180)
while (-not $process.HasExited -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250; $process.Refresh() }
if (-not $process.HasExited) { throw 'CP/Help native probe did not finish; its owned window needs inspection.' }
$errorPath = Join-Path $output 'error.txt'
if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
$result = Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
foreach ($name in @('AllRegisteredCommandsHaveFeatureHelp','CurrentBundledHelpAndSemanticTopic','FeatureReferenceLinksNavigate','CurrentWalkthroughNavigation','ModalCpAndSemanticHelpNavigation','LocalCopyAndStartButtonsWithoutSubmission','ActualClipboardCredentialConnectAndClear','RealReviewedEmbeddingBuildAndSemanticQuery','SourceAndDirtyBufferPreserved','SyntheticSourcesOnly')) {
    if ($result.$name -ne $true) { throw "CP/Help check failed: $name" }
}
$result | ConvertTo-Json
