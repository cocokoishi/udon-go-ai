#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;

public static class GoAiVsAiGameCheck
{
    private const int SeedTargetMoves = 340;
    // Correct KataGo superko masking changes the calibrated pass policy on
    // this dense seed. Keep enough AI turns for a genuine double-pass finish
    // instead of treating an arbitrary 100-move cutoff as a completed game.
    private const int MaxAiMoves = 160;
    private static int failures;
    private static int frames;
    private static int lastMoveCount;
    private static int lastRevision;
    private static PureUdonGo.GoGame game;
    private static PureUdonGo.GoAiController ai;
    private static PureUdonGo.GoDifficultyProfile blackDifficulty;
    private static PureUdonGo.GoDifficultyProfile whiteDifficulty;
    private static PureUdonGo.GoGpuNeuralRuntime runtime;

    public static void Run()
    {
        failures = 0;
        frames = 0;
        lastMoveCount = 0;
        lastRevision = 0;
        try
        {
            Networking.LocalPlayer = MakeOwner();
            Networking.LocalPlayerOwnsObjects = true;
            Networking.IsMaster = true;
            EditorSceneManager.OpenScene(PureUdonGo.Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            GameObject root = FindGeneratedRoot();
            Expect(root != null, "generated root");
            if (root == null)
            {
                Finish();
                return;
            }

            game = root.GetComponentInChildren<PureUdonGo.GoGame>(true);
            ai = root.GetComponentInChildren<PureUdonGo.GoAiController>(true);
            runtime = root.GetComponentInChildren<PureUdonGo.GoGpuNeuralRuntime>(true);
            blackDifficulty = game == null ? null : game.blackDifficulty;
            whiteDifficulty = game == null ? null : game.whiteDifficulty;
            Expect(game != null && ai != null && runtime != null, "generated AIvAI references");
            Expect(blackDifficulty != null && whiteDifficulty != null, "both AIvAI profiles");
            if (game == null || ai == null || runtime == null || blackDifficulty == null || whiteDifficulty == null)
            {
                Finish();
                return;
            }

            game.Start();
            blackDifficulty.Start();
            if (whiteDifficulty != blackDifficulty)
                whiteDifficulty.Start();
            ai.Start();

            game.blackIsAI = false;
            game.whiteIsAI = false;
            game.blackPlayerId = -1;
            game.whitePlayerId = -1;
            game.blackPlayerName = "";
            game.whitePlayerName = "";
            game.starter = PureUdonGo.GoGame.BLACK;
            game.humanSide = PureUdonGo.GoGame.BLACK;
            game.matchStarted = true;
            Networking.LocalPlayer = null;
            game.RequestNewGame();

            int seededMoves = SeedDensePosition();
            int emptyPoints = CountEmptyPoints();
            Expect(seededMoves >= SeedTargetMoves - 10, "dense legal seed position");
            Expect(game.gameState == PureUdonGo.GoGame.STATE_PLAYING, "dense seed remains playable");

            Networking.LocalPlayer = MakeOwner();
            game.blackIsAI = true;
            game.whiteIsAI = true;
            game.matchStarted = true;
            blackDifficulty.ApplyPreset(PureUdonGo.GoDifficultyProfile.BEGINNER);
            whiteDifficulty.ApplyPreset(PureUdonGo.GoDifficultyProfile.BEGINNER);
            game.RequestStartMatch();
            ai.SyncControllerMirrorFromGame();
            ai.aiMoves = 0;
            ai.controllerState = PureUdonGo.GoAiController.STATE_IDLE;
            ai.lastError = "";
            lastMoveCount = game.moveCount;
            lastRevision = game.revision;
            Expect(game.blackIsAI && game.whiteIsAI && game.matchStarted, "AIvAI match started");
            Debug.Log("PURE_UDON_GO_AIVAI_START preset=Beginner visits=32 seedMoves=" +
                seededMoves + " emptyPoints=" + emptyPoints + " maxMoves=" + MaxAiMoves);
            EditorApplication.update += Pump;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_AIVAI_FAIL exception=" + exception);
            Finish();
        }
    }

    private static void Pump()
    {
        frames++;
        if (ai == null || game == null)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_AIVAI_FAIL missing runtime references");
            Finish();
            return;
        }

        int beforeMoveCount = game.moveCount;
        int movingSide = game.sideToMove;
        ai.Tick();
        EditorApplication.QueuePlayerLoopUpdate();

        if (game.moveCount != beforeMoveCount)
        {
            int move = game.lastMove;
            string coordinate = Coordinate(move);
            int visits = ai.search == null ? -1 : ai.search.visitsCompleted;
            int nodes = ai.search == null ? -1 : ai.search.treeNodeCount;
            int edges = ai.search == null ? -1 : ai.search.treeEdgeCount;
            int rootVisits = ai.search == null ? 0 : ai.search.GetRootVisitsForMove(move);
            float neuralScore = ai.search == null ? float.NaN : ai.search.rootNeuralScoreMean;
            float neuralOwnership = ai.search == null ? float.NaN : ai.search.rootNeuralOwnershipMean;
            Debug.Log("PURE_UDON_GO_AIVAI_MOVE number=" + game.moveCount +
                " side=" + SideName(movingSide) + " move=" + coordinate +
                " visits=" + visits + " rootVisits=" + rootVisits +
                " nodes=" + nodes + " edges=" + edges +
                " neuralScore=" + neuralScore + " neuralOwnership=" + neuralOwnership +
                " revision=" + game.revision + " frames=" + frames +
                " action=" + game.lastActionText);
            Expect(move == PureUdonGo.GoGame.PASS || (move >= 0 && move < PureUdonGo.GoGame.AREA),
                "AIvAI move domain");
            Expect(visits == 32, "AIvAI exact Beginner visits");
            Expect(rootVisits > 0, "AIvAI selected move has backed-up root visits");
            Expect(ai.search != null && ai.search.rootNeuralOutputRevision > 0,
                "AIvAI root neural telemetry revision");
            Expect(!float.IsNaN(neuralScore) && !float.IsInfinity(neuralScore),
                "AIvAI score output finite");
            Expect(!float.IsNaN(neuralOwnership) && !float.IsInfinity(neuralOwnership),
                "AIvAI ownership output finite");
            Expect(game.revision > lastRevision, "AIvAI revision advanced");
            lastMoveCount = game.moveCount;
            lastRevision = game.revision;
        }

        if (game.gameState != PureUdonGo.GoGame.STATE_PLAYING)
        {
            Debug.Log("PURE_UDON_GO_AIVAI_RESULT state=" + game.gameState +
                " winner=" + game.winner + " moves=" + game.moveCount +
                " blackArea=" + game.finalBlackArea + " whiteArea=" + game.finalWhiteArea +
                " score=" + game.finalWhiteMinusBlackScore);
            Finish();
            return;
        }

        Expect(game.moveCount == lastMoveCount, "AIvAI move accounting");
        if (ai.controllerState == PureUdonGo.GoAiController.STATE_ERROR)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_AIVAI_FAIL controller=" + ai.lastError +
                " phase=" + (ai.search == null ? -1 : ai.search.phase));
            Finish();
            return;
        }
        if (ai.aiMoves >= MaxAiMoves)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_AIVAI_FAIL incomplete-game aiMoves=" + ai.aiMoves +
                " totalMoves=" + game.moveCount + " frames=" + frames);
            Finish();
            return;
        }
        if (frames > 500000)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_AIVAI_FAIL timeout moves=" + game.moveCount +
                " phase=" + (ai.search == null ? -1 : ai.search.phase) +
                " reader=" + (ai.reader == null ? -1 : ai.reader.readbackState));
            Finish();
        }
    }

    private static string SideName(int side)
    {
        return side == PureUdonGo.GoGame.BLACK ? "BLACK" : "WHITE";
    }

    private static int SeedDensePosition()
    {
        int seeded = 0;
        for (int round = 0; round < PureUdonGo.GoGame.AREA && seeded < SeedTargetMoves; round++)
        {
            for (int offset = 0; offset < PureUdonGo.GoGame.AREA && seeded < SeedTargetMoves; offset++)
            {
                int loc = (round * 97 + offset * 37 + 11) % PureUdonGo.GoGame.AREA;
                if (game.TryPlay(loc))
                    seeded++;
            }
        }
        return seeded;
    }

    private static int CountEmptyPoints()
    {
        int empty = 0;
        for (int i = 0; i < PureUdonGo.GoGame.AREA; i++)
            if (game.board[i] == PureUdonGo.GoGame.EMPTY)
                empty++;
        return empty;
    }

    private static string Coordinate(int loc)
    {
        if (loc == PureUdonGo.GoGame.PASS)
            return "PASS";
        if (loc < 0 || loc >= PureUdonGo.GoGame.AREA)
            return "NONE";
        string columns = "ABCDEFGHJKLMNOPQRST";
        return columns.Substring(loc % PureUdonGo.GoGame.SIZE, 1) +
            (PureUdonGo.GoGame.SIZE - loc / PureUdonGo.GoGame.SIZE);
    }

    private static GameObject FindGeneratedRoot()
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].GetComponent<PureUdonGo.GoGeneratedWorld>() != null)
                return roots[i];
        return null;
    }

    private static VRCPlayerApi MakeOwner()
    {
        VRCPlayerApi owner = new VRCPlayerApi();
        owner.playerId = 1;
        owner.displayName = "AIvAI Benchmark Owner";
        owner.isLocal = true;
        return owner;
    }

    private static void Expect(bool condition, string message)
    {
        if (condition)
            return;
        failures++;
        Debug.LogError("PURE_UDON_GO_AIVAI_FAIL " + message);
    }

    private static void Finish()
    {
        EditorApplication.update -= Pump;
        if (runtime != null)
            runtime.ReleaseAllResources();
        Networking.LocalPlayer = null;
        Networking.LocalPlayerOwnsObjects = true;
        Networking.IsMaster = true;
        if (failures == 0)
        {
            Debug.Log("PURE_UDON_GO_AIVAI_PASS failures=0");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("PURE_UDON_GO_AIVAI_FAIL count=" + failures);
            EditorApplication.Exit(6);
        }
    }
}
#endif
