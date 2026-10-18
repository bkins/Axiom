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
$latestChoice = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoiceLatest\ProgId'
$latestBefore = if (Test-Path $latestChoice) { (Get-ItemProperty $latestChoice | Select-Object ProgId,Hash | ConvertTo-Json -Compress) } else { 'absent' }
try {
    $result = & (Join-Path $PSScriptRoot 'Repair-ReviewFileOpening.ps1') -PackagePath $PackagePath -StateRoot (Join-Path $output 'state with spaces') -IntegrationRoot (Join-Path $output 'integration with spaces') -RegistryBase $base
    $command = (Get-Item -LiteralPath $key).GetValue('')
    if (!$command.EndsWith('Axiom.Open.exe" "%1"')) { throw 'Document command did not use the stable native launcher and literal argument.' }
    if (!(Test-Path (Join-Path (Split-Path $result.Launcher) 'Axiom.Open.exe'))) { throw 'Native association launcher missing.' }
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
    # Exercise the actual native bridge using only a harmless fixture wrapper.
    $sample = Join-Path $output 'Literal [brackets] & spaces.md'
    $received = Join-Path $output 'received.txt'
    Set-Content -LiteralPath $sample -Value '# disposable launcher fixture' -Encoding utf8
    $capture = 'param([string]$Document)' + "`r`n" + '[IO.File]::WriteAllText(''' + $received.Replace("'", "''") + ''', $Document)'
    try {
        [IO.File]::WriteAllText($result.Launcher, $capture)
        $bridge = Start-Process -FilePath (Join-Path (Split-Path $result.Launcher) 'Axiom.Open.exe') -ArgumentList ('"' + $sample + '"') -PassThru
        if (!$bridge.WaitForExit(15000)) { throw 'Native bridge fixture timed out.' }
        if ($bridge.ExitCode -ne 0 -or [IO.File]::ReadAllText($received) -ne $sample) { throw 'Native bridge changed the document argument.' }
    } finally { [IO.File]::WriteAllText($result.Launcher, $source) }
    & (Join-Path (Split-Path $result.Launcher) 'Restore-Registration.ps1') -BackupPath $result.Backup
    if ((Get-Item -LiteralPath $key).GetValue('') -ne '"C:\missing old build\Axiom.App.exe" "%1"') { throw 'Registry rollback failed.' }
    $after = if (Test-Path -LiteralPath $choice) { (Get-ItemProperty -LiteralPath $choice | Select-Object ProgId,Hash | ConvertTo-Json -Compress) } else { 'absent' }
    if ($before -ne $after) { throw 'UserChoice changed.' }
    $latestAfter = if (Test-Path $latestChoice) { (Get-ItemProperty $latestChoice | Select-Object ProgId,Hash | ConvertTo-Json -Compress) } else { 'absent' }
    if ($latestBefore -ne $latestAfter) { throw 'UserChoiceLatest changed.' }
    [ordered]@{Candidate=$result.Candidate; PayloadVerified=$true; LiteralDocumentArgument=$true; ExistingValuesRetained=$true; BothMarkdownTypes=$true; GeneratedLauncherParses=$true; ReviewStateRetained=$true; RegistryRollback=$true; UserChoiceUnchanged=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw
} finally {
    # Fixed test prefix plus GUID only; never remove a production registry tree.
    if ($base -match '^HKCU:\\Software\\Axiom-OpenWith-Repair-Test-[0-9a-f]{32}$') { Remove-Item -LiteralPath $base -Recurse -Force }
}
