param(
    [string]$RepositoryRoot = '',
    [string]$KaTrainRoot = 'E:\UnityPRJS\ShaderGPT_neoversion\KaTrain',
    [string]$OutputRoot = '',
    [int]$ReferenceVisits = 1000
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\StrengthRegression'
}

$sourceKataGoRoot = Join-Path $KaTrainRoot '_internal\katrain\KataGo'
$sourceExecutable = Join-Path $sourceKataGoRoot 'katago.exe'
$sourceConfig = Join-Path $sourceKataGoRoot 'analysis_config.cfg'
$modelPath = Join-Path $RepositoryRoot '.KataGO\g170e-b10c128-s1141046784-d204142634.bin.gz'
$oracleTempRoot = Join-Path $RepositoryRoot 'Temp\KataGoOracle\KataGo'
$oracleExecutable = Join-Path $oracleTempRoot 'katago.exe'
$oracleConfig = Join-Path $oracleTempRoot 'analysis_config.cfg'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$sameBudgetPath = Join-Path $OutputRoot "$stamp-same-budget.jsonl"
$referencePath = Join-Path $OutputRoot "$stamp-reference-$ReferenceVisits.jsonl"
$manifestPath = Join-Path $OutputRoot "$stamp-manifest.json"

foreach ($required in @($sourceExecutable, $sourceConfig, $modelPath))
{
    if (-not (Test-Path -LiteralPath $required))
    {
        throw "KataGo strength oracle input is missing: $required"
    }
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $oracleTempRoot -Force | Out-Null
# Keep tuning/cache writes out of the read-only KaTrain installation.
Get-ChildItem -LiteralPath $sourceKataGoRoot -File | Copy-Item -Destination $oracleTempRoot -Force

$columns = 'ABCDEFGHJKLMNOPQRST'
function Convert-Location([int]$location)
{
    if ($location -lt 0 -or $location -ge 19 * 19)
    {
        throw "Invalid fixed Go location: $location"
    }
    return $columns[$location % 19].ToString() + (19 - [math]::Floor($location / 19)).ToString()
}
function Convert-MoveLog([int[]]$locations)
{
    $moves = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $locations.Length; $i++)
    {
        $moves.Add(@($(if (($i % 2) -eq 0) { 'B' } else { 'W' }), (Convert-Location $locations[$i])))
    }
    return $moves.ToArray()
}
# These integer logs are copied from GoSearchCalibrationProbe's four fixed
# scenarios. Keeping the source form avoids coordinate transcription drift.
$positions = [ordered]@{
    opening = Convert-MoveLog @(60, 300)
    capture = Convert-MoveLog @(1, 0, 19)
    fight = Convert-MoveLog @(180, 181, 161, 199, 179, 201, 160, 220)
    mixed = Convert-MoveLog @(60, 300, 40, 320, 61, 299, 59, 301, 62, 298, 58)
}
$budgets = @(4, 32, 100, 200)

$psi = [Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $oracleExecutable
$psi.WorkingDirectory = $oracleTempRoot
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
foreach ($argument in @(
    'analysis', '-model', $modelPath, '-config', $oracleConfig,
    '-override-config', 'forDeterministicTesting=true,nnRandomize=false,numAnalysisThreads=1,numSearchThreads=1'))
{
    [void]$psi.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::new()
$process.StartInfo = $psi
[void]$process.Start()
$process.StandardInput.AutoFlush = $true
$stderrTask = $process.StandardError.ReadToEndAsync()
$sameBudgetResponses = New-Object System.Collections.Generic.List[string]
$referenceResponses = New-Object System.Collections.Generic.List[string]

function Read-Response([string]$expectedId)
{
    while ($true)
    {
        $line = $process.StandardOutput.ReadLine()
        if ($null -eq $line)
        {
            throw "KataGo closed stdout before response $expectedId."
        }
        try { $response = $line | ConvertFrom-Json }
        catch { continue }
        if ($response.id -eq $expectedId)
        {
            return ($response | ConvertTo-Json -Depth 20 -Compress)
        }
    }
}

try
{
    foreach ($positionName in $positions.Keys)
    {
        foreach ($visits in $budgets)
        {
            $query = [ordered]@{
                id = "$positionName-v$visits"
                moves = $positions[$positionName]
                rules = 'tromp-taylor'
                komi = 7.5
                boardXSize = 19
                boardYSize = 19
                maxVisits = $visits
                includePolicy = $true
                includeOwnership = $true
                analysisPVLen = 8
            }
            $process.StandardInput.WriteLine(($query | ConvertTo-Json -Depth 10 -Compress))
            $sameBudgetResponses.Add((Read-Response $query.id))
        }
        $referenceQuery = [ordered]@{
            id = "$positionName-ref$ReferenceVisits"
            moves = $positions[$positionName]
            rules = 'tromp-taylor'
            komi = 7.5
            boardXSize = 19
            boardYSize = 19
            maxVisits = $ReferenceVisits
            includePolicy = $true
            includeOwnership = $true
            analysisPVLen = 8
        }
        $process.StandardInput.WriteLine(($referenceQuery | ConvertTo-Json -Depth 10 -Compress))
        $referenceResponses.Add((Read-Response $referenceQuery.id))
    }
}
finally
{
    if ($process -and -not $process.HasExited)
    {
        $process.StandardInput.Close()
        $process.WaitForExit()
    }
    $stderr = $stderrTask.Result
    if ($process) { $process.Dispose() }
}

[IO.File]::WriteAllText($sameBudgetPath, (($sameBudgetResponses -join [Environment]::NewLine) + [Environment]::NewLine))
[IO.File]::WriteAllText($referencePath, (($referenceResponses -join [Environment]::NewLine) + [Environment]::NewLine))
$manifest = [ordered]@{
    schema = 'pure-udon-go.strength-regression-oracle.v1'
    generatedLocal = (Get-Date).ToString('o')
    model = $modelPath
    modelSha256 = (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    executable = $sourceExecutable
    executableSha256 = (Get-FileHash -LiteralPath $sourceExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    config = $sourceConfig
    configSha256 = (Get-FileHash -LiteralPath $sourceConfig -Algorithm SHA256).Hash.ToLowerInvariant()
    rules = 'tromp-taylor'
    komi = 7.5
    deterministic = $true
    budgets = $budgets
    referenceVisits = $ReferenceVisits
    positions = $positions
    sameBudgetJsonl = (Split-Path $sameBudgetPath -Leaf)
    referenceJsonl = (Split-Path $referencePath -Leaf)
    interpretation = @(
        'Same-budget rows compare deterministic Udon observations with the same-model KataGo analysis at the matching visit target.',
        'Reference rows rank the Udon move against a higher-visit same-model KataGo teacher and are a regression screen, not an Elo estimate.',
        'No-Catastrophic-Signal requires every selected move to appear in the reference candidate list, at least 75 percent in the top ten, and max winrate regret no greater than 0.15.'
    )
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 20))
Write-Output "PURE_UDON_GO_STRENGTH_ORACLE_PASS budgets=$($budgets -join ',') referenceVisits=$ReferenceVisits positions=$($positions.Count) sameBudget=$sameBudgetPath reference=$referencePath manifest=$manifestPath"
