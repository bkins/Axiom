[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$OutputDirectory, [switch]$RegisteredOpening)
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose fresh evidence output.' }
$mode = if ($RegisteredOpening) {'--registered-review-opening-probe'} else {'--next-ui-stories-probe'}
$process = Start-Process -FilePath $app -ArgumentList @($mode, ('"'+$output+'"')) -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(80)
while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250; $process.Refresh() }
if (!$process.HasExited) { Stop-Process -Id $process.Id; throw 'The owned UI-story probe timed out.' }
if (Test-Path -LiteralPath (Join-Path $output 'error.txt')) { throw ([IO.File]::ReadAllText((Join-Path $output 'error.txt'))) }
if ($process.ExitCode -ne 0) { throw "Owned UI-story probe exited with $($process.ExitCode)." }
Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw
