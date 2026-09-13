param(
    [string]$RepositoryRoot = '',
    [string]$FixturePath = 'E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoUnityTest\go-feature-oracle-fixtures.json',
    [string]$OutputRoot = 'E:\UnityPRJS\ShaderGPT_neoversion\Temp\FeatureOracle\2026-08-31',
    [string]$KaTrainRoot = 'E:\UnityPRJS\ShaderGPT_neoversion\KaTrain',
    [string]$ModelPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($ModelPath))
{
    $ModelPath = Join-Path $RepositoryRoot '.KataGO\g170e-b10c128-s1141046784-d204142634.bin.gz'
}

$sourceKataGoRoot = Join-Path $KaTrainRoot '_internal\katrain\KataGo'
$sourceExecutable = Join-Path $sourceKataGoRoot 'katago.exe'
$oracleTempRoot = 'E:\UnityPRJS\ShaderGPT_neoversion\Temp\KaTrainOracle\KataGo'
$oracleExecutable = Join-Path $oracleTempRoot 'katago.exe'
$oracleConfig = Join-Path $oracleTempRoot 'analysis_config.cfg'
foreach ($required in @($FixturePath, $sourceExecutable, $ModelPath, $oracleExecutable, $oracleConfig))
{
    if (-not (Test-Path -LiteralPath $required))
    {
        throw "Feature oracle input is missing: $required"
    }
}

$fixtureSet = Get-Content -Raw -LiteralPath $FixturePath | ConvertFrom-Json
$legacyMovesByName = @{
    'empty-tromp' = @()
    'opening-7-tromp' = @('D16', 'Q4', 'Q16', 'D4', 'C17', 'R3', 'R17')
    'fight-15-tromp' = @('D16', 'Q4', 'Q16', 'D4', 'C17', 'R3', 'R17', 'C3', 'E16', 'P4', 'Q15', 'D5', 'F17', 'O3', 'R14')
    'ladder-19-tromp' = @('D16', 'Q4', 'Q16', 'D4', 'C17', 'R3', 'R17', 'C3', 'E16', 'P4', 'Q15', 'D5', 'F17', 'O3', 'R14', 'G16', 'N4', 'Q13', 'D7')
    'capture-9-tromp' = @('D4', 'C4', 'T19', 'D3', 'C3', 'T1', 'C5', 'A1', 'B4')
    'pass-history-tromp' = @('D16', 'PASS')
    'double-pass-tromp' = @('PASS', 'PASS')
    'empty-chinese' = @()
}

function Convert-ToSgfCoordinate([string]$coordinate)
{
    if ($coordinate -eq 'PASS') { return '' }
    $columns = 'ABCDEFGHJKLMNOPQRST'
    $x = $columns.IndexOf($coordinate.Substring(0, 1).ToUpperInvariant())
    $row = [int]$coordinate.Substring(1)
    $y = 19 - $row
    if ($x -lt 0 -or $y -lt 0 -or $y -ge 19) { throw "Invalid Go coordinate: $coordinate" }
    return ([char]([int][char]'a' + $x)).ToString() + ([char]([int][char]'a' + $y)).ToString()
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$oracleLog = Join-Path $OutputRoot 'katago-feature-oracle.log'
$manifestRows = New-Object System.Collections.Generic.List[object]
$logLines = New-Object System.Collections.Generic.List[string]

foreach ($fixture in @($fixtureSet.fixtures))
{
    if ($null -ne $fixture.moves)
    {
        $moves = @($fixture.moves)
    }
    elseif ($legacyMovesByName.ContainsKey($fixture.name))
    {
        $moves = @($legacyMovesByName[$fixture.name])
    }
    else
    {
        throw "No SGF move definition for fixture $($fixture.name)."
    }
    if ($moves.Count -ne [int]$fixture.moveCount)
    {
        throw "$($fixture.name): fixture move count $($fixture.moveCount) does not match SGF definition $($moves.Count)."
    }

    $sgf = '(;GM[1]FF[4]CA[UTF-8]SZ[19]KM[7.5]RU[' + $fixture.rules + ']PB[PureUdonGo]PW[KataGo]'
    for ($i = 0; $i -lt $moves.Count; $i++)
    {
        $player = if (($i % 2) -eq 0) { 'B' } else { 'W' }
        $sgf += ';' + $player + '[' + (Convert-ToSgfCoordinate $moves[$i]) + ']'
    }
    $sgf += ')'
    $sgfPath = Join-Path $OutputRoot ($fixture.name + '.sgf')
    $npzPath = Join-Path $OutputRoot ($fixture.name + '.npz')
    [IO.File]::WriteAllText($sgfPath, $sgf)
    if (Test-Path -LiteralPath $npzPath)
    {
        throw "Refusing to overwrite an existing oracle file: $npzPath. Use a new OutputRoot for a fresh run."
    }

    $arguments = @(
        'evalsgf', '-model', $ModelPath, '-config', $oracleConfig,
        '-override-config', 'forDeterministicTesting=true,nnRandomize=false,numAnalysisThreads=1,numSearchThreads=1',
        $sgfPath, '-move-num', ([string]$fixture.moveCount), '-visits', '1', '-threads', '1',
        '-dump-npz-input-to', $npzPath
    )
    # KataGo writes progress separators to stderr even on success. Temporarily
    # relax PowerShell's terminating error policy around this native process so
    # stderr is captured as diagnostic output instead of aborting the corpus.
    $savedErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try
    {
        $output = (& $oracleExecutable @arguments 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    $logLines.Add(('===== ' + $fixture.name + ' exit=' + $exitCode + ' ====='))
    $logLines.Add($output.TrimEnd())
    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $npzPath))
    {
        throw "KataGo feature oracle failed for $($fixture.name), exit=$exitCode."
    }
    $manifestRows.Add([ordered]@{
        name = $fixture.name
        rules = $fixture.rules
        moveCount = [int]$fixture.moveCount
        sgf = [IO.Path]::GetFileName($sgfPath)
        npz = [IO.Path]::GetFileName($npzPath)
        npzSha256 = (Get-FileHash -LiteralPath $npzPath -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

[IO.File]::WriteAllText($oracleLog, ($logLines -join [Environment]::NewLine) + [Environment]::NewLine)
$manifest = [ordered]@{
    schema = 'pure-udon-go.katago-feature-oracle-manifest.v1'
    status = 'KATAGO_EXECUTED'
    fixturePath = $FixturePath
    oracleExecutable = $sourceExecutable
    oracleExecutableSha256 = (Get-FileHash -LiteralPath $sourceExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    modelPath = $ModelPath
    modelSha256 = (Get-FileHash -LiteralPath $ModelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    configPath = Join-Path $KaTrainRoot '_internal\katrain\KataGo\analysis_config.cfg'
    configSha256 = (Get-FileHash -LiteralPath (Join-Path $KaTrainRoot '_internal\katrain\KataGo\analysis_config.cfg') -Algorithm SHA256).Hash.ToLowerInvariant()
    rules = 'Tromp-Taylor for production cases; Chinese control case'
    rows = $manifestRows
    oracleLog = [IO.Path]::GetFileName($oracleLog)
}
[IO.File]::WriteAllText((Join-Path $OutputRoot 'manifest.json'), ($manifest | ConvertTo-Json -Depth 10))
Write-Output "PURE_UDON_GO_KATAGO_FEATURE_ORACLE_PASS cases=$($manifestRows.Count) output=$OutputRoot"
