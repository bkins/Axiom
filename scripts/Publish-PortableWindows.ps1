[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot ("artifacts\Axiom-" + $Runtime)

dotnet publish (Join-Path $repoRoot 'src\Axiom.App\Axiom.App.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $outputPath

if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
Write-Output $outputPath

