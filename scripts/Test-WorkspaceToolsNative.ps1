[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose fresh workspace-tools evidence directory.' }
$process = Start-Process -FilePath ([IO.Path]::GetFullPath($AppPath)) -ArgumentList @('--workspace-tools-probe', ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(55)
while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200; $process.Refresh() }
if (!$process.HasExited) { Stop-Process -Id $process.Id; throw 'Workspace tools probe timed out.' }
if (Test-Path -LiteralPath (Join-Path $output 'error.txt')) { throw ([IO.File]::ReadAllText((Join-Path $output 'error.txt'))) }
$result = Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
foreach ($property in $result.PSObject.Properties) { if ($property.Value -is [bool] -and !$property.Value) { throw "Failed: $($property.Name)" } }
$result | ConvertTo-Json
