[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$ReleaseVersion,
    [string]$CommitId,
    [string]$OutputDirectory,
    [string]$TestStateName,
    [string]$Channel = 'candidate',
    [string]$ReleaseNotes = 'Implementation candidate; human release acceptance remains pending.',
    [switch]$UpdateReviewFileOpening,
    [string]$LocalCpBackendPath,
    [ValidateRange(1024,65535)][int]$LocalCpPort = 5289,
    [string]$LocalCpCredentialPath = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Axiom\CpSemanticQwenReview\credential.dpapi')
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
& (Join-Path $PSScriptRoot 'Sync-OfflineHelp.ps1') -Check

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
if ($LocalCpBackendPath) {
    $backend = [IO.Path]::GetFullPath($LocalCpBackendPath)
    if (!(Test-Path -LiteralPath $backend) -or !(Test-Path -LiteralPath $LocalCpCredentialPath)) { throw 'Configured CP backend or protected credential is missing.' }
    $setupFolder = Join-Path $outputPath 'CpSetup'
    New-Item -ItemType Directory -Path $setupFolder -Force | Out-Null
    $setup = Join-Path $setupFolder 'Start-local-CP.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-SemanticQwenReview.ps1') -Destination $setup -Force
    [ordered]@{ Endpoint="http://127.0.0.1:$LocalCpPort"; CredentialPath=[IO.Path]::GetFullPath($LocalCpCredentialPath); SetupScript='CpSetup/Start-local-CP.ps1'; SetupSha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash; BackendPath=$backend; BackendSha256=(Get-FileHash -LiteralPath $backend -Algorithm SHA256).Hash; Model='qwen2.5:7b' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'cp-local-profile.json') -Encoding utf8
}
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
    Channel = $Channel
    ReleaseNotes = $ReleaseNotes
    Files = $payloadFiles
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
[pscustomobject]@{
    OutputPath = $outputPath
    Version = $resolvedVersion
    InformationalVersion = $informationalVersion
}


if ($UpdateReviewFileOpening) {
    # Packaged hosts can redirect LocalAppData into their private cache, making
    # the apparent path invisible to Explorer. Prefer the shared stable opener.
    $integration = Join-Path $PSScriptRoot '../tools/WindowsFileOpening'
    if (!(Test-Path -LiteralPath (Join-Path $integration 'registration.json'))) {
        $integration = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Axiom/ReviewOpenWith'
    }
    if (!(Test-Path -LiteralPath (Join-Path $integration 'registration.json'))) { throw 'No existing review-file registration to update. Use an explicit first-time repair instead.' }
    $openingState = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $TestStateName
    $packageCache = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/Local'
    if (Test-Path -LiteralPath (Join-Path $packageCache $TestStateName)) {
        $openingState = Join-Path $packageCache $TestStateName
    }
    & (Join-Path $PSScriptRoot 'Repair-ReviewFileOpening.ps1') -PackagePath $outputPath -StateRoot $openingState -IntegrationRoot $integration
}
