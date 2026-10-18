[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$StateRoot,
    [Parameter(Mandatory)][string]$IntegrationRoot,
    [string]$RegistryBase = 'HKCU:\Software'
)
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackagePath)
$state = [IO.Path]::GetFullPath($StateRoot)
$integration = [IO.Path]::GetFullPath($IntegrationRoot)
$manifest = Get-Content -LiteralPath (Join-Path $package 'payload-manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.Files) {
    $path = [IO.Path]::GetFullPath((Join-Path $package $file.RelativePath))
    if (!$path.StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe payload path.' }
    if ((Get-Item -LiteralPath $path).Length -ne $file.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Sha256) { throw "Payload verification failed: $($file.RelativePath)" }
}
$exe = Join-Path $package 'Axiom.App.exe'
New-Item -ItemType Directory -Path $integration -Force | Out-Null
$launcher = Join-Path $integration 'Open-AxiomFile.ps1'
$quoteExe = $exe.Replace("'", "''")
$quoteState = $state.Replace("'", "''")
$quotePackage = $package.Replace("'", "''")
$quoteVersion = ([string]$manifest.InformationalVersion).Replace("'", "''")
$quoteDispatch = (Join-Path $integration 'last-open.json').Replace("'", "''")
$body = @"
param([Parameter(Mandatory)][string]`$Document)
`$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath '$quoteExe' -PathType Leaf)) { throw 'The registered Axiom review build is missing. Repair the Axiom Open with registration.' }
`$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo('$quoteExe').ProductVersion
if (`$actualVersion -ne '$quoteVersion') { throw 'The registered Axiom executable identity changed. Repair the Open with registration.' }
`$env:AXIOM_STATE_ROOT = '$quoteState'
`$path = [IO.Path]::GetFullPath(`$Document)
if ([IO.Path]::GetExtension(`$path) -notin @('.md', '.markdown')) { throw 'Choose a Markdown file.' }
`$started = Start-Process -FilePath '$quoteExe' -WorkingDirectory '$quotePackage' -ArgumentList @('"' + `$path.Replace('"', '') + '"') -PassThru
try {
    [ordered]@{ Utc=[DateTime]::UtcNow.ToString('o'); Candidate='$quoteVersion'; ActualVersion=`$actualVersion; Executable='$quoteExe'; StateRoot='$quoteState'; StartedProcessId=`$started.Id } | ConvertTo-Json | Set-Content -LiteralPath '$quoteDispatch' -Encoding utf8
} catch { }
"@
[IO.File]::WriteAllText($launcher, $body)
$nativeLauncher = Join-Path $integration 'Axiom.Open.exe'
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$compiledLauncher = Join-Path $integration ('Axiom.Open-' + [Guid]::NewGuid().ToString('N') + '.exe')
& $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll "/out:$compiledLauncher" (Join-Path $PSScriptRoot 'Axiom.Open.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not build the stable Windows opening application.' }
Move-Item -LiteralPath $compiledLauncher -Destination $nativeLauncher -Force
$command = '"' + $nativeLauncher + '" "%1"'
$programId = 'Axiom.Review.Markdown'
$capabilities = 'Axiom\ReviewOpenWith\Capabilities'
$values = @(
    @{Key='Classes\Applications\Axiom.Open.exe'; Name='FriendlyAppName'; Value='Axiom'},
    @{Key='Classes\Applications\Axiom.Open.exe\shell\open\command'; Name=''; Value=$command},
    @{Key='Classes\Applications\Axiom.Open.exe\SupportedTypes'; Name='.md'; Value=''},
    @{Key='Classes\Applications\Axiom.Open.exe\SupportedTypes'; Name='.markdown'; Value=''},
    @{Key='Classes\Applications\Axiom.App.exe'; Name='FriendlyAppName'; Value='Axiom'},
    @{Key='Classes\Applications\Axiom.App.exe\shell\open\command'; Name=''; Value=$command},
    @{Key='Classes\Applications\Axiom.App.exe\SupportedTypes'; Name='.md'; Value=''},
    @{Key='Classes\Applications\Axiom.App.exe\SupportedTypes'; Name='.markdown'; Value=''},
    @{Key="Classes\$programId"; Name=''; Value='Axiom Markdown file'},
    @{Key="Classes\$programId\DefaultIcon"; Name=''; Value=('"' + $exe + '",0')},
    @{Key="Classes\$programId\shell\open\command"; Name=''; Value=$command},
    @{Key="Classes\.md\OpenWithProgids"; Name=$programId; Value=''},
    @{Key="Classes\.markdown\OpenWithProgids"; Name=$programId; Value=''},
    @{Key=$capabilities; Name='ApplicationName'; Value='Axiom'},
    @{Key=$capabilities; Name='ApplicationDescription'; Value='Open Markdown files in the current Axiom review build.'},
    @{Key=$capabilities; Name='ApplicationIcon'; Value=('"' + $exe + '",0')},
    @{Key=($capabilities+'\FileAssociations'); Name='.md'; Value=$programId},
    @{Key=($capabilities+'\FileAssociations'); Name='.markdown'; Value=$programId},
    @{Key='RegisteredApplications'; Name='Axiom Review'; Value=('Software\'+$capabilities)}
)
$before = foreach ($entry in $values) {
    $key = Join-Path $RegistryBase $entry.Key
    $existing = if (Test-Path -LiteralPath $key) { Get-Item -LiteralPath $key } else { $null }
    $present = $existing -and $existing.GetValueNames() -contains $entry.Name
    [ordered]@{Key=$key; Name=$entry.Name; Present=[bool]$present; Value= $(if ($present) {$existing.GetValue($entry.Name)} else {$null}); Kind=$(if($present){$existing.GetValueKind($entry.Name).ToString()}else{$null})}
}
$backup = Join-Path $integration ('registration-before-' + [Guid]::NewGuid().ToString('N') + '.json')
@($before) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $backup -Encoding utf8
foreach ($entry in $values) {
    $key = Join-Path $RegistryBase $entry.Key
    if (!(Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
    $writable = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key.Substring(6), $true)
    try { $writable.SetValue($entry.Name, [string]$entry.Value, [Microsoft.Win32.RegistryValueKind]::String) } finally { $writable.Dispose() }
}
$rollback = @'
param([Parameter(Mandatory)][string]$BackupPath)
$ErrorActionPreference = 'Stop'
foreach ($entry in (Get-Content -LiteralPath $BackupPath -Raw | ConvertFrom-Json)) {
    if ($entry.Present) {
        if (!(Test-Path -LiteralPath $entry.Key)) { New-Item -Path $entry.Key -Force | Out-Null }
        $writable = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry.Key.Substring(6), $true)
        try { $writable.SetValue([string]$entry.Name, $entry.Value, [Microsoft.Win32.RegistryValueKind]::$($entry.Kind)) } finally { $writable.Dispose() }
    } elseif (Test-Path -LiteralPath $entry.Key) {
        $writable = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry.Key.Substring(6), $true)
        try { $writable.DeleteValue([string]$entry.Name, $false) } finally { $writable.Dispose() }
    }
}
'@
[IO.File]::WriteAllText((Join-Path $integration 'Restore-Registration.ps1'), $rollback)
[ordered]@{Candidate=$manifest.InformationalVersion; Package=$package; StateRoot=$state; Launcher=$launcher; Command=$command; RegistryBase=$RegistryBase; Backup=$backup; UserChoiceModified=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $integration 'registration.json') -Encoding utf8
if ($RegistryBase -eq 'HKCU:\Software') {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class AxiomAssociationRefresh { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint e, uint f, IntPtr a, IntPtr b); }'
    [AxiomAssociationRefresh]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
}
[pscustomobject]@{Candidate=$manifest.InformationalVersion; Backup=$backup; Launcher=$launcher; ValuesRegistered=$values.Count; UserChoiceModified=$false}
