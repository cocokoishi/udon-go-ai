param(
    [string]$RepositoryRoot = '',
    [string]$ManifestPath = '',
    [string[]]$UdonTracePath = @(),
    [string]$SameBudgetOraclePath = '',
    [string]$ReferenceOraclePath = '',
    [int]$ReferenceVisits = 1000,
    [string]$OutputPath = '',
    [int[]]$Budgets = @(4, 32),
    [switch]$AllowPartial
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($ManifestPath))
{
    $ManifestPath = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\EndgameRegression\professional-endgames.json'
}
if ($null -eq $UdonTracePath -or $UdonTracePath.Count -eq 0)
{
    $UdonTracePath = @(Join-Path $RepositoryRoot 'Benchmarks\KaTrain\ClientSimExports\GoProfessionalEndgame\go-professional-endgame.json')
}
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\EndgameRegression\professional-endgame-comparison.json'
}
if (-not (Test-Path -LiteralPath $ManifestPath)) { throw "Manifest is missing: $ManifestPath" }
foreach ($tracePath in $UdonTracePath)
{
    if (-not (Test-Path -LiteralPath $tracePath)) { throw "Udon trace is missing: $tracePath" }
}
if (-not (Test-Path -LiteralPath $SameBudgetOraclePath)) { throw "Same-budget oracle is missing: $SameBudgetOraclePath" }

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$udonTraces = @($UdonTracePath | ForEach-Object {
    Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json
})
$udonRows = New-Object System.Collections.Generic.List[object]
foreach ($trace in $udonTraces)
{
    if ($trace.status -ne 'CLIENTSIM_PRO_ENDGAME_PASS')
    {
        throw "Udon professional endgame trace is not a PASS: $($trace.status)"
    }
    foreach ($traceRow in @($trace.rows))
    {
        # Keep historical 100-visit artifacts readable, but never mix them
        # into the focused 4/32 parity report.
        if ($Budgets -contains [int]$traceRow.targetVisits)
        { [void]$udonRows.Add($traceRow) }
    }
}
$columns = 'ABCDEFGHJKLMNOPQRST'
function Convert-Location([int]$location)
{
    if ($location -eq -1) { return 'pass' }
    if ($location -lt 0 -or $location -ge 19 * 19) { return 'none' }
    return $columns[$location % 19].ToString() +
        (19 - [math]::Floor($location / 19)).ToString()
}
if ($manifest.schema -ne 'pure-udon-go.professional-endgame-corpus.v1' -or
    $manifest.positionCount -ne 32 -or $Budgets.Count -eq 0)
{
    throw 'Unexpected professional endgame manifest.'
}
foreach ($budget in $Budgets)
{
    if (@($manifest.budgets | ForEach-Object { [int]$_ }) -notcontains $budget)
    { throw "Requested budget $budget is not present in the corpus manifest." }
}
$expectedRowCount = $manifest.positionCount * $Budgets.Count
if (-not $AllowPartial -and $udonRows.Count -ne $expectedRowCount)
{
    throw 'Udon professional endgame trace is not a complete PASS.'
}

function Read-JsonLines([string]$path)
{
    $map = @{}
    foreach ($line in Get-Content -LiteralPath $path)
    {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $value = $line | ConvertFrom-Json
        if ($null -ne $value.id) { $map[[string]$value.id] = $value }
    }
    return $map
}
$sameBudget = Read-JsonLines $SameBudgetOraclePath
$reference = if (-not [string]::IsNullOrWhiteSpace($ReferenceOraclePath) -and
    (Test-Path -LiteralPath $ReferenceOraclePath)) { Read-JsonLines $ReferenceOraclePath } else { @{} }

$rows = New-Object System.Collections.Generic.List[object]
foreach ($row in $udonRows)
{
    $id = "$($row.id)-v$($row.targetVisits)"
    $oracle = $sameBudget[$id]
    if ($null -eq $oracle)
    {
        throw "Missing same-budget oracle response: $id"
    }
    $moveInfos = @($oracle.moveInfos)
    # KataGo's analysis JSON is sorted by AnalysisData::operator<.  The first
    # row is the deterministic play-selection comparator
    # (playSelectionValue, then visits, then policy), not raw child-visit
    # argmax. Keep both views explicit so comparator ordering is not mistaken
    # for a search-tree divergence.
    $playSelectionBest = if ($moveInfos.Count -gt 0) { $moveInfos[0] } else { $null }
    $rawVisitValues = @($moveInfos | ForEach-Object {
        if ($null -ne $_.edgeVisits) { [int64]$_.edgeVisits } else { [int64]$_.visits }
    })
    $rawVisitMax = if ($rawVisitValues.Count -gt 0) {
        ($rawVisitValues | Measure-Object -Maximum).Maximum
    } else { $null }
    $rawVisitMoves = New-Object System.Collections.Generic.List[string]
    if ($null -ne $rawVisitMax)
    {
        foreach ($info in $moveInfos)
        {
            $edgeVisits = if ($null -ne $info.edgeVisits) { [int64]$info.edgeVisits } else { [int64]$info.visits }
            if ($edgeVisits -eq $rawVisitMax -and $null -ne $info.move -and
                -not $rawVisitMoves.Contains([string]$info.move))
            { [void]$rawVisitMoves.Add([string]$info.move) }
        }
    }
    $playSelectionMove = if ($null -ne $playSelectionBest) { [string]$playSelectionBest.move } else { $null }
    $sampledMove = if ($null -ne $row.sampledSelectedCoordinate -and
        $row.sampledSelectedCoordinate -ne 'none') { [string]$row.sampledSelectedCoordinate } else {
        [string]$row.selectedCoordinate
    }
    $deterministicMove = if ($null -ne $row.deterministicRootCoordinate -and
        $row.deterministicRootCoordinate -ne 'none') { [string]$row.deterministicRootCoordinate } else {
        # v1 traces predate the explicit deterministic field. Their selected
        # move is the only available observation, so use it as a compatibility
        # fallback rather than silently dropping the sample.
        $sampledMove
    }
    $sampled = @($moveInfos | Where-Object { $_.move -eq $sampledMove } | Select-Object -First 1)
    if ($sampled.Count -eq 0) { $sampled = $null } else { $sampled = $sampled[0] }
    $deterministic = @($moveInfos | Where-Object { $_.move -eq $deterministicMove } | Select-Object -First 1)
    if ($deterministic.Count -eq 0) { $deterministic = $null } else { $deterministic = $deterministic[0] }
    $oracleCandidateMoves = New-Object System.Collections.Generic.List[string]
    foreach ($info in ($moveInfos | Select-Object -First 10))
    {
        if ($null -ne $info.move -and -not $oracleCandidateMoves.Contains([string]$info.move))
        {
            [void]$oracleCandidateMoves.Add([string]$info.move)
        }
    }
    $udonCandidateMoves = New-Object System.Collections.Generic.List[string]
    if ($null -ne $row.rootCandidateMoves)
    {
        foreach ($move in @($row.rootCandidateMoves | Select-Object -First 10))
        {
            if ([int]$move -lt 0) { continue }
            $coordinate = Convert-Location ([int]$move)
            if (-not $udonCandidateMoves.Contains($coordinate))
            {
                [void]$udonCandidateMoves.Add($coordinate)
            }
        }
    }
    $candidateOverlap10 = 0
    foreach ($coordinate in $udonCandidateMoves)
    {
        if ($oracleCandidateMoves.Contains($coordinate)) { $candidateOverlap10++ }
    }
    $candidateUnion10 = $udonCandidateMoves.Count + $oracleCandidateMoves.Count - $candidateOverlap10
    $candidateJaccard10 = if ($candidateUnion10 -gt 0) { $candidateOverlap10 / [double]$candidateUnion10 } else { 0.0 }
    $udonTop5 = @($udonCandidateMoves | Select-Object -First 5)
    $udonTop1 = @($udonCandidateMoves | Select-Object -First 1)
    $oracleTop5 = @($oracleCandidateMoves | Select-Object -First 5)
    $oracleTop1 = @($oracleCandidateMoves | Select-Object -First 1)
    $top5Overlap = @($udonTop5 | Where-Object { $oracleTop5 -contains $_ }).Count
    $top1Overlap = @($udonTop1 | Where-Object { $oracleTop1 -contains $_ }).Count
    $udonVisitedMoves = New-Object System.Collections.Generic.List[object]
    if ($null -ne $row.rootCandidateMoves -and $null -ne $row.rootCandidateVisits)
    {
        $candidateCount = [Math]::Min(@($row.rootCandidateMoves).Count,
            @($row.rootCandidateVisits).Count)
        for ($candidate = 0; $candidate -lt $candidateCount; $candidate++)
        {
            $move = [int]$row.rootCandidateMoves[$candidate]
            $visits = [int]$row.rootCandidateVisits[$candidate]
            if ($move -ge 0 -and $visits -gt 0)
            { [void]$udonVisitedMoves.Add([pscustomobject]@{ move=$move; visits=$visits }) }
        }
    }
    $udonRawVisitMax = if ($udonVisitedMoves.Count -gt 0) {
        ($udonVisitedMoves | Measure-Object -Property visits -Maximum).Maximum
    } else { $null }
    $udonRawVisitMoves = New-Object System.Collections.Generic.List[string]
    if ($null -ne $row.rawRootVisitArgmaxMoves -and
        @($row.rawRootVisitArgmaxMoves).Count -gt 0)
    {
        foreach ($move in @($row.rawRootVisitArgmaxMoves))
        {
            if ([int]$move -ge 0)
            {
                $coordinate = Convert-Location ([int]$move)
                if (-not $udonRawVisitMoves.Contains($coordinate))
                { [void]$udonRawVisitMoves.Add($coordinate) }
            }
        }
    }
    # Compatibility fallback for v1-v3 traces that predate the complete
    # tie-set accessors.
    if ($udonRawVisitMoves.Count -eq 0 -and $null -ne $udonRawVisitMax)
    {
        foreach ($candidate in $udonVisitedMoves)
        {
            if ($candidate.visits -eq $udonRawVisitMax)
            {
                $coordinate = Convert-Location $candidate.move
                if (-not $udonRawVisitMoves.Contains($coordinate))
                { [void]$udonRawVisitMoves.Add($coordinate) }
            }
        }
    }
    $ref = $null
    $refBest = $null
    $refSampled = $null
    $refDeterministic = $null
    if ($reference.Count -gt 0)
    {
        $ref = $reference["$($row.id)-ref$ReferenceVisits"]
        if ($null -ne $ref)
        {
            $refInfos = @($ref.moveInfos)
            if ($refInfos.Count -gt 0) { $refBest = $refInfos[0] }
            $refSampled = @($refInfos | Where-Object { $_.move -eq $sampledMove } | Select-Object -First 1)
            if ($refSampled.Count -eq 0) { $refSampled = $null } else { $refSampled = $refSampled[0] }
            $refDeterministic = @($refInfos | Where-Object { $_.move -eq $deterministicMove } | Select-Object -First 1)
            if ($refDeterministic.Count -eq 0) { $refDeterministic = $null } else { $refDeterministic = $refDeterministic[0] }
        }
    }
    $sampledRank = if ($null -ne $sampled) { [int]$sampled.order } else { $null }
    $deterministicRank = if ($null -ne $deterministic) { [int]$deterministic.order } else { $null }
    $sampledRefRank = if ($null -ne $refSampled) { [int]$refSampled.order } else { $null }
    $deterministicRefRank = if ($null -ne $refDeterministic) { [int]$refDeterministic.order } else { $null }
    $rows.Add([ordered]@{
        id = $row.id
        sourceUrl = $row.sourceUrl
        moveHashSha256 = $row.moveHashSha256
        prefixMoveCount = [int]$row.prefixMoveCount
        sideToMove = $row.sideToMove
        targetVisits = [int]$row.targetVisits
        actualVisits = [int]$row.actualVisits
        terminalState = if ($null -ne $row.terminalState) { [int]$row.terminalState } else { 0 }
        aiResigned = $row.aiResigned -eq $true
        searchFrames = if ($null -ne $row.searchFrames) { [int]$row.searchFrames } else { 0 }
        searchMilliseconds = if ($null -ne $row.searchMilliseconds) { [double]$row.searchMilliseconds } else { $null }
        featureMilliseconds = if ($null -ne $row.featureMilliseconds) { [double]$row.featureMilliseconds } else { $null }
        ladderMilliseconds = if ($null -ne $row.ladderMilliseconds) { [double]$row.ladderMilliseconds } else { $null }
        scoreUtilityMilliseconds = if ($null -ne $row.scoreUtilityMilliseconds) { [double]$row.scoreUtilityMilliseconds } else { $null }
        gpuEvaluationMilliseconds = if ($null -ne $row.gpuEvaluationMilliseconds) { [double]$row.gpuEvaluationMilliseconds } else { $null }
        gpuDispatchMilliseconds = if ($null -ne $row.gpuDispatchMilliseconds) { [double]$row.gpuDispatchMilliseconds } else { $null }
        readbackMilliseconds = if ($null -ne $row.readbackMilliseconds) { [double]$row.readbackMilliseconds } else { $null }
        historyEntriesExamined = if ($null -ne $row.historyEntriesExamined) { [int]$row.historyEntriesExamined } else { 0 }
        historyQueryCount = if ($null -ne $row.historyQueryCount) { [int]$row.historyQueryCount } else { 0 }
        historyBucketHitCount = if ($null -ne $row.historyBucketHitCount) { [int]$row.historyBucketHitCount } else { 0 }
        historyBucketMissCount = if ($null -ne $row.historyBucketMissCount) { [int]$row.historyBucketMissCount } else { 0 }
        historyFullScanCount = if ($null -ne $row.historyFullScanCount) { [int]$row.historyFullScanCount } else { 0 }
        ladderSearchTransitions = if ($null -ne $row.ladderSearchTransitions) { [int]$row.ladderSearchTransitions } else { 0 }
        ladderNodes = if ($null -ne $row.ladderNodes) { [int]$row.ladderNodes } else { 0 }
        gpuPasses = if ($null -ne $row.gpuPasses) { [int]$row.gpuPasses } else { 0 }
        gpuGraphFrames = if ($null -ne $row.gpuGraphFrames) { [int]$row.gpuGraphFrames } else { 0 }
        readbackRequests = if ($null -ne $row.readbackRequests) { [int]$row.readbackRequests } else { 0 }
        # Keep selectedMove as the sampled product result for compatibility.
        selectedMove = $sampledMove
        sampledSelectedMove = $sampledMove
        deterministicRootMove = $deterministicMove
        deterministicRootVisits = if ($null -ne $row.deterministicRootVisits) {
            [int]$row.deterministicRootVisits
        } else { 0 }
        policyMove = $row.policyCoordinate
        exactUdonSearch = ([int]$row.actualVisits -eq [int]$row.targetVisits)
        # Compatibility alias: oracleBestMove historically meant
        # moveInfos[0], which is KataGo's play-selection ordering.
        oracleBestMove = $playSelectionMove
        oraclePlaySelectionMove = $playSelectionMove
        oracleRawVisitArgmaxMoves = $rawVisitMoves.ToArray()
        oracleRawVisitArgmaxMove = if ($rawVisitMoves.Count -eq 1) { $rawVisitMoves[0] } else { $null }
        oracleRawVisitArgmaxTieCount = $rawVisitMoves.Count
        udonRawVisitArgmaxMoves = $udonRawVisitMoves.ToArray()
        udonRawVisitArgmaxMove = if ($udonRawVisitMoves.Count -eq 1) { $udonRawVisitMoves[0] } else { $null }
        udonRawVisitArgmaxTieCount = $udonRawVisitMoves.Count
        rootVisitArgmaxParity = ($rawVisitMoves.Count -gt 0 -and
            $udonRawVisitMoves.Count -gt 0 -and
            @($udonRawVisitMoves | Where-Object { $rawVisitMoves.Contains($_) }).Count -gt 0)
        rootVisitArgmaxExact = ($rawVisitMoves.Count -eq 1 -and
            $udonRawVisitMoves.Count -eq 1 -and
            $rawVisitMoves[0] -eq $udonRawVisitMoves[0])
        playSelectionParity = ($null -ne $playSelectionMove -and
            $playSelectionMove -eq $deterministicMove)
        # Legacy aliases intentionally retain sampled semantics.
        oracleTop1Match = ($null -ne $playSelectionBest -and $playSelectionMove -eq $sampledMove)
        oracleCandidate = ($null -ne $sampled)
        oracleRankZeroBased = $sampledRank
        sampledTop1Matches = ($null -ne $playSelectionBest -and $playSelectionMove -eq $sampledMove)
        deterministicTop1Matches = ($null -ne $playSelectionBest -and $playSelectionMove -eq $deterministicMove)
        sampledCandidateCoverage = ($null -ne $sampled)
        deterministicCandidateCoverage = ($null -ne $deterministic)
        sampledRankZeroBased = $sampledRank
        deterministicRankZeroBased = $deterministicRank
        candidateTop1Overlap = $top1Overlap
        candidateTop5Overlap = $top5Overlap
        candidateTop10Overlap = $candidateOverlap10
        candidateTop10Jaccard = $candidateJaccard10
        oracleSelectedWinrate = if ($null -ne $sampled) { [double]$sampled.winrate } else { $null }
        oracleBestWinrate = if ($null -ne $playSelectionBest) { [double]$playSelectionBest.winrate } else { $null }
        oracleWinrateRegret = if ($null -ne $sampled -and $null -ne $playSelectionBest) { [Math]::Abs([double]$sampled.winrate - [double]$playSelectionBest.winrate) } else { $null }
        oracleSelectedScoreMean = if ($null -ne $sampled) { [double]$sampled.scoreMean } else { $null }
        oracleBestScoreMean = if ($null -ne $playSelectionBest) { [double]$playSelectionBest.scoreMean } else { $null }
        oracleScoreRegret = if ($null -ne $sampled -and $null -ne $playSelectionBest) { [Math]::Abs([double]$sampled.scoreMean - [double]$playSelectionBest.scoreMean) } else { $null }
        sampledWinrateRegret = if ($null -ne $sampled -and $null -ne $playSelectionBest) { [Math]::Abs([double]$sampled.winrate - [double]$playSelectionBest.winrate) } else { $null }
        deterministicWinrateRegret = if ($null -ne $deterministic -and $null -ne $playSelectionBest) { [Math]::Abs([double]$deterministic.winrate - [double]$playSelectionBest.winrate) } else { $null }
        sampledScoreRegret = if ($null -ne $sampled -and $null -ne $playSelectionBest) { [Math]::Abs([double]$sampled.scoreMean - [double]$playSelectionBest.scoreMean) } else { $null }
        deterministicScoreRegret = if ($null -ne $deterministic -and $null -ne $playSelectionBest) { [Math]::Abs([double]$deterministic.scoreMean - [double]$playSelectionBest.scoreMean) } else { $null }
        referenceAvailable = ($null -ne $ref)
        referenceCandidate = ($null -ne $refSampled)
        referenceRankZeroBased = $sampledRefRank
        sampledReferenceCandidate = ($null -ne $refSampled)
        deterministicReferenceCandidate = ($null -ne $refDeterministic)
        sampledReferenceRank = $sampledRefRank
        deterministicReferenceRank = $deterministicRefRank
        referenceBestMove = if ($null -ne $refBest) { $refBest.move } else { $null }
        referenceWinrateRegret = if ($null -ne $refSampled -and $null -ne $refBest) { [Math]::Abs([double]$refSampled.winrate - [double]$refBest.winrate) } else { $null }
        referenceScoreRegret = if ($null -ne $refSampled -and $null -ne $refBest) { [Math]::Abs([double]$refSampled.scoreMean - [double]$refBest.scoreMean) } else { $null }
        sampledReferenceWinrateRegret = if ($null -ne $refSampled -and $null -ne $refBest) { [Math]::Abs([double]$refSampled.winrate - [double]$refBest.winrate) } else { $null }
        deterministicReferenceWinrateRegret = if ($null -ne $refDeterministic -and $null -ne $refBest) { [Math]::Abs([double]$refDeterministic.winrate - [double]$refBest.winrate) } else { $null }
        sampledReferenceScoreRegret = if ($null -ne $refSampled -and $null -ne $refBest) { [Math]::Abs([double]$refSampled.scoreMean - [double]$refBest.scoreMean) } else { $null }
        deterministicReferenceScoreRegret = if ($null -ne $refDeterministic -and $null -ne $refBest) { [Math]::Abs([double]$refDeterministic.scoreMean - [double]$refBest.scoreMean) } else { $null }
    })
}

function Get-Mean([object[]]$values)
{
    if ($null -eq $values -or $values.Count -eq 0) { return $null }
    return (($values | Measure-Object -Average).Average)
}
function Get-Max([object[]]$values)
{
    if ($null -eq $values -or $values.Count -eq 0) { return $null }
    return (($values | Measure-Object -Maximum).Maximum)
}

$aggregates = New-Object System.Collections.Generic.List[object]
foreach ($budget in $Budgets)
{
    $subset = @($rows | Where-Object { $_.targetVisits -eq $budget })
    $sampledRanks = @($subset | Where-Object { $null -ne $_.sampledRankZeroBased } | ForEach-Object { $_.sampledRankZeroBased })
    $deterministicRanks = @($subset | Where-Object { $null -ne $_.deterministicRankZeroBased } | ForEach-Object { $_.deterministicRankZeroBased })
    $sampledRefRanks = @($subset | Where-Object { $null -ne $_.sampledReferenceRank } | ForEach-Object { $_.sampledReferenceRank })
    $deterministicRefRanks = @($subset | Where-Object { $null -ne $_.deterministicReferenceRank } | ForEach-Object { $_.deterministicReferenceRank })
    $sampledWr = @($subset | Where-Object { $null -ne $_.sampledWinrateRegret } | ForEach-Object { $_.sampledWinrateRegret })
    $deterministicWr = @($subset | Where-Object { $null -ne $_.deterministicWinrateRegret } | ForEach-Object { $_.deterministicWinrateRegret })
    $sampledScore = @($subset | Where-Object { $null -ne $_.sampledScoreRegret } | ForEach-Object { $_.sampledScoreRegret })
    $deterministicScore = @($subset | Where-Object { $null -ne $_.deterministicScoreRegret } | ForEach-Object { $_.deterministicScoreRegret })
    $sampledRefWr = @($subset | Where-Object { $null -ne $_.sampledReferenceWinrateRegret } | ForEach-Object { $_.sampledReferenceWinrateRegret })
    $deterministicRefWr = @($subset | Where-Object { $null -ne $_.deterministicReferenceWinrateRegret } | ForEach-Object { $_.deterministicReferenceWinrateRegret })
    $sampledRefScore = @($subset | Where-Object { $null -ne $_.sampledReferenceScoreRegret } | ForEach-Object { $_.sampledReferenceScoreRegret })
    $deterministicRefScore = @($subset | Where-Object { $null -ne $_.deterministicReferenceScoreRegret } | ForEach-Object { $_.deterministicReferenceScoreRegret })
    $jaccards = @($subset | ForEach-Object { $_.candidateTop10Jaccard })
    $aggregates.Add([ordered]@{
        visits = $budget
        samples = $subset.Count
        exactUdonSearches = @($subset | Where-Object { $_.exactUdonSearch }).Count
        sampledTop1Matches = @($subset | Where-Object { $_.sampledTop1Matches }).Count
        deterministicTop1Matches = @($subset | Where-Object { $_.deterministicTop1Matches }).Count
        rootVisitArgmaxParity = @($subset | Where-Object { $_.rootVisitArgmaxParity }).Count
        rootVisitArgmaxExact = @($subset | Where-Object { $_.rootVisitArgmaxExact }).Count
        playSelectionParity = @($subset | Where-Object { $_.playSelectionParity }).Count
        sampledCandidateCoverage = @($subset | Where-Object { $_.sampledCandidateCoverage }).Count
        deterministicCandidateCoverage = @($subset | Where-Object { $_.deterministicCandidateCoverage }).Count
        sampledTop5Coverage = @($subset | Where-Object { $null -ne $_.sampledRankZeroBased -and $_.sampledRankZeroBased -lt 5 }).Count
        deterministicTop5Coverage = @($subset | Where-Object { $null -ne $_.deterministicRankZeroBased -and $_.deterministicRankZeroBased -lt 5 }).Count
        sampledTop10Coverage = @($subset | Where-Object { $null -ne $_.sampledRankZeroBased -and $_.sampledRankZeroBased -lt 10 }).Count
        deterministicTop10Coverage = @($subset | Where-Object { $null -ne $_.deterministicRankZeroBased -and $_.deterministicRankZeroBased -lt 10 }).Count
        # Existing aggregate names remain sampled aliases for consumers of v1.
        sameBudgetTop1Matches = @($subset | Where-Object { $_.sampledTop1Matches }).Count
        sameBudgetCandidateCoverage = @($subset | Where-Object { $_.sampledCandidateCoverage }).Count
        sameBudgetTop5Coverage = @($subset | Where-Object { $null -ne $_.sampledRankZeroBased -and $_.sampledRankZeroBased -lt 5 }).Count
        sameBudgetTop10Coverage = @($subset | Where-Object { $null -ne $_.sampledRankZeroBased -and $_.sampledRankZeroBased -lt 10 }).Count
        candidateTop1Overlap = ($subset | Measure-Object -Property candidateTop1Overlap -Sum).Sum
        candidateTop5Overlap = ($subset | Measure-Object -Property candidateTop5Overlap -Sum).Sum
        candidateTop10Overlap = ($subset | Measure-Object -Property candidateTop10Overlap -Sum).Sum
        meanCandidateTop10Jaccard = Get-Mean $jaccards
        meanSampledSameBudgetRankZeroBased = Get-Mean $sampledRanks
        meanDeterministicSameBudgetRankZeroBased = Get-Mean $deterministicRanks
        meanSampledSameBudgetWinrateRegret = Get-Mean $sampledWr
        maxSampledSameBudgetWinrateRegret = Get-Max $sampledWr
        meanDeterministicSameBudgetWinrateRegret = Get-Mean $deterministicWr
        maxDeterministicSameBudgetWinrateRegret = Get-Max $deterministicWr
        meanSampledSameBudgetScoreRegret = Get-Mean $sampledScore
        meanDeterministicSameBudgetScoreRegret = Get-Mean $deterministicScore
        # Existing aliases remain sampled values.
        meanSameBudgetRankZeroBased = Get-Mean $sampledRanks
        meanSameBudgetWinrateRegret = Get-Mean $sampledWr
        maxSameBudgetWinrateRegret = Get-Max $sampledWr
        meanSameBudgetScoreRegret = Get-Mean $sampledScore
        referenceSamples = $sampledRefRanks.Count
        sampledReferenceCandidateCoverage = @($subset | Where-Object { $_.sampledReferenceCandidate }).Count
        deterministicReferenceCandidateCoverage = @($subset | Where-Object { $_.deterministicReferenceCandidate }).Count
        sampledReferenceTop5Coverage = @($subset | Where-Object { $null -ne $_.sampledReferenceRank -and $_.sampledReferenceRank -lt 5 }).Count
        deterministicReferenceTop5Coverage = @($subset | Where-Object { $null -ne $_.deterministicReferenceRank -and $_.deterministicReferenceRank -lt 5 }).Count
        sampledReferenceTop10Coverage = @($subset | Where-Object { $null -ne $_.sampledReferenceRank -and $_.sampledReferenceRank -lt 10 }).Count
        deterministicReferenceTop10Coverage = @($subset | Where-Object { $null -ne $_.deterministicReferenceRank -and $_.deterministicReferenceRank -lt 10 }).Count
        resignedSamples = @($subset | Where-Object { $_.aiResigned }).Count
        referenceCandidateCoverage = @($subset | Where-Object { $_.sampledReferenceCandidate }).Count
        referenceTop5Coverage = @($subset | Where-Object { $null -ne $_.sampledReferenceRank -and $_.sampledReferenceRank -lt 5 }).Count
        referenceTop10Coverage = @($subset | Where-Object { $null -ne $_.sampledReferenceRank -and $_.sampledReferenceRank -lt 10 }).Count
        meanSampledReferenceRankZeroBased = Get-Mean $sampledRefRanks
        meanDeterministicReferenceRankZeroBased = Get-Mean $deterministicRefRanks
        meanSampledReferenceWinrateRegret = Get-Mean $sampledRefWr
        maxSampledReferenceWinrateRegret = Get-Max $sampledRefWr
        meanDeterministicReferenceWinrateRegret = Get-Mean $deterministicRefWr
        maxDeterministicReferenceWinrateRegret = Get-Max $deterministicRefWr
        meanSampledReferenceScoreRegret = Get-Mean $sampledRefScore
        meanDeterministicReferenceScoreRegret = Get-Mean $deterministicRefScore
        meanReferenceRankZeroBased = Get-Mean $sampledRefRanks
        meanReferenceWinrateRegret = Get-Mean $sampledRefWr
        maxReferenceWinrateRegret = Get-Max $sampledRefWr
        meanReferenceScoreRegret = Get-Mean $sampledRefScore
    })
}

$allRefRanks = @($rows | Where-Object { $null -ne $_.referenceRankZeroBased } | ForEach-Object { $_.referenceRankZeroBased })
$allRefWr = @($rows | Where-Object { $null -ne $_.referenceWinrateRegret } | ForEach-Object { $_.referenceWinrateRegret })
$allDeterministicRefRanks = @($rows | Where-Object { $null -ne $_.deterministicReferenceRank } | ForEach-Object { $_.deterministicReferenceRank })
$allDeterministicRefWr = @($rows | Where-Object { $null -ne $_.deterministicReferenceWinrateRegret } | ForEach-Object { $_.deterministicReferenceWinrateRegret })
$catastrophic = 'NOT_RUN'
if ($reference.Count -gt 0)
{
    $catastrophic = if ($allRefRanks.Count -eq $rows.Count -and
        @($allRefRanks | Where-Object { $_ -lt 10 }).Count -ge [Math]::Ceiling($rows.Count * 0.75) -and
        (Get-Max $allRefWr) -le 0.15) { 'NO_CATASTROPHIC_SIGNAL' } else { 'REVIEW_REQUIRED' }
}

$comparison = [ordered]@{
    schema = 'pure-udon-go.professional-endgame-comparison.v4'
    status = if ($udonRows.Count -eq $expectedRowCount) { 'PASS' } else { 'PARTIAL' }
    generatedLocal = (Get-Date).ToString('o')
    corpusManifest = (Split-Path $ManifestPath -Leaf)
    corpusManifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    udonTrace = @($UdonTracePath | ForEach-Object { Split-Path $_ -Leaf })
    udonTraceSha256 = @($UdonTracePath | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() })
    sameBudgetOracle = (Split-Path $SameBudgetOraclePath -Leaf)
    sameBudgetOracleSha256 = (Get-FileHash -LiteralPath $SameBudgetOraclePath -Algorithm SHA256).Hash.ToLowerInvariant()
    referenceOracle = if ($reference.Count -gt 0) { (Split-Path $ReferenceOraclePath -Leaf) } else { $null }
    referenceVisits = if ($reference.Count -gt 0) { $ReferenceVisits } else { $null }
    positionCount = $manifest.positionCount
    budgets = $Budgets
    sampleCount = $rows.Count
    exactUdonSearches = @($rows | Where-Object { $_.exactUdonSearch }).Count
    sampledTop1Matches = @($rows | Where-Object { $_.sampledTop1Matches }).Count
    deterministicTop1Matches = @($rows | Where-Object { $_.deterministicTop1Matches }).Count
    rootVisitArgmaxParity = @($rows | Where-Object { $_.rootVisitArgmaxParity }).Count
    rootVisitArgmaxExact = @($rows | Where-Object { $_.rootVisitArgmaxExact }).Count
    playSelectionParity = @($rows | Where-Object { $_.playSelectionParity }).Count
    sampledCandidateCoverage = @($rows | Where-Object { $_.sampledCandidateCoverage }).Count
    deterministicCandidateCoverage = @($rows | Where-Object { $_.deterministicCandidateCoverage }).Count
    # Keep old aggregate aliases sampled for compatibility.
    sameBudgetTop1Matches = @($rows | Where-Object { $_.oracleTop1Match }).Count
    sameBudgetCandidateCoverage = @($rows | Where-Object { $_.oracleCandidate }).Count
    sampledReferenceRankSamples = $allRefRanks.Count
    deterministicReferenceRankSamples = $allDeterministicRefRanks.Count
    meanSampledReferenceRankZeroBased = Get-Mean $allRefRanks
    meanDeterministicReferenceRankZeroBased = Get-Mean $allDeterministicRefRanks
    meanSampledReferenceWinrateRegret = Get-Mean $allRefWr
    maxSampledReferenceWinrateRegret = Get-Max $allRefWr
    meanDeterministicReferenceWinrateRegret = Get-Mean $allDeterministicRefWr
    maxDeterministicReferenceWinrateRegret = Get-Max $allDeterministicRefWr
    referenceCandidateCoverage = @($rows | Where-Object { $_.referenceCandidate }).Count
    referenceTop10Coverage = @($rows | Where-Object { $null -ne $_.referenceRankZeroBased -and $_.referenceRankZeroBased -lt 10 }).Count
    meanReferenceRankZeroBased = Get-Mean $allRefRanks
    meanReferenceWinrateRegret = Get-Mean $allRefWr
    maxReferenceWinrateRegret = Get-Max $allRefWr
    catastrophicRegressionScreen = $catastrophic
    aggregates = $aggregates.ToArray()
    rows = $rows.ToArray()
    scope = '32 fixed professional late-game positions, real Udon ClientSim observations and same-model KataGo analysis; this is a position-level consistency screen, not Elo or complete-game strength calibration.'
}

New-Item -ItemType Directory -Path (Split-Path $OutputPath) -Force | Out-Null
[IO.File]::WriteAllText($OutputPath, ($comparison | ConvertTo-Json -Depth 30))
$reportPath = [IO.Path]::ChangeExtension($OutputPath, '.md')
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# PureUdonGo professional endgame consistency')
$lines.Add('')
$lines.Add("Status: ``$catastrophic`` (position-level screen; not an Elo claim).")
$lines.Add('')
$lines.Add("Corpus: $($manifest.positionCount) fixed positions; budgets: $($Budgets -join ', '); samples: $($rows.Count).")
$lines.Add('')
$lines.Add('| visits | exact Udon | raw visit argmax | play-selection parity | Udon deterministic vs play-selection | sampled Top-1 | deterministic ref rank | sampled ref rank | mean candidate Jaccard@10 |')
$lines.Add('| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
foreach ($aggregate in $aggregates)
{
    $lines.Add("| $($aggregate.visits) | $($aggregate.exactUdonSearches)/$($aggregate.samples) | $($aggregate.rootVisitArgmaxParity)/$($aggregate.samples) | $($aggregate.playSelectionParity)/$($aggregate.samples) | $($aggregate.deterministicTop1Matches)/$($aggregate.samples) | $($aggregate.sampledTop1Matches)/$($aggregate.samples) | $([string]::Format('{0:F3}', $aggregate.meanDeterministicReferenceRankZeroBased)) | $([string]::Format('{0:F3}', $aggregate.meanSampledReferenceRankZeroBased)) | $([string]::Format('{0:F3}', $aggregate.meanCandidateTop10Jaccard)) |")
}
$lines.Add('')
$lines.Add("Overall raw root-visit argmax parity: $($comparison.rootVisitArgmaxParity)/$($comparison.sampleCount); KataGo play-selection parity: $($comparison.playSelectionParity)/$($comparison.sampleCount); Udon deterministic-vs-play-selection Top-1: $($comparison.deterministicTop1Matches)/$($comparison.sampleCount). Deterministic reference rank samples: $($comparison.deterministicReferenceRankSamples); sampled reference rank samples: $($comparison.sampledReferenceRankSamples).")
$lines.Add('')
$lines.Add('The same-model oracle and Udon search use the same fixed model/rules query. Candidate coverage and regret are stronger than Top-1 alone at 4/32 visits, where deterministic low-budget tie-breaking can differ. This does not establish full policy-distribution identity, bit-for-bit KataGo PUCT identity, Elo, or complete-game win rate.')
$lines.Add('')
$lines.Add("Machine-readable output: ``$(Split-Path $OutputPath -Leaf)``")
[IO.File]::WriteAllLines($reportPath, $lines)
if ($comparison.status -eq 'PASS')
{
    Write-Output "PURE_UDON_GO_PRO_ENDGAME_COMPARISON_PASS positions=$($manifest.positionCount) budgets=$($Budgets -join ',') samples=$($rows.Count) sampledTop1=$($comparison.sampledTop1Matches)/$($comparison.sampleCount) rawVisitParity=$($comparison.rootVisitArgmaxParity)/$($comparison.sampleCount) playSelectionParity=$($comparison.playSelectionParity)/$($comparison.sampleCount) referenceTop10=$($comparison.referenceTop10Coverage)/$($comparison.sampleCount) catastrophic=$catastrophic output=$OutputPath report=$reportPath"
}
else
{
    Write-Output "PURE_UDON_GO_PRO_ENDGAME_COMPARISON_PARTIAL positions=$($manifest.positionCount) budgets=$($Budgets -join ',') samples=$($rows.Count) sampledTop1=$($comparison.sampledTop1Matches)/$($comparison.sampleCount) rawVisitParity=$($comparison.rootVisitArgmaxParity)/$($comparison.sampleCount) playSelectionParity=$($comparison.playSelectionParity)/$($comparison.sampleCount) referenceTop10=$($comparison.referenceTop10Coverage)/$($comparison.sampleCount) catastrophic=$catastrophic output=$OutputPath report=$reportPath"
}
