param(
    [string]$RepositoryRoot = '',
    [string]$KaTrainRoot = 'E:\UnityPRJS\ShaderGPT_neoversion\KaTrain',
    [string]$ManifestPath = '',
    [string]$OutputRoot = '',
    [int]$ReferenceVisits = 1000,
    [int[]]$Budgets = @(4, 32)
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
$projectRoot = (Get-Item -LiteralPath $RepositoryRoot).Parent.Parent.FullName
if ([string]::IsNullOrWhiteSpace($ManifestPath))
{
    $ManifestPath = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\EndgameRegression\professional-endgames.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\EndgameRegression'
}
if (-not (Test-Path -LiteralPath $ManifestPath))
{
    throw "Professional endgame manifest is missing: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 'pure-udon-go.professional-endgame-corpus.v1' -or
    $manifest.positionCount -ne 32 -or $manifest.positions.Count -ne 32)
{
    throw 'Professional endgame manifest schema/count is invalid.'
}
$budgets = @($Budgets)
if ($budgets.Count -eq 0 -or @($budgets | Where-Object { $_ -le 0 }).Count -gt 0)
{ throw 'Professional endgame oracle budgets must be positive.' }
foreach ($budget in $budgets)
{
    if (@($manifest.budgets | ForEach-Object { [int]$_ }) -notcontains $budget)
    { throw "Requested budget $budget is not present in the corpus manifest." }
}

$sourceKataGoRoot = Join-Path $KaTrainRoot '_internal\katrain\KataGo'
$sourceExecutable = Join-Path $sourceKataGoRoot 'katago.exe'
$sourceConfig = Join-Path $sourceKataGoRoot 'analysis_config.cfg'
$modelPath = Join-Path $RepositoryRoot '.KataGO\g170e-b10c128-s1141046784-d204142634.bin.gz'
$oracleTempRoot = Join-Path $projectRoot 'Temp\PureUdonGoEndgameRun\KataGoOracle\ProfessionalEndgame'
$oracleExecutable = Join-Path $oracleTempRoot 'katago.exe'
$oracleConfig = Join-Path $oracleTempRoot 'analysis_config.cfg'
foreach ($required in @($sourceExecutable, $sourceConfig, $modelPath))
{
    if (-not (Test-Path -LiteralPath $required))
    {
        throw "KataGo oracle input is missing: $required"
    }
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $oracleTempRoot -Force | Out-Null
Get-ChildItem -LiteralPath $sourceKataGoRoot -File | Copy-Item -Destination $oracleTempRoot -Force

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$sameBudgetPath = Join-Path $OutputRoot "$stamp-professional-endgame-same-budget.jsonl"
$referencePath = Join-Path $OutputRoot "$stamp-professional-endgame-reference-$ReferenceVisits.jsonl"
$runManifestPath = Join-Path $OutputRoot "$stamp-professional-endgame-oracle-manifest.json"

$columns = 'ABCDEFGHJKLMNOPQRST'
function Convert-Location([int]$location)
{
    if ($location -eq -1) { return 'pass' }
    if ($location -lt 0 -or $location -ge 19 * 19)
    {
        throw "Invalid endgame location: $location"
    }
    return $columns[$location % 19].ToString() +
        (19 - [math]::Floor($location / 19)).ToString()
}
function Convert-MoveLog([int[]]$locations)
{
    $moves = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $locations.Length; $i++)
    {
        $player = if (($i % 2) -eq 0) { 'B' } else { 'W' }
        $moves.Add(@($player, (Convert-Location $locations[$i])))
    }
    return $moves.ToArray()
}

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
            return ($response | ConvertTo-Json -Depth 30 -Compress)
        }
    }
}

try
{
    foreach ($position in $manifest.positions)
    {
        $moves = Convert-MoveLog ([int[]]$position.moves)
        foreach ($visits in $budgets)
        {
            $query = [ordered]@{
                id = "$($position.id)-v$visits"
                moves = $moves
                rules = 'tromp-taylor'
                komi = 7.5
                boardXSize = 19
                boardYSize = 19
                maxVisits = $visits
                includePolicy = $true
                includeOwnership = $true
                analysisPVLen = 8
            }
            $process.StandardInput.WriteLine(($query | ConvertTo-Json -Depth 20 -Compress))
            $sameBudgetResponses.Add((Read-Response $query.id))
        }
        $referenceQuery = [ordered]@{
            id = "$($position.id)-ref$ReferenceVisits"
            moves = $moves
            rules = 'tromp-taylor'
            komi = 7.5
            boardXSize = 19
            boardYSize = 19
            maxVisits = $ReferenceVisits
            includePolicy = $true
            includeOwnership = $true
            analysisPVLen = 8
        }
        $process.StandardInput.WriteLine(($referenceQuery | ConvertTo-Json -Depth 20 -Compress))
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

[IO.File]::WriteAllText($sameBudgetPath,
    (($sameBudgetResponses -join [Environment]::NewLine) + [Environment]::NewLine))
[IO.File]::WriteAllText($referencePath,
    (($referenceResponses -join [Environment]::NewLine) + [Environment]::NewLine))
$runManifest = [ordered]@{
    schema = 'pure-udon-go.professional-endgame-oracle.v1'
    generatedLocal = (Get-Date).ToString('o')
    sourceCorpusManifest = (Split-Path $ManifestPath -Leaf)
    sourceCorpusManifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
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
    positionCount = $manifest.positionCount
    sameBudgetJsonl = (Split-Path $sameBudgetPath -Leaf)
    referenceJsonl = (Split-Path $referencePath -Leaf)
    interpretation = @(
        'Same-budget rows compare real Udon observations with the same-model KataGo response at equal visits.',
        'Reference rows are a high-visit move-quality screen; they are not an Elo or complete-game strength estimate.',
        'The source corpus manifest keeps all online SGF attribution and hashes separate from this oracle output.'
    )
}
[IO.File]::WriteAllText($runManifestPath, ($runManifest | ConvertTo-Json -Depth 20))
Write-Output "PURE_UDON_GO_PRO_ENDGAME_ORACLE_PASS positions=$($manifest.positionCount) budgets=$($budgets -join ',') referenceVisits=$ReferenceVisits sameBudget=$sameBudgetPath reference=$referencePath manifest=$runManifestPath"
