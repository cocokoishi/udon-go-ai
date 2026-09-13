param(
    [Parameter(Mandatory = $true)]
    [string]$FirstSummary,
    [Parameter(Mandatory = $true)]
    [string]$SecondSummary,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Read-Summary([string]$path)
{
    if (-not (Test-Path -LiteralPath $path))
    {
        throw "Summary does not exist: $path"
    }
    return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
}

function Get-Rows($summary)
{
    return @($summary.positions | ForEach-Object { $_.cases })
}

$first = Read-Summary $FirstSummary
$second = Read-Summary $SecondSummary
$firstRows = Get-Rows $first
$secondRows = Get-Rows $second
$fields = @(
    'chosenMove',
    'oracleBestMove',
    'oracleChosenOrderZeroBased',
    'oracleChosenTopK',
    'oracleBestScoreMean',
    'oracleChosenScoreMean',
    'absoluteScoreMeanDelta',
    'oracleBestWinrate',
    'oracleChosenWinrate',
    'absoluteWinrateDelta',
    'policyAgreesWithPuct'
)
$differences = New-Object System.Collections.Generic.List[object]

foreach ($firstRow in $firstRows)
{
    $key = "$($firstRow.position)|$($firstRow.preset)"
    $secondRow = $secondRows | Where-Object {
        "$($_.position)|$($_.preset)" -eq $key
    } | Select-Object -First 1
    if ($null -eq $secondRow)
    {
        $differences.Add([ordered]@{ key = $key; field = '__row__'; first = 'present'; second = 'missing' })
        continue
    }
    foreach ($field in $fields)
    {
        if ([string]$firstRow.$field -ne [string]$secondRow.$field)
        {
            $differences.Add([ordered]@{
                key = $key
                field = $field
                first = $firstRow.$field
                second = $secondRow.$field
            })
        }
    }
}

foreach ($secondRow in $secondRows)
{
    $key = "$($secondRow.position)|$($secondRow.preset)"
    if (-not ($firstRows | Where-Object { "$($_.position)|$($_.preset)" -eq $key }))
    {
        $differences.Add([ordered]@{ key = $key; field = '__row__'; first = 'missing'; second = 'present' })
    }
}

$result = if ($differences.Count -eq 0) { 'PASS' } else { 'FAIL' }
$firstSummaryLeaf = [IO.Path]::GetFileName($FirstSummary)
$secondSummaryLeaf = [IO.Path]::GetFileName($SecondSummary)
$interpretation = if ($differences.Count -eq 0) {
    'The two independent oracle processes produced identical selected-move, rank, score/winrate, and policy/PUCT fields for every corpus case.'
} else {
    'At least one compared oracle field changed between independent processes; do not treat this corpus run as reproducible.'
}
$evidence = [ordered]@{
    schema = 'pure-udon-go.katrain-corpus-reproducibility.v1'
    result = $result
    comparedFields = $fields
    firstSummary = $firstSummaryLeaf
    secondSummary = $secondSummaryLeaf
    firstCaseCount = $first.caseCount
    secondCaseCount = $second.caseCount
    firstDeterministicTesting = $first.oracle.deterministicTesting
    secondDeterministicTesting = $second.oracle.deterministicTesting
    firstQueryOverrides = $first.oracle.queryOverrides
    secondQueryOverrides = $second.oracle.queryOverrides
    differenceCount = $differences.Count
    differences = $differences.ToArray()
    interpretation = $interpretation
}

$parent = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $parent -Force | Out-Null
[IO.File]::WriteAllText($OutputPath, ($evidence | ConvertTo-Json -Depth 12))
Write-Output "PURE_UDON_GO_KATA_REPRODUCIBILITY_$result cases=$($first.caseCount) differences=$($differences.Count)"
Write-Output "evidence=$OutputPath"
