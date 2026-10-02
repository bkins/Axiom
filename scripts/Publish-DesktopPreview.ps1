[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('osx-arm64', 'osx-x64', 'linux-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$PythonPath = 'python'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh desktop preview output directory.' }
$archivePath = $output + '.zip'
if (Test-Path -LiteralPath $archivePath) { throw 'The preview archive already exists; it was retained.' }
$verificationPath = $output + '-verification.json'
if (Test-Path -LiteralPath $verificationPath) { throw 'The preview verification evidence already exists; it was retained.' }
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$releaseVersion = [string]$properties.Project.PropertyGroup.AxiomVersion
if ([string]::IsNullOrWhiteSpace($releaseVersion)) { throw 'Authoritative AxiomVersion is missing.' }
$sourceCommit = (& git -C $repoRoot rev-parse --short=12 HEAD)
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{12}$') { throw 'Source identity is unavailable.' }
$sourceBranch = (& git -C $repoRoot branch --show-current)
$changed = @(& git -C $repoRoot status --porcelain -- src Directory.Build.props Directory.Build.targets scripts)
if ($LASTEXITCODE -ne 0 -or $changed.Count -gt 0) { throw 'Commit implementation/build inputs before publishing an exact preview.' }
& (Join-Path $PSScriptRoot 'Sync-OfflineHelp.ps1') -Check
$informationalVersion = "$releaseVersion+$sourceCommit"
$isMac = $Runtime.StartsWith('osx-', [StringComparison]::Ordinal)
$payload = if ($isMac) { Join-Path $output 'Axiom.app/Contents/MacOS' } else { Join-Path $output 'Axiom' }
New-Item -ItemType Directory -Path $payload | Out-Null
dotnet publish (Join-Path $repoRoot 'src/Axiom.App/Axiom.App.csproj') -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:Version=$releaseVersion -p:InformationalVersion=$informationalVersion -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Desktop preview publish failed; evidence is retained.' }
if (-not (Test-Path -LiteralPath (Join-Path $payload 'Axiom.App') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $payload 'Axiom.App.dll') -PathType Leaf)) { throw 'Native app host or managed entry point is missing.' }
if ($isMac) {
    $contents = Split-Path -Parent $payload
    $resources = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Path $resources | Out-Null
    & $PythonPath (Join-Path $PSScriptRoot 'Convert-PngToIcns.py') (Join-Path $repoRoot 'src/Axiom.App/Assets/Axiom.png') (Join-Path $resources 'Axiom.icns')
    if ($LASTEXITCODE -ne 0) { throw 'macOS icon conversion failed; preview evidence is retained.' }
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Axiom Preview</string>
<key>CFBundleDisplayName</key><string>Axiom Preview</string>
<key>CFBundleIdentifier</key><string>com.cpuniverse.axiom.preview</string>
<key>CFBundleExecutable</key><string>Axiom.App</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$releaseVersion</string>
<key>CFBundleVersion</key><string>$releaseVersion</string>
<key>CFBundleIconFile</key><string>Axiom.icns</string>
<key>NSHighResolutionCapable</key><true/>
<key>AxiomInformationalVersion</key><string>$informationalVersion</string>
</dict></plist>
"@
    [IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist, [Text.UTF8Encoding]::new($false))
}
$instructions = @"
Axiom $informationalVersion — $Runtime implementation preview
Native launch and feature parity have NOT been verified on this target.
This is not a supported 1.0.20 release or a Windows portable-update package.
Use an isolated AXIOM_STATE_ROOT for evaluation; preserve existing state/documents.
macOS: signing/notarization and native launch are pending. Do not bypass platform trust checks.
Linux: native WebView dependencies and /usr/bin/gio Trash support need verification. PDF uses bundled offline graphics layout; see Help for limits and font/copying warnings.
macOS/Linux: /usr/bin/stat is required for existing-file alias checks. State schema 9 preserves exact Unix spelling and rejects incompatible Windows/Unix path policies.
Extract with an archive tool that preserves Unix executable permissions. Launch the macOS Axiom.app bundle or Linux Axiom/Axiom.App from this preview; preserve the whole payload. Use a fresh absolute AXIOM_STATE_ROOT for each candidate and platform. Do not reuse an older candidate's state after migration.
Windows install/update commands are unavailable on these targets. Preview replacement is manual: close the preview, preserve its documents and state backup, and extract the next verified archive to another folder. Signed installation/update distribution is not implemented or accepted for these previews.
Both targets still require native path/state and alias checks, pickers, fonts, permissions, recovery, offline Help and workflow verification.
Human acceptance remains pending. See the maintained Axiom 1.0.20 plan/checklist.
"@
[IO.File]::WriteAllText((Join-Path $output 'PREVIEW.txt'), $instructions, [Text.UTF8Encoding]::new($false))
$schemaSource = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Axiom.Infrastructure/SqliteStateStore.cs'))
$schemaMatch = [regex]::Match($schemaSource, 'CurrentSchemaVersion\s*=\s*(\d+)')
if (-not $schemaMatch.Success) { throw 'Authoritative state schema is unavailable.' }
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
    if ($_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Linked payloads cannot be packaged.' }
    [ordered]@{ RelativePath = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = [ordered]@{
    FormatVersion = 1
    Kind = 'DesktopPreview'
    Runtime = $Runtime
    Version = $releaseVersion
    InformationalVersion = $informationalVersion
    CommitId = $sourceCommit
    Branch = $sourceBranch
    SchemaVersion = [int]$schemaMatch.Groups[1].Value
    BuiltUtc = [DateTimeOffset]::UtcNow.ToString('O')
    NativeVerified = $false
    SignedAndNotarized = $false
    Files = $files
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'desktop-preview-manifest.json') -Encoding utf8
# Unix mode bits alone are insufficient if a Windows ZIP writer marks the
# creator system as DOS. Emit both Unix creator and permission metadata.
& $PythonPath (Join-Path $PSScriptRoot 'Create-DesktopPreviewArchive.py') $output $archivePath
if ($LASTEXITCODE -ne 0) { throw 'Preview archive creation failed; evidence is retained.' }
& $PythonPath (Join-Path $PSScriptRoot 'Verify-DesktopPreview.py') $output $verificationPath
if ($LASTEXITCODE -ne 0) { throw 'Preview verification failed; package and failure evidence are retained.' }
[pscustomobject]@{ Runtime=$Runtime; InformationalVersion=$informationalVersion; OutputPath=$output; Archive=$archivePath; NativeVerified=$false; Files=$files.Count }
