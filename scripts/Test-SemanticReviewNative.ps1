[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$BackendPath, [Parameter(Mandatory)][string]$OutputDirectory, [string]$InsightsModel = 'phi3:mini')
$ErrorActionPreference = 'Stop'
$backend = [IO.Path]::GetFullPath($BackendPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh semantic probe evidence directory.' }
if (@(Get-NetTCPConnection -LocalPort 5288 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Probe port 5288 is occupied; no existing service was replaced.' }
if ($InsightsModel -notin @((Invoke-RestMethod 'http://localhost:11434/api/tags').models.name)) { throw 'The requested insight model is not installed; no model was downloaded.' }
$credential = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$credentialHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($credential)))
$environment = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    ASPNETCORE_URLS = 'http://127.0.0.1:5288'
    DocumentAnalysis__Enabled = 'true'
    DocumentAnalysis__ClientKeySha256 = $credentialHash
    DocumentAnalysis__TimeoutSeconds = '120'
    LlmClient__Provider = 'Ollama'
    LlmClient__Endpoint = 'http://localhost:11434'
    LlmClient__DefaultModel = $InsightsModel
    LlmClient__Timeout = '120'
    Embedding__OllamaBaseUrl = 'http://localhost:11434'
    Embedding__EmbeddingModel = 'nomic-embed-text:latest'
}
$server = $null
$app = $null
try {
    $server = Start-Process -FilePath $backend -WorkingDirectory ([IO.Path]::GetDirectoryName($backend)) -WindowStyle Hidden -Environment $environment -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $server.Refresh()
        if ($server.HasExited) { throw 'Isolated correction backend exited during startup.' }
        try {
            $caps = Invoke-RestMethod 'http://127.0.0.1:5288/api/document-analysis/capabilities' -Headers @{'X-Document-Analysis-Key'=$credential} -TimeoutSec 3
            if ($caps.protocolVersion -eq 1 -and -not $caps.simulated -and $caps.insightsModel -eq $InsightsModel) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $ready) { throw 'Isolated correction backend did not become ready.' }
    $app = Start-Process -FilePath ([IO.Path]::GetFullPath($AppPath)) -ArgumentList @('--semantic-review-probe', ('"' + $output + '"')) -WindowStyle Hidden -Environment @{AXIOM_SEMANTIC_PROBE_ENDPOINT='http://127.0.0.1:5288';AXIOM_SEMANTIC_PROBE_KEY=$credential} -PassThru
    $deadline = [DateTime]::UtcNow.AddMinutes(6)
    while (-not $app.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250; $app.Refresh() }
    if (-not $app.HasExited) { throw 'Semantic review probe timed out.' }
    $errorPath = Join-Path $output 'error.txt'
    if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
    Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw
} finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id }
    $credential = $null
}
