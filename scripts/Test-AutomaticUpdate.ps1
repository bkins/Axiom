[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory, [Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$vpk = Join-Path $repo '.tools/velopack/vpk.exe'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Choose a new fixture evidence directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$feed = Join-Path $evidence 'feed'
$install = Join-Path $evidence 'installed'
$state = Join-Path $evidence 'state'
$receipt = Join-Path $evidence 'receipt.json'
foreach ($version in @('0.0.1','0.0.2')) {
    & $vpk pack --packId AxiomUpdaterProbe --packTitle AxiomUpdaterProbe --packVersion $version --packDir $PayloadDirectory --mainExe Axiom.App.exe --runtime win-x64 --channel win-x64-preview --outputDir $feed --delta none --exclude '^$' --noDefaultExclude --shortcuts None
    if ($LASTEXITCODE -ne 0) { throw 'Fixture package creation failed.' }
    if ($version -eq '0.0.1') { Copy-Item (Join-Path $feed '*Portable.zip') (Join-Path $evidence 'initial.zip') }
}
Expand-Archive -LiteralPath (Join-Path $evidence 'initial.zip') -DestinationPath $install
$executable = @(Get-ChildItem $install -Filter Axiom.App.exe -Recurse)[0].FullName
if (!$executable) { throw 'Fixture executable missing.' }
$previousState = $env:AXIOM_STATE_ROOT
try {
    $env:AXIOM_STATE_ROOT = $state
    $process = Start-Process -FilePath $executable -ArgumentList @('--automatic-update-probe',('"'+$feed+'"'),('"'+$receipt+'"')) -PassThru -WindowStyle Hidden
} finally { $env:AXIOM_STATE_ROOT = $previousState }
$deadline = [DateTime]::UtcNow.AddSeconds(55)
while (!(Test-Path -LiteralPath $receipt) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
if (!(Test-Path -LiteralPath $receipt)) { throw "Update/restart receipt not produced. Inspect $evidence" }
$result = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
if ($result.Version -ne '0.0.2' -or !$result.DraftRetained -or $result.StateRoot -ne $state) { throw 'Native updater fixture failed.' }
Write-Output "PASS: real Velopack 0.0.1 -> 0.0.2 download/apply/restart; isolated state and recovery draft retained. Evidence: $receipt"

$badFeed = Join-Path $evidence 'corrupt-feed'
$badInstall = Join-Path $evidence 'corrupt-install'
$badReceipt = Join-Path $evidence 'corrupt-receipt.txt'
New-Item -ItemType Directory -Path $badFeed | Out-Null
Copy-Item (Join-Path $feed 'releases.win-x64-preview.json') $badFeed
$package = @(Get-ChildItem $feed -Filter '*0.0.2*full.nupkg')[0]
$badPackage = Join-Path $badFeed $package.Name
Copy-Item -LiteralPath $package.FullName -Destination $badPackage
$stream = [IO.File]::Open($badPackage, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
try { $stream.Position=4096; $original=$stream.ReadByte(); $stream.Position=4096; $stream.WriteByte([byte]($original -bxor 255)) } finally { $stream.Dispose() }
Expand-Archive -LiteralPath (Join-Path $evidence 'initial.zip') -DestinationPath $badInstall
$badExecutable = @(Get-ChildItem $badInstall -Filter Axiom.App.exe -Recurse)[0].FullName
$previousState = $env:AXIOM_STATE_ROOT
try {
    $env:AXIOM_STATE_ROOT = Join-Path $evidence 'corrupt-state'
    $badProcess = Start-Process -FilePath $badExecutable -ArgumentList @('--automatic-update-probe',('"'+$badFeed+'"'),('"'+$badReceipt+'"')) -PassThru -WindowStyle Hidden
} finally { $env:AXIOM_STATE_ROOT = $previousState }
if (!$badProcess.WaitForExit(30000)) { throw 'Corrupt-package probe timed out.' }
if ($badProcess.ExitCode -ne 1 -or !(Test-Path $badReceipt) -or (Get-Content $badReceipt -Raw) -notmatch '(?i)checksum|hash|size') { throw 'Corrupt package was not safely rejected.' }
Write-Output "PASS: tampered full package rejected before apply; original fixture retained. Evidence: $badReceipt"
