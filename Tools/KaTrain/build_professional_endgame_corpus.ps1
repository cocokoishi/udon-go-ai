param(
    [string]$RepositoryRoot = '',
    [string]$ArchivePath = '',
    [string]$SourceRoot = '',
    [string]$OutputRoot = '',
    [int]$TailPlies = 32,
    [int]$MinimumPrefixMoves = 128
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
{
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
$projectRoot = (Get-Item -LiteralPath $RepositoryRoot).Parent.Parent.FullName
if ([string]::IsNullOrWhiteSpace($ArchivePath))
{
    $ArchivePath = Join-Path $projectRoot 'Temp\PureUdonGoEndgameRun\games.tgz'
}
if ([string]::IsNullOrWhiteSpace($SourceRoot))
{
    $SourceRoot = Join-Path $projectRoot 'Temp\PureUdonGoEndgameRun\ProfessionalEndgameSource'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $RepositoryRoot 'Benchmarks\KaTrain\EndgameRegression'
}

$archiveUrl = 'https://homepages.cwi.nl/~aeb/go/games/games.tgz'
$expectedArchiveSha256 = 'e98ed32b0185e057371d8f7b2d8ddfbc88687442aea914582b30e31356114842'
$sourceBaseUrl = 'https://homepages.cwi.nl/~aeb/go/games/games/'

# The selection intentionally spans historical and modern professional records:
# four Go Seigen games, four Cho Chikun games, eight AlphaGo match/summit games,
# and sixteen AlphaGo Master games against different professionals.  We take a
# deterministic late-game prefix rather than the terminal result, so each case
# remains a legal position for the search comparison.
$entries = @(
    [ordered]@{ id='go-seigen-1966-01-05'; path='games/Go_Seigen/1966-01-05.sgf' },
    [ordered]@{ id='go-seigen-1973-07-01'; path='games/Go_Seigen/1973-07-01.sgf' },
    [ordered]@{ id='go-seigen-1951-04-11'; path='games/Go_Seigen/1951-04-11.sgf' },
    [ordered]@{ id='go-seigen-1971-09-12'; path='games/Go_Seigen/1971-09-12.sgf' },
    [ordered]@{ id='cho-chikun-2021-01-07'; path='games/Cho_Chikun/2021-01-07.sgf' },
    [ordered]@{ id='cho-chikun-2018-10-22'; path='games/Cho_Chikun/2018-10-22.sgf' },
    [ordered]@{ id='cho-chikun-2025-05-22'; path='games/Cho_Chikun/2025-05-22.sgf' },
    [ordered]@{ id='cho-chikun-2012-04-12b'; path='games/Cho_Chikun/2012-04-12b.sgf' },
    [ordered]@{ id='alphago-fanhui-1'; path='games/AlphaGo/FanHui/1.sgf' },
    [ordered]@{ id='alphago-fanhui-2'; path='games/AlphaGo/FanHui/2.sgf' },
    [ordered]@{ id='alphago-fanhui-5'; path='games/AlphaGo/FanHui/5.sgf' },
    [ordered]@{ id='alphago-lee-sedol-1'; path='games/AlphaGo/LeeSedol/1.sgf' },
    [ordered]@{ id='alphago-lee-sedol-2'; path='games/AlphaGo/LeeSedol/2.sgf' },
    [ordered]@{ id='alphago-lee-sedol-5'; path='games/AlphaGo/LeeSedol/5.sgf' },
    [ordered]@{ id='alphago-ke-jie-1'; path='games/AlphaGo/May2017/1.sgf' },
    [ordered]@{ id='alphago-ke-jie-2'; path='games/AlphaGo/May2017/2.sgf' },
    [ordered]@{ id='master-mi-yuting'; path='games/AlphaGo/NewYear2017/T29.sgf' },
    [ordered]@{ id='master-jiang-weijie'; path='games/AlphaGo/NewYear2017/F35.sgf' },
    [ordered]@{ id='master-chen-yaoye-t22'; path='games/AlphaGo/NewYear2017/T22.sgf' },
    [ordered]@{ id='master-meng-tailing-t09'; path='games/AlphaGo/NewYear2017/T09.sgf' },
    [ordered]@{ id='master-meng-tailing-f40'; path='games/AlphaGo/NewYear2017/F40.sgf' },
    [ordered]@{ id='master-chen-yaoye-t21'; path='games/AlphaGo/NewYear2017/T21.sgf' },
    [ordered]@{ id='master-park-junghwan-f48'; path='games/AlphaGo/NewYear2017/F48.sgf' },
    [ordered]@{ id='master-chen-yaoye-f55'; path='games/AlphaGo/NewYear2017/F55.sgf' },
    [ordered]@{ id='master-park-junghwan-t25'; path='games/AlphaGo/NewYear2017/T25.sgf' },
    [ordered]@{ id='master-an-sungjun'; path='games/AlphaGo/NewYear2017/F44.sgf' },
    [ordered]@{ id='master-park-junghwan-t20'; path='games/AlphaGo/NewYear2017/T20.sgf' },
    [ordered]@{ id='master-nie-weiping'; path='games/AlphaGo/NewYear2017/F54.sgf' },
    [ordered]@{ id='master-tuo-jiaxi'; path='games/AlphaGo/NewYear2017/F38.sgf' },
    [ordered]@{ id='master-gu-li'; path='games/AlphaGo/NewYear2017/F60.sgf' },
    [ordered]@{ id='master-ke-jie-t18'; path='games/AlphaGo/NewYear2017/T18.sgf' },
    [ordered]@{ id='master-park-junghwan-t24'; path='games/AlphaGo/NewYear2017/T24.sgf' }
)

if ($entries.Count -ne 32)
{
    throw "Professional endgame corpus selection must contain 32 entries, got $($entries.Count)."
}
if ($TailPlies -lt 8)
{
    throw 'TailPlies must leave a meaningful late-game position.'
}
if ($MinimumPrefixMoves -lt 64)
{
    throw 'MinimumPrefixMoves is too small for an endgame corpus.'
}

New-Item -ItemType Directory -Path $SourceRoot -Force | Out-Null
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

if (-not (Test-Path -LiteralPath $ArchivePath))
{
    throw "SGF archive is missing: $ArchivePath. Download $archiveUrl first."
}
$archiveHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveHash -ne $expectedArchiveSha256)
{
    throw "Unexpected SGF archive SHA-256 $archiveHash; expected $expectedArchiveSha256."
}

# Extract only the selected records.  No source archive is copied into the
# repository benchmark output; the manifest records its URL and hash instead.
$archivePaths = @($entries | ForEach-Object { $_.path })
tar -xzf $ArchivePath -C $SourceRoot @archivePaths

function Get-SgfProperty([string]$text, [string]$name)
{
    $match = [regex]::Match($text, '(?<![A-Za-z])' + [regex]::Escape($name) + '\[([^\]]*)\]')
    if ($match.Success) { return $match.Groups[1].Value }
    return ''
}

function Get-MainlineMoves([string]$text)
{
    $result = New-Object System.Collections.Generic.List[object]
    $depth = 0
    $i = 0
    while ($i -lt $text.Length)
    {
        $ch = $text[$i]
        if ($ch -eq '(')
        {
            $depth++
            $i++
            continue
        }
        if ($ch -eq ')')
        {
            if ($depth -gt 0) { $depth-- }
            $i++
            continue
        }
        if ($ch -eq ';' -and $depth -eq 1 -and $i + 2 -lt $text.Length)
        {
            $colour = $text[$i + 1]
            if (($colour -eq 'B' -or $colour -eq 'W') -and $text[$i + 2] -eq '[')
            {
                $end = $i + 3
                while ($end -lt $text.Length -and $text[$end] -ne ']')
                {
                    if ($text[$end] -eq '\\' -and $end + 1 -lt $text.Length) { $end++ }
                    $end++
                }
                if ($end -ge $text.Length)
                {
                    throw 'unterminated SGF move property'
                }
                $coordinate = $text.Substring($i + 3, $end - ($i + 3))
                $result.Add([ordered]@{ colour=[string]$colour; coordinate=$coordinate })
                $i = $end + 1
                continue
            }
        }
        if ($ch -eq '[')
        {
            $end = $i + 1
            while ($end -lt $text.Length -and $text[$end] -ne ']')
            {
                if ($text[$end] -eq '\\' -and $end + 1 -lt $text.Length) { $end++ }
                $end++
            }
            $i = [Math]::Min($text.Length, $end + 1)
            continue
        }
        $i++
    }
    return $result.ToArray()
}

function Convert-SgfCoordinate([string]$coordinate)
{
    if ([string]::IsNullOrEmpty($coordinate)) { return -1 }
    if ($coordinate.Length -ne 2) { throw "Unsupported SGF coordinate '$coordinate'." }
    $column = [int][char]$coordinate[0] - [int][char]'a'
    $row = [int][char]$coordinate[1] - [int][char]'a'
    if ($column -lt 0 -or $column -ge 19 -or $row -lt 0 -or $row -ge 19)
    {
        throw "Out-of-range 19x19 SGF coordinate '$coordinate'."
    }
    return $row * 19 + $column
}

function Get-MoveHash([int[]]$moves)
{
    $payload = ($moves -join ',')
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $sha = [Security.Cryptography.SHA256]::Create()
    try
    {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

$positions = New-Object System.Collections.Generic.List[object]
foreach ($entry in $entries)
{
    $sourceFile = Join-Path $SourceRoot ($entry.path -replace '/', '\\')
    if (-not (Test-Path -LiteralPath $sourceFile))
    {
        throw "Selected SGF was not extracted: $sourceFile"
    }
    $raw = Get-Content -LiteralPath $sourceFile -Raw
    $mainline = @(Get-MainlineMoves $raw)
    if ($mainline.Count -lt ($MinimumPrefixMoves + 1))
    {
        throw "$($entry.id) has only $($mainline.Count) moves; cannot create a late-game prefix."
    }
    if ((Get-SgfProperty $raw 'SZ') -and (Get-SgfProperty $raw 'SZ') -ne '19')
    {
        throw "$($entry.id) is not a 19x19 record."
    }
    if ((Get-SgfProperty $raw 'AB') -or (Get-SgfProperty $raw 'AW') -or (Get-SgfProperty $raw 'HA'))
    {
        throw "$($entry.id) contains setup/handicap stones; this corpus requires a clean alternating game."
    }
    if ($mainline[0].colour -ne 'B')
    {
        throw "$($entry.id) does not begin with a black move."
    }
    for ($i = 0; $i -lt $mainline.Count; $i++)
    {
        $expected = if (($i % 2) -eq 0) { 'B' } else { 'W' }
        if ($mainline[$i].colour -ne $expected)
        {
            throw "$($entry.id) has a non-alternating mainline at ply $($i + 1)."
        }
    }

    $prefixCount = $mainline.Count - $TailPlies
    if ($prefixCount -lt $MinimumPrefixMoves) { $prefixCount = $MinimumPrefixMoves }
    if ($prefixCount -ge $mainline.Count) { $prefixCount = $mainline.Count - 1 }
    $moves = New-Object System.Collections.Generic.List[int]
    for ($i = 0; $i -lt $prefixCount; $i++)
    {
        [void]$moves.Add((Convert-SgfCoordinate $mainline[$i].coordinate))
    }
    $relativePath = $entry.path.Substring(6)
    $positions.Add([ordered]@{
        id = $entry.id
        sourceArchivePath = $entry.path
        sourceUrl = $sourceBaseUrl + $relativePath
        sourceFileSha256 = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant()
        black = Get-SgfProperty $raw 'PB'
        blackRank = Get-SgfProperty $raw 'BR'
        white = Get-SgfProperty $raw 'PW'
        whiteRank = Get-SgfProperty $raw 'WR'
        date = Get-SgfProperty $raw 'DT'
        result = Get-SgfProperty $raw 'RE'
        originalKomi = Get-SgfProperty $raw 'KM'
        originalMoveCount = $mainline.Count
        prefixMoveCount = $prefixCount
        tailPlies = $mainline.Count - $prefixCount
        sideToMove = if (($prefixCount % 2) -eq 0) { 'B' } else { 'W' }
        moveHashSha256 = Get-MoveHash $moves.ToArray()
        moves = $moves.ToArray()
    })
}

$manifest = [ordered]@{
    schema = 'pure-udon-go.professional-endgame-corpus.v1'
    generatedLocal = (Get-Date).ToString('o')
    boardSize = 19
    rules = 'tromp-taylor positional-superko area komi-7.5'
    tailPliesRequested = $TailPlies
    minimumPrefixMoves = $MinimumPrefixMoves
    positionCount = $positions.Count
    budgets = @(4, 32, 100)
    source = [ordered]@{
        archiveUrl = $archiveUrl
        archiveSha256 = $archiveHash
        archiveLicenseNote = 'The CWI archive page provides the SGF download and attribution; no separate redistribution license is asserted here. Keep source URLs and hashes with benchmark results.'
        archiveRetrievedLocal = (Get-Date).ToString('o')
    }
    selection = [ordered]@{
        composition = '4 Go Seigen + 4 Cho Chikun + 8 AlphaGo match/summit + 16 AlphaGo Master professional games'
        positionRule = 'main-line prefix ending before the recorded game tail; no setup or handicap stones'
        noRuntimeRandomization = $true
    }
    positions = $positions.ToArray()
}

$manifestPath = Join-Path $OutputRoot 'professional-endgames.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 30))
$readmePath = Join-Path $OutputRoot 'README.md'
$readme = @"
# Professional endgame consistency corpus

This corpus contains 32 deterministic 19x19 late-game positions extracted from
public professional SGF records.  It is a search-consistency benchmark, not a
claim that the recorded human move is optimal.

Source archive: [$archiveUrl]($archiveUrl)

Archive SHA-256: `$archiveHash`

The manifest records each source path, source URL, player metadata, original
length, exact prefix length, side to move, source-file hash, and SHA-256 of the
flattened move prefix.  The prefix is replayed by the real Udon Go rules path
before each search.  The benchmark uses fixed Tromp-Taylor positional-superko,
area scoring and komi 7.5 for both implementations; the historical SGF KM is
retained as provenance only.

The active parity audit queries each position at exactly 4 and 32 visits in
Udon ClientSim and in the same shipped KataGo model. Older 100-visit artifacts
are retained as historical evidence only and are excluded from the focused
report. The comparison reports raw root-visit argmax (tie-aware), KataGo
play-selection ordering, sampled product moves, candidate overlap/rank, and
regret against a fixed high-visit KataGo reference. It does not claim
bit-for-bit PUCT identity, Elo, or complete-game strength.

The CWI index lists its professional-player and AlphaGo collections here:
[$sourceBaseUrl]($sourceBaseUrl)
"@
[IO.File]::WriteAllText($readmePath, $readme)

Write-Output "PURE_UDON_GO_ENDGAME_CORPUS_BUILD_PASS positions=$($positions.Count) budgets=4,32,100 manifest=$manifestPath archiveSha256=$archiveHash"
