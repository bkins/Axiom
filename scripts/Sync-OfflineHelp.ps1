[CmdletBinding()]
param([string]$SourcePath)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SourcePath) { $SourcePath = Join-Path (Split-Path (Split-Path $repoRoot -Parent) -Parent) 'Application Documentation/Axiom/07 - User Documentation/Axiom v1 User Guide.md' }
$source = [IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'The maintained Axiom User Guide was not found. Supply -SourcePath.' }
$helpFolder = Join-Path $repoRoot 'src/Axiom.App/Help'
New-Item -ItemType Directory -Path $helpFolder -Force | Out-Null
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$content = [IO.File]::ReadAllText($source)
[IO.File]::WriteAllText((Join-Path $helpFolder 'UserGuide.md'), "<!-- Generated from the maintained Axiom v1 User Guide. Source SHA-256: $hash. Edit the maintained guide and run scripts/Sync-OfflineHelp.ps1. -->`n`n$content")
[ordered]@{ Source = 'Axiom/07 - User Documentation/Axiom v1 User Guide.md'; Sha256 = $hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $helpFolder 'source.json') -Encoding utf8
"Offline Help generated from $source ($hash)"
