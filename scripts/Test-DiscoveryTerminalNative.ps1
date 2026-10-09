[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($AppPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh discovery/terminal evidence directory.' }
$process = Start-Process -FilePath $app -ArgumentList @('--discovery-terminal-probe', ('"' + $output + '"')) -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(90)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250; $process.Refresh() }
if (-not $process.HasExited) { Stop-Process -Id $process.Id; throw 'Discovery/terminal native probe timed out.' }
$errorPath = Join-Path $output 'error.txt'
if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
if ($process.ExitCode -ne 0) { throw "Probe exit code: $($process.ExitCode)" }
$result = Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
foreach ($name in @('RetainedDiscoveryAndDirtyBuffer','PersistentRightAndBottomLayouts','SideToolbarAndZenRestore','RealConPtyPowerShellAndRenderedOutput','RecipeDoesNotExecuteBeforePhysicalEnter','PendingCommandNotOverwritten','LiteralRootWithSpacesAndQuotes','ShellDisposed','RunningCommandBlockedAndCtrlCWorks','ChangedDirectoryDisablesRecipes','ReopeningGitRetainsShell','TerminalResize')) {
    if ($result.$name -ne $true) { throw "Native check failed: $name" }
}
$result | ConvertTo-Json -Depth 5
if ($result.PhysicalManualDirectoryReturnAndShellExitRestart -ne $true -or $result.CopiedDocumentationTasks -ne $true) { throw 'Shell restart or copied-documentation task check failed.' }
if ($result.ChangesTabRetainsShellAndPendingCommand -ne $true -or $result.InputLimitRequiresExplicitCancellation -ne $true) { throw 'Terminal continuity or input-limit guard check failed.' }
