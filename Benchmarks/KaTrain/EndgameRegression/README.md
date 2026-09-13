# Professional endgame consistency corpus

This corpus contains 32 deterministic 19x19 late-game positions extracted from
public professional SGF records.  It is a search-consistency benchmark, not a
claim that the recorded human move is optimal.

Source archive: [https://homepages.cwi.nl/~aeb/go/games/games.tgz](https://homepages.cwi.nl/~aeb/go/games/games.tgz)

Archive SHA-256: $archiveHash

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
[https://homepages.cwi.nl/~aeb/go/games/games/](https://homepages.cwi.nl/~aeb/go/games/games/)
