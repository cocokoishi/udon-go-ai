#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

public static class GoFeatureOracleDump
{
    [Serializable]
    private sealed class Fixture
    {
        public string name;
        public string rules;
        public int moveCount;
        public int sideToMove;
        public int gameState;
        public int consecutivePasses;
        public int koLoc;
        public bool positionalSuperko;
        public bool multiStoneSuicideLegal;
        public bool areaScoring;
        public string[] moves;
        public int[] board;
        public int[] previousBoard1;
        public int[] previousBoard2;
        public int[] recentMoveLoc;
        public int[] recentMovePla;
        public bool[] superkoBanned;
        public float[] spatial;
        public float[] global;
    }

    [Serializable]
    private sealed class FixtureSet
    {
        public Fixture[] fixtures;
    }

    private sealed class CaseDefinition
    {
        public string name;
        public string rules;
        public bool positionalSuperko = true;
        public bool multiStoneSuicideLegal = true;
        public bool areaScoring = true;
        public string[] moves;
        public int randomSeed;
        public int randomTargetMoves;
        public bool randomIncludePasses;
    }

    private static readonly CaseDefinition[] Cases =
    {
        new CaseDefinition { name = "empty-tromp", rules = "Tromp-Taylor", moves = new string[0] },
        new CaseDefinition { name = "opening-7-tromp", rules = "Tromp-Taylor", moves = new[] { "D16", "Q4", "Q16", "D4", "C17", "R3", "R17" } },
        new CaseDefinition { name = "fight-15-tromp", rules = "Tromp-Taylor", moves = new[] { "D16", "Q4", "Q16", "D4", "C17", "R3", "R17", "C3", "E16", "P4", "Q15", "D5", "F17", "O3", "R14" } },
        new CaseDefinition { name = "ladder-19-tromp", rules = "Tromp-Taylor", moves = new[] { "D16", "Q4", "Q16", "D4", "C17", "R3", "R17", "C3", "E16", "P4", "Q15", "D5", "F17", "O3", "R14", "G16", "N4", "Q13", "D7" } },
        new CaseDefinition { name = "capture-9-tromp", rules = "Tromp-Taylor", moves = new[] { "D4", "C4", "T19", "D3", "C3", "T1", "C5", "A1", "B4" } },
        new CaseDefinition { name = "pass-history-tromp", rules = "Tromp-Taylor", moves = new[] { "D16", "PASS" } },
        new CaseDefinition { name = "double-pass-tromp", rules = "Tromp-Taylor", moves = new[] { "PASS", "PASS" } },
        new CaseDefinition { name = "empty-chinese", rules = "Chinese", positionalSuperko = false, multiStoneSuicideLegal = false, moves = new string[0] },
        new CaseDefinition { name = "random-32-tromp", rules = "Tromp-Taylor", randomSeed = 17391, randomTargetMoves = 32 },
        new CaseDefinition { name = "random-64-tromp-pass", rules = "Tromp-Taylor", randomSeed = 48127, randomTargetMoves = 64, randomIncludePasses = true },
        new CaseDefinition { name = "random-96-tromp", rules = "Tromp-Taylor", randomSeed = 90211, randomTargetMoves = 96 },
        new CaseDefinition { name = "random-144-tromp-pass", rules = "Tromp-Taylor", randomSeed = 137821, randomTargetMoves = 144, randomIncludePasses = true },
        new CaseDefinition { name = "random-32-chinese", rules = "Chinese", positionalSuperko = false, multiStoneSuicideLegal = false, randomSeed = 22117, randomTargetMoves = 32 },
        new CaseDefinition { name = "random-64-chinese-pass", rules = "Chinese", positionalSuperko = false, multiStoneSuicideLegal = false, randomSeed = 66301, randomTargetMoves = 64, randomIncludePasses = true },
        new CaseDefinition { name = "random-96-chinese", rules = "Chinese", positionalSuperko = false, multiStoneSuicideLegal = false, randomSeed = 104729, randomTargetMoves = 96 },
        new CaseDefinition { name = "random-144-chinese-pass", rules = "Chinese", positionalSuperko = false, multiStoneSuicideLegal = false, randomSeed = 191939, randomTargetMoves = 144, randomIncludePasses = true }
    };

    public static void Run()
    {
        GameObject gameObject = null;
        GameObject encoderObject = null;
        try
        {
            Networking.LocalPlayer = null;
            Networking.LocalPlayerOwnsObjects = true;
            Networking.IsMaster = true;
            gameObject = new GameObject("GoFeatureOracleDumpGame");
            PureUdonGo.GoGame game = gameObject.AddComponent<PureUdonGo.GoGame>();
            encoderObject = new GameObject("GoFeatureOracleDumpEncoder");
            PureUdonGo.GoFeatureEncoder encoder = encoderObject.AddComponent<PureUdonGo.GoFeatureEncoder>();
            Fixture[] fixtures = new Fixture[Cases.Length];

            game.Start();
            for (int i = 0; i < Cases.Length; i++)
            {
                CaseDefinition definition = Cases[i];
                game.positionalSuperko = definition.positionalSuperko;
                game.multiStoneSuicideLegal = definition.multiStoneSuicideLegal;
                game.areaScoring = definition.areaScoring;
                game.blackIsAI = false;
                game.whiteIsAI = false;
                game.starter = PureUdonGo.GoGame.BLACK;
                game.matchStarted = true;
                game.RequestNewGame();
                string[] moves = definition.moves;
                if (definition.randomTargetMoves > 0)
                    moves = GenerateRandomMoves(game, definition.randomSeed, definition.randomTargetMoves, definition.randomIncludePasses);
                for (int moveIndex = 0; moveIndex < moves.Length && definition.randomTargetMoves <= 0; moveIndex++)
                {
                    string move = moves[moveIndex];
                    bool accepted;
                    if (move == "PASS")
                    {
                        int before = game.moveCount;
                        game.Pass();
                        accepted = game.moveCount == before + 1;
                    }
                    else
                    {
                        accepted = game.TryPlay(ParseCoordinate(move));
                    }
                    if (!accepted)
                        throw new InvalidOperationException(definition.name + " rejected move " + move + ": " + game.lastActionText);
                }

                if (definition.randomTargetMoves > 0 && moves.Length != definition.randomTargetMoves)
                    throw new InvalidOperationException(definition.name + " generated " + moves.Length + " moves, expected " + definition.randomTargetMoves);

                bool[] banned = new bool[PureUdonGo.GoGame.AREA];
                for (int loc = 0; loc < banned.Length; loc++)
                    banned[loc] = game.IsSuperkoBanned(loc);
                float[] spatial = new float[PureUdonGo.GoFeatureEncoder.SPATIAL_COUNT];
                float[] global = new float[PureUdonGo.GoFeatureEncoder.GLOBAL_CHANNELS];
                if (!encoder.EncodeState(game.board, game.previousBoard1, game.previousBoard2,
                    game.recentMoveLoc, game.recentMovePla, game.sideToMove, game.koLoc,
                    game.komiTimes2, game.positionalSuperko, game.multiStoneSuicideLegal,
                    game.areaScoring, game.consecutivePasses, game.gameState, banned, spatial, global))
                    throw new InvalidOperationException(definition.name + " feature encoding failed: " + encoder.lastError);

                fixtures[i] = new Fixture
                {
                    name = definition.name,
                    rules = definition.rules,
                    moveCount = game.moveCount,
                    sideToMove = game.sideToMove,
                    gameState = game.gameState,
                    consecutivePasses = game.consecutivePasses,
                    koLoc = game.koLoc,
                    positionalSuperko = game.positionalSuperko,
                    multiStoneSuicideLegal = game.multiStoneSuicideLegal,
                    areaScoring = game.areaScoring,
                    moves = Copy(moves),
                    board = Copy(game.board),
                    previousBoard1 = Copy(game.previousBoard1),
                    previousBoard2 = Copy(game.previousBoard2),
                    recentMoveLoc = Copy(game.recentMoveLoc),
                    recentMovePla = Copy(game.recentMovePla),
                    superkoBanned = banned,
                    spatial = spatial,
                    global = global
                };
            }

            string root = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(root, "go-feature-oracle-fixtures.json");
            File.WriteAllText(path, JsonUtility.ToJson(new FixtureSet { fixtures = fixtures }, true));
            Debug.Log("PURE_UDON_GO_FEATURE_ORACLE_DUMP_PASS cases=" + fixtures.Length + " path=" + path);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("PURE_UDON_GO_FEATURE_ORACLE_DUMP_FAIL exception=" + exception);
            EditorApplication.Exit(6);
        }
        finally
        {
            if (encoderObject != null)
                UnityEngine.Object.DestroyImmediate(encoderObject);
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);
            Networking.LocalPlayer = null;
            Networking.LocalPlayerOwnsObjects = true;
            Networking.IsMaster = true;
        }
    }

    private static int[] Copy(int[] source)
    {
        int[] copy = new int[source.Length];
        for (int i = 0; i < source.Length; i++) copy[i] = source[i];
        return copy;
    }

    private static string[] Copy(string[] source)
    {
        string[] copy = new string[source == null ? 0 : source.Length];
        if (source != null)
            for (int i = 0; i < source.Length; i++) copy[i] = source[i];
        return copy;
    }

    private static string[] GenerateRandomMoves(PureUdonGo.GoGame game, int seed, int targetMoves, bool includePasses)
    {
        string[] moves = new string[targetMoves];
        int state = seed;
        bool previousWasPass = false;
        for (int moveIndex = 0; moveIndex < targetMoves; moveIndex++)
        {
            bool accepted = false;
            if (includePasses && !previousWasPass && NextRandom(ref state) % 19 == 0)
            {
                int before = game.moveCount;
                game.Pass();
                accepted = game.moveCount == before + 1 && game.gameState == PureUdonGo.GoGame.STATE_PLAYING;
                if (accepted)
                {
                    moves[moveIndex] = "PASS";
                    previousWasPass = true;
                }
            }
            if (!accepted)
            {
                for (int attempt = 0; attempt < PureUdonGo.GoGame.AREA * 2; attempt++)
                {
                    int loc = NextRandom(ref state) % PureUdonGo.GoGame.AREA;
                    if (!game.TryPlay(loc)) continue;
                    moves[moveIndex] = FormatCoordinate(loc);
                    accepted = true;
                    previousWasPass = false;
                    break;
                }
            }
            if (!accepted)
                throw new InvalidOperationException("random move generation exhausted legal candidates at ply " + moveIndex);
        }
        return moves;
    }

    private static int NextRandom(ref int state)
    {
        unchecked { state = state * 1103515245 + 12345; }
        return state & 0x7fffffff;
    }

    private static string FormatCoordinate(int loc)
    {
        const string columns = "ABCDEFGHJKLMNOPQRST";
        return columns.Substring(loc % PureUdonGo.GoGame.SIZE, 1) +
            (PureUdonGo.GoGame.SIZE - loc / PureUdonGo.GoGame.SIZE);
    }

    private static int ParseCoordinate(string value)
    {
        const string columns = "ABCDEFGHJKLMNOPQRST";
        if (value == null || value.Length < 2) return -1;
        int x = columns.IndexOf(char.ToUpperInvariant(value[0]));
        if (x < 0) return -1;
        int row;
        if (!int.TryParse(value.Substring(1), out row)) return -1;
        int y = PureUdonGo.GoGame.SIZE - row;
        return x >= 0 && y >= 0 && y < PureUdonGo.GoGame.SIZE ? y * PureUdonGo.GoGame.SIZE + x : -1;
    }
}
#endif
