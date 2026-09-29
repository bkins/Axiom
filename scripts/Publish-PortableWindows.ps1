[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$ReleaseVersion,
    [string]$CommitId,
    [string]$OutputDirectory,
    [string]$TestStateName
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repoRoot ("artifacts\Axiom-" + $Runtime) } else { [IO.Path]::GetFullPath($OutputDirectory) }
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$authoritativeVersion = [string]$props.Project.PropertyGroup.AxiomVersion
if ([string]::IsNullOrWhiteSpace($authoritativeVersion)) { throw 'Directory.Build.props does not define AxiomVersion.' }
if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion) -and $ReleaseVersion -ne $authoritativeVersion) {
    throw "Requested version $ReleaseVersion contradicts authoritative AxiomVersion $authoritativeVersion."
}
$resolvedVersion = $authoritativeVersion
if ([string]::IsNullOrWhiteSpace($TestStateName)) { $TestStateName = "Axiom-Test-$resolvedVersion" }
if ($TestStateName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{0,80}$' -or $TestStateName -match '\.\.') { throw 'TestStateName must be a single safe folder name.' }

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
$testLauncher = "@echo off`r`nset `"AXIOM_STATE_ROOT=%LOCALAPPDATA%\$TestStateName`"`r`nstart `"Axiom $resolvedVersion test`" `"%~dp0Axiom.App.exe`"`r`n"
Set-Content -LiteralPath (Join-Path $outputPath 'Test-Axiom.cmd') -Value $testLauncher -Encoding ascii
$schemaSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Axiom.Infrastructure\SqliteStateStore.cs') -Raw
$schemaMatch = [regex]::Match($schemaSource, 'CurrentSchemaVersion\s*=\s*(\d+)')
if (-not $schemaMatch.Success) { throw 'Could not read authoritative state schema version.' }
$manifestPath = Join-Path $outputPath 'payload-manifest.json'
if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
$payloadFiles = @(Get-ChildItem -LiteralPath $outputPath -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ RelativePath = [IO.Path]::GetRelativePath($outputPath, $_.FullName).Replace('\','/'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = [ordered]@{
    FormatVersion = 1
    Version = $resolvedVersion
    InformationalVersion = $informationalVersion
    CommitId = $CommitId.ToLowerInvariant()
    Branch = (& git -C $repoRoot branch --show-current)
    BuiltUtc = [DateTimeOffset]::UtcNow.ToString('O')
    SchemaVersion = [int]$schemaMatch.Groups[1].Value
    Files = $payloadFiles
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
[pscustomobject]@{
    OutputPath = $outputPath
    Version = $resolvedVersion
    InformationalVersion = $informationalVersion
}

