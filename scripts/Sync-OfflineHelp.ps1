[CmdletBinding()]
param([string]$SourcePath, [switch]$Check)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SourcePath) { $SourcePath = Join-Path (Split-Path (Split-Path $repoRoot -Parent) -Parent) 'Application Documentation/Axiom/07 - User Documentation/Axiom v1 User Guide.md' }
$source = [IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'The maintained Axiom User Guide was not found. Supply -SourcePath.' }
$helpFolder = Join-Path $repoRoot 'src/Axiom.App/Help'
New-Item -ItemType Directory -Path $helpFolder -Force | Out-Null
$algorithm = [Security.Cryptography.SHA256]::Create()
$sourceStream = [IO.File]::OpenRead($source)
try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($sourceStream)).Replace('-', '') }
finally { $sourceStream.Dispose(); $algorithm.Dispose() }
$content = [IO.File]::ReadAllText($source)
if ($Check) {
    $expected = "<!-- Generated from the maintained Axiom v1 User Guide. Source SHA-256: $hash. Edit the maintained guide and run scripts/Sync-OfflineHelp.ps1. -->`n`n$content"
    $target = Join-Path $helpFolder 'UserGuide.md'
    if (-not (Test-Path -LiteralPath $target) -or [IO.File]::ReadAllText($target) -cne $expected) { throw 'Bundled Help is stale. Update the maintained User Guide and run scripts/Sync-OfflineHelp.ps1.' }
    "Bundled Help matches maintained User Guide ($hash)"
    return
}
[IO.File]::WriteAllText((Join-Path $helpFolder 'UserGuide.md'), "<!-- Generated from the maintained Axiom v1 User Guide. Source SHA-256: $hash. Edit the maintained guide and run scripts/Sync-OfflineHelp.ps1. -->`n`n$content")
[ordered]@{ Source = 'Axiom/07 - User Documentation/Axiom v1 User Guide.md'; Sha256 = $hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $helpFolder 'source.json') -Encoding utf8
"Offline Help generated from $source ($hash)"
