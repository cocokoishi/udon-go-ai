#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

public static class GoRulesSuperkoRegressionCheck
{
    private static int failures;

    public static void Run()
    {
        GameObject gameObject = null;
        GameObject searchObject = null;
        try
        {
            failures = 0;
            Networking.LocalPlayer = null;
            Networking.LocalPlayerOwnsObjects = true;
            Networking.IsMaster = true;

            gameObject = new GameObject("GoRulesSuperkoRegressionGame");
            PureUdonGo.GoGame game = gameObject.AddComponent<PureUdonGo.GoGame>();
            game.Start();
            game.positionalSuperko = true;
            game.areaScoring = true;
            game.multiStoneSuicideLegal = true;
            game.blackIsAI = false;
            game.whiteIsAI = false;
            game.matchStarted = true;
            game.RequestNewGame();

            string[] moves = { "D4", "C4", "T19", "D3", "C3", "T1", "C5", "A1", "B4" };
            for (int i = 0; i < moves.Length; i++)
                Expect(game.TryPlay(ParseCoordinate(moves[i])), "play " + moves[i]);

            int singletonSuicide = ParseCoordinate("C4");
            Expect(game.board[singletonSuicide] == PureUdonGo.GoGame.EMPTY, "captured point is empty");
            Expect(!game.IsLegalMove(singletonSuicide), "singleton suicide is illegal");
            Expect(!game.IsSuperkoBanned(singletonSuicide), "singleton suicide is not superko-banned");
            int moveCountBefore = game.moveCount;
            int revisionBefore = game.revision;
            Expect(!game.TryPlay(singletonSuicide), "singleton suicide cannot be played");
            Expect(game.moveCount == moveCountBefore && game.revision == revisionBefore, "rejected suicide is side-effect free");

            searchObject = new GameObject("GoRulesSuperkoRegressionSearch");
            PureUdonGo.GoSearchState search = searchObject.AddComponent<PureUdonGo.GoSearchState>();
            search.CopyFromGame(game);
            Expect(!search.IsLegalMove(singletonSuicide), "search singleton suicide is illegal");
            Expect(!search.IsSuperkoBanned(singletonSuicide), "search singleton suicide is not superko-banned");

            game.Pass();
            game.Pass();
            Expect(game.gameState != PureUdonGo.GoGame.STATE_PLAYING, "double pass reaches terminal state");
            int terminalBanned = 0;
            for (int loc = 0; loc < PureUdonGo.GoGame.AREA; loc++)
                if (game.IsSuperkoBanned(loc)) terminalBanned++;
            Expect(terminalBanned == 0, "terminal state has no superko mask actions");
            search.CopyFromGame(game);
            int terminalSearchBanned = 0;
            for (int loc = 0; loc < PureUdonGo.GoGame.AREA; loc++)
                if (search.IsSuperkoBanned(loc)) terminalSearchBanned++;
            Expect(terminalSearchBanned == 0, "terminal search state has no superko mask actions");

            Debug.Log("PURE_UDON_GO_RULES_SUPERKO_PASS failures=" + failures +
                " singletonSuicide=" + singletonSuicide + " terminalBanned=" + terminalBanned);
            EditorApplication.Exit(failures == 0 ? 0 : 7);
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_RULES_SUPERKO_FAIL exception=" + exception);
            EditorApplication.Exit(7);
        }
        finally
        {
            if (searchObject != null) UnityEngine.Object.DestroyImmediate(searchObject);
            if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            Networking.LocalPlayer = null;
            Networking.LocalPlayerOwnsObjects = true;
            Networking.IsMaster = true;
        }
    }

    private static void Expect(bool condition, string label)
    {
        if (!condition)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_RULES_SUPERKO_ASSERT_FAIL " + label);
        }
    }

    private static int ParseCoordinate(string value)
    {
        const string columns = "ABCDEFGHJKLMNOPQRST";
        int x = columns.IndexOf(value[0]);
        int row = int.Parse(value.Substring(1));
        return x + (PureUdonGo.GoGame.SIZE - row) * PureUdonGo.GoGame.SIZE;
    }
}
#endif
