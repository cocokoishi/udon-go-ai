param(
    [string]$KaTrainRoot = 'E:\UnityPRJS\ShaderGPT_neoversion\KaTrain',
    [string]$RepositoryRoot = '',
    [string]$UnityLog = 'E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\go-difficulty-corpus-1.log',
    [string]$ModelPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

$benchmarkRoot = Join-Path $RepositoryRoot 'Benchmarks\KaTrain'
$oracleTempRoot = Join-Path $RepositoryRoot 'Temp\KataGoOracle\KataGo'
$sourceKataGoRoot = Join-Path $KaTrainRoot '_internal\katrain\KataGo'
$sourceExecutable = Join-Path $sourceKataGoRoot 'katago.exe'
if ([string]::IsNullOrWhiteSpace($ModelPath))
{
    $ModelPath = Join-Path $RepositoryRoot '.KataGO\g170e-b10c128-s1141046784-d204142634.bin.gz'
}
$sourceModel = $ModelPath
$sourceConfig = Join-Path $sourceKataGoRoot 'analysis_config.cfg'
$oracleExecutable = Join-Path $oracleTempRoot 'katago.exe'
$oracleConfig = Join-Path $oracleTempRoot 'analysis_config.cfg'

New-Item -ItemType Directory -Path $benchmarkRoot -Force | Out-Null
New-Item -ItemType Directory -Path $oracleTempRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $sourceExecutable) -or
    -not (Test-Path -LiteralPath $sourceModel) -or
    -not (Test-Path -LiteralPath $sourceConfig))
{
    throw 'KaTrain oracle executable/model/config is incomplete.'
}

# Copy only the executable-side files into disposable Temp. This prevents
# KataGo OpenCL tuning from writing into the read-only KaTrain installation.
Get-ChildItem -LiteralPath $sourceKataGoRoot -File | Copy-Item -Destination $oracleTempRoot -Force
if (-not (Test-Path -LiteralPath $oracleExecutable))
{
    throw 'Temp KaTrain oracle executable copy is missing.'
}

$metricPattern = 'PURE_UDON_GO_CORPUS_METRICS id=(?<id>\S+) preset=(?<preset>\S+) configuredVisits=(?<configured>\d+) completedVisits=(?<completed>\d+) chosen=(?<chosen>-?\d+) coordinate=(?<coordinate>\S+) policyMove=(?<policyMove>-?\d+) policyCoordinate=(?<policyCoordinate>\S+) rootVisits=(?<rootVisits>\d+) nodes=(?<nodes>\d+) edges=(?<edges>\d+) frames=(?<frames>\d+)'
$metrics = New-Object System.Collections.Generic.List[object]
foreach ($line in Get-Content -LiteralPath $UnityLog)
{
    if ($line -match $metricPattern)
    {
        $parts = $Matches.id -split '-'
        $position = ($parts[0..($parts.Length - 2)] -join '-')
        $metrics.Add([pscustomobject][ordered]@{
            id = $Matches.id
            position = $position
            preset = $Matches.preset
            configuredVisits = [int]$Matches.configured
            completedVisits = [int]$Matches.completed
            chosen = [int]$Matches.chosen
            coordinate = $Matches.coordinate
            policyMove = [int]$Matches.policyMove
            policyCoordinate = $Matches.policyCoordinate
            rootVisits = [int]$Matches.rootVisits
            nodes = [int]$Matches.nodes
            edges = [int]$Matches.edges
            frames = [int]$Matches.frames
        })
    }
}
if ($metrics.Count -eq 0)
{
    throw "No completed Go corpus metrics found in $UnityLog."
}

$expectedPresetVisits = [ordered]@{
    Beginner = 4
    Advanced = 32
    Master = 100
    Ultrahard = 200
}
foreach ($presetName in $expectedPresetVisits.Keys)
{
    $presetMetrics = @($metrics | Where-Object { $_.preset -eq $presetName })
    if ($presetMetrics.Count -eq 0)
    {
        throw "Missing benchmark metrics for preset $presetName."
    }
    $configuredValues = @($presetMetrics | Select-Object -ExpandProperty configuredVisits -Unique)
    $completedValues = @($presetMetrics | Select-Object -ExpandProperty completedVisits -Unique)
    if ($configuredValues.Count -ne 1 -or $configuredValues[0] -ne $expectedPresetVisits[$presetName])
    {
        throw "Preset $presetName has unexpected configured visits: $($configuredValues -join ',')."
    }
    if ($completedValues.Count -ne 1 -or $completedValues[0] -ne $expectedPresetVisits[$presetName])
    {
        throw "Preset $presetName did not complete exactly $($expectedPresetVisits[$presetName]) visits: $($completedValues -join ',')."
    }
}

$positions = [ordered]@{
    'opening-7' = @(
        @('B', 'D16'), @('W', 'Q4'), @('B', 'Q16'), @('W', 'D4'),
        @('B', 'C17'), @('W', 'R3'), @('B', 'R17'))
    'fight-15' = @(
        @('B', 'D16'), @('W', 'Q4'), @('B', 'Q16'), @('W', 'D4'),
        @('B', 'C17'), @('W', 'R3'), @('B', 'R17'), @('W', 'C3'),
        @('B', 'E16'), @('W', 'P4'), @('B', 'Q15'), @('W', 'D5'),
        @('B', 'F17'), @('W', 'O3'), @('B', 'R14'))
    'ladder-19' = @(
        @('B', 'D16'), @('W', 'Q4'), @('B', 'Q16'), @('W', 'D4'),
        @('B', 'C17'), @('W', 'R3'), @('B', 'R17'), @('W', 'C3'),
        @('B', 'E16'), @('W', 'P4'), @('B', 'Q15'), @('W', 'D5'),
        @('B', 'F17'), @('W', 'O3'), @('B', 'R14'), @('W', 'G16'),
        @('B', 'N4'), @('W', 'Q13'), @('B', 'D7'))
    'endgame-25' = @(
        @('B', 'A19'), @('W', 'T1'), @('B', 'T19'), @('W', 'A1'),
        @('B', 'C10'), @('W', 'R10'), @('B', 'K17'), @('W', 'K3'),
        @('B', 'B14'), @('W', 'S7'), @('B', 'G18'), @('W', 'M2'),
        @('B', 'F14'), @('W', 'N6'), @('B', 'F6'), @('W', 'N14'),
        @('B', 'H16'), @('W', 'L4'), @('B', 'D12'), @('W', 'P8'),
        @('B', 'H4'), @('W', 'L16'), @('B', 'D8'), @('W', 'P12'),
        @('B', 'K10'))
}

foreach ($metric in $metrics)
{
    if (-not $positions.Contains($metric.position))
    {
        throw "No seed move definition for corpus position $($metric.position)."
    }
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
    'analysis', '-model', $sourceModel, '-config', $oracleConfig,
    '-override-config', 'forDeterministicTesting=true, nnRandomize=false, numAnalysisThreads=1, numSearchThreads=1'))
{
    [void]$psi.ArgumentList.Add($argument)
}

$process = [Diagnostics.Process]::new()
$process.StartInfo = $psi
[void]$process.Start()
$process.StandardInput.AutoFlush = $true
$stderrTask = $process.StandardError.ReadToEndAsync()
$originalResponses = @{}
$forcedResponses = @{}
$rawResponses = New-Object System.Collections.Generic.List[string]

function Read-OracleResponse([string]$expectedId)
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
            $rawResponses.Add(($response | ConvertTo-Json -Depth 10 -Compress))
            return $response
        }
    }
}

try
{
    foreach ($positionName in $positions.Keys)
    {
        $query = [ordered]@{
            id = "corpus-$positionName-original"
            moves = $positions[$positionName]
            rules = 'tromp-taylor'
            komi = 7.5
            boardXSize = 19
            boardYSize = 19
            maxVisits = 1000
            includePolicy = $true
            includeOwnership = $true
            analysisPVLen = 8
        }
        $process.StandardInput.WriteLine(($query | ConvertTo-Json -Depth 10 -Compress))
        $originalResponses[$positionName] = Read-OracleResponse $query.id
    }

    $uniqueMoves = $metrics | Group-Object position,coordinate | ForEach-Object { $_.Group[0] }
    foreach ($metric in $uniqueMoves)
    {
        $query = [ordered]@{
            id = "corpus-$($metric.position)-forced-$($metric.coordinate)"
            moves = $positions[$metric.position]
            allowMoves = @([ordered]@{ player = 'W'; moves = @($metric.coordinate); untilDepth = 1 })
            rules = 'tromp-taylor'
            komi = 7.5
            boardXSize = 19
            boardYSize = 19
            maxVisits = 1000
            includePolicy = $true
            includeOwnership = $true
            analysisPVLen = 8
        }
        $process.StandardInput.WriteLine(($query | ConvertTo-Json -Depth 10 -Compress))
        $forcedResponses["$($metric.position)|$($metric.coordinate)"] = Read-OracleResponse $query.id
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
    if ($process)
    {
        $process.Dispose()
    }
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($metric in $metrics)
{
    $original = $originalResponses[$metric.position]
    $best = @($original.moveInfos) | Select-Object -First 1
    $selected = @($original.moveInfos | Where-Object { $_.move -eq $metric.coordinate }) | Select-Object -First 1
    $forced = $forcedResponses["$($metric.position)|$($metric.coordinate)"]
    $rank = $null
    if ($selected) { $rank = [int]$selected.order }
    $scoreDelta = $null
    $winrateDelta = $null
    if ($selected -and $best)
    {
        $scoreDelta = [Math]::Abs([double]$selected.scoreMean - [double]$best.scoreMean)
        $winrateDelta = [Math]::Abs([double]$selected.winrate - [double]$best.winrate)
    }
    $rows.Add([ordered]@{
        id = $metric.id
        position = $metric.position
        preset = $metric.preset
        configuredVisits = $metric.configuredVisits
        completedVisits = $metric.completedVisits
        nodes = $metric.nodes
        edges = $metric.edges
        frames = $metric.frames
        policyMove = $metric.policyCoordinate
        chosenMove = $metric.coordinate
        policyAgreesWithPuct = ($metric.policyCoordinate -eq $metric.coordinate)
        rootVisitsForChosen = $metric.rootVisits
        oracleRootVisits = [int]$original.rootInfo.visits
        oracleBestMove = if ($best) { $best.move } else { $null }
        oracleBestOrderZeroBased = if ($best) { [int]$best.order } else { $null }
        oracleChosenOrderZeroBased = $rank
        oracleChosenTopK = if ($rank -ne $null) { $rank + 1 } else { $null }
        oracleBestScoreMean = if ($best) { [double]$best.scoreMean } else { $null }
        oracleChosenScoreMean = if ($selected) { [double]$selected.scoreMean } else { $null }
        absoluteScoreMeanDelta = $scoreDelta
        oracleBestWinrate = if ($best) { [double]$best.winrate } else { $null }
        oracleChosenWinrate = if ($selected) { [double]$selected.winrate } else { $null }
        absoluteWinrateDelta = $winrateDelta
        forcedRootVisits = if ($forced.rootInfo) { [int]$forced.rootInfo.visits } else { $null }
        forcedScoreLead = if ($forced.rootInfo) { [double]$forced.rootInfo.scoreLead } else { $null }
        forcedWinrate = if ($forced.rootInfo) { [double]$forced.rootInfo.winrate } else { $null }
        forcedUtility = if ($forced.rootInfo) { [double]$forced.rootInfo.utility } else { $null }
    })
}

$stamp = Get-Date -Format 'yyyy-MM-dd'
$rawPath = Join-Path $benchmarkRoot "katrain-corpus-$stamp-raw.jsonl"
$summaryPath = Join-Path $benchmarkRoot "katrain-corpus-$stamp-summary.json"
[IO.File]::WriteAllText($rawPath, (($rawResponses -join [Environment]::NewLine) + [Environment]::NewLine))
[IO.File]::WriteAllText((Join-Path $benchmarkRoot "katrain-corpus-$stamp-engine.log"), $stderr)

function Get-Sha256([string]$path)
{
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$summary = [ordered]@{
    schema = 'pure-udon-go.katrain-corpus-evidence.v1'
    status = 'LOCAL_ORACLE_PARTIAL_NOT_STRENGTH_CALIBRATED'
    generatedLocal = (Get-Date).ToString('o')
    unityLog = $UnityLog
    unityLogRepositoryCopy = "katrain-corpus-$stamp-unity.log"
    oracle = [ordered]@{
        sourceRoot = $KaTrainRoot
        engine = 'KataGo v1.18.1'
        gitRevision = '92ee95c0a4b25fec214da00951ab69e97e207729'
        backend = 'OpenCL'
        deviceObserved = 'NVIDIA GeForce RTX 3060 Laptop GPU'
        executableSha256 = Get-Sha256 $sourceExecutable
        modelPath = $sourceModel
        modelSha256 = Get-Sha256 $sourceModel
        configPath = $sourceConfig
        configSha256 = Get-Sha256 $sourceConfig
        queryOverrides = 'forDeterministicTesting=true, nnRandomize=false, numAnalysisThreads=1, numSearchThreads=1'
        rules = 'tromp-taylor'
        deterministicTesting = $true
        analysisVisits = 1000
        allowMovesSchema = '[{player,moves,untilDepth}]'
        runtimeCacheLocation = $oracleTempRoot
    }
    positionCount = $positions.Count
    presetCount = ($metrics | Select-Object -ExpandProperty preset -Unique).Count
    difficultyLadder = $expectedPresetVisits
    caseCount = $rows.Count
    policyPuctDivergenceCount = @($rows | Where-Object { -not $_.policyAgreesWithPuct }).Count
    rankedCaseCount = @($rows | Where-Object { $_.oracleChosenOrderZeroBased -ne $null }).Count
    positions = @($rows | Group-Object { $_['position'] } | ForEach-Object {
        [ordered]@{
            position = $_.Name
            seedMoves = $positions[$_.Name]
            cases = @($_.Group)
            oracleBestMove = $_.Group[0]['oracleBestMove']
        }
    })
    interpretation = @(
        'All listed Udon cases completed their exact configured PUCT visits in the real D3D11 GPU path.',
        'Oracle ranks and deltas are position-level measurements; they are not an Elo estimate or complete-game strength claim.',
        'This corpus does not by itself establish a monotonic preset ladder, PolicyOnly-vs-MCTS advantage, blunder rate, or balanced game win rate.'
    )
    rawResponses = (Split-Path $rawPath -Leaf)
}
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 12))

Write-Output "PURE_UDON_GO_KATA_ORACLE_PASS cases=$($rows.Count) positions=$($positions.Count) presets=$(($metrics | Select-Object -ExpandProperty preset -Unique).Count) policyPuctDivergences=$($summary.policyPuctDivergenceCount)"
Write-Output "summary=$summaryPath"
Write-Output "raw=$rawPath"
