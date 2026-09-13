param(
    [string]$ComparisonPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ComparisonPath))
{ $ComparisonPath = Join-Path $PSScriptRoot '..\..\..\Benchmarks\KaTrain\EndgameRegression\professional-endgame-parity-4-32-current-v2.json' }
if (-not (Test-Path -LiteralPath $ComparisonPath)) { throw "Comparison is missing: $ComparisonPath" }
$comparison = Get-Content -LiteralPath $ComparisonPath -Raw | ConvertFrom-Json
if ($comparison.schema -ne 'pure-udon-go.professional-endgame-comparison.v4')
{ throw "Unexpected comparator schema: $($comparison.schema)" }
if (@($comparison.budgets).Count -ne 2 -or
    [int]$comparison.budgets[0] -ne 4 -or [int]$comparison.budgets[1] -ne 32)
{ throw 'The focused comparator must contain only 4 and 32 visits.' }
$rows = @($comparison.rows)
if ($rows.Count -ne 64) { throw "Expected 64 focused rows, got $($rows.Count)." }
foreach ($row in $rows)
{
    if ([int]$row.targetVisits -ne 4 -and [int]$row.targetVisits -ne 32)
    { throw "Unexpected budget in row $($row.id): $($row.targetVisits)" }
    if ($null -eq $row.oraclePlaySelectionMove -or
        $null -eq $row.oracleRawVisitArgmaxMoves -or
        $null -eq $row.rootVisitArgmaxParity)
    { throw "Comparator fields are incomplete for $($row.id)-v$($row.targetVisits)." }
}
# Guard the key semantic distinction: at least one row must differ between
# KataGo play-selection and raw-visit-set comparison; otherwise a future
# refactor could silently collapse the two metrics again.
$separated = @($rows | Where-Object {
    ([bool]$_.rootVisitArgmaxParity) -and -not ([bool]$_.playSelectionParity)
}).Count
if ($separated -lt 1)
{ throw 'Raw visit and play-selection comparators were not kept separate.' }
Write-Output "PURE_UDON_GO_PARITY_COMPARATOR_TEST_PASS rows=$($rows.Count) separatedRows=$separated raw4=$($comparison.aggregates[0].rootVisitArgmaxParity)/$($comparison.aggregates[0].samples) raw32=$($comparison.aggregates[1].rootVisitArgmaxParity)/$($comparison.aggregates[1].samples)"
