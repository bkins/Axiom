[CmdletBinding()]
param([Parameter(Mandatory)][string]$BackendPath, [Parameter(Mandatory)][string]$ResultPath, [switch]$SkipClipboard)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$backend = [IO.Path]::GetFullPath($BackendPath)
$endpoint = 'http://127.0.0.1:5289'
$installed = @((Invoke-RestMethod 'http://localhost:11434/api/tags' -TimeoutSec 5).models.name)
if ('qwen2.5:7b' -notin $installed) { throw 'Qwen 2.5 7B is not installed. No model was downloaded.' }
$state = Join-Path $env:LOCALAPPDATA 'Axiom\CpSemanticQwenReview'
if (-not (Test-Path -LiteralPath $backend -PathType Leaf)) { throw "Correction backend is missing: $backend" }
New-Item -ItemType Directory -Path $state -Force | Out-Null
$keyPath = Join-Path $state 'credential.dpapi'
if (-not (Test-Path -LiteralPath $keyPath)) {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    $secret = ConvertTo-SecureString ([Convert]::ToBase64String($bytes)) -AsPlainText -Force
    $secret | ConvertFrom-SecureString | Set-Content -LiteralPath $keyPath
}
$secure = (Get-Content -LiteralPath $keyPath -Raw).Trim() | ConvertTo-SecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $credential = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
$hashProvider = [Security.Cryptography.SHA256]::Create()
try { $hash = ([BitConverter]::ToString($hashProvider.ComputeHash([Text.Encoding]::UTF8.GetBytes($credential)))).Replace('-', '') }
finally { $hashProvider.Dispose() }
$server = $null
try {
    $listeners = @(Get-NetTCPConnection -LocalPort 5289 -State Listen -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $owner = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
        if (-not [string]::Equals($owner.Path, $backend, [StringComparison]::OrdinalIgnoreCase)) { throw 'Port 5289 belongs to another process. No service was stopped or replaced.' }
    }
    if ($listeners.Count -eq 0) {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $backend
        $start.WorkingDirectory = [IO.Path]::GetDirectoryName($backend)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $settings = @{
            ASPNETCORE_ENVIRONMENT='Development'; ASPNETCORE_URLS=$endpoint
            DocumentAnalysis__Enabled='true'; DocumentAnalysis__ClientKeySha256=$hash; DocumentAnalysis__TimeoutSeconds='120'
            LlmClient__Provider='Ollama'; LlmClient__Endpoint='http://localhost:11434'; LlmClient__DefaultModel='qwen2.5:7b'; LlmClient__Timeout='120'
            Embedding__OllamaBaseUrl='http://localhost:11434'; Embedding__EmbeddingModel='nomic-embed-text:latest'
        }
        foreach ($setting in $settings.GetEnumerator()) { $start.EnvironmentVariables[$setting.Key] = $setting.Value }
        $server = [Diagnostics.Process]::Start($start)
    }
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($server -and $server.HasExited) { throw 'Correction backend exited during startup.' }
        try {
            $capabilities = Invoke-RestMethod "$endpoint/api/document-analysis/capabilities" -Headers @{'X-Document-Analysis-Key'=$credential} -TimeoutSec 3
        } catch { Start-Sleep -Milliseconds 250 }
        if ($capabilities) {
            if ($capabilities.insightsModel -ne 'qwen2.5:7b') { throw 'The service on port 5289 uses a different model. No existing service was replaced.' }
            if ($capabilities.protocolVersion -eq 1 -and -not $capabilities.simulated) { $ready = $true; break }
        }
    }
    if (-not $ready) { throw 'Correction backend could not authenticate. Existing services were retained.' }
    $copied = $false
    for ($attempt = 0; -not $SkipClipboard -and $attempt -lt 12; $attempt++) {
        try { Set-Clipboard -Value $credential; $copied = $true; break }
        catch { Start-Sleep -Milliseconds 125 }
    }
    [ordered]@{ Endpoint=$endpoint; Backend=$backend; ProcessId=(@(Get-NetTCPConnection -LocalPort 5289 -State Listen)[0].OwningProcess); Model='qwen2.5:7b'; Authenticated=$true; CredentialCopied=$copied; DocumentSubmitted=$false; CheckedAt=(Get-Date -Format o) } | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::GetFullPath($ResultPath))
    Write-Host 'Qwen semantic correction CP is ready at http://127.0.0.1:5289.'
    if ($copied) { Write-Host 'Credential copied. In the correction app, enter that endpoint, paste the credential and Connect.' }
    elseif (-not $SkipClipboard) { Write-Warning 'CP is ready, but Windows denied clipboard copy in this context. Run this launcher from Explorer in your desktop session to copy the credential. No credential was printed.' }
    else { Write-Host 'Clipboard copy was explicitly skipped for this automated startup check.' }
    Write-Host 'No document was submitted. The original CP at port 5277 is unchanged.'
} catch {
    if ($server -and -not $server.HasExited) { $server.Kill() }
    throw
} finally { $credential = $null }
