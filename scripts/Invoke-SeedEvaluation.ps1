[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Seeds,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Seeds.Count -ne 10) {
    throw 'Exactly 10 valid Balatro seed strings are required for a comparable evaluation.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$balatrobotEndpoint = [Environment]::GetEnvironmentVariable('BALATROBOT_ENDPOINT')
if ([string]::IsNullOrWhiteSpace($balatrobotEndpoint)) {
    $settingsPath = Join-Path $projectRoot 'src\JevBalatroBot\appsettings.json'
    $balatrobotEndpoint = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).Balatrobot.Endpoint
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot (Join-Path 'logs\evaluations' (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$savedSeed = [Environment]::GetEnvironmentVariable('BALATRO_SEED')
$savedLogPath = [Environment]::GetEnvironmentVariable('JEV_LOG_PATH')
$results = [System.Collections.Generic.List[object]]::new()

try {
    for ($index = 0; $index -lt $Seeds.Count; $index++) {
        $seed = $Seeds[$index]
        $seedDirectory = Join-Path $OutputDirectory ('seed-{0:D2}' -f ($index + 1))
        $logRoot = Join-Path $seedDirectory 'logs'
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

        [Environment]::SetEnvironmentVariable('BALATRO_SEED', $seed)
        [Environment]::SetEnvironmentVariable('JEV_LOG_PATH', (Join-Path $logRoot 'anchor.jsonl'))
        Write-Host "[EVAL] Seed $($index + 1)/10: $seed"
        Invoke-RestMethod -Method Post -Uri $balatrobotEndpoint -ContentType 'application/json' -Body (@{ jsonrpc = '2.0'; method = 'menu'; id = $index + 1 } | ConvertTo-Json -Compress) | Out-Null
        & dotnet run --project (Join-Path $projectRoot 'src\JevBalatroBot') --no-restore
        $exitCode = $LASTEXITCODE

        $summary = Get-ChildItem -LiteralPath $logRoot -Recurse -Filter 'summary.json' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc |
            Select-Object -Last 1
        if ($null -eq $summary) {
            $results.Add([pscustomobject]@{ Seed = $seed; ExitCode = $exitCode; Result = 'NoSummary'; HighestAnte = $null; FinalScore = $null; RunId = $null })
            continue
        }

        $runSummary = Get-Content -LiteralPath $summary.FullName -Raw | ConvertFrom-Json
        $results.Add([pscustomobject]@{
            Seed = $seed
            ExitCode = $exitCode
            Result = $runSummary.Result.Result
            HighestAnte = $runSummary.Result.HighestAnte
            FinalScore = $runSummary.Result.FinalScore
            RunId = $runSummary.Result.RunId
        })
    }
}
finally {
    [Environment]::SetEnvironmentVariable('BALATRO_SEED', $savedSeed)
    [Environment]::SetEnvironmentVariable('JEV_LOG_PATH', $savedLogPath)
}

$summaryPath = Join-Path $OutputDirectory 'evaluation-summary.json'
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding utf8
$results | Format-Table -AutoSize
Write-Host "[EVAL] Summary: $summaryPath"
