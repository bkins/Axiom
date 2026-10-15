[CmdletBinding()]
param([Parameter(Mandatory)][string]$BackendPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose fresh setup-test evidence.' }
if (@(Get-NetTCPConnection -LocalPort 5291 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Test port 5291 is occupied; no service was changed.' }
New-Item -ItemType Directory -Path $output | Out-Null
$fixture = Join-Path $output 'credential-fixture'
New-Item -ItemType Directory -Path $fixture | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$owned = $null
$listener = $null
function Create-TestKey([string]$Path) {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    ConvertTo-SecureString ([Convert]::ToBase64String($bytes)) -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -LiteralPath $Path
}
function Invoke-TestHelper([string]$KeyPath, [string]$Name, [int]$Port, [bool]$HostileEnvironment) {
    $result = Join-Path $output ($Name + '.json')
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    $start.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $PSScriptRoot 'Start-SemanticQwenReview.ps1') + '" -BackendPath "' + $BackendPath + '" -ResultPath "' + $result + '" -CredentialPath "' + $KeyPath + '" -Port ' + $Port + ' -SkipClipboard'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if ($HostileEnvironment) {
        $start.EnvironmentVariables['LOCALAPPDATA'] = Join-Path $fixture 'different-context'
        $start.EnvironmentVariables['HTTP_PROXY'] = 'http://127.0.0.1:1'
        $start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:1'
        $start.EnvironmentVariables['ALL_PROXY'] = 'http://127.0.0.1:1'
    }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(80000)) { $process.Kill(); throw 'Owned setup-test helper timed out.' }
    # Started backends inherit pipe handles. Never wait for their EOF here.
    $code = $process.ExitCode
    $process.Dispose()
    $record = $null
    if (Test-Path -LiteralPath $result) { $record = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json }
    return [pscustomobject]@{ExitCode=$code;Record=$record}
}
function Check([string]$Name, [bool]$Passed) {
    if (!$Passed) { throw ('Setup check failed: ' + $Name) }
    $checks.Add([pscustomobject]@{Check=$Name;Passed=$true})
    Write-Output ('PASS: ' + $Name)
}
try {
    $firstKey = Join-Path $fixture 'first.dpapi'
    $wrongKey = Join-Path $fixture 'wrong.dpapi'
    Create-TestKey $firstKey
    Create-TestKey $wrongKey
    $fresh = Invoke-TestHelper $firstKey 'fresh' 5291 $true
    if ($fresh.Record -and $fresh.Record.ProcessId) { $owned = Get-Process -Id $fresh.Record.ProcessId }
    Check 'Fresh exact backend authenticates despite different LOCALAPPDATA and proxy variables' ($fresh.ExitCode -eq 0 -and $fresh.Record.Authenticated -and $fresh.Record.ProxyDisabled -and $fresh.Record.CredentialPath -eq $firstKey)
    $repeat = Invoke-TestHelper $firstKey 'repeat' 5291 $true
    Check 'Repeated setup tolerates different LOCALAPPDATA and proxy variables while reusing the same process' ($repeat.ExitCode -eq 0 -and $repeat.Record.Authenticated -and $repeat.Record.ProcessId -eq $owned.Id)
    $wrong = Invoke-TestHelper $wrongKey 'wrong-key' 5291 $false
    Check 'Wrong credential reports HTTP 401 without replacing the running process' ($wrong.ExitCode -ne 0 -and !$wrong.Record.Authenticated -and $wrong.Record.HttpStatus -eq 401 -and $wrong.Record.FailureCode -eq 'credential_rejected' -and !(Get-Process -Id $owned.Id).HasExited)
    $missing = Join-Path $fixture 'missing.dpapi'
    $absent = Invoke-TestHelper $missing 'missing-key' 5291 $false
    Check 'Missing explicitly pinned credential is not silently regenerated' ($absent.ExitCode -ne 0 -and !(Test-Path -LiteralPath $missing))
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,5292)
    $listener.Start()
    $unrelated = Invoke-TestHelper $firstKey 'unrelated-owner' 5292 $false
    Check 'Unrelated listener is refused and retained' ($unrelated.ExitCode -ne 0 -and $unrelated.Record.FailureCode -eq 'unrelated_port_owner' -and @(Get-NetTCPConnection -LocalPort 5292 -State Listen).Count -gt 0)
    $wrongRecordText = Get-Content -LiteralPath (Join-Path $output 'wrong-key.json') -Raw
    Check 'Diagnostics contain neither plaintext credential nor credential verifier' ($wrongRecordText -notmatch 'ClientKeySha256|X-Document-Analysis-Key|"credential"\s*:')
    [ordered]@{Passed=$checks.Count;Checks=$checks;ExactBackend=$BackendPath;ProductionChanged=$false;DocumentsSubmitted=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'result.json')
} finally {
    if ($listener) { $listener.Stop() }
    if ($owned) {
        $owned.Refresh()
        if (!$owned.HasExited -and [string]::Equals($owned.Path,[IO.Path]::GetFullPath($BackendPath),[StringComparison]::OrdinalIgnoreCase)) { Stop-Process -Id $owned.Id }
    }
}
