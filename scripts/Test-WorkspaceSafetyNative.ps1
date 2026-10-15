[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose fresh native evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($probe in @('workspace-safety', 'state-restore')) {
    $folder = Join-Path $output $probe
    $process = Start-Process -FilePath $app -ArgumentList @(('--' + $probe + '-probe'), ('"' + $folder + '"')) -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(55)
    $resultPath = Join-Path $folder 'result.json'
    $errorPath = Join-Path $folder 'error.txt'
    while ([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath $resultPath) -and !(Test-Path -LiteralPath $errorPath)) {
        Start-Sleep -Milliseconds 200
    }
    if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
    if (!(Test-Path -LiteralPath $resultPath)) {
        if (!$process.HasExited) { Stop-Process -Id $process.Id }
        throw "$probe did not produce a result; inspect only its disposable state at $folder."
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    foreach ($property in $result.PSObject.Properties) {
        if ($property.Value -is [bool] -and !$property.Value) { throw "Failed check: $($property.Name)" }
    }
    $result | ConvertTo-Json
}
