[CmdletBinding()]
param([Parameter(Mandatory)][string]$BackendPath, [Parameter(Mandatory)][string]$OutputDirectory,
      [string[]]$Models = @('qwen2.5:7b','mistral:latest'))
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh evidence directory.' }
if (@(Get-NetTCPConnection -LocalPort 5290 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Port 5290 is occupied; no service was replaced.' }
$installed = @((Invoke-RestMethod 'http://localhost:11434/api/tags').models.name)
foreach ($model in $Models) { if ($model -notin $installed) { throw "Model is not already installed: $model" } }
New-Item -ItemType Directory -Path $output | Out-Null
$credential = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($credential)))
$documents = @(
    @{ referenceKey='buffer-1-Solar.md'; text="# Solar electricity`nSolar panels convert sunlight into electricity.`nThe system generates power during daylight.`nBatteries can store surplus energy for use at night." },
    @{ referenceKey='buffer-2-Wind.md'; text="# Wind electricity`nWind turbines use moving air to generate electrical power.`nOutput varies with wind speed.`nBatteries can store surplus energy when generation exceeds demand." },
    @{ referenceKey='buffer-3-Lunch.md'; text="# Lunch recipe`nCombine cooked pasta with tomatoes and olive oil.`nAdd basil and serve warm.`nStore leftover food in the refrigerator." }
)
foreach ($document in $documents) { $document.sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($document.text))) }
$records = [Collections.Generic.List[object]]::new()
foreach ($model in $Models) {
    $server = $null
    try {
        $server = Start-Process -FilePath ([IO.Path]::GetFullPath($BackendPath)) -WorkingDirectory ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($BackendPath))) -WindowStyle Hidden -PassThru -Environment @{
            ASPNETCORE_ENVIRONMENT='Development'; ASPNETCORE_URLS='http://127.0.0.1:5290'
            DocumentAnalysis__Enabled='true'; DocumentAnalysis__ClientKeySha256=$hash; DocumentAnalysis__TimeoutSeconds='120'
            LlmClient__Provider='Ollama'; LlmClient__Endpoint='http://localhost:11434'; LlmClient__DefaultModel=$model; LlmClient__Timeout='120'
            Embedding__OllamaBaseUrl='http://localhost:11434'; Embedding__EmbeddingModel='nomic-embed-text:latest'
        }
        $ready = $false
        for ($attempt=0; $attempt -lt 60; $attempt++) {
            $server.Refresh(); if ($server.HasExited) { throw 'Isolated backend exited.' }
            try { $caps = Invoke-RestMethod 'http://127.0.0.1:5290/api/document-analysis/capabilities' -Headers @{'X-Document-Analysis-Key'=$credential} -TimeoutSec 3; if ($caps.insightsModel -eq $model) { $ready=$true; break } } catch { Start-Sleep -Milliseconds 250 }
        }
        if (-not $ready) { throw 'Requested isolated model did not become ready.' }
        foreach ($case in @('original','reordered')) {
            $selected = if ($case -eq 'original') { $documents } else { @($documents[2],$documents[1],$documents[0]) }
            $request = @{protocolVersion=1;clientRequestId=[Guid]::NewGuid().ToString();documents=$selected;question='Compare how the two energy documents generate electricity and handle surplus power. Cite the reviewed sources and exclude the recipe.'}
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $fileStem = $model.Replace(':','-') + '-' + $case
            try {
                $answer = Invoke-RestMethod 'http://127.0.0.1:5290/api/document-analysis/insights' -Method Post -Headers @{'X-Document-Analysis-Key'=$credential} -ContentType 'application/json' -Body ($request | ConvertTo-Json -Depth 8 -Compress) -TimeoutSec 125
                $answer | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output "$fileStem.json")
                $citations = [regex]::Matches($answer.text,'\[\[(?<key>[^|\]]+)\|L(?<first>\d+)(?:-L(?<last>\d+))?\]\]')
                $valid = @($citations | Where-Object { $match=$_; $source=$documents | Where-Object referenceKey -eq $match.Groups['key'].Value; $first=[int]$match.Groups['first'].Value; $last=if($match.Groups['last'].Success){[int]$match.Groups['last'].Value}else{$first}; $source -and $first -ge 1 -and $last -ge $first -and $last -le $source.text.Split("`n").Length })
                $keys = @($valid | ForEach-Object {$_.Groups['key'].Value})
                $records.Add([ordered]@{Model=$model;ProviderModel=$answer.model;Case=$case;Seconds=[math]::Round($watch.Elapsed.TotalSeconds,2);ResolvedRanges=$valid.Count;BothEnergySourcesCited=('buffer-1-Solar.md' -in $keys -and 'buffer-2-Wind.md' -in $keys);ExcludedRecipeAbsent=($answer.text -notmatch '(?i)lunch|recipe|pasta|refrigerator');InvalidLocationTokens=($citations.Count - $valid.Count);SnapshotManifestPreserved=($answer.references.Count -eq 3);HumanClaimReviewRequired=$true})
            } catch { $records.Add([ordered]@{Model=$model;Case=$case;Seconds=[math]::Round($watch.Elapsed.TotalSeconds,2);Error='Request failed or timed out; no quality pass claimed';HumanClaimReviewRequired=$true}) }
        }
    } finally { if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id } }
}
$credential = $null
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'result.json')
$records | ConvertTo-Json -Depth 6
