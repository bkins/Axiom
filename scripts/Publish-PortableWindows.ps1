[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$ReleaseVersion,
    [string]$CommitId
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot ("artifacts\Axiom-" + $Runtime)
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$authoritativeVersion = [string]$props.Project.PropertyGroup.AxiomVersion
if ([string]::IsNullOrWhiteSpace($authoritativeVersion)) { throw 'Directory.Build.props does not define AxiomVersion.' }
if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion) -and $ReleaseVersion -ne $authoritativeVersion) {
    throw "Requested version $ReleaseVersion contradicts authoritative AxiomVersion $authoritativeVersion."
}
$resolvedVersion = $authoritativeVersion

if ([string]::IsNullOrWhiteSpace($CommitId)) {
    $CommitId = (& git -C $repoRoot rev-parse --short=12 HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($CommitId)) { $CommitId = 'local' }
}
if ($CommitId -ne 'local' -and $CommitId -notmatch '^[0-9a-fA-F]{7,40}$') {
    throw 'CommitId must be a 7-40 character hexadecimal Git commit ID or local.'
}
$informationalVersion = "$resolvedVersion+$($CommitId.ToLowerInvariant())"

dotnet publish (Join-Path $repoRoot 'src\Axiom.App\Axiom.App.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$resolvedVersion `
    -p:InformationalVersion=$informationalVersion `
    -o $outputPath

if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
[pscustomobject]@{
    OutputPath = $outputPath
    Version = $resolvedVersion
    InformationalVersion = $informationalVersion
}

