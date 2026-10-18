[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh test evidence folder.' }
New-Item -ItemType Directory -Path $output | Out-Null
$base = 'HKCU:\Software\Axiom-OpenWith-Repair-Test-' + [Guid]::NewGuid().ToString('N')
$key = $base + '\Classes\Applications\Axiom.App.exe\shell\open\command'
New-Item -Path $key -Force | Out-Null
New-ItemProperty -LiteralPath $key -Name '(default)' -Value '"C:\missing old build\Axiom.App.exe" "%1"' -Force | Out-Null
New-ItemProperty -LiteralPath $key -Name 'KeepOtherValue' -Value 'retain' -Force | Out-Null
$choice = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice'
$before = if (Test-Path -LiteralPath $choice) { (Get-ItemProperty -LiteralPath $choice | Select-Object ProgId,Hash | ConvertTo-Json -Compress) } else { 'absent' }
try {
    $result = & (Join-Path $PSScriptRoot 'Repair-ReviewFileOpening.ps1') -PackagePath $PackagePath -StateRoot (Join-Path $output 'state with spaces') -IntegrationRoot (Join-Path $output 'integration with spaces') -RegistryBase $base
    $command = (Get-Item -LiteralPath $key).GetValue('')
    if (!$command.Contains('-Document "%1"') -or !$command.Contains('-WindowStyle Hidden')) { throw 'Document command did not preserve the literal argument or hide its helper console.' }
    if ((Get-Item -LiteralPath $key).GetValue('KeepOtherValue') -ne 'retain') { throw 'Unrelated registry value changed.' }
    foreach ($extension in @('.md','.markdown')) {
        $types = Get-Item -LiteralPath ($base + '\Classes\Applications\Axiom.App.exe\SupportedTypes')
        if ($types.GetValueNames() -notcontains $extension) { throw 'Missing supported extension.' }
    }
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($result.Launcher,[ref]$null,[ref]$errors) | Out-Null
    if ($errors.Count) { throw 'Generated launcher syntax failed.' }
    $source = [IO.File]::ReadAllText($result.Launcher)
    if (!$source.Contains('AXIOM_STATE_ROOT') -or !$source.Contains('state with spaces')) { throw 'Review state was not retained.' }
    & (Join-Path (Split-Path $result.Launcher) 'Restore-Registration.ps1') -BackupPath $result.Backup
    if ((Get-Item -LiteralPath $key).GetValue('') -ne '"C:\missing old build\Axiom.App.exe" "%1"') { throw 'Registry rollback failed.' }
    $after = if (Test-Path -LiteralPath $choice) { (Get-ItemProperty -LiteralPath $choice | Select-Object ProgId,Hash | ConvertTo-Json -Compress) } else { 'absent' }
    if ($before -ne $after) { throw 'UserChoice changed.' }
    [ordered]@{Candidate=$result.Candidate; PayloadVerified=$true; LiteralDocumentArgument=$true; ExistingValuesRetained=$true; BothMarkdownTypes=$true; GeneratedLauncherParses=$true; ReviewStateRetained=$true; RegistryRollback=$true; UserChoiceUnchanged=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw
} finally {
    # Fixed test prefix plus GUID only; never remove a production registry tree.
    if ($base -match '^HKCU:\\Software\\Axiom-OpenWith-Repair-Test-[0-9a-f]{32}$') { Remove-Item -LiteralPath $base -Recurse -Force }
}
