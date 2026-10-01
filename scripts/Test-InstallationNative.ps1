[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$package = Split-Path -Parent $app
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose fresh installation evidence.' }
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($kind in @('integration', 'window')) {
    $evidence = Join-Path $output $kind
    $arguments = if ($kind -eq 'integration') {
        @('--windows-installation-probe', ('"' + $package + '"'), ('"' + $evidence + '"'))
    } else { @('--installation-window-probe', ('"' + $evidence + '"')) }
    $process = Start-Process -FilePath $app -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(55000)) { Stop-Process -Id $process.Id; throw "Installation $kind probe timed out." }
    $errorPath = Join-Path $evidence 'error.txt'
    if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
    if ($process.ExitCode -ne 0) { throw "Installation $kind probe exit: $($process.ExitCode)" }
    $result = Get-Content -LiteralPath (Join-Path $evidence 'result.json') -Raw | ConvertFrom-Json
    $fields = if ($kind -eq 'integration') {
        @('ActualPerUserRegistration','ActualStartMenuShortcut','ExplicitOpenWithRegistration','ModifiedPayloadUninstallBlocked','VerifiedPayloadAndIntegrationRemoved','DraftStateAndBackupRetained','TrustedOfferAndArchivePipelineWithPackagedFixture')
    } else { @('ActualInstallerConfirmationCancelled','UpdateNetworkOptInCancelled','DirtyBufferPreserved','AboutPackageChannelAndNotes') }
    foreach ($name in $fields) { if ($result.$name -ne $true) { throw "Native check failed: $name" } }
    $result | ConvertTo-Json -Depth 5
}
