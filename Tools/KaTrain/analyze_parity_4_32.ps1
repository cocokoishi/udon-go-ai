param(
    [string]$ComparisonPath = '',
    [string]$KataGoTracePath = '',
    [string[]]$UdonTracePath = @(),
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ComparisonPath))
{ $ComparisonPath = Join-Path $PSScriptRoot '..\..\Benchmarks\KaTrain\EndgameRegression\professional-endgame-parity-4-32-current.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath))
{ $OutputPath = Join-Path (Split-Path $ComparisonPath) 'professional-endgame-first-divergence-4-32.json' }
if (-not (Test-Path -LiteralPath $ComparisonPath)) { throw "Comparison is missing: $ComparisonPath" }

$comparison = Get-Content -LiteralPath $ComparisonPath -Raw | ConvertFrom-Json
$rows = @($comparison.rows | Where-Object { $_.targetVisits -eq 4 -or $_.targetVisits -eq 32 })
if ($rows.Count -eq 0) { throw 'The comparison has no 4/32 rows.' }

$kataRecords = @()
if (-not [string]::IsNullOrWhiteSpace($KataGoTracePath) -and
    (Test-Path -LiteralPath $KataGoTracePath))
{
    $kata = Get-Content -LiteralPath $KataGoTracePath -Raw | ConvertFrom-Json
    $kataRecords = @($kata.records)
}
$udonRows = @()
foreach ($tracePath in $UdonTracePath)
{
    if (-not (Test-Path -LiteralPath $tracePath)) { continue }
    $trace = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
    $udonRows += @($trace.rows)
}

$byKey = @{}
foreach ($row in $rows) { $byKey["$($row.id)-$($row.targetVisits)"] = $row }
$reportRows = New-Object System.Collections.Generic.List[object]
foreach ($row4 in @($rows | Where-Object { $_.targetVisits -eq 4 }))
{
    $row32 = $byKey["$($row4.id)-32"]
    $trace4 = @($udonRows | Where-Object { $_.id -eq $row4.id -and [int]$_.targetVisits -eq 4 } | Select-Object -First 1)
    $trace32 = @($udonRows | Where-Object { $_.id -eq $row4.id -and [int]$_.targetVisits -eq 32 } | Select-Object -First 1)
    if ($trace4.Count -eq 0) { $trace4 = $null } else { $trace4 = $trace4[0] }
    if ($trace32.Count -eq 0) { $trace32 = $null } else { $trace32 = $trace32[0] }
    $kata4 = @($kataRecords | Where-Object { $_.id -eq $row4.id -and [int]$_.targetVisits -eq 4 } | Sort-Object rootVisits)
    $kata32 = @($kataRecords | Where-Object { $_.id -eq $row4.id -and [int]$_.targetVisits -eq 32 } | Sort-Object rootVisits)
    $rootTrace4 = if ($null -ne $trace4 -and $null -ne $trace4.rootDecisionTrace) { @($trace4.rootDecisionTrace) } else { @() }
    $rootTrace32 = if ($null -ne $trace32 -and $null -ne $trace32.rootDecisionTrace) { @($trace32.rootDecisionTrace) } else { @() }
    function Find-ComparableDivergence($udonDecisions, $kataSnapshots)
    {
        if ($udonDecisions.Count -eq 0 -or $kataSnapshots.Count -eq 0)
        { return [ordered]@{ status='NOT_COMPARABLE'; simulationIndex=$null; selectedMove=$null; kataRawMoves=@() } }
        foreach ($decision in ($udonDecisions | Sort-Object simulationIndex))
        {
            # Udon records the decision while the root has N visits; the
            # corresponding post-backup snapshot has N+1 root visits.
            $expectedRootVisits = [int]$decision.simulationIndex + 1
            $snapshot = @($kataSnapshots | Where-Object { [int]$_.rootVisits -eq $expectedRootVisits } | Select-Object -First 1)
            if ($snapshot.Count -eq 0) { continue }
            $snapshot = $snapshot[0]
            $rawMoves = @($snapshot.rawVisitArgmaxMoves)
            if ($rawMoves -notcontains $decision.selectedCoordinate)
            {
                return [ordered]@{
                    status='EARLIEST_COMPARABLE_ROOT_SNAPSHOT'
                    simulationIndex=[int]$decision.simulationIndex
                    selectedMove=$decision.selectedCoordinate
                    kataRawMoves=$rawMoves
                    kataRootVisits=$expectedRootVisits
                    margin=[double]$decision.bestScore - [double]$decision.secondBestScore
                }
            }
        }
        return [ordered]@{ status='NO_DIVERGENCE_AT_REPORTED_SNAPSHOTS'; simulationIndex=$null; selectedMove=$null; kataRawMoves=@() }
    }
    $comparable4 = Find-ComparableDivergence $rootTrace4 $kata4
    $comparable32 = Find-ComparableDivergence $rootTrace32 $kata32
    $status4 = if ($row4.rootVisitArgmaxParity) { 'RAW_VISIT_SET_MATCH' } else { 'RAW_VISIT_SET_MISMATCH' }
    $status32 = if ($null -ne $row32 -and $row32.rootVisitArgmaxParity) { 'RAW_VISIT_SET_MATCH' } else { 'RAW_VISIT_SET_MISMATCH' }
    $reportRows.Add([ordered]@{
        id = $row4.id
        v4 = [ordered]@{
            udonDeterministicMove = $row4.deterministicRootMove
            katagoPlaySelectionMove = $row4.oraclePlaySelectionMove
            katagoRawVisitArgmaxMoves = @($row4.oracleRawVisitArgmaxMoves)
            udonRawVisitArgmaxMoves = @($row4.udonRawVisitArgmaxMoves)
            rawVisitParity = [bool]$row4.rootVisitArgmaxParity
            rawVisitExact = [bool]$row4.rootVisitArgmaxExact
            playSelectionParity = [bool]$row4.playSelectionParity
            deterministicVsPlaySelection = [bool]$row4.deterministicTop1Matches
            rootDecisionTraceAvailable = ($rootTrace4.Count -gt 0)
            udonRootDecisions = @($rootTrace4 | ForEach-Object { $_.selectedCoordinate })
            earliestComparableDivergence = $comparable4
            kataRootReports = @($kata4 | ForEach-Object {
                [ordered]@{ rootVisits=[int]$_.rootVisits; rawVisitArgmaxMoves=@($_.rawVisitArgmaxMoves); playSelectionMove=$_.playSelectionMove }
            })
        }
        v32 = if ($null -ne $row32) { [ordered]@{
            udonDeterministicMove = $row32.deterministicRootMove
            katagoPlaySelectionMove = $row32.oraclePlaySelectionMove
            katagoRawVisitArgmaxMoves = @($row32.oracleRawVisitArgmaxMoves)
            udonRawVisitArgmaxMoves = @($row32.udonRawVisitArgmaxMoves)
            rawVisitParity = [bool]$row32.rootVisitArgmaxParity
            rawVisitExact = [bool]$row32.rootVisitArgmaxExact
            playSelectionParity = [bool]$row32.playSelectionParity
            deterministicVsPlaySelection = [bool]$row32.deterministicTop1Matches
            rootDecisionTraceAvailable = ($rootTrace32.Count -gt 0)
            udonRootDecisions = @($rootTrace32 | ForEach-Object { $_.selectedCoordinate })
            earliestComparableDivergence = $comparable32
            kataRootReports = @($kata32 | ForEach-Object {
                [ordered]@{ rootVisits=[int]$_.rootVisits; rawVisitArgmaxMoves=@($_.rawVisitArgmaxMoves); playSelectionMove=$_.playSelectionMove }
            })
        } } else { $null }
        category = if ($null -eq $row32) { 'MISSING_32_ROW' }
            elseif (-not $row4.rootVisitArgmaxParity -and -not $row32.rootVisitArgmaxParity) { 'A_BOTH_RAW_MISMATCH' }
            elseif (-not $row4.rootVisitArgmaxParity -and $row32.rootVisitArgmaxParity) { 'B_ONLY_4_RAW_MISMATCH' }
            elseif ($row4.rootVisitArgmaxParity -and -not $row32.rootVisitArgmaxParity) { 'C_ONLY_32_RAW_MISMATCH' }
            else { 'D_RAW_MATCH_BOTH' }
        firstDivergence = [ordered]@{
            status = if ($rootTrace4.Count -gt 0 -or $rootTrace32.Count -gt 0) {
                'UDON_TRACE_CAPTURED_KATAGO_REPORTS_SPARSE'
            } else { 'NOT_CAPTURED' }
            note = 'The analyzer compares only root visits that KataGo reportDuringSearch actually emitted. An earliest comparable mismatch is a lower bound, not an exact first simulation; exact first-simulation parity requires a trace-capable KataGo build or equivalent per-playout hook.'
        }
    })
}

$result = [ordered]@{
    schema = 'pure-udon-go.professional-endgame-first-divergence.v1'
    generatedLocal = (Get-Date).ToString('o')
    comparison = Split-Path $ComparisonPath -Leaf
    budgets = @(4, 32)
    positions = $reportRows.ToArray()
    rawVisitMismatch4 = @($reportRows | Where-Object { $_.v4.rawVisitParity -eq $false } | ForEach-Object { $_.id })
    rawVisitMismatch32 = @($reportRows | Where-Object { $_.v32.rawVisitParity -eq $false } | ForEach-Object { $_.id })
    categories = [ordered]@{
        bothRawMismatch = @($reportRows | Where-Object { $_.category -eq 'A_BOTH_RAW_MISMATCH' } | ForEach-Object { $_.id })
        only4RawMismatch = @($reportRows | Where-Object { $_.category -eq 'B_ONLY_4_RAW_MISMATCH' } | ForEach-Object { $_.id })
        only32RawMismatch = @($reportRows | Where-Object { $_.category -eq 'C_ONLY_32_RAW_MISMATCH' } | ForEach-Object { $_.id })
        rawMatchBoth = @($reportRows | Where-Object { $_.category -eq 'D_RAW_MATCH_BOTH' } | ForEach-Object { $_.id })
    }
    interpretation = 'Raw visit argmax is compared as a tie-aware set. KataGo play-selection and sampled product moves remain separate metrics. The current report does not claim exact first divergence until a per-playout KataGo hook is available.'
}
New-Item -ItemType Directory -Path (Split-Path $OutputPath) -Force | Out-Null
[IO.File]::WriteAllText($OutputPath, ($result | ConvertTo-Json -Depth 30))
$reportPath = [IO.Path]::ChangeExtension($OutputPath, '.md')
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# 4/32 deterministic parity index')
$lines.Add('')
$lines.Add('This report separates raw root-visit argmax from KataGo play-selection ordering.')
$lines.Add('')
$lines.Add('| position | 4v Udon | 4v KataGo play | 4v raw set | 32v Udon | 32v KataGo play | 32v raw set | category |')
$lines.Add('| --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($row in $reportRows)
{
    $v32 = if ($null -ne $row.v32) { $row.v32 } else { [ordered]@{udonDeterministicMove='—';katagoPlaySelectionMove='—';rawVisitParity=$false} }
    $raw4 = if ($row.v4.rawVisitParity) { 'MATCH' } else { 'MISMATCH' }
    $raw32 = if ($v32.rawVisitParity) { 'MATCH' } else { 'MISMATCH' }
    $lines.Add("| $($row.id) | $($row.v4.udonDeterministicMove) | $($row.v4.katagoPlaySelectionMove) | $raw4 | $($v32.udonDeterministicMove) | $($v32.katagoPlaySelectionMove) | $raw32 | $($row.category) |")
}
$lines.Add('')
$lines.Add("4v raw-visit set mismatches: $($result.rawVisitMismatch4.Count); 32v: $($result.rawVisitMismatch32.Count).")
$lines.Add('')
$lines.Add($result.interpretation)
[IO.File]::WriteAllLines($reportPath, $lines)
Write-Output "PURE_UDON_GO_PARITY_INDEX_PASS positions=$($reportRows.Count) rawMismatch4=$($result.rawVisitMismatch4.Count) rawMismatch32=$($result.rawVisitMismatch32.Count) output=$OutputPath"
