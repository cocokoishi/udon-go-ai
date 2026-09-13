param(
    [string]$RepositoryRoot = '',
    [string]$KaTrainRoot = 'E:\UnityPRJS\ShaderGPT_neoversion\KaTrain',
    [string]$ManifestPath = '',
    [string]$OutputPath = '',
    [string[]]$PositionId = @(),
    [int[]]$Budgets = @(4, 32)
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{ $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
if ([string]::IsNullOrWhiteSpace($ManifestPath))
{ $ManifestPath = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\EndgameRegression\professional-endgames.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath))
{ $OutputPath = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\EndgameRegression\katago-root-traces-4-32.json' }
if (-not (Test-Path -LiteralPath $ManifestPath)) { throw "Manifest is missing: $ManifestPath" }
if ($Budgets.Count -eq 0 -or @($Budgets | Where-Object { $_ -le 0 }).Count -gt 0)
{ throw 'Budgets must contain positive visit counts.' }

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 'pure-udon-go.professional-endgame-corpus.v1' -or
    $manifest.positionCount -ne 32)
{ throw 'Unexpected professional endgame manifest.' }
$positions = @($manifest.positions)
if ($PositionId.Count -gt 0)
{
    $positions = @($positions | Where-Object { $PositionId -contains $_.id })
    if ($positions.Count -eq 0) { throw 'No requested position id was found.' }
}

$sourceRoot = Join-Path $KaTrainRoot '_internal\katrain\KataGo'
$sourceExe = Join-Path $sourceRoot 'katago.exe'
$sourceConfig = Join-Path $sourceRoot 'analysis_config.cfg'
$model = Join-Path $RepositoryRoot '.KataGO\g170e-b10c128-s1141046784-d204142634.bin.gz'
foreach ($path in @($sourceExe, $sourceConfig, $model))
{ if (-not (Test-Path -LiteralPath $path)) { throw "KataGo input is missing: $path" } }

$runtimeRoot = Join-Path $RepositoryRoot 'Temp\KaTrainParityTrace\KataGo'
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -File | Copy-Item -Destination $runtimeRoot -Force
$exe = Join-Path $runtimeRoot 'katago.exe'
$config = Join-Path $runtimeRoot 'analysis_config.cfg'

$columns = 'ABCDEFGHJKLMNOPQRST'
function Convert-Location([int]$location)
{
    if ($location -eq -1) { return 'pass' }
    if ($location -lt 0 -or $location -ge 19 * 19) { return 'none' }
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
$psi.FileName = $exe
$psi.WorkingDirectory = $runtimeRoot
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
foreach ($argument in @(
    'analysis', '-model', $model, '-config', $config,
    '-override-config', 'forDeterministicTesting=true,nnRandomize=false,numAnalysisThreads=1,numSearchThreads=1'))
{ [void]$psi.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $psi
[void]$process.Start()
$process.StandardInput.AutoFlush = $true
$stderrTask = $process.StandardError.ReadToEndAsync()
$records = New-Object System.Collections.Generic.List[object]

function Read-ReportSequence([string]$expectedId)
{
    $reports = New-Object System.Collections.Generic.List[object]
    while ($true)
    {
        $line = $process.StandardOutput.ReadLine()
        if ($null -eq $line) { throw "KataGo closed stdout before response $expectedId." }
        try { $response = $line | ConvertFrom-Json } catch { continue }
        if ($response.id -ne $expectedId) { continue }
        [void]$reports.Add($response)
        if (-not $response.isDuringSearch) { return $reports.ToArray() }
    }
}

try
{
    foreach ($position in $positions)
    {
        foreach ($visits in $Budgets)
        {
            $id = "$($position.id)-v$visits-trace"
            $query = [ordered]@{
                id = $id
                moves = Convert-MoveLog ([int[]]$position.moves)
                rules = 'tromp-taylor'
                komi = 7.5
                boardXSize = 19
                boardYSize = 19
                maxVisits = $visits
                includePolicy = $true
                includeOwnership = $false
                includePVVisits = $true
                analysisPVLen = 8
                reportDuringSearchEvery = 0.001
                firstReportDuringSearchAfter = 0.001
            }
            $process.StandardInput.WriteLine(($query | ConvertTo-Json -Depth 20 -Compress))
            $reports = Read-ReportSequence $id
            foreach ($report in $reports)
            {
                $infos = @($report.moveInfos)
                $rawMax = if ($infos.Count -gt 0) {
                    ($infos | ForEach-Object {
                        if ($null -ne $_.edgeVisits) { [int64]$_.edgeVisits } else { [int64]$_.visits }
                    } | Measure-Object -Maximum).Maximum
                } else { $null }
                $rawMoves = @($infos | Where-Object {
                    $value = if ($null -ne $_.edgeVisits) { [int64]$_.edgeVisits } else { [int64]$_.visits }
                    $null -ne $rawMax -and $value -eq $rawMax
                } | ForEach-Object { [string]$_.move })
                [void]$records.Add([ordered]@{
                    id = $position.id
                    targetVisits = $visits
                    isDuringSearch = [bool]$report.isDuringSearch
                    rootVisits = if ($null -ne $report.rootInfo) { [int]$report.rootInfo.visits } else { 0 }
                    playSelectionMove = if ($infos.Count -gt 0) { [string]$infos[0].move } else { $null }
                    playSelectionValue = if ($infos.Count -gt 0) { [double]$infos[0].playSelectionValue } else { $null }
                    rawVisitArgmaxMoves = $rawMoves
                    rawVisitArgmaxVisits = if ($null -ne $rawMax) { [int]$rawMax } else { 0 }
                    moveInfos = $infos
                })
            }
        }
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

$output = [ordered]@{
    schema = 'pure-udon-go.katago-root-trace.v1'
    generatedLocal = (Get-Date).ToString('o')
    corpusManifest = Split-Path $ManifestPath -Leaf
    corpusManifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    budgets = $Budgets
    positionCount = $positions.Count
    engine = 'KataGo analysis reportDuringSearch'
    deterministicOverrides = 'forDeterministicTesting=true,nnRandomize=false,numAnalysisThreads=1,numSearchThreads=1'
    records = $records.ToArray()
    stderr = $stderr
}
New-Item -ItemType Directory -Path (Split-Path $OutputPath) -Force | Out-Null
[IO.File]::WriteAllText($OutputPath, ($output | ConvertTo-Json -Depth 30))
Write-Output "PURE_UDON_GO_KATAGO_ROOT_TRACE_PASS positions=$($positions.Count) budgets=$($Budgets -join ',') records=$($records.Count) output=$OutputPath"
