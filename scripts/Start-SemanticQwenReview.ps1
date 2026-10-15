[CmdletBinding()]
param([Parameter(Mandatory)][string]$BackendPath, [Parameter(Mandatory)][string]$ResultPath,
      [string]$CredentialPath, [ValidateRange(1024,65535)][int]$Port = 5289, [switch]$SkipClipboard)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$backend = [IO.Path]::GetFullPath($BackendPath)
$endpoint = 'http://127.0.0.1:' + $Port
Add-Type -AssemblyName System.Net.Http
$handler = New-Object Net.Http.HttpClientHandler
$handler.UseProxy = $false
$http = New-Object Net.Http.HttpClient($handler)
$http.Timeout = [TimeSpan]::FromSeconds(3)
function Read-LocalJson([string]$Uri, [string]$Key) {
    $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Get, $Uri)
    if ($Key) { $request.Headers.Add('X-Document-Analysis-Key', $Key) }
    $response = $null
    try {
        $response = $http.SendAsync($request).GetAwaiter().GetResult()
        $value = $null
        if ($response.IsSuccessStatusCode) { $value = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json }
        return [pscustomobject]@{ Status=[int]$response.StatusCode; Value=$value }
    } finally { if ($response) { $response.Dispose() }; $request.Dispose() }
}
$installed = @((Read-LocalJson 'http://127.0.0.1:11434/api/tags' '').Value.models.name)
if ('qwen2.5:7b' -notin $installed) { throw 'Qwen 2.5 7B is not installed. No model was downloaded.' }
Write-Verbose 'Local model catalog validated.'
$state = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Axiom\CpSemanticQwenReview'
if (-not (Test-Path -LiteralPath $backend -PathType Leaf)) { throw "Correction backend is missing: $backend" }
New-Item -ItemType Directory -Path $state -Force | Out-Null
$explicitCredential = ![string]::IsNullOrWhiteSpace($CredentialPath)
$keyPath = if ($explicitCredential) { [IO.Path]::GetFullPath($CredentialPath) } else { Join-Path $state 'credential.dpapi' }
if (-not (Test-Path -LiteralPath $keyPath)) {
    if ($explicitCredential) { throw "The configured review credential is missing: $keyPath. No replacement credential was generated." }
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
Write-Verbose 'Protected review credential loaded.'
$server = $null
$lastStatus = $null
$failureCode = 'setup_failed'
try {
    $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $owner = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
        if ($listener.LocalAddress -ne '127.0.0.1' -or -not [string]::Equals($owner.Path, $backend, [StringComparison]::OrdinalIgnoreCase)) { $failureCode='unrelated_port_owner'; throw "Port $Port belongs to another process or address. No service was stopped or replaced." }
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
        $localBypass = 'localhost,127.0.0.1'
        if ($start.EnvironmentVariables['NO_PROXY']) { $localBypass = $start.EnvironmentVariables['NO_PROXY'] + ',' + $localBypass }
        $start.EnvironmentVariables['NO_PROXY'] = $localBypass
        $start.EnvironmentVariables['no_proxy'] = $localBypass
        $server = [Diagnostics.Process]::Start($start)
        Write-Verbose 'Owned review backend started.'
    }
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($server -and $server.HasExited) { throw 'Correction backend exited during startup.' }
        $capabilities = $null
        try {
            $response = Read-LocalJson "$endpoint/api/document-analysis/capabilities" $credential
            $lastStatus = $response.Status
            Write-Verbose "Capabilities HTTP status: $lastStatus."
            $capabilities = $response.Value
        } catch { $failureCode='connection_failed'; Start-Sleep -Milliseconds 250 }
        if ($lastStatus -in @(401,403)) { $failureCode='credential_rejected'; throw "Review CP is running but rejected the configured credential (HTTP $lastStatus). Credential file: $keyPath. Existing services were retained. Diagnostic result: $ResultPath" }
        if (!$capabilities -and $lastStatus) { $failureCode='http_response'; Start-Sleep -Milliseconds 250 }
        if ($capabilities) {
            if ($capabilities.insightsModel -ne 'qwen2.5:7b') { $failureCode='wrong_model'; throw "The service on port $Port uses a different model. No existing service was replaced." }
            if ($capabilities.protocolVersion -eq 1 -and -not $capabilities.simulated) { $ready = $true; break }
        }
    }
    if (-not $ready) { throw "Review CP did not become ready. Failure: $failureCode; last HTTP status: $lastStatus. Existing services were retained. Diagnostic result: $ResultPath" }
    $copied = $false
    for ($attempt = 0; -not $SkipClipboard -and $attempt -lt 12; $attempt++) {
        try { Set-Clipboard -Value $credential; $copied = $true; break }
        catch { Start-Sleep -Milliseconds 125 }
    }
    [ordered]@{ Endpoint=$endpoint; Backend=$backend; ProcessId=(@(Get-NetTCPConnection -LocalPort $Port -State Listen)[0].OwningProcess); Model='qwen2.5:7b'; Authenticated=$true; CredentialCopied=$copied; CredentialPath=$keyPath; AccountSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value; ProxyDisabled=$true; HttpStatus=$lastStatus; DocumentSubmitted=$false; CheckedAt=(Get-Date -Format o) } | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::GetFullPath($ResultPath))
    Write-Host "Qwen review CP is ready at $endpoint."
    if ($copied) { Write-Host 'Credential copied. In the correction app, enter that endpoint, paste the credential and Connect.' }
    elseif (-not $SkipClipboard) { Write-Warning 'CP is ready, but Windows denied clipboard copy in this context. Run this launcher from Explorer in your desktop session to copy the credential. No credential was printed.' }
    else { Write-Host 'Clipboard copy was explicitly skipped for this automated startup check.' }
    Write-Host 'No document was submitted. The original CP at port 5277 is unchanged.'
} catch {
    [ordered]@{ Endpoint=$endpoint; Backend=$backend; Authenticated=$false; CredentialCopied=$false; CredentialPath=$keyPath; AccountSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value; ProxyDisabled=$true; HttpStatus=$lastStatus; FailureCode=$failureCode; DocumentSubmitted=$false; CheckedAt=(Get-Date -Format o) } | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::GetFullPath($ResultPath))
    if ($server -and -not $server.HasExited) { $server.Kill() }
    throw
} finally { $credential = $null; $http.Dispose(); $handler.Dispose() }
