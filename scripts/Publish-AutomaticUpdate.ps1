[CmdletBinding()]
param(
    [ValidateSet('stable','preview')][string]$Channel = 'preview',
    [Parameter(Mandatory)][string]$ReleaseNotesPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$Publish,
    [switch]$CurrentReview
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new empty output directory; retained releases must not be overwritten.' }
if (!(Test-Path -LiteralPath $ReleaseNotesPath -PathType Leaf)) { throw 'Release notes are required.' }
$notes = [IO.Path]::GetFullPath($ReleaseNotesPath)
$dirtySource = & git -C $repo status --porcelain -- src scripts Directory.Build.props
if ($dirtySource) { throw 'Commit application and packaging sources before publishing so the package commit identifies its exact source.' }
$vpk = Join-Path $repo '.tools/velopack/vpk.exe'
if (!(Test-Path -LiteralPath $vpk)) {
    dotnet tool install vpk --version 1.2.161 --tool-path (Split-Path $vpk -Parent)
    if ($LASTEXITCODE -ne 0) { throw 'Velopack installation failed.' }
}
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.AxiomVersion
$commit = (& git -C $repo rev-parse --short=12 HEAD).Trim()
$payload = Join-Path $output 'payload'
$releases = Join-Path $output 'releases'
& (Join-Path $PSScriptRoot 'Publish-PortableWindows.ps1') -OutputDirectory $payload -TestStateName "Axiom-UpdateReview-$version" -Channel $Channel -ReleaseNotes ([IO.File]::ReadAllText($notes)) -UpdateReviewFileOpening:$CurrentReview
if ($LASTEXITCODE -ne 0) { throw 'Payload publish failed.' }
# Keep app/state paths separate: Velopack owns AxiomDesktop, while state is in Axiom.
# Keep symbols too: the payload manifest hashes every file and must survive packaging.
& $vpk pack --packId AxiomDesktop --packTitle Axiom --packAuthors bkins --packVersion $version --packDir $payload --mainExe Axiom.App.exe --runtime win-x64 --channel "win-x64-$Channel" --outputDir $releases --releaseNotes $notes --icon (Join-Path $repo 'src/Axiom.App/Assets/Axiom.ico') --delta none --exclude '^$' --shortcuts StartMenuRoot
if ($LASTEXITCODE -ne 0) { throw 'Update packaging failed.' }
# Publish only the explicitly selected runtime assets, never checkout files or credentials.
$assets = @(Get-ChildItem -LiteralPath $releases -File | Where-Object { $_.Name -match '(Setup\.exe$|Portable\.zip$|\.nupkg$|^releases\..*\.json$)' })
if ($assets.Count -ne 4) { throw "Expected installer, portable ZIP, full package and channel feed; found $($assets.Count)." }
$assets | ForEach-Object { "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash )  $($_.Name)" } | Set-Content (Join-Path $releases 'SHA256SUMS.txt')
[ordered]@{ Version=$version; InformationalVersion="$version+$commit"; Channel=$Channel; Repository='bkins/Axiom-Releases'; Files=@($assets.Name) } | ConvertTo-Json | Set-Content (Join-Path $output 'release-record.json')
if ($Publish) {
    $arguments = @('release','create',"v$version-$Channel",'--repo','bkins/Axiom-Releases','--title',"Axiom $version ($Channel)",'--notes-file',$notes)
    if ($Channel -eq 'preview') { $arguments += @('--prerelease','--latest=false') }
    $arguments += @($assets.FullName) + @(Join-Path $releases 'SHA256SUMS.txt')
    & gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'GitHub release creation failed. Local packages are retained.' }
}
Write-Output "Prepared $version+$commit ($Channel) in $output"
